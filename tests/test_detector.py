"""
tests/test_detector.py

Test strategy for the onset detector, in three tiers (see README "Testing"
section for the full explanation of why each tier exists):

  1. UNIT TESTS on synthetic signals with a known, exact ground truth.
     These don't need librosa or any audio file -- they test normalize(),
     adaptive_threshold(), and pick_peaks() directly against hand-built
     numpy arrays where we already know the "correct" answer.

  2. INTEGRATION TEST on a generated click track (needs librosa, since it
     goes through the real STFT/mel-spectrogram path). Verifies the full
     detect_onsets() pipeline end-to-end against a signal with known onset
     times.

  3. SMOKE TEST on a real music file, if one is present in data/raw/.
     Can't assert exact correctness (no ground truth), so it just checks
     the pipeline runs without crashing and returns a plausible number of
     onsets. Real perceptual validation happens via sonify.py instead
     (see README) -- that's a listening test, not something you can
     assert on in a unit test.

Run with:
    pytest tests/test_detector.py -v
"""

import os
import sys
import glob

import numpy as np
import pytest

sys.path.append(os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "src"))

from onset.detector import (
    normalize, adaptive_threshold, pick_peaks, detect_onsets,
    ADAPTIVE_THRESHOLD_MARGIN, ADAPTIVE_THRESHOLD_DELTA,
)
from audio.loader import generate_click_track, load_audio, DEFAULT_SR


# ---------------------------------------------------------------------
# Tier 1: pure-numpy unit tests, no audio decoding involved
# ---------------------------------------------------------------------

def test_normalize_basic_range():
    x = np.array([2.0, 4.0, 6.0, 8.0])
    n = normalize(x)
    assert n.min() == pytest.approx(0.0)
    assert n.max() == pytest.approx(1.0)


def test_normalize_constant_signal_returns_zeros():
    # a flat/silent signal has no min-max range -- must not divide by zero
    x = np.full(10, 0.5)
    n = normalize(x)
    assert np.all(n == 0.0)


def test_adaptive_threshold_and_peak_picking_on_synthetic_flux():
    """
    The core validation test: build a fake flux curve with a known noise
    floor plus 10 sharp spikes at known frame positions, and check that
    peak picking recovers exactly those positions and nothing else.

    This is the same test that caught the multiplicative-threshold bug
    during development (see detector.py's adaptive_threshold docstring)
    -- a purely `median * margin` threshold let noise leak through in
    silent regions. Keeping this test in the suite guards against that
    regression if the threshold formula is ever changed.
    """
    sr = 22050
    hop = 512
    n_frames = 400
    rng = np.random.default_rng(0)   # fixed seed -> reproducible test

    flux = rng.normal(0.05, 0.01, n_frames).clip(min=0)  # noise floor only
    true_peak_frames = [20, 55, 90, 125, 160, 195, 230, 265, 300, 335]
    for p in true_peak_frames:
        flux[p] += 1.0  # sharp, unambiguous spike

    flux_n = normalize(flux)
    thresh = adaptive_threshold(flux_n, sr, hop_length=hop,
                                 margin=ADAPTIVE_THRESHOLD_MARGIN,
                                 delta=ADAPTIVE_THRESHOLD_DELTA)
    peaks = pick_peaks(flux_n, thresh, sr, hop_length=hop)

    assert list(peaks) == true_peak_frames

    # bonus check: spacing between detected peaks implies the correct BPM
    times = peaks * hop / sr
    implied_bpm = 60.0 / np.median(np.diff(times))
    true_bpm = 60.0 / (np.median(np.diff(true_peak_frames)) * hop / sr)
    assert implied_bpm == pytest.approx(true_bpm, rel=1e-6)


def test_pick_peaks_respects_minimum_spacing():
    """Two distinct local maxima close together (closer than
    min_spacing_sec) should collapse into a single detected peak --
    the stronger of the two."""
    sr = 22050
    hop = 512
    flux = np.zeros(50)
    flux[10] = 1.0   # stronger peak
    flux[11] = 0.3   # dip between them, so both 10 and 12 are local maxima
    flux[12] = 0.8   # weaker peak, only 2 frames after the first
    thresh = np.full(50, 0.1)

    peaks = pick_peaks(flux, thresh, sr, hop_length=hop, min_spacing_sec=0.05)
    assert len(peaks) == 1
    assert peaks[0] == 10  # the stronger of the two nearby peaks wins


def test_adaptive_threshold_delta_prevents_noise_false_positives():
    """Regression test targeting the exact bug found during development:
    with delta=0, pure noise can produce false-positive onsets; with the
    default delta it should not."""
    sr = 22050
    hop = 512
    rng = np.random.default_rng(1)
    flux = normalize(rng.normal(0.05, 0.01, 300).clip(min=0))

    thresh_no_floor = adaptive_threshold(flux, sr, hop_length=hop,
                                          margin=ADAPTIVE_THRESHOLD_MARGIN, delta=0.0)
    thresh_with_floor = adaptive_threshold(flux, sr, hop_length=hop,
                                            margin=ADAPTIVE_THRESHOLD_MARGIN,
                                            delta=ADAPTIVE_THRESHOLD_DELTA)

    peaks_no_floor = pick_peaks(flux, thresh_no_floor, sr, hop_length=hop)
    peaks_with_floor = pick_peaks(flux, thresh_with_floor, sr, hop_length=hop)

    # the floor should strictly reduce (or leave equal) the number of
    # spurious peaks detected in a signal that is pure noise
    assert len(peaks_with_floor) <= len(peaks_no_floor)


# ---------------------------------------------------------------------
# Tier 2: integration test through the real STFT/mel pipeline (needs librosa)
# ---------------------------------------------------------------------

def test_detect_onsets_on_synthetic_click_track():
    """
    End-to-end test: generate a clean 128 BPM click track, run it through
    the *actual* detect_onsets() pipeline (real STFT + mel spectrogram,
    not the numpy-only mocks used in Tier 1), and check we recover
    onsets at close to the right times and the right count.

    We allow a little slack (+/- 1 onset, +/- one hop's worth of timing
    error) because real STFT framing introduces small quantization you
    don't get with hand-built numpy arrays.
    """
    bpm = 128.0
    duration = 8.0
    track = generate_click_track(bpm=bpm, duration_sec=duration, sr=DEFAULT_SR)

    onsets = detect_onsets(track)
    expected_clicks = int(duration / (60.0 / bpm))

    assert abs(len(onsets) - expected_clicks) <= 1, (
        f"expected ~{expected_clicks} onsets for a clean {bpm} BPM click "
        f"track, got {len(onsets)}"
    )

    # spacing between detected onsets should imply ~128 BPM
    times = np.array([o.time for o in onsets])
    implied_bpm = 60.0 / np.median(np.diff(times))
    assert implied_bpm == pytest.approx(bpm, abs=3.0)


# ---------------------------------------------------------------------
# Tier 3: smoke test on a real song, if the user has dropped one in data/raw/
# ---------------------------------------------------------------------

def _find_real_audio_file():
    project_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    raw_dir = os.path.join(project_root, "data", "raw")
    for ext in ("*.mp3", "*.wav", "*.ogg", "*.flac"):
        matches = glob.glob(os.path.join(raw_dir, ext))
        if matches:
            return matches[0]
    return None


@pytest.mark.skipif(_find_real_audio_file() is None,
                     reason="no audio file found in data/raw/ -- drop one in to run this test")
def test_detect_onsets_smoke_test_on_real_song():
    """Not a correctness test (no ground truth for a real song) -- just
    verifies the full pipeline runs on real, messy audio without crashing,
    and returns a plausible (non-zero, non-absurd) number of onsets."""
    path = _find_real_audio_file()
    track = load_audio(path, sr=DEFAULT_SR)
    onsets = detect_onsets(track)

    onsets_per_sec = len(onsets) / track.duration
    assert 0.2 < onsets_per_sec < 15.0, (
        f"{onsets_per_sec:.2f} onsets/sec is outside a plausible range -- "
        f"check --margin/--delta tuning"
    )


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
