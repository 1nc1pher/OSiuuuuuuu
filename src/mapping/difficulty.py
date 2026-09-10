"""
src/mapping/difficulty.py

Step 5 of the pipeline: difficulty scaling.

A single audio file can produce many maps of different difficulty. Rather
than run a fresh analysis per difficulty, we keep one set of onsets + beat
grid and vary a table of parameters:

  - how sensitive onset detection is        (onset_margin / onset_delta)
  - the rhythmic grid resolution            (snap_division)
  - how aggressively to thin density        (min_spacing_beats)
  - the osu! difficulty settings            (AR / OD / CS / HP)
  - slider speed, spacing and flow          (slider_multiplier /
                                             distance_spacing / max_turn_degrees)
  - the classification thresholds           (spinner/slider/sustain/stream)

These live in `configs/difficulty_presets.yaml` (five tiers, Easy ->
Expert). This module loads and validates that file, exposes a lookup by
name or by star target, and wires a chosen preset through Steps 2 -> 3 ->
4 to produce the placed HitObjects for that difficulty.

NOTE: the `stars` value on each preset is a *label*, not a computed osu!
star rating. Real star rating is a separate algorithm and is out of scope.
"""

from dataclasses import dataclass, fields, asdict
import argparse
import os
import sys

sys.path.append(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from audio.loader import load_audio, generate_click_track, AudioTrack, DEFAULT_SR
from onset.detector import detect_onsets
from onset.beat_tracker import track_beats, BeatGrid
from mapping.object_classifier import (
    classify, assign_geometry, summarize, snap_onsets, enforce_min_spacing,
)
from mapping import object_classifier


PROJECT_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DEFAULT_PRESETS_PATH = os.path.join(PROJECT_ROOT, "configs", "difficulty_presets.yaml")


@dataclass(frozen=True)
class DifficultyPreset:
    name: str
    stars: float
    onset_margin: float
    onset_delta: float
    snap_division: int
    min_spacing_beats: float
    approach_rate: float
    overall_difficulty: float
    circle_size: float
    hp_drain: float
    slider_multiplier: float
    distance_spacing: float
    max_turn_degrees: float
    spinner_min_beats: float
    slider_min_beats: float
    slider_max_beats: float
    sustain_threshold: float
    stream_min_len: int

    def to_osu_difficulty_section(self) -> dict:
        """The osu! [Difficulty] section values for Step 6's .osu writer."""
        return {
            "HPDrainRate": self.hp_drain,
            "CircleSize": self.circle_size,
            "OverallDifficulty": self.overall_difficulty,
            "ApproachRate": self.approach_rate,
            "SliderMultiplier": self.slider_multiplier,
            "SliderTickRate": 1,
        }


# Mirror of difficulty_presets.yaml. Used as a fallback if the file is
# missing, and cross-checked against the file by the test suite so the two
# can't silently drift apart.
BUILTIN_PRESETS = {
    "Easy": dict(stars=1.5, onset_margin=2.2, onset_delta=0.08, snap_division=1,
                 min_spacing_beats=2.0, approach_rate=4.0, overall_difficulty=3.0,
                 circle_size=3.2, hp_drain=3.0, slider_multiplier=1.2,
                 distance_spacing=0.85, max_turn_degrees=40.0,
                 spinner_min_beats=4.0, slider_min_beats=1.5,
                 slider_max_beats=3.0, sustain_threshold=0.55, stream_min_len=4),
    "Normal": dict(stars=2.5, onset_margin=1.8, onset_delta=0.06, snap_division=2,
                   min_spacing_beats=1.0, approach_rate=6.0, overall_difficulty=4.0,
                   circle_size=3.8, hp_drain=4.0, slider_multiplier=1.3,
                   distance_spacing=1.05, max_turn_degrees=55.0,
                   spinner_min_beats=4.0, slider_min_beats=1.0,
                   slider_max_beats=2.0, sustain_threshold=0.5, stream_min_len=5),
    "Hard": dict(stars=3.7, onset_margin=1.5, onset_delta=0.05, snap_division=4,
                 min_spacing_beats=0.5, approach_rate=7.5, overall_difficulty=6.0,
                 circle_size=4.0, hp_drain=5.0, slider_multiplier=1.4,
                 distance_spacing=1.30, max_turn_degrees=75.0,
                 spinner_min_beats=3.0, slider_min_beats=1.0,
                 slider_max_beats=2.0, sustain_threshold=0.5, stream_min_len=6),
    "Insane": dict(stars=4.8, onset_margin=1.3, onset_delta=0.04, snap_division=4,
                   min_spacing_beats=0.25, approach_rate=8.5, overall_difficulty=7.5,
                   circle_size=4.2, hp_drain=6.0, slider_multiplier=1.5,
                   distance_spacing=1.65, max_turn_degrees=100.0,
                   spinner_min_beats=3.0, slider_min_beats=1.0,
                   slider_max_beats=1.5, sustain_threshold=0.5, stream_min_len=12),
    "Expert": dict(stars=6.0, onset_margin=1.1, onset_delta=0.03, snap_division=4,
                   min_spacing_beats=0.25, approach_rate=9.3, overall_difficulty=8.5,
                   circle_size=4.2, hp_drain=6.5, slider_multiplier=1.6,
                   distance_spacing=2.10, max_turn_degrees=130.0,
                   spinner_min_beats=3.0, slider_min_beats=1.0,
                   slider_max_beats=1.5, sustain_threshold=0.5, stream_min_len=999),
}

# canonical easy -> hard order
TIER_ORDER = ["Easy", "Normal", "Hard", "Insane", "Expert"]

_REQUIRED_FIELDS = {f.name for f in fields(DifficultyPreset)} - {"name"}


def _make_preset(name: str, data: dict) -> DifficultyPreset:
    missing = _REQUIRED_FIELDS - set(data)
    extra = set(data) - _REQUIRED_FIELDS
    if missing:
        raise ValueError(f"preset {name!r} missing fields: {sorted(missing)}")
    if extra:
        raise ValueError(f"preset {name!r} has unknown fields: {sorted(extra)}")
    return DifficultyPreset(name=name, **{
        "snap_division": int(data["snap_division"]),
        "stream_min_len": int(data["stream_min_len"]),
        **{k: float(v) for k, v in data.items()
           if k not in ("snap_division", "stream_min_len")},
    })


def load_presets(path: str = DEFAULT_PRESETS_PATH) -> dict:
    """
    Load and validate the difficulty presets. Falls back to BUILTIN_PRESETS
    if the YAML file is not present. Returns an ordered dict keyed by name.
    """
    if os.path.isfile(path):
        import yaml
        with open(path, "r", encoding="utf-8") as fh:
            raw = yaml.safe_load(fh)
        source = raw.get("presets", {})
    else:
        source = BUILTIN_PRESETS

    presets = {}
    for name in TIER_ORDER:
        if name not in source:
            raise ValueError(f"preset table is missing tier {name!r}")
        presets[name] = _make_preset(name, dict(source[name]))

    stars = [presets[n].stars for n in TIER_ORDER]
    if stars != sorted(stars):
        raise ValueError(f"preset `stars` are not in ascending order: {stars}")
    return presets


PRESETS = load_presets()


def get_preset(key, presets: dict = None) -> DifficultyPreset:
    """
    Look a preset up by name (case-insensitive) or by star target (returns
    the tier with the nearest `stars` value).
    """
    presets = presets or PRESETS
    if isinstance(key, str):
        for name, preset in presets.items():
            if name.lower() == key.lower():
                return preset
        try:
            key = float(key)
        except ValueError:
            raise KeyError(f"no difficulty preset named {key!r}; "
                           f"have {list(presets)}")
    return min(presets.values(), key=lambda p: abs(p.stars - float(key)))


def list_presets(presets: dict = None) -> str:
    presets = presets or PRESETS
    lines = [f"{'tier':8s} {'stars':>5s} {'snap':>4s} {'thin':>5s} "
             f"{'AR':>4s} {'OD':>4s} {'CS':>4s} {'SV':>4s} {'DS':>5s} {'turn':>5s}"]
    for p in presets.values():
        lines.append(f"{p.name:8s} {p.stars:5.1f} {p.snap_division:4d} "
                     f"{p.min_spacing_beats:5.2f} {p.approach_rate:4.1f} "
                     f"{p.overall_difficulty:4.1f} {p.circle_size:4.1f} "
                     f"{p.slider_multiplier:4.1f} {p.distance_spacing:5.2f} "
                     f"{p.max_turn_degrees:4.0f}d")
    return "\n".join(lines)


# ------------------------------------------------------------------
# Wiring a preset through the pipeline
# ------------------------------------------------------------------

def build_map(track: AudioTrack, preset: DifficultyPreset, seed: int = 0) -> tuple:
    """
    Steps 2 -> 3 -> 4 with every knob taken from `preset`. Returns
    (objects, grid). The beat grid does not depend on difficulty, but we
    return it because Step 6 needs it for the timing point.
    """
    onsets = detect_onsets(track, margin=preset.onset_margin,
                            delta=preset.onset_delta)
    grid = track_beats(track)

    objs = classify(
        onsets, grid, track,
        snap_division=preset.snap_division,
        min_spacing_beats=preset.min_spacing_beats,
        spinner_min_beats=preset.spinner_min_beats,
        slider_min_beats=preset.slider_min_beats,
        slider_max_beats=preset.slider_max_beats,
        stream_min_len=preset.stream_min_len,
        sustain_threshold=preset.sustain_threshold,
    )
    assign_geometry(objs, grid, sv_multiplier=1.0,
                    slider_multiplier=preset.slider_multiplier,
                    circle_size=preset.circle_size,
                    distance_spacing=preset.distance_spacing,
                    max_turn_degrees=preset.max_turn_degrees,
                    seed=seed)
    return objs, grid


def build_all_difficulties(track: AudioTrack, seed: int = 0) -> dict:
    """One map per tier. Returns {tier_name: (objects, grid)}."""
    return {name: build_map(track, PRESETS[name], seed=seed) for name in TIER_ORDER}


def rhythm_slot_count(track: AudioTrack, preset: DifficultyPreset,
                       grid: BeatGrid = None) -> int:
    """
    How many rhythmic slots this preset keeps *before* classification /
    stream-folding: onsets detected at the preset's sensitivity, snapped to
    its grid resolution, thinned by its min spacing. This is the honest
    "difficulty density" number -- it is monotonic across the tiers, where
    the final object count is not (harder tiers fold fewer streams, so they
    can end up with fewer objects despite more notes).
    """
    onsets = detect_onsets(track, margin=preset.onset_margin,
                            delta=preset.onset_delta)
    grid = grid or track_beats(track)
    snapped = snap_onsets(onsets, grid, division=preset.snap_division)
    snapped = enforce_min_spacing(snapped, grid, preset.min_spacing_beats)
    return len(snapped)


# ------------------------------------------------------------------
# CLI
# ------------------------------------------------------------------

def main():
    parser = argparse.ArgumentParser(description="Step 5: difficulty scaling")
    parser.add_argument("audio_path", nargs="?", help="path to audio file")
    parser.add_argument("--synthetic", action="store_true")
    parser.add_argument("--bpm", type=float, default=128.0)
    parser.add_argument("--difficulty", default="Hard",
                         help="tier name (Easy..Expert) or a star target, e.g. 4.5")
    parser.add_argument("--all", action="store_true",
                         help="build every tier and print a density comparison")
    parser.add_argument("--list", action="store_true",
                         help="print the preset table and exit")
    parser.add_argument("--seed", type=int, default=0)
    parser.add_argument("--preview", action="store_true",
                         help="also save a playfield preview (a few seconds of the map)")
    parser.add_argument("--preview-window", type=float, nargs=2,
                         metavar=("T0", "T1"), default=(0.0, 4.0))
    parser.add_argument("--out-dir", default=None)
    args = parser.parse_args()

    if args.list:
        print(list_presets())
        return

    if args.synthetic:
        track = generate_click_track(bpm=args.bpm, duration_sec=20.0)
    elif args.audio_path:
        track = load_audio(args.audio_path, sr=DEFAULT_SR)
    else:
        parser.error("Provide an audio_path or use --synthetic")
        return

    out_dir = args.out_dir or os.path.join(PROJECT_ROOT, "data", "output")

    if args.all:
        print(f"{track.name} ({track.duration:.1f}s)\n")
        grid = track_beats(track)
        print(f"{'tier':8s} {'slots':>6s} {'objects':>8s} {'circles':>8s} "
              f"{'sliders':>8s} {'spinners':>9s} {'obj/s':>7s}")
        for name in TIER_ORDER:
            preset = PRESETS[name]
            slots = rhythm_slot_count(track, preset, grid=grid)
            objs, _ = build_map(track, preset, seed=args.seed)
            c = summarize(objs)
            print(f"{name:8s} {slots:6d} {len(objs):8d} {c['circle']:8d} "
                  f"{c['slider']:8d} {c['spinner']:9d} "
                  f"{len(objs) / track.duration:7.2f}")
        return

    preset = get_preset(args.difficulty)
    objs, grid = build_map(track, preset, seed=args.seed)
    counts = summarize(objs)

    print(f"Track:      {track.name} ({track.duration:.1f}s)")
    print(f"Difficulty: {preset.name} (~{preset.stars:.1f}*)")
    print(f"Tempo:      {grid.bpm:.2f} BPM, offset {grid.offset * 1000:.0f} ms")
    print(f"osu! diff:  AR {preset.approach_rate} / OD {preset.overall_difficulty}"
          f" / CS {preset.circle_size} / HP {preset.hp_drain} / "
          f"SV {preset.slider_multiplier}")
    print(f"Objects:    {len(objs)}  ({counts['circle']} circles, "
          f"{counts['slider']} sliders, {counts['spinner']} spinners, "
          f"{len(objs) / track.duration:.2f}/s)")

    object_classifier.plot_objects(
        track, grid, objs,
        out_path=os.path.join(out_dir, f"{track.name}_{preset.name}_objects.png"))

    if args.preview:
        t0, t1 = args.preview_window
        object_classifier.plot_playfield(
            objs, out_path=os.path.join(
                out_dir, f"{track.name}_{preset.name}_playfield.png"),
            t0=t0, t1=t1, circle_size=preset.circle_size,
            title=(f"{track.name} [{preset.name}]  {t0:.0f}-{t1:.0f}s  "
                   f"CS {preset.circle_size}, DS {preset.distance_spacing} "
                   f"diameters/beat"))


if __name__ == "__main__":
    main()
