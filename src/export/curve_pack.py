"""
src/export/curve_pack.py

How a per-frame curve is written into dsp.json, and read back.

The DSP visualization wants the signals the pipeline computes at every
STFT frame -- spectral flux, its local median, the onset envelope, the
RMS envelope, and flux per frequency band. A four-minute track at
sr=22050 / hop=512 is about 11,000 frames, so that is seven arrays of
11,000 numbers each.

Written as JSON floats at three decimals that is roughly 400 KB of
digits, which would triple the beatmap folder's JSON for precision no
line 400 pixels tall can possibly show.

Downsampling instead (the way spectrogram.png caps itself at 2048
columns) was the obvious alternative and is the wrong one here. At four
minutes, 2048 columns is 117 ms per column, and these curves get drawn
*behind* millisecond-precise onset markers -- a spark would visibly sit
beside its own flux peak. Worse, flux has to be max-pooled to keep its
peaks while its threshold would have to be mean-pooled, and the two
together draw a curve crossing its threshold nearly everywhere: a
picture that lies about the algorithm it is supposed to be showing.

So: full resolution, encoded as binary, base64 for JSON transport.

WHY THERE ARE THREE ENCODINGS
  Precision that is ample for *drawing* a curve is not ample for
  *recomputing an algorithm over* it. The client re-runs the onset
  peak-picker over the flux and median curves so the detector's
  sensitivity can be explored live, and that algorithm asks
  `flux[i] > flux[i-1]` and `flux[i] > threshold[i]`. Both are knife
  edges: an encoding that flattens two near-equal neighbours into one
  value does not blur a peak, it deletes it.

  Measured against three real tracks at all five difficulty presets:

    flux uint8   -- loses ~6 onsets in 499; adjacent samples collide
    flux uint16  -- exact on the three easier presets, but still shifts
                    a peak by one frame where two neighbours differ by
                    2e-6, and flips one onset that sits 2e-6 above its
                    own threshold
    flux float32 -- reproduces the backend exactly, on every track and
                    every preset tried (15 combinations)

  The median curve is gentler: it is a moving median, so it has no
  single-frame spikes to lose, and uint16 reproduces the backend
  exactly alongside a float32 flux. The five curves that are only ever
  drawn stay at uint8, which is already finer than a pixel.

  So: float32 for flux, uint16 for median, uint8 for the rest. The cost
  of exactness is about 60 KB on a four-minute track, against a
  spectrogram.png of 500 KB sitting in the same folder.

  `encoding` travels in the record as a plain string rather than being
  implied by the curve's name, so a reader never has to know which
  curve is which -- it switches on what the record says it is.

BYTE ORDER
  Multi-byte payloads are little-endian, always, written explicitly
  rather than left to the platform. The reader on the other side of
  this file is C#, and a format whose meaning depends on the machine
  that wrote it is not a format.

DECODING, IN ONE RULE
  value[i] = raw[i] / divisor * scale

  where divisor is the encoding's level count (255, 65535) or 1 for
  float32, and scale is carried in the record. One formula for all
  three encodings.

The layout is pinned at both ends by
tests/fixtures/curve_pack_reference.json: the Python test here and the
C# test in OsuClient.Tests both assert against that one file, so the two
implementations cannot drift apart without a test going red.
"""

import base64

import numpy as np


class _Encoding:
    """One wire encoding: its numpy dtype and the divisor its decode uses."""

    def __init__(self, dtype: str, divisor: float):
        self.dtype = np.dtype(dtype)
        self.divisor = float(divisor)


# Level counts are 255 and 65535 rather than 256 and 65536 so that a
# value equal to the scale round-trips to exactly the scale rather than
# to 255/256 of it.
ENCODINGS = {
    "uint8": _Encoding("uint8", 255),
    "uint16": _Encoding("<u2", 65535),
    # Stored as-is; the divisor exists only so the decode rule is the
    # same sentence for all three.
    "float32": _Encoding("<f4", 1),
}

# What a curve gets when the caller doesn't say. Drawing precision.
DEFAULT_ENCODING = "uint8"

# Encodings that quantise, and so have a level count worth asserting
# accuracy against. float32 has no quantisation step to speak of.
QUANTISED_ENCODINGS = ("uint8", "uint16")


def levels(encoding: str) -> float:
    """The divisor an encoding's decode divides by. 1 for float32."""
    return _lookup(encoding).divisor


def _lookup(encoding: str) -> _Encoding:
    if encoding not in ENCODINGS:
        raise ValueError(
            f"unsupported encoding {encoding!r}; expected one of {sorted(ENCODINGS)}")

    return ENCODINGS[encoding]


def pack(values, scale: float = None, encoding: str = DEFAULT_ENCODING) -> dict:
    """
    Encode a curve and base64 it.

    Returns {"scale": float, "encoding": str, "count": int, "data": str},
    where the original value at frame i is
    data[i] / levels(encoding) * scale.

    `scale` defaults to the curve's own maximum, and is forced to 1 for
    float32, which stores values directly and has nothing to normalise
    against.

    Negatives are clamped to zero -- every curve this carries is a
    magnitude, and a negative would otherwise wrap around into a large
    positive value under an unsigned encoding. NaN and infinity are
    flattened to zero for the same reason: a silent or degenerate track
    should produce a flat curve, not a corrupt one.
    """
    spec = _lookup(encoding)

    array = np.asarray(values, dtype=float).ravel()
    array = np.nan_to_num(array, nan=0.0, posinf=0.0, neginf=0.0)
    array = np.maximum(array, 0.0)

    if spec.divisor == 1:
        # Nothing to quantise against, so nothing to scale by. Writing
        # scale 1 keeps the decode rule identical for every encoding.
        scale = 1.0
        encoded = array.astype(spec.dtype)
    else:
        if scale is None:
            scale = float(array.max()) if array.size else 0.0

        # A silent track gives an all-zero curve, whose maximum is 0.
        # Dividing by it would be a warning and a NaN payload, so the
        # whole curve is written as zeros against a unit scale -- which
        # decodes back to the all-zero curve it started as.
        if not np.isfinite(scale) or scale <= 0:
            scale = 1.0
            encoded = np.zeros(array.shape, dtype=spec.dtype)
        else:
            encoded = np.rint(array / scale * spec.divisor)
            encoded = np.clip(encoded, 0, spec.divisor).astype(spec.dtype)

    return {
        "scale": float(scale),
        "encoding": encoding,
        # Derivable from the payload's length, and written anyway: it is
        # the only corruption this format can detect, and a truncated
        # curve that silently draws short is exactly the failure the
        # client's forgiving loader wants to catch.
        "count": int(encoded.size),
        "data": base64.b64encode(encoded.tobytes()).decode("ascii"),
    }


def unpack(record: dict) -> np.ndarray:
    """
    Inverse of pack(). Returns a float array.

    Raises ValueError if the record names an encoding this format
    doesn't define, or if its own `count` disagrees with how many
    samples are actually there.
    """
    spec = _lookup(record.get("encoding", DEFAULT_ENCODING))

    raw = base64.b64decode(record["data"])

    if len(raw) % spec.dtype.itemsize:
        raise ValueError(
            f"{len(raw)} bytes is not a whole number of "
            f"{record.get('encoding', DEFAULT_ENCODING)} samples")

    encoded = np.frombuffer(raw, dtype=spec.dtype)

    count = record.get("count")
    if count is not None and int(count) != encoded.size:
        raise ValueError(
            f"curve claims {count} samples but carries {encoded.size}")

    return encoded.astype(float) / spec.divisor * float(record["scale"])
