"""
tests/test_analysis_export.py

Test strategy for the analysis export (FRONTEND_PLAN.md Phase 7): the
frontend animates a reveal entirely from analysis.json + spectrogram.png,
so what matters here is that the two files are actually *readable by a
program that isn't Python* and that the numbers in them are the ones the
pipeline produced.

  1. UNIT TESTS on the record builders and the spectrogram downsampling,
     which are pure functions over data we can construct by hand.

  2. INTEGRATION TEST: run a real build_map() on a click track, export,
     read the JSON back, and check every stage's data survived with the
     field names and shapes the frontend's AnalysisData expects.

  3. SIZE CHECK on a long track -- the plan flagged "does this file need
     paging for long songs?" as an open question, so it gets measured
     rather than assumed.

Run with:
    pytest tests/test_analysis_export.py -v
"""

import json
import os
import sys

import numpy as np
import pytest

sys.path.append(os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "src"))

from audio.loader import generate_click_track
from audio.visualize import compute_mel_spectrogram
from mapping.hit_object import HitObject
from mapping.difficulty import PRESETS, build_map_detailed
from onset.beat_tracker import BeatGrid
from onset.detector import Onset
from export.analysis_export import (
    MAX_SPECTROGRAM_WIDTH, downsample_columns, write_spectrogram_png,
    onset_record, beat_grid_record, hit_object_record, build_analysis,
    export_analysis,
)


# ------------------------------------------------------------------
# 1. Unit tests -- pure functions
# ------------------------------------------------------------------

def test_downsample_leaves_a_short_spectrogram_alone():
    mel = np.random.rand(128, 500)

    assert downsample_columns(mel, max_width=2048) is mel


def test_downsample_fits_within_the_width_limit():
    mel = np.random.rand(128, 15000)

    reduced = downsample_columns(mel, max_width=2048)

    assert reduced.shape[0] == 128
    assert reduced.shape[1] <= 2048


def test_downsample_keeps_bright_frames_rather_than_averaging_them_away():
    # One bright column in an otherwise quiet spectrogram: this is exactly
    # a percussive onset, and it is the whole reason the image is worth
    # showing. Averaging would dilute it by the block size.
    mel = np.full((8, 4000), -80.0)
    mel[:, 1234] = 0.0

    reduced = downsample_columns(mel, max_width=100)

    assert reduced.max() == pytest.approx(0.0)


def test_onset_record_carries_time_strength_and_dominant_band():
    onset = Onset(time=1.23456, frame=53, strength=0.98765,
                  band_energy={"low": 0.9, "mid": 0.2, "high": 0.1})

    record = onset_record(onset)

    assert record["time"] == pytest.approx(1.235)
    assert record["strength"] == pytest.approx(0.988)
    assert record["band"] == "low"


def test_beat_grid_record_carries_every_beat():
    times = np.arange(0.25, 10.0, 0.5)
    grid = BeatGrid(bpm=120.0, offset=0.25, beat_times=times, confidence=0.87)

    record = beat_grid_record(grid)

    assert record["bpm"] == pytest.approx(120.0)
    assert record["offset"] == pytest.approx(0.25)
    assert record["confidence"] == pytest.approx(0.87)
    assert len(record["beatTimes"]) == len(times)


def test_hit_object_record_keeps_the_provenance_the_osu_file_drops():
    obj = HitObject(kind="slider", time=4.5, x=256.0, y=192.0,
                    strength=0.75, source_band="low", snap="1/2",
                    end_time=5.0)

    record = hit_object_record(obj)

    assert record["kind"] == "slider"
    assert record["strength"] == pytest.approx(0.75)
    assert record["band"] == "low"
    assert record["snap"] == "1/2"
    assert record["endTime"] == pytest.approx(5.0)


def test_a_circle_has_no_end_time_field():
    record = hit_object_record(HitObject(kind="circle", time=1.0))

    assert "endTime" not in record


# ------------------------------------------------------------------
# 2. Integration -- a real pipeline run, read back
# ------------------------------------------------------------------

@pytest.fixture(scope="module")
def click_track():
    return generate_click_track(bpm=128.0, duration_sec=20.0)


@pytest.fixture(scope="module")
def exported(click_track, tmp_path_factory):
    folder = str(tmp_path_factory.mktemp("analysis"))

    onsets, objs, grid = build_map_detailed(click_track, PRESETS["Normal"])
    paths = export_analysis(folder, click_track,
                             compute_mel_spectrogram(click_track),
                             onsets, grid, {"Normal": objs},
                             onset_source="Normal")

    with open(paths["analysis"], encoding="utf-8") as f:
        return paths, json.load(f), objs, grid, onsets


def test_export_writes_both_files(exported):
    paths, _, _, _, _ = exported

    assert os.path.isfile(paths["analysis"])
    assert os.path.isfile(paths["spectrogram"])
    assert os.path.getsize(paths["spectrogram"]) > 0


def test_exported_json_has_every_stage_the_reveal_animates(exported):
    _, data, _, _, _ = exported

    # One key per stage of the frontend's sequence: spectrogram (track),
    # onsets, beat grid, hit objects.
    assert data["version"] == 1
    assert data["track"]["name"]
    assert data["onsets"]
    assert data["beatGrid"]["beatTimes"]
    assert data["hitObjects"]["Normal"]


def test_exported_onsets_match_what_was_detected(exported):
    _, data, _, _, onsets = exported

    assert len(data["onsets"]) == len(onsets)
    assert data["onsetSource"] == "Normal"
    assert data["onsets"][0]["time"] == pytest.approx(onsets[0].time, abs=1e-3)


def test_exported_objects_match_the_placed_map(exported):
    _, data, objs, _, _ = exported

    exported_objects = data["hitObjects"]["Normal"]

    assert len(exported_objects) == len(objs)
    assert [o["kind"] for o in exported_objects] == [o.kind for o in objs]
    assert exported_objects[0]["x"] == pytest.approx(objs[0].x, abs=0.1)
    assert exported_objects[0]["time"] == pytest.approx(objs[0].time, abs=1e-3)


def test_exported_bpm_matches_the_tracked_grid(exported):
    _, data, _, grid, _ = exported

    assert data["beatGrid"]["bpm"] == pytest.approx(grid.bpm, abs=0.01)


def test_the_spectrogram_png_is_a_real_png(exported):
    paths, _, _, _, _ = exported

    with open(paths["spectrogram"], "rb") as f:
        assert f.read(8) == b"\x89PNG\r\n\x1a\n"


# ------------------------------------------------------------------
# 3. Size -- the plan's open question, measured
# ------------------------------------------------------------------

def test_a_long_track_stays_a_reasonable_file(tmp_path):
    """
    FRONTEND_PLAN.md Phase 7 left open whether analysis.json needs paging
    or trimming for long songs. Six minutes at five tiers is longer than
    anything this project has been tested against; if that comes in small
    enough to load in one read, the question is settled and the frontend
    can keep loading the whole file at once.
    """
    track = generate_click_track(bpm=175.0, duration_sec=360.0)

    onsets, objs, grid = build_map_detailed(track, PRESETS["Expert"])
    tiers = {name: objs for name in ("Easy", "Normal", "Hard", "Insane", "Expert")}

    paths = export_analysis(str(tmp_path), track, compute_mel_spectrogram(track),
                             onsets, grid, tiers, onset_source="Expert")

    analysis_mb = os.path.getsize(paths["analysis"]) / (1024 * 1024)
    spectrogram_mb = os.path.getsize(paths["spectrogram"]) / (1024 * 1024)

    assert analysis_mb < 8, f"analysis.json grew to {analysis_mb:.1f} MB"
    assert spectrogram_mb < 4, f"spectrogram.png grew to {spectrogram_mb:.1f} MB"


def test_spectrogram_width_is_capped_for_a_long_track(tmp_path):
    track = generate_click_track(bpm=128.0, duration_sec=300.0)
    mel = compute_mel_spectrogram(track)

    assert mel.shape[1] > MAX_SPECTROGRAM_WIDTH

    path = write_spectrogram_png(str(tmp_path / "spectrogram.png"), mel)

    # Read the width straight out of the PNG's IHDR rather than pulling in
    # an image library just for one assertion.
    with open(path, "rb") as f:
        header = f.read(24)

    width = int.from_bytes(header[16:20], "big")

    assert width <= MAX_SPECTROGRAM_WIDTH


def test_build_analysis_orders_tiers_as_given(click_track):
    grid = BeatGrid(bpm=120.0, offset=0.0, beat_times=np.arange(0, 10, 0.5),
                     confidence=0.9)
    tiers = {"Easy": [HitObject(kind="circle", time=1.0)],
             "Insane": [HitObject(kind="circle", time=2.0)]}

    data = build_analysis(click_track, [], grid, tiers)

    assert list(data["hitObjects"].keys()) == ["Easy", "Insane"]
