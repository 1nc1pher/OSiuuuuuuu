"""
src/onset/sonify.py

The most important test for real music: SONIFICATION. Unit tests can only
validate the detector against synthetic signals where you already know the
answer. For a real song, there is no ground truth file lying around -- so
the standard MIR (music information retrieval) practice is to mix an
audible "click" into the original track at every detected onset time, and
listen. If the clicks land exactly on the kicks/snares/note attacks you
hear, the detector is working. If they drift, double-trigger, or miss
hits, you'll hear it immediately -- far faster than staring at a plot.

Usage:
    python src/onset/sonify.py data/raw/your_song.mp3
    python src/onset/sonify.py data/raw/your_song.mp3 --margin 1.2 --delta 0.03

Produces data/output/<song_name>_sonified.wav -- play it in headphones.
"""

import argparse
import os
import sys

import numpy as np
import librosa
import soundfile as sf

sys.path.append(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from audio.loader import load_audio, generate_click_track, AudioTrack, DEFAULT_SR
from onset.detector import (
    detect_onsets, ADAPTIVE_THRESHOLD_MARGIN, ADAPTIVE_THRESHOLD_DELTA,
)


def sonify_onsets(track: AudioTrack, onsets: list, click_freq: float = 1500.0,
                   click_gain: float = 0.5, original_gain: float = 0.8) -> np.ndarray:
    """
    Mix an audible click at each onset's timestamp into a copy of the
    original track.

    click_freq: pitch of the click tone (Hz). 1500Hz is chosen deliberately
        high -- most musical/percussive content in a track sits well below
        that, so the click stays clearly audible and distinguishable from
        the song itself instead of blending into it.
    click_gain / original_gain: mix levels. Original is kept prominent
        so you can still judge musical context (was that actually a
        snare hit?); clicks are loud enough to be unambiguous.
    """
    click_times = np.array([o.time for o in onsets])
    clicks = librosa.clicks(times=click_times, sr=track.sr,
                             click_freq=click_freq, length=len(track.y))
    mixed = original_gain * track.y + click_gain * clicks

    peak = np.max(np.abs(mixed))
    if peak > 1.0:
        mixed = mixed / peak  # avoid clipping when both layers are loud simultaneously

    return mixed.astype(np.float32)


def sonify_and_save(track: AudioTrack, onsets: list, out_path: str,
                     **sonify_kwargs) -> str:
    mixed = sonify_onsets(track, onsets, **sonify_kwargs)
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    sf.write(out_path, mixed, track.sr)
    return out_path


def main():
    parser = argparse.ArgumentParser(
        description="Sonify detected onsets for listening-test validation"
    )
    parser.add_argument("audio_path", nargs="?", help="path to audio file")
    parser.add_argument("--synthetic", action="store_true",
                         help="use a generated click track instead of a real file")
    parser.add_argument("--bpm", type=float, default=128.0)
    parser.add_argument("--margin", type=float, default=ADAPTIVE_THRESHOLD_MARGIN)
    parser.add_argument("--delta", type=float, default=ADAPTIVE_THRESHOLD_DELTA)
    parser.add_argument("--click-freq", type=float, default=1500.0)
    parser.add_argument("--out-dir", default=None)
    args = parser.parse_args()

    if args.synthetic:
        track = generate_click_track(bpm=args.bpm, duration_sec=8.0)
    elif args.audio_path:
        track = load_audio(args.audio_path, sr=DEFAULT_SR)
    else:
        parser.error("Provide an audio_path or use --synthetic")
        return

    onsets = detect_onsets(track, margin=args.margin, delta=args.delta)
    print(f"Detected {len(onsets)} onsets in {track.name} "
          f"({len(onsets) / track.duration:.2f} onsets/sec)")

    project_root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    out_dir = args.out_dir or os.path.join(project_root, "data", "output")
    out_path = os.path.join(out_dir, f"{track.name}_sonified.wav")

    sonify_and_save(track, onsets, out_path, click_freq=args.click_freq)
    print(f"Saved sonified audio -> {out_path}")
    print("Listen in headphones: clicks should land exactly on the "
          "kicks/snares/note attacks. If they drift or double-trigger, "
          "re-run with different --margin/--delta values.")


if __name__ == "__main__":
    main()
