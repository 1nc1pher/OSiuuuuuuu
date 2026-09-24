"""
tests/fixtures/make_client_fixture.py

Writes a small but completely real beatmap analysis into
frontend/OsuClient.Tests/Fixtures/, for the C# side to read.

The C# tests need to assert that the ported peak-picker reproduces the
backend's own onsets. Hand-written JSON cannot show that: it would only
prove the reader parses what the test author imagined the writer emits.
So this runs the actual pipeline over a short synthetic track and
commits the actual output.

The track is the calibrated one from tests/test_dsp_trace.py -- three
loudness populations so the difficulty presets genuinely disagree,
off-grid subdivisions so snapping has something to do, and quiet windows
with and without sustained energy so all three object kinds occur. At 24
seconds the pair of files is small enough to keep in the repository.

Regenerate after any change to the export format:

    python tests/fixtures/make_client_fixture.py
"""

import os
import sys

import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.append(os.path.join(ROOT, "src"))

from audio.loader import AudioTrack
from audio.visualize import compute_mel_spectrogram
from export.analysis_export import export_analysis
from mapping.difficulty import PRESETS, build_map_detailed
from onset.beat_tracker import track_beats


OUT_DIR = os.path.join(ROOT, "frontend", "OsuClient.Tests", "Fixtures")

# Two tiers rather than five: the assertions are about a tier's shape and
# about the extremes disagreeing, and two keeps the committed files small.
TIERS = ("Easy", "Expert")

SOURCE_TIER = "Expert"


def build_track():
    """
    The calibrated fixture track. Kept in step with the copy in
    tests/test_dsp_trace.py -- see that docstring for why each layer is
    here and what it exercises.
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
        return 6.05 < at < 6.95 or 9.8 < at < 14.2 or 16.6 < at < 21.4

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

    hit(17.0, 0.9, 700, decay=14, length=0.5)
    hit(20.0, 0.9, 700, decay=14, length=0.5)

    y += rng.normal(0, 0.035, n)
    y /= np.max(np.abs(y))

    return AudioTrack(y=y.astype(np.float32), sr=sr, duration=duration,
                      path="<synthetic>", name="calibrated_test_track")


def main():
    os.makedirs(OUT_DIR, exist_ok=True)

    track = build_track()
    mel = compute_mel_spectrogram(track)
    grid = track_beats(track)

    onsets, objects, presets = {}, {}, {}

    for name in TIERS:
        preset = PRESETS[name]
        detected, placed, _ = build_map_detailed(track, preset)

        onsets[name] = detected
        objects[name] = placed
        presets[name] = preset

    written = export_analysis(OUT_DIR, track, mel, onsets[SOURCE_TIER], grid,
                              objects, onset_source=SOURCE_TIER,
                              presets=presets, tier_onsets=onsets)

    # The spectrogram is kept, unlike an earlier version of this script that
    # deleted it: no C# test reads it, but the reveal's top half *is* it, and
    # a screenshot of the chassis against a blank strip cannot show whether
    # the onsets and the beat grid line up with the streaks that produced
    # them. At this track length it is a small file.
    for key in ("analysis", "dsp", "spectrogram"):
        path = written[key]
        print(f"wrote {path}  ({os.path.getsize(path) / 1024:.0f} KB)")

    print(f"  tempo: {grid.bpm:.2f} BPM, confidence {grid.confidence:.3f}")

    for name in TIERS:
        print(f"  {name}: {len(onsets[name])} onsets, {len(objects[name])} objects")


if __name__ == "__main__":
    main()
