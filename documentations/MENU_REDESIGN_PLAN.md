# Main menu redesign — the OTO logo, live spectrum, and the expanding strip

Status key: 📋 planned · 🚧 in progress · ✅ done. Update a step's header when
you start/finish it and append an "as built" note, the same way
`FRONTEND_PLAN.md` and `CAROUSEL_REDESIGN_PLAN.md` do — this doc is meant to
survive contact with the real implementation.

## 0. What this replaces, and why

`Screens/MainMenu/MainMenuScreen.cs` is still the Phase 0 placeholder: a
`FillFlowContainer` of three `BasicButton`s under `AnimatedLogo` (a rotating
blue `Box` that exists only to prove the transform system runs). It is now the
only screen in the client that hasn't been themed — song select is the vinyl
deck, gameplay has the neon HUD, and the menu in front of both is a grey
prototype.

The new menu is:

- a large white-outlined circle with **OTO** in a blocky pixel face, filled
  with a slowly cycling colour spectrum, pulsing on the beat;
- a random song from the library playing behind it, credited top-right;
- a random wallpaper from `data/default wallpapers` behind that;
- a ring of tiny square blocks around the circle showing the live FFT of the
  playing song;
- one click on the circle: it shrinks, and a horizontal strip grows out from
  behind it with **+ CREATE** on the left and **▶ PLAY** on the right, routing
  to `UploadScreen` and `RetroSongSelectScreen` respectively.

### Verdict on the spectrum ring (question 3): build it — it is not the hard part

osu.Framework hands you the FFT already computed. `Track` implements
`IHasAmplitudes`, and `track.CurrentAmplitudes.FrequencyAmplitudes` is a
256-float array, one bin per ~78 Hz step of 0–20 kHz, refreshed continuously by
BASS on the audio thread. No DSP, no P/Invoke, no backend call — this is the
same API osu!(lazer)'s own menu visualiser is built on. Confirmed present in
`ppy.osu.Framework 2026.807.0`, the version this project pins.

What *would* have been expensive is drawing it: 64 columns × 12 blocks = 768
drawables mutated every frame. Step 6 avoids that entirely — each column is a
**single masked sprite** over a pre-baked block-stack texture (exactly the
trick `CarouselBackground` already uses for its CRT scanlines), so the whole
ring is ~64 drawables and one `Height` assignment each per frame. That is
cheaper than the vinyl wheel already on screen in song select.

Two real caveats, both handled in step 6:

- `FrequencyAmplitudes` is mutated in place on the audio thread. **Copy it**
  into your own buffer before reading — the framework's own XML docs say
  outright there is no guarantee of a consistent single-sample state.
- Raw bins look terrible: bass swamps everything and the ring jitters. It needs
  log-frequency grouping + per-frame decay + neighbour smoothing. That's ~30
  lines of pure maths, and it goes in its own testable class, not in the
  drawable.

So: keep it. If it has to be cut later, step 6 is self-contained and deleting
`SpectrumRing` leaves a working menu.

## Reuse note — read before writing any new code

Almost every piece of this screen already exists somewhere in the repo:

| Need | Already in the codebase |
| --- | --- |
| Beat-synced pulsing | `Screens/Gameplay/BeatPulse.cs` — `PhaseAt` / `IntensityAt` / `PlayableEnvelope`, pure maths, already unit-tested |
| Playing a song from a beatmap folder | `RetroSongSelectScreen.updatePreviewTrack` (`host.GetStorage(dir)` → `StorageBackedResourceStore` → `audio.GetTrackStore(store).Get(file)`) |
| Blocky/retro type | `Graphics/RetroText.cs`, `RetroFontFamily.Display` = Press Start 2P (already embedded) |
| Palette | `Graphics/RetroPalette.cs` |
| Layered background: art + darken + grade + scanlines, crossfaded | `Screens/SongSelect/CarouselBackground.cs` — copy its structure, including the "only animate a layer that actually reached the tree" rule |
| Image file → `Texture` | `Graphics/BeatmapBackground.cs` (full size) and `Graphics/CoverArt.cs` (cached, downscaled) |
| Laying drawables out around a circle | `Turntable.strobeRing()` — `new Vector2(MathF.Sin(a), -MathF.Cos(a)) * radius` with `RelativePositionAxes` |
| Neon glow on a shape | `EdgeEffect`, as used by `HUD/GlowBar.cs` and `HUD/BeatBorderFlash.cs` |
| Verifying it actually renders | `OsuClient.Tests/Visual/ScreenshotHarness.cs` — add scenes to its `Scenes` dictionary |

**Do not** add an image-loading path, a colour constant, or a beat-phase
formula that duplicates one of these.

---

## Step 0 — Wallpaper source ✅

**Goal:** a tested way to ask for a random wallpaper path. No visual change.

- New `Graphics/WallpaperLibrary.cs`, mirroring `BeatmapLibrary`'s shape:
  - `ResolveDefaultDirectory()` — `OSUCLIENT_WALLPAPER_DIR` env var if set,
    else `Path.Combine(FindRepositoryRoot(AppContext.BaseDirectory), "data",
    "default wallpapers")`. Reuse `BeatmapLibrary.FindRepositoryRoot`; do not
    write a second repo-root walker.
  - `Load(string? directory)` → `IReadOnlyList<string>` of `.jpg/.jpeg/.png/
    .bmp/.webp` paths, sorted for determinism, empty list for a missing or
    empty directory. Never throws.
  - `PickRandom(IReadOnlyList<string>, Random?)` — injectable `Random` so the
    test is deterministic.
- **The folder is empty today and `data/` is gitignored**, so a clean clone has
  no wallpapers at all. That is the normal case, not an error: the fallback
  chain is *random wallpaper → the playing song's own background image → the
  bare `RetroPalette` gradient*. Decide this here so steps 2 and 3 have
  something real to render against.
- Unit tests in `OsuClient.Tests/Graphics/WallpaperLibraryTests.cs` against a
  temp directory: picks up images, ignores non-images, empty dir → empty list,
  missing dir → empty list, seeded `Random` → predictable choice.

*Why first:* it's pure and testable, and it's the only piece whose real data
doesn't exist yet on disk.

## Step 1 — `MenuTrack`: pick a random song and play it ✅

**Goal:** the menu plays a random library song on entry. Nothing on screen yet
except the existing placeholder UI.

- New `Screens/MainMenu/MenuTrack.cs` — a small component owning:
  - the chosen `BeatmapLibraryEntry` and a representative `Beatmap` from it
    (take `Difficulties[0]`, or the middle one — it only supplies timing
    points and metadata);
  - the `Track`, loaded by the exact route `updatePreviewTrack` uses;
  - `Looping = true`, a menu volume constant (~0.5), and a seek to the same
    "middle of the playable span" point song select previews from — the intro
    of a generated map is often silence.
- Selection rule: random among entries where `IsValid` **and** `Set?.AudioPath`
  exists. Empty library → everything downstream must run silently; the menu
  must not crash or show an empty credit.
- Expose read-only `Title`, `Artist`, `Beatmap`, `CurrentTime`, `IsRunning`,
  and the `Track` itself (step 6 needs `CurrentAmplitudes`).
- **Lifecycle, the bug-prone part.** Copy song select's contract exactly:
  `OnSuspending` → stop; `OnResuming` → restart the *same* song (don't reroll —
  rerolling on every trip back from song select is disorienting); `OnExiting` →
  stop. If the menu track is still running when `RetroSongSelectScreen` starts
  its own preview, you get two songs at once — that is the single most likely
  regression in this whole plan, so add the manual check for it now.
- Verify: run the game, hear a song, go into song select, hear exactly one song
  at a time.

## Step 2 — Background: wallpaper, graded, crossfading ✅

**Goal:** the menu sits on a random wallpaper instead of flat grey.

- New `Screens/MainMenu/MenuBackground.cs`. Structurally a sibling of
  `CarouselBackground`: `Void` fill → art layer(s) → black knock-back → sunset
  `GradeTop`/`GradeBottom` wash → scanlines.
- Differences from song select's version: **no blur** (the art is the subject
  here, not a backdrop for text), a weaker knock-back (~0.30 rather than 0.46),
  no left-side darkening gradient, and a slow ~1.06× Ken Burns drift so a still
  wallpaper doesn't look like a frozen frame behind a moving logo.
- Reuse `BeatmapBackground` for the sprite and keep
  `CarouselBackground.SetBackground`'s async-load discipline verbatim:
  `pendingLayer` vs `displayedLayer`, and never transform a drawable that
  hasn't entered the tree. That comment exists because it crashed before.
- Fallback chain from step 0. If the wallpaper folder is empty, this step
  visibly falls back to the song's own art, which is a good result, not a
  degraded one.
- Verify: new screenshot scene `menu-background`.

## Step 3 — The logo: outlined circle, OTO, spectrum fill ✅

**Goal:** the centrepiece, static — correct at rest, before any motion.

- New `Screens/MainMenu/MenuLogo.cs` (`CompositeDrawable`), sized as a square
  of `0.34 × min(DrawWidth, DrawHeight)` recomputed in `Update`, like the
  wheel geometry in `RetroSongSelectScreen`.
- Structure, inner to outer:
  1. `Circle` (or `CircularContainer` + `Box`) — the colour-cycling fill;
  2. `CircularContainer { Masking = true, BorderThickness ≈ 6–8,
     BorderColour = white, Child = transparent Box }` — the bold outline;
  3. `RetroText { Font = RetroFontFamily.Display, Text = "OTO" }`, centred,
     sized ~0.30 of the circle diameter, white.
- **Colour cycling: do not use a `FadeColour(...).Loop()` chain.** Colour
  transforms interpolate in RGB, so a red→green tween passes through mud, and a
  full hue loop needs six chained segments to look right. Instead set the fill
  colour each frame in `Update` from `Color4Extensions.FromHSV(...)` with the
  hue advancing by `Time.Elapsed` (one full revolution ≈ 24 s, saturation
  ~0.75, value ~0.95). Frame-time driven, so it's resolution- and
  framerate-independent.
- Optionally make the fill a two-stop `ColourInfo.GradientVertical` of two hues
  120° apart, both cycling — reads much more like a spectrum than a flat fill,
  costs nothing.
- Input: the circle must only react inside its circle, not its bounding box.
  `CircularContainer` with `Masking = true` already restricts positional input
  to the rounded region; assert this rather than assuming. Nothing behind it
  (background, spectrum ring) may take clicks.
- `AnimatedLogo.cs` is now dead — delete it in step 10, not here.
- Verify: screenshot scene `menu-logo`.

## Step 4 — Beat pulse ✅

**Goal:** the circle breathes on the playing song's beat.

- Drive it from `BeatPulse.PhaseAt(menuTrack.Beatmap, menuTrack.CurrentTime)`
  and `IntensityAt(phase, sharpness)`, the same pair `Turntable.SetBeatFlash`
  uses. Sharpness ~5–8; scale between 1.0 and ~1.06, and push the outline's
  `EdgeEffect` radius/alpha with the same intensity so the pulse is felt in the
  glow as well as the size.
- Apply the scale to a wrapper container, **not** to `MenuLogo` itself, so
  step 7's shrink animation and the beat pulse are two independent transforms
  that can't fight over `Scale`. (Nesting is the fix; a single shared `Scale`
  written by two systems is the classic source of jitter here.)
- No timing points / no track (empty library) → `PhaseAt` returns `NaN`,
  `IntensityAt` returns 0, and the logo just sits still. Verify that path
  explicitly by launching with `OSUCLIENT_SONGS_DIR` pointed at an empty dir.
- Optional second input: nudge the pulse with `CurrentAmplitudes.Average` so an
  actual kick shows even between notated beats. Only if step 6 is in already.

## Step 5 — Now-playing credit, top-right ✅

**Goal:** title and artist of the background song, top-right corner.

- Small right-aligned `FillFlowContainer`: a `NOW PLAYING` label in
  `RetroPalette.TextDim`, title in `RetroFontFamily.Body` at ~20px, artist
  dimmer at ~14px. Right-anchored with the same 52px margin song select uses.
- Fade in ~600 ms after the track starts; hide entirely when there's no track.
- **`RetroText` re-rasterizes to a texture on every `Text` assignment** — set
  it once when the song is chosen, never per frame.
- Verify: screenshot scene `menu-playing`.

## Step 6 — The spectrum ring ✅

**Goal:** a ring of tiny square blocks around the circle, reacting to the song.

Split deliberately into maths and drawing, so the fiddly half is testable
without a window.

**6a — `Screens/MainMenu/SpectrumAnalyser.cs` (pure, no framework):**

- `Update(ReadOnlySpan<float> bins, double elapsedMs)` → fills an internal
  `float[Columns]` in 0..1.
- Copy the incoming bins first (the audio thread mutates them in place).
- Log-frequency grouping: map 64 columns over bins ~1..128 (≈78 Hz–10 kHz;
  above that is near-silent for music), taking the max within each group —
  linear grouping puts everything interesting in the first three columns.
- Perceptual scaling: `MathF.Pow(value, 0.4f)` or a dB-ish log, then clamp.
- Per-frame decay so bars fall smoothly rather than strobing:
  `value = MathF.Max(newValue, previous * MathF.Pow(decayPerSecond, dt))`.
- One pass of neighbour smoothing (weights 0.25 / 0.5 / 0.25).
- Unit tests: silence → all zeros; a single loud bin → a peak in the expected
  column and near-zero elsewhere; decay falls monotonically when input stops;
  output always within 0..1 for adversarial inputs (NaN, huge, negative).

**6b — `Screens/MainMenu/SpectrumRing.cs` (drawing):**

- Bake one **block-stack texture** at load, the way
  `CarouselBackground.rebuildScanlinesIfNeeded` bakes its scanlines: a
  1-px-wide ImageSharp `Image<Rgba32>` of `blocks × (blockPx + gapPx)` rows,
  opaque for a block, transparent for the gap. Pixel-snapped, so the squares
  stay crisp.
- One column = `Container { Masking = true, Origin = BottomCentre }` holding a
  `Sprite` of that texture anchored to its bottom. Animating the container's
  `Height` reveals a whole number of blocks — that's the retro LED-meter look,
  for one drawable per column.
- Place the 64 columns with `Turntable.strobeRing()`'s formula:
  `Position = new Vector2(MathF.Sin(a), -MathF.Cos(a)) * ringRadius`, plus
  `Rotation` so each column points outward, radius just outside the logo's
  outline.
- Colour: hue by column index across the same spectrum the fill cycles through,
  or the fill's current hue ± a spread — they should look like one system.
- `Update` pulls `menuTrack.Track?.CurrentAmplitudes.FrequencyAmplitudes`, runs
  the analyser, writes 64 `Height`s. Nothing is created or destroyed per frame.
- No track → all columns at zero height, ring invisible. The ring must never
  handle input.
- Verify: screenshot scene `menu-spectrum` with an **injected fake bin array**
  (e.g. an `IAmplitudeSource` the harness can substitute) — a single captured
  frame of a real track is a coin-flip between "silence" and "everything", and
  you cannot debug a ring from a frame that happened to catch a rest.

## Step 7 — Click the circle: shrink, and grow the strip ✅

**Goal:** the state change, with the strip present but its buttons still blank.

- `MenuLogo` raises an `Action? Clicked` from `OnClick`. The screen owns a
  two-state machine — `Idle` / `Expanded` — and one method per transition; do
  not scatter transforms across event handlers.
- `Idle → Expanded`: logo wrapper `ScaleTo(0.62f, 420, Easing.OutQuint)`; the
  strip container `ResizeWidthTo(target, 460, Easing.OutQuint)` from 0 with a
  `FadeIn(260)`. The strip is a horizontal bar centred on the logo, drawn
  **behind** it in the tree, so it reads as sliding out from under the circle
  with the buttons emerging either side.
- `Expanded → Idle` on a second click of the circle, or `Escape`. A menu that
  can only open is a dead end and will be the first thing you trip over while
  testing the rest.
- Guard re-entrancy: ignore a click while a transition is running, or
  `FinishTransforms()` on the pieces before starting the next one. Rapid
  clicking mid-tween is how these get stuck half-open.
- The strip's target width is geometry — compute it in `Update` from the
  window's smaller dimension, not a literal.
- Verify: screenshot scenes `menu-idle` and `menu-expanded` (the latter with an
  `OnLoadComplete` hook calling a public `Expand()`, exactly how
  `retro-song-select-turned` steps the wheel).

## Step 8 — The two buttons ✅

**Goal:** CREATE and PLAY, themed, wired.

- New `Screens/MainMenu/MenuStripButton.cs`: a wide flat slab with a
  chrome/panel gradient from `RetroPalette`, a neon accent edge (`Magenta` for
  CREATE, `Cyan` for PLAY), an icon, and a `RetroText` label in
  `RetroFontFamily.Body`.
- Icons drawn in code, not imported: the play triangle is a rotated `Triangle`
  drawable (or three `Box`es); the `+` is two crossed `Box`es. Consistent with
  the "draw retro effects in code" rule the carousel plan set.
- Hover: accent brightens, slight `ScaleTo(1.03f)`, outward nudge. Click: brief
  flash, then navigate.
- Wiring, guarded with `this.IsCurrentScreen()` like the current menu does:
  - PLAY → `this.Push(new RetroSongSelectScreen(songsDirectory))`
  - CREATE → `this.Push(new UploadScreen(songsDirectory))`
- Keyboard: `Enter` = PLAY (matching today's behaviour and its hint text);
  `Space`/`Enter` in `Idle` expands first. Keep Exit reachable — the old menu
  had an Exit button and this design has no third slot; put it on `Escape` from
  `Idle` (same `host.Exit()` call) and say so in a small hint line.
- Verify: click both routes, and back out of both, with audio behaving per
  step 1.

## Step 9 — Entry/exit polish ✅

**Goal:** the menu doesn't just pop into existence.

- `OnEntering`: background fades in first, then the logo scales up from ~0.9
  with `OutQuint`, then the credit fades in — ~150 ms apart, total under a
  second.
- `OnResuming` from song select: skip the staggered intro, just fade in, and
  reset to `Idle` so the strip isn't left open from last time.
- `OnExiting` / `OnSuspending`: stop the track (step 1), fade out.
- Resize behaviour: drag the window from tiny to maximised and confirm the
  logo, ring radius, strip width and margins all track `min(width, height)`.

## Step 10 — Visual QA, then delete the old menu ✅

- Capture every scene at 1920×1080 and at ~1280×720 and **look at them**:
  `dotnet run --project frontend/OsuClient.Tests -- --screenshot <scene>
  <out.png> [delayMs]`. Green tests prove nothing about rendering — that's what
  the cursor incident in `FRONTEND_PLAN.md`'s Phase 5 notes is about.
- Checklist: outline is crisp white at both sizes · OTO is pixel-aligned, not
  blurry · the hue cycle never passes through grey or washes the text out ·
  blocks are square, not rectangles, at both sizes · the ring doesn't overlap
  the outline · the strip is symmetric · nothing clips at 720p.
- Delete `Graphics/AnimatedLogo.cs` and the old `MainMenuScreen` body once the
  new one is feature-complete (mirroring how step 11 of the carousel plan
  retired `BeatmapCarousel`). Check for other references first — `AnimatedLogo`
  may appear in a test scene.
- Update `FRONTEND_PLAN.md`'s roadmap with a line pointing here, and append an
  "as built" section to this doc.

---

## Art & asset needs

**None.** Everything is procedural: the fonts are already embedded
(`Resources/Fonts/PressStart2P-Regular.ttf` for OTO), the block texture is
baked at runtime, the icons are boxes and a triangle. The only external files
are the wallpapers dropped into `data/default wallpapers` — and the menu works
without them (step 0's fallback chain).

## Risks and known traps

1. **Two songs at once.** Menu track vs. song select preview vs. gameplay
   playback. Step 1's lifecycle contract is the whole mitigation; re-check it
   after step 8 wires navigation up.
2. **`FrequencyAmplitudes` is a live buffer.** Copy it. Don't cache the array
   reference.
3. **Two systems writing one `Scale`.** Beat pulse and the shrink animation —
   separate containers (step 4).
4. **Transforms on async-loading drawables crash.** The `pendingLayer` /
   `displayedLayer` split in `CarouselBackground` exists for exactly this; keep
   it when you copy that class.
5. **`RetroText` re-rasterizes on assignment.** Never set `Text` in `Update`.
6. **Colour transforms interpolate through grey.** Per-frame HSV instead
   (step 3).
7. **Single-frame screenshots of live audio are unreliable.** Inject fake
   amplitudes for the spectrum scene (step 6b).
8. **Half-open strip from mid-tween clicks.** Guard the transition (step 7).

## Open questions

- "A horizontal strip appears *underneath*" — this plan reads that as *behind
  and spanning outward from* the circle, buttons flanking left and right, since
  the brief calls them "two buttons on both side of the circular spectrum". If
  a bar stacked *below* the circle was meant instead, only step 7's layout
  changes; steps 8–10 are unaffected.
- Should the menu reroll its random song each time you come back from song
  select, or keep playing the one it started with? Step 1 assumes *keep*.
- Exit: `Escape` from `Idle` (this plan), or a third small button on the strip?

---

## As built — what steps 0–10 actually taught us

All eleven steps are in. What follows is what differed from the plan, and the
bugs the plan didn't predict — the reason this section exists.

### The spectrum was the easy part; the scale transforms were not

Step 6 landed almost exactly as designed and took one tuning pass. The plan's
two predicted traps (copying the live buffer, needing log grouping + decay)
were both real, both handled up front, and neither cost any time. What
actually broke things was the risk listed at #3 — **two systems writing one
property** — and it broke three separate times, each in a different place:

1. **`EdgeEffect`, in `MenuLogo`.** The first draft had the beat writing the
   glow every frame *and* hover animating it with `FadeEdgeEffectTo`. Caught
   while writing it: the hover tween would have been overwritten on the next
   beat. Fixed by making the glow a pure function of `beat` + `IsHovered`,
   recomputed in `Update`.
2. **`logoArea.Scale`, between the entry animation and the open/close
   transition.** Not caught while writing it — caught by a 720p screenshot
   where the logo was plainly the wrong size. Entering the screen already open
   ran `Expand()` from `OnLoadComplete` and then `OnEntering`'s scale-up, and
   the second simply replaced the first. Fixed by nesting a second container
   (`logoEntry`) so each animation owns a `Scale`.
3. **`MenuStrip.Width`**, avoided by design: `Update` owns it, and the
   animation drives an `Expansion` property instead.

The lesson for the next screen: "give each animation its own container" isn't
a style preference, it's the default, and the cases that bite are the ones
where the two writers are in *different classes* and neither looks wrong on
its own.

### Three bugs a green test suite would never have found

- **The strip was invisible.** It was added with `Depth = 1` to put it behind
  the logo, which put it behind the *opaque background* instead. Equal-depth
  ordering by insertion position is what was wanted.
- **A faint dotted ring with nothing playing.** Columns at zero height still
  showed a pixel of themselves: a masked container feathers its edge, so a
  zero-height clip leaks about a pixel. Columns are now hidden outright at
  zero rather than merely flat.
- **`Expand()` before the screen finished loading threw.** The fields it
  touches are built on the background load thread. A test scene that pushes
  the screen and opens it on the next step hits this reliably; the screenshot
  scenes never did, because they hook `OnLoadComplete`. The state is now
  recorded whenever it's set and applied without animation in `LoadComplete`.

All three were found by looking at rendered frames and by driving the screen
from a test scene — none of them fails a unit test, and the first two don't
even log anything.

### Deviations from the plan

- **128 columns and 10 blocks, not 64 × 12** (later 144, mirrored — see
  revision two). 64 columns left the ring too
  sparse to read as a spectrum at the radius the logo actually occupies. The
  masked-sprite approach made the column count nearly free, so it went up
  rather than down.
- **Gain was tuned from captures, not derived.** The first pass (gain 9) sat
  pinned at full height through any ordinary chorus — a ring, not a spectrum.
  3.2 with a 0.65 curve gave the full range on real tracks; revision two
  raised it again once the per-band tilt was added.
- **`ScanlineOverlay` was extracted rather than copied.** Song select had the
  only scanline bake; rather than duplicate 20 lines, it moved to
  `Graphics/ScanlineOverlay.cs` and `CarouselBackground` now uses it too.
- **Exit moved to `Escape` from the closed state**, as the plan's open
  question anticipated, with a contextual hint line at the bottom of the
  screen. `Enter` opens the strip when closed and starts a game when open.
- **`SpectrumRing.AmplitudeSource` is public** and is what both the
  `menu-spectrum` screenshot scene and the interactive test scene drive, so
  neither depends on what the song happened to be playing.

### What the wallpaper fallback actually does

`data/default wallpapers` was empty for the first half of this work, which
turned out to be the useful case: every early capture exercised the *fallback*
(the playing song's own cover art), and it looked deliberate rather than
broken. Wallpapers dropped in later were picked up with no code change. Both
paths, and the third (neither available → the bare gradient, verified with an
empty songs directory), have been seen on screen.

### Scenes and tests added

- `main-menu`, `main-menu-expanded`, `menu-spectrum` in `ScreenshotHarness`.
- `TestSceneMainMenu` — open/close hammering, and the ring on a sweeping
  synthetic signal.
- `WallpaperLibraryTests` (8), `MenuTrackTests` (5), `SpectrumAnalyserTests`
  (11). Full suite: 390 passing, 1 skipped.

### Both follow-ups from the first pass are now in

- **The pulse reacts to loudness, not just to timing points.** Step 4's
  optional amplitude nudge is in, but as a *modulation* rather than an
  addition: `beat * (0.7 + 0.3 * LowEnergy)`. Taking the louder of the two
  instead — the obvious first try — leaves the logo permanently inflated
  through any sustained bass line, because bass never drops between beats
  the way the timing-point pulse does. `LowEnergy` is the mean of the
  bottom eighth of the spectrum columns, so it reuses the analyser's
  existing smoothing and decay instead of growing a second envelope
  follower on raw `CurrentAmplitudes.Average`.
- **PLAY carries the song across.** Song select is pushed with the menu
  track's own set path through the `preferredSetPath` argument it already
  took, so the music continues instead of cutting to an unrelated track.

### A test-infrastructure trap, found while verifying this

**Running the screenshot harness immediately before `dotnet test` makes the
next test run fail 54 visual tests and take 70s instead of 15s.** The run
after that passes. It is not caused by anything in this work — screenshotting
the older `retro-song-select` scene poisons the next run in exactly the same
way — and it leaves no file behind in `bin/`, so it looks like the graphics or
audio device still being torn down as the test host starts.

It matters because it is a convincing false alarm: the failure count is
stable at 54, so it reads as a real regression rather than as noise. **If a
test run fails right after you captured a screenshot, run it again before
believing it.**


---

## Revision two — five changes after the first play-test

### The buttons never appeared, and the reason was self-inflicted

`MenuStrip.Update` set `Alpha = 0` while the strip was shut. A drawable that
hides itself in its own `Update` is no longer present, and so **stops being
updated at all** — which meant the strip could never animate back open. It was
shut on its first frame and stuck there for the life of the screen.

What made it hard to see:

- The click *worked*. `applyExpansion` ran, the logo shrank on cue and the
  hint line swapped to the open text. Only one of the three things that
  method starts had no effect, so the screen read as "opened, with the
  buttons missing" rather than as "an animation that never ran".
- **Every existing check passed.** The `main-menu-expanded` screenshot scene
  calls `Expand()` from `OnLoadComplete` — before the strip's first `Update`,
  so `Alpha` was still 1 and the transform ran fine. The NUnit click test
  passed for the same reason, and *still* passes with the fix reverted: a
  `TestScene` keeps its content updating in a way the real tree does not.
  Neither could see this.

It was caught by building a screenshot scene that reproduces the real thing —
the actual tree the game builds, cursor layer included, driven by a real
click — and then looking at the frame. That scene (`main-menu-clicked`) is
kept as the regression guard, since the unit test demonstrably cannot be one.

Two smaller things fell out of chasing it:

- `MoveMouseTo` and `Click` in the same frame hit-test against the pointer's
  *previous* position, so the click lands on nothing. Split across frames, or
  the scene silently captures a closed menu.
- `AlwaysPresent` keeps the shut strip updating, so `ReceivePositionalInputAt`
  now refuses input below half expansion — otherwise an invisible strip would
  still catch clicks along its centre line.

### The other four

- **Bigger circle, both states.** The resting circle went from 0.33 to 0.44 of
  the window's smaller dimension, and the open state shrinks it to 0.33 —
  i.e. to what it used to be at rest. The strip's own factors were divided
  down by the same ratio so it stays the width it was on screen.
- **The flash moved to the window edges.** The strip buttons' click flash is
  gone; the beat now washes in from the left and right edges the way gameplay's
  does. The two screens share the drawing (`Graphics/EdgeGlow.cs`, extracted
  from `BeatBorderFlash`, which keeps its beat maths) and differ only in what
  drives it. The menu's is tinted with the logo's current hue rather than
  gameplay's fixed blue.
- **The ring is even now.** Two changes: a **per-band tilt** weights bands by
  frequency, because music falls off steeply with pitch and an untilted ring
  is a wall of bass beside a dead arc of treble — all its motion crowded into
  one quarter of the circle. And the ring is **mirrored**, 144 columns over 72
  bands, so bass sits at the top and treble at the bottom on both sides. The
  first tilt (0.75 at a 1.9 kHz pivot) over-corrected and simply moved the bald
  patch to the top; 0.55 at 1.2 kHz, with the gain raised to 5, fills it.
  `max_length` is no longer a free parameter — it is derived so the blocks come
  out square, since a block is as wide as its share of the circumference.
- **The background wears the circle's colour.** `MenuLogo` exposes its live
  hue; the screen feeds it to the background wash at a much lower value (0.42
  against the circle's 0.96) and to the edge flash. Strength landed at 0.44
  after 0.5 buried the wallpaper and 0.3 was too weak to read as synced —
  it is one constant (`tint_strength`) if it wants moving again.

The centre gap between the buttons is now sized against the **ring's** reach
rather than the circle's: a loud passage throws blocks about half the logo
square out from the centre, and a gap that only cleared the circle let them
land on the buttons.


---

## Revision three — the build-up

### The play arrow was sagging because rotation pivots on the origin

`Rotation` turns a drawable about its **Origin**, and the button's layout sets
every icon's origin to its centre-left — so a bare `Triangle` with
`Rotation = 90` swung down and right around its own left edge instead of
spinning in place. The plus beside it was never rotated, which is why only
PLAY looked wrong, and why it read as "tilted" rather than as "misplaced".

The triangle is now wrapped in a plain box and rotated about its own centre
inside it, so the turn and the layout never touch each other.

### Music now starts quiet and swells

The menu track opens at 0.18 and rides to 0.6 over 900 ms when the strip
opens, transformed rather than set, so it is heard as a rise. Paired with the
colour spread below, the click reads as the app coming up to speed rather
than as a menu that was already running.

### A synthesized whoosh, verified without listening to it

`MenuSoundSynth.Whoosh()` generates it at startup the way
`HitSoundSynth` does the hitsounds — filtered noise under a swelling
envelope, with the filter sweeping up and back. No sample file to ship.

**This was tuned by measurement, not by ear**, since the session that wrote it
could not play audio. The first version used a single one-pole low-pass and
measured a spectral centroid of 7-9 kHz — audibly a hiss, not a whoosh,
because one pole rolls off at only 6 dB an octave and leaves most of the
energy above the cutoff. Cascading two stages and dropping the peak cutoff to
1.8 kHz puts the centroid where it belongs: sweeping about 1.9 → 3.7 → 2.1 kHz
and back, under a smooth hump envelope, peaking at 0.85 with no clipping.
Those are the numbers to re-check against if it is ever re-tuned. Its level
and length are taste, and have not been heard.

The output is normalised to a target peak rather than scaled by a hand-tuned
gain, because the cascaded filter's output level depends on every other
constant in the function.

### Colour spreads out of the circle

The wallpaper is decoded **twice** — a grey copy and a colour copy — and the
colour one lives inside a `CircularContainer` mask centred on the logo. At
rest only grey shows; clicking grows the mask past the screen corners over
1150 ms on `OutQuint`, with three staggered rings chasing it outwards. Closing
drains it back to the circle's edge.

Two things make this work that are easy to get wrong:

- **The content inside the mask is sized to the window, not to the mask.**
  Sizing it to its parent would make the picture appear to zoom out of the
  circle as the mask opens, instead of standing still while the mask
  uncovers it.
- **Both copies drift on their own containers, started in the same frame.**
  The slow Ken Burns drift has to be identical on both, or the colour copy
  slides against the grey one and the reveal edge ghosts.

Greyscale is applied on the pixels at decode time (`BeatmapBackground` took a
`greyscale` flag) rather than with a shader, which keeps the whole screen on
stock drawables.

### The edge flash starts white

A coloured flash on a black-and-white screen was the one thing still breaking
the monochrome opening. The flash now interpolates from white to the logo's
hue on `colourAmount`, a bindable animated on the same 1150 ms curve as the
colour spread — so it takes its colour as the picture does, rather than
snapping at the click.

Measured rather than eyeballed: on an idle frame the outer six pixels come
back perfectly neutral (R=G=B, zero spread) and brighter than the interior on
a beat, which is what "white flash over a grey screen" should look like.
`TestEdgeFlashStartsWhite` pins both directions.

### Still not verified by ear

The whoosh's loudness and duration, and how the volume swell sits against it.
Everything else in this revision was checked on rendered frames.
