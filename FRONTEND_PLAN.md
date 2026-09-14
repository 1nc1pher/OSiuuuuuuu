# Frontend Plan — Custom osu!(lazer)-style Client

This document covers the **frontend/player** half of the project: a
custom game client that plays the `.osz` beatmaps produced by the
backend (see [BACKEND.md](BACKEND.md)). The two halves are decoupled —
the backend's only contract with the frontend is "produces a valid
`.osu`/`.osz` file" (Phase 7 adds one more small artifact to that
contract, not a new coupling — see §4) — so the frontend can be built in
whatever stack best serves the goal, independent of the Python
pipeline.

As of Phase 5, that covers *playing* a beatmap end to end. Phases 6-8
extend the scope to the other side of this project's actual point:
*producing* one from inside the client — uploading a song, watching the
signal processing that turns it into a map, and it's a DSP project
before it's a rhythm game, so making that process visible rather than a
terminal command is the centerpiece, not a nice-to-have.

## 1. Framework decision

**Goal stated by the user:** near-identical feel to osu!(lazer) —
smooth, fluid animations and game feel — with room for personalization.

**Choice: [osu.Framework](https://github.com/ppy/osu-framework) (C# / .NET)**

This isn't "a C# framework similar to osu!'s" — it **is** the actual
framework osu!(lazer) itself is built on, open-sourced by the osu! team
(`ppy`). osu!(lazer) (`github.com/ppy/osu`) is just a game built on top
of `osu.Framework`, which is a general-purpose 2D game framework
(drawable scene graph, dependency injection, easing/transform system,
input handling, audio via `ManagedBass`, veldrid-based rendering
targeting OpenGL/Vulkan/Metal/Direct3D). Building on it directly is the
only way to get *actually identical* animation curves and game feel,
not an approximation of them.

**Why not pygame:**
- Pygame is an immediate-mode 2D blitting library with no scene graph,
  no built-in easing/tween system, no compositing/blending pipeline
  beyond basic surfaces, and no GPU-accelerated transform stack. Getting
  lazer-grade fluid motion (approach circles easing, menu transitions,
  storyboard-style layering) means building an animation engine from
  scratch on top of it — significant effort for a worse ceiling than
  just using the framework that already does this.
- Pygame is a reasonable choice for a simple/prototype rhythm game, but
  not for "replicate osu!lazer's feel."

**Alternatives considered and rejected:**
| Option | Why not chosen |
|---|---|
| Godot (GDScript/C#) | Good general engine with a real tween system — a legitimate fallback if osu.Framework's learning curve proves too steep — but further from lazer's actual animation code, so replication would still be an approximation. |
| Web (PixiJS/Three.js + TS) | Great for shareability (playable in-browser, no install) and there are existing web osu clients (e.g. `o2jam`-style / osu!web projects) to reference, but rebuilding lazer's specific feel in a DOM/canvas/WebGL stack is again an approximation, and skinning/audio-latency parity is harder to get right in-browser. Worth revisiting later if a browser-playable version becomes a goal. |
| Fork osu!(lazer) itself | Maximum fidelity by construction, but it's a large, actively-developed codebase (game rules, online services, multiplayer, etc.) — heavy to fork just to reskin. Building a new, smaller game project *on top of* `osu.Framework` gets the same animation engine with a much smaller surface area to own. |

**Decision:** new C# project targeting `osu.Framework` directly (not a
fork of osu!lazer). Pull in only what's needed: drawables/transforms,
input, audio, and a custom (or reused) beatmap decoder — not lazer's
online services, multiplayer, or non-gameplay screens.

## 2. High-level architecture

```
.osz file (from backend/data/output/)
   |
   v
[Beatmap Importer]   -- unzips .osz, parses .osu (v14) into an in-memory model
   |
   v
[Song Select Screen]  <-- browses imported beatmap sets, shows difficulties
   |
   v
[Gameplay Screen]
   ├── Beatmap Playback Clock (synced to audio track position)
   ├── Hit Object Pool (circles / sliders / spinners as Drawables)
   ├── Input Handler (keyboard/mouse -> hit judgements)
   ├── Judgement/Scoring system (300/100/50/miss, combo, accuracy)
   ├── HUD (health bar, combo counter, accuracy, progress bar)
   └── Hitsounds (sample playback synced to judgements)
   |
   v
[Results Screen]  -- score, accuracy, judgement breakdown, grade
```

Screen flow uses `osu.Framework`'s `Screen`/`ScreenStack` navigation
(the same primitive lazer uses for Menu -> Song Select -> Player ->
Results).

## 3. Tech stack

- **Language / runtime:** C#, .NET 8 (LTS)
- **Framework:** `osu.Framework` (NuGet: `ppy.osu.Framework`, plus
  platform-specific desktop package `ppy.osu.Framework.Desktop`)
- **Beatmap parsing:** start with a small hand-rolled `.osu` v14 decoder
  (the format is well-documented and the backend already controls
  exactly what it emits — see `BACKEND.md`'s "What the `.osu` writer
  emits" section for the exact subset in play: `[General]`,
  `[Metadata]`, `[Difficulty]`, one uninherited `[TimingPoints]` line,
  `[HitObjects]` with circle/slider/spinner records). Only reach for
  `ppy.osu.Game.Beatmaps` (the real parser) later if edge cases in
  hand-rolled parsing become a time sink — it's a heavier dependency
  that pulls in more of lazer's data model than needed for a v1.
- **Audio:** `osu.Framework`'s built-in `ManagedBass` audio track/sample
  playback (frame-accurate, same engine lazer uses — important for
  hitsound/gameplay sync).
- **Testing:** `osu.Framework`'s visual test browser
  (`VisualTestScene`) for interactive component testing, plus xUnit/NUnit
  for pure logic (score calculation, judgement windows, replay of a
  known input sequence against a known beatmap).

## 4. Integration with the backend

The two stay decoupled at the file level:

- Backend writes to `data/output/<Artist> - <Song>.osz` (existing,
  unchanged — see `BACKEND.md`).
- Frontend gets a **"Songs" folder** it watches/imports from. Simplest
  v1: point it at the backend's `data/output/` directly (or a symlink),
  so freshly generated maps show up in song select without a manual
  copy step. No shared code, no shared process — just a shared
  directory convention.
- **Generation now happens from inside the frontend** (Phase 6): a
  screen that accepts an audio file shells out to the backend's
  existing `generate_beatmap_set()` and imports the result through the
  same `OszImporter`/`BeatmapLibrary` path a manually-copied `.osz`
  already goes through — not a new contract, just an in-app trigger for
  the one that already exists.
- **A second artifact, for the DSP visualization** (Phase 7): alongside
  the `.osz`, the backend additionally writes a small `analysis.json`
  and a `spectrogram.png` — the onsets, beat grid and per-tier hit
  objects it already computes while building the map, serialized once
  more for the frontend to animate through after the fact. See Phase 7
  for the exact shape and why a written artifact beats a live stream.

## 5. Project structure (new `frontend/` directory)

```
osu-dsp-project/
├── BACKEND.md                     # backend pipeline docs (renamed from README.md)
├── FRONTEND_PLAN.md               # this file
├── README.md                      # project overview, links to both
├── src/ data/ tests/ configs/     # existing backend, unchanged
└── frontend/
    ├── OsuClient.sln
    ├── OsuClient.Game/                     # the actual game project (osu.Framework game)
    │   ├── OsuClient.Game.csproj
    │   ├── OsuClientGame.cs                # entry Game class, root ScreenStack
    │   ├── Beatmaps/
    │   │   ├── BeatmapDecoder.cs           # .osu v14 text -> Beatmap model
    │   │   ├── OszImporter.cs              # unzip .osz, locate audio + .osu files
    │   │   ├── Beatmap.cs                  # parsed model: metadata, difficulty, timing, objects
    │   │   └── HitObjects/
    │   │       ├── HitCircleData.cs
    │   │       ├── SliderData.cs
    │   │       └── SpinnerData.cs
    │   ├── Screens/
    │   │   ├── MainMenu/
    │   │   │   └── MainMenuScreen.cs
    │   │   ├── SongSelect/
    │   │   │   ├── SongSelectScreen.cs
    │   │   │   ├── BeatmapCarousel.cs
    │   │   │   └── DifficultyIcon.cs
    │   │   ├── Gameplay/
    │   │   │   ├── PlayerScreen.cs         # owns the beatmap clock + object pool
    │   │   │   ├── DrawableHitCircle.cs
    │   │   │   ├── DrawableSlider.cs
    │   │   │   ├── DrawableSpinner.cs
    │   │   │   ├── ApproachCircle.cs
    │   │   │   ├── JudgementProcessor.cs   # hit windows -> 300/100/50/miss
    │   │   │   ├── ScoreProcessor.cs       # combo, accuracy, score value
    │   │   │   └── HUD/
    │   │   │       ├── HealthBar.cs
    │   │   │       ├── ComboCounter.cs
    │   │   │       ├── AccuracyCounter.cs
    │   │   │       └── SongProgressBar.cs
    │   │   ├── Results/
    │   │   │   └── ResultsScreen.cs
    │   │   ├── Generation/                 # Phase 6/7: upload -> generate -> visualize -> import
    │   │   │   ├── UploadScreen.cs         # Phase 6: drag/drop or file-picker + metadata form
    │   │   │   ├── DspVisualizationScreen.cs     # Phase 7: runs the generation, then plays the reveal
    │   │   │   │                                 #   (replaced Phase 6's GenerationProgressScreen outright)
    │   │   │   ├── SpectrogramReveal.cs    # Phase 7: stage 1 (wipe-in spectrogram.png)
    │   │   │   ├── OnsetSparkLayer.cs      # Phase 7: stage 2 (onset markers over the spectrogram)
    │   │   │   ├── BeatGridReveal.cs       # Phase 7: stage 3 (grid snap-in + BPM readout)
    │   │   │   └── HitObjectAssemblyPreview.cs   # Phase 7: stage 4 (objects placed on a preview playfield)
    │   │   └── Customization/              # Phase 8: colour/effect pickers, writes through SkinStore
    │   ├── Backend/
    │   │   ├── BackendPaths.cs             # Phase 6: finds the checkout and its venv interpreter
    │   │   ├── BackendRunner.cs            # Phase 6: shells out to src/main.py, parses its output
    │   │   └── AnalysisData.cs             # Phase 7: reads analysis.json (returns null for anything unusable)
    │   ├── Audio/
    │   │   └── HitSoundPlayer.cs
    │   ├── Input/
    │   │   └── GameplayKeyBindings.cs
    │   ├── Skinning/
    │   │   ├── ISkinSource.cs
    │   │   ├── DefaultSkin.cs              # personalization hook point
    │   │   ├── BeatmapSkin.cs              # Phase 8: per-set cover/video/cursor/combo/effect overrides
    │   │   ├── SkinStore.cs                # Phase 8: loads/saves the JSON sidecar, resolves effective skin
    │   │   └── BackgroundMedia.cs          # Phase 8: cover Sprite / gameplay VideoSprite, dimmed behind the HUD
    │   └── Resources/                      # default skin textures, fonts, hitsounds
    ├── OsuClient.Desktop/                  # thin executable host (Program.cs / Main)
    │   └── OsuClient.Desktop.csproj
    └── OsuClient.Tests/                    # xUnit/NUnit + osu.Framework visual tests
        ├── BeatmapDecoderTests.cs
        ├── JudgementProcessorTests.cs
        └── Visual/
            └── TestSceneDrawableHitCircle.cs
```

This mirrors how lazer's own repo is organized (`*.Game` /
`*.Desktop` / `*.Tests` split is the standard `osu.Framework` project
template), which keeps the learning curve low since docs/examples for
that layout are exactly what's available for this framework.

## 6. Roadmap

**Phase 0 — scaffold** ✅ done
- `osu.Framework` project template, empty window, confirm a basic
  `Drawable` animates (sanity check the toolchain).

**Phase 1 — beatmap pipeline** ✅ done
- `.osz` importer + `.osu` v14 decoder for the exact subset the backend
  emits. Load one of the backend's generated maps and print the parsed
  object list to the console.
- Delivered: `BeatmapDecoder`, `OszImporter`, `BeatmapLibrary` (scans a
  songs directory, tolerates broken entries), full model in
  `Beatmaps/` + `Beatmaps/HitObjects/`, `--dump` CLI verb in
  `OsuClient.Desktop/Program.cs`, plus decoder/importer/library test
  coverage in `OsuClient.Tests`.

**Phase 2 — core gameplay loop (MVP)** ✅ done
- Menu → Song Select → Player navigation wired with `ScreenStack`;
  `MainMenuScreen`, `SongSelectScreen` + `BeatmapCarousel` +
  `DifficultyIcon` (browses real imported sets, shows per-difficulty
  AR/CS/OD/HP/object counts).
- Real `PlayerScreen` (replaces the old `PlayerScreenPlaceholder`):
  an audio-lead-in gameplay clock (`Track` loaded via
  `AudioManager.GetTrackStore` against a `Storage` rooted at the
  beatmap's own folder, so no copying into the app's resource store is
  needed), hit circles with approach circles that animate via
  `BeginAbsoluteSequence` against that clock, osu!-standard hit
  windows/preempt/fade-in maths (`JudgementProcessor`), combo/accuracy/
  score bookkeeping (`ScoreProcessor`, `HUD/ComboCounter`), a floating
  300/100/50/Miss judgement popup, and a "Cleared!" summary once every
  object is judged.
- Input: click (any mouse button) or the classic Z/X keys judge
  whichever unjudged object is earliest, only if the cursor is over it
  — real osu!-style aim-and-time hitting, not blind key timing, with
  osu!'s note lock (you can't reach past an unhit object to hit a later
  one).
- Scoring is a simplified combo-scaled formula, not osu!'s exact
  ranking-score algorithm — good enough to make combo/accuracy feel
  right for MVP testing; revisit only if exact score parity ever
  matters.

**Phase 2 follow-up — two bugs that made the loop unplayable**

Found once input was driven through the framework's real input pipeline
(`ManualInputManager`) instead of by calling judgement methods directly.
Both are the sort of thing that reads as "input doesn't work at all":

- *The gameplay clock ignored the audio.* Time was derived from wall
  clock alone, so what you heard drifted from what was judged — you'd
  hit in time with the music and the game had already expired the note.
  Now everything in the playfield runs on `GameplayClock`, which
  free-runs only through the lead-in and then follows the track, easing
  out small drift and hard-seeking large drift so the audio stays the
  authority without visible stutter.
- *An early press instantly missed the note.* `Judge()` returns `Miss`
  for anything outside the windows, so a slightly-early click destroyed
  the circle being aimed at; the properly-timed click that followed then
  found nothing under the cursor and silently did nothing. Presses
  outside the hit window are now ignored, as in osu!.

**Phase 3 — full hit object support** ✅ done
- `SliderPath` turns control points into a sampled curve: linear,
  bezier (including the repeated-anchor convention for hard corners),
  perfect circle (with a collinear fallback) and catmull, then trimmed
  or extended to the declared pixel length — that length is what the
  slider's duration is derived from, so the drawn body and the timing
  agree.
- `DrawableSlider`: body drawn with `SmoothPath` (border + fill), head
  judged like a hit circle, then a ball running the path (back and
  forth across repeats) that has to be followed with a held key inside
  the follow circle to collect ticks, repeats and the tail. The overall
  300/100/50 comes from how many parts were collected, as in osu!;
  parts carry combo but not accuracy.
- `DrawableSpinner`: cursor rotation accumulated around the centre (no
  key held, as in osu!), required spins from OD via osu!'s 90/150/225
  SPM difficulty range, judged on completion ratio.
- All three types share `DrawableHitObject`, which `PlayerScreen` drives
  uniformly — one per-frame state update, one press-routing rule, one
  removal rule.
- Verified against every real backend map in `data/output/`: 9302
  circles, 1281 sliders, 43 spinners across 30 difficulties build paths
  with no NaNs and no length mismatches, and a real 1044-object Expert
  map plays with its real audio track without throwing.

**Phase 3 polish — snaking, repeat arrows, hitsounds** ✅ done
- Slider bodies snake in: `SliderPath.Segment(progress)` returns the
  leading portion of the curve as its own polyline, and `DrawableSlider`
  redraws the body from that every frame from 0 (a stub at the head) to
  1 (the full curve) over the object's fade-in duration, matching when
  it becomes fully visible.
- Repeat arrows: a `Triangle` at each actual repeat point (not the final
  tail), rotated via `SliderPath.DirectionAt` to point the way the ball
  is about to travel next, with a gentle continuous pulse. Reuses the
  same fade-out-on-consumption path as tick markers.
- Hitsounds: `HitSoundSynth` procedurally generates two short PCM WAVs
  at startup — a pitch-swept "kick" thump for hits, a quiet high click
  for ticks — rather than bundling any actual osu! sample, which isn't
  something this repo has the rights to redistribute.
  `HitSoundPlayer` loads them into osu.Framework's sample system via an
  in-memory `IResourceStore<byte[]>` and plays a fresh channel per
  judgement/tick so overlapping hits don't cut each other off.
- Verified against a real 482-object map with 52 repeating sliders:
  plays several seconds with none of the three features throwing.

Known gaps vs lazer, deliberately left: the approximations noted above
for scoring and slider-tick spacing under variable slider velocity, and
no audio-offset setting yet (worth adding first if hits feel
systematically early/late against lazer).

**Testing the gameplay loop**

Timing-dependent gameplay is easy to "verify" without verifying
anything, so the suite is layered:

- Pure logic, no game host: hit windows/preempt/fade-in
  (`JudgementProcessorTests`), combo/accuracy/score
  (`ScoreProcessorTests`), audio-follow behaviour (`GameplayClockTests`),
  curve geometry (`SliderPathTests`).
- Deterministic drawable tests: the playfield is parked on a
  `ManualClock` and objects are stepped through gameplay time by hand,
  so results don't depend on how fast the test host renders
  (`TestSceneDrawableHitCircle`, `TestSceneSliderAndSpinner`).
- Real input through the framework's pipeline via `ManualInputManager`
  (`TestSceneGameplayInput`). Presses must be issued in the *same frame*
  the hit window opens — a following `AddStep` runs a frame or more
  later, which at these windows is long enough for the note to expire.
  This is the layer that caught both bugs above; tests that call
  judgement methods directly cannot.
- End to end: `TestScenePlayerScreen` runs the real screen over a map
  with a circle, a slider and a spinner and checks each gets judged.

**Phase 4 — polish pass** ✅ done

Most of this list was already standing by the end of Phase 3 — song
select and its carousel (Phase 2), hitsounds (Phase 3 polish),
accuracy/score display, AR-correct preempt timing and combo-colour
cycling on the new-combo flag (Phase 2). What Phase 4 actually added:

- **Health / HP drain** (`HealthProcessor`, `HUD/HealthBar`): health
  drains continuously across the map's playable span, is refilled by
  hits and cut by misses, and empties into a failed run that stops
  gameplay where it stands. osu!stable derives its drain rate by
  simulating the beatmap and binary-searching a rate a perfect play
  barely survives; that's disproportionate here, so the rates scale off
  HP drain rate directly. Tuned against the real generated maps — a
  perfect play never dips below full on any of them, an 80%-accuracy run
  survives comfortably, and touching nothing fails in ~2-4s on Hard and
  Insane, ~14s on Easy.
- **Results screen** (`Screens/Results/ResultsScreen.cs`): grade, score,
  accuracy, max combo and the judgement breakdown, or a FAILED banner.
  Grades follow osu!standard's rules — SS for all-300s, then tiers on
  the share of 300s, with a clean run promoted a tier over one with
  misses. It takes a snapshot of the numbers at the moment the play ends
  rather than holding the live `ScoreProcessor`, so nothing can edit the
  result afterwards.
- **Song progress bar** (`HUD/SongProgressBar`): measured across the
  first-to-last hit object rather than the audio file's length, so it
  fills exactly as the objects run out.

Two bugs worth recording, both caught by tests rather than by playing:
- Gameplay kept spawning and judging objects after a fail, editing the
  score behind the failed banner. A failed run now stops where it ended.
- Dismissing the results screen popped back *into* the finished map
  instead of out to song select, and exiting from inside the resume
  callback re-entered the screen stack mid-unwind — the exit is now
  deferred a frame.

**Phase 5 — personalization** ✅ done
- Custom cursor (`Graphics/Cursor/GameCursor.cs`): a glowing blue orb —
  soft edge-effect halo, a gradient body, a hot white core, an idle
  pulse, a scale-down on click — replacing the OS pointer everywhere.
- Hit circles restyled to match lazer's actual look
  (`Screens/Gameplay/HitCircleBody.cs`, shared by circles and slider
  heads): a thick white ring, a darker combo-colour disc inside it, and
  a lighter disc inside that with a top-to-bottom gradient, in place of
  the original flat single-colour fill.
- Retro fonts (`Graphics/RetroText.cs`): Press Start 2P (blocky 8-bit
  arcade) for big punchy numbers — combo, judgements, grade — and
  Audiowide (sharp geometric sci-fi) for titles/labels, applied through
  gameplay's HUD and the results screen. osu.Framework's own font
  system (`Game.AddFont`) turned out to expect a font pre-baked into
  its bitmap-atlas format — individual glyph images plus a parameters
  file — not a plain downloaded `.ttf`; feeding it one fails deep in
  its glyph cache with a null texture stream. `RetroText` rasterizes
  the actual font files directly via SixLabors.Fonts (SixLabors.ImageSharp
  is already an osu.Framework dependency) into a texture instead, sized
  off font metrics rather than each string's tight ink bounds so it
  doesn't jitter vertically as digit counts change. Menu and song select
  still use the default font — this covers gameplay and results, which
  is what "game screen fonts" most literally means; extending it further
  is a small follow-up if wanted.
- Both fonts are Google Fonts, OFL-licensed; the `.ttf`s and their
  `OFL.txt`s live in `Resources/Fonts/` and are embedded resources, not
  external downloads at runtime.

**Phase 5 continued — pause menu, key overlay, beat-synced effects, spinner rework**

Closes out the gameplay-feel work: the loop now feels solid enough that
the remaining friction is "there's no way to make a beatmap without a
terminal," not the game itself — which is exactly the cue to move on to
Phases 6-8 below.

- **Pause menu** (`Screens/Gameplay/PauseOverlay.cs`): Escape now pauses
  instead of ending the run — stops the gameplay clock (which freezes
  every judgement/animation reading off it, since Phase 2's follow-up
  bugs already made everything in the playfield depend on that one
  clock) and the track, with Continue/Quit buttons styled like the
  results screen (blurred background, retro fonts). A finished run
  still exits outright on Escape — there's nothing left to pause.
- **Key overlay** (`Screens/Gameplay/HUD/KeyTimingBar.cs`): one vertical
  bar per hit key, both a live key-press indicator and a plot of every
  hit's timing error. The bar's colour bands *are* the map's real
  judgement windows (blue/green/yellow out to the Great/Ok/Meh
  boundaries), so where a hit lands on the bar matches the "300"/"100"
  that popped off the circle, rather than being decorative.
- **Beat-synced ambient effects** (`HUD/BeatBorderFlash.cs`,
  `HUD/BeatPulseBackground.cs`): a soft gradient wash from each edge of
  the window, and a heartbeat-style zoom pulse on the background, both
  peaking exactly on the beat and easing to nothing in between. Both
  share one pure, dependency-free helper (`Screens/Gameplay/BeatPulse.cs`
  — phase-in-beat, a raised-cosine pulse, a playable-span fade envelope)
  tested the same direct way as `JudgementProcessor`, and both already
  carry a public `Enabled` toggle — Phase 8 just wires a skin value into
  a property that already exists, no new plumbing.
- **Spinner redesign** (`DrawableSpinner.cs`): rebuilt to lazer's
  argon-style look — a rotating ring of tick marks, two glowing meters
  either side of the disc that fill with progress, a colour glow
  growing out of the centre — replacing the original flat scaling disc.
  Spinning on after the bar fills now pays out a bonus per extra full
  rotation (`ScoreProcessor.ApplyBonus()`, its own "+100" popup) —
  reward for the showboating osu! itself rewards, where there'd
  otherwise been none.
- **Approach-circle timing fix**, across all three object types: the
  closing ring was easing (`Easing.OutQuint`) instead of moving at a
  constant rate, so it read as shut well before the object's actual hit
  window — an eased curve spends nearly all its travel in the first
  half, so "distance from the circle" stopped meaning "time remaining"
  partway through. Now linear, matching osu!.
- **Slider tick density fix** (`DrawableSlider.cs`): the backend always
  writes `SliderTickRate 1` (`difficulty.py`), which under the real osu!
  tick-distance formula left the shortest sliders on Insane/Expert with
  *zero* ticks between head and tail — nothing rewarded actually
  tracking one over just clicking its head. Ticks now space at triple
  the map's authored rate. A frontend rendering/scoring call, not a
  claim that the backend's own rate is wrong for what it's for.

The running lesson of this pass, worth keeping in mind for Phases 6-8:
every timing/scoring change here was verified by deliberately breaking
it and confirming its test failed before fixing it, and every visual
change was checked against an actual rendered frame — several (the
spinner's glow, the side flash, the approach circle) looked wrong or
were flatly invisible on the first pass despite a fully green test
suite. A test proves the logic; only a rendered pixel proves the logic
was the right thing to test.

A cursor debugging note worth keeping: the glowing orb initially
rendered nothing at all — not clipped, not mispositioned, just absent,
while `ActiveCursor.Alpha` and `.Position` both read back correct
values under direct inspection. Also not a hidden-container issue —
`CursorContainer` (via `VisibilityContainer`) already has working
`PopIn`/`PopOut` defaults; overriding them, even to replicate the exact
same fade, made no difference either way. Screenshotting a real running
window at each step of simplifying the cursor traced it to one
specific, surprising line: `Anchor = Anchor.Centre` on the drawable
`CreateCursor()` returns. `Origin = Anchor.Centre` alone — which is all
that's actually needed to put the mouse point at the middle of the
orb — works fine; adding `Anchor.Centre` on top of it, a completely
ordinary combination anywhere else in the UI, leaves
`CursorContainer`'s own positioning logic pointed somewhere degenerate.
Property inspection couldn't see this at all; only an actual rendered
frame could.

**Phase 6 — in-app beatmap generation** ✅ done

Replaces "copy a file into `data/raw/`, run a terminal command, come
back later" with an upload screen inside the client itself — the
natural next step now that the gameplay loop (Phases 0-5) is solid
enough that manual generation is the remaining friction, not the game
feel.

- **Upload surface** (`Screens/Generation/UploadScreen.cs`, reached from
  the main menu or song select): accepts an audio file two ways, both
  already free from `osu.Framework` with no new dependency —
  `IWindow.DragDrop` (drop a file onto the window, the same gesture
  lazer itself uses to import a beatmap) and
  `GameHost.CreateSystemFileSelector(new[] { ".mp3", ".wav", ".ogg", ".flac" })`
  (a native OS file-picker) behind a "Browse" button for anyone who'd
  rather not drag. A small metadata form (artist/title, defaulted from
  the filename the way `generate_beatmap_set()` already defaults them)
  and a difficulty-tier multi-select (all five by default, matching the
  CLI's own default) sit above a single "Generate" button.
- **`Backend/BackendRunner.cs`** — wraps `System.Diagnostics.Process`
  around the backend's own interpreter. Locates the checkout by walking
  up from the running executable looking for `BACKEND.md` (only present
  at the repo root), then uses `.venv/Scripts/python.exe` (Windows) or
  `.venv/bin/python` (Mac/Linux) inside it — falling back to a path
  saved in a small settings file if that search comes up empty.
  Invokes `src/main.py <path> --artist ... --title ... --difficulties ...`
  exactly as the manual workflow always has — `generate_beatmap_set()`
  is a plain function underneath, `main()` is just its argument parsing
  — captures stdout/stderr for the progress screen, and treats a
  non-zero exit code as a real, surfaced error rather than a silently
  empty result.
- **Progress screen** — while the process runs, a plain "Generating…"
  state showing the tail of stdout (which tier is building, object
  counts as they print) is enough for this phase; Phase 7 replaces it
  outright.
- **Import on completion** — a successful run writes into the same
  `data/output/` the "Songs" folder already watches (§4), so finishing
  is just `BeatmapLibrary` rescanning and the screen handing off into
  song select with the fresh set already selected.

**Not in this phase:** the DSP visualization (Phase 7) and any
generation-time customization (Phase 8) — this phase's bar is "upload a
song, get a playable map, no terminal required," the same
ship-something-playable-per-phase discipline every earlier phase used.

*Built as planned above, with two things the plan didn't anticipate:*

- **The backend printed nothing until it was finished.** `main.py` only
  wrote its summary once `generate_beatmap_set()` had returned, so the
  progress screen's whole reason for existing — showing what the
  pipeline is doing — would have been an empty box for the entire run,
  then a burst of text a moment before the screen changed. Analysis
  re-runs per tier (each detects onsets at its own sensitivity), so a
  five-difficulty set is five real passes over the audio: long enough
  that silence reads as a hang. `generate_beatmap_set()` now takes an
  optional `on_progress` callback and reports each stage as it starts;
  the CLI passes a printer (so the terminal workflow gained the same
  progress), and the frontend reads those lines off the pipe. The
  library function itself still prints nothing. A frontend-side elapsed
  timer covers the quiet stretches between lines.
- **A crash on leaving the screen.** osu.Framework can dispose a
  drawable more than once, and `CancellationTokenSource.Cancel()` throws
  after the source is disposed — so exiting a generation took the whole
  game down with an unhandled `ObjectDisposedException`. Both the exit
  path and the dispose path now go through one guarded cancel. Every
  test was green when this was happening: it only appeared when the
  process was actually run and actually closed.

**Phase 7 — DSP visualization experience** ✅ done

The actual point of this project: the signal processing is the
interesting part, and right now it's completely invisible — a terminal
prints a few counts and an `.osz` appears. This phase turns Phase 6's
plain progress screen into a step-by-step reveal of what the pipeline
actually did, in the same order `BACKEND.md` lists its steps:
spectrogram → onset detection → beat tracking → hit-object assignment.

**Decision: an animated replay of written artifacts, not a live stream
of the running Python process.**

| Option | Why not chosen |
|---|---|
| Live IPC (a socket, or parsed stdout progress lines) streaming the visualization frame-by-frame in lockstep with the actual computation | Real streaming coupling between two independently-evolving codebases in different languages — exactly what §4 has avoided since Phase 0. Compute time also swings wildly with song length and hardware, so pacing the spectacle to real compute time means a short clip reveals the spectrogram in 200ms (nothing to see) and a long song crawls for 30+ seconds (boring either way) — neither is the experience being asked for. |
| Reimplement STFT / onset detection / beat tracking in C# so the frontend can compute (and animate) it itself | Throws away the entire numpy/scipy/librosa foundation that *is* this project's actual DSP work, to duplicate it badly in a second language purely for a rendering convenience. |
| **(chosen)** The backend writes a small `analysis.json` + a `spectrogram.png` once generation finishes — the same moment it already writes the `.osz` — and the frontend plays a fixed, hand-tuned animated sequence through that data, independent of how long the real computation took | Keeps the file-only decoupling this project has used from the start (§4); the animation can be paced for *watching* rather than for matching wall-clock compute time; and it needs nothing new from the backend beyond one more serialization step next to the `.osu` writer it already has. |

**What the backend adds** (small and additive — doesn't touch the
existing `.osu`/`.osz` contract): a new export step alongside
`export/osu_writer.py`, called once at the end of
`generate_beatmap_set()`, writing into the same output folder:

- `spectrogram.png` — a clean render of the mel spectrogram
  `audio/visualize.py` already computes (`compute_mel_spectrogram()`),
  re-exported without the plot chrome (axes/title/colorbar) its
  existing debug figure carries, since this copy is a game asset, not a
  developer readout.
- `analysis.json` — the data every stage below needs, all of it already
  sitting in memory during a normal run and simply not currently
  written anywhere:
  - `onsets`: `{time, strength, band}` per detected `Onset` (Step 2).
  - `beatGrid`: `{bpm, offset, confidence, beatTimes[]}`, straight off
    the `BeatGrid` Step 3 already returns.
  - `hitObjects`: per difficulty tier, `{kind, time, x, y, strength,
    band, snap}` for every placed `HitObject` (Step 4) — the same
    objects the `.osu` writer already serializes, plus the `strength`/
    `source_band`/`snap` provenance fields the `.osu` format has no room
    for, which is exactly the "why did this become a slider" detail
    worth putting on screen.

**The sequence itself** (`Screens/Generation/DspVisualizationScreen.cs`,
one component per stage — every other beat-reactive effect in this
project already gets its own file, no reason this should be different):

1. **Spectrogram reveal** (`SpectrogramReveal.cs`) — `spectrogram.png`
   wipes in left to right (a moving mask, the same reveal-by-progress
   trick `SliderPath.Segment()` already uses for a slider's snake-in)
   while a short audio preview plays underneath, so the reveal reads as
   "this is what the computer is looking at," not a static image fading
   in.
2. **Onset detection** (`OnsetSparkLayer.cs`) — bright markers spark in
   over the now-visible spectrogram at each onset's time position,
   colour/intensity keyed off `strength` — the same glow-and-fade
   language `KeyTimingBar`'s hit markers already use, applied to "the
   computer finding the hits" instead of a player's presses.
3. **Beat tracking** (`BeatGridReveal.cs`) — a grid of vertical lines
   snaps across the spectrogram at the detected beat spacing, with a
   BPM readout (`RetroText`, matching gameplay's own numbers) ticking up
   and settling on the final value rather than just appearing — sells
   "this took a real estimate," not a lookup.
4. **Hit-object assignment** (`HitObjectAssemblyPreview.cs`) — the view
   transitions from the spectrogram into a plain, non-interactive
   playfield preview, and circles/sliders/spinners pop into place in
   time order at their real assigned positions, ending on a "Map ready"
   beat that hands off into song select with the new set already
   highlighted.

**Pacing and repeats.** The whole sequence targets roughly 15-25
seconds — long enough to actually watch each stage, short enough not to
become the thing standing between a player and playing. A visible
"skip" (Escape, or a click) is not optional: nobody should be forced to
re-watch this on their tenth upload of the session, only their first.

**Open question worth settling before building rather than during:**
whether `analysis.json`'s onset/hit-object lists (thousands of entries
for a long song across five tiers) need trimming or paging for very
long tracks, or whether song lengths this project has actually been
tested against already make that a non-issue — needs a real file-size
check against a real long song before assuming either way.

*Built as planned above. The open question is settled, and four things
the plan didn't anticipate:*

- **The file-size question is a non-issue.** Measured rather than
  assumed, which is what the plan asked for: a real 3m39s track at all
  five tiers (4,300 objects, 1,122 onsets) writes a 324 KB
  `analysis.json` and a 484 KB `spectrogram.png`; a synthetic six-minute
  track comes in at 540 KB. Rounding times to the millisecond and
  positions to a tenth of an osu!pixel roughly halves it for digits no
  animation could use. No paging, no trimming — the frontend reads the
  whole file in one go, and `tests/test_analysis_export.py` keeps a
  size ceiling on it so that stays true.
- **Step 2's onsets had to be kept, not recomputed.** `build_map()`
  consumed the detected onsets and returned only the placed objects, so
  the export had no onsets to write without running a second full
  detection pass over the audio. `build_map_detailed()` returns
  `(onsets, objects, grid)` and `build_map()` now delegates to it, so
  the export is handed the pass the run already did. Detection
  sensitivity is per tier, so there is no single "the" onset list: the
  export takes the tier that found the most and records which one that
  was, in `onsetSource`.
- **Three bugs that every green test missed, all only visible in a
  rendered frame.** The spectrogram drew as a plain white block, because
  the image was wrapped in `using` while `TextureUpload` takes ownership
  and consumes it later on the draw thread. The overlay layers never
  appeared, because `SpectrogramReveal` assigned `InternalChildren` in
  its dependency loader, replacing the sparks and beat grid that had
  been added to it a moment earlier. And the finished map arrived in
  song select *selected but not scrolled to*, because scrolling to a
  panel added in the same frame silently lands back at the top. Tests
  were green through all three.
- **Every density constant was wrong on first guess.** Drawn straight
  from the data, 420 onset sparks across a screen-width panel is one
  solid salmon block, and 260 beat lines is a fine comb over the
  spectrogram — both of them showing *less* than a fraction of the count
  does. 160 sparks and 80 beat lines (about one line every 20 pixels)
  read as individual detections and as a grid. The spectrogram itself
  needed the same treatment: max-pooling the columns lifts the floor
  and washes a real song out to near-uniform pink, so the export pins
  the colour scale to a fixed −45..0 dB window instead of letting each
  image auto-scale — which also means brightness means the same thing
  from one map to the next. None of this is guessable from the data
  shape; it came from looking at real generated maps on screen.

**Phase 8 — per-beatmap customization (backgrounds, skinning & effects)** 📋 planned

Lets a user attach a photo and/or a video to a beatmap: the photo acts
as its cover (song select thumbnail, gameplay fallback background), the
video plays as the actual gameplay background when one is set. Cursor
colour and hit object (combo) colours become configurable alongside it,
through the same mechanism — and so does every beat-reactive effect
Phase 5 added since this phase was first sketched: the side flash's
colour and brightness, the background's heartbeat-pulse intensity (or
turning it off outright), and the gameplay background's dim/blur
strength. None of that is new plumbing — `BeatBorderFlash` and
`BeatPulseBackground` already expose an `Enabled` toggle and their tuned
constants for exactly this reason; this phase's job is reading those
values from a skin instead of a `private const`.

*Where customization data lives.* The backend and frontend are
decoupled at the file level (§4) on purpose — the backend has no notion
of images or video and never will, and re-running it on a song
overwrites `data/output/<Artist> - <Song>/` wholesale. Two ways to hang
per-beatmap customization off that:

| Option | Why not chosen |
|---|---|
| Write a real `[Events]` background/video line into the `.osu` file itself, osu!-format-style (`0,0,"bg.jpg"` / `Video,0,"bg.mp4"`) | Matches real osu! exactly, but means the frontend has to rewrite files the backend owns — the next `python src/main.py` run on that song silently deletes the customization, and `BeatmapDecoder` has to grow write support it has never needed. |
| **(chosen) A sidecar record the frontend owns entirely**, keyed by beatmap set, untouched by the backend | Survives regeneration, keeps the decoupling real, and the frontend already has a natural place to keep this: `BeatmapLibrary` already scans `data/output/` and builds `BeatmapLibraryEntry` objects per set — a `Skinning/BeatmapSkin.cs` record and a `Skinning/SkinStore.cs` that loads/saves one JSON file per set (keyed by set folder name) slots in beside it without touching decoder or backend at all. |

Sits on the `Skinning/` hook point the project structure (§5) has
carried since Phase 0 and nothing has used yet:

- `Skinning/BeatmapSkin.cs` — the per-set override record: optional
  cover image path, optional gameplay video path, optional cursor
  colour, optional combo colour palette, optional side-flash colour,
  optional side-flash/background-pulse enabled flags and intensities,
  optional background dim/blur strength. Every field optional and
  missing = inherit from `DefaultSkin`, so a set with no customization
  behaves exactly as today.
- `Skinning/SkinStore.cs` — loads/saves the JSON sidecar (one file per
  set, e.g. `data/output/<set>/skin.json`, or a separate
  frontend-owned folder if writing into the backend's output directory
  turns out to be the wrong call once this is actually built) and
  resolves the effective skin for a `BeatmapSelection`: per-set
  override falling back to `ISkinSource`/`DefaultSkin` field by field,
  not all-or-nothing — a set that only sets a cursor colour still gets
  the default cover/background.
- `Skinning/BackgroundMedia.cs` — owns "what's behind the playfield
  right now": a `Sprite` for the cover image in song select and as the
  gameplay fallback, a `VideoSprite` (osu.Framework has real video
  decode/playback built on FFmpeg already — `FFmpeg.AutoGen` is already
  in the dependency tree via `ppy.osu.Framework`, nothing new to pull
  in) for gameplay when a video is set, dimmed behind the HUD the way
  osu! dims backgrounds during play so hit objects stay readable over
  busy footage.
- Cursor colour flows into `GameCursor`'s `PulsingOrb` (currently
  `CoreColour`/`GlowColour` constants — become skin-provided values).
- Combo colour flows into `PlayerScreen`'s `combo_colours` array
  (currently a fixed four-colour palette — becomes skin-provided, still
  falling back to the current palette when unset).
- Side-flash colour/brightness and the background-pulse toggle/intensity
  flow into `BeatBorderFlash`/`BeatPulseBackground`'s constructors —
  both already take their tuned values as constructor state, not global
  statics, so a skin-provided value just replaces the literal passed in
  from `PlayerScreen`; `Enabled` is already a public setter on both.
- Background dim/blur strength flows into the dim `Box` alpha and
  `BufferedContainer.BlurSigma` values `PlayerScreen`, `ResultsScreen`
  and `PauseOverlay` each currently hardcode — worth a single shared
  place to read the value from once three separate screens want it,
  rather than three independent skin lookups.
- A customization screen (`Screens/Customization/`, reached from song
  select) is the actual surface a user touches: file pickers for the
  photo/video, colour pickers for cursor/combo/flash, sliders for
  pulse/dim/blur intensity, writing through `SkinStore`. Live preview
  matters more here than for the media fields — a colour or brightness
  choice is hard to judge from a swatch alone — so this screen likely
  wants a small looping gameplay-style preview (a few real hit objects
  animating against the chosen background, with the flash/pulse
  actually running) rather than a static settings form. Not designed
  beyond that yet; the data model and rendering above are the parts
  worth planning now.

Open questions worth settling before building rather than during:

- **Per set or per difficulty?** Real osu! shares one background across
  every difficulty in a set (they share audio too); leaning the same
  way here — `BeatmapSkin` keyed by set, not by individual `.osu` file.
- **Copy uploaded media in, or reference it in place?** Copying into
  the set's folder (or a frontend-owned media cache) survives the user
  moving or deleting the original; referencing in place is lighter on
  disk but silently breaks the background if that file moves. Leaning
  copy — consistent with how the backend already treats
  `data/output/` as the durable home for everything a beatmap needs.
- **Video decode cost during play.** Every phase of the actual gameplay
  loop so far has been built and tested around keeping the audio-driven
  `GameplayClock` exact (see Phase 2's follow-up bugs) — decoding a
  video every frame behind it is real per-frame work that needs
  confirming it doesn't disturb frame pacing or judgement timing before
  this ships, not after.
- **Format/size limits.** Whatever containers/codecs FFmpeg handles
  should work without extra code, but nothing here has exercised that
  path yet; needs a real test video, not an assumption. A duration/file
  size ceiling is worth setting so one giant video can't blow up load
  times or disk usage.

Each phase should be playable end-to-end on a real backend-generated
`.osz` before moving to the next — the same "sonify/visualize and
listen" validation philosophy `BACKEND.md` uses for the DSP steps
applies here too: play-test on a real song at the end of every phase,
don't just trust that the code compiles.

## 7. Open questions

- **Distribution target:** desktop-only (Windows/Mac/Linux via
  `osu.Framework`'s desktop backend) is the v1 assumption. Mobile is
  possible later (`osu.Framework` supports it) but out of scope.
- **Scoring model:** replicate osu!(stable/lazer) scoring exactly, or
  define a simpler custom scoring scheme? Deferred to Phase 2/3.
- **Skin format:** reuse osu!'s `.osk` skin format for compatibility
  with existing community skins, or a simpler custom format? Phase 8
  answers the per-beatmap-background half of this with a plain JSON
  sidecar (see Phase 8's own alternatives table for why); a full `.osk`
  question would only resurface if importing whole community skin packs
  becomes a goal, which isn't currently planned.
- **Backend distribution.** Phase 6 assumes a dev checkout — the
  frontend finds the backend's own `.venv` beside it and shells into
  that. Fine on this machine, but shipping the client to someone who
  never set up the Python side needs either bundling a frozen
  interpreter (e.g. `PyInstaller`-packaged `main.py`, so nothing beyond
  the game installs) or documenting a one-time Python/pip setup step.
  Deferred until Phase 6 is actually built and someone other than the
  dev machine needs to run generation.
