"""
tests/test_curve_pack.py

Test strategy for the dsp.json curve format (GENERATION_REDESIGN_PLAN.md
step 0).

This is a wire format with two independent implementations -- pack()
here and CurveData.Decode() in the C# client -- so the tests come in
three kinds:

  1. PROPERTY TESTS on pack/unpack: round-trip accuracy at every
     encoding, and the degenerate curves a real track actually produces
     (silence, a constant, a single frame).

  2. PEAK PRESERVATION, which is the property the whole feature rests
     on. The client re-runs the onset peak-picker over the packed flux
     curve, and that algorithm asks `flux[i] > flux[i-1]` -- so an
     encoding that flattens two near-equal neighbours into one value
     does not merely blur the picture, it deletes an onset. This is why
     flux is written as float32 rather than quantised; these tests are
     what pin that decision down.

  3. A SHARED FIXTURE, tests/fixtures/curve_pack_reference.json, which
     both this file and OsuClient.Tests assert against. A format pinned
     by two independent tests against one file cannot drift silently; a
     format pinned only by "both sides look correct" can, and will.

Run with:
    pytest tests/test_curve_pack.py -v
"""

import base64
import json
import os
import sys

import numpy as np
import pytest

sys.path.append(os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "src"))

from audio.loader import generate_click_track
from audio.visualize import compute_mel_spectrogram
from onset.detector import normalize, spectral_flux
from export.curve_pack import (
    DEFAULT_ENCODING, ENCODINGS, QUANTISED_ENCODINGS, levels, pack, unpack,
)


FIXTURE_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                            "fixtures", "curve_pack_reference.json")

ALL_ENCODINGS = sorted(ENCODINGS)


def real_flux():
    """A flux curve off real audio machinery, not a made-up array."""
    track = generate_click_track(bpm=128.0, duration_sec=8.0)
    return normalize(spectral_flux(compute_mel_spectrogram(track)))


def tolerance(encoding, scale=1.0):
    """The worst error a correct decoder can produce at this encoding."""
    if encoding in QUANTISED_ENCODINGS:
        return scale / levels(encoding)

    # float32 carries the values themselves; the only loss is the cast.
    return scale * 1e-6


# ------------------------------------------------------------------
# 1. Round-trip accuracy
# ------------------------------------------------------------------

@pytest.mark.parametrize("encoding", ALL_ENCODINGS)
def test_round_trip_stays_within_one_quantisation_step(encoding):
    values = np.linspace(0, 1, 500)

    restored = unpack(pack(values, encoding=encoding))

    assert np.max(np.abs(restored - values)) <= tolerance(encoding)


@pytest.mark.parametrize("encoding", ALL_ENCODINGS)
def test_round_trip_scales_with_the_curve(encoding):
    # Not every curve is normalised to 1 by the time it is packed, so a
    # quantised encoding's error budget has to follow the scale.
    values = np.linspace(0, 40, 500)

    restored = unpack(pack(values, encoding=encoding))

    assert np.max(np.abs(restored - values)) <= tolerance(encoding, 40.0)


@pytest.mark.parametrize("encoding", ALL_ENCODINGS)
def test_the_maximum_round_trips_exactly(encoding):
    # 255 levels rather than 256 exists for this: a value sitting at the
    # scale should come back as the scale, not as 255/256 of it.
    values = np.array([0.0, 0.3, 1.0])

    assert unpack(pack(values, encoding=encoding))[2] == pytest.approx(1.0)


@pytest.mark.parametrize("encoding", ALL_ENCODINGS)
def test_length_is_preserved(encoding):
    assert unpack(pack(np.random.rand(1234), encoding=encoding)).shape == (1234,)


def test_each_encoding_is_meaningfully_finer_than_the_last():
    # Guards against a wider encoding that silently falls back to a
    # narrower one.
    values = np.linspace(0, 1, 2000)

    def error(encoding):
        return np.max(np.abs(unpack(pack(values, encoding=encoding)) - values))

    assert error("uint16") < error("uint8") / 100
    assert error("float32") < error("uint16") / 100


# ------------------------------------------------------------------
# 2. Format details a second implementation can get wrong
# ------------------------------------------------------------------

@pytest.mark.parametrize("encoding", ALL_ENCODINGS)
def test_the_record_names_its_own_encoding(encoding):
    # The reader must not have to know which curves are which.
    assert pack([1.0], encoding=encoding)["encoding"] == encoding


def test_the_default_encoding_is_uint8():
    assert pack([1.0])["encoding"] == DEFAULT_ENCODING == "uint8"


def test_multi_byte_payloads_are_little_endian():
    # Pinned explicitly rather than left to the platform: the reader on
    # the other side of this file is C#, and a format whose meaning
    # depends on the machine that wrote it is not a format.
    raw = base64.b64decode(pack([1.0], scale=1.0, encoding="uint16")["data"])
    assert list(raw) == [0xFF, 0xFF]                      # 65535, low byte first

    raw = base64.b64decode(
        pack([1.0 / levels("uint16")], scale=1.0, encoding="uint16")["data"])
    assert list(raw) == [0x01, 0x00]                      # 1, low byte first

    raw = base64.b64decode(pack([1.0], encoding="float32")["data"])
    assert list(raw) == [0x00, 0x00, 0x80, 0x3F]          # 1.0f, little-endian


@pytest.mark.parametrize("encoding,width", [("uint8", 1), ("uint16", 2), ("float32", 4)])
def test_payload_width_matches_the_encoding(encoding, width):
    payload = base64.b64decode(pack(np.zeros(64), encoding=encoding)["data"])

    assert len(payload) == 64 * width


def test_float32_carries_values_directly_on_a_unit_scale():
    # Nothing to quantise against, so nothing to normalise by -- and a
    # scale of 1 keeps the decode rule identical for every encoding.
    record = pack([0.0, 7.5, 2.5], encoding="float32")

    assert record["scale"] == 1.0
    assert unpack(record)[1] == pytest.approx(7.5)


@pytest.mark.parametrize("encoding", ["", "uint4", "int16", "float64", "UINT8", None])
def test_an_unknown_encoding_is_refused_at_both_ends(encoding):
    with pytest.raises(ValueError):
        pack([1.0], encoding=encoding)

    with pytest.raises(ValueError):
        unpack({"scale": 1.0, "encoding": encoding, "data": ""})


# ------------------------------------------------------------------
# 3. Degenerate curves -- all of these come off real audio
# ------------------------------------------------------------------

@pytest.mark.parametrize("encoding", ALL_ENCODINGS)
def test_a_silent_curve_packs_and_unpacks_to_silence(encoding):
    # A silent track's normalised flux is all zeros, whose maximum is 0.
    # Scaling by it would be a divide-by-zero and a NaN payload.
    record = pack(np.zeros(64), encoding=encoding)

    assert record["scale"] == 1.0
    assert np.all(unpack(record) == 0)


@pytest.mark.parametrize("encoding", ALL_ENCODINGS)
def test_a_constant_curve_survives(encoding):
    restored = unpack(pack(np.full(64, 0.7), encoding=encoding))

    assert np.allclose(restored, 0.7, atol=tolerance(encoding))


@pytest.mark.parametrize("encoding", ALL_ENCODINGS)
def test_a_single_sample_curve_survives(encoding):
    assert unpack(pack([0.5], encoding=encoding)) == pytest.approx(
        0.5, abs=tolerance(encoding))


@pytest.mark.parametrize("encoding", ALL_ENCODINGS)
def test_an_empty_curve_is_empty_rather_than_an_error(encoding):
    record = pack([], encoding=encoding)

    assert record["count"] == 0
    assert unpack(record).size == 0


@pytest.mark.parametrize("encoding", ALL_ENCODINGS)
def test_negatives_are_clamped_rather_than_wrapped(encoding):
    # Every curve here is a magnitude. A negative reaching an unsigned
    # cast would wrap into a large positive value -- a spike where the
    # quietest moment in the song is.
    restored = unpack(pack([-5.0, 0.0, 1.0], encoding=encoding))

    assert restored[0] == 0.0


@pytest.mark.parametrize("encoding", ALL_ENCODINGS)
def test_nan_and_infinity_flatten_to_zero(encoding):
    restored = unpack(pack([np.nan, np.inf, -np.inf, 1.0], encoding=encoding))

    assert np.all(np.isfinite(restored))
    assert restored[0] == 0.0
    assert restored[1] == 0.0


# ------------------------------------------------------------------
# 4. Peak preservation -- why flux is written as float32
# ------------------------------------------------------------------

@pytest.mark.parametrize("encoding", ALL_ENCODINGS)
def test_the_peak_of_a_real_flux_curve_does_not_move(encoding):
    flux = real_flux()

    restored = unpack(pack(flux, encoding=encoding))

    assert int(np.argmax(restored)) == int(np.argmax(flux))


def test_float32_preserves_every_strict_local_maximum():
    # `flux[i] > flux[i-1] and flux[i] >= flux[i+1]` is pick_peaks()'s
    # exact test, so this is the predicate that has to survive, not a
    # looser one. Measured on real tracks, uint8 flux loses about 6
    # onsets in 499 to precisely this and uint16 still shifts one peak
    # by a frame; float32 loses none.
    flux = real_flux()
    restored = unpack(pack(flux, encoding="float32"))

    for i in range(1, len(flux) - 1):
        if flux[i] > flux[i - 1] and flux[i] >= flux[i + 1]:
            assert restored[i] > restored[i - 1]
            assert restored[i] >= restored[i + 1]


# ------------------------------------------------------------------
# 5. Corruption the format can actually detect
# ------------------------------------------------------------------

def test_a_truncated_payload_is_rejected():
    record = pack(np.linspace(0, 1, 100))
    record["data"] = record["data"][:40]

    with pytest.raises(ValueError):
        unpack(record)


def test_a_payload_that_is_not_a_whole_number_of_samples_is_rejected():
    record = pack(np.linspace(0, 1, 100), encoding="float32")
    record["data"] = base64.b64encode(
        base64.b64decode(record["data"])[:-1]).decode("ascii")

    with pytest.raises(ValueError):
        unpack(record)


def test_a_record_without_a_count_still_reads():
    # Tolerated rather than required, so a hand-written or hand-trimmed
    # record stays readable; the count is a check, not part of the data.
    record = pack(np.linspace(0, 1, 100))
    del record["count"]

    assert unpack(record).shape == (100,)


def test_a_record_without_an_encoding_is_read_as_uint8():
    # The encoding that was implicit before the format named one.
    record = pack(np.linspace(0, 1, 100), encoding="uint8")
    del record["encoding"]

    assert unpack(record).shape == (100,)


# ------------------------------------------------------------------
# 6. The shared fixture -- the contract with the C# reader
# ------------------------------------------------------------------

def load_fixture():
    with open(FIXTURE_PATH, encoding="utf-8") as f:
        return json.load(f)


def test_the_reference_fixture_exists():
    assert os.path.exists(FIXTURE_PATH), (
        "regenerate with: python tests/fixtures/make_curve_pack_reference.py")


def test_the_fixture_covers_every_encoding():
    # An encoding with no fixture case is an encoding the C# reader is
    # never tested against.
    assert sorted(case["encoding"] for case in load_fixture()["cases"]) == ALL_ENCODINGS


def test_packing_the_fixture_values_reproduces_the_fixture_payload():
    # This is the drift guard. If pack() ever changes -- a different
    # rounding, a different divisor, a different scale rule, a different
    # byte order -- this goes red here and in the C# test at the same
    # time, instead of the two halves quietly disagreeing about what a
    # curve means.
    fixture = load_fixture()

    for case in fixture["cases"]:
        assert pack(fixture["values"], encoding=case["encoding"]) == case["packed"]


def test_the_fixture_round_trips_within_its_stated_tolerance():
    fixture = load_fixture()
    values = np.array(fixture["values"])

    for case in fixture["cases"]:
        restored = unpack(case["packed"])

        assert np.max(np.abs(restored - values)) <= case["tolerance"]
