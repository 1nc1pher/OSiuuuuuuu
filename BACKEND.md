# osu!-lazer-style Beatmap Generator (DSP Project)

Automatic rhythm-game beatmap generation from raw audio, exported directly
to the `.osu` file format so maps can be tested in real osu!(lazer).

**The pipeline is end-to-end.** One command turns an audio file into an
importable beatmap set:

```bash
python src/main.py data/raw/your_song.mp3 --artist "Artist" --title "Song"
```

This writes `data/output/<Artist> - <Song>.osz` (five difficulties, Easy
-> Expert). Drag it onto osu!(lazer) to import and play.

## Pipeline

```
audio file
   |
   v
[1] Loading & STFT/spectrogram          <- src/audio/            [DONE]
   |
   v
[2] Onset detection (spectral flux)     <- src/onset/detector.py [DONE]
   |
   v
[3] Tempo / beat-grid estimation        <- src/onset/beat_tracker.py [DONE]
   |
   v
[4] Hit-object classification           <- src/mapping/object_classifier.py [DONE]
    (circle / slider / spinner) + placement
   |
   v
[5] Difficulty scaling (Easy .. Expert) <- src/mapping/difficulty.py [DONE]
   |
   v
[6] .osu / .osz export                  <- src/export/osu_writer.py [DONE]
    (full pipeline: src/main.py)
   |
   v
[7] analysis.json + spectrogram.png     <- src/export/analysis_export.py [DONE]
    (what the DSP saw, for the frontend's
     visualization; skip with --no-analysis)
```

## Project structure

```
osu-dsp-project/
├── README.md
├── requirements.txt
├── src/
│   ├── audio/
│   │   ├── __init__.py
│   │   ├── loader.py          # load + resample + normalize audio
│   │   └── visualize.py       # waveform / STFT / mel-spectrogram plots
│   ├── onset/
│   │   ├── __init__.py
│   │   ├── detector.py        # spectral-flux onset detection (STEP 2)
│   │   └── beat_tracker.py    # BPM + beat grid estimation      (STEP 3)
│   ├── mapping/
│   │   ├── __init__.py
│   │   ├── hit_object.py          # shared HitObject model + playfield consts
│   │   ├── object_classifier.py   # circle / slider / spinner + placement (STEP 4)
│   │   ├── slider_generator.py    # slider path + length + slides (STEP 4)
│   │   ├── spinner_generator.py   # spinner duration -> spin target (STEP 4)
│   │   └── difficulty.py          # per-tier parameter tables + build_map (STEP 5)
│   ├── export/
│   │   ├── __init__.py
│   │   ├── osu_writer.py      # renders .osu v14 + packages .osz  (STEP 6)
│   │   └── analysis_export.py # analysis.json + spectrogram.png   (STEP 7)
│   └── main.py                 # full audio -> .osz CLI            (STEP 6)
├── data/
│   ├── raw/                    # put input audio files here (.mp3/.wav/.ogg)
│   └── output/                 # generated .osu + song folders land here
├── notebooks/                  # exploratory DSP experiments (Jupyter)
├── tests/                      # unit tests per module
└── configs/
    └── difficulty_presets.yaml # per-tier onsets / snap / spacing / AR / OD / CS / SV / jumps  [DONE]
```

## Status

- [x] Step 1: Audio loading + STFT/spectrogram visualization
- [x] Step 2: Spectral-flux onset detection
- [x] Step 3: Tempo/beat-grid estimation
- [x] Step 4: Hit-object classification + placement
- [x] Step 5: Difficulty scaling
- [x] Step 6: `.osu` / `.osz` export + end-to-end CLI
- [x] Step 7: `analysis.json` + `spectrogram.png` export (frontend visualization)

**All six steps are implemented.** `pytest` runs 64 tests across every stage.

## Setup

```bash
python -m venv venv
source venv/bin/activate        # or venv\Scripts\activate on Windows
pip install -r requirements.txt
```

## Running Step 1 (audio loading + visualization)

```bash
python src/audio/visualize.py data/raw/your_song.mp3
```

This produces a 3-panel figure (waveform, linear-frequency spectrogram,
mel spectrogram) saved to `data/output/<song_name>_analysis.png`, and prints
basic audio stats (sample rate, duration, RMS).

## Running Step 2 (spectral-flux onset detection)

```bash
# sanity check with a synthetic click track first -- should detect every click
python src/onset/detector.py --synthetic --bpm 128

# on a real song
python src/onset/detector.py data/raw/your_song.mp3
```

Prints the number of onsets detected, a preview of their timestamps /
strength / dominant frequency band, and saves a 2-panel debug figure
(`data/output/<song_name>_onsets.png`) showing the mel spectrogram with
onset markers overlaid, plus the flux curve against its adaptive threshold.

**Tuning knobs** (both exposed as CLI flags):
- `--margin` (default 1.5): multiplier on the local median flux. Raise it
  to detect fewer, more confident onsets (good for low-star maps); lower
  it to catch quieter/ghost notes (good for high-star maps).
- `--delta` (default 0.05): absolute floor under the threshold, so silent
  passages don't produce false onsets from noise. Raise it if you're
  seeing spurious onsets during quiet sections.

If detection looks off on a real song, tune `--margin`/`--delta` and
re-run -- watch the flux plot to see whether real hits are being missed
(threshold too high) or noise is leaking through (threshold too low).

## Testing the onset detector

Testing an onset detector is different from testing normal code, because
for real music there's no ground-truth file sitting around that says
"the onsets are at these exact timestamps." The approach here has three
tiers:

**1. Unit tests on synthetic signals (exact, automatable)**
```bash
pytest tests/test_detector.py -v
```
`tests/test_detector.py` builds fake flux arrays with hand-planted spikes
at known frame positions and checks `normalize()` / `adaptive_threshold()`
/ `pick_peaks()` recover exactly those positions. It also runs the full
pipeline on a generated click track (known BPM -> known onset spacing) as
an end-to-end integration test. These are fast, deterministic, and belong
in CI.

**2. Sonification / listening test (the real validation for real music)**
```bash
python src/onset/sonify.py data/raw/your_song.mp3
```
This mixes an audible click into a copy of the song at every detected
onset timestamp and saves it as `data/output/<song_name>_sonified.wav`.
Play it in headphones: if the clicks land exactly on the kicks/snares/
note attacks you hear, the detector is working. Drift, double-triggers,
or missed hits are all immediately obvious by ear in a way they aren't
from a plot. This is the standard validation method used in MIR
(music information retrieval) research, since there's no automatic way
to score "correctness" against an unlabeled real recording.

Try this across a few different genres/styles to stress-test robustness:
a clean four-on-the-floor electronic track (should be nearly perfect),
a song with live/human drums (small timing variance is normal and fine),
and something bass-heavy or vocal-only (harder -- good for finding where
`--margin`/`--delta` need retuning).

**3. (Optional, for a more rigorous report) Precision/recall against
hand-tapped ground truth**
Tap along to a song's percussion (e.g. in Audacity, or just pressing a
key while playing it back) to log your own onset timestamps, then compare
against the detector's output using a small tolerance window (~50ms is
standard in onset-detection literature, e.g. MIREX evaluations). This
turns "sounds about right" into an actual precision/recall number you
can put in your writeup -- not required for the project to work, but a
nice addition if you want to demonstrate rigorous evaluation methodology.

## Running Step 3 (tempo + beat-grid estimation)

```bash
# sanity check with a synthetic click track -- should recover the BPM exactly
python src/onset/beat_tracker.py --synthetic --bpm 140

# on a real song
python src/onset/beat_tracker.py data/raw/your_song.mp3

# also write a metronome-mixed .wav for a listening test (see below)
python src/onset/beat_tracker.py data/raw/your_song.mp3 --sonify
```

Prints the estimated tempo (BPM), the beat offset (time of the first
beat), the number of beats, two confidence numbers, and the
`(offset_ms, beatLength_ms)` pair that Step 6 will drop straight into the
`.osu` file as an uninherited timing point. Saves a 2-panel debug figure
(`data/output/<song_name>_beatgrid.png`): the onset envelope with the
beat grid overlaid, and the autocorrelation curve vs candidate BPM with
the picked tempo and its +/- octave positions marked.

**How it works** (full derivation in `src/onset/beat_tracker.py` docstring):
1. Reuse Step 2's spectral flux as a continuous onset-strength envelope.
2. Autocorrelate it; the lag with the strongest correlation is the beat
   period. Search is restricted to a sane tempo range and weighted by a
   log-normal prior centred on 120 BPM.
3. Octave correction: autocorrelation genuinely peaks at 1x, 2x and 1/2x
   the true tempo, so re-score `{0.5x, 1x, 2x}` by how well a pulse train
   at that tempo lines up with real onset energy, and keep the best.
4. Beat phase: slide a pulse train across one beat period and pick the
   offset whose pulses collect the most onset energy.
5. Emit a constant-tempo grid: `arange(offset, duration, 60/bpm)`.

Rhythm-game source music is almost always machine-timed, so a single
global tempo is the right model (and is what makes a map feel snapped).
Variable-tempo tracking is deliberately out of scope.

**Tuning knobs** (CLI flags):
- `--tempo-min` / `--tempo-max` (default 50 / 210): the BPM search range.
  Narrow it if you know roughly what the tempo is and you're getting an
  octave error.
- `--prior` (default 120): centre of the log-normal tempo prior. This is
  the tie-breaker when the autocorrelation can't decide between a tempo
  and its double/half -- set it near the tempo you expect.

## Testing the beat tracker

Same three-tier strategy as the onset detector:

**1. Unit tests on synthetic onset envelopes (exact, automatable)**
```bash
pytest tests/test_beat_tracker.py -v
```
`tests/test_beat_tracker.py` builds synthetic onset envelopes -- impulse
trains at an exactly known BPM and phase, some with weaker off-beat
subdivisions -- and checks `estimate_tempo()`, `estimate_beat_phase()`
and the octave-correction logic recover the right answer, including the
two classic failure modes: locking onto double tempo, and coming in an
octave low. Also runs the full `track_beats()` pipeline end-to-end
through the real STFT/mel path on a generated click track and checks the
grid stays locked to the clicks across the whole track (no drift).

**2. Metronome listening test (the real validation for real music)**
```bash
python src/onset/beat_tracker.py data/raw/your_song.mp3 --sonify
```
Mixes a metronome click onto the song at every detected beat and saves
`data/output/<song_name>_metronome.wav`. Play it in headphones:
- click stays locked to the groove for the whole song -> tempo and phase
  are both right.
- click slowly slides out of sync -> tempo is slightly off (try a
  narrower `--tempo-min/--tempo-max` or a better `--prior`).
- click is a steady half-beat / quarter-beat off -> phase is wrong.

**3. (Optional) Compare BPM against a known value**
Many songs have a documented BPM (the original osu! map, the artist's
release notes, a BPM-analysis site). Round-tripping against a few of
those is a quick objective check on the tempo estimate for a writeup.

## Running Step 4 (circle / slider / spinner classification + placement)

```bash
# synthetic click track -- every beat should become a plain circle
python src/mapping/object_classifier.py --synthetic --bpm 128

# on a real song
python src/mapping/object_classifier.py data/raw/your_song.mp3
```

Runs Step 2 (onsets) + Step 3 (beat grid) and then classifies every
onset and places it on the playfield. Prints the type counts and the
first 15 objects (type, position, rhythmic snap, source band, and
slider/spinner specifics), and saves a debug figure
(`data/output/<song_name>_objects.png`): the onset envelope for the first
20 s with circles as dots, sliders as bars, and spinners as shaded spans.

**How a type is decided.** Rhythm comes from the onsets; the *type* comes
from what happens in the gap *after* each onset, using a short-time RMS
"sustain" measurement (fraction of the gap where loudness stays up):

| condition | type |
|---|---|
| long gap (>= 3 beats) **and** energy stays up | **spinner** |
| a run of >= 4 onsets spaced <= 1/2 beat apart | **slider** (the run folds into one) |
| gap of 1-2 beats **and** energy stays up (held note) | **slider** |
| anything else | **circle** |

Onsets are snapped to the beat grid at 1/4-beat resolution first (removes
jitter, collapses double-triggers).

### Placement (distance snap)

Real mappers don't place objects "120 pixels apart" -- they use the
editor's **distance snap**, which puts the next object a fixed number of
circle *diameters* away per beat of elapsed time. We copy that:

```
distance = circle_diameter x distance_spacing x gap_in_beats
```

with `circle_diameter` derived from CS (`radius = 54.4 - 4.48 * CS`, per
the osu! wiki). Two consequences, both of which match real maps:

- **spacing is linear in the time gap**, so a 1/1 gap reads as exactly
  twice the movement of a 1/2 gap;
- **spacing tracks CS**, so a small-circle difficulty automatically
  spreads out and the same pattern reads the same way at any circle size.

On top of distance snap, three readability rules keep the output looking
hand-made rather than like a random walk:

1. **Never bury.** Consecutive objects stay at least **0.75 diameters**
   apart (**0.65** inside a 1/4 stream, where tight spacing is correct and
   intended). Overlaps are therefore always the partial, "continuation"
   kind mappers use deliberately -- never one object sitting invisibly on
   top of another.
2. **Stay off the slider body.** A candidate must clear the body of the
   slider it just came off. Because the cursor already sits at the
   slider's end, this naturally parks the next object just past that end
   and off to one side -- exactly where mappers put it.
3. **Flow.** The heading turns only slightly across short gaps, so a
   stream comes out as a smooth arc that keeps curving the same way; long
   gaps may turn sharply, so jumps get real angles. The turn limit rises
   Easy -> Expert (`max_turn_degrees`). Near an edge the heading is
   nudged back toward the middle, so patterns use the whole playfield
   instead of crawling along a wall.

Candidates are proposed along the flow heading and, if a rule is
violated, swept around in widening steps until one fits. Ranking is
strict: on the playfield first, then off the slider body, then not
crowding an older circle. Spinners are always centred and clear the
placement history (they take over the screen anyway).

**Tuning knobs** (CLI flags):
- `--margin` / `--delta`: passed straight through to the Step 2 onset
  detector (more/fewer onsets = more/fewer objects).
- `--sv` (default 1.0): slider-velocity multiplier. Higher = faster,
  shorter sliders.
- `--seed` (default 0): placement RNG seed. Change it to reshuffle the
  object flow without changing the rhythm.

The classification thresholds (`SPINNER_MIN_BEATS`, `SLIDER_MIN_BEATS`,
`SUSTAIN_THRESHOLD`, `STREAM_MIN_LEN`, ...) and the placement constants
(`MIN_SEPARATION_DIAMETERS`, `SLIDER_BODY_CLEARANCE_DIAMETERS`, ...) live
at the top of `object_classifier.py`; Step 5 overrides the per-difficulty
ones from `configs/difficulty_presets.yaml`.

### Checking placement without launching osu!

`plot_playfield()` (via `difficulty.py --preview`) draws the objects at
their true CS radius on the 512x384 playfield, with slider bodies at the
width the game draws them, numbered in play order and joined by the
cursor path:

```bash
python src/mapping/difficulty.py data/raw/your_song.mp3 --difficulty Hard \
    --preview --preview-window 8 12
```

Keep the window **short** (a few seconds). Only objects within one
approach-rate preempt (~0.8-1.2 s) are ever on screen together, so a long
window piles up shapes the player never sees at once and makes perfectly
readable placement look like soup.

## Testing the object classifier

Same three tiers:

**1. Unit tests on hand-built inputs (exact, automatable)**
```bash
pytest tests/test_object_classifier.py -v
```
Checks onset->grid snapping and de-duplication, the sustain measurement,
and the slider/spinner geometry math (single-slide length matches the
intended beat count; over-long sliders gain repeats; every object and
every slider anchor stays on the playfield).

**2. Placement readability tests** -- these lock in the two problems found
by actually play-testing a generated map in osu!(lazer):
- consecutive objects are **never stacked** (always at least the stream
  floor apart, so overlaps read as continuations);
- a circle **never lands inside the previous slider's body** (its centre
  always clears the body's drawn radius);
- distance snap holds: doubling the time gap doubles the distance, and a
  1-beat gap lands at exactly `distance_spacing` diameters;
- spacing scales with CS -- smaller circles travel less in absolute
  pixels for the same distance-snap setting.

`tests/test_difficulty.py` re-runs the first two across **all five
tiers**, since that's where the problem showed up.

**3. Integration tests through the real Step 2 -> 3 -> 4 chain**
Synthesized audio designed to force one specific outcome:
- a plain click track -> all circles, no sliders, no spinners
- clicks then a long held tone -> a spinner appears in the held stretch
- clicks then a 1/4-note burst -> the burst folds into a slider

**4. Visual check on a real song**
Open `data/output/<song_name>_objects.png` (rhythm against the music) and
`data/output/<song>_<tier>_playfield.png` (spacing and flow), then
play-test the exported `.osz` in osu!(lazer) for the real verdict.

## Running Step 5 (difficulty scaling)

One audio file, many maps. Step 5 keeps a single onset + beat-grid
analysis and varies a table of parameters per difficulty tier (Easy,
Normal, Hard, Insane, Expert), defined in
`configs/difficulty_presets.yaml`.

```bash
# print the preset table
python src/mapping/difficulty.py --list

# build one difficulty and save its object plot
python src/mapping/difficulty.py data/raw/your_song.mp3 --difficulty Hard
python src/mapping/difficulty.py data/raw/your_song.mp3 --difficulty 4.5   # nearest tier

# also render the playfield itself, to check spacing without launching osu!
python src/mapping/difficulty.py data/raw/your_song.mp3 --difficulty Hard \
    --preview --preview-window 8 12

# build every tier and print a density comparison
python src/mapping/difficulty.py data/raw/your_song.mp3 --all
```

`--all` on a real song looks like:

```
tier      slots  objects  circles  sliders  spinners   obj/s
Easy        159      159      138       14         7    0.73
Normal      342      342      201      133         8    1.56
Hard        515      511      400       97        14    2.33
Insane      880      793      658      127         8    3.62
Expert     1011     1011      921       89         1    4.61
```

**What each tier controls** (see the YAML for the full table and per-field
docs):

| knob | Easy -> Expert |
|---|---|
| `onset_margin` / `onset_delta` | detector gets more sensitive (more notes) |
| `snap_division` | 1/1 -> 1/2 -> 1/4 rhythmic grid |
| `min_spacing_beats` | 2.0 -> 0.25 (less density thinning) |
| `AR` / `OD` / `CS` / `HP` | the osu! difficulty settings, written verbatim by Step 6 |
| `slider_multiplier` | 1.2 -> 1.6 (faster sliders) |
| `distance_spacing` | 0.85 -> 2.10 circle diameters per beat (distance snap) |
| `max_turn_degrees` | 40 -> 130 (flow angles get sharper) |
| `stream_min_len` | 4 -> 999: easy tiers fold fast runs into sliders; Expert keeps them as circles |

**`slots` vs `objects`.** `slots` = notes kept *before* classification
(onsets snapped to the grid and thinned). This is the honest density
metric and it is strictly monotonic across the tiers. The final `objects`
count is *not* always monotonic in the middle tiers, because harder tiers
fold fewer streams -- a Hard map can turn a 1/4 run into a chain of
sliders (few objects, many notes) while Expert keeps every note as a
circle. `rhythm_slot_count()` exposes the `slots` number directly.

`DifficultyPreset.to_osu_difficulty_section()` hands Step 6 the
`[Difficulty]` section (HP/CS/OD/AR/SliderMultiplier/SliderTickRate).
`build_map(track, preset)` runs Steps 2 -> 3 -> 4 with every knob taken
from the preset and returns the placed objects + the beat grid.

## Testing the difficulty scaling

```bash
pytest tests/test_difficulty.py -v
```

**1. Preset-table unit tests** -- the YAML file and the in-code
`BUILTIN_PRESETS` fallback agree field-for-field; all five tiers exist and
are star-sorted; lookup by name and by star target works; every per-tier
knob moves monotonically in the right direction; malformed presets
(missing / unknown fields) are rejected.

**2. Integration tests** through `build_map()` on synthesized audio with
sub-beat ghost notes: `rhythm_slot_count` is monotonic across the tiers
(Expert strictly denser than Easy), every tier produces a playable map
(objects on the playfield, valid slider geometry), Easy is the sparsest
and Expert the densest, and harder tiers actually resolve finer
subdivisions (Easy is beat-only; Hard picks up 1/2 and 1/4).

**3. Real-song check** -- run `--all` and confirm `slots` and `obj/s`
climb smoothly from Easy to Expert, then open
`data/output/<song>_<tier>_objects.png` for two tiers and compare the
density against the music.

## Running Step 6 (.osu / .osz export) + the full pipeline

```bash
# whole pipeline: audio -> five-difficulty beatmap set
python src/main.py data/raw/your_song.mp3 --artist "Artist" --title "Song"

# pick specific difficulties (names or star targets)
python src/main.py data/raw/your_song.mp3 --difficulties Easy,Hard,Expert
python src/main.py data/raw/your_song.mp3 --difficulties 2,5

# folder only, no zip
python src/main.py data/raw/your_song.mp3 --no-osz
```

Output in `data/output/`:
- `<Artist> - <Song>/` -- the beatmap folder: the audio (copied as-is) and
  one `<Artist> - <Song> (<creator>) [<Difficulty>].osu` per tier
- `<Artist> - <Song>.osz` -- the same folder zipped; **drag this onto
  osu!(lazer)** to import and play-test

**What the `.osu` writer emits** (`src/export/osu_writer.py`):
- `[General]` / `[Editor]` -- audio filename + harmless editor defaults
- `[Metadata]` -- title / artist / creator / difficulty name
- `[Difficulty]` -- HP / CS / OD / AR / SliderMultiplier straight from the
  Step 5 preset (`DifficultyPreset.to_osu_difficulty_section()`)
- `[TimingPoints]` -- one uninherited (red) line: the Step 3 tempo + offset
- `[HitObjects]` -- one record per object. `type` is the osu! bitfield
  (1 circle / 2 slider / 8 spinner, +4 new combo). Sliders are linear
  (`L|`) with the per-slide `pixel_length` and slide count from Step 4;
  spinners carry an end time; new combos start the map, follow every
  spinner, and follow any rest of 4+ beats.

**Slider timing.** osu! computes a slider's duration as
`length * slides / (100 * SliderMultiplier * SV)`. `slider_generator.py`
sized `pixel_length` against the same `SliderMultiplier` we write into
`[Difficulty]` (SV = 1, no green lines), so a rendered slider lasts the
number of beats Step 4 intended. If you change one, change the other.

## Testing the exporter

```bash
pytest tests/test_osu_writer.py -v
```

osu!(lazer) can't run in CI, so the bar is: **structurally valid v14, and
every number round-trips.**

**1. Line-format unit tests** -- exact header (`osu file format v14`);
sections present and in the required order; the timing point encodes
tempo + offset with the uninherited flag set; one correct record per
object kind (rounding, new-combo bit, slider curve / slides / edge fields,
spinner centring + end time); a pathless slider raises.

**2. Round-trip integration** -- render a real `build_map()` result, parse
it back with a small in-file parser, and check: object count preserved,
times preserved to the millisecond and non-decreasing, kinds preserved,
positions inside the playfield, exactly one timing point, and the slider
`length` field implies the intended beat duration. Then package an `.osz`
and confirm it's a valid zip containing the audio + one `.osu` per
difficulty, with filesystem-unsafe metadata sanitized.

**3. Full-pipeline smoke test** -- `src/main.py`'s `generate_beatmap_set()`
on a real song: five difficulties, every `.osu` parses, every hit-object
record has the right field count for its type.

**4. Manual (the real test)** -- drag `data/output/<Artist> - <Song>.osz`
onto osu!(lazer). It refuses to import a malformed file, so a clean import
+ a playable map is the end-to-end confirmation.

## Running Step 7 (analysis export)

Nothing to run on its own: `src/main.py` writes both artifacts at the end
of a normal run, into the beatmap folder it just produced.

```
python src/main.py data/raw/song.mp3
# ... Beatmap folder: data/output/Artist - Song
#     Analysis data:  data/output/Artist - Song/analysis.json  (324 KB)

python src/main.py data/raw/song.mp3 --no-analysis   # skip both files
```

Both sit *beside* the `.osu` files rather than inside the `.osz` -- the
packaged set is written before this step runs, so importing into
osu!(lazer) is unaffected by anything here.

**`spectrogram.png`** -- the same mel spectrogram `src/audio/visualize.py`
computes, saved as a plain image: no axes, no title, no colorbar. That
figure is a developer readout; this copy is a game asset. Two things it
does differently, both tuned against real songs rather than guessed:

- Columns are max-pooled down to at most 2048, since the frontend
  stretches the image across a screen-width panel and a six-minute track
  has 15,000-plus frames. Max rather than mean because the bright
  single-frame streaks *are* the percussive onsets -- averaging blurs
  away the one feature the image exists to show.
- The colour scale is pinned to a fixed -45..0 dB window instead of
  auto-scaling per image. Max-pooling lifts the floor, which washes a
  real song out to near-uniform pink with every streak technically
  present and none of them visible; pinning it also means brightness
  means the same thing from one map to the next.

**`analysis.json`** -- the numbers behind the map, which the `.osu`
format has nowhere to put:

```json
{
  "version": 1,
  "track":   { "name": "...", "duration": 219.15, "sampleRate": 22050 },
  "onsetSource": "Expert",
  "onsets":  [ { "time": 1.416, "strength": 0.626, "band": "low" } ],
  "beatGrid": { "bpm": 137.85, "offset": 0.42, "confidence": 0.73,
                "beatTimes": [ ... ] },
  "hitObjects": { "Easy": [ { "kind": "circle", "time": 1.375,
                              "x": 230.2, "y": 153.6, "strength": 0.626,
                              "band": "low", "snap": "1/1" } ] }
}
```

`strength`, `band` and `snap` are the provenance fields -- *why* a given
onset became the object it did -- and are the reason the file exists at
all. Detection sensitivity is per tier, so there is no single "the" onset
list: `onsets` comes from whichever tier detected the most, and
`onsetSource` records which one that was.

Times are rounded to the millisecond and positions to a tenth of an
osu!pixel, which roughly halves the file for digits nothing downstream
can use. A real 3m39s track at all five tiers writes ~324 KB.

## Testing the analysis export

```
pytest tests/test_analysis_export.py -v
```

**1. Unit tests** on the pure parts -- the record builders (every field
present, provenance preserved, no `endTime` on a circle) and the
spectrogram downsampling (stays within the width cap, and a lone bright
frame survives a 40x reduction).

**2. Round-trip integration** -- run a real `build_map()`, export, read
the JSON back, and check the onsets, BPM, beat times and every placed
object match what the pipeline produced. The PNG is checked to be a real
PNG by its magic bytes and width by its IHDR, rather than pulling in an
image library for two assertions.

**3. Size ceiling** -- a six-minute track at five tiers, asserted to stay
well under a few MB. This is the check that settled whether the frontend
can read the whole file at once (it can); if a future change makes the
file balloon, that test is where it shows up.
