# Generation redesign — the studio rack, and showing the DSP's working

Status key: 📋 planned · 🚧 in progress · ✅ done. Update a step's header when
you start/finish it and append an "as built" note, the same way
`FRONTEND_PLAN.md`, `CAROUSEL_REDESIGN_PLAN.md` and `MENU_REDESIGN_PLAN.md`
do — this doc is meant to survive contact with the real implementation.

## 0. What this replaces, and why

Two screens are still wearing the Phase 6 prototype: `UploadScreen` and the
first half of `DspVisualizationScreen`. Both are `BasicButton`s and default
`SpriteText` over flat blue-grey literals — the same grey prototype look the
main menu had before `MENU_REDESIGN_PLAN.md`. Song select is a turntable, the
menu is a radio, gameplay has the neon HUD, and the screen you pass through to
*make* a map looks like a settings dialog.

The second half of `DspVisualizationScreen` — the four-stage reveal — is
themed, works, and is the best thing in the client. It is also showing about a
fifth of what the backend actually did. Today it shows four *results*:

| Stage | What it shows | What the backend actually did |
| --- | --- | --- |
| 1 | the mel spectrogram, as an image | STFT → 128 mel bands → dB relative to peak |
| 2 | onset markers, coloured by band | spectral flux → half-wave rectify → L2 per frame → **local median threshold (Bello et al. 2005)** → peak-pick with min spacing |
| 3 | vertical lines at the beat spacing, BPM counting up | **autocorrelation over the onset envelope → log-normal tempo prior → 0.5×/1×/2× octave re-scoring by phase alignment → pulse-train phase fit** |
| 4 | circles and sliders popping onto a playfield | 1/4 snap with collision-by-strength → min-spacing thinning → RMS sustain ratio per gap → slider/spinner/stream typing → distance-snap placement, **all of it five times over, once per difficulty preset** |

Everything in the right column is in memory during a normal run and is thrown
away. The project is graded on that column. This plan exports it and puts it on
screen.

### The three decisions this plan is built on

Settled up front (see §"Decisions" for the alternatives and why they lost):

1. **A second file, `dsp.json`, not a version bump on `analysis.json`.** The
   fourteen maps already in `data/output/` keep working exactly as they do now.
2. **Per-frame curves ship as base64-packed bytes, not JSON floats.** Full
   resolution, ~100 KB, and no downsampling decision baked into the file.
3. **The cinematic stays and gains stages; a separate `DspInspectorScreen`
   carries the depth.** The cinematic is the first-run spectacle; the inspector
   is what a grader can stop, scrub and poke at. Both reachable from song
   select, so every already-generated map becomes demo material.

### The visual idea: the generation half is a *studio*, not a *deck*

Song select is playback hardware — a turntable, a record, cassettes off a
shelf. The generation screens are the other half of the same world: the room
where the tape gets *made*. A reel-to-reel machine, a rack of analyser modules,
a mastering console, VU meters, a patch bay, chrome bezels and screws. Same
palette, same fonts, same "drawn from primitives, no art assets" rule — a
different set of objects, so the two halves of the app read as different rooms
rather than the same screen twice.

Concretely: `UploadScreen` becomes a **tape deck you load a cassette into**,
the running view becomes a **reel-to-reel machine with a row of stage lamps**,
the reveal plays across a **rack-mount analyser chassis**, and the inspector is
that same rack with its panels made interactive.

---

## Honesty rule — read this before drawing a single meter

This plan puts a lot of gauges, lamps and needles on screen. Every one of them
is either driven by real data or is visibly idle furniture. **No meter invents
a number.**

- While Python is running, nothing streams except stdout lines. So the stage
  lamps are driven by parsed progress lines (step 9), the tier counter by the
  real `[i/n]` in those lines, and the elapsed clock by the clock. There is no
  percentage bar, because there is no honest percentage.
- A VU meter needs a signal. During the run there isn't one, so there is no VU
  meter during the run — the reels turn, which is exactly what a turning reel
  honestly indicates, and that is all. The reveal and the inspector *do* have
  audio playing, so that is where a real VU lives, fed from
  `Track.CurrentAmplitudes` through `SpectrumAnalyser` exactly as
  `SpectrumRing` already is.
- Anything decorative — screws, bezel wear, the patch bay — is static and
  clearly not a readout.

This matters more here than it usually would: the entire point of the screen is
to be believed about what the DSP did.

## Traps already paid for — do not rediscover these

Every one of these cost a debugging session in an earlier phase, and every one
of them sits directly in this plan's path.

| Trap | Where it bit | What to do |
| --- | --- | --- |
| `TextureUpload` takes ownership of the image and consumes it later on the draw thread — wrapping it in `using` gives a white block | `SpectrogramReveal`, Phase 7 | Do not dispose the image. Every new texture path here has the same hazard |
| Assigning `InternalChildren` in `[BackgroundDependencyLoader]` silently replaces children added before load | `SpectrogramReveal`'s overlays never appeared | Build children in the constructor when anything adds overlays to you |
| A drawable that sets its own `Alpha = 0` inside its own `Update` stops updating and never comes back | recorded in memory as `drawable-hides-itself-deadlock` | `AlwaysPresent = true` on anything that hides itself |
| `TransformTo(nameof(Foo), …)` resolves the member by name at runtime and fails **silently** when it cannot | `MenuStrip.Expansion` | Animate a `Bindable` with `TransformBindableTo` |
| `SmoothPath` sizes itself around its vertices, so it lands off-centre unless shifted | `DrawableSlider` lines 203–209 | `path.Position = -path.PositionInBoundingBox(Vector2.Zero)` — the new `TraceGraph` needs exactly this |
| Scrolling to a panel added in the same frame silently lands back at the top | song select hand-off, Phase 7 | Schedule the scroll a frame later |
| Every density constant was wrong on first guess: 420 sparks is one solid block, 260 beat lines is a comb | Phase 7 as-built notes | Tune every new count against a real generated map on screen, never against the shape of the data |
| A green test suite says nothing about whether a screen renders | Phase 5's invisible cursor | Add a `ScreenshotHarness` scene per step and *look at the frame* |

## Reuse note — read before writing any new code

| Need | Already in the codebase |
| --- | --- |
| Drawing a curve as a line | `SmoothPath` (`osu.Framework.Graphics.Lines`), already used by `DrawableSlider`, including the bounding-box shift it needs |
| Retro type | `Graphics/RetroText.cs`; `Display` = Press Start 2P for numbers, `Body` = Audiowide for labels |
| Palette and the five-tier colour ramp | `Graphics/RetroPalette.cs` — `DifficultyRamp` / `ForDifficultyRank` is the *same* ramp song select's cassettes use; a tier keeps its colour across both screens |
| Band colours (low/mid/high) | `OnsetSparkLayer`'s `low_colour` / `mid_colour` / `high_colour` — promote them into `RetroPalette` so sparks, band curves and hit objects agree |
| Cassette shell, reels, paper label, screws | `SongSelect/CassetteButton.cs` — the upload screen's cassette is this vocabulary at a larger size |
| Cream paper sticker with a hand-applied tilt | `SongSelect/CassetteSearchBar.cs` |
| Chrome gradient, plinth, strobe dots, laying drawables out around a circle | `SongSelect/Turntable.cs` |
| Neon glow on a shape | `EdgeEffect`, as used by `HUD/GlowBar.cs` and `HUD/BeatBorderFlash.cs` |
| Layered background — art, knock-back, grade, scanlines, crossfaded, with the "never transform a layer that has not entered the tree" discipline | `SongSelect/CarouselBackground.cs`, `MainMenu/MenuBackground.cs` |
| Turning live FFT into something drawable: log grouping, perceptual curve, decay floor | `MainMenu/SpectrumAnalyser.cs` — pure and unit-tested; any live meter uses this, not raw bins |
| Beat-synced pulsing | `Gameplay/BeatPulse.cs` — pure, unit-tested |
| Synthesized UI sounds built once at startup rather than per press | `Audio/MenuSoundSynth.cs` + `MenuSoundPlayer.cs` |
| Running the backend and streaming its stdout | `Backend/BackendRunner.cs` — untouched by this plan |
| "Read a file from the beatmap folder, return null if it is unusable" | `Backend/AnalysisData.cs` — `DspDetail` copies this shape exactly |
| Loading a track out of a beatmap folder | `DspVisualizationScreen.startPreviewAudio` — `host.GetStorage` → `StorageBackedResourceStore` → `audio.GetTrackStore` |
| Rendering one frame to a PNG to check it | `OsuClient.Tests/Visual/ScreenshotHarness.cs` |

**Do not** add a second colour constant for a band, a second repo-root walker,
a second image-load path, or a second beat-phase formula.

---

# Decisions

### Why a second file rather than `analysis.json` v2

| Option | Why not chosen |
| --- | --- |
| Bump `analysis.json` to version 2 and add the new blocks to it | `AnalysisData.Parse` gates on `data.Version != SupportedVersion` — exact equality. A v2 file needs a range check anyway, and more to the point the fourteen sets already in `data/output/` are v1: with the inspector reachable from song select (step 19), "an existing map shows nothing at all" is the one outcome this plan must not produce |
| A binary sidecar — `.npy`, or raw little-endian floats | A third format for the frontend to parse, for no size win over packed bytes inside JSON |
| **(chosen)** A sibling `dsp.json`, versioned independently, simply absent on every map generated so far | Purely additive — the same relationship `analysis.json` itself has to the `.osz`. Degradation is automatic and already has a name: `analysis.json` alone → the four original stages; `+ dsp.json` → every new stage and the inspector. No existing file changes, so no existing map breaks |

### Why packed bytes rather than JSON floats

A four-minute track at `sr = 22050`, `hop = 512` is about 10,500 frames. Seven
curves at three decimals is roughly 400 KB of digits — tripling the folder's
JSON.

| Option | Why not chosen |
| --- | --- |
| Full-resolution JSON floats | ~400 KB of text carrying precision that no line 400 px tall can show |
| Downsample to 2048 columns, as `spectrogram.png` already does | At four minutes that is 117 ms per column. These curves are drawn *behind* millisecond-precise onset markers, so a spark would visibly sit beside its own flux peak. Worse: max-pooling flux while mean-pooling its threshold would draw the curve crossing the threshold nearly everywhere — a picture that lies about the algorithm |
| **(chosen)** Full resolution, base64'd binary, each curve carrying the encoding it was written at | No resolution decision baked into the file, and display-time reduction (min/max per pixel column, the way every audio editor draws a waveform) left to the frontend, which is the only side that knows the panel width. **Step 0 found that one width does not fit all seven curves** — see its as-built note: the two curves the client re-runs an algorithm over need exactness that quantisation cannot give, so `flux` is float32, `median` uint16, and the five that are only ever drawn stay uint8. Measured cost: 163 KB for a four-minute track, against a `spectrogram.png` of 500 KB in the same folder |

Scalars, counts, preset knobs, octave candidates and the two short tempo curves
stay plain readable JSON. They are small, and a human opening the file should
find the interesting numbers rather than a wall of base64.

### Why the frontend derives the threshold instead of reading it

`threshold[n] = delta + margin * median(flux[n-w : n+w])`. The median curve is
**tier-independent**; only `margin` and `delta` come from the preset. So
`dsp.json` ships the median curve once, plus every tier's `(margin, delta)`,
and the frontend computes any tier's threshold in one line.

This is not only a size win. It is what makes step 17 possible: two knobs for
`margin` and `delta`, the threshold redrawing as they turn, and the onset count
recomputing live. Exporting five baked threshold curves would give five fixed
pictures instead of the actual control surface the detector has.

### Why the cinematic survives the inspector

| Option | Why not chosen |
| --- | --- |
| Inspector only | An empty inspector is a worse thing to land on after a two-minute generation than a sequence that plays itself. The reveal is also the payoff for having waited |
| Cinematic only, extended to cover everything | A viewer only ever sees the frame the timeline chose. The autocorrelation, the octave scores and the tier funnel are things worth *stopping on* |
| **(chosen)** Cinematic, whose finale offers OPEN ANALYSER / PLAY | The sequence stays the first-run experience and stays skippable in one keypress; the depth is one click away, and also reachable later from song select without regenerating anything |

### One assumption, flagged

The "analysis parameters block" was not among the exports picked, but the three
that were cannot be decoded or labelled without a handful of its fields:
`sampleRate` and `hopLength` (frames → seconds, and autocorrelation lags →
BPM), `bandEdges` (which band curve is which), and the detector's window and
spacing constants (so the frontend's ported peak-picker matches the backend's).
Step 1 ships **only those**, as a small `frames` / `bands` / `detection`
header — not the wider "real labelled Hz axes everywhere" idea. Worth knowing
that once those fields are present, labelled axes cost almost nothing more, if
that turns out to be wanted after all.

---

# Part 1 — Backend: export what the DSP saw

Steps 0–3 are pure Python, testable with `.venv/Scripts/python.exe -m pytest`,
and change nothing the frontend already reads.

## Step 0 — Curve packing, agreed at both ends ✅

**Goal:** one function each side that round-trips a float array through base64
bytes. No pipeline change.

- `src/export/curve_pack.py`: `pack(values) -> {"scale": float, "data": str}`
  and `unpack(record) -> np.ndarray`. `scale` is the array's maximum — guard a
  maximum of zero by writing scale 1 and an all-zero payload, which is what a
  silent track gives you. Clamp negatives to zero: every curve here is a
  magnitude.
- Tests: round-trip within `scale / 255`; a constant array; an all-zero array;
  a single element; and — the property the whole visualization rests on — the
  argmax of a real flux curve surviving quantisation.
- The C# half lands in step 4, but **write the fixture here**: dump a known
  array's packed record to `tests/fixtures/curve_pack_reference.json` and have
  both the Python test and the later C# test assert against that same file. A
  format pinned by two independent tests against one fixture does not drift.

*Why first:* it is the only new format in the plan, it is tiny, and getting it
wrong stays invisible until a curve renders as noise.

*Built in `src/export/curve_pack.py`, with 58 tests in
`tests/test_curve_pack.py`. The plan's single-encoding assumption did not
survive contact with a real track, and that is the main finding:*

- **One encoding was not enough, and the reason is peak-picking, not
  looks.** The client re-runs the onset peak-picker over the flux curve,
  and that algorithm asks `flux[i] > flux[i-1]` and `flux[i] >
  threshold[i]`. Both are knife edges, so an encoding that flattens two
  near-equal neighbours into one value does not blur a peak — it deletes
  it. Measured against `closer` / `bad_apple` / `golden` at all five
  presets: **uint8 flux loses 7 of 499 onsets** on Hard; **uint16 flux is
  exact on the three easier tiers but still shifts one peak by a frame**
  (two neighbours 2e-6 apart collapsing to the same value) **and flips one
  onset sitting 2e-6 above its own threshold**; **float32 flux reproduces
  the backend exactly in all fifteen song/tier combinations.** The median
  curve is a moving median with no single-frame spikes to lose and is
  exact at uint16 beside a float32 flux.
- **So the record names its encoding** — `"uint8"`, `"uint16"` or
  `"float32"` as a plain string — rather than the reader having to know
  which curve is which. One decode rule covers all three:
  `value = raw / divisor * scale`, where the divisor is 255, 65535 or 1.
- **`count` beside `scale`, `encoding` and `data`.** Derivable from the
  payload length and written anyway: it is the only corruption this
  format can detect, and a truncated curve that silently draws short is
  exactly the failure the frontend's forgiving loader wants to catch.
  `unpack` also rejects a payload that is not a whole number of samples.
- **Little-endian, pinned by a test that asserts the actual bytes.** The
  reader on the other side is C#; a format whose meaning depends on the
  machine that wrote it is not a format.
- **The fixture has a generator**, `tests/fixtures/make_curve_pack_reference.py`,
  carrying a warning that regenerating it to make a red test go green
  defeats its purpose. It emits a case per encoding — a reader that
  handles uint8 correctly can still get the other two wrong silently —
  on a scale of 2.5 rather than 1, so a C# reader that ignores `scale`
  fails loudly instead of looking almost right.

## Step 1 — `dsp.json`: the curves ✅

**Goal:** a real generated map's folder gains a `dsp.json` with seven curves in
it. Nothing reads it yet.

- New `src/export/dsp_trace.py`, a sibling of `analysis_export.py` and with the
  same contract: a pure consumer of things already in hand, it writes a file,
  and nothing streams or blocks.
- `build_curves(track, mel_db)` computes, from the mel spectrogram
  `export_analysis` is *already handed*:
  - `flux` — `detector.spectral_flux` → `normalize`
  - `median` — `scipy.ndimage.median_filter` over `flux` at the detector's own
    window. This is `adaptive_threshold`'s middle term, factored out
  - `envelope` — `flux` under the tracker's three-frame moving average; this is
    literally the signal Step 3 autocorrelated
  - `rms` — `librosa.feature.rms` at the same hop, normalised; this is what
    `sustain_ratio` thresholds against
  - `bandLow` / `bandMid` / `bandHigh` — `detector.band_limited_flux`, each
    normalised
- Header: `frames {sampleRate, hopLength, nFft, nMels, frameRate, count}`;
  `bands {low: [20,200], mid: [200,2000], high: [2000,10000]}`;
  `detection {medianWindowSec, minSpacingSec}` plus every tier's
  `{margin, delta}`.
- **No new audio pass and no new mel pass.** Everything above derives from
  `mel_db` plus one cheap `librosa.feature.rms` call. Budget it well under a
  second on top of a run that already takes minutes, then measure it once and
  write the real number into the as-built note.
- Wire it into `export_analysis` — same folder, same call site, skipped by
  `--no-analysis` along with the rest.
- Tests mirroring `tests/test_analysis_export.py`, including **a size
  ceiling** — that test is why the analysis file stayed sane, and this is the
  file with the arrays in it. Ceiling: 250 KB for a six-minute synthetic track.

*Verify:* generate against a real song, unpack `flux` in a notebook, and plot
it beside `detector.plot_onsets`'s own flux panel. They must be the same curve.
If they are not, nothing downstream is worth building.

*Built as planned, in `src/export/dsp_trace.py` with 30 tests in
`tests/test_dsp_trace.py`, and wired through `export_analysis` and
`src/main.py`. Verified more strongly than the plan asked for, and the
numbers it wanted measured are measured:*

- **The verification is an assertion, not a notebook plot.** "Plot it
  beside the debug figure and check they match" is a check nobody repeats.
  Instead every curve is asserted against the very function the pipeline
  runs — `flux` against `spectral_flux`, `median` against `local_median`,
  `envelope` against `onset_envelope`, `rms` against the RMS the
  classifier measures sustain with. A curve that is merely flux-*like*
  would make the whole visualization a lie told convincingly.
- **Step 5's reproduction test already runs, in Python, one part at a
  time.** `test_picking_peaks_from_the_packed_curves_reproduces_the_backend`
  decodes flux and median out of the built document, rebuilds each tier's
  threshold as `delta + margin × median`, picks, and asserts the frames
  equal `detect_onsets`'s. It passes at all five presets. Run end to end
  against a real song, the written `dsp.json` re-derives `analysis.json`'s
  491 onsets exactly, to within the millisecond that file rounds to. The
  C# port in step 5 now has a target it is known to be able to hit.
- **The test track is a click track with noise and a sustained tone mixed
  under it.** A bare click track is near-silent between hits: its flux is
  sparse and its local median is flat zero, so it exercises none of the
  near-ties and knife-edge crossings the peak-picker is fragile around.
  On a bare click track the encoding bug found in step 0 would not have
  shown up at all.
- **The open question "how much does this add to a run" is answered:
  0.11 s** on a 4m22s track (0.16 s on a six-minute one), against a
  five-tier run measured in minutes. No second decode and no second mel
  pass — the module is handed the spectrogram `export_analysis` already
  has, and RMS is one `librosa` call.
- **Sizes:** 163 KB for a real 4m22s track, 223 KB for a six-minute
  synthetic, 123 KB for the two-tier run used as the end-to-end check.
  The ceiling test sits at 400 KB — roomy on purpose, since it exists to
  catch a change that makes the file grow by a *multiple* (a curve added
  per tier, an encoding widened unnoticed), not to shave kilobytes off a
  file smaller than the `spectrogram.png` beside it.
- **Two small refactors rather than two copies.** `adaptive_threshold`'s
  moving median is now `detector.local_median`, called by both the
  threshold and the export — the median is the only part of the threshold
  that is preset-independent, which is the whole reason the file ships it.
  And `onset_envelope` gained an optional `mel_db`, so the export gets the
  exact array Step 3 autocorrelated without paying for a second mel pass.
  Both are additive; all 174 backend tests pass.
- **`export_analysis` now returns three paths** (`analysis`, `spectrogram`,
  `dsp`) and takes an optional `presets` that only reaches `dsp.json`. The
  CLI prints the new file's size beside the other two. Passing no presets
  still writes a valid document, just without the per-tier block — which
  is what a caller that doesn't have them should get.

## Step 2 — `dsp.json`: how the tempo was found ✅

**Goal:** the file carries Step 3's *search*, not only its answer.

- `tempo_trace(envelope, sr, hop, grid)` in `dsp_trace.py`, reusing
  `beat_tracker`'s own `_autocorrelation`, `tempo_prior_weight`,
  `_phase_scores` and `_bpm_to_lag` — **import them, do not reimplement them.**
  A second copy of the octave rule is a second thing that can be wrong.
- Writes:
  - `acf` — the normalised autocorrelation across a lag range covering the
    search band *and both its octaves*, so the 1× / 2× / ½× peak structure is
    visible rather than cropped away. Plain floats: at `sr/hop ≈ 43 fps` the
    50–210 BPM band is only lags ~12–52, so even with octave headroom this is
    about 120 numbers
  - `prior` — `tempo_prior_weight` at each of those lags, with `searchLagMin`
    and `searchLagMax` so the frontend can shade the band actually searched
  - `rawBpm` (argmax of `acf × prior`) and `chosenBpm` (after octave
    correction). **These differ on real songs, and that difference is the
    entire reason Step 3 has an octave stage**
  - `octaveCandidates` — for each of 0.5× / 1× / 2×: `bpm`, `phaseStrength`,
    `priorWeight`, `score`, `chosen`
  - `phaseScores` — the pulse train's score at each integer offset within one
    period (about 20 numbers at 128 BPM), plus `phaseOffsetFrames` and
    `phaseStrength`
- Tests: on a synthetic click track the chosen BPM is the generated one; the
  winning candidate is flagged; `phaseScores` has exactly `ceil(period_frames)`
  entries; and the argmax of `acf × prior` inside the search band lands on
  `rawBpm`'s lag.

*Why this is the highest-value export in the plan:* Step 3 is the only part of
the pipeline that performs a genuine search with a genuine ambiguity in it, and
it is currently a number that appears from nowhere.

*Built as planned, as `tempo_trace()` in `src/export/dsp_trace.py` with 27
tests. The plan's own open question about this stage is now answered, with
real data:*

- **Octave correction changes the answer on 2 of the 14 tracks on disk**, so
  the stage is worth building and there is a named case to build it against.
  `idol.mp3` is the demo frame: raw 166.04 BPM, chosen 83.02, won by
  `2.314 strength × 0.868 prior = 2.009` against `2.182 × 0.896 = 1.955` — a
  2.8% margin — with the 332 BPM candidate struck out as out of range.
  `gtasa.mp3` is the other (47.55 → 95.10, where the raw estimate fell
  outside the search band entirely and only the 2× candidate was viable).
- **Even where it doesn't flip, the scores are close enough to be worth
  showing.** `closer.mp3` is 1.326 against 1.210, `bling.mp3` 0.921 against
  0.903. The panel is not just "here is the winner" on those tracks, it is
  "here is how nearly it went the other way", which is the more honest
  picture of what the algorithm does.
- **`octaveCorrected` is a field, not something the client has to infer**, so
  the stage can say "the first answer was wrong" only when it actually was.
- **Out-of-range candidates are written rather than dropped**, flagged
  `inRange: false` with a zero score. `_resolve_octave` skips them silently;
  writing them lets the panel show them struck out, and "this one was not
  even considered" is part of the story.
- **`weighted` is written even though it is `acf × prior`.** Derivable, and
  written anyway because it is the curve the peak marker sits on — and a test
  asserts it stays their product, so the marker cannot start pointing at
  something else.
- **The autocorrelation reaches an octave past each edge of the search band.**
  The band itself is only lags 12–52 at 43 fps; drawing just those crops away
  the 1×/2×/½× peak structure that the whole stage is about. The wider range
  is ~99 floats instead of ~40.
- **The whole block is about 4 KB and does not grow with the track** — it is
  bounded by the lag range and one beat period. That is asserted directly
  (a 200-second track's block must match a 20-second track's within 15%)
  rather than as a fraction of the file, since the curves scale with duration
  and this does not.
- **Everything is computed with `beat_tracker`'s own functions**, imported
  rather than reimplemented, including the private `_autocorrelation`,
  `_phase_scores` and `_bpm_to_lag`. A second copy of the octave rule would
  be a second thing that can be wrong, and the panel's whole claim is that it
  shows what the pipeline did.

## Step 3 — `dsp.json`: snapping, sustain, and the five-tier funnel ✅

**Goal:** Steps 4 and 5, which are currently invisible, become data.

- **Snapping.** `snap_onsets` already computes the quantised time and throws
  the raw one away. Record, per kept onset: `rawTime`, `snapTime`, `deltaMs`
  and the `snap` label, plus a `merged` count for onsets that lost a slot
  collision to a stronger neighbour. Rounded to the millisecond, like
  everything else in these files.
- **Sustain.** `classify()` computes a `sustain_ratio` per gap and discards it.
  Give it an **optional `trace=None` list** that it appends
  `{time, gapBeats, sustainRatio, became}` to at each slider / spinner / rest
  decision. Default `None` means zero cost and no behaviour change; only
  `build_map_detailed` passes one, and only when asked. Do not change what
  `classify` returns.
- **The funnel**, per tier: `detected → snapped → afterSpacing → objects`, plus
  `kinds {circle, slider, spinner}`, a `snapHistogram {1/1, 1/2, 1/4}`, and the
  preset's own knobs (`margin`, `delta`, `snapDivision`, `minSpacingBeats`,
  `circleSize`, `approachRate`, `distanceSpacing`, `streamMinLen`). `main.py`
  already loops the tiers and already holds each tier's onsets in `detected` —
  the intermediate counts come from calling the *pure* `snap_onsets` and
  `enforce_min_spacing` on onsets already in memory. No extra detection pass.
  (`rhythm_slot_count` computes the same thing today but re-detects to do it;
  give it an optional `onsets=` parameter and call that, rather than writing
  the loop a second time.)
- Only the `onsetSource` tier gets a full sustain trace; the others get counts.
  Five full traces is four times the bytes for a panel that shows one tier.
- Tests: the funnel is monotonically non-increasing left to right for every
  tier; `deltaMs` never exceeds half a snap division; and `trace=None` produces
  byte-identical objects to today — a guard against the decision log quietly
  changing behaviour.

**Optional, and worth it:** an `--analysis-only` flag on `src/main.py` that
runs detection and writes `analysis.json`, `dsp.json` and `spectrogram.png`
without re-writing the `.osu`/`.osz`. The fourteen sets in `data/output/` are
all v1; this upgrades each in one pass instead of regenerating maps that are
already fine.

*Built as planned, with 25 further tests. Two departures from the plan and one
finding that cost more time than the feature did:*

- **The sustain trace re-runs `classify()` rather than being threaded through
  `build_map_detailed`.** The plan had `main.py` collecting traces; in
  practice that means `difficulty.py` and `main.py` both learning about
  tracing to carry data neither uses. Instead `dsp_trace.sustain_trace()`
  calls `classify()` again for the source tier alone, with a trace attached.
  That is a second pass over the *classifier* — not over the audio, and not
  over detection, which are the expensive parts — and it leaves the
  pipeline's own call sites untouched.
- **The trace is filtered, not truncated.** Every non-circle outcome is kept
  however short its gap, plus the circles whose gap was long enough that a
  slider or spinner was genuinely on the table. A circle after a 1/4 gap never
  consulted sustain in any meaningful sense — the gap test ruled a slider out
  first — and there are thousands of those. On a real five-tier run this is
  37 records instead of 678, and it keeps exactly the ones the panel wants:
  the first record off `golden.mp3` is a 2.75-beat clean gap with a sustain of
  0.138 against a threshold of 0.5, which stayed a circle. That is the "why
  isn't this a slider" frame, straight out of the data.
- **`snap_onsets` now records `raw_time` and a `merged` tally**, and
  `classify` takes an optional `trace`. Both are strictly additive: a test
  asserts that attaching a trace produces byte-identical objects, because a
  decision log that changed a decision would make every number in it describe
  a map that was never written.
- **The test fixture had to be rebuilt, and that was the real work here.**
  Three assertions failed on the first run — the tiers all detected the same
  onsets, and every object snapped to 1/1 — and the fault was the fixture, not
  the code: a click track is one identical hit per beat over silence, which
  has no near-ties, a local median of flat zero, one snap division, and no gap
  long enough to classify. The replacement is **calibrated against the
  detector's real thresholds** rather than guessed, which took three
  measurement passes: beats at ~0.27 normalised flux and eighths at ~0.17
  (above every tier's threshold), ghost notes at ~0.046 — above Expert's 0.030
  and below Easy's 0.080, so only the loosest tiers hear them — plus off-grid
  subdivisions, broadband noise to lift the median off zero, and three quiet
  windows (a ~2-beat gap with a pad behind it, a ~9-beat gap with a pad, and a
  long gap where a struck note decays to nothing) so all three object kinds
  and the near-miss circles actually occur. This is the same lesson Phase 7
  recorded about density constants: the numbers are not guessable from the
  shape of the data.
- **The funnel on a real song does what the panel needs it to.** From
  `golden.mp3`, detected → snapped → spaced → objects:
  Easy `320 → 250 → 110 → 110`, Hard `491 → 474 → 322 → 314`,
  Expert `712 → 678 → 678 → 678`. Easy's collapse is its 2-beat minimum
  spacing; Expert barely thins at all and folds no streams
  (`streamMinLen: 999`). Snap deltas on Expert: 678 of them, mean 28 ms, max
  60 ms against a half-division bound of 61 ms — a test asserts nothing can
  exceed that bound, since a larger move would mean the snap went to the wrong
  slot entirely.

---

# Part 2 — Frontend: read it, and prove the port is honest

## Step 4 — `DspDetail`, the forgiving reader ✅

**Goal:** C# models for `dsp.json`, loading to `null` for anything unusable.

- `Backend/DspDetail.cs` and `Backend/CurveData.cs`, shaped exactly like
  `AnalysisData`: a `FileName` constant, a `SupportedVersion`, a
  `LoadFromFolder` that returns null, a `Parse` that catches `JsonException`,
  and a `HasContent`. Copy that class's remarks discipline too — "no file is a
  normal outcome, not an error" is the contract the entire degradation story
  rests on.
- `CurveData.Decode()` → `float[]`, from `scale` plus base64, with a
  `FrameRate`-derived `TimeAt(i)`.
- Tests against the step 0 fixture, plus: a missing file; a truncated file; a
  v1 `analysis.json` handed to it by mistake; a future version; and a real
  `dsp.json` copied into the test fixtures.

*Built as `Backend/CurveData.cs` and `Backend/DspDetail.cs`. Three decisions
the plan left open:*

- **Curves validate without decoding.** `CurveData.IsValid` checks the
  encoding is known and that the base64 length is exactly the sample count it
  claims — arithmetic on the string's length, no decode. So `Parse` can
  validate all seven curves on load for almost nothing, and `Decode()` stays
  lazy and cached (a trace redrawn every frame must not re-run base64 over
  11,000 samples each time; there is a test that the same array comes back).
- **A corrupt curve rejects the whole document**, rather than being dropped
  individually. A half-written file from an interrupted run should read as
  "no trace for this map", not as a screen of traces where one stops partway
  through the song.
- **Little-endian is read explicitly**, through `BinaryPrimitives`, not
  `BitConverter` — whose byte order follows the machine. Two tests assert the
  actual bytes, because a reader that gets this wrong on a `uint16` produces
  256× the right value and on a `float32` produces garbage, and neither looks
  like an endianness bug when you see it on screen.
- **The fixtures are real generated files**, not hand-written JSON:
  `tests/fixtures/make_client_fixture.py` runs the actual pipeline over the
  calibrated 24-second track from step 3 and commits its `analysis.json` and
  `dsp.json` (32 KB together). Hand-written samples agree with the reader by
  construction — they can only prove it parses what the test author imagined
  the writer emits. Both fixture sets are found by walking up to the
  repository root with `BeatmapLibrary.FindRepositoryRoot`, so there is one
  mechanism, and tests skip rather than fail outside a checkout.

## Step 5 — `OnsetPeakPicker`: the port, proved against the backend ✅

**Goal:** C# can re-derive the backend's onsets from the exported curves.

- `Backend/OnsetPeakPicker.cs` — pure, no framework types:
  `Threshold(median, margin, delta)` and
  `Pick(flux, threshold, minSpacingFrames)`, ported line for line from
  `adaptive_threshold`'s outer form and `pick_peaks`. Keep the exact quirks:
  the one-sided strict local-maximum test, the greedy keep-the-stronger
  replacement inside the spacing window, and the **ceiling** on the
  spacing-in-frames conversion — the Python comment explains why truncation is
  wrong there, and the port must not quietly reintroduce it.
- **The test that makes this feature honest:** take a real generated folder,
  decode `flux` and `median` out of `dsp.json`, apply the `onsetSource` tier's
  `margin` and `delta`, pick, and assert the result reproduces
  `analysis.json`'s onset list — same count, every onset within one frame
  (~23 ms).
- If it does not reproduce, **stop and fix the port before building step 17.** A
  knob producing numbers the backend would not produce is worse than no knob:
  it is a demo that misrepresents the algorithm.

*Why here, before any drawing:* this is the riskiest correctness claim in the
plan, it is testable with no window open, and step 17 is built entirely on it.

*Built as `Backend/OnsetPeakPicker.cs`, and **it reproduces exactly.** 92 new
C# tests, 453 in the suite, all green.*

- **The claim is asserted twice, from two directions.** Re-picking from
  `dsp.json` at the source tier's sensitivity recovers `analysis.json`'s onset
  list — same count, every time within the millisecond that file rounds to.
  And separately, *every* tier's pick count matches the `detected` figure its
  own funnel records — which covers the tiers `analysis.json` carries no
  onsets for at all. The plan asked for the first; the second is what makes it
  a property of the port rather than of one lucky tier.
- **The three fragile details each got their own test**, because all three are
  the kind of thing a later tidy-up would "fix": the local-maximum test being
  strict on the left and loose on the right (a plateau must yield one peak,
  not none and not all of it); the spacing window *replacing* the kept peak
  with a stronger neighbour rather than skipping it; and seconds converting to
  frames by ceiling, not truncation — 0.05 s at 43.07 fps is 2.15 frames,
  which must become 3, and truncating would silently enforce a shorter gap
  than asked for.
- **A threshold shorter than the flux is refused** rather than picked over
  until it runs out, which would otherwise stop silently partway through a
  song and look like a quiet second half.
- Nothing about the port needed a window open, which is why it came before any
  drawing: the riskiest claim in the plan is now settled, and step 17's knobs
  are known to be able to tell the truth.

## Step 6 — `TraceGraph`: the component every graph is made of ✅

**Goal:** one reusable curve drawable, with a screenshot scene on real data.

- `Graphics/TraceGraph.cs` — in `Graphics/`, not `Screens/Generation/`, because
  the inspector uses it too:
  - takes a `float[]` and a frame rate, and draws a `SmoothPath` over a visible
    time window `[t0, t1]`
  - **reduces for display, not in the file**: with more samples than pixels,
    walk each pixel column and emit its min and its max — the standard waveform
    draw. This is what keeps a single-sample peak visible at any zoom, and it
    is why step 1 could afford to ship full resolution
  - remembers `path.Position = -path.PositionInBoundingBox(Vector2.Zero)`
    (`DrawableSlider:203`), or the trace lands off-centre
  - offers `Fill` (a translucent area under the line) for flux, `Line` for
    envelopes, and `Dashed` for a derived threshold
  - exposes a `Progress` property, 0..1, for the draw-in reveal — the same
    reveal-by-progress idea as `SpectrogramReveal`'s wipe, so the curve and the
    spectrogram above it can wipe in together on one shared axis
- Vertices rebuild on a window or size change, **never per frame.** A
  10,000-point path rebuilt every frame is the one way this becomes a
  performance problem.
- Unit-test the reduction: a spike one sample wide survives a 100:1 reduction.
- **Screenshot scene `trace-graph`** — a real decoded flux curve with its
  derived threshold over it. Look at it: if the threshold does not visibly ride
  the curve's local level, something upstream is wrong.

*Built as `Graphics/TraceGraph.cs` with 21 tests, and rendered. **The
screenshot found something no test could have**, which is the whole reason the
step ends with looking at a frame:*

- **A linear value axis makes these curves unreadable.** Measured on the real
  fixture: spectral flux has a median of 0.0001 and a maximum of 1, and the
  adaptive threshold drawn over it sits at **3% of the panel height**. The
  first render was a correct graph of the right numbers that showed nothing —
  a flat cyan line along the bottom with an invisible amber thread under it.
  The fix is `ValueCurve`, the same perceptual exponent `SpectrumAnalyser`
  already applies to the menu's FFT ring, for the same reason level meters are
  drawn in dB. At 0.4 the threshold sits at 31% and the peaks visibly cross it.
- **That transform is honest, and there is a test proving it.** The exponent
  is monotonic, so applying the *same* one to a signal and to its threshold
  leaves every crossing exactly where it was: the picture still says "detected
  here" in precisely the frames the detector did. Asserted over 2,000 random
  flux/threshold pairs. The corollary is written into the API docs — curves
  compared against each other **must** share a `ValueCurve` and a
  `ValueRange`, or they will appear to cross where the detector never did.
- **`FitValueRange()` for a curve shown on its own.** The local median peaks
  around 0.005 on this track; against flux's 0..1 axis it is a flat line. Fitted
  to its own range it becomes the most informative trace in the scene — it
  visibly rises through the busy sections and flattens through the quiet
  windows, which is exactly what a local noise floor should do.
- **`Fill` is not a second code path.** It pins every column's bottom to the
  baseline, so the same single zig-zag path renders as a filled area.
  osu.Framework has no polygon primitive and this avoids needing one.
- **`Progress` drives the mask, not the vertex list** — a wipe over a finished
  path, the same arrangement `SpectrogramReveal` uses. Rebuilding 2,000
  vertices per frame during a reveal was the obvious trap and it is avoided by
  construction.
- The reduction keeps min *and* max per column, and two tests pin both
  directions: a one-sample spike survives 100:1 **and lands in its own
  column** (a peak that survived at the wrong x would put an onset marker
  beside its own flux peak — the exact failure the file format exists to
  avoid), and peaks sparser than the columns are not smeared into onsets that
  are not there.

*Note for later steps: running `dotnet test` immediately after a screenshot
capture fails ~57 tests, taking 70s instead of 16s. **Diagnosed in step 7 and
it is not a GL-context clash**: `dotnet run -- --screenshot` deletes
`SixLabors.Fonts.dll`, `SixLabors.ImageSharp.Drawing.dll` and
`OsuClient.Tests.deps.json` out of `bin/Debug/net8.0/`, even with
`--no-build`, and the tests then die on `Could not load file or assembly
'SixLabors.Fonts'`. The failures are scattered across gameplay, menu and
generation, so it reads like a broad regression and is not one. **The fix is
`dotnet build --no-incremental`** — and `--no-incremental` is the part that
matters, as step 9 found out: a plain `dotnet build` sees the project as up to
date, skips the copy step and leaves the files deleted, so it only appears to
work when a source file happened to change in between. The loop for every
visual step is: build → screenshot → look at the PNG →
`build --no-incremental` → test.*

## Step 7 — The rack: chrome, lamps, switches, knobs ✅

**Goal:** the kit of physical parts the next four parts are assembled from.

- `Graphics/Rack/`, everything drawn from primitives per the house rule:
  - `RackPanel` — brushed-chrome bezel (the `Chrome` → `ChromeDark` vertical
    gradient `CassetteButton` and `Turntable` already use), a rounded inset
    well, four corner screws, and an optional engraved `RetroText` title
  - `IndicatorLamp` — a small domed LED: off (dark, faintly tinted), lit
    (`EdgeEffect` glow), and a slow breathing state for "working"
  - `ToggleSwitch` — a chrome paddle that throws up and down beside a lit
    legend
  - `ControlKnob` — a knurled dial, turned by drag or scroll, with a pointer
    line and a tick arc, exposing a `BindableFloat` (**a bindable, not a plain
    property driven by `TransformTo(nameof(...))` — see the traps table**)
  - `SegmentReadout` — a numeric readout in `RetroFontFamily.Display` on a dark
    inset, with a faint unlit-segment ghost behind it
- **Screenshot scene `rack-kit`** — one of each, in every state, at two window
  sizes.

*Why a kit step rather than building parts as they are needed:* four screens
want these, and five ad-hoc chrome gradients will not match each other.

*Built in `Graphics/Rack/`, all five, and rendered. The kit step paid for
itself immediately: **three of the five parts were wrong in the first frame
and every one of them looked correct in code.***

- **The knob pointers did not render at all.** The pointer container inherited
  the default top-left origin, so `Rotation` swung it about the knob's corner
  and the masked face clipped it away entirely. Every knob drew identically
  whatever its value — which is the worst possible failure for a control whose
  whole job is showing a value, and completely invisible without the frame.
  Fixed by centring the container's anchor and origin; the four knobs in the
  scene now visibly point at 1.10, 1.50, 2.90 and 0.050.
- **The knurling and the tick arc were being swallowed.** Both were drawn, both
  were technically inside the panel, and both were too low-contrast against the
  chrome gradient to see. Knurls moved out to the rim and darkened; ticks
  lengthened and brightened.
- **The switch positions differed only in brightness**, which is hard to read
  in a still frame — and a still frame is most of how this screen gets checked.
  The paddle's gradient now flips as well as darkening, so up and down differ
  in silhouette.
- **`EdgeEffect` requires `Masking = true`**, or the framework throws
  `Can not have border effects/edge effects if masking is disabled` at draw
  time rather than at construction. Cost one crash on `SegmentReadout`'s glow
  container; worth knowing before adding a glow to anything in steps 8–19.
- **`Drawable.Colour` reads back as a `ColourInfo`**, not a `Color4`, so it
  cannot be handed to the `Color4` extension helpers. Anything that needs its
  own colour back keeps a `Color4` field beside it.
- `RackPanel` is a `Container` with `Content` pointed at the recessed well
  rather than a `CompositeDrawable`, so callers add children the ordinary way
  and land inside the recess instead of over the bezel.

---

# Part 3 — The upload screen becomes a tape deck

## Step 8 — `UploadScreen` reskin ✅

**Goal:** the last grey screen in the client is gone. Identical behaviour,
entirely new surface.

The screen is a **deck with an open bay**, and one object carries the whole
form:

- **Background** — `MenuBackground`'s layering (wallpaper → knock-back → grade
  → scanlines) with the Ken Burns drift switched off. This is a workbench; it
  holds still.
- **The bay and the cassette** — a large cassette drawn in `CassetteButton`'s
  vocabulary (shell, two reels, paper label, corner screws) sitting in a
  recessed bay. Empty state: the bay is empty and lit by a dim interior lamp,
  legend `DROP A TAPE`. A dropped or browsed file slides the cassette in and
  the reels begin to turn. The bay **is** the drop zone — the whole-window
  `DragDrop` handler is unchanged; only what it animates is new.
- **Artist and Title** are written on that cassette's own paper label — cream
  sticker, hand-applied tilt, straight out of `CassetteSearchBar`. Two
  `BasicTextBox`es restyled onto the plate. This is the unification worth
  having: the file and its metadata are one physical object rather than a form.
- **Difficulties** are five mini cassette spines standing in a rack beside the
  deck, in `RetroPalette.ForDifficultyRank` order — **the same five colours
  song select gives the same five tiers.** Pushed in and lit means selected;
  pulled out and desaturated means not. Each spine's label carries its tier
  name and star target.
- **LOAD TAPE** (was Browse) — a chunky chrome transport button. Its
  `Choosing…` disabled state becomes the button held down with its lamp lit.
- **REC** — a round red record button with a glow, dull when disabled, and a
  REC lamp on the panel that lights on press before the screen transitions.
- **Status and errors** — an `IndicatorLamp` and an engraved legend on the
  front panel. `BackendPaths.Locate`'s missing-backend message goes there with
  the red lamp lit, instead of being a red sentence floating under a form.
- **Escape to go back** — engraved on the bezel, not a floating grey label.

**Do not touch** `chooseFile`, `presentFileSelector`, `onFilePicked`, the
`DragDrop` subscribe/unsubscribe pair, or any of the `Exposed for tests`
properties. `TestSceneUploadScreen` and `NativeFileDialogTests` must pass
unchanged; if a test needs editing, the reskin has reached behaviour it was not
supposed to reach.

*Verify:* screenshot scenes `upload-empty`, `upload-loaded` (cassette in,
metadata typed, three tiers selected) and `upload-no-backend`.

*Built as planned, in `CassetteBay.cs`, `TierSpine.cs` and a rewritten
`UploadScreen`. **`TestSceneUploadScreen` and `NativeFileDialogTests` pass
unedited**, which was the constraint: the reskin stops at the surface and
every file-handling method below it is untouched.*

- **Two things were extracted rather than copied.** The ink-on-cream text box
  and its hand-written caret lived privately inside `CassetteSearchBar`; they
  are now `Graphics/PaperTextBox.cs` and both screens use them. The caret in
  particular has to be written out by hand (`BasicCaret` repaints itself white
  on every keystroke, and white on cream paper is invisible), so a second copy
  was exactly the kind that drifts. Song select was re-screenshotted afterwards
  to confirm the search sticker still renders.
- **`MenuBackground` gained a `Drifting` flag.** The menu's slow Ken Burns
  drift is right behind a moving logo and wrong behind a form, where a
  photograph sliding under a text field reads as a bug.
- **The backdrop stays greyscale, deliberately.** `MenuBackground` only spreads
  colour when asked, and not asking turned out to be the better picture: with
  the art monochrome, every coloured thing on the screen is carrying meaning —
  the difficulty ramp, the record lamp, the label's magenta spine. It is also
  knocked back further than the menu's own 22%, because here the art is a
  backdrop for text rather than the subject.
- **Three things the frame caught.** The `⏏` eject glyph is not in Audiowide
  and silently drew nothing, so the button read `LOAD TAPE` with a gap;
  removed rather than left as a hole. An unlit status lamp is a dark red dot,
  which on an otherwise empty panel reads as a stray artefact, so it now hides
  entirely when there is no message. And the wallpaper was never being loaded
  at all — `MenuBackground` needs an explicit `SetBackground`, so the first
  render was the bare gradient and looked intentional.
- **C# will not let a helper method assign a `readonly` field**, even one
  called from the constructor. `CassetteBay` builds its shell and label in
  helpers, so those fields are plain and the reason is commented — otherwise
  the next person "tidies" the modifier back on.
- The tier spines use `RetroPalette.ForDifficultyRank`, and a side-by-side
  capture of song select confirms the ramp agrees: Easy cyan, Normal mint,
  Hard amber, Insane magenta, Expert violet, in both places.

*Not built: the `upload-no-backend` scene. The screen finds the real venv from
inside the checkout, so there is no honest way to stage a missing backend for a
capture without faking `BackendPaths` — and `TestSceneUploadScreen` already
asserts the real discovery works. The fault lamp and message path are the same
ones the other failure states use.*

---

# Part 4 — The running view becomes a machine

## Step 9 — `PipelineProgress` and the reel-to-reel panel ✅

**Goal:** while Python runs, the screen is a machine doing work rather than a
log in a box.

- **`Screens/Generation/PipelineProgress.cs` — pure, and tested against the
  real strings.** `main.py`'s `report()` emits a small fixed set:
  `Loading <file>`, `[i/n] Analysing and mapping <Tier>`, `Writing beatmap
  files`, `Writing analysis data`. Parse a line into
  `{Stage, TierIndex, TierCount, TierName}`. The tests assert against strings
  **copied from `src/main.py`**, and a comment in each file points at the
  other, because this is a real coupling: change a report string and the lamps
  stop lighting.
- **The panel**, a `RackPanel` carrying:
  - a **reel-to-reel pair**, tape running supply → take-up, turning while the
    process lives. A liveness indicator, which is precisely what a turning reel
    honestly is — no percentage claimed
  - a row of **stage lamps** — `LOAD · DETECT · MAP · WRITE · ANALYSE` — lit as
    the parsed stage advances, the current one breathing
  - a **tier counter** on a `SegmentReadout`: `3/5 HARD`, from the real
    `[i/n]`. This is the honest progress number, because mapping is the slow
    part and it genuinely reports which pass is running
  - an **elapsed clock**, also a `SegmentReadout`
  - the **stdout tail**, kept, as a narrow printout strip in a dim inset at a
    small size. It stays because it is the only error surface a failed run has
- **Failure state:** the stage lamps freeze where they stopped, a red FAULT
  lamp lights, the printout strip expands to full height and stays scrollable,
  and `Back` becomes a chrome transport button. A failed generation is the one
  moment a user actually needs the raw text, so it gets *more* room, not less.
- `Update`'s elapsed-seconds write stays as it is; it is already cheap.

*Verify:* screenshot scenes `generation-running` (lamps mid-sequence, driven by
fed lines, so no real backend is needed) and `generation-failed`.

*Built as `Screens/Generation/PipelineProgress.cs` (pure, 15 tests),
`Graphics/Rack/TapeReel.cs` and `Screens/Generation/GenerationProgressPanel.cs`,
wired into `DspVisualizationScreen` in place of `createRunningView`'s grey
form. Both scenes rendered and checked.*

- **Four lamps, not five — the honesty rule bit the plan's own sketch.** This
  document specified `LOAD · DETECT · MAP · WRITE · ANALYSE`, but the backend
  emits *one* line covering detection and mapping together
  (`[3/5] Analysing and mapping Hard`). Lighting two lamps off one signal
  would be the panel inventing a stage boundary it cannot observe, so the row
  is `LOAD · MAP · WRITE · ANALYSE` and a test pins the count.
- **`src/main.py` now carries a comment saying its `report()` strings are a
  contract**, pointing at `PipelineProgress.cs`; the tests assert against
  copies of those exact strings. The coupling fails silently in the worst way
  — an unrecognised line is just log output, so the lamps quietly stop
  advancing — and this is what turns that into a red build.
- **The parser is tested for what it must *not* match** as hard as for what it
  must. `main.py` prints a closing report (`Analysis data: …`, `DSP trace: …`,
  per-tier counts) after the last stage; a parser loose enough to match
  "Analysis" in the wrong place would jump the lamps at the end of every run.
- **A vertical `FillFlowContainer` cannot express "fill the rest".** A child
  with `RelativeSizeAxes.Both` takes the *whole* parent height and is then
  pushed down by its siblings, so the printout panel hung 260px off the bottom
  of the screen with all its content out of frame. Rebuilt as a `GridContainer`
  with absolute/absolute/distributed rows — which also made the failure state
  trivial, since it is just a different set of row dimensions.
- **Colour carries the lamp's meaning, not only the animation.** A breathing
  `Working` lamp and a steady `Lit` one were indistinguishable in a captured
  frame, which is how this screen gets checked. The running stage is now amber
  and passed stages mint.
- **The printout feeds from the bottom.** Top-anchored, four lines sat marooned
  at the top of a tall empty panel; bottom-anchored they stack upward out of
  the machine.
- **The failure state nearly lost its own error message.** The status line
  lived on the transport panel, which the fault state hides — leaving a red
  lamp and a traceback but no statement that the run failed or with what exit
  code. It now goes into the printout panel's legend.

---

# Part 5 — The cinematic: from four stages to nine

Same screen, same hand-off, and the same skip contract — **Escape or a click,
at any point. This is not negotiable, and it gets re-checked at the end of
every step below.** The four existing components are re-skinned rather than
replaced; each new stage is a new file, matching how every beat-reactive effect
in this project already gets its own.

Everything plays on one **analyser chassis**: the spectrogram is a tape strip
across the top for stages 1–6, the trace rack sits beneath it, and stages 7–8
slide a second panel over the top.

## Step 10 — Re-skin the four existing stages onto the chassis ✅

**Goal:** what exists today looks like it belongs to this game. No new data.

- `createRevealView`'s blue-white literals → `RetroPalette`; `SpriteText` →
  `RetroText`; the stage title and detail become engraved bezel legends on a
  `RackPanel`.
- `SpectrogramReveal`: keep the wipe and keep `TimeToX`. **Every later stage
  places itself through `TimeToX`, and that shared axis is the thing that makes
  the whole chain readable — do not let a new stage invent its own.** Add a
  chrome bezel, tick marks, and a scan line in `RetroPalette.Cyan`.
- Promote `OnsetSparkLayer`'s three band colours into `RetroPalette` as
  `BandLow` / `BandMid` / `BandHigh` and use them for the band curves in step
  11 and for hit objects. The difficulty spines stay on the difficulty ramp:
  bands and tiers are different axes and must not share a ramp.
- `BeatGridReveal`'s BPM readout → `SegmentReadout`.
- `HitObjectAssemblyPreview`'s playfield → a `RackPanel` inset.

*Verify:* scenes `reveal-stage1` … `reveal-stage4` against a real folder. This
step must look at least as good as today before anything is added on top.

## Step 11 — Stages 2a and 2b: flux, threshold, and peaks that *cross* ✅

**Goal:** onset detection stops being "markers appear" and becomes the
algorithm.

- `Screens/Generation/FluxStage.cs`, three `TraceGraph`s under the spectrogram
  on the shared axis:
  - **2a — SPECTRAL FLUX.** The flux curve draws in left to right in step with
    the spectrogram's own wipe above it, so the two read as one pass over the
    song. Then the three band curves fade in beneath as a stacked trio, tinted
    `BandLow` / `BandMid` / `BandHigh`. Legend: `half-wave rectified · L2
    across 128 mel bands`.
  - **2b — ADAPTIVE THRESHOLD.** The median curve rises out of the flux, the
    derived `δ + margin × median` threshold draws over it dashed, and the
    sparks pop **at their crossings**, in time order — `OnsetSparkLayer`
    re-timed off the threshold instead of off a blind sweep. Legend: `local
    median + margin — Bello et al. (2005) · margin 1.5 · δ 0.05`, with the
    numbers taken from the file.
- Put the citation on screen. It is already in `detector.py`'s docstring, it is
  the actual method, and someone reading it off the frame is exactly the point.
- `MaxSparks = 160` stays. That constant was measured against a real map, and
  nothing here changes the pixel budget it was measured against.

## Step 12 — Stages 3a and 3b: the tempo search, and the phase lock ✅

**Goal:** the BPM stops appearing from nowhere.

- `Screens/Generation/TempoStage.cs` slides a second `RackPanel` up over the
  trace rack. The spectrogram stays visible above it — the search is *about*
  what is up there:
  - **3a — AUTOCORRELATION.** `acf` drawn against a **BPM axis** (lag → BPM, so
    the reader sees tempo rather than lags), the log-normal `prior` overlaid
    faint, the searched band shaded, and the product's peak marked. Then three
    small `RackPanel` cards, one per octave candidate, each showing
    `phaseStrength × priorWeight = score`; the winner's lamp lights and the
    others dim. Where `rawBpm ≠ chosenBpm`, **say so on screen** — that is
    octave correction visibly earning its place in the pipeline.
  - **3b — PHASE FIT.** A pulse train slides across the onset envelope through
    one beat period while `phaseScores` fills in below it, then locks at the
    argmax. The panel drops away and `BeatGridReveal` snaps the grid onto the
    spectrogram as it does today — now arriving as the *result* of a search the
    viewer has just watched.
- `SegmentReadout`s for `rawBpm`, `chosenBpm`, `confidence`, `phaseStrength`.

*Verify:* scene `reveal-tempo` against a real folder — and specifically against
one where octave correction changed the answer, since that is the frame worth
having.

## Step 13 — Stages 4 and 5: snapping, sustain, and the five-tier funnel ✅

**Goal:** Steps 4 and 5 of the pipeline appear on screen for the first time.

- `Screens/Generation/SnapStage.cs`:
  - onsets visibly **slide onto** their nearest grid line, each by its real
    `deltaMs`, and colliding ones merge with the weaker fading out — exactly
    what `snap_onsets` does
  - three counters for the `1/1` / `1/2` / `1/4` histogram
  - then a **gap bracket** appears over a long gap, a small gauge fills to that
    gap's `sustainRatio` against its threshold line, and the gap resolves to
    `SLIDER`, `SPINNER` or `REST`. Play three or four real decisions out of the
    sustain trace, not one
- `Screens/Generation/TierFunnelStage.cs`: five lanes, each a cassette spine in
  its ramp colour, each narrowing `detected → snapped → spaced → objects` with
  the four real counts, and that tier's knobs (`margin`, `δ`, `1/N`, `min
  spacing`) printed on the spine label. The funnel narrowing at different rates
  per tier **is** what difficulty scaling means in this project.
- Then `HitObjectAssemblyPreview`, as today, for the preferred tier.

## Step 14 — Re-pace the whole sequence, against a real run ✅

**Goal:** nine stages that do not outstay their welcome.

- Budget about 30 s, up from ~18 s. Longer than Phase 7's original target, and
  deliberately so: the finale now offers a way deeper in, which makes the
  sequence an overview rather than the only telling.
- **Tune the stage durations by watching a real generated map, not by
  arithmetic.** The Phase 7 notes are unambiguous that every constant of this
  kind was wrong on first guess.
- Re-check the skip contract after every stage: `skip()` must jump *every*
  component — including the five new ones — to its finished state before
  handing off, or a skip lands mid-animation.
- The finale becomes two chrome buttons, **OPEN ANALYSER** and **PLAY**, with
  PLAY taken on a timeout so an unattended run still ends in song select
  exactly as it does today.

### Part 5 as built

*Steps 10–14 shipped together. Eleven stages, every one rendered and looked
at. 493 frontend tests, 226 backend.*

**The reveal became its own component.** `DspReveal` owns the chassis, the
stage list and the sequencing; `DspVisualizationScreen` now just creates one
when the run finishes. Not in the plan, and necessary immediately: a timed
sequence living inside a screen that only exists after a Python run cannot be
screenshotted, and *every* stage here needed looking at. It also hands step 19
its song-select entry for free.

**Stages are a list, not a chain.** The count depends on what the backend
wrote — five for a map with only an `analysis.json`, eleven with a `dsp.json`
beside it — and a chain of methods handing to each other would need a branch
at every link. `StartStage` and `HoldAtStartStage` let a capture land on the
stage being built rather than on whichever one the clock reached.

**Four traps, three already in this document's own table:**

- **A drawable that has not entered the tree silently ignores transforms.**
  `Play()` ran straight from `LoadComplete`, so the first stage's
  `FadeInFromZero` hit an unloaded heading and the title never appeared at
  all. `Schedule(Play)` fixes it — the same trap `CarouselBackground` carries
  a comment about.
- **A stage driven from outside must build in its constructor.** `FluxStage`
  built in `LoadComplete` and threw on its first null child, because the
  sequence fires before the stage finishes loading. The rule
  `SpectrogramReveal` already follows for its overlays.
- **`TransformTo(nameof(...))` got written into `FluxStage` and taken out
  again** before it could fail silently, in favour of a `BindableFloat` and
  `TransformBindableTo`.
- **A transparent overlay is not a panel.** The signals rack read straight
  through the tempo and snap panels as part of the plot. It now steps back to
  12% while a panel is over it; the spectrogram strip deliberately stays,
  since the tempo search is *about* what is on it.

**Three things only the frames caught:**

- **The band trio was completely invisible**, drawn behind a filled flux trace
  reaching the same baseline. No opacity recovers that. The bands now have
  their own labelled lanes under the global curve — which is also the more
  honest picture, since they are a decomposition rather than an overlay.
- **The octave cards covered the peaks they were scoring.** Moved into their
  own row.
- **Two stages were both titled "STEP 4 — HIT OBJECTS"**, which read as the
  sequence repeating itself. Both genuinely *are* backend step 4, so they are
  split by what they do: `SNAP AND CLASSIFY` for `classify()`'s decisions,
  `PLACEMENT` for `assign_geometry()`'s distance snap and turn limit.

**Pacing: 33.8 s, not 30.** The plan sketched nine stages; there are eleven,
because detection and tempo each split into method and result once there was
data for both. Compressing to 30 would put stages under 2.5 s, below which
they stop being readable — and eleven unreadable stages is worse than a longer
sequence. `RevealPacing.FullSequence` is a computed property with a test
holding it under 36 s, and a second test holding every stage at or above
2.5 s, so it cannot drift a few hundred milliseconds per edit. A map with no
trace plays five stages in 14.6 s, which is where this started. **The plan's
open question — whether it is too long — still cannot be answered from a
screenshot. It needs watching after a real two-minute generation, and if it
is, the answer is to move stages 3a and 5 into the analyser rather than to
speed everything up.**

*Not built: the OPEN ANALYSER / PLAY finale buttons. They lead to the
analyser, which is Part 6 and does not exist yet — a button to nowhere would
be worse than the current behaviour, which hands off to song select exactly as
before.*

---

# Part 6 — The analyser: the part a grader can poke

`Screens/Analysis/DspInspectorScreen.cs` — its own folder, because it is
reachable from song select as well as from generation.

The layout echoes song select, which is the layout this client has settled on:
a left column of panels, a large working area on the right, everything sitting
on the `CarouselBackground` treatment.

## Step 15 — Inspector skeleton: the tape strip and the playhead ✅

**Goal:** a screen you can scrub, with one trace on it.

- Top: the spectrogram, full width, with a **playhead**. Click or drag to seek,
  and the preview track seeks with it — loaded by the same route
  `DspVisualizationScreen` already uses.
- Below it: one `TraceGraph` showing flux, on the same axis.
- Scroll to zoom the time window, drag to pan, and a minimap strip underneath
  shows the whole track with the current window marked.
- Escape returns to whichever screen pushed it.
- **The audio lifecycle is the bug-prone part**, exactly as it was for
  `MenuTrack`: `OnSuspending` stops, `OnResuming` restarts, `OnExiting` stops.
  Two songs playing at once is the single most likely regression when a third
  screen with a preview track joins a stack that already has two.

## Step 16 — The trace rack ✅

**Goal:** every exported curve, switchable.

- Stacked `TraceGraph`s on the shared axis: flux with its derived threshold and
  its picked peaks; the band trio; the onset envelope with the beat grid over
  it; RMS with the sustain threshold line.
- A column of `ToggleSwitch`es down the left, one per trace, lit when thrown.
- Hovering an onset marker shows a small card: time, strength, dominant band,
  the snap it took, and its `deltaMs`.

## Step 17 — The live detector: two knobs and an honest recount ✅

**Goal:** the centrepiece. Turn a knob, watch the algorithm's answer change.

- Two `ControlKnob`s — `margin` and `delta` — with a row of five tier buttons
  that snap them to that preset's values.
- Turning either re-derives the threshold (`δ + margin × median`), re-runs
  `OnsetPeakPicker` over the decoded flux, redraws the dashed threshold and the
  peaks, and updates an onset count on a `SegmentReadout`. Step 5's test is
  what makes this a demonstration rather than a toy.
- Recompute at most once per frame over ~10,000 samples — a trivial pass — but
  rebuilding the `SmoothPath` is not free, so rebuild vertices once per settled
  value rather than once per drag event.
- A second readout compares the live count against that tier's exported
  `detected` count. Selecting a tier and watching the count land on the
  backend's own number is the moment the port proves itself on screen.

## Step 18 — Tempo and funnel panels ✅

**Goal:** stages 3 and 5 become inspectable rather than merely watchable.

- Tempo `RackPanel`: the ACF over its BPM axis with the prior overlaid, the
  three octave candidates and their scores, and the phase-score dial. Hovering
  a lag reads out its BPM and its correlation.
- Funnel `RackPanel`: the five spines from step 13 with each tier's knobs, and
  a tier click that switches which tier's objects the playfield preview shows.

## Step 19 — Entry points ✅

**Goal:** the fourteen maps already on disk become demo material.

- **From the cinematic:** the OPEN ANALYSER button from step 14.
- **From song select:** an `ANALYSE` chrome toggle on `SongInfoPanel`, enabled
  only when the selected set's folder holds an `analysis.json`. It pushes the
  inspector for that folder.
- **Degradation, stated plainly on screen.** A v1 map — `analysis.json`, no
  `dsp.json` — opens the inspector with its spectrogram, onsets, beat grid and
  objects, and with the trace rack, the knobs and the tempo panel replaced by a
  single engraved line: `no trace data — regenerate this map to record it`. It
  must not look broken, because for every map currently on disk this is the
  normal case until they are re-run.

### Part 6 as built

*Steps 15–19 shipped together as one screen, `Screens/Analysis/DspInspectorScreen`,
plus `PeakMarkers`. 498 frontend tests, 226 backend.*

- **The centrepiece works and is tested through the screen's own path.**
  `TestSceneDspInspector` sets the knobs to each tier's sensitivity and
  asserts the live count equals the `detected` figure that tier's funnel
  records — not by poking the picker directly, but through
  `SetSensitivity`, which is what the preset buttons call. A screen showing a
  stale count would look exactly as convincing as a working one, so the
  wiring is what needed the test; the picker's own agreement with the backend
  was already settled in step 5.
- **Recomputation is coalesced to one pass per frame.** A knob drag fires
  many times a frame; the pass over ~11,000 samples is cheap but rebuilding
  the threshold's `SmoothPath` is not. `SetSensitivity` bypasses the queue so
  a caller can read the count back on the next line.
- **Markers are pooled and repositioned, never rebuilt**, and thinned past
  400. Allocating a thousand drawables per knob turn is the obvious way to
  make a live control feel dead, and a marker every pixel is the same solid
  block `OnsetSparkLayer` learned about at 420 sparks. They also draw at 42%
  opacity: at full strength a few hundred full-height lines bury the curve
  they are supposed to be marking points on.
- **A drawable may not be added to two containers**, and removing it first
  does not take effect in time. The three band traces are built straight into
  their group so one switch throws all three.
- **The degradation was looked at against a real map, not a contrived
  folder.** The `inspector-v1-map` scene points at
  `data/output/The Chainsmokers - Closer` — an actual set generated before
  the trace existed. It opens with its real spectrogram, its header, and one
  line saying there is no trace to show; the detector and funnel row collapses
  to zero height rather than reserving an empty strip that would read as
  something failing to load. A folder with no analysis at all says that too.
- **Both entry points are wired.** Song select's `SongInfoPanel` gains an
  ANALYSE button, shown only when the selected set's folder actually holds an
  `analysis.json` — so the fourteen sets already on disk become reachable
  without regenerating anything. The reveal's finale offers OPEN ANALYSER
  beside PLAY, and only when its host supplied somewhere to go: a button to
  nowhere would be worse than no button. PLAY still wins on a timeout, so an
  unattended run ends in song select exactly as it did before there was a
  choice.

## Step 20 — Deck sounds 📋 *(optional, last, cuttable)*

`Audio/DeckSoundSynth.cs` and a player, following `MenuSoundSynth`'s exact
shape — synthesize once at startup, play by name: a cassette-door clunk when a
file loads, a relay click on a tier spine, a REC engage, a soft tape-stop on
skip. Nothing depends on this; it is the last 2% and the first thing to cut.

---

# Verification

Every step above lands at least one `ScreenshotHarness` scene, because a green
suite has now twice failed to notice a screen that did not render. New scenes:

`trace-graph` · `rack-kit` · `upload-empty` · `upload-loaded` ·
`upload-no-backend` · `generation-running` · `generation-failed` ·
`reveal-stage1..4` · `reveal-flux` · `reveal-tempo` · `reveal-snap` ·
`reveal-funnel` · `inspector` · `inspector-knobs` · `inspector-v1-map`

Run them, **open the PNGs, and look at them.** Several need a real generated
folder: point them at `data/output/` by the same env-var route `BeatmapLibrary`
and `WallpaperLibrary` already use, and skip cleanly when it is absent so the
harness still runs on a clean clone.

Backend tests: `.venv/Scripts/python.exe -m pytest`.
Frontend tests: `dotnet test frontend/OsuClient.Tests`.

# Where this can be cut

In the order it should be cut, if it has to be:

1. **Step 20** (sounds) — nothing depends on it.
2. **Steps 18 and 16** — the inspector still demonstrates its best trick (step
   17) with only the skeleton and the knobs.
3. **Step 13's funnel stage** — the data is in the file; the stage can arrive
   later.
4. **The whole of Part 6** — the cinematic gains every new stage regardless,
   and what is lost is step 19's song-select entry, not the DSP content.

What must not be cut: **steps 0–5.** They are the export, the model, and the
proof that the C# port matches the backend. Everything visual is a way of
drawing them, and any of it can be rebuilt later from data that exists — none
of it can be drawn at all from data that does not.

# Open questions

- ~~**Does `rawBpm` actually differ from `chosenBpm` on the songs on disk?**~~
  **Answered in step 2: yes, on 2 of 14.** `idol.mp3` (166.04 → 83.02, a 2.8%
  margin) and `gtasa.mp3` (47.55 → 95.10, raw estimate out of range). Build
  stage 3a in full, and use `idol.mp3` as its screenshot scene. Several other
  tracks are near-ties that did *not* flip, which is worth showing too.
- ~~**How much does step 1 really add to a run?**~~ **Answered: 0.11 s** on a
  4m22s track, 0.16 s on a six-minute one, against a five-tier run measured in
  minutes. No second decode and no second mel pass. (`main.py` still computes
  one extra mel spectrogram for `export_analysis` itself — pre-existing, and
  still worth passing through one day, but not on this budget.)
- **Is a 30 s cinematic too long once the inspector exists?** Only watching it
  after a real two-minute generation will answer that. If it is, the cut is
  stages 3a and 5 out of the timeline and into the inspector — not faster
  pacing, which would make nine stages unreadable rather than shorter.
