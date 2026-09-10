"""
src/mapping/spinner_generator.py

Fills in the geometry/target fields of a spinner-typed HitObject.

A spinner in osu! is just a start time, an end time, and (implicitly, via
Overall Difficulty) a number of spins the player must complete to clear
it. There is no position -- spinners are always centred.

The `required_spins` we compute here is a *nominal* target based on a
fixed comfortable spin rate. The real, OD-dependent clear/bonus
thresholds are Step 5's job (difficulty.py), which will overwrite this
using the proper osu! spins-per-second tables. We compute something
sensible now so a spinner is playable even before Step 5 exists.
"""

import os
import sys

sys.path.append(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from mapping.hit_object import HitObject, PLAYFIELD_CENTER


# A relaxed, very-clearable spin rate. osu!'s OD5 requirement is ~2 rev/s;
# real maps sit above that. Step 5 replaces this with an OD table.
NOMINAL_SPINS_PER_SEC = 1.8


def generate_spinner(obj: HitObject,
                      spins_per_sec: float = NOMINAL_SPINS_PER_SEC) -> HitObject:
    """
    Fill in `obj.duration`, `obj.required_spins` and centre the spinner.

    Args:
        obj: a HitObject with kind == "spinner" and end_time set
        spins_per_sec: nominal spin rate used for the target count
    """
    if obj.end_time is None or obj.end_time <= obj.time:
        raise ValueError("spinner needs end_time > time")

    obj.duration = float(obj.end_time - obj.time)
    obj.required_spins = max(1, int(round(obj.duration * spins_per_sec)))
    obj.x, obj.y = PLAYFIELD_CENTER
    return obj
