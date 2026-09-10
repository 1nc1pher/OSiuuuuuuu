"""
src/mapping/object_classifier.py

Step 4 of the pipeline: decide, for every detected onset, whether it
becomes a CIRCLE, a SLIDER, or a SPINNER, then place all the objects on
the playfield.

INPUTS
  - the onsets from Step 2 (time, strength, dominant frequency band)
  - the beat grid from Step 3 (tempo + offset)
  - the audio track (for the RMS energy profile -- see "sustain" below)

WHY THESE THREE TYPES, AND HOW WE TELL THEM APART
  Rhythm is carried by *onsets*; the type of object is carried by what
  happens in the *gap after* an onset:

    circle   -- a hit with nothing sustained after it. The default.
    slider   -- an onset whose sound is held for ~1-2 beats (a held note,
                a vocal sustain), OR the start of a fast run of onsets
                (a stream / melodic run) that we fold into one slider so
                the map stays readable.
    spinner  -- a long stretch (>= a few beats) with no new onsets but
                the audio energy stays up: a build-up, a held chord, a
                riser. Silence over the same stretch is just a rest and
                produces nothing.

  "Sustain" is measured from a short-time RMS envelope of the waveform:
  the fraction of the gap after an onset where loudness stays above a
  small fraction of the track's peak. High sustain + long gap => the
  sound is being held => slider or spinner. Low sustain => the hit
  decayed => circle.

SNAPPING
  Onsets are first quantized to the beat grid at 1/4-beat resolution.
  This removes detection jitter, collapses double-triggers onto one slot,
  and gives every object a clean rhythmic position that Step 6 can write
  as an exact millisecond time.

POSITIONS
  assign_geometry() walks the objects in time order and moves a virtual
  cursor around the playfield, following the same rules real mappers use
  (distance snap, flow, no burying). See the "Placement" section below
  for the details. Step 5 supplies the per-difficulty numbers and Step 6
  just reads out the coordinates.
"""

from collections import deque
from dataclasses import dataclass
import argparse
import math
import os
import sys

import numpy as np
import librosa

sys.path.append(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from audio.loader import load_audio, generate_click_track, AudioTrack, DEFAULT_SR
from audio.visualize import HOP_LENGTH
from onset.detector import detect_onsets, Onset
from onset.beat_tracker import track_beats, onset_envelope, BeatGrid
from mapping.hit_object import (
    HitObject, PLAYFIELD_W, PLAYFIELD_H, PLAYFIELD_MARGIN, PLAYFIELD_CENTER,
    circle_radius, DEFAULT_CIRCLE_SIZE,
)
from mapping.slider_generator import (
    generate_slider, slider_end_position, DEFAULT_SLIDER_MULTIPLIER,
)
from mapping.spinner_generator import generate_spinner


# ---- classification thresholds -----------------------------------------
SNAP_DIVISION = 4               # quantize to 1/4 beats
SPINNER_MIN_BEATS = 3.0         # gap this long, with sustain, is a spinner
SLIDER_MIN_BEATS = 1.0          # gap this long, with sustain, is a slider
SLIDER_MAX_BEATS = 2.0          # cap a held-note slider at this many beats
STREAM_MAX_SPACING_BEATS = 0.5  # onsets this close count as one stream
STREAM_MIN_LEN = 4              # need this many to fold a stream into a slider
SUSTAIN_THRESHOLD = 0.5         # fraction of the gap that must stay "loud"

# ---- placement tuning ---------------------------------------------------
# Every distance below is in circle DIAMETERS, not pixels, so it scales
# with CS automatically. See the "Placement" comment above assign_geometry().
DEFAULT_DISTANCE_SPACING = 1.3          # diameters travelled per beat
DEFAULT_MAX_TURN_DEGREES = 80.0         # how sharply flow may turn on a jump
STREAM_GAP_BEATS = 0.375                # at/below this a gap is a stream
MIN_SEPARATION_DIAMETERS = 0.65         # floor inside a stream
NON_STREAM_SEPARATION_DIAMETERS = 0.75  # floor everywhere else
SLIDER_BODY_CLEARANCE_DIAMETERS = 0.85  # keep objects off a slider's body
RECENT_POINT_CLEARANCE_DIAMETERS = 0.8  # and off the last few object centres
MAX_SPACING_DIAMETERS = 4.0             # sanity cap on one jump
PLACEMENT_ATTEMPTS_DEG = (0, 25, -25, 50, -50, 80, -80, 110, -110, 145, -145, 180)
PLACEMENT_HISTORY = 4                   # how many recent objects to avoid


@dataclass
class _SnappedOnset:
    time: float
    strength: float
    band: str
    snap: str


# ------------------------------------------------------------------
# Snapping
# ------------------------------------------------------------------

def snap_onsets(onsets, grid: BeatGrid, division: int = SNAP_DIVISION):
    """
    Quantize onset times to the beat grid at `1/division` resolution,
    dropping onsets that land on an already-used slot (keeping the
    stronger one). Returns a time-sorted list of _SnappedOnset.
    """
    if not onsets:
        return []

    step = grid.beat_period / division
    slots = {}
    for o in onsets:
        k = round((o.time - grid.offset) / step)
        t = grid.offset + k * step
        if t < 0:
            continue
        label = "1/1" if k % division == 0 else (
            "1/2" if (k * 2) % division == 0 else f"1/{division}")
        prev = slots.get(k)
        if prev is None or o.strength > prev.strength:
            slots[k] = _SnappedOnset(time=t, strength=float(o.strength),
                                      band=o.dominant_band(), snap=label)
    return [slots[k] for k in sorted(slots)]


# ------------------------------------------------------------------
# Sustain / energy
# ------------------------------------------------------------------

def _rms_envelope(track: AudioTrack, frame_length: int = 2048,
                   hop_length: int = HOP_LENGTH):
    rms = librosa.feature.rms(y=track.y, frame_length=frame_length,
                               hop_length=hop_length)[0]
    times = librosa.frames_to_time(np.arange(len(rms)), sr=track.sr,
                                    hop_length=hop_length)
    return rms, times


def sustain_ratio(rms: np.ndarray, rms_times: np.ndarray, t0: float, t1: float,
                   rel: float = 0.15) -> float:
    """
    Fraction of the window (t0, t1) where the short-time RMS stays above
    `rel` * (track peak RMS). ~1.0 => the sound is held through the gap;
    ~0.0 => it decayed to silence (or the gap is a rest).
    """
    mask = (rms_times >= t0) & (rms_times < t1)
    if not np.any(mask):
        return 0.0
    peak = float(np.max(rms)) or 1.0
    return float(np.mean(rms[mask] > rel * peak))


# ------------------------------------------------------------------
# Classification
# ------------------------------------------------------------------

def enforce_min_spacing(snapped, grid: BeatGrid, min_spacing_beats: float):
    """
    Greedy thinning: walk the time-sorted snapped onsets and drop any that
    fall within `min_spacing_beats` of the last kept one, keeping whichever
    of the pair is stronger. This is how lower difficulties shed density
    without leaving lopsided rhythmic gaps. Same greedy rule as the onset
    detector's peak picker.
    """
    if min_spacing_beats <= 0 or not snapped:
        return list(snapped)
    min_dt = min_spacing_beats * grid.beat_period - 1e-9
    kept = [snapped[0]]
    for s in snapped[1:]:
        if s.time - kept[-1].time >= min_dt:
            kept.append(s)
        elif s.strength > kept[-1].strength:
            kept[-1] = s
    return kept


def classify(onsets, grid: BeatGrid, track: AudioTrack,
              snap_division: int = SNAP_DIVISION,
              min_spacing_beats: float = 0.0,
              spinner_min_beats: float = SPINNER_MIN_BEATS,
              slider_min_beats: float = SLIDER_MIN_BEATS,
              slider_max_beats: float = SLIDER_MAX_BEATS,
              stream_max_spacing: float = STREAM_MAX_SPACING_BEATS,
              stream_min_len: int = STREAM_MIN_LEN,
              sustain_threshold: float = SUSTAIN_THRESHOLD) -> list:
    """
    Assign a type to every snapped onset. Returns a list of HitObject
    with kind/time/end_time/strength/snap filled in but NO positions yet
    (call assign_geometry() for that).

    `snap_division` sets the rhythmic grid resolution (1 = 1/1, 2 = 1/2,
    4 = 1/4); `min_spacing_beats` optionally thins the result further.
    Both are driven per difficulty by Step 5.
    """
    snapped_full = snap_onsets(onsets, grid, division=snap_division)
    snapped = enforce_min_spacing(snapped_full, grid, min_spacing_beats)
    if not snapped:
        return []

    rms, rms_times = _rms_envelope(track)
    full_times = np.array([s.time for s in snapped_full])
    period = grid.beat_period
    times = [s.time for s in snapped]
    n = len(snapped)

    objs = []
    i = 0
    while i < n:
        s = snapped[i]
        t_next = times[i + 1] if i + 1 < n else track.duration
        gap_beats = (t_next - s.time) / period
        sustain = sustain_ratio(rms, rms_times, s.time, t_next)
        # was there a real onset inside this gap that density-thinning
        # removed? if so the sound was re-attacked -- it is NOT a held
        # note, so it can't become a slider/spinner however loud it stays.
        gap_is_clean = not np.any((full_times > s.time + 1e-6)
                                  & (full_times < t_next - 1e-6))

        # 1. spinner: long clean gap, energy stays up
        if (gap_is_clean and gap_beats >= spinner_min_beats
                and sustain >= sustain_threshold):
            obj = HitObject(kind="spinner", time=s.time, end_time=t_next,
                            strength=s.strength, source_band=s.band, snap=s.snap)
            generate_spinner(obj)
            objs.append(obj)
            i += 1
            continue

        # 2. stream -> slider(s): a run of onsets spaced <= stream_max_spacing.
        # We only fold up to slider_max_beats worth into one slider, then
        # loop again -- a long stream becomes a *chain* of stream-sliders,
        # never one absurd map-long slider.
        run = 1
        while (i + run < n and
               (times[i + run] - times[i + run - 1]) / period
               <= stream_max_spacing + 1e-6):
            run += 1
        if run >= stream_min_len:
            fold_deadline = s.time + slider_max_beats * period
            j = i
            while j + 1 < i + run and times[j + 1] <= fold_deadline + 1e-6:
                j += 1
            if j > i:
                obj = HitObject(kind="slider", time=s.time, end_time=times[j],
                                strength=s.strength, source_band=s.band,
                                snap=s.snap)
                objs.append(obj)     # geometry filled later
                i = j + 1
                continue

        # 3. held-note slider: sustained energy across a clean ~1-2 beat gap
        if (gap_is_clean and gap_beats >= slider_min_beats
                and sustain >= sustain_threshold):
            end = s.time + min(gap_beats, slider_max_beats) * period
            obj = HitObject(kind="slider", time=s.time, end_time=end,
                            strength=s.strength, source_band=s.band, snap=s.snap)
            objs.append(obj)
            i += 1
            continue

        # 4. circle: the default
        objs.append(HitObject(kind="circle", time=s.time, strength=s.strength,
                               source_band=s.band, snap=s.snap))
        i += 1

    return objs


# ------------------------------------------------------------------
# Placement
# ------------------------------------------------------------------
#
# Real mappers do not place objects "120 pixels apart" -- they use the
# editor's distance-snap tool, which puts the next object a fixed number
# of circle DIAMETERS away per beat of time. Two consequences we copy:
#
#   * spacing is LINEAR in the time gap (constant cursor velocity), so a
#     1/1 gap reads as exactly twice the movement of a 1/2 gap. The old
#     sqrt curve squashed everything toward the same distance, which is
#     what made the maps feel cramped.
#   * spacing is measured in diameters, so it tracks CS: a small-circle
#     difficulty automatically spreads out.
#
# On top of distance snap, three readability rules keep the output
# looking hand-made instead of like a random walk:
#
#   1. NEVER BURY. Consecutive objects stay at least 0.75 diameters apart
#      (0.5 inside a 1/4 stream, where tight spacing is correct and
#      intended). Overlaps therefore always read as the partial,
#      "continuation" kind mappers use deliberately -- never one object
#      sitting invisibly on top of another.
#   2. STAY OFF THE SLIDER BODY. A candidate must clear the body of the
#      slider it just came off. Since the cursor already sits at the
#      slider's end, this naturally parks the next object just past that
#      end and off to one side -- exactly where mappers put it.
#   3. FLOW. The heading turns only slightly across short gaps, so a
#      stream comes out as a smooth arc (and keeps curving the same way);
#      long gaps may turn sharply, so jumps get real angles. The turn
#      limit rises Easy -> Expert.
#
# Candidates are proposed along the flow heading and, if a rule is
# violated, swept around in widening steps until one fits; the best
# candidate is used if none fully fits.

def _point_segment_distance(p, a, b) -> float:
    """Shortest distance from point `p` to the line segment a-b."""
    px, py = p
    ax, ay = a
    bx, by = b
    vx, vy = bx - ax, by - ay
    denom = vx * vx + vy * vy
    if denom <= 1e-12:
        return math.hypot(px - ax, py - ay)
    t = max(0.0, min(1.0, ((px - ax) * vx + (py - ay) * vy) / denom))
    return math.hypot(px - (ax + t * vx), py - (ay + t * vy))


def _blend_angle(a: float, b: float, w: float) -> float:
    """Rotate angle `a` a fraction `w` of the way toward angle `b`."""
    delta = (b - a + math.pi) % (2 * math.pi) - math.pi
    return a + w * delta


def _clearance_ratios(p, history, point_min: float, segment_min: float) -> tuple:
    """
    Worst (actual / required) clearance of `p` against recent geometry,
    reported separately for slider bodies and for object centres.
    Each is >= 1.0 when that constraint is satisfied.

    They are kept apart so the caller can rank them: landing on a slider
    BODY hides an object completely and is the worst outcome, so it
    outranks merely crowding an older circle.
    """
    worst_segment = worst_point = math.inf
    for entry in history:
        if entry[0] == "point":
            if point_min > 1e-9:
                dist = math.hypot(p[0] - entry[1][0], p[1] - entry[1][1])
                worst_point = min(worst_point, dist / point_min)
        elif segment_min > 1e-9:
            dist = _point_segment_distance(p, entry[1], entry[2])
            worst_segment = min(worst_segment, dist / segment_min)
    return (1.0 if worst_segment == math.inf else worst_segment,
            1.0 if worst_point == math.inf else worst_point)


def assign_geometry(objs, grid: BeatGrid, sv_multiplier: float = 1.0,
                     slider_multiplier: float = DEFAULT_SLIDER_MULTIPLIER,
                     circle_size: float = DEFAULT_CIRCLE_SIZE,
                     distance_spacing: float = DEFAULT_DISTANCE_SPACING,
                     max_turn_degrees: float = DEFAULT_MAX_TURN_DEGREES,
                     seed: int = 0) -> list:
    """
    Walk the objects in time order and place each one. Mutates and
    returns `objs`.

    Args:
        circle_size: the map's CS -- sets the diameter every distance
            below is measured in.
        distance_spacing: circle diameters travelled per beat (the
            editor's distance-snap multiplier).
        max_turn_degrees: how far flow may turn on a full-beat gap.
        slider_multiplier / sv_multiplier: slider speed.
    """
    if not objs:
        return objs

    rng = np.random.default_rng(seed)
    diameter = 2.0 * circle_radius(circle_size)

    lo_x, hi_x = PLAYFIELD_MARGIN, PLAYFIELD_W - PLAYFIELD_MARGIN
    lo_y, hi_y = PLAYFIELD_MARGIN, PLAYFIELD_H - PLAYFIELD_MARGIN
    cx, cy = PLAYFIELD_CENTER
    max_target = min(MAX_SPACING_DIAMETERS * diameter,
                     0.85 * min(hi_x - lo_x, hi_y - lo_y))

    x, y = cx, cy
    heading = float(rng.uniform(0, 2 * math.pi))
    curl = 1.0                                  # which way streams arc
    history = deque(maxlen=PLACEMENT_HISTORY)
    prev_t = objs[0].time

    for obj in objs:
        if obj.kind == "spinner":
            # spinners take over the screen; nothing before them can be
            # overlapped, so reset the cursor and forget the history
            obj.x, obj.y = cx, cy
            x, y = cx, cy
            history.clear()
            prev_t = obj.end()
            continue

        dt_beats = max(0.0, (obj.time - prev_t) / grid.beat_period)
        is_stream = dt_beats <= STREAM_GAP_BEATS
        min_sep = diameter * (MIN_SEPARATION_DIAMETERS if is_stream
                              else NON_STREAM_SEPARATION_DIAMETERS)
        # distance snap: linear in time, floored so nothing gets buried
        target = float(np.clip(diameter * distance_spacing * dt_beats,
                               min_sep, max(min_sep, max_target)))

        # flow: gentle, same-direction turns inside a stream; free turns on jumps
        turn_limit = math.radians(max_turn_degrees) * float(
            np.clip(dt_beats, 0.2, 1.0))
        if is_stream:
            turn = curl * float(rng.uniform(0.1, 0.7)) * turn_limit
        else:
            turn = float(rng.uniform(-1.0, 1.0)) * turn_limit
            curl = 1.0 if turn >= 0 else -1.0

        # you can never clear more than you travel, so cap the requirement
        point_min = min(target * 0.95, diameter * RECENT_POINT_CLEARANCE_DIAMETERS)
        segment_min = min(target * 0.95, diameter * SLIDER_BODY_CLEARANCE_DIAMETERS)

        # Sweep for a heading that satisfies every rule. First around the
        # natural flow direction; if the cursor is cornered and nothing
        # there lands on the playfield, sweep again around "back toward the
        # middle" -- aiming inward rather than bouncing off the wall, which
        # is what used to fold the pattern back over itself.
        # Gentle inward bias once the cursor gets near an edge, so patterns
        # use the whole playfield instead of crawling along a wall.
        to_center = math.atan2(cy - y, cx - x)
        edge_dist = min(x - lo_x, hi_x - x, y - lo_y, hi_y - y)
        comfort = 1.5 * diameter
        pull = 0.0 if edge_dist >= comfort else 0.55 * (1.0 - max(edge_dist, 0.0) / comfort)
        flow_heading = _blend_angle(heading + turn, to_center, pull)

        best, done = None, False
        for base_heading in (flow_heading, to_center):
            for deg in PLACEMENT_ATTEMPTS_DEG:
                h = base_heading + math.radians(deg)
                nx = x + math.cos(h) * target
                ny = y + math.sin(h) * target
                in_bounds = lo_x <= nx <= hi_x and lo_y <= ny <= hi_y
                seg_ratio, pt_ratio = _clearance_ratios((nx, ny), history,
                                                        point_min, segment_min)
                # rank: on the playfield first, then off the slider body,
                # then not crowding an older circle
                score = (1 if in_bounds else 0,
                         min(seg_ratio, 1.5), min(pt_ratio, 1.5))
                if best is None or score > best[0]:
                    best = (score, deg, h, nx, ny)
                if in_bounds and seg_ratio >= 1.0 and pt_ratio >= 1.0:
                    done = True
                    break
            if done:
                break

        (in_bounds_flag, _, _), deg, h, nx, ny = best
        if not in_bounds_flag:
            nx = float(np.clip(nx, lo_x, hi_x))     # last resort
            ny = float(np.clip(ny, lo_y, hi_y))
            curl = -curl
        elif abs(deg) >= 110:
            curl = -curl        # the arc was blocked; start bending back

        heading = h
        obj.x, obj.y = float(nx), float(ny)

        if obj.kind == "slider":
            generate_slider(obj, grid, start=(nx, ny),
                            direction=(math.cos(h), math.sin(h)),
                            sv_multiplier=sv_multiplier,
                            slider_multiplier=slider_multiplier)
            start_pt, end_pt = obj.path[0], obj.path[-1]
            history.append(("segment", start_pt, end_pt))
            x, y = slider_end_position(obj)
            # carry the slider's own direction as the new flow (reversed
            # when an even slide count brings the cursor back to the start)
            body = math.atan2(end_pt[1] - start_pt[1], end_pt[0] - start_pt[0])
            heading = body if obj.slides % 2 == 1 else body + math.pi
        else:
            history.append(("point", (nx, ny)))
            x, y = nx, ny

        prev_t = obj.end()

    return objs


# ------------------------------------------------------------------
# Orchestrator
# ------------------------------------------------------------------

def build_objects(track: AudioTrack, margin: float = 1.5, delta: float = 0.05,
                   sv_multiplier: float = 1.0, seed: int = 0) -> tuple:
    """
    Full Step 2 -> 3 -> 4: audio in, placed HitObjects + the BeatGrid out.
    """
    onsets = detect_onsets(track, margin=margin, delta=delta)
    grid = track_beats(track)
    objs = classify(onsets, grid, track)
    assign_geometry(objs, grid, sv_multiplier=sv_multiplier, seed=seed)
    return objs, grid


def summarize(objs) -> dict:
    counts = {"circle": 0, "slider": 0, "spinner": 0}
    for o in objs:
        counts[o.kind] = counts.get(o.kind, 0) + 1
    return counts


# ------------------------------------------------------------------
# Visualization / CLI
# ------------------------------------------------------------------

def plot_objects(track: AudioTrack, grid: BeatGrid, objs, out_path: str = None,
                  show: bool = False, window: float = 20.0):
    """
    Debug view: onset envelope for the first `window` seconds, with circles
    as dots, sliders as horizontal bars (start -> end), spinners as shaded
    spans, and the beat grid faint in the background.
    """
    import matplotlib.pyplot as plt

    env = onset_envelope(track)
    env_times = librosa.frames_to_time(np.arange(len(env)), sr=track.sr,
                                        hop_length=HOP_LENGTH)

    fig, ax = plt.subplots(figsize=(13, 4))
    ax.plot(env_times, env, color="lightsteelblue", linewidth=0.8, zorder=1)
    for bt in grid.beats_in_range(0, window):
        ax.axvline(bt, color="gray", linewidth=0.4, alpha=0.4, zorder=0)

    colors = {"circle": "tab:blue", "slider": "tab:green", "spinner": "tab:red"}
    for o in objs:
        if o.time > window:
            break
        c = colors[o.kind]
        if o.kind == "circle":
            ax.plot(o.time, o.strength, "o", color=c, markersize=6, zorder=3)
        elif o.kind == "slider":
            ax.hlines(o.strength, o.time, o.end(), color=c, linewidth=4, zorder=3)
        else:
            ax.axvspan(o.time, o.end(), color=c, alpha=0.25, zorder=2)

    counts = summarize(objs)
    ax.set_title(f"{track.name}: {counts['circle']} circles, "
                 f"{counts['slider']} sliders, {counts['spinner']} spinners "
                 f"(first {window:.0f}s)")
    ax.set_xlabel("Time (s)")
    ax.set_ylabel("Onset strength")
    ax.set_xlim(0, min(window, track.duration))
    fig.tight_layout()

    if out_path:
        os.makedirs(os.path.dirname(out_path), exist_ok=True)
        fig.savefig(out_path, dpi=150)
        print(f"Saved figure -> {out_path}")
    if show:
        plt.show()
    else:
        plt.close(fig)


def plot_playfield(objs, out_path: str = None, show: bool = False,
                    t0: float = 0.0, t1: float = 4.0,
                    circle_size: float = DEFAULT_CIRCLE_SIZE, title: str = ""):
    """
    Draw the objects in a time window at their true size on the 512x384
    playfield -- circles at the real CS radius, slider bodies at the width
    the game draws them, numbered in play order and joined by the cursor
    path. Colour fades from light to dark over the window so the reading
    order is obvious.

    This is the view that makes placement problems visible without
    launching osu!: objects stacked on top of each other, or a circle
    swallowed by the slider body it follows.

    Keep the window SHORT (a few seconds). Only objects within one
    approach-rate preempt (~0.8-1.2 s) are ever on screen together, so a
    long window piles up shapes that a player never sees at once and makes
    perfectly readable placement look like soup.
    """
    import matplotlib.pyplot as plt
    from matplotlib.patches import Circle as MplCircle

    radius = circle_radius(circle_size)
    window = [o for o in objs if t0 <= o.time <= t1]

    fig, ax = plt.subplots(figsize=(10, 7.5))
    ax.add_patch(plt.Rectangle((0, 0), PLAYFIELD_W, PLAYFIELD_H, fill=False,
                                edgecolor="0.7", linewidth=1))

    # cursor path between consecutive objects, so flow is readable
    path_pts = []
    for o in window:
        path_pts.append((o.x, o.y))
        if o.kind == "slider" and o.path:
            path_pts.append(slider_end_position(o))
    if len(path_pts) > 1:
        px, py = zip(*path_pts)
        ax.plot(px, py, color="0.55", linewidth=0.9, linestyle="--",
                alpha=0.9, zorder=0)

    n = max(1, len(window) - 1)
    for i, o in enumerate(window):
        fade = 0.20 + 0.45 * (i / n)            # later objects are darker
        if o.kind == "spinner":
            ax.add_patch(MplCircle((o.x, o.y), radius * 2.2, fill=False,
                                    edgecolor="tab:red", linewidth=1.5, alpha=0.8))
            ax.text(o.x, o.y, f"{i + 1}", ha="center", va="center",
                    fontsize=8, color="tab:red", zorder=4)
            continue
        if o.kind == "slider" and o.path:
            (sx, sy), (ex, ey) = o.path[0], o.path[-1]
            ax.plot([sx, ex], [sy, ey], color="tab:green", alpha=0.25,
                    linewidth=radius * 1.55, solid_capstyle="round", zorder=1)
            ax.plot([sx, ex], [sy, ey], color="tab:green", alpha=0.9,
                    linewidth=1.2, zorder=3)
        color = "tab:green" if o.kind == "slider" else "tab:blue"
        ax.add_patch(MplCircle((o.x, o.y), radius, facecolor=color,
                                alpha=fade, edgecolor=color, linewidth=1.4,
                                zorder=2))
        ax.text(o.x, o.y, str(i + 1), ha="center", va="center", fontsize=8,
                color="0.15", zorder=4)

    ax.set_xlim(-10, PLAYFIELD_W + 10)
    ax.set_ylim(PLAYFIELD_H + 10, -10)          # osu! y axis points down
    ax.set_aspect("equal")
    ax.set_title(title or f"Playfield {t0:.1f}-{t1:.1f}s "
                          f"({len(window)} objects, CS {circle_size})")
    fig.tight_layout()

    if out_path:
        os.makedirs(os.path.dirname(out_path), exist_ok=True)
        fig.savefig(out_path, dpi=150)
        print(f"Saved playfield preview -> {out_path}")
    if show:
        plt.show()
    else:
        plt.close(fig)


def main():
    parser = argparse.ArgumentParser(
        description="Step 4: circle / slider / spinner classification + placement"
    )
    parser.add_argument("audio_path", nargs="?", help="path to audio file")
    parser.add_argument("--synthetic", action="store_true")
    parser.add_argument("--bpm", type=float, default=128.0)
    parser.add_argument("--margin", type=float, default=1.5,
                         help="onset detector threshold margin (Step 2)")
    parser.add_argument("--delta", type=float, default=0.05)
    parser.add_argument("--sv", type=float, default=1.0,
                         help="slider-velocity multiplier")
    parser.add_argument("--seed", type=int, default=0,
                         help="placement RNG seed")
    parser.add_argument("--out-dir", default=None)
    args = parser.parse_args()

    if args.synthetic:
        track = generate_click_track(bpm=args.bpm, duration_sec=20.0)
    elif args.audio_path:
        track = load_audio(args.audio_path, sr=DEFAULT_SR)
    else:
        parser.error("Provide an audio_path or use --synthetic")
        return

    objs, grid = build_objects(track, margin=args.margin, delta=args.delta,
                                sv_multiplier=args.sv, seed=args.seed)
    counts = summarize(objs)

    print(f"Track:   {track.name} ({track.duration:.1f}s)")
    print(f"Tempo:   {grid.bpm:.2f} BPM, offset {grid.offset * 1000:.0f} ms")
    print(f"Objects: {len(objs)}  "
          f"({counts['circle']} circles, {counts['slider']} sliders, "
          f"{counts['spinner']} spinners)")
    for o in objs[:15]:
        extra = ""
        if o.kind == "slider":
            extra = (f"  end={o.end():.2f}s slides={o.slides} "
                     f"len={o.pixel_length:.0f}px")
        elif o.kind == "spinner":
            extra = f"  end={o.end():.2f}s spins={o.required_spins}"
        print(f"  t={o.time:6.3f}s {o.kind:7s} @({o.x:5.0f},{o.y:5.0f}) "
              f"snap={o.snap:4s} band={o.source_band}{extra}")
    if len(objs) > 15:
        print(f"  ... and {len(objs) - 15} more")

    project_root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    out_dir = args.out_dir or os.path.join(project_root, "data", "output")
    plot_objects(track, grid, objs,
                 out_path=os.path.join(out_dir, f"{track.name}_objects.png"))


if __name__ == "__main__":
    main()
