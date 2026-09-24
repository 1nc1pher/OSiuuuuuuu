"""
tests/test_dsp_trace.py

Test strategy for dsp.json (GENERATION_REDESIGN_PLAN.md step 1).

This file carries the signals underneath the pipeline's decisions, so
the tests are about three promises:

  1. ONE TIME AXIS. Every curve has exactly as many samples as the
     spectrogram has frames, so frame i means the same moment in all of
     them. The visualization draws a flux trace under a spectrogram
     image and expects a peak to line up with the streak that caused
     it; an off-by-one here is a silent, permanent misalignment.

  2. THE CURVES ARE THE REAL ONES. Each is asserted against the very
     function the pipeline runs, not against a plausible shape. A
     "spectral flux" curve that is merely flux-like would make the whole
     visualization a lie told convincingly.

  3. THE PEAK-PICKER REPRODUCES. The client re-runs onset detection over
     the exported flux and median so the detector's sensitivity can be
     explored live. If picking peaks from the packed curves does not
     recover exactly the onsets the backend found, that feature is
     misinformation rather than a demonstration -- so it is asserted
     here, at every difficulty preset, before any of it is built.

Plus a size check, because this is the file with the arrays in it.

Run with:
    pytest tests/test_dsp_trace.py -v
"""

import json
import os
import sys

import numpy as np
import pytest

sys.path.append(os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "src"))

from audio.loader import AudioTrack, generate_click_track
from audio.visualize import HOP_LENGTH, N_FFT, compute_mel_spectrogram
from mapping.difficulty import PRESETS, TIER_ORDER, build_map_detailed
from mapping.object_classifier import classify
from onset.beat_tracker import (
    TEMPO_MAX_BPM, TEMPO_MIN_BPM, TEMPO_PRIOR_BPM, _bpm_to_lag, _lag_to_bpm,
    onset_envelope, track_beats,
)
from onset.detector import (
    ADAPTIVE_MEDIAN_WINDOW_SEC, BAND_EDGES_HZ, MIN_ONSET_SPACING_SEC,
    detect_onsets, local_median, normalize, pick_peaks, spectral_flux,
)
from export.curve_pack import levels, unpack
from export.dsp_trace import (
    CURVE_ENCODING, CURVE_NAMES, DSP_FILENAME, DSP_VERSION, _align,
    bands_record, build_curves, build_dsp, detection_record, export_dsp,
    frames_record, rms_envelope, tempo_trace,
)


# ------------------------------------------------------------------
# Fixtures
# ------------------------------------------------------------------

@pytest.fixture(scope="module")
def track():
    """
    A synthetic track built to exercise the pipeline rather than to
    sound like anything.

    `generate_click_track` is not enough here, and the reason is worth
    recording: it is one identical hit on every beat, over silence. That
    gives a flux curve with no near-ties, a local median of flat zero,
    one snap division, and no gap long enough to classify -- so a test
    suite built on it cannot tell a working detector from a broken one,
    and the encoding bug found in step 0 would not have shown up at all.

    This track has, deliberately:

      * THREE LOUDNESS POPULATIONS, measured rather than guessed, so the
        difficulty presets genuinely disagree about what is there:
        beats at ~0.27 normalised flux and eighths at ~0.17 (above every
        tier's threshold), and ghost notes at ~0.046 -- above Expert's
        0.030 and below Easy's 0.080, so only the loosest tiers hear
        them at all. That separation is what makes the funnel narrow by
        different amounts per tier.
      * OFF-GRID SUBDIVISIONS (eighths and sixteenths), so snapping has
        something to do and the snap histogram has more than one key.
      * THREE QUIET WINDOWS with different energy behind them: a ~2-beat
        gap with a pad holding through it (a held-note slider), a
        ~9-beat gap with a pad (a spinner), and a long gap where a
        struck note decays into silence (which must stay circles -- the
        near-miss the sustain panel exists to show).
      * BROADBAND NOISE, which lifts the local median off zero so the
        adaptive threshold's multiplicative term actually does something.

    The amplitudes are calibrated against the detector's real
    thresholds. If the detector's constants move far enough that the
    populations stop separating, several tests here fail together --
    which is the correct outcome, not a nuisance: it means the tiers
    have stopped differing in the way the funnel claims they do.
    """
    sr, bpm, duration = 22050, 128.0, 24.0
    period = 60.0 / bpm
    n = int(duration * sr)

    y = np.zeros(n)
    rng = np.random.default_rng(seed=20260920)

    def hit(at, amp, freq, decay=60.0, length=0.05):
        start = int(at * sr)
        if start < 0 or start >= n:
            return
        k = min(int(length * sr), n - start)
        tt = np.arange(k) / sr
        y[start:start + k] += amp * np.sin(2 * np.pi * freq * tt) * np.exp(-tt * decay)

    def pad(t0, t1, amp, freq):
        a, b = int(t0 * sr), int(t1 * sr)
        tt = np.arange(b - a) / sr
        y[a:b] += amp * np.sin(2 * np.pi * freq * tt)

    def quiet(at):
        return (6.05 < at < 6.95          # ~2 beats, pad holding  -> slider
                or 9.8 < at < 14.2        # ~9 beats, pad holding  -> spinner
                or 16.6 < at < 21.4)      # long, decaying to none -> circles

    beat, t = 0, 0.0
    while t < duration:
        if not quiet(t):
            hit(t, 1.0 if beat % 4 == 0 else 0.85, 1400 if beat % 4 == 0 else 900)

            if beat % 2 == 1:
                hit(t + period / 2, 0.85, 2000)

            if beat % 4 in (1, 3):
                hit(t + period / 4, [0.45, 0.52, 0.60][beat % 3], 2600)

        beat += 1
        t = beat * period

    pad(6.0, 7.0, 0.50, 330)
    pad(10.0, 14.0, 0.45, 220)

    # Struck notes that decay to nothing well before the next hit: long
    # clean gaps with no sustain behind them.
    hit(17.0, 0.9, 700, decay=14, length=0.5)
    hit(20.0, 0.9, 700, decay=14, length=0.5)

    y += rng.normal(0, 0.035, n)
    y /= np.max(np.abs(y))

    return AudioTrack(y=y.astype(np.float32), sr=sr, duration=duration,
                      path="<synthetic>", name="calibrated_test_track")


@pytest.fixture(scope="module")
def mel(track):
    return compute_mel_spectrogram(track)


@pytest.fixture(scope="module")
def grid(track):
    return track_beats(track)


@pytest.fixture(scope="module")
def document(track, mel, grid):
    return build_dsp(track, mel, presets=PRESETS, grid=grid)


@pytest.fixture(scope="module")
def tempo(document):
    return document["tempo"]


# ------------------------------------------------------------------
# 1. One time axis
# ------------------------------------------------------------------

def test_every_curve_has_the_spectrograms_frame_count(document, mel):
    expected = mel.shape[1]

    assert document["frames"]["count"] == expected

    for name in CURVE_NAMES:
        assert unpack(document["curves"][name]).size == expected, name


def test_every_named_curve_is_present(document):
    assert sorted(document["curves"]) == sorted(CURVE_NAMES)


def test_align_trims_a_long_curve():
    assert _align(np.arange(10.0), 4).tolist() == [0, 1, 2, 3]


def test_align_pads_a_short_curve_by_repeating_its_edge():
    # Repeating the edge rather than padding with zeros: a curve that
    # ends a frame early should hold its last value, not drop to silence
    # for one frame at the very end of the song.
    assert _align(np.array([1.0, 2.0]), 4).tolist() == [1, 2, 2, 2]


def test_align_pads_an_empty_curve_with_silence():
    # np.pad's edge mode needs an edge to copy, and an empty curve has
    # none -- this is the case that would otherwise raise.
    assert _align(np.array([]), 3).tolist() == [0, 0, 0]


def test_align_leaves_a_matching_curve_untouched():
    curve = np.arange(5.0)

    assert _align(curve, 5) is not None
    assert np.array_equal(_align(curve, 5), curve)


# ------------------------------------------------------------------
# 2. The curves are the real ones
# ------------------------------------------------------------------

def test_flux_is_the_detectors_own_flux(track, mel, document):
    expected = normalize(spectral_flux(mel))

    restored = unpack(document["curves"]["flux"])

    assert np.allclose(restored, expected, atol=1e-6)


def test_median_is_the_detectors_own_local_median(track, mel, document):
    expected = local_median(normalize(spectral_flux(mel)), track.sr)

    restored = unpack(document["curves"]["median"])

    assert np.allclose(restored, expected, atol=expected.max() / levels("uint16"))


def test_envelope_is_the_signal_the_tempo_search_autocorrelated(track, mel, document):
    # Not "a smoothed flux" -- the exact array track_beats() ran over,
    # produced by the same function.
    expected = onset_envelope(track, mel_db=mel)

    restored = unpack(document["curves"]["envelope"])

    assert np.allclose(restored, expected, atol=expected.max() / levels("uint8"))


def test_rms_is_the_signal_sustain_ratio_measures(track, document):
    expected = normalize(_align(rms_envelope(track), document["frames"]["count"]))

    restored = unpack(document["curves"]["rms"])

    assert np.allclose(restored, expected, atol=1.0 / levels("uint8"))


def test_the_three_band_curves_differ_from_each_other(document):
    # A band split that returned the same curve three times would pass
    # every shape assertion above and be useless.
    low = unpack(document["curves"]["bandLow"])
    mid = unpack(document["curves"]["bandMid"])
    high = unpack(document["curves"]["bandHigh"])

    assert not np.allclose(low, mid)
    assert not np.allclose(mid, high)


def test_every_curve_is_non_negative(document):
    for name in CURVE_NAMES:
        assert unpack(document["curves"][name]).min() >= 0, name


# ------------------------------------------------------------------
# 3. The peak-picker reproduces -- the promise the live knobs rest on
# ------------------------------------------------------------------

@pytest.mark.parametrize("tier", TIER_ORDER)
def test_picking_peaks_from_the_packed_curves_reproduces_the_backend(track, document, tier):
    """
    Re-derive the onsets from what dsp.json actually carries, exactly as
    the client will: decode flux and median, rebuild this tier's
    threshold as `delta + margin * median`, and pick.

    If this drifts, the analyser's margin/delta knobs are showing
    numbers the backend would never produce, and the honest fix is the
    export -- not a looser assertion here.
    """
    preset = PRESETS[tier]

    flux = unpack(document["curves"]["flux"])
    median = unpack(document["curves"]["median"])
    threshold = preset.onset_delta + median * preset.onset_margin

    from_packed = pick_peaks(flux, threshold, track.sr, hop_length=HOP_LENGTH)
    from_backend = [o.frame for o in detect_onsets(
        track, margin=preset.onset_margin, delta=preset.onset_delta)]

    assert list(from_packed) == list(from_backend)


def test_the_threshold_is_derivable_rather_than_stored(document):
    # The point of exporting the median instead of five baked threshold
    # curves: any margin/delta at all, including ones no preset uses.
    median = unpack(document["curves"]["median"])

    strict = 0.2 + median * 3.0
    loose = 0.0 + median * 0.5

    assert np.all(strict > loose)


# ------------------------------------------------------------------
# 4. Header
# ------------------------------------------------------------------

def test_the_document_declares_its_version(document):
    assert document["version"] == DSP_VERSION


def test_frames_record_carries_what_a_time_axis_needs(track, mel):
    record = frames_record(track, mel.shape[1])

    assert record["sampleRate"] == track.sr
    assert record["hopLength"] == HOP_LENGTH
    assert record["nFft"] == N_FFT
    assert record["nMels"] == 128
    assert record["count"] == mel.shape[1]
    assert record["frameRate"] == pytest.approx(track.sr / HOP_LENGTH, abs=1e-5)


def test_the_frame_rate_converts_frames_to_seconds(track, document):
    # The one arithmetic the client does with this header constantly.
    rate = document["frames"]["frameRate"]
    last = document["frames"]["count"] - 1

    assert last / rate == pytest.approx(track.duration, rel=0.02)


def test_bands_record_matches_the_detectors_band_split():
    record = bands_record()

    assert sorted(record) == sorted(BAND_EDGES_HZ)

    for name, (lo, hi) in BAND_EDGES_HZ.items():
        assert record[name] == [lo, hi]


def test_detection_record_carries_every_tiers_sensitivity():
    record = detection_record(PRESETS)

    assert record["medianWindowSec"] == ADAPTIVE_MEDIAN_WINDOW_SEC
    assert record["minSpacingSec"] == MIN_ONSET_SPACING_SEC

    for tier in TIER_ORDER:
        assert record["tiers"][tier]["margin"] == PRESETS[tier].onset_margin
        assert record["tiers"][tier]["delta"] == PRESETS[tier].onset_delta


def test_detection_record_without_presets_is_still_valid():
    # Nothing downstream should crash on a document written by a caller
    # that didn't have the presets to hand.
    assert detection_record()["tiers"] == {}


def test_curves_are_written_at_the_encoding_the_table_names(document):
    for name in CURVE_NAMES:
        assert document["curves"][name]["encoding"] == CURVE_ENCODING[name], name


def test_flux_is_float32_and_the_drawn_curves_are_not(document):
    # The reason the file is the size it is, asserted rather than left as
    # a comment: exactness where an algorithm re-runs, drawing precision
    # everywhere else.
    assert document["curves"]["flux"]["encoding"] == "float32"
    assert document["curves"]["bandLow"]["encoding"] == "uint8"


# ------------------------------------------------------------------
# 5. Writing it out
# ------------------------------------------------------------------

def test_export_writes_a_file_that_reads_back(tmp_path, track, mel):
    path = export_dsp(str(tmp_path), track, mel, presets=PRESETS)

    assert os.path.basename(path) == DSP_FILENAME

    with open(path, encoding="utf-8") as f:
        reloaded = json.load(f)

    assert reloaded["version"] == DSP_VERSION
    assert unpack(reloaded["curves"]["flux"]).size == mel.shape[1]


def test_export_creates_the_folder_if_it_is_missing(tmp_path, track, mel):
    folder = str(tmp_path / "does" / "not" / "exist")

    assert os.path.exists(export_dsp(folder, track, mel))


def test_a_silent_track_exports_without_crashing(tmp_path):
    # Not hypothetical: a leading silent passage, a failed decode, or a
    # very short upload all land here, and every curve's normalisation
    # divides by a maximum that is zero.
    silent = AudioTrack(y=np.zeros(22050 * 3, dtype=np.float32), sr=22050,
                        duration=3.0, path="<silent>", name="silence")

    document = build_dsp(silent, compute_mel_spectrogram(silent), presets=PRESETS)

    for name in CURVE_NAMES:
        restored = unpack(document["curves"][name])

        assert restored.size == document["frames"]["count"]
        assert np.all(np.isfinite(restored))


# ------------------------------------------------------------------
# 6. Size -- this is the file with the arrays in it
# ------------------------------------------------------------------

def test_a_six_minute_track_stays_well_under_the_ceiling():
    """
    The analysis export keeps a size ceiling for exactly this reason and
    it is why that file stayed sane.

    400 KB is roomy on purpose: a six-minute track measures around
    230 KB, and the spectrogram.png written into the same folder is
    already 500 KB. The ceiling exists to catch a change that makes the
    file grow by a multiple -- a curve added per tier, say, or an
    encoding widened without noticing -- not to shave kilobytes.
    """
    long_track = generate_click_track(bpm=175.0, duration_sec=360.0)

    document = build_dsp(long_track, compute_mel_spectrogram(long_track),
                         presets=PRESETS)

    size_kb = len(json.dumps(document, separators=(",", ":"))) / 1024

    assert size_kb < 400, f"dsp.json grew to {size_kb:.0f} KB"


# ------------------------------------------------------------------
# 7. The tempo search (step 2) -- Step 3's working, not its answer
# ------------------------------------------------------------------

def test_the_tempo_block_is_absent_without_a_grid(track, mel):
    # Same degradation rule as a missing file: the stage simply isn't
    # available, rather than the document being invalid.
    assert "tempo" not in build_dsp(track, mel, presets=PRESETS)


def test_the_chosen_tempo_is_the_grids_tempo(tempo, grid):
    assert tempo["chosenBpm"] == pytest.approx(grid.bpm, abs=0.01)


def test_a_click_track_recovers_the_tempo_it_was_generated_at():
    clean = generate_click_track(bpm=140.0, duration_sec=20.0)

    document = build_dsp(clean, compute_mel_spectrogram(clean),
                         grid=track_beats(clean))

    assert document["tempo"]["chosenBpm"] == pytest.approx(140.0, abs=1.0)


def test_the_acf_curves_share_one_lag_axis(tempo):
    # bpms is the axis the panel labels; acf, prior and weighted are the
    # three things drawn against it. A length mismatch would misalign
    # the peak marker from the peak.
    width = len(tempo["bpms"])

    assert width > 0
    assert len(tempo["acf"]) == width
    assert len(tempo["prior"]) == width
    assert len(tempo["weighted"]) == width
    assert tempo["lagMax"] - tempo["lagMin"] + 1 == width


def test_weighted_is_the_product_the_argmax_is_taken_over(tempo):
    # Derivable from acf and prior, and written anyway because it is the
    # curve the peak marker sits on. If it ever stops being their
    # product, the marker is pointing at the wrong thing.
    product = np.array(tempo["acf"]) * np.array(tempo["prior"])

    assert np.allclose(product, tempo["weighted"], atol=1e-4)


def test_the_drawn_lag_range_contains_the_searched_one(tempo):
    # The search only looks at 50-210 BPM; the drawn range reaches an
    # octave past each edge so the 1x/2x/half peaks are all in frame.
    assert tempo["lagMin"] <= tempo["searchLagMin"]
    assert tempo["lagMax"] >= tempo["searchLagMax"]
    assert tempo["lagMin"] < tempo["lagMax"]


def test_the_search_band_is_the_tempo_range_it_claims(track, tempo):
    # searchLagMin comes from the *fastest* tempo, because a shorter lag
    # is a faster tempo -- an inversion that is easy to get backwards.
    fast = _lag_to_bpm(tempo["searchLagMin"], track.sr, HOP_LENGTH)
    slow = _lag_to_bpm(tempo["searchLagMax"], track.sr, HOP_LENGTH)

    assert slow < fast
    assert fast == pytest.approx(TEMPO_MAX_BPM, rel=0.15)
    assert slow == pytest.approx(TEMPO_MIN_BPM, rel=0.15)


def test_the_bpm_axis_falls_as_the_lag_rises(track, tempo):
    bpms = np.array(tempo["bpms"])

    assert np.all(np.diff(bpms) < 0)
    assert bpms[0] == pytest.approx(
        _lag_to_bpm(tempo["lagMin"], track.sr, HOP_LENGTH), rel=1e-3)


def test_the_raw_tempo_is_the_peak_of_the_weighted_curve_in_the_search_band(track, tempo):
    # estimate_tempo() takes the argmax of acf x prior over the search
    # band and then refines it parabolically, so the exported rawBpm
    # should land on the integer lag that argmax picked.
    lags = np.arange(tempo["lagMin"], tempo["lagMax"] + 1)
    inside = (lags >= tempo["searchLagMin"]) & (lags <= tempo["searchLagMax"])

    weighted = np.array(tempo["weighted"])
    best_lag = int(lags[inside][int(np.argmax(weighted[inside]))])

    assert abs(_bpm_to_lag(tempo["rawBpm"], track.sr, HOP_LENGTH) - best_lag) <= 1


def test_the_autocorrelation_is_normalised(tempo):
    # Lag 0 is 1.0 by construction and isn't exported, so what is
    # assertable is that nothing exported exceeds it.
    assert np.all(np.abs(np.array(tempo["acf"])) <= 1.0 + 1e-6)


def test_the_prior_peaks_at_the_prior_tempo(tempo):
    bpms = np.array(tempo["bpms"])
    prior = np.array(tempo["prior"])

    assert prior.max() <= 1.0 + 1e-6
    assert bpms[int(np.argmax(prior))] == pytest.approx(TEMPO_PRIOR_BPM, rel=0.1)


# --- the octave stage ---------------------------------------------

def test_there_are_three_octave_candidates_at_half_one_and_double(tempo):
    assert [c["factor"] for c in tempo["octaveCandidates"]] == [0.5, 1.0, 2.0]


def test_each_candidate_is_its_factor_times_the_raw_estimate(tempo):
    for candidate in tempo["octaveCandidates"]:
        assert candidate["bpm"] == pytest.approx(
            tempo["rawBpm"] * candidate["factor"], abs=0.02)


def test_exactly_one_candidate_is_marked_chosen(tempo):
    assert sum(c["chosen"] for c in tempo["octaveCandidates"]) == 1


def test_the_chosen_candidate_is_the_tempo_the_map_was_built_on(tempo):
    chosen = next(c for c in tempo["octaveCandidates"] if c["chosen"])

    assert chosen["bpm"] == pytest.approx(tempo["chosenBpm"], abs=0.02)


def test_a_candidate_outside_the_search_range_is_flagged_and_scoreless(tempo):
    # The real search skips these; they are written anyway so the panel
    # can show them struck out. "Not even considered" is part of the
    # story the stage tells.
    for candidate in tempo["octaveCandidates"]:
        in_range = TEMPO_MIN_BPM <= candidate["bpm"] <= TEMPO_MAX_BPM

        assert candidate["inRange"] == in_range

        if not in_range:
            assert candidate["score"] == 0
            assert not candidate["chosen"]


def test_each_candidates_score_is_its_strength_times_its_prior(tempo):
    # The arithmetic _resolve_octave() scores by, kept visible so the
    # panel can show the multiplication rather than just the winner.
    for candidate in tempo["octaveCandidates"]:
        assert candidate["score"] == pytest.approx(
            candidate["phaseStrength"] * candidate["priorWeight"], abs=1e-3)


def test_the_chosen_candidate_scores_at_least_as_well_as_the_others(tempo):
    scores = [c["score"] for c in tempo["octaveCandidates"] if c["inRange"]]
    chosen = next(c["score"] for c in tempo["octaveCandidates"] if c["chosen"])

    assert chosen == pytest.approx(max(scores), abs=1e-3)


def test_octave_corrected_says_whether_the_answer_actually_moved(tempo):
    moved = abs(tempo["rawBpm"] - tempo["chosenBpm"]) > 0.01

    assert tempo["octaveCorrected"] == moved


# --- the phase fit ------------------------------------------------

def test_there_is_one_phase_score_per_frame_of_a_beat_period(tempo):
    assert len(tempo["phaseScores"]) == int(np.ceil(tempo["periodFrames"]))


def test_the_period_matches_the_chosen_tempo(track, tempo):
    assert tempo["periodFrames"] == pytest.approx(
        _bpm_to_lag(tempo["chosenBpm"], track.sr, HOP_LENGTH), rel=1e-3)


def test_the_phase_offset_is_the_argmax_of_the_phase_scores(tempo):
    assert tempo["phaseOffsetFrames"] == int(np.argmax(tempo["phaseScores"]))


def test_the_phase_offset_agrees_with_the_grids_own_offset(track, tempo, grid):
    # The slide's winning offset, in frames, is where the grid's first
    # beat sits -- the two must not disagree, or the panel shows a lock
    # at a different place than the grid it produced.
    expected = grid.offset * track.sr / HOP_LENGTH

    assert tempo["phaseOffsetFrames"] == pytest.approx(expected, abs=1)


def test_the_phase_strength_beats_an_average_placement(tempo):
    # > 1 means beats land on louder-than-average moments, which is the
    # whole claim the number makes.
    assert tempo["phaseStrength"] > 1.0


def test_confidence_and_offset_come_straight_off_the_grid(tempo, grid):
    assert tempo["confidence"] == pytest.approx(grid.confidence, abs=1e-3)
    assert tempo["offset"] == pytest.approx(grid.offset, abs=1e-3)


def test_the_tempo_block_does_not_grow_with_the_track():
    """
    The block is bounded by the lag range it draws and by one beat
    period -- about 100 autocorrelation values and 40 phase scores --
    and by nothing else. A four-minute track must cost the same as a
    twenty-second one.

    Asserted as "does it grow", not as a fraction of the file: the
    curves scale with duration and this does not, so a percentage would
    mean something different at every length. Growth here would mean
    something per-frame had leaked into the block by accident.
    """
    def block_bytes(seconds):
        clean = generate_click_track(bpm=128.0, duration_sec=seconds)
        document = build_dsp(clean, compute_mel_spectrogram(clean),
                             grid=track_beats(clean))

        return len(json.dumps(document["tempo"], separators=(",", ":")))

    short = block_bytes(20.0)
    long = block_bytes(200.0)

    assert long == pytest.approx(short, rel=0.15)
    assert long < 8 * 1024


def test_a_silent_track_produces_a_tempo_block_without_crashing():
    silent = AudioTrack(y=np.zeros(22050 * 5, dtype=np.float32), sr=22050,
                        duration=5.0, path="<silent>", name="silence")

    tempo = build_dsp(silent, compute_mel_spectrogram(silent),
                      grid=track_beats(silent))["tempo"]

    assert len(tempo["acf"]) == len(tempo["bpms"])
    assert np.all(np.isfinite(np.array(tempo["acf"], dtype=float)))
    assert len(tempo["phaseScores"]) >= 1


# ------------------------------------------------------------------
# 8. Snapping, sustain and the tier funnel (step 3) -- Steps 4 and 5
# ------------------------------------------------------------------

@pytest.fixture(scope="module")
def tier_data(track, grid):
    """
    A real two-tier run: onsets detected at each preset's own
    sensitivity, classified and placed, exactly as main.py does it.

    Two tiers rather than five because the assertions are about the
    shape of a tier's record, and Easy/Expert are the two extremes --
    the one that thins hardest and the one that barely thins at all.
    """
    onsets, objects = {}, {}

    for name in ("Easy", "Expert"):
        detected, placed, _ = build_map_detailed(track, PRESETS[name])
        onsets[name] = detected
        objects[name] = placed

    return onsets, objects


@pytest.fixture(scope="module")
def tiers(track, mel, grid, tier_data):
    onsets, objects = tier_data

    return build_dsp(track, mel, presets=PRESETS, grid=grid,
                     tier_onsets=onsets, tier_objects=objects,
                     onset_source="Expert")["tiers"]


def test_the_tiers_block_is_absent_without_per_tier_data(track, mel, grid):
    document = build_dsp(track, mel, presets=PRESETS, grid=grid)

    assert "tiers" not in document


def test_the_tiers_block_is_absent_without_a_grid(track, mel, tier_data):
    onsets, objects = tier_data

    document = build_dsp(track, mel, presets=PRESETS, tier_onsets=onsets,
                         tier_objects=objects)

    assert "tiers" not in document


def test_a_tier_with_no_preset_is_skipped_rather_than_half_written(track, mel, grid, tier_data):
    onsets, objects = tier_data

    document = build_dsp(track, mel, presets={"Easy": PRESETS["Easy"]},
                         grid=grid, tier_onsets=onsets, tier_objects=objects)

    assert sorted(document["tiers"]) == ["Easy"]


# --- the funnel ---------------------------------------------------

def test_every_tiers_funnel_narrows(tiers):
    # detected -> snapped -> afterSpacing -> objects, each step only
    # ever removing. A funnel that widened would mean a stage was
    # inventing notes.
    for name, tier in tiers.items():
        funnel = tier["funnel"]

        assert funnel["detected"] >= funnel["snapped"], name
        assert funnel["snapped"] >= funnel["afterSpacing"], name
        assert funnel["objects"] <= funnel["afterSpacing"], name


def test_the_easier_tier_detects_fewer_onsets(tiers):
    # A higher margin and delta is a stricter detector. If this ever
    # inverts, the funnel panel would be telling the opposite story
    # from the presets printed beside it.
    assert tiers["Easy"]["funnel"]["detected"] < tiers["Expert"]["funnel"]["detected"]


def test_the_easier_tier_keeps_far_fewer_after_spacing(tiers):
    # Easy thins at 2 beats and Expert at a quarter of one: this is the
    # step where the tiers really separate, and it is what the funnel
    # panel exists to show.
    assert tiers["Easy"]["funnel"]["afterSpacing"] < tiers["Expert"]["funnel"]["afterSpacing"]


def test_the_object_count_matches_the_objects_that_were_placed(tiers, tier_data):
    _, objects = tier_data

    for name, tier in tiers.items():
        assert tier["funnel"]["objects"] == len(objects[name]), name


def test_the_kind_histogram_accounts_for_every_object(tiers):
    for name, tier in tiers.items():
        assert sum(tier["kinds"].values()) == tier["funnel"]["objects"], name


def test_the_snap_histogram_accounts_for_every_object(tiers):
    for name, tier in tiers.items():
        assert sum(tier["snapHistogram"].values()) == tier["funnel"]["objects"], name


def test_a_whole_beat_tier_only_ever_snaps_to_whole_beats(tiers):
    # Easy runs at snap_division 1, so every object must sit on a 1/1.
    assert PRESETS["Easy"].snap_division == 1
    assert list(tiers["Easy"]["snapHistogram"]) == ["1/1"]


def test_a_quarter_beat_tier_uses_more_than_whole_beats(tiers):
    assert PRESETS["Expert"].snap_division == 4
    assert len(tiers["Expert"]["snapHistogram"]) > 1


# --- snapping -----------------------------------------------------

def test_there_is_one_snap_delta_per_surviving_onset(tiers):
    for name, tier in tiers.items():
        assert len(tier["snapDeltasMs"]) == tier["funnel"]["afterSpacing"], name


def test_no_onset_moves_further_than_half_a_snap_division(tiers, grid):
    # Quantising to the nearest slot can never move anything more than
    # half a slot. A delta past that would mean the snap had gone to the
    # wrong slot entirely.
    for name, tier in tiers.items():
        step_ms = grid.beat_period / PRESETS[name].snap_division * 1000
        limit = step_ms / 2 + 1e-6

        assert max((abs(d) for d in tier["snapDeltasMs"]), default=0) <= limit, name


def test_snap_deltas_go_both_ways(tiers):
    # Onsets land on both sides of the grid line they snap to. All-positive
    # deltas would mean the raw time was being read as the snapped one.
    deltas = tiers["Expert"]["snapDeltasMs"]

    assert any(d > 0 for d in deltas)
    assert any(d < 0 for d in deltas)


def test_merged_counts_the_onsets_that_lost_a_slot(tiers):
    for name, tier in tiers.items():
        funnel = tier["funnel"]

        # Everything detected either keeps a slot, loses one to a
        # stronger neighbour, or falls before the first beat.
        assert tier["merged"] <= funnel["detected"] - funnel["snapped"], name


def test_tracing_a_classification_does_not_change_it(track, grid):
    # The decision log must be a read-only observer. If attaching it
    # changed an outcome, every number in the trace would be describing
    # a map that wasn't the one written.
    onsets = detect_onsets(track)

    plain = classify(onsets, grid, track, snap_division=4)
    traced = classify(onsets, grid, track, snap_division=4, trace=[])

    assert [(o.kind, o.time, o.end_time) for o in plain] == \
           [(o.kind, o.time, o.end_time) for o in traced]


# --- the sustain trace --------------------------------------------

def test_only_the_onset_source_tier_carries_a_sustain_trace(tiers):
    # Five traces is five times the bytes for a panel that shows one
    # tier, and the source tier is the one analysis.json's own onsets
    # came from, so the two files agree about which pass is on screen.
    assert "sustain" in tiers["Expert"]
    assert "sustain" not in tiers["Easy"]


def test_the_sustain_trace_only_keeps_decisions_sustain_could_have_changed(tiers):
    # A circle after a 1/4 gap never consulted sustain in any meaningful
    # sense -- the gap test ruled a slider out first -- and there are
    # thousands of those.
    threshold = min(PRESETS["Expert"].slider_min_beats,
                    PRESETS["Expert"].spinner_min_beats)

    for record in tiers["Expert"]["sustain"]:
        assert record["became"] != "circle" or record["gapBeats"] >= threshold


def test_every_non_circle_decision_is_in_the_trace(tiers, tier_data):
    # The interesting outcomes are never filtered out, however short
    # their gap.
    _, objects = tier_data

    traced = sum(1 for r in tiers["Expert"]["sustain"] if r["became"] != "circle")
    placed = sum(1 for o in objects["Expert"] if o.kind != "circle")

    assert traced == placed


def test_a_held_slider_needed_sustained_energy(tiers):
    threshold = PRESETS["Expert"].sustain_threshold

    for record in tiers["Expert"]["sustain"]:
        if record["became"] in ("heldSlider", "spinner"):
            assert record["sustain"] >= threshold
            assert record["clean"]


def test_a_spinner_needed_a_longer_gap_than_a_slider(tiers):
    preset = PRESETS["Expert"]

    for record in tiers["Expert"]["sustain"]:
        if record["became"] == "spinner":
            assert record["gapBeats"] >= preset.spinner_min_beats
        elif record["became"] == "heldSlider":
            assert record["gapBeats"] >= preset.slider_min_beats


def test_the_sustain_trace_records_near_misses_too(tiers):
    # The point of the panel: a gap long enough for a slider that stayed
    # a circle because the sound had already decayed. Without these it
    # only ever shows the rule succeeding, never the test doing work.
    near_misses = [r for r in tiers["Expert"]["sustain"] if r["became"] == "circle"]

    assert near_misses
    assert all(r["sustain"] < PRESETS["Expert"].sustain_threshold or not r["clean"]
               for r in near_misses)


# --- the preset knobs ---------------------------------------------

def test_every_preset_knob_reaches_the_file(tiers):
    expected = {
        "stars", "margin", "delta", "snapDivision", "minSpacingBeats",
        "circleSize", "approachRate", "overallDifficulty", "hpDrain",
        "sliderMultiplier", "distanceSpacing", "maxTurnDegrees",
        "streamMinLen", "spinnerMinBeats", "sliderMinBeats",
        "sliderMaxBeats", "sustainThreshold",
    }

    for name, tier in tiers.items():
        assert set(tier["preset"]) == expected, name


def test_the_preset_knobs_are_the_presets_own_values(tiers):
    for name, tier in tiers.items():
        preset = PRESETS[name]
        record = tier["preset"]

        assert record["margin"] == preset.onset_margin
        assert record["delta"] == preset.onset_delta
        assert record["snapDivision"] == preset.snap_division
        assert record["minSpacingBeats"] == preset.min_spacing_beats
        assert record["streamMinLen"] == preset.stream_min_len
        assert record["circleSize"] == preset.circle_size


def test_the_detection_block_and_the_tier_presets_agree(document, tiers):
    # dsp.json states each tier's sensitivity twice -- once in
    # `detection.tiers`, which the peak-picker reads, and once in the
    # funnel's preset label. They must not drift apart.
    for name, tier in tiers.items():
        if name in document["detection"]["tiers"]:
            shared = document["detection"]["tiers"][name]

            assert shared["margin"] == tier["preset"]["margin"]
            assert shared["delta"] == tier["preset"]["delta"]
