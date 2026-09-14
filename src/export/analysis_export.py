"""
src/export/analysis_export.py

A second export artifact, written next to the .osu/.osz the pipeline
already produces: what the DSP actually *saw*.

The .osu format has room for the result of the analysis (a circle, here,
at this time) and none at all for its reasoning -- how strong the onset
was, which frequency band it came from, what subdivision it snapped to,
where the beat grid landed. That reasoning is the interesting part of
this project and it currently evaporates the moment the map is written.

So at the end of a run this module writes two files into the beatmap's
own folder:

    spectrogram.png  -- the mel spectrogram, plain: no axes, no title,
                        no colorbar. The debug figure in
                        src/audio/visualize.py is a developer readout;
                        this copy is a game asset.
    analysis.json    -- onsets, beat grid, and per-tier hit objects with
                        the provenance fields the .osu file drops.

The frontend (FRONTEND_PLAN.md Phase 7) animates a step-by-step reveal
from these two files. That is the whole coupling: files in a folder,
same as the .osz. Nothing here streams, blocks or talks to the frontend,
and a frontend that never runs changes nothing about this.

Both files are additive -- build_beatmap_set() has already zipped the
.osz by the time these are written, so neither ends up inside it.
"""

import json
import os

import numpy as np

import matplotlib
matplotlib.use("Agg")           # no display needed; this only ever writes files
import matplotlib.pyplot as plt


ANALYSIS_FILENAME = "analysis.json"
SPECTROGRAM_FILENAME = "spectrogram.png"

# The frontend stretches this across a screen-width panel, so the mel
# spectrogram's native frame count (~43 columns/second -- over 15,000 for
# a six-minute track) is far more than it can show. Wider than the widest
# display it will ever be drawn on is just file size.
MAX_SPECTROGRAM_WIDTH = 2048

SPECTROGRAM_COLORMAP = "magma"

# dB window the colormap spans. compute_mel_spectrogram() returns dB
# relative to the track's own peak, so 0 is the loudest moment in the
# song and -45 is well below anything audible in a mix.
#
# Two reasons to pin it rather than let the image auto-scale to its own
# min/max. The downsampling below takes a maximum per block, which lifts
# the floor and washes a real song out to near-uniform bright pink --
# every streak still there, none of them visible. And an auto-scaled
# image means a quiet song and a loud one get different scales, so the
# same brightness means something different per map.
SPECTROGRAM_DB_FLOOR = -45.0
SPECTROGRAM_DB_CEILING = 0.0

# Rounding for the JSON. Times to the millisecond (finer than any
# animation can show, and the .osu format itself is integer milliseconds),
# positions to a tenth of an osu!pixel, strengths to 3 decimals. On a real
# five-tier map this is the difference between a ~2 MB file and a ~700 KB
# one, for digits nothing downstream can use.
TIME_DECIMALS = 3
POSITION_DECIMALS = 1
STRENGTH_DECIMALS = 3


def downsample_columns(mel_db: np.ndarray, max_width: int = MAX_SPECTROGRAM_WIDTH) -> np.ndarray:
    """
    Shrink a (n_mels, n_frames) spectrogram along time to at most
    `max_width` columns, by taking the maximum over each block of frames.

    Max rather than mean on purpose: what makes this image worth showing
    is the vertical streaks where percussive onsets are, and those are
    single bright frames. Averaging blurs exactly the feature the whole
    visualization is about; a max keeps every streak visible at any
    downsampling ratio.
    """
    n_frames = mel_db.shape[1]

    if n_frames <= max_width or max_width <= 0:
        return mel_db

    # Pad up to a whole number of blocks so the last (short) block doesn't
    # need a separate case -- padding with the array's own minimum keeps
    # the padding silent rather than inventing a bright edge.
    block = int(np.ceil(n_frames / max_width))
    padded_width = block * int(np.ceil(n_frames / block))
    pad = padded_width - n_frames

    if pad:
        mel_db = np.pad(mel_db, ((0, 0), (0, pad)), mode="constant",
                         constant_values=float(mel_db.min()))

    blocks = mel_db.reshape(mel_db.shape[0], -1, block)
    return blocks.max(axis=2)


def write_spectrogram_png(path: str, mel_db: np.ndarray,
                           max_width: int = MAX_SPECTROGRAM_WIDTH) -> str:
    """
    Write the mel spectrogram as a plain image: one pixel per (mel bin,
    time block), low frequencies at the bottom, no plot chrome at all.

    plt.imsave rather than a figure + specshow for exactly that reason --
    a figure would bake in axes, margins and a background the frontend
    would then have to mask around.
    """
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)

    reduced = downsample_columns(mel_db, max_width=max_width)

    # origin="lower": row 0 is the lowest mel bin, and an image's row 0 is
    # its top. Without this the spectrogram comes out upside down -- bass
    # along the top -- which reads as wrong to anyone who has ever seen one.
    plt.imsave(path, reduced, cmap=SPECTROGRAM_COLORMAP, origin="lower",
               vmin=SPECTROGRAM_DB_FLOOR, vmax=SPECTROGRAM_DB_CEILING)

    return path


def onset_record(onset) -> dict:
    """One detected onset, as the frontend reads it: {time, strength, band}."""
    return {
        "time": round(float(onset.time), TIME_DECIMALS),
        "strength": round(float(onset.strength), STRENGTH_DECIMALS),
        "band": onset.dominant_band(),
    }


def beat_grid_record(grid) -> dict:
    """The Step 3 beat grid: tempo, phase, how sure it is, and every beat."""
    return {
        "bpm": round(float(grid.bpm), 2),
        "offset": round(float(grid.offset), TIME_DECIMALS),
        "confidence": round(float(grid.confidence), STRENGTH_DECIMALS),
        "beatTimes": [round(float(t), TIME_DECIMALS) for t in grid.beat_times],
    }


def hit_object_record(obj) -> dict:
    """
    One placed object. `kind`/`time`/`x`/`y` are what the .osu file also
    carries; `strength`/`band`/`snap` are the provenance it has no room
    for, and are the reason this file exists.
    """
    record = {
        "kind": obj.kind,
        "time": round(float(obj.time), TIME_DECIMALS),
        "x": round(float(obj.x), POSITION_DECIMALS),
        "y": round(float(obj.y), POSITION_DECIMALS),
        "strength": round(float(obj.strength), STRENGTH_DECIMALS),
        "band": obj.source_band,
        "snap": obj.snap,
    }

    # Sliders and spinners occupy time rather than an instant, and the
    # preview draws them for as long as they last.
    if obj.end_time is not None:
        record["endTime"] = round(float(obj.end_time), TIME_DECIMALS)

    return record


def build_analysis(track, onsets, grid, tiers: dict,
                    onset_source: str = "") -> dict:
    """
    Assemble the whole analysis document.

    Args:
        track: the AudioTrack that was analysed
        onsets: list[Onset] from Step 2
        grid: the BeatGrid from Step 3
        tiers: {tier name: list[HitObject]}, in the order they were built
        onset_source: which tier's detection pass `onsets` came from --
            sensitivity is per-tier, so "these onsets" is only meaningful
            alongside whose they are.
    """
    return {
        "version": 1,
        "track": {
            "name": track.name,
            "duration": round(float(track.duration), TIME_DECIMALS),
            "sampleRate": int(track.sr),
        },
        "onsetSource": onset_source,
        "onsets": [onset_record(o) for o in onsets],
        "beatGrid": beat_grid_record(grid),
        "hitObjects": {name: [hit_object_record(o) for o in objects]
                       for name, objects in tiers.items()},
    }


def write_analysis(path: str, analysis: dict) -> str:
    """Write the analysis document as compact JSON (no indentation: it is read by a program, not a person)."""
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)

    with open(path, "w", encoding="utf-8") as f:
        json.dump(analysis, f, separators=(",", ":"))

    return path


def export_analysis(folder: str, track, mel_db: np.ndarray, onsets, grid,
                     tiers: dict, onset_source: str = "") -> dict:
    """
    Write both artifacts into an existing beatmap folder.

    Returns {"analysis": path, "spectrogram": path}.
    """
    spectrogram_path = write_spectrogram_png(
        os.path.join(folder, SPECTROGRAM_FILENAME), mel_db)

    analysis_path = write_analysis(
        os.path.join(folder, ANALYSIS_FILENAME),
        build_analysis(track, onsets, grid, tiers, onset_source=onset_source))

    return {"analysis": analysis_path, "spectrogram": spectrogram_path}
