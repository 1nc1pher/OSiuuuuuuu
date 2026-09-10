"""
src/mapping/slider_generator.py

Turns a slider-typed HitObject (which so far only knows its start time and
end time) into concrete osu! slider geometry: a path, a per-slide pixel
length, and a slide count.

osu! slider timing model
------------------------
A slider's duration is determined entirely by its length and the map's
velocity, not stored directly:

    velocity (px per beat) = 100 * SliderMultiplier * SV
    duration (beats)       = pixel_length * slides / velocity

So to make a slider last `slider_beats` beats we need
`pixel_length * slides = slider_beats * velocity`. We pick a path segment
that fits inside the playfield, then choose `slides` so the total path
length matches the intended duration. `SliderMultiplier` is a map-wide
constant (osu! default 1.4); `SV` (a per-object multiplier, default 1.0)
is where Step 5 will later dial slider speed per difficulty.

We only generate straight ("L") sliders here. Curved paths are pure
polish and can be layered on later without touching the timing math.
"""

import math
import os
import sys

sys.path.append(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from mapping.hit_object import (
    HitObject, PLAYFIELD_W, PLAYFIELD_H, PLAYFIELD_MARGIN,
)


DEFAULT_SLIDER_MULTIPLIER = 1.4
MAX_SLIDE_PX = 240.0      # longest single slide we'll draw
MIN_SLIDE_PX = 40.0       # shortest single slide worth drawing


def _max_segment(start, direction, cap: float) -> float:
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
    limits = [t for t in limits if t > 0]
    return max(0.0, min(limits)) if limits else 0.0


def generate_slider(obj: HitObject, grid, start, direction,
                     sv_multiplier: float = 1.0,
                     slider_multiplier: float = DEFAULT_SLIDER_MULTIPLIER) -> HitObject:
    """
    Fill in `obj.path`, `obj.pixel_length`, `obj.slides` and `obj.slider_beats`
    for a slider running from `start` along `direction`.

    Args:
        obj: a HitObject with kind == "slider" and end_time set
        grid: the BeatGrid (for beat_period)
        start: (x, y) start position, already inside the playfield
        direction: (dx, dy), need not be normalized
        sv_multiplier: per-object slider-velocity multiplier (Step 5 knob)
        slider_multiplier: map-wide SliderMultiplier
    """
    beats = max(0.25, (obj.end_time - obj.time) / grid.beat_period)
    velocity = 100.0 * slider_multiplier * sv_multiplier   # px per beat
    total_px = beats * velocity

    dx, dy = direction
    norm = math.hypot(dx, dy) or 1.0
    dx, dy = dx / norm, dy / norm

    room = _max_segment(start, (dx, dy), MAX_SLIDE_PX)
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
            candidate_room = _max_segment(start, (cdx, cdy), MAX_SLIDE_PX)
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

    end = (start[0] + dx * seg, start[1] + dy * seg)

    obj.path = [(float(start[0]), float(start[1])), (float(end[0]), float(end[1]))]
    obj.pixel_length = float(seg)
    obj.slides = int(slides)
    obj.slider_beats = float(beats)
    return obj


def slider_end_position(obj: HitObject) -> tuple:
    """
    Where the cursor is left after the slider finishes: the far end of the
    path on an odd slide count, back at the start on an even one.
    """
    if not obj.path:
        return (obj.x, obj.y)
    return obj.path[-1] if obj.slides % 2 == 1 else obj.path[0]
