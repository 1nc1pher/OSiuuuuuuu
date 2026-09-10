"""
src/onset/beat_tracker.py

Step 3 of the pipeline: tempo (BPM) estimation and beat-grid construction.

WHAT THIS PRODUCES
  A BeatGrid: a single global tempo in BPM, the time of the first beat
  ("offset"), and the full list of beat timestamps spanning the track.
  Downstream (.osu export, Step 6) an uninherited timing point is just
  (offset_ms, beat_length_ms) -- exactly BeatGrid.to_osu_timing().

THEORY
  1. Onset envelope. We reuse Step 2's spectral flux as a continuous
     "how much is happening right now" signal (one value per STFT frame).
     Beats show up as a roughly periodic ripple in this curve.

  2. Tempo via autocorrelation. Autocorrelating the onset envelope makes
     that periodicity explicit: the lag with the highest correlation is
     the beat period. We search only lags corresponding to a sane tempo
     range and weight the result by a log-normal prior centred on
     120 BPM (Parncutt 1994 -- human tempo perception clusters there),
     so that when the raw autocorrelation can't decide between a tempo
     and its double/half, the more perceptually plausible one wins.

  3. Octave correction. Autocorrelation genuinely peaks at 1x, 2x and
     1/2x the true tempo. We re-score {0.5x, 1x, 2x} of the raw estimate
     by how cleanly a pulse train at that tempo lands on actual onset
     energy (phase alignment), times the tempo prior, and keep the best.

  4. Beat phase. Given the period, slide a pulse train over the onset
     envelope one frame at a time across one whole beat; the offset whose
     pulses sum to the most onset energy is the beat phase. Everything
     after that is just arange(offset, duration, period).

WHY A SINGLE GLOBAL TEMPO
  Rhythm-game source music is almost always machine-timed and constant
  tempo. A constant grid is also what makes a map feel "snapped" and
  playable. Variable-tempo tracking is a possible later extension; it is
  deliberately out of scope here.
"""

from dataclasses import dataclass, field
import argparse
import os
import sys

import numpy as np

sys.path.append(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from audio.loader import load_audio, generate_click_track, AudioTrack, DEFAULT_SR
from audio.visualize import N_FFT, HOP_LENGTH, compute_mel_spectrogram, frame_times
from onset.detector import spectral_flux, normalize


# ---- Tempo search range / prior ------------------------------------------
# Tempos outside ~[50, 210] BPM are rare for rhythm-game source music, and
# a tight range keeps octave errors (picking 2x / 1/2x the real tempo) down.
TEMPO_MIN_BPM = 50.0
TEMPO_MAX_BPM = 210.0

# Log-normal tempo prior. Centre = the perceptually "neutral" tempo;
# width = standard deviation measured in octaves (1.0 => fairly loose).
TEMPO_PRIOR_BPM = 120.0
TEMPO_PRIOR_WIDTH_OCTAVES = 1.0


@dataclass
class BeatGrid:
    """Result of Step 3: a constant-tempo beat grid over the whole track."""
    bpm: float
    offset: float                 # time of the first beat (seconds)
    beat_times: np.ndarray        # every beat time across the track (seconds)
    confidence: float             # 0..1 -- autocorrelation periodicity strength
    phase_strength: float = 0.0   # >1 => beats land on above-average onset energy
    onset_env: np.ndarray = field(default=None, repr=False)
    env_times: np.ndarray = field(default=None, repr=False)

    @property
    def beat_period(self) -> float:
        """Seconds per beat."""
        return 60.0 / self.bpm

    @property
    def n_beats(self) -> int:
        return len(self.beat_times)

    def to_osu_timing(self) -> tuple:
        """(offset_ms, beat_length_ms) for a .osu uninherited timing point."""
        return self.offset * 1000.0, self.beat_period * 1000.0

    def beats_in_range(self, t0: float, t1: float) -> np.ndarray:
        return self.beat_times[(self.beat_times >= t0) & (self.beat_times <= t1)]


# ------------------------------------------------------------------
# Onset envelope
# ------------------------------------------------------------------

def _moving_average(x: np.ndarray, w: int) -> np.ndarray:
    if w <= 1:
        return x
    return np.convolve(x, np.ones(w) / w, mode="same")


def onset_envelope(track: AudioTrack, n_fft: int = N_FFT,
                    hop_length: int = HOP_LENGTH, n_mels: int = 128,
                    smooth: bool = True) -> np.ndarray:
    """
    Continuous onset-strength signal (one value per STFT frame), reusing
    Step 2's spectral flux. Lightly smoothed by default so single-frame
    jitter doesn't split one beat's energy across two autocorrelation lags.
    """
    mel_db = compute_mel_spectrogram(track, n_fft=n_fft, hop_length=hop_length,
                                      n_mels=n_mels)
    env = normalize(spectral_flux(mel_db))
    if smooth:
        env = _moving_average(env, 3)
    return env


# ------------------------------------------------------------------
# Tempo estimation
# ------------------------------------------------------------------

def _autocorrelation(x: np.ndarray) -> np.ndarray:
    """
    Normalized autocorrelation (lag 0 == 1.0), computed via FFT so it stays
    O(n log n) on full-length songs. Mean is removed first so a constant
    DC offset in the envelope doesn't swamp the periodic structure.
    """
    x = np.asarray(x, dtype=float)
    x = x - x.mean()
    n = len(x)
    if n == 0:
        return np.array([1.0])
    nfft = 1 << (2 * n - 1).bit_length()
    spec = np.fft.rfft(x, nfft)
    r = np.fft.irfft(spec * np.conj(spec), nfft)[:n]
    if r[0] > 0:
        r = r / r[0]
    return r


def _bpm_to_lag(bpm: float, sr: int, hop_length: int) -> float:
    return 60.0 * sr / (hop_length * bpm)


def _lag_to_bpm(lag, sr: int, hop_length: int):
    return 60.0 * sr / (hop_length * np.asarray(lag, dtype=float))


def tempo_prior_weight(bpm, prior_bpm: float = TEMPO_PRIOR_BPM,
                        width_octaves: float = TEMPO_PRIOR_WIDTH_OCTAVES):
    """Log-normal bump in tempo-octave space, peak 1.0 at `prior_bpm`."""
    octaves = np.log2(np.asarray(bpm, dtype=float) / prior_bpm)
    return np.exp(-0.5 * (octaves / width_octaves) ** 2)


def _parabolic_peak(y: np.ndarray, i: int) -> float:
    """Sub-sample peak location by fitting a parabola through y[i-1:i+2]."""
    if i <= 0 or i >= len(y) - 1:
        return float(i)
    a, b, c = y[i - 1], y[i], y[i + 1]
    denom = a - 2 * b + c
    if abs(denom) < 1e-12:
        return float(i)
    return float(i) + 0.5 * (a - c) / denom


def estimate_tempo(onset_env: np.ndarray, sr: int, hop_length: int = HOP_LENGTH,
                    tempo_min: float = TEMPO_MIN_BPM,
                    tempo_max: float = TEMPO_MAX_BPM,
                    prior_bpm: float = TEMPO_PRIOR_BPM) -> tuple:
    """
    Estimate a single global tempo from the onset envelope.

    Returns (bpm, confidence), where confidence is the (prior-free)
    normalized autocorrelation height at the chosen lag, clipped to [0, 1]
    -- a rough "how periodic is this really" score. This is the *raw*
    autocorrelation estimate; octave correction happens in track_beats().
    """
    acf = _autocorrelation(onset_env)

    lag_min = max(1, int(np.floor(_bpm_to_lag(tempo_max, sr, hop_length))))
    lag_max = min(len(acf) - 2, int(np.ceil(_bpm_to_lag(tempo_min, sr, hop_length))))
    if lag_max <= lag_min:
        return float(prior_bpm), 0.0

    lags = np.arange(lag_min, lag_max + 1)
    bpms = _lag_to_bpm(lags, sr, hop_length)
    weighted = acf[lags] * tempo_prior_weight(bpms, prior_bpm)

    best_lag = int(lags[int(np.argmax(weighted))])
    refined_lag = _parabolic_peak(acf, best_lag)
    bpm = float(_lag_to_bpm(refined_lag, sr, hop_length))
    confidence = float(np.clip(acf[best_lag], 0.0, 1.0))
    return bpm, confidence


# ------------------------------------------------------------------
# Beat phase + octave correction
# ------------------------------------------------------------------

def _phase_scores(onset_env: np.ndarray, period_frames: float) -> np.ndarray:
    """
    For each integer start offset in [0, ceil(period)), the mean onset
    energy under a pulse train at exactly `period_frames` spacing starting
    there. The pulse positions are fractional, so we sample the envelope by
    linear interpolation rather than integer indexing -- otherwise a
    non-integer beat period slowly walks off the beats over a long track.
    The offset that maximizes this is the beat phase.
    """
    n = len(onset_env)
    if period_frames < 1 or n == 0:
        return np.array([float(np.mean(onset_env)) if n else 0.0])
    period_int = int(np.ceil(period_frames))
    frame_idx = np.arange(n)
    scores = np.empty(period_int)
    for off in range(period_int):
        n_beats = int(np.floor((n - 1 - off) / period_frames)) + 1
        if n_beats <= 0:
            scores[off] = 0.0
            continue
        pos = off + np.arange(n_beats) * period_frames
        scores[off] = np.interp(pos, frame_idx, onset_env).mean()
    return scores


def estimate_beat_phase(onset_env: np.ndarray, sr: int, bpm: float,
                         hop_length: int = HOP_LENGTH) -> tuple:
    """
    Returns (offset_seconds, phase_strength). phase_strength is the ratio
    of mean onset energy on-beat to mean onset energy overall: > 1 means
    beats preferentially land on louder-than-average moments (good).
    """
    period_frames = _bpm_to_lag(bpm, sr, hop_length)
    scores = _phase_scores(onset_env, period_frames)
    best_off = int(np.argmax(scores))
    offset_sec = best_off * hop_length / sr
    strength = float(scores[best_off] / (onset_env.mean() + 1e-12))
    return offset_sec, strength


def _resolve_octave(onset_env: np.ndarray, sr: int, hop_length: int,
                     base_bpm: float, tempo_min: float, tempo_max: float,
                     prior_bpm: float) -> float:
    """
    Autocorrelation peaks at the true tempo *and* its integer multiples /
    divisors. Re-score {0.5x, 1x, 2x} of the raw estimate by
    (phase alignment) x (tempo prior) and return the winner.
    """
    best_bpm, best_score = base_bpm, -np.inf
    for factor in (0.5, 1.0, 2.0):
        cand = base_bpm * factor
        if cand < tempo_min or cand > tempo_max:
            continue
        _, strength = estimate_beat_phase(onset_env, sr, cand, hop_length)
        score = strength * float(tempo_prior_weight(cand, prior_bpm))
        if score > best_score:
            best_bpm, best_score = cand, score
    return best_bpm


# ------------------------------------------------------------------
# Full Step 3 pipeline
# ------------------------------------------------------------------

def track_beats(track: AudioTrack, n_fft: int = N_FFT,
                 hop_length: int = HOP_LENGTH, n_mels: int = 128,
                 tempo_min: float = TEMPO_MIN_BPM,
                 tempo_max: float = TEMPO_MAX_BPM,
                 prior_bpm: float = TEMPO_PRIOR_BPM) -> BeatGrid:
    """
    onset envelope -> autocorrelation tempo -> octave correction ->
    beat phase -> constant-tempo beat grid across the whole track.
    """
    env = onset_envelope(track, n_fft=n_fft, hop_length=hop_length, n_mels=n_mels)
    env_times = frame_times(len(env), track.sr, hop_length=hop_length)

    raw_bpm, _ = estimate_tempo(env, track.sr, hop_length=hop_length,
                                 tempo_min=tempo_min, tempo_max=tempo_max,
                                 prior_bpm=prior_bpm)
    bpm = _resolve_octave(env, track.sr, hop_length, raw_bpm,
                           tempo_min, tempo_max, prior_bpm)

    offset, phase_strength = estimate_beat_phase(env, track.sr, bpm,
                                                  hop_length=hop_length)

    period = 60.0 / bpm
    beat_times = np.arange(offset, track.duration, period)

    # confidence: autocorrelation height at the *final* lag, clipped to [0,1]
    acf = _autocorrelation(env)
    final_lag = int(round(_bpm_to_lag(bpm, track.sr, hop_length)))
    confidence = (float(np.clip(acf[final_lag], 0.0, 1.0))
                  if 0 < final_lag < len(acf) else 0.0)

    return BeatGrid(bpm=bpm, offset=offset, beat_times=beat_times,
                     confidence=confidence, phase_strength=phase_strength,
                     onset_env=env, env_times=env_times)


# ------------------------------------------------------------------
# Visualization / sonification / CLI
# ------------------------------------------------------------------

def plot_beat_grid(track: AudioTrack, grid: BeatGrid, out_path: str = None,
                    show: bool = False):
    """
    2-panel debug figure:
      top    -- onset envelope with the beat grid overlaid (green lines)
      bottom -- autocorrelation vs candidate BPM, with the picked tempo,
                its prior weighting, and the +/- octave positions marked.
    """
    import matplotlib.pyplot as plt

    env = grid.onset_env
    env_times = grid.env_times

    acf = _autocorrelation(env)
    lag_min = max(1, int(np.floor(_bpm_to_lag(TEMPO_MAX_BPM, track.sr, HOP_LENGTH))))
    lag_max = min(len(acf) - 2, int(np.ceil(_bpm_to_lag(TEMPO_MIN_BPM, track.sr, HOP_LENGTH))))
    lags = np.arange(lag_min, lag_max + 1)
    bpm_axis = _lag_to_bpm(lags, track.sr, HOP_LENGTH)

    fig, axes = plt.subplots(2, 1, figsize=(13, 7))

    axes[0].plot(env_times, env, color="steelblue", linewidth=0.9,
                 label="onset envelope")
    for bt in grid.beat_times:
        axes[0].axvline(bt, color="green", linewidth=0.7, alpha=0.6)
    axes[0].axvline(grid.offset, color="red", linewidth=1.4,
                    label=f"offset = {grid.offset * 1000:.0f} ms")
    axes[0].set_xlim(0, min(track.duration, 15))  # first 15s is enough to eyeball
    axes[0].set_xlabel("Time (s)")
    axes[0].set_ylabel("Onset strength")
    axes[0].set_title(
        f"Beat grid -- {track.name}: {grid.bpm:.2f} BPM, "
        f"confidence {grid.confidence:.2f}, phase {grid.phase_strength:.2f} "
        f"(first 15 s shown)"
    )
    axes[0].legend(loc="upper right")

    axes[1].plot(bpm_axis, acf[lags], color="steelblue", linewidth=1,
                 label="autocorrelation")
    axes[1].plot(bpm_axis, tempo_prior_weight(bpm_axis), color="gray",
                 linestyle=":", linewidth=1, label="tempo prior")
    axes[1].axvline(grid.bpm, color="green", linewidth=1.6,
                    label=f"picked = {grid.bpm:.2f} BPM")
    for f, style in ((0.5, ":"), (2.0, ":")):
        cand = grid.bpm * f
        if bpm_axis.min() <= cand <= bpm_axis.max():
            axes[1].axvline(cand, color="orange", linestyle=style, linewidth=1,
                            alpha=0.8)
    axes[1].set_xlabel("Tempo (BPM)")
    axes[1].set_ylabel("Normalized autocorrelation")
    axes[1].legend(loc="upper right")

    fig.tight_layout()
    if out_path:
        os.makedirs(os.path.dirname(out_path), exist_ok=True)
        fig.savefig(out_path, dpi=150)
        print(f"Saved figure -> {out_path}")
    if show:
        plt.show()
    else:
        plt.close(fig)


def sonify_beat_grid(track: AudioTrack, grid: BeatGrid, out_path: str,
                      click_freq: float = 2000.0, click_gain: float = 0.5,
                      original_gain: float = 0.8) -> str:
    """
    Mix a metronome click onto the track at every beat time and save it.
    This is the real listening test for Step 3 (same rationale as
    sonify.py for Step 2): if the metronome locks to the groove for the
    whole song, the tempo *and* phase are right. If it slowly slides out
    of sync, the tempo is slightly off; if it's a steady half-beat off,
    the phase is wrong.
    """
    import librosa
    import soundfile as sf

    clicks = librosa.clicks(times=grid.beat_times, sr=track.sr,
                             click_freq=click_freq, length=len(track.y))
    mixed = original_gain * track.y + click_gain * clicks
    peak = np.max(np.abs(mixed))
    if peak > 1.0:
        mixed = mixed / peak

    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    sf.write(out_path, mixed.astype(np.float32), track.sr)
    return out_path


def main():
    parser = argparse.ArgumentParser(
        description="Step 3: tempo + beat-grid estimation"
    )
    parser.add_argument("audio_path", nargs="?", help="path to audio file")
    parser.add_argument("--synthetic", action="store_true",
                         help="use a generated click track instead of a real file")
    parser.add_argument("--bpm", type=float, default=128.0,
                         help="BPM for --synthetic click track (also printed as ground truth)")
    parser.add_argument("--tempo-min", type=float, default=TEMPO_MIN_BPM)
    parser.add_argument("--tempo-max", type=float, default=TEMPO_MAX_BPM)
    parser.add_argument("--prior", type=float, default=TEMPO_PRIOR_BPM,
                         help="centre of the log-normal tempo prior (BPM)")
    parser.add_argument("--sonify", action="store_true",
                         help="also write a metronome-mixed .wav for a listening test")
    parser.add_argument("--out-dir", default=None)
    args = parser.parse_args()

    if args.synthetic:
        track = generate_click_track(bpm=args.bpm, duration_sec=20.0)
    elif args.audio_path:
        track = load_audio(args.audio_path, sr=DEFAULT_SR)
    else:
        parser.error("Provide an audio_path or use --synthetic")
        return

    grid = track_beats(track, tempo_min=args.tempo_min, tempo_max=args.tempo_max,
                        prior_bpm=args.prior)
    offset_ms, beat_len_ms = grid.to_osu_timing()

    print(f"Track:       {track.name} ({track.duration:.1f}s)")
    print(f"Tempo:       {grid.bpm:.2f} BPM  (beat every {beat_len_ms:.1f} ms)")
    print(f"Offset:      {offset_ms:.1f} ms  (first beat)")
    print(f"Beats:       {grid.n_beats}")
    print(f"Confidence:  {grid.confidence:.3f} (autocorrelation periodicity)")
    print(f"Phase str.:  {grid.phase_strength:.3f} (on-beat / overall onset energy)")
    print(f".osu timing: offset={offset_ms:.0f}ms, beatLength={beat_len_ms:.3f}ms")
    if args.synthetic:
        err = grid.bpm - args.bpm
        print(f"Ground truth {args.bpm:.2f} BPM -> error {err:+.2f} BPM")
    print("First beats: " + ", ".join(f"{t:.3f}" for t in grid.beat_times[:8]))

    project_root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    out_dir = args.out_dir or os.path.join(project_root, "data", "output")

    plot_beat_grid(track, grid,
                   out_path=os.path.join(out_dir, f"{track.name}_beatgrid.png"))

    if args.sonify:
        out = sonify_beat_grid(track, grid,
                               os.path.join(out_dir, f"{track.name}_metronome.wav"))
        print(f"Saved metronome mix -> {out}")
        print("Listen in headphones: the click should stay locked to the "
              "beat for the whole song. A slow slide => tempo slightly off; "
              "a constant half-beat offset => phase wrong.")


if __name__ == "__main__":
    main()
