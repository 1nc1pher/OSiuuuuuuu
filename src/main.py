"""
src/main.py

End-to-end CLI: audio file in, importable osu! beatmap set (.osz) out.

    python src/main.py data/raw/song.mp3
    python src/main.py data/raw/song.mp3 --artist "Artist" --title "Song" \
        --creator "you" --difficulties Easy,Normal,Hard,Insane,Expert

Pipeline:
    [1/2] load audio + detect onsets            (src/audio, src/onset)
    [3]   estimate tempo + beat grid            (src/onset/beat_tracker)
    [4/5] classify + place objects per tier     (src/mapping)
    [6]   render .osu files + zip an .osz       (src/export/osu_writer)

The .osz lands in data/output/. Drag it onto osu!(lazer) to import and
play-test the generated maps.
"""

import argparse
import os
import sys

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from audio.loader import load_audio, DEFAULT_SR
from mapping.difficulty import PRESETS, TIER_ORDER, get_preset, build_map
from mapping.object_classifier import summarize
from export.osu_writer import OsuMetadata, build_beatmap_set


PROJECT_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def generate_beatmap_set(audio_path: str, out_dir: str,
                          artist: str = None, title: str = None,
                          creator: str = "osu-dsp-generator",
                          difficulties=None, seed: int = 0,
                          make_osz: bool = True) -> dict:
    """
    Run the full pipeline and write a beatmap set. Returns the dict from
    build_beatmap_set() plus a "summary" list of (tier, counts) per map.
    """
    difficulties = difficulties or list(TIER_ORDER)
    track = load_audio(audio_path, sr=DEFAULT_SR)

    title = title or track.name
    artist = artist or "unknown artist"

    built, summary = [], []
    for key in difficulties:
        preset = get_preset(key)
        objs, grid = build_map(track, preset, seed=seed)
        built.append({
            "name": preset.name,
            "objects": objs,
            "grid": grid,
            "difficulty_section": preset.to_osu_difficulty_section(),
        })
        summary.append((preset.name, summarize(objs), grid))

    metadata = OsuMetadata(title=title, artist=artist, creator=creator)
    result = build_beatmap_set(out_dir, audio_path, metadata, built,
                                make_osz=make_osz)
    result["summary"] = summary
    return result


def main():
    parser = argparse.ArgumentParser(
        description="Generate an osu! beatmap set from an audio file")
    parser.add_argument("audio_path", help="path to .mp3/.wav/.ogg/.flac")
    parser.add_argument("--artist", default=None)
    parser.add_argument("--title", default=None, help="defaults to the filename")
    parser.add_argument("--creator", default="osu-dsp-generator")
    parser.add_argument("--difficulties", default=",".join(TIER_ORDER),
                         help="comma-separated tier names or star targets")
    parser.add_argument("--seed", type=int, default=0)
    parser.add_argument("--no-osz", action="store_true",
                         help="write the folder only, skip the .osz zip")
    parser.add_argument("--out-dir", default=None)
    args = parser.parse_args()

    out_dir = args.out_dir or os.path.join(PROJECT_ROOT, "data", "output")
    diffs = [d.strip() for d in args.difficulties.split(",") if d.strip()]

    result = generate_beatmap_set(
        args.audio_path, out_dir, artist=args.artist, title=args.title,
        creator=args.creator, difficulties=diffs, seed=args.seed,
        make_osz=not args.no_osz)

    print(f"Beatmap folder: {result['folder']}")
    for name, counts, grid in result["summary"]:
        print(f"  [{name:7s}] {grid.bpm:6.2f} BPM  "
              f"{counts['circle']:4d} circles  {counts['slider']:4d} sliders  "
              f"{counts['spinner']:3d} spinners")
    if result["osz"]:
        size_kb = os.path.getsize(result["osz"]) / 1024
        print(f"Importable set: {result['osz']}  ({size_kb:.0f} KB)")
        print("Drag the .osz onto osu!(lazer) to import and play-test.")


if __name__ == "__main__":
    main()
