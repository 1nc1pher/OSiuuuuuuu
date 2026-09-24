"""
tests/fixtures/make_curve_pack_reference.py

Regenerates curve_pack_reference.json, the one file the Python and C#
halves of the dsp.json curve format both assert against.

Run it only when the format is deliberately being changed -- the whole
point of the fixture is that it does *not* move when pack() is edited by
accident. Regenerating it to make a red test go green defeats it.

    python tests/fixtures/make_curve_pack_reference.py
"""

import json
import os
import sys

sys.path.append(os.path.join(
    os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))), "src"))

from export.curve_pack import ENCODINGS, QUANTISED_ENCODINGS, levels, pack


# A short curve chosen to exercise every branch a decoder can get wrong,
# on a scale that is deliberately not 1 so a reader that ignores `scale`
# fails loudly rather than looking almost right:
#
#   - exact zero and the exact maximum (the two endpoints)
#   - a one-sample spike between two near-zero neighbours (peak survival)
#   - values landing exactly on quantisation boundaries, and values
#     landing exactly halfway between two of them (rounding direction)
#   - a flat run (constant regions must stay constant)
#   - a value just over zero, which must not round up into a spike
SCALE = 2.5

_STEP_8 = SCALE / levels("uint8")

VALUES = [
    0.0,
    0.01,
    0.0,
    2.5,                # the maximum: must decode to exactly the scale
    0.0,
    _STEP_8 * 1,        # exactly one 8-bit step
    _STEP_8 * 2,        # exactly two 8-bit steps
    _STEP_8 * 1.5,      # exactly halfway between two 8-bit steps
    _STEP_8 * 127,      # mid-scale, on a boundary
    _STEP_8 * 127.5,    # mid-scale, halfway
    1.1, 1.1, 1.1, 1.1,  # a flat run
    0.4, 0.8, 1.2, 1.6, 2.0,
    2.0, 1.6, 1.2, 0.8, 0.4,
    0.0,
    2.4999,             # just under the maximum
    0.0001,             # just over zero: must not round up into a spike
    0.9, 0.3, 0.6, 0.15,
]


def main():
    out_path = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                            "curve_pack_reference.json")

    fixture = {
        "comment": "Shared reference for the dsp.json curve format. Asserted "
                   "against by tests/test_curve_pack.py and by "
                   "OsuClient.Tests/Backend/CurveDataTests.cs. Regenerate "
                   "only when the format is deliberately changed.",
        "levels": {name: levels(name) for name in sorted(ENCODINGS)},
        "values": VALUES,
        # Every encoding, because a reader that handles uint8 correctly
        # can still get the others wrong -- byte order and the differing
        # divisors are exactly what it could get wrong silently.
        "cases": [
            {
                "encoding": name,
                "packed": pack(VALUES, encoding=name),
                # One quantisation step at this curve's scale: the worst
                # error a correct decoder can produce. float32 carries
                # the values themselves, so its only error is the float
                # cast, which is far below this.
                "tolerance": (SCALE / levels(name)
                              if name in QUANTISED_ENCODINGS else 1e-6),
            }
            for name in sorted(ENCODINGS)
        ],
    }

    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(fixture, f, indent=2)
        f.write("\n")

    print(f"wrote {out_path}")


if __name__ == "__main__":
    main()
