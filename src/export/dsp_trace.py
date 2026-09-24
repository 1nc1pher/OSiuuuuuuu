"""
src/export/dsp_trace.py

A third export artifact: dsp.json, what the DSP was *looking at* while it
worked.

analysis_export.py already writes what the pipeline decided -- the
onsets it picked, the grid it settled on, the objects it placed. This
file writes the signals underneath those decisions: the spectral flux
the peak-picker ran over, the local median its threshold was built from,
the onset envelope the tempo search autocorrelated, the RMS envelope the
slider/spinner typing measured sustain against, and flux split into the
three frequency bands that decide which band an onset belongs to.

All of it exists in memory during a normal run and none of it survives
one. Without it the visualization can show four results; with it, it can
show the four algorithms that produced them.

WHY A SECOND FILE RATHER THAN A VERSION BUMP
  analysis.json is at version 1 and every beatmap generated so far
  carries one. The client's AnalysisData gates on an exact version
  match, so a v2 would make every existing map unreadable -- and the
  analysis viewer is reachable from song select for maps generated long
  before this file existed. A sibling file is purely additive: no
  dsp.json simply means the deeper stages aren't available for that map,
  the same way no analysis.json means no visualization at all.

WHAT IT COSTS
  Nothing is recomputed from the audio. This module is handed the mel
  spectrogram export_analysis() already has, and everything except the
  RMS envelope is derived from it. RMS is one librosa call. There is no
  second decode and no second mel pass.

FRAME ALIGNMENT
  Every curve here shares one time axis: frame i of any of them is
  frame i of the spectrogram, at t = i * hopLength / sampleRate. That
  is what lets the client draw a flux trace under a spectrogram image
  and have a peak line up with the streak that caused it. The RMS
  envelope comes from a separate librosa call and so is the one curve
  that could come back a frame short or long; _align() is what keeps
  the promise.
"""

import json
import os

import numpy as np
import librosa

from audio.visualize import HOP_LENGTH, N_FFT
from onset.beat_tracker import (
    TEMPO_MAX_BPM, TEMPO_MIN_BPM, TEMPO_PRIOR_BPM, TEMPO_PRIOR_WIDTH_OCTAVES,
    _autocorrelation, _bpm_to_lag, _lag_to_bpm, _phase_scores,
    estimate_beat_phase, estimate_tempo, onset_envelope, tempo_prior_weight,
)
from mapping.object_classifier import classify, enforce_min_spacing, snap_onsets
from onset.detector import (
    ADAPTIVE_MEDIAN_WINDOW_SEC, BAND_EDGES_HZ, MIN_ONSET_SPACING_SEC,
    band_limited_flux, local_median, normalize, spectral_flux,
)
from export.curve_pack import pack


DSP_FILENAME = "dsp.json"

# Independent of analysis.json's version on purpose -- the two files are
# read separately and either can move without the other.
DSP_VERSION = 1

# Frame length librosa's RMS uses. Matches the STFT window so the RMS
# envelope is measured over the same span of audio that each frame of
# the spectrogram covers.
RMS_FRAME_LENGTH = 2048

# How far past the tempo search band the exported autocorrelation
# reaches, as a factor on the band's edges.
#
# The search itself only ever looks at 50-210 BPM, which at ~43 frames
# per second is lags 12 to 52 -- about 40 numbers. Drawing only those
# crops away exactly what makes the stage worth showing: autocorrelation
# genuinely peaks at the true tempo *and* at its double and half, and
# the octave stage exists to choose between them. Reaching an octave
# past each edge puts all three peaks in frame, and costs about 60 more
# floats.
ACF_OCTAVE_MARGIN = 2.0

# Every curve written, with the wire encoding each is packed at. Named
# here so the tests and the C# reader have one list to agree with.
#
# Flux and median are the two curves the client runs an algorithm over
# rather than merely drawing: it re-runs the onset peak-picker on them
# so the detector's sensitivity can be explored live. That algorithm
# tests `flux[i] > flux[i-1]`, which an encoding that flattens two
# near-equal neighbours turns from a peak into nothing.
#
# Measured against closer / bad_apple / golden at all five presets:
# uint8 flux loses ~6 onsets in 499; uint16 flux still shifts one peak
# by a frame and flips one onset sitting 2e-6 above its threshold;
# float32 flux reproduces the backend exactly in all fifteen
# combinations. The median is a moving median with no single-frame
# spikes to lose, and uint16 is exact beside a float32 flux. The five
# curves that are only ever drawn stay at uint8, already finer than a
# pixel.
CURVE_ENCODING = {
    "flux": "float32",
    "median": "uint16",
    "envelope": "uint8",
    "rms": "uint8",
    "bandLow": "uint8",
    "bandMid": "uint8",
    "bandHigh": "uint8",
}

CURVE_NAMES = ("flux", "median", "envelope", "rms",
               "bandLow", "bandMid", "bandHigh")


def _align(curve: np.ndarray, n_frames: int) -> np.ndarray:
    """
    Force a curve onto the spectrogram's frame count.

    Trims a long curve and edge-pads a short one. Only ever moves things
    by a frame or two -- librosa's RMS and melspectrogram both centre
    their frames and agree in practice, but "in practice" is not the
    same as guaranteed, and a silent off-by-one here would put a curve a
    frame out of step with the image above it.
    """
    curve = np.asarray(curve, dtype=float)

    if curve.size == n_frames:
        return curve

    if curve.size > n_frames:
        return curve[:n_frames]

    pad = n_frames - curve.size

    # np.pad's "edge" mode needs an edge to copy; an empty curve has
    # none, so that case pads with silence instead.
    return np.pad(curve, (0, pad), mode="edge" if curve.size else "constant")


def rms_envelope(track, hop_length: int = HOP_LENGTH,
                 frame_length: int = RMS_FRAME_LENGTH) -> np.ndarray:
    """
    Short-time RMS, one value per frame -- the loudness signal Step 4's
    sustain_ratio() thresholds against to decide whether a gap is a held
    note (slider/spinner) or a rest.
    """
    return librosa.feature.rms(y=track.y, frame_length=frame_length,
                               hop_length=hop_length)[0]


def build_curves(track, mel_db: np.ndarray, hop_length: int = HOP_LENGTH,
                 n_mels: int = 128) -> dict:
    """
    Every per-frame signal the visualization needs, packed and keyed by
    name. All normalised to [0, 1], all on the spectrogram's frame axis.

    Note what is *not* here: the adaptive threshold. It is
    `delta + margin * median`, and margin/delta are per difficulty
    preset, so writing it would mean writing five nearly identical
    curves. The median is preset-independent, so it is written once and
    the client derives whichever threshold it wants from it -- including
    thresholds no preset uses, which is what makes the detector's
    sensitivity explorable rather than merely illustrated.
    """
    n_frames = int(mel_db.shape[1])

    flux = normalize(spectral_flux(mel_db))
    bands = band_limited_flux(mel_db, track.sr, n_mels=n_mels)

    curves = {
        "flux": flux,
        # Computed from the *normalised* flux, because that is what
        # adaptive_threshold() is given: a median of the raw flux would
        # be on a different scale from the curve it has to sit on.
        "median": local_median(flux, track.sr, hop_length=hop_length),
        # The smoothed flux the tempo search actually autocorrelated --
        # recomputed through the same function rather than approximated,
        # and handed the spectrogram so it costs no second pass.
        "envelope": onset_envelope(track, hop_length=hop_length,
                                   n_mels=n_mels, mel_db=mel_db),
        "rms": normalize(_align(rms_envelope(track, hop_length=hop_length),
                                n_frames)),
        "bandLow": normalize(bands["low"]),
        "bandMid": normalize(bands["mid"]),
        "bandHigh": normalize(bands["high"]),
    }

    return {name: pack(_align(curves[name], n_frames),
                        encoding=CURVE_ENCODING[name])
            for name in CURVE_NAMES}


def frames_record(track, n_frames: int, hop_length: int = HOP_LENGTH,
                  n_fft: int = N_FFT, n_mels: int = 128) -> dict:
    """
    The shared time axis, and the STFT settings it came from. Without
    sampleRate and hopLength the client cannot turn a frame index into a
    time, or an autocorrelation lag into a BPM.
    """
    return {
        "sampleRate": int(track.sr),
        "hopLength": int(hop_length),
        "nFft": int(n_fft),
        "nMels": int(n_mels),
        "frameRate": round(float(track.sr) / hop_length, 6),
        "count": int(n_frames),
    }


def bands_record() -> dict:
    """Which frequency range each band curve covers, in Hz."""
    return {name: [float(lo), float(hi)]
            for name, (lo, hi) in BAND_EDGES_HZ.items()}


def detection_record(presets: dict = None) -> dict:
    """
    What the peak-picker was configured with.

    `medianWindowSec` and `minSpacingSec` are global; `margin` and
    `delta` are per difficulty preset, and are written per tier so the
    client can reconstruct any tier's threshold -- and check its own
    ported peak-picker against the onsets the backend recorded.
    """
    record = {
        "medianWindowSec": float(ADAPTIVE_MEDIAN_WINDOW_SEC),
        "minSpacingSec": float(MIN_ONSET_SPACING_SEC),
        "tiers": {},
    }

    for name, preset in (presets or {}).items():
        record["tiers"][name] = {
            "margin": float(preset.onset_margin),
            "delta": float(preset.onset_delta),
        }

    return record


def build_dsp(track, mel_db: np.ndarray, presets: dict = None, grid=None,
              tier_onsets: dict = None, tier_objects: dict = None,
              onset_source: str = "", hop_length: int = HOP_LENGTH,
              n_fft: int = N_FFT, n_mels: int = 128) -> dict:
    """
    Assemble the whole dsp.json document.

    Every block past the curves is optional, and absent rather than
    empty when its inputs weren't supplied:

      `grid`                       -> the `tempo` block (step 2)
      `tier_onsets` + `tier_objects` + `presets` + `grid`
                                   -> the `tiers` block (step 3)

    That is the same degradation rule the whole file follows: a stage
    whose data isn't there simply isn't available for that map, and the
    client says so rather than breaking.
    """
    n_frames = int(mel_db.shape[1])

    curves = build_curves(track, mel_db, hop_length=hop_length, n_mels=n_mels)

    document = {
        "version": DSP_VERSION,
        "frames": frames_record(track, n_frames, hop_length=hop_length,
                                n_fft=n_fft, n_mels=n_mels),
        "bands": bands_record(),
        "detection": detection_record(presets),
        "curves": curves,
    }

    if grid is not None:
        # The envelope the tracker actually ran over. BeatGrid keeps it,
        # so this is the same array rather than a recomputed lookalike --
        # and the one the `envelope` curve above was packed from.
        envelope = grid.onset_env

        if envelope is None:
            envelope = onset_envelope(track, hop_length=hop_length,
                                      n_mels=n_mels, mel_db=mel_db)

        document["tempo"] = tempo_trace(np.asarray(envelope, dtype=float),
                                        track.sr, grid, hop_length=hop_length)

        if tier_onsets and tier_objects:
            document["tiers"] = tiers_record(track, grid, presets, tier_onsets,
                                             tier_objects,
                                             onset_source=onset_source)

    return document


def preset_record(preset) -> dict:
    """
    Every knob this tier was built with.

    Step 5 is the least visible part of the pipeline: five maps come out
    of one song and nothing anywhere records *what was turned* to make
    them different. These are the inputs to that, printed on the tier's
    own label in the funnel panel.
    """
    return {
        "stars": float(preset.stars),
        "margin": float(preset.onset_margin),
        "delta": float(preset.onset_delta),
        "snapDivision": int(preset.snap_division),
        "minSpacingBeats": float(preset.min_spacing_beats),
        "circleSize": float(preset.circle_size),
        "approachRate": float(preset.approach_rate),
        "overallDifficulty": float(preset.overall_difficulty),
        "hpDrain": float(preset.hp_drain),
        "sliderMultiplier": float(preset.slider_multiplier),
        "distanceSpacing": float(preset.distance_spacing),
        "maxTurnDegrees": float(preset.max_turn_degrees),
        "streamMinLen": int(preset.stream_min_len),
        "spinnerMinBeats": float(preset.spinner_min_beats),
        "sliderMinBeats": float(preset.slider_min_beats),
        "sliderMaxBeats": float(preset.slider_max_beats),
        "sustainThreshold": float(preset.sustain_threshold),
    }


def tier_record(preset, onsets, grid, objects) -> dict:
    """
    One tier's journey from detected onsets to placed objects.

    The funnel is the point: `detected` onsets get quantised onto the
    grid (`snapped`, where collisions merge), thinned by the tier's
    minimum spacing (`afterSpacing`), and then classified into
    `objects` -- where a run of onsets can fold into a single slider, so
    this last number can drop sharply on the easier tiers and barely at
    all on the hardest.

    Everything here runs on onsets already in memory. snap_onsets() and
    enforce_min_spacing() are pure functions over them, so no tier is
    re-detected to produce these counts.
    """
    snapped = snap_onsets(onsets, grid, division=preset.snap_division)
    kept = enforce_min_spacing(snapped, grid, preset.min_spacing_beats)

    kinds = {}
    snap_histogram = {}
    for obj in objects:
        kinds[obj.kind] = kinds.get(obj.kind, 0) + 1
        snap_histogram[obj.snap] = snap_histogram.get(obj.snap, 0) + 1

    return {
        "preset": preset_record(preset),
        "funnel": {
            "detected": len(onsets),
            "snapped": len(snapped),
            "afterSpacing": len(kept),
            "objects": len(objects),
        },
        "kinds": kinds,
        "snapHistogram": snap_histogram,
        # How many onsets lost a grid slot to a stronger neighbour. The
        # difference between `detected` and `snapped` is exactly this
        # plus anything landing before the first beat.
        "merged": sum(s.merged for s in snapped),
        # How far each surviving onset had to move to reach the grid,
        # in milliseconds, signed. The snap stage animates these as a
        # slide rather than a cut, so they are the movement itself.
        "snapDeltasMs": [round((s.time - s.raw_time) * 1000, 1) for s in kept],
    }


def sustain_trace(track, onsets, grid, preset) -> list:
    """
    The slider / spinner / rest decisions, with the numbers behind them.

    Re-runs classify() with a trace attached. That is a second pass over
    the classifier for one tier -- not over the audio, and not over
    detection, which are the expensive parts -- and it keeps the
    pipeline's own call sites untouched: nothing in difficulty.py or
    main.py has to learn about tracing to get this.

    Only decisions where sustain could actually have mattered are kept:
    every non-circle outcome, plus the circles whose gap was long enough
    that a slider or spinner was on the table and the sustain test is
    what ruled it out. A circle following a 1/4 gap never consulted
    sustain in any meaningful sense, and there are thousands of those.
    """
    records = []
    classify(onsets, grid, track,
             snap_division=preset.snap_division,
             min_spacing_beats=preset.min_spacing_beats,
             spinner_min_beats=preset.spinner_min_beats,
             slider_min_beats=preset.slider_min_beats,
             slider_max_beats=preset.slider_max_beats,
             stream_min_len=preset.stream_min_len,
             sustain_threshold=preset.sustain_threshold,
             trace=records)

    threshold_gap = min(preset.slider_min_beats, preset.spinner_min_beats)

    return [
        {
            "time": round(r["time"], 3),
            "gapBeats": round(r["gapBeats"], 3),
            "sustain": round(r["sustain"], 3),
            "clean": r["clean"],
            "run": r["run"],
            "became": r["became"],
        }
        for r in records
        if r["became"] != "circle" or r["gapBeats"] >= threshold_gap
    ]


def tiers_record(track, grid, presets: dict, tier_onsets: dict,
                 tier_objects: dict, onset_source: str = "") -> dict:
    """
    Every tier's funnel, plus one tier's classification trace.

    Only `onset_source` gets the trace: five of them is five times the
    bytes for a panel that shows one tier at a time, and that tier is
    the one whose onsets analysis.json already carries, so the two files
    agree about which pass is being looked at.
    """
    record = {}

    for name, objects in tier_objects.items():
        preset = (presets or {}).get(name)
        onsets = (tier_onsets or {}).get(name)

        if preset is None or onsets is None:
            continue

        entry = tier_record(preset, onsets, grid, objects)

        if name == onset_source:
            entry["sustain"] = sustain_trace(track, onsets, grid, preset)

        record[name] = entry

    return record


def tempo_trace(envelope: np.ndarray, sr: int, grid,
                hop_length: int = HOP_LENGTH,
                tempo_min: float = TEMPO_MIN_BPM,
                tempo_max: float = TEMPO_MAX_BPM,
                prior_bpm: float = TEMPO_PRIOR_BPM) -> dict:
    """
    Step 3's search, rather than Step 3's answer.

    analysis.json already carries the result -- bpm, offset, confidence,
    every beat time. What it cannot show is that the tempo was *found*:
    an autocorrelation over the onset envelope, weighted by a log-normal
    prior, then re-scored at half and double speed by how well a pulse
    train at each lands on real onset energy, and finally slid across
    one beat period to find its phase. A BPM that simply appears reads
    like a lookup; this is what makes it read like the estimate it is.

    Everything here is computed with beat_tracker's own functions rather
    than reimplemented -- a second copy of the octave rule is a second
    thing that can be wrong, and the whole point of the panel is that it
    shows what the pipeline did.
    """
    acf = _autocorrelation(envelope)

    # The band the search actually looked at, and the wider band drawn
    # around it so the octave peaks are visible.
    search_lag_min = max(1, int(np.floor(_bpm_to_lag(tempo_max, sr, hop_length))))
    search_lag_max = min(len(acf) - 2,
                          int(np.ceil(_bpm_to_lag(tempo_min, sr, hop_length))))

    draw_lag_min = max(1, int(np.floor(
        _bpm_to_lag(tempo_max * ACF_OCTAVE_MARGIN, sr, hop_length))))
    draw_lag_max = min(len(acf) - 1, int(np.ceil(
        _bpm_to_lag(tempo_min / ACF_OCTAVE_MARGIN, sr, hop_length))))

    lags = np.arange(draw_lag_min, max(draw_lag_min + 1, draw_lag_max + 1))
    bpms = _lag_to_bpm(lags, sr, hop_length)

    raw_bpm, raw_confidence = estimate_tempo(
        envelope, sr, hop_length=hop_length, tempo_min=tempo_min,
        tempo_max=tempo_max, prior_bpm=prior_bpm)

    # The three candidates _resolve_octave() scored, with the arithmetic
    # it scored them by kept visible: strength x prior = score.
    candidates = []
    for factor in (0.5, 1.0, 2.0):
        bpm = raw_bpm * factor
        in_range = tempo_min <= bpm <= tempo_max

        if in_range:
            _, phase_strength = estimate_beat_phase(envelope, sr, bpm, hop_length)
            prior_weight = float(tempo_prior_weight(bpm, prior_bpm))
            score = phase_strength * prior_weight
        else:
            # Out-of-range candidates are skipped by the real search, and
            # are written anyway so the panel can show them struck out --
            # "this one wasn't even considered" is part of the story.
            phase_strength = prior_weight = score = 0.0

        candidates.append({
            "factor": float(factor),
            "bpm": round(float(bpm), 2),
            "inRange": bool(in_range),
            "phaseStrength": round(float(phase_strength), 4),
            "priorWeight": round(float(prior_weight), 4),
            "score": round(float(score), 4),
            # Compared against the grid's own tempo rather than against
            # the best score computed here, so this says which candidate
            # the map was actually built on.
            "chosen": bool(in_range and abs(bpm - grid.bpm) < 0.01),
        })

    # The phase slide: one score per whole-frame offset across a single
    # beat period, peaking where the pulse train lands on most energy.
    period_frames = _bpm_to_lag(grid.bpm, sr, hop_length)
    phase_scores = _phase_scores(envelope, period_frames)

    return {
        "searchMinBpm": float(tempo_min),
        "searchMaxBpm": float(tempo_max),
        "priorBpm": float(prior_bpm),
        "priorWidthOctaves": float(TEMPO_PRIOR_WIDTH_OCTAVES),
        # Lags rather than BPMs as the array index, because that is what
        # autocorrelation is indexed by; `bpms` is the axis to label with.
        "lagMin": int(lags[0]),
        "lagMax": int(lags[-1]),
        "searchLagMin": int(search_lag_min),
        "searchLagMax": int(search_lag_max),
        "bpms": [round(float(b), 3) for b in bpms],
        "acf": [round(float(v), 5) for v in acf[lags]],
        "prior": [round(float(v), 5) for v in tempo_prior_weight(bpms, prior_bpm)],
        # The product the argmax is actually taken over. Derivable from
        # the two arrays above, and written anyway because it is the
        # curve the stage draws the peak marker on.
        "weighted": [round(float(v), 5)
                      for v in acf[lags] * tempo_prior_weight(bpms, prior_bpm)],
        "rawBpm": round(float(raw_bpm), 2),
        "rawConfidence": round(float(raw_confidence), 4),
        # The two differ exactly when octave correction changed the
        # answer, which is the frame worth putting on screen.
        "chosenBpm": round(float(grid.bpm), 2),
        "octaveCorrected": bool(abs(raw_bpm - grid.bpm) > 0.01),
        "octaveCandidates": candidates,
        "periodFrames": round(float(period_frames), 4),
        "phaseScores": [round(float(v), 5) for v in phase_scores],
        "phaseOffsetFrames": int(np.argmax(phase_scores)),
        "phaseStrength": round(float(grid.phase_strength), 4),
        "offset": round(float(grid.offset), 3),
        "confidence": round(float(grid.confidence), 4),
    }


def write_dsp(path: str, document: dict) -> str:
    """Write the document as compact JSON, like analysis.json -- this is read by a program, not a person."""
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)

    with open(path, "w", encoding="utf-8") as f:
        json.dump(document, f, separators=(",", ":"))

    return path


def export_dsp(folder: str, track, mel_db: np.ndarray, presets: dict = None,
               grid=None, tier_onsets: dict = None, tier_objects: dict = None,
               onset_source: str = "", n_mels: int = 128) -> str:
    """Write dsp.json into an existing beatmap folder. Returns its path."""
    return write_dsp(
        os.path.join(folder, DSP_FILENAME),
        build_dsp(track, mel_db, presets=presets, grid=grid,
                  tier_onsets=tier_onsets, tier_objects=tier_objects,
                  onset_source=onset_source, n_mels=n_mels))
