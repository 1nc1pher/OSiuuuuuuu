# Frontend Plan — Custom osu!(lazer)-style Client

This document covers the **frontend/player** half of the project: a
custom game client that plays the `.osz` beatmaps produced by the
backend (see [BACKEND.md](BACKEND.md)). The two halves are decoupled —
the backend's only contract with the frontend is "produces a valid
`.osu`/`.osz` file" — so the frontend can be built in whatever stack
best serves the goal, independent of the Python pipeline.

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
- Later, if wanted: a "generate from audio" button *inside* the
  frontend that shells out to `python src/main.py <file>` and imports
  the result — a convenience wrapper, not a dependency. Not in scope
  for v1.

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
    │   │   └── Results/
    │   │       └── ResultsScreen.cs
    │   ├── Audio/
    │   │   └── HitSoundPlayer.cs
    │   ├── Input/
    │   │   └── GameplayKeyBindings.cs
    │   ├── Skinning/
    │   │   ├── ISkinSource.cs
    │   │   └── DefaultSkin.cs              # personalization hook point
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

**Phase 0 — scaffold**
- `osu.Framework` project template, empty window, confirm a basic
  `Drawable` animates (sanity check the toolchain).

**Phase 1 — beatmap pipeline**
- `.osz` importer + `.osu` v14 decoder for the exact subset the backend
  emits. Load one of the backend's generated maps and print the parsed
  object list to the console.

**Phase 2 — core gameplay loop (MVP)**
- Beatmap clock synced to audio.
- Render hit circles with approach circles; keyboard input; hit
  judgement against timing windows; basic combo counter.
- No sliders/spinners yet — circles-only maps first (this is enough to
  play-test rhythm/timing fidelity against the backend's onset output).

**Phase 3 — full hit object support**
- Sliders (path rendering + slide/repeat judgement) and spinners (spin
  input + completion judgement), matching `BACKEND.md`'s object model.

**Phase 4 — polish pass**
- Song select screen + carousel, results screen, hitsounds, health bar,
  accuracy/score display, approach-rate/AR-correct preempt timing,
  combo-color cycling on new-combo flag.

**Phase 5 — personalization**
- Skinning hook points (`Skinning/`), custom color schemes, custom
  hitsounds, UI layout tweaks — the actual "identical but mine" payoff.

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
  with existing community skins, or a simpler custom format? Deferred
  to Phase 5.
