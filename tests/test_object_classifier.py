"""
tests/test_object_classifier.py

Test strategy for Step 4 (circle / slider / spinner classification +
placement), same three tiers as the earlier steps:

  1. UNIT TESTS on hand-built inputs with a known right answer: snapping
     onsets to the grid, the sustain measurement, and the slider / spinner
     geometry math. Pure-ish -- no audio decoding except where noted.

  2. INTEGRATION TESTS through the real Step 2 -> 3 -> 4 chain on
     synthesized audio designed to force one specific object type:
       - a plain click track  -> all circles
       - clicks then a long held tone -> a spinner appears
       - clicks then a 1/4 burst -> a stream folds into a slider

  3. SMOKE TEST on a real song if one is in data/raw/: the pipeline runs,
     every object lands on the playfield, and the type mix is plausible.

Run with:
    pytest tests/test_object_classifier.py -v
"""

import os
import sys
import glob

import numpy as np
import pytest

sys.path.append(os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "src"))

from onset.detector import Onset
from onset.beat_tracker import BeatGrid
from audio.loader import generate_click_track, load_audio, AudioTrack, DEFAULT_SR
from mapping.hit_object import (
    HitObject, PLAYFIELD_W, PLAYFIELD_H, PLAYFIELD_MARGIN, PLAYFIELD_CENTER,
    circle_radius, DEFAULT_CIRCLE_SIZE,
)
from mapping.slider_generator import generate_slider
from mapping.spinner_generator import generate_spinner
from mapping.object_classifier import (
    snap_onsets, sustain_ratio, classify, assign_geometry, build_objects,
    summarize, _point_segment_distance, MIN_SEPARATION_DIAMETERS,
)


SR = DEFAULT_SR


def _grid(bpm=120.0, offset=0.0, duration=30.0):
    period = 60.0 / bpm
    return BeatGrid(bpm=bpm, offset=offset,
                    beat_times=np.arange(offset, duration, period),
                    confidence=1.0)


def _onset(t, strength=0.8, band="mid"):
    energy = {"low": 0.0, "mid": 0.0, "high": 0.0}
    energy[band] = 1.0
    return Onset(time=t, frame=int(t * 100), strength=strength, band_energy=energy)


def _synth_track(bpm=120.0, sr=SR, name="synth", clicks=(), tones=()):
    """
    Build a synthetic AudioTrack. `clicks` is a list of beat positions for
    50 ms decaying blips; `tones` is a list of (start_beat, end_beat) for
    sustained 220 Hz pads. Everything is peak-normalized at the end.
    """
    period = 60.0 / bpm
    last_beat = max(list(clicks) + [e for _, e in tones] + [1.0]) + 4.0
    n = int(last_beat * period * sr)
    y = np.zeros(n, dtype=np.float32)

    clen = int(0.05 * sr)
    tc = np.arange(clen) / sr
    blip = (np.sin(2 * np.pi * 1000 * tc) * np.exp(-tc * 60)).astype(np.float32)
    for b in clicks:
        s = int(b * period * sr)
        if s + clen <= n:
            y[s:s + clen] += blip

    for b0, b1 in tones:
        s0, s1 = int(b0 * period * sr), min(n, int(b1 * period * sr))
        tt = np.arange(s1 - s0) / sr
        attack = np.minimum(1.0, tt / 0.01)
        y[s0:s1] += (0.6 * np.sin(2 * np.pi * 220 * tt) * attack).astype(np.float32)

    peak = float(np.max(np.abs(y))) or 1.0
    y = (y / peak).astype(np.float32)
    return AudioTrack(y=y, sr=sr, duration=len(y) / sr, path="<synth>", name=name)


# ---------------------------------------------------------------------
# Tier 1: unit tests
# ---------------------------------------------------------------------

def test_snap_onsets_quantizes_and_labels():
    grid = _grid(bpm=120.0)          # beat period 0.5 s, 1/4 step = 0.125 s
    onsets = [_onset(0.02), _onset(0.26), _onset(1.01)]
    snapped = snap_onsets(onsets, grid)

    assert [round(s.time, 3) for s in snapped] == [0.0, 0.25, 1.0]
    assert [s.snap for s in snapped] == ["1/1", "1/2", "1/1"]


def test_snap_onsets_dedupes_same_slot_keeping_stronger():
    grid = _grid(bpm=120.0)
    snapped = snap_onsets([_onset(0.01, strength=0.4),
                           _onset(0.03, strength=0.9)], grid)
    assert len(snapped) == 1
    assert snapped[0].strength == pytest.approx(0.9)


def test_sustain_ratio_high_and_low():
    times = np.array([0.0, 0.1, 0.2, 0.3])
    assert sustain_ratio(np.array([1., 1., 1., 1.]), times, 0.0, 0.35) == 1.0
    assert sustain_ratio(np.array([1., 0., 0., 1.]), times, 0.05, 0.25) == 0.0


def test_generate_slider_single_slide_length_matches_beats():
    grid = _grid(bpm=120.0)                       # 0.5 s / beat
    obj = HitObject(kind="slider", time=0.0, end_time=0.5)   # 1 beat
    generate_slider(obj, grid, start=(256, 192), direction=(1.0, 0.0))

    # velocity = 100 * 1.4 * 1.0 = 140 px/beat -> 1 beat -> 140 px, no repeat
    assert obj.slides == 1
    assert obj.pixel_length == pytest.approx(140.0, abs=1.0)
    assert obj.slider_beats == pytest.approx(1.0)
    for (px, py) in obj.path:
        assert 0 <= px <= PLAYFIELD_W and 0 <= py <= PLAYFIELD_H


def test_generate_slider_adds_repeats_when_too_long_for_playfield():
    grid = _grid(bpm=120.0)
    obj = HitObject(kind="slider", time=0.0, end_time=2.0)   # 4 beats -> 560 px
    generate_slider(obj, grid, start=(256, 192), direction=(1.0, 0.0))

    assert obj.slides >= 2
    # total path length still reflects the intended ~4-beat duration
    assert obj.slides * obj.pixel_length == pytest.approx(560.0, abs=obj.pixel_length)
    for (px, py) in obj.path:
        assert 0 <= px <= PLAYFIELD_W and 0 <= py <= PLAYFIELD_H


def test_generate_spinner_duration_and_centering():
    obj = HitObject(kind="spinner", time=1.0, end_time=4.0)
    generate_spinner(obj)
    assert obj.duration == pytest.approx(3.0)
    assert obj.required_spins == 5          # round(3.0 * 1.8)
    assert (obj.x, obj.y) == PLAYFIELD_CENTER


def test_assign_geometry_keeps_everything_on_the_playfield():
    grid = _grid(bpm=120.0)
    objs = [
        HitObject(kind="circle", time=0.0),
        HitObject(kind="slider", time=0.5, end_time=1.0),
        HitObject(kind="circle", time=1.5),
        HitObject(kind="spinner", time=2.0, end_time=5.0),
        HitObject(kind="circle", time=6.0),
        HitObject(kind="slider", time=6.5, end_time=7.5),
    ]
    assign_geometry(objs, grid, seed=3)

    lo_x, hi_x = PLAYFIELD_MARGIN, PLAYFIELD_W - PLAYFIELD_MARGIN
    lo_y, hi_y = PLAYFIELD_MARGIN, PLAYFIELD_H - PLAYFIELD_MARGIN
    for o in objs:
        assert lo_x <= o.x <= hi_x
        assert lo_y <= o.y <= hi_y
        if o.kind == "spinner":
            assert (o.x, o.y) == PLAYFIELD_CENTER
        if o.kind == "slider":
            for (px, py) in o.path:
                assert 0 <= px <= PLAYFIELD_W and 0 <= py <= PLAYFIELD_H


def test_classify_empty_onsets_returns_empty():
    assert classify([], _grid(), _synth_track(clicks=[0, 1, 2])) == []


# --- placement readability ------------------------------------------------
# These lock in the two problems found by play-testing a generated map in
# osu!(lazer): objects packed so tightly they sat completely on top of each
# other, and circles landing invisibly inside the previous slider's body.

def _cursor_after(obj):
    """Where the cursor is left after `obj` (a slider ends away from its start)."""
    if obj.kind == "slider" and obj.path:
        return obj.path[-1] if obj.slides % 2 == 1 else obj.path[0]
    return (obj.x, obj.y)


def _placement_report(objs, circle_size):
    """(min separation, min slider-body clearance), both in circle diameters."""
    diameter = 2.0 * circle_radius(circle_size)
    min_sep, min_body = float("inf"), float("inf")
    prev = None
    for o in objs:
        if o.kind == "spinner":
            prev = None                      # a spinner clears the screen
            continue
        if prev is not None:
            px, py = _cursor_after(prev)
            min_sep = min(min_sep, np.hypot(o.x - px, o.y - py) / diameter)
            if prev.kind == "slider" and prev.path:
                body = _point_segment_distance((o.x, o.y), prev.path[0],
                                                prev.path[-1]) / diameter
                min_body = min(min_body, body)
        prev = o
    return min_sep, min_body


def test_objects_are_never_stacked_on_top_of_each_other():
    """Consecutive objects must always stay at least the stream floor apart,
    so any overlap reads as a partial 'continuation', never a full burial."""
    track = generate_click_track(bpm=170.0, duration_sec=30.0, sr=DEFAULT_SR)
    objs, grid = build_objects(track)
    min_sep, _ = _placement_report(objs, DEFAULT_CIRCLE_SIZE)
    assert min_sep >= MIN_SEPARATION_DIAMETERS - 1e-6, (
        f"closest pair is {min_sep:.2f} diameters apart")


def test_circles_never_land_inside_the_previous_slider_body():
    """A slider's body is drawn at the circle radius (0.5 diameters), so an
    object whose centre is nearer than that to the body is swallowed by it."""
    track = _synth_track(bpm=140.0,
                         clicks=list(range(0, 8)) + list(range(10, 24)),
                         tones=[(8.0, 9.5), (24.0, 25.5)])
    objs, grid = build_objects(track)
    assert any(o.kind == "slider" for o in objs), "need sliders to test this"
    _, min_body = _placement_report(objs, DEFAULT_CIRCLE_SIZE)
    assert min_body >= 0.45, (
        f"an object sits {min_body:.2f} diameters from a slider body")


def test_spacing_is_linear_in_the_time_gap():
    """Distance snap: double the time gap, double the distance. Placement at
    a 1-beat gap should sit at ~distance_spacing diameters."""
    grid = _grid(bpm=120.0)
    diameter = 2.0 * circle_radius(DEFAULT_CIRCLE_SIZE)
    spacing = 1.2

    def gap_distance(beats):
        objs = [HitObject("circle", time=0.0),
                HitObject("circle", time=beats * grid.beat_period)]
        assign_geometry(objs, grid, circle_size=DEFAULT_CIRCLE_SIZE,
                        distance_spacing=spacing, seed=1)
        return np.hypot(objs[1].x - objs[0].x, objs[1].y - objs[0].y) / diameter

    assert gap_distance(1.0) == pytest.approx(spacing, rel=0.02)
    assert gap_distance(2.0) == pytest.approx(2 * spacing, rel=0.02)


def test_spacing_scales_with_circle_size():
    """Smaller circles (higher CS) mean less absolute travel for the same
    distance-snap setting, because spacing is measured in diameters."""
    grid = _grid(bpm=120.0)

    def travel(cs):
        objs = [HitObject("circle", time=0.0),
                HitObject("circle", time=grid.beat_period)]
        assign_geometry(objs, grid, circle_size=cs, distance_spacing=1.2, seed=1)
        return np.hypot(objs[1].x - objs[0].x, objs[1].y - objs[0].y)

    assert travel(2.0) > travel(6.0)


# ---------------------------------------------------------------------
# Tier 2: integration through the real Step 2 -> 3 -> 4 chain
# ---------------------------------------------------------------------

def test_click_track_is_all_circles():
    track = generate_click_track(bpm=120.0, duration_sec=20.0, sr=DEFAULT_SR)
    objs, grid = build_objects(track)
    counts = summarize(objs)

    assert counts["circle"] > 15
    assert counts["slider"] == 0
    assert counts["spinner"] == 0
    for o in objs:
        assert PLAYFIELD_MARGIN <= o.x <= PLAYFIELD_W - PLAYFIELD_MARGIN
        assert PLAYFIELD_MARGIN <= o.y <= PLAYFIELD_H - PLAYFIELD_MARGIN


def test_held_tone_becomes_a_spinner():
    track = _synth_track(
        bpm=120.0,
        clicks=list(range(0, 16)) + list(range(26, 34)),
        tones=[(16.0, 24.0)],          # 8-beat held pad = 4 s at 120 BPM
    )
    objs, grid = build_objects(track)

    spinners = [o for o in objs if o.kind == "spinner"]
    assert spinners, f"expected a spinner, got {summarize(objs)}"
    assert any(o.duration > 2.0 for o in spinners)
    # the spinner should sit in the held-tone stretch (~8 s .. ~12 s)
    assert any(6.0 < o.time < 13.0 for o in spinners)


def test_quarter_note_burst_folds_into_a_slider():
    burst = [8.0 + 0.25 * k for k in range(8)]      # 8 onsets at 1/4 spacing
    track = _synth_track(
        bpm=120.0,
        clicks=list(range(0, 8)) + burst + list(range(11, 22)),
    )
    objs, grid = build_objects(track)

    sliders = [o for o in objs if o.kind == "slider"]
    assert sliders, f"expected a slider from the burst, got {summarize(objs)}"
    # burst starts at beat 8 == 4.0 s at 120 BPM
    assert any(3.5 < o.time < 5.0 for o in sliders)


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
def test_build_objects_smoke_on_real_song():
    track = load_audio(_find_real_audio_file(), sr=DEFAULT_SR)
    objs, grid = build_objects(track)

    assert objs
    per_sec = len(objs) / track.duration
    assert 0.1 < per_sec < 12.0, f"{per_sec:.2f} objects/sec looks implausible"

    counts = summarize(objs)
    assert counts["circle"] > 0     # a real song is never all sliders/spinners

    for o in objs:
        assert 0 <= o.x <= PLAYFIELD_W and 0 <= o.y <= PLAYFIELD_H
        if o.end_time is not None:
            assert o.end_time >= o.time
        if o.kind == "slider":
            assert o.path and o.slides >= 1 and o.pixel_length > 0


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
