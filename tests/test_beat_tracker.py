"""
tests/test_beat_tracker.py

Test strategy for Step 3 (tempo + beat grid), same three tiers as the
onset detector (see README "Testing"):

  1. UNIT TESTS on synthetic onset envelopes with an exact known tempo /
     phase. Pure numpy -- no audio decoding. These pin down estimate_tempo(),
     estimate_beat_phase() and the octave-correction logic.

  2. INTEGRATION TEST through the real STFT/mel path on a generated click
     track: track_beats() end-to-end must recover the click tempo, a phase
     that lands on the clicks, and a beat grid that actually lines up.

  3. SMOKE TEST on a real song if one is in data/raw/. No ground truth,
     so it only checks the pipeline runs and returns a sane tempo. Real
     validation is the metronome listening test (beat_tracker.py --sonify).

Run with:
    pytest tests/test_beat_tracker.py -v
"""

import os
import sys
import glob

import numpy as np
import pytest

sys.path.append(os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "src"))

from onset.beat_tracker import (
    estimate_tempo, estimate_beat_phase, _resolve_octave, track_beats,
    _bpm_to_lag, BeatGrid, TEMPO_MIN_BPM, TEMPO_MAX_BPM, TEMPO_PRIOR_BPM,
)
from audio.loader import generate_click_track, load_audio, DEFAULT_SR


SR = DEFAULT_SR
HOP = 512


def _impulse_envelope(bpm, sr=SR, hop=HOP, duration_sec=20.0, offset_frames=0.0,
                       subdivision_gain=0.0, noise=0.0, seed=0):
    """
    Build a synthetic onset envelope: an impulse train at exactly `bpm`,
    optionally with weaker impulses halfway between beats (subdivision_gain)
    and a Gaussian noise floor. Each impulse is deposited with linear
    interpolation across its two neighbouring frames, so the train stays
    exactly periodic (no cumulative rounding drift) even when the beat
    period is not a whole number of frames.
    """
    period = _bpm_to_lag(bpm, sr, hop)
    n_frames = int(duration_sec * sr / hop)
    rng = np.random.default_rng(seed)
    env = rng.normal(0.0, noise, n_frames).clip(min=0) if noise else np.zeros(n_frames)

    def deposit(pos, gain):
        i = int(np.floor(pos))
        frac = pos - i
        if 0 <= i < n_frames:
            env[i] += gain * (1.0 - frac)
        if 0 <= i + 1 < n_frames:
            env[i + 1] += gain * frac

    k = 0
    while offset_frames + k * period < n_frames:
        base = offset_frames + k * period
        deposit(base, 1.0)
        if subdivision_gain > 0:
            deposit(base + period / 2.0, subdivision_gain)
        k += 1
    return env


# ---------------------------------------------------------------------
# Tier 1: pure-numpy unit tests
# ---------------------------------------------------------------------

@pytest.mark.parametrize("bpm", [90.0, 110.0, 128.0, 150.0, 174.0])
def test_estimate_tempo_recovers_known_bpm(bpm):
    env = _impulse_envelope(bpm)
    est, conf = estimate_tempo(env, SR, hop_length=HOP)
    assert est == pytest.approx(bpm, abs=2.0), f"got {est:.2f} for true {bpm}"
    assert conf > 0.3


@pytest.mark.parametrize("offset_frames", [0, 5, 11, 17])
def test_estimate_beat_phase_recovers_offset(offset_frames):
    bpm = 128.0
    env = _impulse_envelope(bpm, offset_frames=offset_frames)
    offset_sec, strength = estimate_beat_phase(env, SR, bpm, hop_length=HOP)

    expected = offset_frames * HOP / SR
    period = 60.0 / bpm
    # phase is only defined modulo one beat period
    err = min((offset_sec - expected) % period, (expected - offset_sec) % period)
    assert err <= HOP / SR + 1e-9
    assert strength > 1.0  # beats land on above-average energy


def test_octave_correction_prefers_base_over_double():
    """Envelope with strong beats + weak halfway subdivisions. The raw
    autocorrelation is tempted by 2x tempo; octave correction should stay
    on the base tempo because the pulse train aligns far better there."""
    bpm = 128.0
    env = _impulse_envelope(bpm, subdivision_gain=0.35)
    resolved = _resolve_octave(env, SR, HOP, bpm, TEMPO_MIN_BPM, TEMPO_MAX_BPM,
                                TEMPO_PRIOR_BPM)
    assert resolved == pytest.approx(bpm, abs=1e-6)


def test_octave_correction_pulls_half_tempo_estimate_up():
    """If the raw estimate came in at half the true tempo, re-scoring 2x
    should recover the true tempo (clicks on every beat align as well as
    clicks on every other beat, and the prior favours the faster one)."""
    true_bpm = 150.0
    env = _impulse_envelope(true_bpm)
    resolved = _resolve_octave(env, SR, HOP, true_bpm / 2, TEMPO_MIN_BPM,
                                TEMPO_MAX_BPM, TEMPO_PRIOR_BPM)
    assert resolved == pytest.approx(true_bpm, abs=1e-6)


def test_to_osu_timing_units():
    grid = BeatGrid(bpm=120.0, offset=0.25,
                    beat_times=np.arange(0.25, 10, 0.5), confidence=0.9)
    offset_ms, beat_len_ms = grid.to_osu_timing()
    assert offset_ms == pytest.approx(250.0)
    assert beat_len_ms == pytest.approx(500.0)  # 120 BPM -> 500 ms/beat


# ---------------------------------------------------------------------
# Tier 2: integration through the real STFT/mel pipeline
# ---------------------------------------------------------------------

@pytest.mark.parametrize("bpm", [100.0, 128.0, 150.0])
def test_track_beats_on_click_track(bpm):
    track = generate_click_track(bpm=bpm, duration_sec=20.0, sr=DEFAULT_SR)
    grid = track_beats(track)

    assert grid.bpm == pytest.approx(bpm, abs=2.0), (
        f"tempo {grid.bpm:.2f} for a clean {bpm} BPM click track"
    )
    assert grid.confidence > 0.3
    assert grid.phase_strength > 1.0

    # "locked": spectral-flux onset detection has a small constant latency,
    # so grid beats sit a fixed few ms after each click. What matters is
    # that this offset is *constant* -- if the tempo or phase were wrong it
    # would drift across the track. Check every click is the same distance
    # from its nearest grid beat.
    click_period = 60.0 / bpm
    true_clicks = np.arange(click_period, track.duration - click_period, click_period)
    signed = []
    for ct in true_clicks:
        i = np.argmin(np.abs(grid.beat_times - ct))
        signed.append(grid.beat_times[i] - ct)
    signed = np.array(signed)
    assert signed.std() < 0.015, f"grid drifts vs clicks (std {signed.std()*1000:.1f} ms)"
    assert np.abs(signed).max() < 0.08, "grid too far from clicks even allowing latency"

    # grid should span roughly the whole track
    assert grid.beat_times[0] < 2 * click_period
    assert grid.beat_times[-1] > track.duration - 2 * click_period


def test_track_beats_offset_matches_click_phase():
    """A click track's first click is at t=0, so the grid offset should be
    a whole number of beats from 0, give or take the constant onset-flux
    detection latency (a few tens of ms)."""
    bpm = 120.0
    track = generate_click_track(bpm=bpm, duration_sec=16.0, sr=DEFAULT_SR)
    grid = track_beats(track)

    period = 60.0 / bpm
    phase = grid.offset % period
    phase = min(phase, period - phase)
    assert phase < 0.09, f"offset {grid.offset:.3f}s is off-phase by {phase*1000:.0f}ms"


# ---------------------------------------------------------------------
# Tier 3: smoke test on a real song, if present
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
                     reason="no audio file in data/raw/ -- drop one in to run this test")
def test_track_beats_smoke_test_on_real_song():
    path = _find_real_audio_file()
    track = load_audio(path, sr=DEFAULT_SR)
    grid = track_beats(track)

    assert TEMPO_MIN_BPM <= grid.bpm <= TEMPO_MAX_BPM
    assert grid.n_beats > 10
    # beats are monotonic and roughly evenly spaced
    diffs = np.diff(grid.beat_times)
    assert np.all(diffs > 0)
    assert np.std(diffs) < 1e-6  # constant-tempo grid by construction


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
