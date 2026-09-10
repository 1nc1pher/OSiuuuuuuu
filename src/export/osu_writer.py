"""
src/export/osu_writer.py

Step 6 of the pipeline: serialize a built map into the osu! .osu file
format (v14) and package it as an importable beatmap set (.osz).

WHAT A .osu FILE IS
  A plain-text, INI-ish file. First line is exactly `osu file format v14`,
  then `[Section]` headers each holding `Key: Value` lines, except
  `[Events]`, `[TimingPoints]` and `[HitObjects]`, which hold one
  comma-separated record per line.

WHAT WE WRITE
  [General]   - audio filename + playback flags
  [Editor]    - editor hints (harmless defaults)
  [Metadata]  - title / artist / creator / difficulty name
  [Difficulty]- HP / CS / OD / AR / SliderMultiplier  (from the Step 5 preset)
  [Events]    - empty (no background / storyboard)
  [TimingPoints] - ONE uninherited (red) line: the Step 3 tempo + offset
  [HitObjects]   - one line per circle / slider / spinner from Step 4

HitObject record layout (osu! wiki, "osu! (file format)"):
  circle : x,y,time,type,hitSound,hitSample
  slider : x,y,time,type,hitSound,curve,slides,length,edgeSounds,edgeSets,hitSample
  spinner: x,y,time,type,hitSound,endTime,hitSample
  `type` is a bitfield: 1=circle, 2=slider, 8=spinner, 4=new combo.

SLIDER TIMING CONSISTENCY
  osu! derives a slider's duration from its pixel length:
      duration_beats = length * slides / (100 * SliderMultiplier * SV)
  slider_generator.py already sized `pixel_length` against the same
  SliderMultiplier we write into [Difficulty] (with SV = 1, no green
  lines), so the rendered slider lasts exactly the intended number of
  beats. Keep those two in sync if you touch either.

A .osz is just a zip of the beatmap folder (audio + one .osu per
difficulty). osu!(lazer) imports it directly.
"""

from dataclasses import dataclass, replace
import os
import re
import shutil
import sys
import zipfile

sys.path.append(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from mapping.hit_object import PLAYFIELD_CENTER


OSU_FILE_FORMAT = "osu file format v14"

# bitfield values for the HitObject `type` field
_TYPE_CIRCLE = 1
_TYPE_SLIDER = 2
_TYPE_SPINNER = 8
_TYPE_NEW_COMBO = 4

# start a new combo after a rest at least this many beats long
_NEW_COMBO_GAP_BEATS = 4.0


@dataclass
class OsuMetadata:
    title: str
    artist: str = "unknown artist"
    creator: str = "osu-dsp-generator"
    version: str = "Normal"        # the difficulty name shown in-game
    source: str = ""
    tags: str = "dsp generated procedural"
    audio_filename: str = "audio.mp3"
    preview_time: int = -1         # ms, -1 = let osu! pick


# ------------------------------------------------------------------
# Line formatting
# ------------------------------------------------------------------

def _ms(seconds: float) -> int:
    return int(round(seconds * 1000.0))


def timing_point_line(grid) -> str:
    """The single uninherited timing point: tempo + offset from Step 3."""
    offset_ms = _ms(grid.offset)
    beat_len_ms = grid.beat_period * 1000.0
    meter, sample_set, sample_index, volume, uninherited, effects = 4, 1, 0, 70, 1, 0
    return (f"{offset_ms},{beat_len_ms:.6f},{meter},{sample_set},"
            f"{sample_index},{volume},{uninherited},{effects}")


def _type_field(base: int, new_combo: bool) -> int:
    return base | (_TYPE_NEW_COMBO if new_combo else 0)


def hit_object_line(obj, new_combo: bool = False) -> str:
    """Format one HitObject as a [HitObjects] record."""
    t = _ms(obj.time)
    x, y = int(round(obj.x)), int(round(obj.y))
    sample = "0:0:0:0:"

    if obj.kind == "circle":
        return f"{x},{y},{t},{_type_field(_TYPE_CIRCLE, new_combo)},0,{sample}"

    if obj.kind == "spinner":
        cx, cy = (int(round(v)) for v in PLAYFIELD_CENTER)
        return (f"{cx},{cy},{t},{_type_field(_TYPE_SPINNER, new_combo)},0,"
                f"{_ms(obj.end())},{sample}")

    if obj.kind == "slider":
        if not obj.path or len(obj.path) < 2:
            raise ValueError(f"slider at {obj.time:.3f}s has no path")
        # curve points are every anchor AFTER the start; linear ("L") slider
        pts = "|".join(f"{int(round(px))}:{int(round(py))}"
                       for px, py in obj.path[1:])
        slides = int(obj.slides)
        length = float(obj.pixel_length)
        edges = slides + 1
        edge_sounds = "|".join(["0"] * edges)
        edge_sets = "|".join(["0:0"] * edges)
        return (f"{x},{y},{t},{_type_field(_TYPE_SLIDER, new_combo)},0,"
                f"L|{pts},{slides},{length:.2f},{edge_sounds},{edge_sets},{sample}")

    raise ValueError(f"unknown hit object kind: {obj.kind!r}")


def _new_combo_flags(objs, grid) -> list:
    """
    Decide which objects start a new combo: the first one, the first after
    any spinner, and the first after a long rest.
    """
    flags = []
    prev = None
    for i, o in enumerate(objs):
        if i == 0 or prev.kind == "spinner":
            flags.append(True)
        elif (o.time - prev.end()) / grid.beat_period >= _NEW_COMBO_GAP_BEATS:
            flags.append(True)
        else:
            flags.append(False)
        prev = o
    return flags


# ------------------------------------------------------------------
# Full-file rendering
# ------------------------------------------------------------------

def render_beatmap(objects, grid, metadata: OsuMetadata,
                    difficulty_section: dict) -> str:
    """Return the complete text of a .osu v14 file."""
    objs = sorted(objects, key=lambda o: o.time)

    def kv(section, pairs):
        return [f"[{section}]"] + [f"{k}:{v}" for k, v in pairs] + [""]

    lines = [OSU_FILE_FORMAT, ""]

    lines += kv("General", [
        ("AudioFilename", metadata.audio_filename),
        ("AudioLeadIn", 0),
        ("PreviewTime", metadata.preview_time),
        ("Countdown", 0),
        ("SampleSet", "Normal"),
        ("StackLeniency", "0.7"),
        ("Mode", 0),
        ("LetterboxInBreaks", 0),
        ("WidescreenStoryboard", 0),
    ])

    lines += kv("Editor", [
        ("DistanceSpacing", "1.0"),
        ("BeatDivisor", 4),
        ("GridSize", 4),
        ("TimelineZoom", "1.0"),
    ])

    lines += kv("Metadata", [
        ("Title", metadata.title),
        ("TitleUnicode", metadata.title),
        ("Artist", metadata.artist),
        ("ArtistUnicode", metadata.artist),
        ("Creator", metadata.creator),
        ("Version", metadata.version),
        ("Source", metadata.source),
        ("Tags", metadata.tags),
        ("BeatmapID", 0),
        ("BeatmapSetID", -1),
    ])

    # osu! expects a fixed key order here; pull from the dict defensively
    diff_order = ["HPDrainRate", "CircleSize", "OverallDifficulty",
                  "ApproachRate", "SliderMultiplier", "SliderTickRate"]
    lines += kv("Difficulty", [(k, difficulty_section[k]) for k in diff_order])

    lines += [
        "[Events]",
        "//Background and Video events",
        "//Break Periods",
        "//Storyboard Layer 0 (Background)",
        "//Storyboard Layer 1 (Fail)",
        "//Storyboard Layer 2 (Pass)",
        "//Storyboard Layer 3 (Foreground)",
        "//Storyboard Layer 4 (Overlay)",
        "//Storyboard Sound Samples",
        "",
    ]

    lines += ["[TimingPoints]", timing_point_line(grid), ""]

    flags = _new_combo_flags(objs, grid)
    lines += ["[HitObjects]"]
    lines += [hit_object_line(o, nc) for o, nc in zip(objs, flags)]
    lines += [""]

    return "\n".join(lines)


def write_beatmap(path: str, objects, grid, metadata: OsuMetadata,
                   difficulty_section: dict) -> str:
    text = render_beatmap(objects, grid, metadata, difficulty_section)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as fh:
        fh.write(text)
    return path


# ------------------------------------------------------------------
# Beatmap set (.osz) packaging
# ------------------------------------------------------------------

_UNSAFE = re.compile(r'[<>:"/\\|?*\x00-\x1f]')


def _safe(name: str) -> str:
    return _UNSAFE.sub("_", name).strip().rstrip(".") or "map"


def build_beatmap_set(out_root: str, audio_path: str, metadata: OsuMetadata,
                       difficulties: list, make_osz: bool = True) -> dict:
    """
    Assemble a beatmap folder and (optionally) zip it into an .osz.

    Args:
        out_root: directory to create the beatmap folder inside
        audio_path: the source audio file (copied into the folder as-is)
        metadata: base metadata; each difficulty overrides `version`
        difficulties: list of dicts, each
            {"name": str, "objects": [...], "grid": BeatGrid,
             "difficulty_section": {...}}
        make_osz: also write "<artist> - <title>.osz"

    Returns:
        {"folder": path, "osu_files": [paths], "osz": path or None}
    """
    if not os.path.isfile(audio_path):
        raise FileNotFoundError(audio_path)

    audio_name = os.path.basename(audio_path)
    set_name = _safe(f"{metadata.artist} - {metadata.title}")
    folder = os.path.join(out_root, set_name)
    os.makedirs(folder, exist_ok=True)

    dest_audio = os.path.join(folder, audio_name)
    if os.path.abspath(dest_audio) != os.path.abspath(audio_path):
        shutil.copyfile(audio_path, dest_audio)

    base_meta = replace(metadata, audio_filename=audio_name)

    osu_files = []
    for d in difficulties:
        meta = replace(base_meta, version=d["name"])
        fname = _safe(f"{meta.artist} - {meta.title} ({meta.creator}) [{d['name']}]") + ".osu"
        path = os.path.join(folder, fname)
        write_beatmap(path, d["objects"], d["grid"], meta,
                      d["difficulty_section"])
        osu_files.append(path)

    osz_path = None
    if make_osz:
        osz_path = os.path.join(out_root, set_name + ".osz")
        with zipfile.ZipFile(osz_path, "w", zipfile.ZIP_DEFLATED) as zf:
            zf.write(dest_audio, audio_name)
            for path in osu_files:
                zf.write(path, os.path.basename(path))

    return {"folder": folder, "osu_files": osu_files, "osz": osz_path}
