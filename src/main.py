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
from audio.visualize import compute_mel_spectrogram
from mapping.difficulty import PRESETS, TIER_ORDER, get_preset, build_map_detailed
from mapping.object_classifier import summarize
from export.osu_writer import OsuMetadata, build_beatmap_set
from export.analysis_export import export_analysis


PROJECT_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def generate_beatmap_set(audio_path: str, out_dir: str,
                          artist: str = None, title: str = None,
                          creator: str = "osu-dsp-generator",
                          difficulties=None, seed: int = 0,
                          make_osz: bool = True,
                          write_analysis: bool = True,
                          on_progress=None) -> dict:
    """
    Run the full pipeline and write a beatmap set. Returns the dict from
    build_beatmap_set() plus a "summary" list of (tier, counts) per map
    and, unless write_analysis is off, an "analysis" dict of the paths
    src/export/analysis_export.py wrote.

    on_progress, if given, is called with a short human-readable string as
    each stage starts. Nothing is printed from here directly -- the CLI
    passes a printer, and the frontend's generation screen (FRONTEND_PLAN.md
    Phase 6) reads the same lines off stdout to show what's happening.
    Analysis re-runs per tier (each one detects onsets at its own
    sensitivity), so a five-difficulty set is five real passes over the
    audio, not one -- long enough that silence would look like a hang.
    """
    difficulties = difficulties or list(TIER_ORDER)

    def report(message):
        if on_progress is not None:
            on_progress(message)

    report(f"Loading {os.path.basename(audio_path)}")
    track = load_audio(audio_path, sr=DEFAULT_SR)

    title = title or track.name
    artist = artist or "unknown artist"

    built, summary = [], []
    detected = []
    for index, key in enumerate(difficulties, start=1):
        preset = get_preset(key)
        report(f"[{index}/{len(difficulties)}] Analysing and mapping {preset.name}")
        onsets, objs, grid = build_map_detailed(track, preset, seed=seed)
        built.append({
            "name": preset.name,
            "objects": objs,
            "grid": grid,
            "difficulty_section": preset.to_osu_difficulty_section(),
        })
        summary.append((preset.name, summarize(objs), grid))
        detected.append((preset.name, onsets))

    report("Writing beatmap files")
    metadata = OsuMetadata(title=title, artist=artist, creator=creator)
    result = build_beatmap_set(out_dir, audio_path, metadata, built,
                                make_osz=make_osz)
    result["summary"] = summary
    result["analysis"] = None

    if write_analysis and built:
        report("Writing analysis data")
        # Detection sensitivity is per tier, so there is no single "the"
        # onset list -- take the pass that found the most, which is the
        # fullest picture of what Step 2 can see in this track, and record
        # whose it was.
        onset_source, onsets = max(detected, key=lambda pair: len(pair[1]))

        result["analysis"] = export_analysis(
            result["folder"], track, compute_mel_spectrogram(track),
            onsets, built[0]["grid"],
            {d["name"]: d["objects"] for d in built},
            onset_source=onset_source)

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
    parser.add_argument("--no-analysis", action="store_true",
                         help="skip analysis.json + spectrogram.png "
                              "(the frontend's DSP visualization reads these)")
    parser.add_argument("--out-dir", default=None)
    args = parser.parse_args()

    out_dir = args.out_dir or os.path.join(PROJECT_ROOT, "data", "output")
    diffs = [d.strip() for d in args.difficulties.split(",") if d.strip()]

    result = generate_beatmap_set(
        args.audio_path, out_dir, artist=args.artist, title=args.title,
        creator=args.creator, difficulties=diffs, seed=args.seed,
        make_osz=not args.no_osz, write_analysis=not args.no_analysis,
        # flush: the frontend reads these lines live off a pipe, where
        # Python would otherwise buffer them until the run finished.
        on_progress=lambda message: print(message, flush=True))

    print(f"Beatmap folder: {result['folder']}")
    for name, counts, grid in result["summary"]:
        print(f"  [{name:7s}] {grid.bpm:6.2f} BPM  "
              f"{counts['circle']:4d} circles  {counts['slider']:4d} sliders  "
              f"{counts['spinner']:3d} spinners")
    if result["analysis"]:
        analysis_kb = os.path.getsize(result["analysis"]["analysis"]) / 1024
        print(f"Analysis data:  {result['analysis']['analysis']}  ({analysis_kb:.0f} KB)")

    if result["osz"]:
        size_kb = os.path.getsize(result["osz"]) / 1024
        print(f"Importable set: {result['osz']}  ({size_kb:.0f} KB)")
        print("Drag the .osz onto osu!(lazer) to import and play-test.")


if __name__ == "__main__":
    main()
