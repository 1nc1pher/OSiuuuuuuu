"""
src/mapping/hit_object.py

The shared data model for Step 4+ : one HitObject type that can represent
an osu! circle, slider, or spinner, plus the playfield constants everything
in src/mapping/ places objects into.

Kept in its own module (rather than in object_classifier.py) so that
slider_generator.py and spinner_generator.py can import the type and the
playfield bounds without a circular import back through the classifier.

osu! playfield is 512 x 384 osu!pixels, origin top-left. Hit objects are
positioned by their centre. We keep a margin so objects (and slider paths)
don't clip the edge of the play area.
"""

from dataclasses import dataclass, field
from typing import Optional, List, Tuple


PLAYFIELD_W = 512
PLAYFIELD_H = 384
PLAYFIELD_MARGIN = 40          # keep objects this far from every edge
PLAYFIELD_CENTER = (PLAYFIELD_W / 2, PLAYFIELD_H / 2)

DEFAULT_CIRCLE_SIZE = 4.0


def circle_radius(circle_size: float = DEFAULT_CIRCLE_SIZE) -> float:
    """
    Radius of a hit circle in osu!pixels for a given CS, per the osu! wiki:

        radius = 54.4 - 4.48 * CS

    This matters far more than it looks. Mappers do not place objects at
    "120 pixels apart" -- they place them at multiples of the circle
    DIAMETER, which is exactly what the editor's distance-snap tool
    enforces. Expressing every placement distance relative to this is what
    keeps a map readable at any CS: the same pattern automatically spreads
    out for small circles and tightens for large ones.
    """
    return 54.4 - 4.48 * float(circle_size)


@dataclass
class HitObject:
    """
    A single placed hit object.

    Common fields:
      kind      -- "circle" | "slider" | "spinner"
      time      -- start time in seconds
      x, y      -- centre position in osu!pixels (spinners are always centred)
      strength  -- source onset strength (0..1), carried through for Step 5
      source_band -- dominant frequency band of the source onset
      snap      -- rhythmic subdivision it snapped to ("1/1", "1/2", "1/4")

    Slider-only (filled by slider_generator):
      end_time     -- time the slider finishes (all slides)
      path         -- absolute anchor points, [(x0,y0), (x1,y1)] for a linear slider
      slides       -- number of slides (1 = no repeat, 2 = one repeat, ...)
      pixel_length -- length of ONE slide of the path, in osu!pixels
      slider_beats -- intended total duration in beats

    Spinner-only (filled by spinner_generator):
      end_time       -- time the spinner finishes
      duration       -- end_time - time, seconds
      required_spins -- nominal spin count target (refined against OD in Step 5)
    """
    kind: str
    time: float
    x: float = PLAYFIELD_CENTER[0]
    y: float = PLAYFIELD_CENTER[1]
    strength: float = 0.0
    source_band: str = "mid"
    snap: str = ""

    end_time: Optional[float] = None

    # slider
    path: Optional[List[Tuple[float, float]]] = None
    slides: int = 1
    pixel_length: Optional[float] = None
    slider_beats: Optional[float] = None

    # spinner
    duration: Optional[float] = None
    required_spins: Optional[int] = None

    def end(self) -> float:
        """End time (== start time for a circle)."""
        return self.end_time if self.end_time is not None else self.time
