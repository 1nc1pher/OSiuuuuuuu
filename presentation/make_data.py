"""
presentation/make_data.py

Runs the real pipeline functions on one song and writes everything the
slides plot: CSV files for pgfplots, a spectrogram image, and a
`stats.tex` of numbers the slides quote. Nothing here is hand-made, so
every curve in the deck is exactly what the generator computed.

    python presentation/make_data.py data/raw/bad_apple.mp3 --window 40 46
"""

import argparse
import csv
import os
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.append(os.path.join(os.path.dirname(HERE), "src"))

import librosa  # noqa: E402

from audio.loader import load_audio  # noqa: E402
from audio.visualize import N_FFT, HOP_LENGTH, compute_mel_spectrogram, frame_times  # noqa: E402
from onset.detector import (spectral_flux, band_limited_flux, normalize,  # noqa: E402
                            adaptive_threshold, local_median, detect_onsets)
from onset.beat_tracker import (onset_envelope, _autocorrelation, _lag_to_bpm,  # noqa: E402
                                _bpm_to_lag, tempo_prior_weight, estimate_tempo,
                                track_beats, _phase_scores, TEMPO_MIN_BPM, TEMPO_MAX_BPM)
from mapping.object_classifier import (snap_onsets, enforce_min_spacing,  # noqa: E402
                                       _rms_envelope, classify)
from mapping.difficulty import PRESETS, TIER_ORDER, build_map_detailed, rhythm_slot_count  # noqa: E402
from mapping.hit_object import circle_radius  # noqa: E402
from mapping.slider_generator import slider_body_points  # noqa: E402

OUT = os.path.join(HERE, "data")


def write_csv(name, header, rows):
    with open(os.path.join(OUT, name), "w", newline="") as f:
        w = csv.writer(f)
        w.writerow(header)
        for r in rows:
            w.writerow([f"{v:.5g}" if isinstance(v, (float, np.floating)) else v for v in r])


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("audio")
    ap.add_argument("--window", nargs=2, type=float, default=[40.0, 46.0])
    ap.add_argument("--tier", default="Hard")
    args = ap.parse_args()

    os.makedirs(OUT, exist_ok=True)
    t0, t1 = args.window
    preset = PRESETS[args.tier]

    track = load_audio(args.audio)
    sr = track.sr
    stats = {}
    stats["SampleRate"] = sr
    stats["Duration"] = f"{track.duration:.1f}"
    stats["NFFT"] = N_FFT
    stats["Hop"] = HOP_LENGTH
    stats["WinMs"] = f"{1000 * N_FFT / sr:.1f}"
    stats["HopMs"] = f"{1000 * HOP_LENGTH / sr:.1f}"
    stats["FrameRate"] = f"{sr / HOP_LENGTH:.1f}"
    stats["BinHz"] = f"{sr / N_FFT:.1f}"
    stats["WinStart"] = f"{t0:g}"
    stats["WinEnd"] = f"{t1:g}"
    stats["Tier"] = args.tier

    # ---- waveform (min/max envelope, so a 6 s excerpt stays a few hundred points)
    a, b = int(t0 * sr), int(t1 * sr)
    seg = track.y[a:b]
    bins = 600
    edges = np.linspace(0, len(seg), bins + 1).astype(int)
    rows = []
    for i in range(bins):
        chunk = seg[edges[i]:edges[i + 1]]
        t = t0 + edges[i] / sr
        rows.append((t, float(chunk.min()), float(chunk.max())))
    write_csv("wave.csv", ["t", "lo", "hi"], rows)

    # ---- mel spectrogram (image for the excerpt)
    mel_db = compute_mel_spectrogram(track)
    times = frame_times(mel_db.shape[1], sr)
    f0, f1 = np.searchsorted(times, [t0, t1])
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    ex = mel_db[:, f0:f1]
    plt.imsave(os.path.join(OUT, "melspec.png"), ex, origin="lower", cmap="magma",
               vmin=ex.max() - 70, vmax=ex.max())
    stats["NFrames"] = mel_db.shape[1]

    # ---- zoomed waveform for the STFT framing animation (first 0.3 s)
    za, zb = int(t0 * sr), int((t0 + 0.3) * sr)
    zseg = track.y[za:zb]
    zstep = max(1, len(zseg) // 900)
    write_csv("wave_zoom.csv", ["ms", "y"],
              [(1000 * i / sr, float(zseg[i])) for i in range(0, len(zseg), zstep)])

    # ---- mel filterbank (a handful of filters, for the explainer slide)
    fb = librosa.filters.mel(sr=sr, n_fft=N_FFT, n_mels=24, fmax=sr / 2)
    freqs = np.linspace(0, sr / 2, fb.shape[1])
    keep = freqs <= 4000
    rows = []
    for k, f in enumerate(freqs[keep]):
        rows.append([f] + [float(fb[m, k] / fb[m].max()) for m in range(0, 12)])
    write_csv("melfb.csv", ["f"] + [f"m{m}" for m in range(12)], rows)

    # ---- spectral flux, threshold, peaks (the tier's sensitivity)
    flux = normalize(spectral_flux(mel_db))
    med = local_median(flux, sr)
    thr = adaptive_threshold(flux, sr, margin=preset.onset_margin, delta=preset.onset_delta)
    rows = [(times[i], flux[i], med[i], thr[i]) for i in range(f0, f1)]
    write_csv("flux.csv", ["t", "flux", "median", "thr"], rows)

    onsets = detect_onsets(track, margin=preset.onset_margin, delta=preset.onset_delta)
    exo = [o for o in onsets if t0 <= o.time < t1]
    write_csv("peaks.csv", ["t", "s", "band"],
              [(o.time, o.strength, o.dominant_band()) for o in exo])
    stats["OnsetsTotal"] = len(onsets)
    stats["OnsetsWindow"] = len(exo)
    stats["Margin"] = preset.onset_margin
    stats["Delta"] = preset.onset_delta

    # ---- the flux computation on the strongest onset in the window:
    # the frame before it and the onset frame, 128 mel bands grouped
    # into 16 so the bars are readable
    # Shown in dB (relative to the frame's loudest group, floored at
    # -60 dB) because in linear power the bass group dwarfs the rest; the
    # onset picked is the window's most broadband attack, where most
    # groups rise at once.
    power = librosa.db_to_power(mel_db)
    groups = np.array_split(np.arange(power.shape[0]), 16)

    def grouped_db(frame):
        g = np.array([power[idx, frame].sum() for idx in groups])
        return 10 * np.log10(g + 1e-12)

    def rises(o):
        return int(np.sum(grouped_db(o.frame) - grouped_db(o.frame - 1) > 1.0))

    best = max(exo, key=lambda o: (rises(o), o.strength))
    prev_db, cur_db = grouped_db(best.frame - 1), grouped_db(best.frame)
    top = max(prev_db.max(), cur_db.max())
    prev_g = np.clip((prev_db - top + 60) / 60, 0, 1)
    cur_g = np.clip((cur_db - top + 60) / 60, 0, 1)
    write_csv("fluxbars.csv", ["band", "prev", "cur", "diff", "rect"],
              [(i + 1, prev_g[i], cur_g[i], cur_g[i] - prev_g[i], max(0.0, cur_g[i] - prev_g[i]))
               for i in range(16)])
    stats["FluxRises"] = rises(best)
    stats["FluxOnsetTime"] = f"{best.time:.2f}"

    # ---- band-limited flux
    bands = {k: normalize(v) for k, v in band_limited_flux(mel_db, sr).items()}
    rows = [(times[i], bands["low"][i], bands["mid"][i], bands["high"][i]) for i in range(f0, f1)]
    write_csv("bands.csv", ["t", "low", "mid", "high"], rows)
    counts = {"low": 0, "mid": 0, "high": 0}
    for o in onsets:
        counts[o.dominant_band()] = counts.get(o.dominant_band(), 0) + 1
    stats["BandLow"], stats["BandMid"], stats["BandHigh"] = counts["low"], counts["mid"], counts["high"]

    # ---- autocorrelation tempo
    env = onset_envelope(track, mel_db=mel_db)
    acf = _autocorrelation(env)
    lag_min = max(1, int(np.floor(_bpm_to_lag(TEMPO_MAX_BPM, sr, HOP_LENGTH))))
    lag_max = int(np.ceil(_bpm_to_lag(TEMPO_MIN_BPM, sr, HOP_LENGTH)))
    lags = np.arange(lag_min, lag_max + 1)
    bpms = _lag_to_bpm(lags, sr, HOP_LENGTH)
    prior = tempo_prior_weight(bpms)
    rows = [(float(bpms[i]), float(acf[l]), float(prior[i]), float(acf[l] * prior[i]))
            for i, l in enumerate(lags)]
    rows.sort()
    write_csv("acf.csv", ["bpm", "acf", "prior", "weighted"], rows)
    # lag-domain view for the "what autocorrelation is" slide
    rows = [(l * HOP_LENGTH / sr, float(acf[l])) for l in range(0, min(len(acf), int(2.5 * sr / HOP_LENGTH)))]
    write_csv("acf_lag.csv", ["lag", "acf"], rows)

    raw_bpm, raw_conf = estimate_tempo(env, sr)
    grid = track_beats(track)
    stats["RawBPM"] = f"{raw_bpm:.2f}"
    stats["BPM"] = f"{grid.bpm:.2f}"
    stats["Offset"] = f"{grid.offset * 1000:.0f}"
    stats["Confidence"] = f"{grid.confidence:.2f}"
    stats["PhaseStrength"] = f"{grid.phase_strength:.2f}"
    stats["BeatMs"] = f"{grid.beat_period * 1000:.1f}"
    stats["NBeats"] = grid.n_beats
    stats["LagFrames"] = f"{_bpm_to_lag(grid.bpm, sr, HOP_LENGTH):.2f}"
    # the wrong phases the beat-phase slide shows first
    period_ms = grid.beat_period * 1000
    stats["PhaseHalfMs"] = f"{(grid.offset * 1000 + period_ms / 2) % period_ms:.1f}"
    stats["PhaseQuarterMs"] = f"{(grid.offset * 1000 + period_ms / 4) % period_ms:.1f}"
    stats["BeatSec"] = f"{grid.beat_period:.5f}"

    # octave candidates: phase strength x prior
    from onset.beat_tracker import estimate_beat_phase
    for name, factor in (("Half", 0.5), ("One", 1.0), ("Double", 2.0)):
        cand = raw_bpm * factor
        _, ps = estimate_beat_phase(env, sr, cand)
        pw = float(tempo_prior_weight(cand))
        stats[f"Oct{name}BPM"] = f"{cand:.1f}"
        stats[f"Oct{name}Phase"] = f"{ps:.2f}"
        stats[f"Oct{name}Prior"] = f"{pw:.2f}"
        stats[f"Oct{name}Score"] = f"{ps * pw:.2f}"

    # phase scan
    period = _bpm_to_lag(grid.bpm, sr, HOP_LENGTH)
    scores = _phase_scores(env, period)
    write_csv("phase.csv", ["ms", "score"],
              [(i * HOP_LENGTH / sr * 1000, float(s)) for i, s in enumerate(scores)])

    # envelope + beat grid in the excerpt
    write_csv("env.csv", ["t", "env"], [(times[i], env[i]) for i in range(f0, f1)])
    write_csv("beats.csv", ["t"], [(t,) for t in grid.beat_times if t0 <= t < t1])

    # ---- snapping: raw onset -> grid slot (tier resolution 1/4)
    step = grid.beat_period / 4
    k0 = int(np.ceil((t0 - grid.offset) / step))
    k1 = int(np.floor((t0 + 3.0 - grid.offset) / step))
    write_csv("grid.csv", ["t"], [(grid.offset + k * step,) for k in range(k0, k1 + 1)])

    snapped = snap_onsets(onsets, grid, division=4)
    rows = []
    for s in snapped:
        if t0 <= s.time < t0 + 3.0:
            rows.append((s.raw_time, s.time, s.snap, s.strength))
    write_csv("snap.csv", ["raw", "snapped", "label", "s"], rows)
    stats["SnapSlots"] = len(snapped)
    stats["SnapMerged"] = sum(s.merged for s in snapped)

    # ---- RMS sustain
    rms, rms_t = _rms_envelope(track)
    peak = float(rms.max())
    r0, r1 = np.searchsorted(rms_t, [t0, t1])
    write_csv("rms.csv", ["t", "rms"], [(rms_t[i], rms[i] / peak) for i in range(r0, r1)])

    # ---- objects for the excerpt, one tier, with the classification trace
    trace = []
    classify(onsets, grid, track, snap_division=preset.snap_division,
             min_spacing_beats=preset.min_spacing_beats,
             spinner_min_beats=preset.spinner_min_beats,
             slider_min_beats=preset.slider_min_beats,
             slider_max_beats=preset.slider_max_beats,
             stream_min_len=preset.stream_min_len,
             sustain_threshold=preset.sustain_threshold, trace=trace)

    _, objs, _ = build_map_detailed(track, preset)
    rows = []
    for o in objs:
        if t0 <= o.time < t1:
            rows.append((o.time, o.end() if o.kind != "circle" else o.time, o.kind, o.strength))
    write_csv("objects.csv", ["t", "end", "kind", "s"], rows)

    # the .osu lines Step 6 writes for this excerpt
    from export.osu_writer import timing_point_line, hit_object_line
    diff = preset.to_osu_difficulty_section()
    lines = ["[Difficulty]"] + [f"{k}:{v:g}" for k, v in diff.items()]
    lines += ["", "[TimingPoints]", timing_point_line(grid), "", "[HitObjects]"]
    shown = {"circle": 0, "slider": 0, "spinner": 0}
    for o in objs:
        if o.time >= t0 and shown[o.kind] < 1:
            lines.append(hit_object_line(o))
            shown[o.kind] += 1
    with open(os.path.join(OUT, "osu_excerpt.txt"), "w") as f:
        f.write("\n".join(lines) + "\n")

    # playfield: a short run of consecutive objects (what's on screen together)
    pf = [o for o in objs if o.kind != "spinner" and t0 + 1.0 <= o.time < t0 + 3.2][:9]
    # Written as ready-to-draw TikZ in osu!pixel coordinates (the slide
    # puts it in a scope scaled by \PFScale cm per pixel, y flipped). Object
    # i appears on overlay i+1, so the pattern builds in play order.
    scale = 0.0135  # cm per osu!pixel: the 512x384 field is 6.9 x 5.2 cm
    r = circle_radius(preset.circle_size)
    with open(os.path.join(OUT, "playfield.tex"), "w") as f:
        f.write(f"% {args.tier}: {len(pf)} consecutive objects from {t0 + 1:.1f}s (generated)\n")
        prev_end = None
        for i, o in enumerate(pf):
            step = i + 2
            body = slider_body_points(o) if o.kind == "slider" else None
            if prev_end is not None:
                f.write(f"\\draw<{step}->[pfcursor] ({prev_end[0]:.1f},{prev_end[1]:.1f}) -- ({o.x:.1f},{o.y:.1f});\n")
            if body:
                pts = body[::max(1, len(body) // 24)] + [body[-1]]
                path = " -- ".join(f"({p[0]:.1f},{p[1]:.1f})" for p in pts)
                f.write(f"\\draw<{step}->[pfbody, line width={2 * r * scale:.3f}cm] {path};\n")
                f.write(f"\\draw<{step}->[pfbodyedge, line width={2 * r * scale - 0.06:.3f}cm] {path};\n")
                f.write(f"\\node<{step}->[pfcircle, minimum size={2 * r * scale:.3f}cm] at ({body[-1][0]:.1f},{body[-1][1]:.1f}) {{}};\n")
                prev_end = body[-1]
            else:
                prev_end = (o.x, o.y)
            f.write(f"\\node<{step}->[pfcircle, minimum size={2 * r * scale:.3f}cm] at ({o.x:.1f},{o.y:.1f}) {{{i + 1}}};\n")
        f.write(f"\\def\\PFCount{{{len(pf)}}}\n")
    stats["PFScale"] = scale
    stats["PFRadius"] = f"{r:.1f}"
    stats["PFCount"] = len(pf)

    # ---- per tier density
    rows = []
    for name in TIER_ORDER:
        p = PRESETS[name]
        _, objs_t, _ = build_map_detailed(track, p)
        kinds = [o.kind for o in objs_t]
        slots = rhythm_slot_count(track, p, grid)
        rows.append((name, p.stars, slots, len(objs_t), kinds.count("circle"),
                     kinds.count("slider"), kinds.count("spinner"),
                     f"{len(objs_t) / track.duration:.2f}"))
    write_csv("tiers.csv", ["tier", "stars", "slots", "objects", "circles", "sliders", "spinners", "rate"], rows)
    for r in rows:
        stats[f"Slots{r[0]}"] = r[2]
        stats[f"Obj{r[0]}"] = r[3]

    with open(os.path.join(OUT, "stats.tex"), "w") as f:
        f.write(f"% generated by make_data.py from {os.path.basename(args.audio)}\n")
        f.write(f"\\def\\SongFile{{{os.path.basename(args.audio).replace('_', '\\_')}}}\n")
        for k, v in stats.items():
            f.write(f"\\def\\stat{k}{{{v}}}\n")

    print("wrote", OUT)
    for k, v in stats.items():
        print(f"  {k} = {v}")


if __name__ == "__main__":
    main()
