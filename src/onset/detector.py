"""
src/onset/detector.py

Step 2 of the pipeline: onset detection via spectral flux.

THEORY (see chat for full explanation) -- short version:
  1. Take the magnitude spectrogram (already computed in Step 1's mel
     spectrogram machinery).
  2. Compute "spectral flux": how much the spectrum's energy increased
     from one frame to the next, summed only over positive changes.
     A drum hit / note attack shows up as a sharp spike in this signal.
  3. Smooth the flux, then threshold it *adaptively* (local median +
     margin) rather than with one fixed number, because a quiet verse
     and a loud chorus need different sensitivity.
  4. Peak-pick: keep only local maxima above threshold, with a minimum
     spacing so one hit doesn't get detected twice.

We also compute flux *per frequency band* (low/mid/high), not just one
global number. This costs nothing extra (we already have the full
spectrogram) and pays off in Step 4, where we need to tell a kick drum
(low-band onset) apart from a hi-hat (high-band onset) to decide whether
something becomes a circle vs. gets folded into a slider/stream.
"""

from dataclasses import dataclass, field
import argparse
import os
import sys

import numpy as np
import librosa
import matplotlib.pyplot as plt

sys.path.append(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from audio.loader import load_audio, generate_click_track, AudioTrack, DEFAULT_SR
from audio.visualize import N_FFT, HOP_LENGTH, compute_mel_spectrogram, frame_times


# ---- Band split (Hz) --------------------------------------------------
# Rough perceptual/instrumental split used for band-limited flux:
#   low  : kick drum, bass                -> 20-200 Hz
#   mid  : snare, vocals, most melodic content -> 200-2000 Hz
#   high : hi-hats, cymbals, transients    -> 2000-10000 Hz
BAND_EDGES_HZ = {
    "low": (20, 200),
    "mid": (200, 2000),
    "high": (2000, 10000),
}

# Peak-picking defaults
MIN_ONSET_SPACING_SEC = 0.05   # 50ms -- fastest realistic distinct hits (~1200 BPM 1/16 territory)
ADAPTIVE_MEDIAN_WINDOW_SEC = 0.5
ADAPTIVE_THRESHOLD_MARGIN = 1.5   # multiplier on local median, tune per song
ADAPTIVE_THRESHOLD_DELTA = 0.05   # additive floor -- see note in adaptive_threshold()


@dataclass
class Onset:
    """A single detected onset event."""
    time: float             # seconds
    frame: int               # STFT frame index
    strength: float          # global spectral flux value (post-normalization)
    band_energy: dict = field(default_factory=dict)  # {"low": x, "mid": y, "high": z}

    def dominant_band(self) -> str:
        """Which frequency band contributed most energy to this onset.
        Used downstream (Step 4) as a hint for circle/slider/spinner typing."""
        if not self.band_energy:
            return "unknown"
        return max(self.band_energy, key=self.band_energy.get)


def _mel_freqs(sr: int, n_mels: int = 128) -> np.ndarray:
    """Center frequency (Hz) of each mel bin, for mapping mel bins -> bands."""
    return librosa.mel_frequencies(n_mels=n_mels, fmin=0, fmax=sr / 2)


def spectral_flux(mel_power_db: np.ndarray) -> np.ndarray:
    """
    Global spectral flux: L2 norm of the positive-only frame-to-frame
    difference in the (linear-power) spectrogram, summed across all
    frequency bins.

    We convert the incoming dB spectrogram back to linear power first --
    flux computed directly on dB values over-weights quiet regions and
    under-weights the loud transients we actually care about.

    Positive-only ("half-wave rectified") difference is the standard trick:
    we want to detect energy *appearing* (an attack), not energy decaying
    away (the tail of a note), so negative differences are clipped to 0
    before summing.
    """
    power = librosa.db_to_power(mel_power_db)
    diff = np.diff(power, axis=1)
    diff = np.maximum(diff, 0.0)                # half-wave rectify
    flux = np.sqrt(np.sum(diff ** 2, axis=0))    # L2 norm across freq bins per frame
    flux = np.concatenate([[0.0], flux])          # pad frame 0 (no previous frame to diff against)
    return flux


def band_limited_flux(mel_power_db: np.ndarray, sr: int,
                       n_mels: int = 128) -> dict:
    """
    Same idea as spectral_flux(), but computed separately within each
    frequency band defined in BAND_EDGES_HZ. Returns a dict of
    {band_name: flux_array}, each the same length as the global flux.
    """
    mel_freqs = _mel_freqs(sr, n_mels)
    power = librosa.db_to_power(mel_power_db)

    bands = {}
    for name, (lo, hi) in BAND_EDGES_HZ.items():
        mask = (mel_freqs >= lo) & (mel_freqs < hi)
        if not np.any(mask):
            bands[name] = np.zeros(power.shape[1])
            continue
        sub = power[mask, :]
        diff = np.diff(sub, axis=1)
        diff = np.maximum(diff, 0.0)
        flux = np.sqrt(np.sum(diff ** 2, axis=0))
        flux = np.concatenate([[0.0], flux])
        bands[name] = flux
    return bands


def normalize(x: np.ndarray) -> np.ndarray:
    """Scale to [0, 1]. Guards against a silent/constant signal (max==min)."""
    lo, hi = np.min(x), np.max(x)
    if hi - lo < 1e-12:
        return np.zeros_like(x)
    return (x - lo) / (hi - lo)


def adaptive_threshold(flux: np.ndarray, sr: int, hop_length: int = HOP_LENGTH,
                        window_sec: float = ADAPTIVE_MEDIAN_WINDOW_SEC,
                        margin: float = ADAPTIVE_THRESHOLD_MARGIN,
                        delta: float = ADAPTIVE_THRESHOLD_DELTA) -> np.ndarray:
    """
    Local adaptive threshold, following the standard form from Bello et al.
    (2005) "A Tutorial on Onset Detection in Music Signals":

        threshold[n] = delta + margin * median(flux[n-w : n+w])

    Why adaptive and not a fixed cutoff: a song's dynamics change --
    a quiet intro and a loud drop need different sensitivity. A moving
    median tracks the *local* noise floor, so a threshold that's too
    strict in the quiet section and too loose in the loud section is
    avoided. Median (not mean) because it's robust to the onset spikes
    themselves skewing the local average upward.

    Why the additive `delta` term matters (found this the hard way while
    validating against a synthetic noise-floor test): in near-silent
    passages the local median collapses toward 0, and a *purely*
    multiplicative threshold (median * margin) collapses toward 0 right
    along with it -- so tiny noise fluctuations start getting picked up
    as false onsets. `delta` puts a hard floor under the threshold so
    silence stays silent regardless of how low the local median gets.
    """
    frames_per_window = max(1, int(window_sec * sr / hop_length))
    # ensure odd window for a centered median filter
    if frames_per_window % 2 == 0:
        frames_per_window += 1

    from scipy.ndimage import median_filter
    local_median = median_filter(flux, size=frames_per_window, mode="reflect")
    return delta + local_median * margin


def pick_peaks(flux: np.ndarray, threshold: np.ndarray, sr: int,
               hop_length: int = HOP_LENGTH,
               min_spacing_sec: float = MIN_ONSET_SPACING_SEC) -> np.ndarray:
    """
    Return frame indices of local maxima in `flux` that exceed `threshold`,
    enforcing a minimum spacing between consecutive picks.

    Local maximum test: flux[i] > flux[i-1] and flux[i] >= flux[i+1]
    (strict on one side only, so we don't miss a peak on a perfectly flat
    plateau -- rare with real audio but cheap to guard against).
    """
    above = flux > threshold
    candidates = []
    for i in range(1, len(flux) - 1):
        if above[i] and flux[i] > flux[i - 1] and flux[i] >= flux[i + 1]:
            candidates.append(i)

    if not candidates:
        return np.array([], dtype=int)

    import math
    # ceil, not int()/truncation: truncating could round the frame-spacing
    # requirement *down*, silently enforcing a smaller gap than the
    # min_spacing_sec the caller asked for. ceil guarantees the enforced
    # spacing is always >= the requested time.
    min_spacing_frames = max(1, math.ceil(min_spacing_sec * sr / hop_length))

    # greedy selection: walk candidates in time order, keep a peak only if
    # it's far enough from the last kept peak; if two candidates are within
    # the spacing window, keep the stronger one.
    kept = [candidates[0]]
    for c in candidates[1:]:
        if c - kept[-1] >= min_spacing_frames:
            kept.append(c)
        elif flux[c] > flux[kept[-1]]:
            kept[-1] = c  # replace with the stronger nearby candidate

    return np.array(kept, dtype=int)


def detect_onsets(track: AudioTrack, n_fft: int = N_FFT,
                   hop_length: int = HOP_LENGTH, n_mels: int = 128,
                   margin: float = ADAPTIVE_THRESHOLD_MARGIN,
                   delta: float = ADAPTIVE_THRESHOLD_DELTA) -> list:
    """
    Full Step 2 pipeline: mel spectrogram -> global + band flux ->
    adaptive threshold -> peak picking -> list[Onset].
    """
    mel_db = compute_mel_spectrogram(track, n_fft=n_fft, hop_length=hop_length,
                                      n_mels=n_mels)
    flux = spectral_flux(mel_db)
    flux_norm = normalize(flux)

    bands = band_limited_flux(mel_db, track.sr, n_mels=n_mels)
    bands_norm = {k: normalize(v) for k, v in bands.items()}

    thresh = adaptive_threshold(flux_norm, track.sr, hop_length=hop_length,
                                 margin=margin, delta=delta)
    peak_frames = pick_peaks(flux_norm, thresh, track.sr, hop_length=hop_length)

    times = frame_times(len(flux_norm), track.sr, hop_length=hop_length)

    onsets = []
    for f in peak_frames:
        onsets.append(Onset(
            time=float(times[f]),
            frame=int(f),
            strength=float(flux_norm[f]),
            band_energy={k: float(v[f]) for k, v in bands_norm.items()},
        ))
    return onsets


# ------------------------------------------------------------------
# Visualization / CLI
# ------------------------------------------------------------------

def plot_onsets(track: AudioTrack, onsets: list, out_path: str = None,
                 show: bool = False, margin: float = ADAPTIVE_THRESHOLD_MARGIN,
                 delta: float = ADAPTIVE_THRESHOLD_DELTA):
    """
    2-panel figure: mel spectrogram with onset markers overlaid, and the
    normalized flux curve with the adaptive threshold and picked peaks.
    This is your main debugging view for tuning `margin` / window sizes.
    """
    import librosa.display

    mel_db = compute_mel_spectrogram(track)
    flux = normalize(spectral_flux(mel_db))
    thresh = adaptive_threshold(flux, track.sr, margin=margin, delta=delta)
    times = frame_times(len(flux), track.sr)

    fig, axes = plt.subplots(2, 1, figsize=(13, 7), sharex=True)

    img = librosa.display.specshow(mel_db, sr=track.sr, hop_length=HOP_LENGTH,
                                    x_axis="time", y_axis="mel", ax=axes[0],
                                    cmap="magma")
    for o in onsets:
        axes[0].axvline(o.time, color="cyan", linewidth=0.8, alpha=0.8)
    axes[0].set_title(f"Mel spectrogram + detected onsets ({len(onsets)} found) -- {track.name}")
    fig.colorbar(img, ax=axes[0], format="%+2.0f dB")

    axes[1].plot(times, flux, label="spectral flux (normalized)", color="steelblue", linewidth=1)
    axes[1].plot(times, thresh, label="adaptive threshold", color="orange",
                 linestyle="--", linewidth=1)
    onset_times = [o.time for o in onsets]
    onset_vals = [o.strength for o in onsets]
    axes[1].scatter(onset_times, onset_vals, color="red", zorder=5, s=25,
                     label="picked onsets")
    axes[1].set_xlabel("Time (s)")
    axes[1].set_ylabel("Flux")
    axes[1].legend(loc="upper right")
    axes[1].set_xlim(0, track.duration)

    fig.tight_layout()
    if out_path:
        os.makedirs(os.path.dirname(out_path), exist_ok=True)
        fig.savefig(out_path, dpi=150)
        print(f"Saved figure -> {out_path}")
    if show:
        plt.show()
    else:
        plt.close(fig)


def main():
    parser = argparse.ArgumentParser(description="Step 2: spectral-flux onset detection")
    parser.add_argument("audio_path", nargs="?", help="path to audio file")
    parser.add_argument("--synthetic", action="store_true",
                         help="use a generated click track instead of a real file")
    parser.add_argument("--bpm", type=float, default=128.0)
    parser.add_argument("--margin", type=float, default=ADAPTIVE_THRESHOLD_MARGIN,
                         help="adaptive threshold margin (higher = fewer onsets detected)")
    parser.add_argument("--delta", type=float, default=ADAPTIVE_THRESHOLD_DELTA,
                         help="adaptive threshold floor (higher = ignore quieter onsets)")
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
    print(f"Detected {len(onsets)} onsets in {track.name} ({track.duration:.1f}s)")
    if onsets:
        implied_bpm_from_spacing(onsets)
    for o in onsets[:15]:
        print(f"  t={o.time:6.3f}s  strength={o.strength:.3f}  "
              f"dominant_band={o.dominant_band()}")
    if len(onsets) > 15:
        print(f"  ... and {len(onsets) - 15} more")

    project_root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    out_dir = args.out_dir or os.path.join(project_root, "data", "output")
    out_path = os.path.join(out_dir, f"{track.name}_onsets.png")
    plot_onsets(track, onsets, out_path=out_path, margin=args.margin, delta=args.delta)


def implied_bpm_from_spacing(onsets: list):
    """Quick eyeball check (not real beat tracking, that's Step 3):
    median spacing between consecutive onsets, converted to BPM.
    Only meaningful for steady, one-onset-per-beat material like the
    synthetic click track."""
    if len(onsets) < 2:
        return
    times = np.array([o.time for o in onsets])
    spacings = np.diff(times)
    median_spacing = np.median(spacings)
    if median_spacing > 0:
        bpm = 60.0 / median_spacing
        print(f"  (median onset spacing implies ~{bpm:.1f} BPM -- "
              f"sanity check only, real tempo tracking is Step 3)")


if __name__ == "__main__":
    main()
