# osu!-lazer-style Beatmap Generator (DSP Project)

Automatic rhythm-game beatmap generation from raw audio, exported directly
to the `.osu` file format so maps can be tested in real osu!(lazer).

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
[3] Tempo / beat-grid estimation        <- src/onset/beat_tracker.py
   |
   v
[4] Hit-object classification           <- src/mapping/
    (circle / slider / spinner)
   |
   v
[5] Difficulty scaling (1-10 stars)     <- src/mapping/difficulty.py
   |
   v
[6] .osu file export                    <- src/export/osu_writer.py
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
│   │   ├── object_classifier.py   # circle / slider / spinner    (STEP 4)
│   │   ├── slider_generator.py    # slider path + length + SV
│   │   ├── spinner_generator.py   # spinner duration -> rotations
│   │   └── difficulty.py          # star-level parameter tables  (STEP 5)
│   ├── export/
│   │   ├── __init__.py
│   │   └── osu_writer.py      # writes valid .osu v14 file       (STEP 6)
│   └── main.py                 # CLI entrypoint tying it all together
├── data/
│   ├── raw/                    # put input audio files here (.mp3/.wav/.ogg)
│   └── output/                 # generated .osu + song folders land here
├── notebooks/                  # exploratory DSP experiments (Jupyter)
├── tests/                      # unit tests per module
└── configs/
    └── difficulty_presets.yaml # onset threshold / snap / AR / OD / CS per star level
```

## Status

- [x] Step 1: Audio loading + STFT/spectrogram visualization
- [x] Step 2: Spectral-flux onset detection
- [ ] Step 3: Tempo/beat-grid estimation
- [ ] Step 4: Hit-object classification
- [ ] Step 5: Difficulty scaling
- [ ] Step 6: `.osu` export

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
