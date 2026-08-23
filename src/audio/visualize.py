"""
src/audio/visualize.py

Step 1 of the pipeline: load audio, compute its STFT, and visualize
waveform + linear spectrogram + mel spectrogram. This is your sanity-check
step -- before writing any onset-detection code, you should be able to
*see* the drum hits and note attacks as vertical bright streaks in the
spectrogram.

Run directly:
    python src/audio/visualize.py path/to/song.mp3
    python src/audio/visualize.py --synthetic   # no file needed, uses a click track
"""

import argparse
import os
import sys

import numpy as np
import librosa
import librosa.display
import matplotlib.pyplot as plt

# allow running this file directly (python src/audio/visualize.py ...)
sys.path.append(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from audio.loader import load_audio, generate_click_track, AudioTrack, DEFAULT_SR


# ---- STFT parameters -------------------------------------------------
# n_fft: FFT window size in samples. Bigger = better frequency resolution,
#        worse time resolution. 2048 @ 22050Hz ~= 93ms window -- a good
#        balance for percussive onset detection.
# hop_length: samples between successive frames. n_fft/4 is the common
#        default (75% overlap), giving smooth, well-resolved time frames.
N_FFT = 2048
HOP_LENGTH = 512


def compute_stft(track: AudioTrack, n_fft: int = N_FFT,
                  hop_length: int = HOP_LENGTH) -> np.ndarray:
    """
    Compute the Short-Time Fourier Transform.

    Returns a complex-valued matrix of shape (1 + n_fft/2, n_frames).
    Magnitude = |STFT|, phase = angle(STFT). For onset detection we'll
    only need the magnitude, but we return the complex result here since
    it's the more general/reusable primitive.
    """
    return librosa.stft(track.y, n_fft=n_fft, hop_length=hop_length,
                         window="hann")


def compute_mel_spectrogram(track: AudioTrack, n_fft: int = N_FFT,
                             hop_length: int = HOP_LENGTH,
                             n_mels: int = 128) -> np.ndarray:
    """
    Compute a mel-scaled power spectrogram (in dB).

    The mel scale compresses high frequencies, matching human pitch
    perception -- and conveniently also matching where most musically
    relevant (and rhythmically relevant) information lives. This is the
    representation we'll feed spectral-flux onset detection in Step 2.
    """
    mel = librosa.feature.melspectrogram(
        y=track.y, sr=track.sr, n_fft=n_fft, hop_length=hop_length,
        n_mels=n_mels
    )
    return librosa.power_to_db(mel, ref=np.max)


def frame_times(n_frames: int, sr: int, hop_length: int = HOP_LENGTH) -> np.ndarray:
    """Convert STFT frame indices to time (seconds)."""
    return librosa.frames_to_time(np.arange(n_frames), sr=sr, hop_length=hop_length)


def plot_analysis(track: AudioTrack, out_path: str = None, show: bool = False):
    """
    3-panel figure: waveform, linear-frequency spectrogram (dB),
    mel spectrogram (dB). Saves to out_path if given.
    """
    stft = compute_stft(track)
    stft_db = librosa.amplitude_to_db(np.abs(stft), ref=np.max)
    mel_db = compute_mel_spectrogram(track)

    fig, axes = plt.subplots(3, 1, figsize=(12, 9), sharex=True)

    # --- waveform ---
    t = np.arange(track.n_samples) / track.sr
    axes[0].plot(t, track.y, linewidth=0.5, color="steelblue")
    axes[0].set_title(f"Waveform -- {track.name}  (sr={track.sr} Hz, "
                       f"{track.duration:.1f}s)")
    axes[0].set_ylabel("Amplitude")
    axes[0].set_xlim(0, track.duration)

    # --- linear spectrogram ---
    img1 = librosa.display.specshow(
        stft_db, sr=track.sr, hop_length=HOP_LENGTH, x_axis="time",
        y_axis="hz", ax=axes[1], cmap="magma"
    )
    axes[1].set_title(f"STFT Spectrogram (n_fft={N_FFT}, hop={HOP_LENGTH})")
    axes[1].set_ylim(0, 8000)  # most rhythmic/percussive energy is below 8kHz
    fig.colorbar(img1, ax=axes[1], format="%+2.0f dB")

    # --- mel spectrogram ---
    img2 = librosa.display.specshow(
        mel_db, sr=track.sr, hop_length=HOP_LENGTH, x_axis="time",
        y_axis="mel", ax=axes[2], cmap="magma"
    )
    axes[2].set_title("Mel Spectrogram (input to Step 2: onset detection)")
    fig.colorbar(img2, ax=axes[2], format="%+2.0f dB")

    fig.tight_layout()

    if out_path:
        os.makedirs(os.path.dirname(out_path), exist_ok=True)
        fig.savefig(out_path, dpi=150)
        print(f"Saved figure -> {out_path}")

    if show:
        plt.show()
    else:
        plt.close(fig)

    return stft, mel_db


def print_track_stats(track: AudioTrack):
    print(f"Track:      {track.name}")
    print(f"Source:     {track.path}")
    print(f"Sample rate:{track.sr} Hz")
    print(f"Duration:   {track.duration:.2f} s")
    print(f"Samples:    {track.n_samples}")
    print(f"RMS level:  {track.rms():.4f}")


def main():
    parser = argparse.ArgumentParser(description="Step 1: audio loading + STFT visualization")
    parser.add_argument("audio_path", nargs="?", help="path to audio file")
    parser.add_argument("--synthetic", action="store_true",
                         help="use a generated click track instead of a real file")
    parser.add_argument("--bpm", type=float, default=128.0,
                         help="BPM for --synthetic click track")
    parser.add_argument("--out-dir", default=None,
                         help="output dir for the figure (default: data/output)")
    args = parser.parse_args()

    if args.synthetic:
        track = generate_click_track(bpm=args.bpm, duration_sec=8.0)
    elif args.audio_path:
        track = load_audio(args.audio_path, sr=DEFAULT_SR)
    else:
        parser.error("Provide an audio_path or use --synthetic")
        return

    print_track_stats(track)

    project_root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    out_dir = args.out_dir or os.path.join(project_root, "data", "output")
    out_path = os.path.join(out_dir, f"{track.name}_analysis.png")

    plot_analysis(track, out_path=out_path)


if __name__ == "__main__":
    main()
