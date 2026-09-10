"""
tests/test_difficulty.py

Test strategy for Step 5 (difficulty scaling):

  1. UNIT TESTS on the preset table: the YAML file and the in-code
     BUILTIN_PRESETS fallback agree, all five tiers are present and
     star-sorted, lookup by name / star target works, the per-tier knobs
     move monotonically in the right direction, and malformed presets are
     rejected.

  2. INTEGRATION TESTS through build_map() on synthesized audio with
     sub-beat content: the honest density metric (rhythm_slot_count) is
     monotonic across the tiers, every tier produces a playable map, and
     harder tiers really do use a finer rhythmic grid.

  3. SMOKE TEST on a real song if present.

Run with:
    pytest tests/test_difficulty.py -v
"""

import os
import sys
import glob

import numpy as np
import pytest

sys.path.append(os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "src"))

from mapping.difficulty import (
    DifficultyPreset, load_presets, get_preset, build_map, rhythm_slot_count,
    PRESETS, TIER_ORDER, DEFAULT_PRESETS_PATH, _make_preset,
)
from mapping.hit_object import PLAYFIELD_W, PLAYFIELD_H
from mapping.object_classifier import summarize
from audio.loader import load_audio, AudioTrack, DEFAULT_SR


SR = DEFAULT_SR


def _synth_track(bpm=128.0, seconds=24.0, sr=SR):
    """Clicks on every beat plus quieter ghost notes on 1/2 and 1/4."""
    period = 60.0 / bpm
    n = int(seconds * sr)
    y = np.zeros(n, dtype=np.float32)
    clen = int(0.05 * sr)
    tc = np.arange(clen) / sr
    blip = (np.sin(2 * np.pi * 1000 * tc) * np.exp(-tc * 60)).astype(np.float32)

    beat = 0
    while beat * period < seconds:
        for frac, gain in ((0.0, 1.0), (0.25, 0.35), (0.5, 0.6), (0.75, 0.35)):
            s = int((beat + frac) * period * sr)
            if s + clen <= n:
                y[s:s + clen] += gain * blip
        beat += 1

    y = y / (float(np.max(np.abs(y))) or 1.0)
    return AudioTrack(y=y.astype(np.float32), sr=sr, duration=n / sr,
                      path="<synth>", name="synth_ghosts")


# ---------------------------------------------------------------------
# Tier 1: the preset table
# ---------------------------------------------------------------------

def test_yaml_and_builtin_fallback_agree():
    from_file = load_presets(DEFAULT_PRESETS_PATH)
    from_builtin = load_presets(os.path.join("no", "such", "presets.yaml"))
    assert from_file == from_builtin


def test_all_tiers_present_and_star_sorted():
    assert list(PRESETS) == TIER_ORDER
    stars = [PRESETS[n].stars for n in TIER_ORDER]
    assert stars == sorted(stars)
    assert len(set(stars)) == len(stars)


def test_get_preset_by_name_is_case_insensitive():
    assert get_preset("hard") is PRESETS["Hard"]
    assert get_preset("EXPERT").name == "Expert"


def test_get_preset_by_star_target_picks_nearest():
    assert get_preset(1.4).name == "Easy"
    assert get_preset(5.0).name == "Insane"      # 4.8 is closer than 6.0
    assert get_preset(100).name == "Expert"
    assert get_preset("2.5").name == "Normal"    # numeric string also works


def test_get_preset_unknown_name_raises():
    with pytest.raises(KeyError):
        get_preset("Lunatic")


def test_difficulty_knobs_are_monotonic():
    p = [PRESETS[n] for n in TIER_ORDER]

    def strictly_up(vals):
        return all(a < b for a, b in zip(vals, vals[1:]))

    def non_decreasing(vals):
        return all(a <= b for a, b in zip(vals, vals[1:]))

    assert strictly_up([x.stars for x in p])
    assert strictly_up([-x.onset_margin for x in p])        # sensitivity rises
    assert non_decreasing([x.snap_division for x in p])
    assert non_decreasing([-x.min_spacing_beats for x in p])  # spacing shrinks
    assert strictly_up([x.approach_rate for x in p])
    assert strictly_up([x.overall_difficulty for x in p])
    assert strictly_up([x.hp_drain for x in p])
    assert non_decreasing([x.circle_size for x in p])
    assert strictly_up([x.slider_multiplier for x in p])
    assert strictly_up([x.distance_spacing for x in p])   # diameters per beat
    assert strictly_up([x.max_turn_degrees for x in p])   # flow gets sharper


def test_to_osu_difficulty_section():
    section = PRESETS["Hard"].to_osu_difficulty_section()
    assert set(section) == {"HPDrainRate", "CircleSize", "OverallDifficulty",
                            "ApproachRate", "SliderMultiplier", "SliderTickRate"}
    for key in ("HPDrainRate", "CircleSize", "OverallDifficulty", "ApproachRate"):
        assert 0.0 <= section[key] <= 10.0
    assert section["SliderMultiplier"] > 0


def test_malformed_presets_are_rejected():
    with pytest.raises(ValueError):
        _make_preset("Broken", {"stars": 3.0})                 # missing fields

    full = {k: getattr(PRESETS["Hard"], k)
            for k in PRESETS["Hard"].__dataclass_fields__ if k != "name"}
    with pytest.raises(ValueError):
        _make_preset("Broken", {**full, "surprise": 1})        # unknown field


# ---------------------------------------------------------------------
# Tier 2: build_map() integration
# ---------------------------------------------------------------------

def test_rhythm_slot_count_is_monotonic_across_tiers():
    track = _synth_track()
    slots = [rhythm_slot_count(track, PRESETS[n]) for n in TIER_ORDER]
    assert slots == sorted(slots), slots
    assert slots[-1] > slots[0]          # Expert genuinely denser than Easy


def test_every_tier_produces_a_playable_map():
    track = _synth_track()
    for name in TIER_ORDER:
        objs, grid = build_map(track, PRESETS[name])
        assert objs, f"{name} produced no objects"
        for o in objs:
            assert 0 <= o.x <= PLAYFIELD_W and 0 <= o.y <= PLAYFIELD_H
            if o.end_time is not None:
                assert o.end_time >= o.time
            if o.kind == "slider":
                assert o.path and o.slides >= 1 and o.pixel_length > 0


def test_easy_is_sparsest_and_expert_is_densest():
    # NOTE: the raw object count is not strictly monotonic across the
    # middle tiers -- harder tiers fold long streams into chains of
    # sliders, so they can carry more notes in fewer objects. That's why
    # rhythm_slot_count (tested above) is the real density metric. What
    # must still hold: Easy has the fewest objects and Expert the most.
    track = _synth_track()
    counts = [len(build_map(track, PRESETS[n])[0]) for n in TIER_ORDER]
    assert counts[0] == min(counts), counts
    assert counts[-1] == max(counts), counts


def test_no_tier_stacks_objects_or_buries_them_in_sliders():
    """Play-testing in osu!(lazer) surfaced objects sitting completely on
    top of each other, and circles hidden inside the slider they followed.
    Every tier must stay clear of both."""
    from mapping.hit_object import circle_radius
    from mapping.object_classifier import (
        _point_segment_distance, MIN_SEPARATION_DIAMETERS,
    )

    track = _synth_track()
    for name in TIER_ORDER:
        preset = PRESETS[name]
        objs, _ = build_map(track, preset)
        diameter = 2.0 * circle_radius(preset.circle_size)

        prev = None
        for o in objs:
            if o.kind == "spinner":
                prev = None
                continue
            if prev is not None:
                if prev.kind == "slider" and prev.path:
                    px, py = (prev.path[-1] if prev.slides % 2 == 1
                              else prev.path[0])
                else:
                    px, py = prev.x, prev.y
                sep = np.hypot(o.x - px, o.y - py) / diameter
                assert sep >= MIN_SEPARATION_DIAMETERS - 1e-6, (
                    f"{name}: objects {sep:.2f} diameters apart at {o.time:.2f}s")

                if prev.kind == "slider" and prev.path:
                    body = _point_segment_distance(
                        (o.x, o.y), prev.path[0], prev.path[-1]) / diameter
                    assert body >= 0.45, (
                        f"{name}: object buried in a slider body at {o.time:.2f}s")
            prev = o


def test_distance_snap_matches_each_preset():
    """At a 1-beat gap the cursor should travel exactly the tier's
    distance_spacing, in circle diameters -- the editor's distance snap."""
    from mapping.hit_object import circle_radius
    from mapping.object_classifier import assign_geometry
    from mapping.hit_object import HitObject
    from onset.beat_tracker import BeatGrid

    period = 0.5
    grid = BeatGrid(bpm=120.0, offset=0.0,
                    beat_times=np.arange(0, 20, period), confidence=1.0)
    for name in TIER_ORDER:
        preset = PRESETS[name]
        objs = [HitObject("circle", time=0.0), HitObject("circle", time=period)]
        assign_geometry(objs, grid, circle_size=preset.circle_size,
                        distance_spacing=preset.distance_spacing,
                        max_turn_degrees=preset.max_turn_degrees, seed=2)
        diameter = 2.0 * circle_radius(preset.circle_size)
        travelled = np.hypot(objs[1].x - objs[0].x, objs[1].y - objs[0].y) / diameter
        assert travelled == pytest.approx(preset.distance_spacing, rel=0.02), name


def test_harder_tiers_use_a_finer_rhythmic_grid():
    track = _synth_track()
    easy_snaps = {o.snap for o in build_map(track, PRESETS["Easy"])[0]}
    hard_snaps = {o.snap for o in build_map(track, PRESETS["Hard"])[0]}

    assert easy_snaps == {"1/1"}                 # Easy is on the beat only
    assert hard_snaps & {"1/2", "1/4"}           # Hard resolves subdivisions


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
def test_difficulty_scaling_on_real_song():
    track = load_audio(_find_real_audio_file(), sr=DEFAULT_SR)

    slots = [rhythm_slot_count(track, PRESETS[n]) for n in TIER_ORDER]
    assert slots == sorted(slots), slots
    assert slots[-1] > slots[0] * 1.5        # clear spread easy -> expert

    for name in ("Easy", "Expert"):
        objs, _ = build_map(track, PRESETS[name])
        per_sec = len(objs) / track.duration
        assert 0.1 < per_sec < 12.0, f"{name}: {per_sec:.2f} obj/s"
        assert summarize(objs)["circle"] >= 0


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
