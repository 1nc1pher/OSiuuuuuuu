"""
src/audio/loader.py

Loads an audio file (mp3/wav/ogg/flac) into a mono numpy array at a fixed
sample rate, ready for STFT/onset analysis downstream.

Why librosa.load() and not scipy.io.wavfile:
- scipy.io.wavfile only reads uncompressed WAV.
- librosa.load() decodes mp3/ogg/flac/wav via audioread/soundfile,
  automatically converts to mono, and resamples to a target sample rate
  in one call -- exactly what we need for a consistent analysis pipeline
  regardless of what format the user's song is in.
"""

from dataclasses import dataclass
import os

import numpy as np
import librosa


# Standard analysis sample rate. 22050 Hz is plenty for onset/beat work
# (most rhythmic information lives well under 10 kHz) and keeps FFTs cheap.
# Bump to 44100 later if you want higher-frequency detail (e.g. hi-hats,
# cymbals) for high-difficulty maps.
DEFAULT_SR = 22050


@dataclass
class AudioTrack:
    """Container for a loaded audio track and its basic metadata."""
    y: np.ndarray        # mono waveform, float32, range [-1, 1]
    sr: int               # sample rate (Hz)
    duration: float       # seconds
    path: str             # original file path
    name: str              # filename without extension

    @property
    def n_samples(self) -> int:
        return len(self.y)

    def rms(self) -> float:
        """Root-mean-square loudness of the whole track."""
        return float(np.sqrt(np.mean(self.y ** 2)))


def load_audio(path: str, sr: int = DEFAULT_SR, mono: bool = True,
                normalize: bool = True) -> AudioTrack:
    """
    Load an audio file into an AudioTrack.

    Args:
        path: path to .mp3/.wav/.ogg/.flac file
        sr: target sample rate. librosa resamples automatically.
        mono: downmix to mono (recommended -- rhythm content is
              near-identical across channels, and mono halves compute cost).
        normalize: peak-normalize waveform to [-1, 1]. Prevents quiet songs
              from producing weak onset detection functions later.

    Returns:
        AudioTrack
    """
    if not os.path.isfile(path):
        raise FileNotFoundError(f"Audio file not found: {path}")

    y, sr_actual = librosa.load(path, sr=sr, mono=mono)

    if normalize:
        peak = np.max(np.abs(y))
        if peak > 0:
            y = y / peak

    name = os.path.splitext(os.path.basename(path))[0]
    duration = len(y) / sr_actual

    return AudioTrack(y=y, sr=sr_actual, duration=duration, path=path, name=name)


def generate_click_track(bpm: float = 128.0, duration_sec: float = 10.0,
                          sr: int = DEFAULT_SR) -> AudioTrack:
    """
    Synthesize a simple metronome click track for testing the pipeline
    without needing a real audio file. Useful for CI / sanity checks:
    every beat should produce a clean, unambiguous onset.

    Each click is a short exponentially-decaying sine burst -- similar
    spectral shape to a real percussive hit.
    """
    beat_period = 60.0 / bpm
    n_samples = int(duration_sec * sr)
    y = np.zeros(n_samples, dtype=np.float32)

    click_len = int(0.05 * sr)  # 50ms click
    t_click = np.arange(click_len) / sr
    click = np.sin(2 * np.pi * 1000 * t_click) * np.exp(-t_click * 60)

    beat_times = np.arange(0, duration_sec, beat_period)
    for bt in beat_times:
        start = int(bt * sr)
        end = start + click_len
        if end <= n_samples:
            y[start:end] += click

    peak = np.max(np.abs(y))
    if peak > 0:
        y = y / peak

    return AudioTrack(y=y, sr=sr, duration=duration_sec, path="<synthetic>",
                       name=f"click_track_{int(bpm)}bpm")


if __name__ == "__main__":
    # quick manual smoke test
    track = generate_click_track(bpm=128, duration_sec=5)
    print(f"Loaded: {track.name}")
    print(f"  sr={track.sr}, duration={track.duration:.2f}s, "
          f"samples={track.n_samples}, rms={track.rms():.4f}")
