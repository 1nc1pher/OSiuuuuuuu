"""
tests/test_osu_writer.py

Test strategy for Step 6 (.osu / .osz export). We can't run osu!(lazer) in
CI, so the bar is: produce a file that is structurally a valid v14 .osu
and whose numbers round-trip back to the map we put in.

  1. UNIT TESTS on line formatting: the header, section order, one line
     per object kind, the timing point, new-combo placement.

  2. INTEGRATION TESTS: render a real build_map() result, parse it back
     with a small in-file parser, and check counts / times / positions /
     slider-duration math all survive the round trip. Package an .osz and
     confirm it's a valid zip with the audio + one .osu per difficulty.

  3. SMOKE TEST: full audio-to-.osz on a real song if one is present.

Manual validation (not automatable here): drag the generated .osz onto
osu!(lazer), which will reject a malformed file outright.

Run with:
    pytest tests/test_osu_writer.py -v
"""

import os
import sys
import zipfile

import numpy as np
import pytest

sys.path.append(os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "src"))

from mapping.hit_object import HitObject, PLAYFIELD_W, PLAYFIELD_H
from mapping.difficulty import PRESETS, TIER_ORDER, build_map
from onset.beat_tracker import BeatGrid
from audio.loader import generate_click_track, load_audio, AudioTrack, DEFAULT_SR
from export.osu_writer import (
    OSU_FILE_FORMAT, OsuMetadata, timing_point_line, hit_object_line,
    render_beatmap, build_beatmap_set,
)


def _grid(bpm=120.0, offset=0.25, duration=30.0):
    period = 60.0 / bpm
    return BeatGrid(bpm=bpm, offset=offset,
                    beat_times=np.arange(offset, duration, period), confidence=1.0)


def _held_note_synth(bpm=128.0, sr=DEFAULT_SR):
    """Clicks on the beat, with a couple of clean 2-beat held tones that
    should classify as sliders."""
    period = 60.0 / bpm
    clicks = list(range(0, 8)) + list(range(10, 20)) + list(range(22, 32))
    tones = [(8.0, 9.6), (20.0, 21.6)]
    n = int(34 * period * sr)
    y = np.zeros(n, dtype=np.float32)
    clen = int(0.05 * sr)
    tc = np.arange(clen) / sr
    blip = (np.sin(2 * np.pi * 1000 * tc) * np.exp(-tc * 60)).astype(np.float32)
    for b in clicks:
        s = int(b * period * sr)
        if s + clen <= n:
            y[s:s + clen] += blip
    for b0, b1 in tones:
        s0, s1 = int(b0 * period * sr), min(n, int(b1 * period * sr))
        tt = np.arange(s1 - s0) / sr
        y[s0:s1] += (0.6 * np.sin(2 * np.pi * 220 * tt)
                     * np.minimum(1.0, tt / 0.01)).astype(np.float32)
    y = y / (float(np.max(np.abs(y))) or 1.0)
    return AudioTrack(y=y.astype(np.float32), sr=sr, duration=n / sr,
                      path="<synth>", name="held")


# --- a tiny .osu parser, just for the tests -----------------------------

def parse_osu(text: str) -> dict:
    sections, cur = {"_header": []}, "_header"
    for raw in text.splitlines():
        line = raw.strip()
        if not line:
            continue
        if line.startswith("[") and line.endswith("]"):
            cur = line[1:-1]
            sections[cur] = []
        else:
            sections[cur].append(line)
    return sections


def kv(section_lines):
    out = {}
    for line in section_lines:
        if ":" in line:
            k, v = line.split(":", 1)
            out[k.strip()] = v.strip()
    return out


def parse_hitobject(line: str) -> dict:
    f = line.split(",")
    typ = int(f[3])
    kind = ("spinner" if typ & 8 else "slider" if typ & 2 else
            "circle" if typ & 1 else "?")
    rec = {"x": int(f[0]), "y": int(f[1]), "time": int(f[2]), "type": typ,
           "kind": kind, "new_combo": bool(typ & 4), "fields": f}
    if kind == "slider":
        rec["slides"] = int(f[6])
        rec["length"] = float(f[7])
    elif kind == "spinner":
        rec["end_time"] = int(f[5])
    return rec


# ---------------------------------------------------------------------
# Tier 1: line formatting
# ---------------------------------------------------------------------

def test_header_is_exact_first_line():
    text = render_beatmap([HitObject("circle", 1.0)], _grid(), OsuMetadata("t"),
                          PRESETS["Normal"].to_osu_difficulty_section())
    assert text.splitlines()[0] == OSU_FILE_FORMAT
    assert OSU_FILE_FORMAT == "osu file format v14"


def test_sections_present_and_in_order():
    text = render_beatmap([HitObject("circle", 1.0)], _grid(), OsuMetadata("t"),
                          PRESETS["Normal"].to_osu_difficulty_section())
    order = [ln[1:-1] for ln in text.splitlines()
             if ln.startswith("[") and ln.endswith("]")]
    assert order == ["General", "Editor", "Metadata", "Difficulty",
                     "Events", "TimingPoints", "HitObjects"]


def test_timing_point_line_encodes_tempo_and_offset():
    line = timing_point_line(_grid(bpm=120.0, offset=0.25))
    f = line.split(",")
    assert f[0] == "250"                 # offset ms
    assert float(f[1]) == pytest.approx(500.0)   # ms per beat at 120 BPM
    assert f[6] == "1"                   # uninherited (red line)


def test_circle_line_rounds_and_flags_new_combo():
    o = HitObject("circle", time=1.0, x=100.4, y=200.6, strength=0.5)
    assert hit_object_line(o, new_combo=False) == "100,201,1000,1,0,0:0:0:0:"
    assert hit_object_line(o, new_combo=True) == "100,201,1000,5,0,0:0:0:0:"


def test_spinner_line_is_centered_with_end_time():
    o = HitObject("spinner", time=2.0, end_time=5.0)
    assert hit_object_line(o) == "256,192,2000,8,0,5000,0:0:0:0:"


def test_slider_line_layout():
    o = HitObject("slider", time=1.0, x=10, y=20, end_time=1.5,
                  path=[(10.0, 20.0), (150.0, 20.0)], slides=1, pixel_length=140.0)
    line = hit_object_line(o)
    assert line == ("10,20,1000,2,0,L|150:20,1,140.00,0|0,0:0|0:0,0:0:0:0:")

    o2 = HitObject("slider", time=1.0, x=10, y=20, end_time=2.0,
                   path=[(10.0, 20.0), (60.0, 20.0)], slides=3, pixel_length=50.0)
    f = hit_object_line(o2).split(",")
    assert f[6] == "3"
    assert f[8] == "0|0|0|0"            # edgeSounds has slides+1 entries


def test_slider_without_path_raises():
    with pytest.raises(ValueError):
        hit_object_line(HitObject("slider", time=1.0, end_time=2.0))


def test_render_sorts_objects_and_first_starts_a_combo():
    objs = [HitObject("circle", 3.0), HitObject("circle", 1.0),
            HitObject("circle", 2.0)]
    sec = parse_osu(render_beatmap(objs, _grid(), OsuMetadata("t"),
                    PRESETS["Normal"].to_osu_difficulty_section()))
    hos = [parse_hitobject(l) for l in sec["HitObjects"]]
    assert [h["time"] for h in hos] == [1000, 2000, 3000]
    assert hos[0]["new_combo"] is True


def test_new_combo_after_spinner():
    objs = [HitObject("circle", 1.0),
            HitObject("spinner", 2.0, end_time=4.0),
            HitObject("circle", 4.2)]
    sec = parse_osu(render_beatmap(objs, _grid(), OsuMetadata("t"),
                    PRESETS["Normal"].to_osu_difficulty_section()))
    hos = [parse_hitobject(l) for l in sec["HitObjects"]]
    assert hos[2]["kind"] == "circle" and hos[2]["new_combo"] is True


def test_difficulty_section_carries_preset_values():
    sec = parse_osu(render_beatmap([HitObject("circle", 1.0)], _grid(),
                    OsuMetadata("t"), PRESETS["Hard"].to_osu_difficulty_section()))
    d = kv(sec["Difficulty"])
    assert float(d["ApproachRate"]) == PRESETS["Hard"].approach_rate
    assert float(d["OverallDifficulty"]) == PRESETS["Hard"].overall_difficulty
    assert float(d["CircleSize"]) == PRESETS["Hard"].circle_size


# ---------------------------------------------------------------------
# Tier 2: round-trip a real build_map() result
# ---------------------------------------------------------------------

def test_rendered_map_round_trips():
    track = generate_click_track(bpm=128.0, duration_sec=20.0, sr=DEFAULT_SR)
    objs, grid = build_map(track, PRESETS["Hard"])
    sec = parse_osu(render_beatmap(objs, grid, OsuMetadata("t"),
                    PRESETS["Hard"].to_osu_difficulty_section()))

    assert len(sec["TimingPoints"]) == 1
    hos = [parse_hitobject(l) for l in sec["HitObjects"]]
    assert len(hos) == len(objs)

    # times preserved (ms), non-decreasing, kinds preserved
    for h, o in zip(hos, sorted(objs, key=lambda z: z.time)):
        assert h["time"] == round(o.time * 1000)
        assert h["kind"] == o.kind
    assert all(a["time"] <= b["time"] for a, b in zip(hos, hos[1:]))

    # positions inside the playfield
    for h in hos:
        assert 0 <= h["x"] <= PLAYFIELD_W and 0 <= h["y"] <= PLAYFIELD_H


def test_slider_length_implies_the_intended_duration():
    track = _held_note_synth()
    preset = PRESETS["Hard"]
    objs, grid = build_map(track, preset)
    sliders = [o for o in objs if o.kind == "slider"]
    assert sliders, "expected the held tones to classify as sliders"

    sm = preset.slider_multiplier
    for o in sliders:
        # osu! derives slider duration from: length * slides / (100 * SM * SV)
        beats_from_length = o.pixel_length * o.slides / (100.0 * sm)
        intended_beats = (o.end_time - o.time) / grid.beat_period
        # slider_generator accepts a small duration error when it has to
        # shorten a segment that would otherwise leave the playfield
        assert beats_from_length == pytest.approx(intended_beats, rel=0.35, abs=0.3)


def test_build_beatmap_set_makes_folder_and_valid_osz(tmp_path):
    track = generate_click_track(bpm=120.0, duration_sec=15.0, sr=DEFAULT_SR)
    # a throwaway audio file to copy
    audio = tmp_path / "song.wav"
    import soundfile as sf
    sf.write(audio, track.y, track.sr)

    diffs = []
    for name in ("Easy", "Hard"):
        objs, grid = build_map(track, PRESETS[name])
        diffs.append({"name": name, "objects": objs, "grid": grid,
                      "difficulty_section": PRESETS[name].to_osu_difficulty_section()})

    meta = OsuMetadata(title="Song", artist="Tester")
    result = build_beatmap_set(str(tmp_path / "out"), str(audio), meta, diffs)

    assert os.path.isdir(result["folder"])
    assert len(result["osu_files"]) == 2
    assert os.path.isfile(os.path.join(result["folder"], "song.wav"))

    assert zipfile.is_zipfile(result["osz"])
    with zipfile.ZipFile(result["osz"]) as zf:
        names = zf.namelist()
    assert "song.wav" in names
    assert sum(n.endswith(".osu") for n in names) == 2


def test_unsafe_metadata_is_sanitized(tmp_path):
    track = generate_click_track(bpm=120.0, duration_sec=10.0, sr=DEFAULT_SR)
    audio = tmp_path / "s.wav"
    import soundfile as sf
    sf.write(audio, track.y, track.sr)

    objs, grid = build_map(track, PRESETS["Normal"])
    meta = OsuMetadata(title="a/b:c*?", artist="x<y>z")
    result = build_beatmap_set(str(tmp_path / "out"), str(audio), meta,
                                [{"name": "Normal", "objects": objs, "grid": grid,
                                  "difficulty_section":
                                      PRESETS["Normal"].to_osu_difficulty_section()}])
    for ch in '<>:"/\\|?*':
        assert ch not in os.path.basename(result["folder"])
        assert ch not in os.path.basename(result["osz"])


# ---------------------------------------------------------------------
# Tier 3: full pipeline on a real song, if present
# ---------------------------------------------------------------------

def _find_real_audio_file():
    project_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    raw_dir = os.path.join(project_root, "data", "raw")
    import glob
    for ext in ("*.mp3", "*.wav", "*.ogg", "*.flac"):
        m = glob.glob(os.path.join(raw_dir, ext))
        if m:
            return m[0]
    return None


@pytest.mark.skipif(_find_real_audio_file() is None,
                     reason="no audio file in data/raw/")
def test_full_pipeline_generates_importable_set(tmp_path):
    from main import generate_beatmap_set

    result = generate_beatmap_set(
        _find_real_audio_file(), str(tmp_path),
        artist="Tester", title="Real Song",
        difficulties=["Easy", "Normal", "Hard", "Insane", "Expert"])

    assert zipfile.is_zipfile(result["osz"])
    assert len(result["osu_files"]) == 5

    for path in result["osu_files"]:
        with open(path, encoding="utf-8") as fh:
            sec = parse_osu(fh.read())
        assert sec["_header"] == [OSU_FILE_FORMAT]
        assert len(sec["TimingPoints"]) == 1
        assert len(sec["HitObjects"]) > 20
        for line in sec["HitObjects"]:
            h = parse_hitobject(line)
            assert h["kind"] in ("circle", "slider", "spinner")
            assert len(h["fields"]) >= (8 if h["kind"] == "slider" else
                                        6 if h["kind"] == "spinner" else 5)


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
