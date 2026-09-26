"""
src/mapping/slider_generator.py

Turns a slider-typed HitObject (which so far only knows its start time and
end time) into concrete osu! slider geometry: a curve, a per-slide pixel
length, and a slide count.

osu! slider timing model
------------------------
A slider's duration is determined entirely by its length and the map's
velocity, not stored directly:

    velocity (px per beat) = 100 * SliderMultiplier * SV
    duration (beats)       = pixel_length * slides / velocity

So to make a slider last `slider_beats` beats we need
`pixel_length * slides = slider_beats * velocity`. We find how long one
slide can be on the playfield, then choose `slides` so the total path
length matches the intended duration. `SliderMultiplier` is a map-wide
constant (osu! default 1.4); `SV` (a per-object multiplier, default 1.0)
is where Step 5 will later dial slider speed per difficulty.

Slider shapes
-------------
The timing above fixes only how LONG one slide is. What it looks like is
free, and osu! maps use a handful of shapes, each stored as a curve type
plus anchor points (the osu! wiki, "osu! (file format)" -> curves):

    straight  "L", 2 anchors     head -> tail in a line
    arc       "P", 3 anchors     a perfect-circle arc through all three
    s-curve   "B", 4 anchors     a cubic bezier that swings out to one side
                                 and back, leaving parallel to how it came in
    corner    "L", 3 anchors     two straight legs with an elbow ("red
                                 anchor" slider)

Whatever the shape, the game samples the curve and then CUTS it to exactly
`pixel_length` (or stretches its last segment if it is shorter). So every
shape here is built a little longer than it needs to be, scaled so its
sampled length is `pixel_length` plus a small overshoot: the client always
trims, never stretches, and the duration is exactly what the timing needs.

Choosing a shape (the part that keeps maps readable)
-----------------------------------------------------
Straight sliders are always available. The others are offered by tier,
bounded by the preset's `max_turn_degrees` (how sharply flow may turn):

  * every curve LEAVES THE HEAD ALONG THE INCOMING HEADING, so the cursor
    enters the slider without a kink -- the same continuity a straight
    slider has. Arcs and corners bend toward the side the pattern is
    already curving, so a slider continues the flow instead of fighting it.
  * every candidate, straight included, is judged on the body the game
    will draw: it must stay inside the playfield margin, never come closer
    to a recent object (or slider body) than its own head already is --
    i.e. it can swing away from what came before but never back across
    it -- and leave the cursor a diameter of room to exit into, so the
    next object isn't forced back over this body. The first candidate
    that passes all three is used. If none does, the one that fails least
    is (bounds, then clearance, then exit room), the straight slider on a
    tie -- so a curve only ever replaces a straight slider that was worse.
  * a straight slider that would end facing a wall is the one case where
    an arc is tried before anything else: bending along the wall is what a
    mapper does there, and what keeps the next object off the body.
  * gentle tiers get gentle arcs only; S-curves need Normal+ turn freedom
    and corners Hard+. Repeat sliders and very short sliders only curve
    gently, and no arc on a longer slider curls tighter than
    MIN_ARC_RADIUS_DIAMETERS -- a body a circle wide bent tighter than
    that reads as a blob.

Calling generate_slider() without an `rng` gives the original behaviour:
straight sliders only.
"""

import math
import os
import sys

import numpy as np

sys.path.append(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from mapping.hit_object import (
    HitObject, PLAYFIELD_W, PLAYFIELD_H, PLAYFIELD_MARGIN,
)


DEFAULT_SLIDER_MULTIPLIER = 1.4
MAX_SLIDE_PX = 240.0      # longest single slide we'll draw
MIN_SLIDE_PX = 40.0       # shortest single slide worth drawing

STRAIGHT, ARC, S_CURVE, CORNER = "straight", "arc", "s-curve", "corner"
SHAPES = (STRAIGHT, ARC, S_CURVE, CORNER)

# Curves are built this much longer than pixel_length (and at least
# CURVE_OVERSHOOT_MIN_PX longer, to absorb anchors being rounded to whole
# pixels) so the game trims them rather than stretching the last segment.
CURVE_OVERSHOOT = 1.01
CURVE_OVERSHOOT_MIN_PX = 3.0

# Must match the client's sampler (frontend .../HitObjects/SliderPath.cs):
# the body checked for overlaps here is then the body actually drawn.
SAMPLE_SPACING_PX = 4.0
MIN_SAMPLES, MAX_SAMPLES = 4, 400
_COLLINEAR_EPSILON = 0.001

# Shape limits.
ARC_MAX_DEGREES = 150.0         # past this the tail curls back toward the head
ARC_MIN_DEGREES = 20.0          # below this an arc is just a wobbly line
ARC_TURN_SCALE = 1.15           # arc bend allowed per degree of max_turn
MIN_ARC_RADIUS_DIAMETERS = 0.75 # no arc on a slider longer than SHORT_SLIDER_DIAMETERS
                                # curls tighter than this: the body (a circle wide)
                                # would fold into a blob. A shorter slider's body is
                                # mostly under its own head and tail anyway.
WALL_ESCAPE_DEGREES = (60.0, 90.0, 120.0)  # arcs tried when a straight would end facing a wall,
WALL_ESCAPE_GENTLE_CAP = 90.0   # capped at this -- or the tier's own arc limit, if higher
S_CURVE_MIN_TURN = 55.0         # tiers below this (Easy) get no S-curves
CORNER_MIN_TURN = 75.0          # tiers below this (Easy/Normal) get no corners
CORNER_MAX_DEGREES = 90.0       # a sharper elbow folds the slider back on itself
S_CURVE_TANGENT = 0.45          # bezier handle length, as a fraction of reach
REPEAT_BEND_SCALE = 0.6         # repeat sliders only curve gently
SHORT_SLIDER_DIAMETERS = 1.1    # below this a slider only straightens or arcs gently
MIN_CHORD_RATIO = 0.55          # tail must end at least this far (x length) from the head
CLEARANCE_TOLERANCE_PX = 2.0    # slack on "never closer than the head already is"
BOUNDS_TOLERANCE_PX = 1.0       # slack on the margin box, for anchor rounding


# ------------------------------------------------------------------
# Geometry helpers
# ------------------------------------------------------------------

def room_ahead(start, direction, cap: float) -> float:
    """
    Distance you can travel from `start` along `direction` (unit vector)
    before crossing the playfield margin box, capped at `cap`.
    """
    px, py = start
    dx, dy = direction
    limits = [cap]
    if dx > 1e-9:
        limits.append((PLAYFIELD_W - PLAYFIELD_MARGIN - px) / dx)
    elif dx < -1e-9:
        limits.append((PLAYFIELD_MARGIN - px) / dx)
    if dy > 1e-9:
        limits.append((PLAYFIELD_H - PLAYFIELD_MARGIN - py) / dy)
    elif dy < -1e-9:
        limits.append((PLAYFIELD_MARGIN - py) / dy)
    # A limit of 0 (sitting on the margin, heading out) or below (already
    # past it, heading further out) means blocked -- it must not be dropped,
    # or a head placed exactly on the margin line gets the full cap and the
    # slider runs straight off the playfield.
    return max(0.0, min(limits))


def _unit(angle: float) -> np.ndarray:
    return np.array([math.cos(angle), math.sin(angle)])


def _polyline_length(pts) -> float:
    pts = np.asarray(pts, dtype=float)
    if len(pts) < 2:
        return 0.0
    return float(np.linalg.norm(np.diff(pts, axis=0), axis=1).sum())


def _sample_count(length: float) -> int:
    return int(np.clip(math.ceil(length / SAMPLE_SPACING_PX), MIN_SAMPLES, MAX_SAMPLES))


def points_to_polyline_distance(points, polyline) -> np.ndarray:
    """Shortest distance from each of `points` (N x 2) to the polyline (M x 2)."""
    p = np.atleast_2d(np.asarray(points, dtype=float))
    poly = np.atleast_2d(np.asarray(polyline, dtype=float))
    if len(poly) == 1:
        return np.linalg.norm(p - poly[0], axis=1)
    a, b = poly[:-1], poly[1:]
    ab = b - a
    denom = np.maximum((ab * ab).sum(axis=1), 1e-12)
    ap = p[:, None, :] - a[None, :, :]
    t = np.clip((ap * ab[None]).sum(axis=2) / denom[None], 0.0, 1.0)
    nearest = a[None] + t[..., None] * ab[None]
    return np.linalg.norm(p[:, None, :] - nearest, axis=2).min(axis=1)


def polyline_distance(p, polyline) -> float:
    """Shortest distance from one point to a polyline (e.g. a slider body)."""
    return float(points_to_polyline_distance([p], polyline)[0])


# ------------------------------------------------------------------
# Curve sampling -- a mirror of the client's SliderPath.cs
# ------------------------------------------------------------------

def _bezier_at(control, t: float) -> np.ndarray:
    work = np.array(control, dtype=float)
    for level in range(len(work) - 1, 0, -1):
        work[:level] = work[:level] + (work[1:level + 1] - work[:level]) * t
    return work[0]


def _sample_bezier_segments(anchors) -> list:
    """A repeated anchor splits the bezier into segments (a hard corner)."""
    result = [tuple(anchors[0])]
    segment = [anchors[0]]
    segments = []
    for i in range(1, len(anchors)):
        if tuple(anchors[i]) == tuple(anchors[i - 1]):
            segments.append(segment)
            segment = [anchors[i]]
            continue
        segment.append(anchors[i])
    segments.append(segment)
    for seg in segments:
        if len(seg) < 2:
            continue
        steps = _sample_count(_polyline_length(seg))
        for i in range(1, steps + 1):
            result.append(tuple(_bezier_at(seg, i / steps)))
    return result


def _sample_arc(a, b, c):
    """Points along the circle through a, b, c from a to c via b; None if collinear."""
    a, b, c = (np.asarray(v, dtype=float) for v in (a, b, c))
    a_sq = b @ b - c @ c
    b_sq = a @ a - c @ c
    det = 2 * ((b[0] - c[0]) * (a[1] - c[1]) - (a[0] - c[0]) * (b[1] - c[1]))
    if abs(det) < _COLLINEAR_EPSILON:
        return None
    centre = np.array([(a_sq * (a[1] - c[1]) - b_sq * (b[1] - c[1])) / det,
                       (b_sq * (b[0] - c[0]) - a_sq * (a[0] - c[0])) / det])
    radius = float(np.linalg.norm(a - centre))
    start = math.atan2(a[1] - centre[1], a[0] - centre[0])
    mid = math.atan2(b[1] - centre[1], b[0] - centre[0])
    end = math.atan2(c[1] - centre[1], c[0] - centre[0])
    while mid < start:
        mid += 2 * math.pi
    while end < start:
        end += 2 * math.pi
    if mid > end:
        end -= 2 * math.pi
    steps = _sample_count(abs(end - start) * radius)
    return [tuple(centre + radius * _unit(start + (end - start) * i / steps))
            for i in range(steps + 1)]


def sample_curve(curve_type: str, anchors) -> list:
    """The untrimmed curve an osu! client draws for these anchors."""
    anchors = [tuple(map(float, p)) for p in anchors]
    if len(anchors) < 2 or curve_type == "L":
        return anchors
    if curve_type == "P":
        if len(anchors) == 3:
            arc = _sample_arc(*anchors)
            return arc if arc is not None else anchors
        return _sample_bezier_segments(anchors)
    return _sample_bezier_segments(anchors)


def trim_to_length(sampled, length: float) -> list:
    """Cut the sampled curve to `length`, or extend its last segment to reach it."""
    result = [tuple(sampled[0])]
    travelled = 0.0
    for i in range(1, len(sampled)):
        p0, p1 = np.asarray(sampled[i - 1]), np.asarray(sampled[i])
        seg = float(np.linalg.norm(p1 - p0))
        if seg <= 0:
            continue
        if travelled + seg >= length:
            t = (length - travelled) / seg
            result.append(tuple(p0 + (p1 - p0) * t))
            return result
        travelled += seg
        result.append(tuple(p1))
    if len(result) >= 2 and travelled < length:
        last, prev = np.asarray(result[-1]), np.asarray(result[-2])
        d = last - prev
        n = float(np.linalg.norm(d))
        if n > 0:
            result.append(tuple(last + d / n * (length - travelled)))
    return result


def slider_body_points(obj: HitObject) -> list:
    """One slide of the slider's body, as the game draws it: head -> tail."""
    if not obj.path:
        return [(float(obj.x), float(obj.y))]
    if len(obj.path) < 2 or not obj.pixel_length:
        return [tuple(map(float, obj.path[0]))]
    return trim_to_length(sample_curve(obj.curve_type, obj.path), float(obj.pixel_length))


def slider_shape(obj: HitObject) -> str:
    """Which of SHAPES this slider's curve is."""
    if obj.curve_type == "P":
        return ARC
    if obj.curve_type == "B":
        return S_CURVE
    return CORNER if obj.path and len(obj.path) > 2 else STRAIGHT


def _heading_along(body, from_end: bool) -> float:
    """Direction of travel at the tail (from_end) or out of the head, in radians."""
    pts = np.asarray(body, dtype=float)
    if len(pts) < 2:
        return 0.0
    if from_end:
        tip, rest = pts[-1], pts[-2::-1]
    else:
        tip, rest = pts[0], pts[1:]
    # look a few pixels along, so a tiny final trim segment doesn't skew it
    for q in rest:
        if np.linalg.norm(q - tip) >= SAMPLE_SPACING_PX:
            break
    d = (tip - q) if from_end else (q - tip)
    return math.atan2(d[1], d[0])


def slider_end_position(obj: HitObject) -> tuple:
    """
    Where the cursor is left after the slider finishes: the tail of the
    body on an odd slide count, back at the head on an even one.
    """
    if not obj.path:
        return (obj.x, obj.y)
    body = slider_body_points(obj)
    return body[-1] if obj.slides % 2 == 1 else body[0]


def slider_exit_heading(obj: HitObject) -> float:
    """
    The direction the cursor is moving as the slider finishes -- the flow
    the next object should continue. Along the tail on an odd slide count;
    on an even one the ball ends travelling back out of the head.
    """
    body = slider_body_points(obj)
    if obj.slides % 2 == 1:
        return _heading_along(body, from_end=True)
    return _heading_along(body, from_end=False) + math.pi


# ------------------------------------------------------------------
# Shape builders: (curve_type, anchors) for a curve of natural length
# `length` leaving `start` at angle `a`, bending toward side `s` (+1/-1).
# ------------------------------------------------------------------

def _straight(start, a, length):
    s = np.asarray(start, dtype=float)
    return "L", [s, s + _unit(a) * length]


def _arc(start, a, s, theta, length):
    radius = length / theta
    start = np.asarray(start, dtype=float)
    centre = start + radius * _unit(a + s * math.pi / 2)
    a0 = a - s * math.pi / 2
    at = lambda phi: centre + radius * _unit(a0 + s * phi)
    return "P", [start, at(theta / 2), at(theta)]


def _s_curve(start, a, s, lateral, length):
    t, n = _unit(a), _unit(a + s * math.pi / 2)
    unit = [np.zeros(2), t * S_CURVE_TANGENT,
            t + n * lateral - t * S_CURVE_TANGENT, t + n * lateral]
    scale = length / _polyline_length(_sample_bezier_segments(unit))
    start = np.asarray(start, dtype=float)
    return "B", [start + p * scale for p in unit]


def _corner(start, a, s, phi, split, length):
    start = np.asarray(start, dtype=float)
    elbow = start + _unit(a) * length * split
    return "L", [start, elbow, elbow + _unit(a + s * phi) * length * (1 - split)]


def _shape_weights(max_bend: float, seg: float, diameter: float, slides: int) -> dict:
    """Relative odds of each shape for this slider, before any fit check."""
    if seg < SHORT_SLIDER_DIAMETERS * diameter:
        return {STRAIGHT: 1.0, ARC: 0.6}
    weights = {STRAIGHT: 1.0, ARC: 1.2}
    if slides == 1 and max_bend >= S_CURVE_MIN_TURN and seg >= 1.5 * diameter:
        weights[S_CURVE] = 0.6 if max_bend < CORNER_MIN_TURN else 0.8
    if slides == 1 and max_bend >= CORNER_MIN_TURN and seg >= 2.0 * diameter:
        weights[CORNER] = 0.35 if max_bend < 100 else 0.5
    return weights


def _arc_limit_degrees(length: float, diameter: float) -> float:
    """The most an arc of this length may bend without curling tighter than MIN_ARC_RADIUS."""
    if length < SHORT_SLIDER_DIAMETERS * diameter:
        return ARC_MAX_DEGREES
    return min(ARC_MAX_DEGREES, math.degrees(length / (MIN_ARC_RADIUS_DIAMETERS * diameter)))


def _candidates(shape, start, a, curl, length, max_bend, seg, diameter, slides, rng):
    """A few concrete variants of one shape: preferred side first, then the other, then gentler."""
    if shape == STRAIGHT:
        return [_straight(start, a, length)]

    if shape == ARC:
        limit = min(ARC_MAX_DEGREES, max(ARC_MIN_DEGREES + 10, max_bend * ARC_TURN_SCALE))
        if slides > 1:
            limit *= REPEAT_BEND_SCALE
        if seg < SHORT_SLIDER_DIAMETERS * diameter:
            limit = min(limit, 60.0)
        limit = min(limit, _arc_limit_degrees(length, diameter))
        if limit < ARC_MIN_DEGREES:
            return []           # too short to bend readably
        theta = math.radians(max(ARC_MIN_DEGREES, float(rng.uniform(0.4, 1.0)) * limit))
        gentle = math.radians(max(ARC_MIN_DEGREES, math.degrees(theta) * 0.5))
        return [_arc(start, a, curl, theta, length), _arc(start, a, -curl, theta, length),
                _arc(start, a, curl, gentle, length)]

    if shape == S_CURVE:
        lateral = float(rng.uniform(0.18, 0.38))
        return [_s_curve(start, a, curl, lateral, length),
                _s_curve(start, a, -curl, lateral, length),
                _s_curve(start, a, curl, lateral * 0.5, length)]

    # corner
    phi = math.radians(float(rng.uniform(35.0, min(CORNER_MAX_DEGREES, max_bend * 0.8))))
    split = float(rng.uniform(0.4, 0.6))
    return [_corner(start, a, curl, phi, split, length),
            _corner(start, a, -curl, phi, split, length)]


def _history_distance(points, entry) -> np.ndarray:
    if entry[0] == "point":
        return np.linalg.norm(np.asarray(points, dtype=float) - np.asarray(entry[1]), axis=1)
    return points_to_polyline_distance(points, entry[1])


def clearance_ratio(body, avoid, clearance: float) -> float:
    """
    How well a slider body keeps off recent geometry, capped at 1.0 (= fine).

    The rule: the body may never come closer to a recent object or slider
    body than its own head already is (or than `clearance`, if the head is
    further off than that). A body may lead AWAY from what came before --
    the head is necessarily near the previous object -- but never swing
    back across it. `avoid` holds placement history entries,
    ("point", (x, y)) or ("body", [points]).
    """
    pts = np.atleast_2d(np.asarray(body, dtype=float))
    ratio = 1.0
    for entry in avoid:
        head = float(_history_distance(pts[:1], entry)[0])
        required = min(head, clearance) - CLEARANCE_TOLERANCE_PX
        if required > 1e-9:
            ratio = min(ratio, float(_history_distance(pts, entry).min()) / required)
    return min(1.0, ratio)


def straight_lane(start, direction, length: float) -> np.ndarray:
    """Points along a straight slider body from `start`, one per sample spacing."""
    n = max(2, int(math.ceil(length / SAMPLE_SPACING_PX)) + 1)
    d = np.asarray(direction, dtype=float)
    d = d / (np.linalg.norm(d) or 1.0)
    return np.asarray(start, dtype=float) + np.outer(np.linspace(0.0, length, n), d)


def _inside_margin(pts) -> bool:
    lo, t = PLAYFIELD_MARGIN - BOUNDS_TOLERANCE_PX, BOUNDS_TOLERANCE_PX
    pts = np.atleast_2d(pts)
    return bool(pts[:, 0].min() >= lo and pts[:, 0].max() <= PLAYFIELD_W - PLAYFIELD_MARGIN + t
                and pts[:, 1].min() >= lo and pts[:, 1].max() <= PLAYFIELD_H - PLAYFIELD_MARGIN + t)


def _assess(body, seg, slides, avoid, clearance, diameter) -> tuple:
    """
    How well a candidate body fits, best first when compared:
    (drawable, clearance ratio capped at 1, exit room). A candidate fits
    outright when it scores (True, 1.0, True).

      drawable   -- inside the margin box and not curled onto itself
      clearance  -- never comes closer to a recent object or slider body
                    than its own head already is (or the standard clearance)
      exit room  -- the cursor has a diameter of playfield ahead of it when
                    the slider finishes, so the next object isn't forced
                    back onto this body or against a wall
    """
    pts = np.asarray(body, dtype=float)
    drawable = (_inside_margin(pts)
                and np.linalg.norm(pts[-1] - pts[0]) >= MIN_CHORD_RATIO * seg)

    ratio = clearance_ratio(pts, avoid, clearance)

    if slides % 2 == 1:
        exit_at, exit_dir = pts[-1], _heading_along(pts, from_end=True)
    else:
        exit_at, exit_dir = pts[0], _heading_along(pts, from_end=False) + math.pi
    exit_room = _inside_margin(exit_at + _unit(exit_dir) * diameter)

    return (bool(drawable), min(1.0, ratio), bool(exit_room))


def generate_slider(obj: HitObject, grid, start, direction,
                     sv_multiplier: float = 1.0,
                     slider_multiplier: float = DEFAULT_SLIDER_MULTIPLIER,
                     rng=None, curl: float = 1.0, max_bend_degrees: float = 0.0,
                     diameter: float = 0.0, avoid=(), clearance: float = 0.0) -> HitObject:
    """
    Fill in `obj.path`, `obj.curve_type`, `obj.pixel_length`, `obj.slides`
    and `obj.slider_beats` for a slider whose head is at `start`.

    Args:
        obj: a HitObject with kind == "slider" and end_time set
        grid: the BeatGrid (for beat_period)
        start: (x, y) head position, already inside the playfield
        direction: (dx, dy) the cursor arrives from; need not be normalized.
            Every shape leaves the head along it.
        sv_multiplier: per-object slider-velocity multiplier (Step 5 knob)
        slider_multiplier: map-wide SliderMultiplier
        rng: numpy Generator. None = straight sliders only (the original
            behaviour); given, the shape is picked as described above.
        curl: +1 / -1, the side the pattern is currently bending toward
        max_bend_degrees: the tier's max_turn_degrees; bounds how sharply a
            curve may bend and which shapes are offered at all
        diameter: circle diameter in osu!pixels (short-slider threshold)
        avoid: recent geometry the body must not swing back across, as the
            placement history entries ("point", (x, y)) / ("body", [pts])
        clearance: the standard distance to keep from those, in osu!pixels
    """
    beats = max(0.25, (obj.end_time - obj.time) / grid.beat_period)
    velocity = 100.0 * slider_multiplier * sv_multiplier   # px per beat
    total_px = beats * velocity

    dx, dy = direction
    norm = math.hypot(dx, dy) or 1.0
    dx, dy = dx / norm, dy / norm
    incoming = math.atan2(dy, dx)

    room = room_ahead(start, (dx, dy), MAX_SLIDE_PX)
    if room < MIN_SLIDE_PX:
        # Blocked against an edge. Sweep outward for the nearest heading
        # with room instead of flipping a full 180 degrees -- a 180 would
        # drag the slider body straight back over the object we just came
        # from, which is exactly the kind of unreadable overlap we avoid.
        base = math.atan2(dy, dx)
        best = (room, dx, dy)
        for step_deg in (30, -30, 60, -60, 90, -90, 120, -120, 150, -150, 180):
            angle = base + math.radians(step_deg)
            cdx, cdy = math.cos(angle), math.sin(angle)
            candidate_room = room_ahead(start, (cdx, cdy), MAX_SLIDE_PX)
            if candidate_room >= MIN_SLIDE_PX:
                best = (candidate_room, cdx, cdy)
                break
            if candidate_room > best[0]:
                best = (candidate_room, cdx, cdy)
        room, dx, dy = best
    room = max(room, MIN_SLIDE_PX)

    # choose slide count so total path length ~= intended duration,
    # keeping each slide within the room we actually have
    slides = max(1, round(total_px / room))
    seg = total_px / slides
    if seg > room:                       # still too long: accept a small
        seg = room                       # duration error rather than clip
    seg = max(seg, MIN_SLIDE_PX)

    # The straight slider: always valid on the playfield, and the fallback.
    end = (start[0] + dx * seg, start[1] + dy * seg)
    chosen = ("L", [(float(start[0]), float(start[1])), (float(end[0]), float(end[1]))])

    if rng is not None and max_bend_degrees > 0:
        deflected = abs(math.remainder(math.atan2(dy, dx) - incoming, 2 * math.pi)) > math.radians(1)
        chosen = _choose_shape(start, incoming, curl, seg, slides, rng,
                               max_bend_degrees, diameter or seg, avoid,
                               clearance, straight=chosen, deflected=deflected)

    obj.curve_type, obj.path = chosen[0], [tuple(map(float, p)) for p in chosen[1]]
    obj.pixel_length = float(seg)
    obj.slides = int(slides)
    obj.slider_beats = float(beats)
    return obj


def _choose_shape(start, incoming, curl, seg, slides, rng, max_bend, diameter,
                  avoid, clearance, straight, deflected):
    """
    Try shapes in a weighted-random order and take the first that fits
    outright. If none does, take whichever candidate fits best -- the
    straight slider on a tie, since that is what this always produced.

    A straight slider that had to be turned away from a wall (`deflected`)
    kinks the flow at its head, which a curve never does, so in that case
    every curve is tried before it.
    """
    weights = _shape_weights(max_bend, seg, diameter, slides)
    names = list(weights)
    p = np.array([weights[n] for n in names], dtype=float)
    order = [str(n) for n in rng.choice(names, size=len(names), replace=False, p=p / p.sum())]
    if deflected:
        order = [n for n in order if n != STRAIGHT] + [STRAIGHT]

    curl = 1.0 if curl >= 0 else -1.0
    length = max(seg * CURVE_OVERSHOOT, seg + CURVE_OVERSHOOT_MIN_PX)
    head = (float(round(start[0])), float(round(start[1])))

    straight_score = _assess(trim_to_length(sample_curve(*straight), seg), seg, slides,
                             avoid, clearance, diameter)
    best, best_score = straight, straight_score

    # A straight slider that would end facing a wall leaves the next object
    # nowhere to go but back over it. A mapper bends that slider along the
    # wall instead, so try doing exactly that first: arcs turning away,
    # either way round, however gentle the tier -- still leaving the head
    # along the incoming heading, so still no kink.
    escapes = []
    if not straight_score[2]:
        tier_arc = min(ARC_MAX_DEGREES, max_bend * ARC_TURN_SCALE)
        cap = min(_arc_limit_degrees(length, diameter),
                  max(WALL_ESCAPE_GENTLE_CAP, tier_arc))
        bends = sorted({min(bend, cap) for bend in WALL_ESCAPE_DEGREES
                        if min(bend, cap) >= ARC_MIN_DEGREES})
        escapes = [_arc(head, incoming, side, math.radians(bend), length)
                   for bend in bends for side in (curl, -curl)]

    for shape in [None] + order:
        if shape == STRAIGHT:
            if straight_score == (True, 1.0, True):
                return straight
            continue
        options = escapes if shape is None else _candidates(
            shape, head, incoming, curl, length, max_bend, seg, diameter, slides, rng)
        for curve_type, anchors in options:
            anchors = [(float(round(x)), float(round(y))) for x, y in anchors]
            sampled = sample_curve(curve_type, anchors)
            if _polyline_length(sampled) < seg:
                continue            # rounding left it short: it would be stretched
            score = _assess(trim_to_length(sampled, seg), seg, slides, avoid,
                            clearance, diameter)
            if not score[0]:
                continue            # off the playfield or curled up: never used
            if score == (True, 1.0, True):
                return curve_type, anchors
            if score > best_score:
                best, best_score = (curve_type, anchors), score
    return best
