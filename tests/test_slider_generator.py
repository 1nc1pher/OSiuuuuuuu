"""
tests/test_slider_generator.py

The curved slider shapes (src/mapping/slider_generator.py): arcs ("P"),
S-curves ("B") and corners (multi-point "L") alongside the straight slider.

What has to hold for the new shapes not to cost the maps anything:

  timing     one slide is exactly pixel_length long as drawn, and the curve
             is always at least that long before trimming (a client
             stretches a short curve's last segment, which would bend it)
  drawing    the sampler here matches what the client draws: arcs through
             all three anchors, beziers through their end anchors
  playfield  every body stays on the playfield; curves inside the margin
  flow       a curve leaves its head along the incoming heading (no kink)
  overlap    a curve is never worse at keeping off recent objects than the
             straight slider it replaces, and never crosses back over them
             when it fully fits
  tiers      Easy only ever gets gentle arcs; S-curves from Normal, corners
             from Hard
  compat     no rng -> exactly the old straight sliders
"""

import math
import os
import sys

import numpy as np
import pytest

sys.path.append(os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "src"))
from mapping.hit_object import (
    HitObject, PLAYFIELD_W, PLAYFIELD_H, PLAYFIELD_MARGIN, circle_radius,
)
from mapping.slider_generator import (
    generate_slider, sample_curve, trim_to_length, slider_body_points,
    slider_end_position, slider_exit_heading, slider_shape, clearance_ratio,
    room_ahead, _arc, _s_curve, _corner, _shape_weights,
    STRAIGHT, ARC, S_CURVE, CORNER, DEFAULT_SLIDER_MULTIPLIER,
)
from mapping.difficulty import build_map, PRESETS, TIER_ORDER
from onset.beat_tracker import BeatGrid
from export.osu_writer import hit_object_line


def _grid(bpm=120.0):
    period = 60.0 / bpm
    return BeatGrid(bpm=bpm, offset=0.0, beat_times=np.arange(0, 60, period), confidence=1.0)


def _length(pts):
    pts = np.asarray(pts, dtype=float)
    return float(np.linalg.norm(np.diff(pts, axis=0), axis=1).sum())


def _heading(a, b):
    return math.atan2(b[1] - a[1], b[0] - a[0])


def _angle_between(a, b):
    return abs((a - b + math.pi) % (2 * math.pi) - math.pi)


DIAMETER = 2 * circle_radius(4.0)


def _random_sliders(n=400, seed=0, max_bend=130.0, with_avoid=True):
    """Fuzz generate_slider: random heads, headings, lengths, recent geometry."""
    rng = np.random.default_rng(seed)
    grid = _grid()
    out = []
    for _ in range(n):
        start = (float(rng.uniform(PLAYFIELD_MARGIN, PLAYFIELD_W - PLAYFIELD_MARGIN)),
                 float(rng.uniform(PLAYFIELD_MARGIN, PLAYFIELD_H - PLAYFIELD_MARGIN)))
        angle = float(rng.uniform(-math.pi, math.pi))
        beats = float(rng.choice([0.5, 1.0, 1.5, 2.0, 3.0, 4.0]))
        avoid = []
        if with_avoid:
            # a previous object somewhere behind the head, as placement leaves it
            back = angle + math.pi + float(rng.uniform(-0.8, 0.8))
            dist = float(rng.uniform(0.65, 2.5)) * DIAMETER
            avoid.append(("point", (start[0] + math.cos(back) * dist,
                                    start[1] + math.sin(back) * dist)))
            if rng.random() < 0.5:
                avoid.append(("point", tuple(rng.uniform([40, 40], [472, 344]))))
        obj = HitObject("slider", time=0.0, end_time=beats * grid.beat_period)
        generate_slider(obj, grid, start=start, direction=(math.cos(angle), math.sin(angle)),
                        rng=np.random.default_rng(int(rng.integers(1 << 30))),
                        curl=float(rng.choice([-1.0, 1.0])), max_bend_degrees=max_bend,
                        diameter=DIAMETER, avoid=avoid,
                        clearance=0.85 * DIAMETER)
        out.append((obj, start, angle, beats, avoid))
    return out


# --- shape builders + sampler ---------------------------------------------

@pytest.mark.parametrize("builder", [
    lambda s, a, L: _arc(s, a, 1, math.radians(90), L),
    lambda s, a, L: _arc(s, a, -1, math.radians(150), L),
    lambda s, a, L: _s_curve(s, a, 1, 0.3, L),
    lambda s, a, L: _corner(s, a, -1, math.radians(60), 0.5, L),
])
def test_each_shape_is_built_to_the_length_asked_for(builder):
    curve_type, anchors = builder((256.0, 192.0), 0.3, 150.0)
    assert _length(sample_curve(curve_type, anchors)) == pytest.approx(150.0, rel=0.01)


def test_arc_sampling_passes_through_all_three_anchors():
    curve_type, anchors = _arc((200.0, 200.0), 0.0, 1, math.radians(120), 180.0)
    assert curve_type == "P"
    pts = np.array(sample_curve(curve_type, anchors))
    assert np.allclose(pts[0], anchors[0]) and np.allclose(pts[-1], anchors[2], atol=1e-6)
    assert np.min(np.linalg.norm(pts - np.asarray(anchors[1]), axis=1)) < 2.0
    # every sample on one circle: the arc starts heading +x from (200, 200)
    # and bends toward +y, so its centre is straight below the head
    radius = 180.0 / math.radians(120)
    assert np.allclose(np.linalg.norm(pts - np.array([200.0, 200.0 + radius]), axis=1),
                       radius, atol=0.01)


def test_collinear_arc_falls_back_to_a_line():
    pts = sample_curve("P", [(0, 0), (50, 0), (100, 0)])
    assert pts == [(0.0, 0.0), (50.0, 0.0), (100.0, 0.0)]


def test_bezier_passes_through_its_end_anchors_and_repeats_split_it():
    pts = sample_curve("B", [(0, 0), (50, 80), (100, 0)])
    assert pts[0] == (0.0, 0.0) and np.allclose(pts[-1], (100, 0))
    # a repeated anchor is a hard corner: the curve passes exactly through it
    corner = sample_curve("B", [(0, 0), (50, 0), (50, 0), (50, 50)])
    assert any(np.allclose(p, (50, 0)) for p in corner)


def test_trimming_cuts_and_extends_like_the_client():
    line = [(0.0, 0.0), (100.0, 0.0)]
    assert _length(trim_to_length(line, 60.0)) == pytest.approx(60.0)
    assert trim_to_length(line, 150.0)[-1] == pytest.approx((150.0, 0.0))


# --- generated sliders ------------------------------------------------------

def test_every_body_is_exactly_one_slide_long_and_never_stretched():
    for obj, *_ in _random_sliders():
        body = slider_body_points(obj)
        assert _length(body) == pytest.approx(obj.pixel_length, abs=0.5)
        # the untrimmed curve reaches the length itself: nothing is extended
        if slider_shape(obj) != STRAIGHT:
            assert _length(sample_curve(obj.curve_type, obj.path)) >= obj.pixel_length


def test_choosing_a_shape_never_changes_the_timing():
    """Slide count and slide length are settled before any shape is
    considered: a curve lasts exactly as long as the straight slider the
    same slider would otherwise have been."""
    for obj, start, angle, beats, _ in _random_sliders():
        straight = HitObject("slider", time=0.0, end_time=obj.end_time)
        generate_slider(straight, _grid(), start=start,
                        direction=(math.cos(angle), math.sin(angle)))
        assert obj.slides == straight.slides
        assert obj.pixel_length == pytest.approx(straight.pixel_length)
        assert obj.slider_beats == pytest.approx(beats)


def test_a_single_slide_that_fits_lasts_exactly_its_beats():
    velocity = 100.0 * DEFAULT_SLIDER_MULTIPLIER
    obj = HitObject("slider", time=0.0, end_time=0.5)       # 1 beat
    generate_slider(obj, _grid(), start=(256.0, 192.0), direction=(1.0, 0.2),
                    rng=np.random.default_rng(0), max_bend_degrees=130.0,
                    diameter=DIAMETER, clearance=0.85 * DIAMETER)
    assert obj.pixel_length * obj.slides == pytest.approx(velocity)


def test_bodies_stay_on_the_playfield_and_curves_inside_the_margin():
    for obj, *_ in _random_sliders(seed=1):
        pts = np.array(slider_body_points(obj))
        assert pts[:, 0].min() >= 0 and pts[:, 0].max() <= PLAYFIELD_W
        assert pts[:, 1].min() >= 0 and pts[:, 1].max() <= PLAYFIELD_H
        if slider_shape(obj) != STRAIGHT:
            slack = 1.5
            assert pts[:, 0].min() >= PLAYFIELD_MARGIN - slack
            assert pts[:, 0].max() <= PLAYFIELD_W - PLAYFIELD_MARGIN + slack
            assert pts[:, 1].min() >= PLAYFIELD_MARGIN - slack
            assert pts[:, 1].max() <= PLAYFIELD_H - PLAYFIELD_MARGIN + slack


def test_curves_leave_the_head_along_the_incoming_heading():
    """No kink: a curve starts off exactly the way the cursor arrived."""
    checked = 0
    for obj, start, angle, *_ in _random_sliders(seed=2):
        if slider_shape(obj) == STRAIGHT:
            continue
        body = slider_body_points(obj)
        # Measured over the first sample (~4 px). On an arc of radius r that
        # chord already points half its subtended angle off the tangent, so
        # allow exactly that, plus a little for anchor rounding.
        chord = math.dist(body[0], body[1])
        allowance = math.radians(3)
        if obj.curve_type == "P":
            (ax, ay), (bx, by), (cx, cy) = obj.path
            area2 = abs((bx - ax) * (cy - ay) - (cx - ax) * (by - ay))
            radius = (math.dist(obj.path[0], obj.path[1]) * math.dist(obj.path[1], obj.path[2])
                      * math.dist(obj.path[0], obj.path[2])) / (2 * area2)
            allowance += math.asin(min(1.0, chord / (2 * radius)))
        else:
            allowance += math.radians(3)
        assert _angle_between(_heading(body[0], body[1]), angle) < allowance
        checked += 1
    assert checked > 50


def test_a_curve_is_never_worse_at_clearance_than_the_straight_it_replaces():
    for obj, start, angle, beats, avoid in _random_sliders(seed=3):
        if slider_shape(obj) == STRAIGHT:
            continue
        straight = HitObject("slider", time=0.0, end_time=obj.end_time)
        generate_slider(straight, _grid(), start=start,
                        direction=(math.cos(angle), math.sin(angle)))
        curve_ratio = clearance_ratio(slider_body_points(obj), avoid, 0.85 * DIAMETER)
        straight_ratio = clearance_ratio(slider_body_points(straight), avoid, 0.85 * DIAMETER)
        assert curve_ratio >= min(1.0, straight_ratio) - 1e-9


def test_a_curve_swings_away_from_an_object_it_would_otherwise_cross():
    """An object sits where an arc bending one way would pass over it: any
    curve chosen must bend the other way (or not at all)."""
    grid = _grid()
    for seed in range(30):
        obj = HitObject("slider", time=0.0, end_time=0.5)       # 1 beat: 140 px, one slide
        # heading right from the middle; an object up-right of the head, where
        # a left-bending (negative y in osu! space) arc would sweep
        blocker = ("point", (256.0 + 70.0, 192.0 - 55.0))
        generate_slider(obj, grid, start=(256.0, 192.0), direction=(1.0, 0.0),
                        rng=np.random.default_rng(seed), curl=-1.0, max_bend_degrees=130.0,
                        diameter=DIAMETER, avoid=[blocker], clearance=0.85 * DIAMETER)
        body = np.array(slider_body_points(obj))
        head_gap = math.dist(body[0], blocker[1])
        assert np.linalg.norm(body - np.array(blocker[1]), axis=1).min() >= \
            min(head_gap, 0.85 * DIAMETER) - 2.5


def test_exit_heading_follows_the_curve():
    grid = _grid()
    obj = HitObject("slider", time=0.0, end_time=0.5)
    obj.curve_type, obj.slides = "P", 1
    obj.path = [(float(round(p[0])), float(round(p[1])))
                for p in _arc((200.0, 200.0), 0.0, 1, math.radians(90), 142.0)[1]]
    obj.pixel_length = 140.0
    # a quarter-circle leaving heading +x, bending toward +y, exits heading ~+y
    assert _angle_between(slider_exit_heading(obj), math.pi / 2) < math.radians(8)
    obj.slides = 2
    # a repeat ends back at the head, travelling back out of it (-x)
    assert _angle_between(slider_exit_heading(obj), math.pi) < math.radians(8)
    assert math.dist(slider_end_position(obj), obj.path[0]) < 1e-6


# --- tiers ------------------------------------------------------------------

def test_shape_menu_by_tier():
    long_seg, d = 4 * DIAMETER, DIAMETER
    easy = _shape_weights(PRESETS["Easy"].max_turn_degrees, long_seg, d, 1)
    normal = _shape_weights(PRESETS["Normal"].max_turn_degrees, long_seg, d, 1)
    hard = _shape_weights(PRESETS["Hard"].max_turn_degrees, long_seg, d, 1)
    assert set(easy) == {STRAIGHT, ARC}
    assert set(normal) == {STRAIGHT, ARC, S_CURVE}
    assert set(hard) == {STRAIGHT, ARC, S_CURVE, CORNER}
    # repeats and short sliders only ever straighten or arc
    assert set(_shape_weights(130.0, long_seg, d, 3)) == {STRAIGHT, ARC}
    assert set(_shape_weights(130.0, 0.8 * d, d, 1)) == {STRAIGHT, ARC}


def test_no_arc_on_a_longer_slider_curls_tighter_than_the_minimum_radius():
    """Short sliders are exempt: their body sits mostly under their own
    head and tail circles, so a tight curl there doesn't fold into a blob."""
    from mapping.slider_generator import MIN_ARC_RADIUS_DIAMETERS, SHORT_SLIDER_DIAMETERS
    checked = 0
    for obj, *_ in _random_sliders(seed=6):
        if slider_shape(obj) != ARC or obj.pixel_length < SHORT_SLIDER_DIAMETERS * DIAMETER:
            continue
        body = slider_body_points(obj)
        bend = _angle_between(_heading(*body[:2]), _heading(*body[-2:]))
        if bend > math.radians(1):
            assert obj.pixel_length / bend >= MIN_ARC_RADIUS_DIAMETERS * DIAMETER * 0.95
            checked += 1
    assert checked > 20


def test_easy_arcs_stay_gentle():
    for obj, *_ in _random_sliders(seed=4, max_bend=PRESETS["Easy"].max_turn_degrees):
        assert slider_shape(obj) in (STRAIGHT, ARC)
        if slider_shape(obj) == ARC:
            turn = _angle_between(_heading(*slider_body_points(obj)[:2]),
                                  _heading(*slider_body_points(obj)[-2:]))
            # Easy's own limit, or a wall-escape arc (capped at 90 degrees
            # on the gentle tiers)
            assert turn <= math.radians(91)


def test_real_maps_use_a_variety_of_shapes():
    from tests.test_difficulty import _synth_track
    track = _synth_track()
    seen = {}
    for name in TIER_ORDER:
        objs, _ = build_map(track, PRESETS[name])
        seen[name] = {slider_shape(o) for o in objs if o.kind == "slider"}
    assert not ({S_CURVE, CORNER} & seen["Easy"])
    harder = set().union(*(seen[n] for n in ("Hard", "Insane", "Expert")))
    assert len(harder) >= 3, seen


# --- writer + compatibility --------------------------------------------------

@pytest.mark.parametrize("curve_type,anchors,prefix", [
    ("P", [(100.0, 100.0), (150.0, 80.0), (200.0, 100.0)], ",P|150:80|200:100,"),
    ("B", [(100.0, 100.0), (130.0, 100.0), (170.0, 140.0), (200.0, 140.0)], ",B|130:100|170:140|200:140,"),
    ("L", [(100.0, 100.0), (150.0, 100.0), (150.0, 150.0)], ",L|150:100|150:150,"),
])
def test_writer_emits_the_curve_type(curve_type, anchors, prefix):
    obj = HitObject("slider", time=1.0, x=100, y=100, end_time=1.5,
                    path=anchors, slides=1, pixel_length=100.0, curve_type=curve_type)
    assert prefix in hit_object_line(obj)


def test_without_an_rng_sliders_are_the_original_straight_ones():
    grid = _grid()
    obj = HitObject("slider", time=0.0, end_time=0.5)
    generate_slider(obj, grid, start=(256, 192), direction=(1.0, 0.0))
    assert obj.curve_type == "L" and len(obj.path) == 2
    assert obj.path[1] == pytest.approx((256 + 140.0, 192))


def test_a_head_on_the_margin_heading_out_has_no_room():
    """Regression: the zero limit used to be discarded, so a head sitting
    exactly on the margin line got the full cap and ran off the playfield."""
    assert room_ahead((300.0, PLAYFIELD_MARGIN), (0.0, -1.0), 240.0) == 0.0
    assert room_ahead((PLAYFIELD_MARGIN, 200.0), (-1.0, 0.0), 240.0) == 0.0
    obj = HitObject("slider", time=0.0, end_time=0.5)
    generate_slider(obj, _grid(), start=(325.0, float(PLAYFIELD_MARGIN)), direction=(0.3, -1.0))
    pts = np.array(slider_body_points(obj))
    assert pts[:, 1].min() >= 0
