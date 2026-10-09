# Song select carousel redesign — vinyl & cassette

Status key: 📋 planned · 🚧 in progress · ✅ done. Update a step's header when
you start/finish it, and append an "as built" note the way `FRONTEND_PLAN.md`
does for its phases — this doc is meant to survive contact with the real
implementation, not describe an idealized one.

## 0. What this replaces, and why it's a separate doc

Today's carousel (`Screens/SongSelect/BeatmapCarousel.cs`) is a flat vertical
scroll list of panels — no wheel, no artwork, no theme. `SongSelectScreen.cs`
wraps it in a plain header/footer grid. This plan replaces both with a themed
song-select screen: a half-visible spinning vinyl wheel on the right, retro
cassette-tape difficulty buttons, a blurred/duotone background, and top-left
song/beat/map info — per the brief and the "Color Your Night" concept art in
`visualization From OSU! lazer/carousel UI/`.

This is the most visually ambitious screen in the client so far, with one
genuinely hard technical unknown (rendering a filled, texture-mapped pie
wedge — osu.Framework has no wedge primitive). Building it as 11 small,
independently-verifiable steps means that unknown gets isolated and solved
(step 6) *before* the rest of the screen is built on top of it, and every
other step ships something you can actually look at, rather than one big
change that's all-or-nothing to debug.

**Reuse note (read this before writing new code):** this codebase already
has an established "draw retro effects in code, not with art assets"
pattern — `RetroText` (bitmap-rasterized retro fonts), `DrawableSpinner`
(rotating `CircularProgress` arcs), `HealthBar`/`GlowBar`/`BeatBorderFlash`
(procedural neon glow via `EdgeEffect`), `BeatmapBackground` (cover-image
sprite, already loaded via `SixLabors.ImageSharp`). This plan leans on all of
them rather than reinventing equivalents — see §"Art & asset needs" below
for what's genuinely new vs. reused.

---

## Step 0 — Plumb background artwork through to the UI ✅

**Goal:** every `BeatmapLibraryEntry` can hand the UI a background image
path. No visual change yet.

- `OszImporter`'s `BeatmapSet.BackgroundPath` already gets resolved on
  import but isn't exposed anywhere `SongSelect` code can see it.
  `BeatmapLibraryEntry` needs a `BackgroundPath` (or expose `Set.BackgroundPath`
  directly — `Set` is already public) that the new UI reads.
- Decide the no-background fallback now, not later: a solid dark card with
  a procedural gradient (no missing-texture flicker). This is what every
  later step's placeholder art renders when a test entry has no background.
- Add/extend a unit test asserting a set built with a background file
  resolves a real, existing path (mirrors the existing decoder tests'
  style of asserting on `BeatmapSet` fields).

Nothing user-visible changes here — this step only exists so steps 2 and 6
have real data to point at instead of TODOs.

## Step 1 — New screen skeleton, old carousel untouched ✅

**Goal:** a `SongSelectScreen` layout with five empty regions in roughly the
right place, at multiple window sizes, with the *existing* `BeatmapCarousel`
still working behind it (or beside it, whichever's easier) so nothing is
ever broken mid-rewrite.

- Recommend building the new screen as a new class/branch of
  `SongSelectScreen` (or behind a constructor flag) rather than editing the
  current one in place — the old flat list stays reachable and playable
  until the new one is feature-complete end-to-end (step 10), then gets
  deleted in step 11. A half-migrated screen that's neither the old list
  nor the new wheel is the worst state to leave the repo in between
  sessions.
- Five regions, positioned but empty (flat colour blocks are fine here):
  top-left info panel, left-middle difficulty-button column, bottom-left
  search bar, full-bleed background layer behind everything, and a
  right-side wheel region whose *centre point sits outside the right edge
  of the screen* — that's what makes it read as "half a vinyl record"
  rather than a full wheel. Get that anchor math and the regions'
  proportions settled now; every later step assumes these rectangles don't
  move.
- Test at a narrow and a wide aspect ratio — the wheel region in particular
  needs to not collide with the left column on a 4:3-ish window.

## Step 2 — Background layer: blur + retro colour grade ✅

**Goal:** the selected song's background fills the screen, blurred, with an
80s duotone/gradient grade over it. Independently verifiable — point it at
a real `.osz` and look at it.

- Reuse `BeatmapBackground` for the image load; wrap it in a
  `BufferedContainer` with `BlurSigma` (same mechanism Phase 8 already
  plans to use for gameplay's background blur — see `FRONTEND_PLAN.md` §Phase 8 —
  so this isn't a new technique for the codebase, just an earlier use of
  one already designed in).
- Retro grade: a gradient `Box` (deep purple → magenta → warm orange, sampled
  from the concept art) composited over the blur at partial alpha/multiply
  blend. Cheap, no shader needed.
- Crossfade (200–300ms) when the selection changes, driven by whatever
  event step 8 ends up firing — stub it with a manual `AddStep` in a test
  scene for now if step 8 isn't built yet.
- A faint scanline overlay (repeating 1px-alpha horizontal lines drawn once
  into a small tiling texture) for CRT texture.

## Step 3 — Top-left info panel ✅

**Goal:** song title/artist, BPM, hit-object count, AR/CS/OD — the same data
`SongSelectScreen.onSelectionChanged`/`detailsLabel` already compute today —
restyled as a retro card.

- `RetroText` with `RetroFontFamily.Body` (Audiowide) for labels/title,
  `RetroFontFamily.Display` (Press Start 2P) for the chunky numeric readouts
  (BPM, counts) — both fonts are already embedded resources, no new font
  needed for this step.
- Rounded card, thin neon-glow border (`EdgeEffect`, same pattern as
  `GlowBar`/`HealthBar`), semi-transparent dark fill so it stays legible
  over any background art.

## Step 4 — Search bar, bottom-left ✅

**Goal:** a working text filter over the loaded entries, styled as a retro
pill/label. Buildable and testable against a flat list of entries even
before the wheel exists.

- A `BasicTextBox` (or a thin custom subclass for the rounded/glowing skin)
  bound to a filter predicate over `IReadOnlyList<BeatmapLibraryEntry>` —
  substring match on artist/title/mapper, case-insensitive.
- Styled as a **cassette label sticker** rather than a generic search pill:
  slightly-rounded rectangle with a taped/torn corner detail, handwriting-ish
  placeholder text. Fits the theme at basically no extra cost over a plain
  rounded input.
- Filtering needs a home: either `SongSelectScreen` holds the full list and
  hands the wheel a filtered view, or the wheel component takes the filter
  string directly. Prefer the former — keeps the wheel a dumb renderer of
  "whatever list it's given," matching how `BeatmapCarousel.SetEntries`
  works today.
- Verify empty-query (show all) and no-matches (empty wheel — must not
  crash step 6/7's angle math on a zero-count list) as explicit test cases.

## Step 5 — Cassette-shaped difficulty buttons ✅

**Goal:** replace `DifficultyIcon`'s pill shape with a cassette-tape body,
keeping its exact public contract (`Selected`, `Action`, `Beatmap`) so
nothing else has to change.

- All procedural, consistent with the codebase's existing style: rounded
  rect shell, two circular "reels" (`Circle`) with a few thin rotated
  `Box` spokes, a recessed label-window rect showing the difficulty name in
  `RetroText`. Tint per difficulty using the existing density-based ramp in
  `DifficultyIcon.ColourFor` — worth re-picking the five colours for a
  neon/synthwave palette while this is being rebuilt anyway.
- **One palette, two panels:** the colour a difficulty gets here is the same
  colour that difficulty contributes to its wedge's accent tint in step 7,
  so the cassette column and the wheel visually agree instead of running
  independent palettes. Put the ramp somewhere both can read it (a small
  shared `Graphics/RetroPalette.cs` — the codebase has no central colour
  class today, and two features needing the same ramp is the point where
  one earns its place).
- Small idle flourish, cheap and worth it: reels rotate slowly while
  hovered/selected (a `Circle` + child spokes on a `Transforms` rotation
  loop).
- Layout: vertical stack between the search bar and the info panel, per the
  brief and the concept art. Keep the column a `FillFlowContainer` so
  orientation (vertical vs. horizontal) is a one-line change if it turns
  out to read better horizontally once real content is on screen.

## Step 6 — Wedge prototype (the hard part, isolated) ✅

**Goal, and *only* goal: one correctly-shaped, correctly-coloured pie wedge
on screen, in a throwaway test scene, matching the concept art's proportions.
Nothing else — no wheel, no data, no interaction — until this looks right.

- Use `osu.Framework`'s `CircularProgress` as the wedge primitive:
  `InnerRadius` (0 = full pie slice from centre, like the mockup's outer
  ring segments; a small nonzero value if the wheel ends up wanting a
  visible hole at the hub), `Progress` for the arc's angular width,
  `Rotation` for its start angle. This isn't a new technique for this
  codebase — `DrawableSpinner`'s completion meter (`Screens/Gameplay/DrawableSpinner.cs`,
  the `arc`/`half` helpers around line 370) already builds two-tone
  `CircularProgress` arcs the same way; read that before starting.
- **The one real unknown: texturing a wedge with the song's own artwork.**
  `CircularProgress.Texture` maps U (0→1) across the arc sweep and V (0→1)
  across the radius — feeding it a song's square cover art will show it
  *polar-warped* around the wedge, not as a straight rectangular crop.
  That's not a bug to route around — it's very close to how the Persona 3
  reference art (`art3.jpg` in the concept-art folder) actually renders
  character portraits into its wedges, so try it straight first. Tint via
  `CircularProgress.Colour` (multiplies the texture, same as a `Sprite`)
  to get the "background picture overlaid by accent colour" look the brief
  asks for on non-selected wedges.
- Fallback if the polar warp looks bad on real album art (busy or
  off-centre images can warp ugly): drop the texture and fill the wedge
  with a flat accent colour instead, keeping the image only on the
  elevated/selected wedge (rendered as a normal masked `Sprite`, not a
  `CircularProgress`, since it isn't wedge-shaped once it's "popped out" —
  see step 7). Decide this by eye against 3–4 real generated `.osz`
  backgrounds, not in the abstract.

## Step 7 — The ring: many wedges, one selected and elevated ✅

**Goal:** N songs laid out as wedges around the off-screen centre from
step 1, matching the brief's "smaller pies for others, highlighted and
elevated for the selected one."

- Equal angular slices (`360° / visible-count`, or a fixed per-slice angle
  with the rest scrolled off — pick based on how step 6's wedges look at
  the concept art's slice count, roughly 8–10 visible).
- Selected wedge: larger `Progress`/radius, full brightness/opacity, real
  (unwarped) artwork — render this one specifically as the masked `Sprite`
  fallback from step 6 inside a rounded-rect card, translated outward from
  the ring and drop-shadowed, exactly like the concept art's single
  "popped out" slice. Everything else stays a `CircularProgress` wedge at
  reduced size/opacity per step 6.
- Cull wedges outside the visible half-circle plus a small buffer — don't
  instantiate hundreds of `CircularProgress` drawables for a large library
  up front; only step 4's filtered/visible set needs live Drawables.

## Step 8 — Interaction: scroll-to-rotate, click-to-select ✅

**Goal:** mouse wheel rotates the ring with snap-to-nearest-slice; clicking
a wedge selects it; existing keyboard up/down still works. The public
contract (`SelectionChanged`, `SelectionConfirmed`, `BeatmapSelection`,
`SelectRelative`, `ConfirmSelection`) stays identical to today's
`BeatmapCarousel` so `SongSelectScreen`'s wiring to `PlayerScreen` needs no
changes at all.

- `OnScroll` accumulates an angle delta; on scroll-end (or continuously,
  designer's call by feel) snap-animate to the nearest slice boundary.
- Clicking a non-selected wedge animates the ring to bring it to the
  selected position rather than selecting in place — matches "select a pie
  slice sector by clicking to highlight the song" while keeping one fixed
  visual "selected" position (the elevated slot) rather than the highlight
  moving around the ring.
- `SelectRelative` (still driving Up/Down) becomes "rotate by one slice"
  instead of "scroll the list by one panel."

## Step 9 — Turntable chrome & polish (cuttable) ✅

*As built:* the planned "slow idle rotation of the ring" was replaced with a
light band sweeping across the disc. Rotating the ring turned out to be
incompatible with the design it was being added to: the selected song is
pinned to 9 o'clock, so turning the wedges drags the selection out from
under the card and the tonearm. A sweeping sheen is what a spinning record
looks like anyway, and it costs nothing to leave running.

*As built, later — one sheen was not enough.* At 19s per revolution and 0.055
alpha it was too slow and too faint to read as anything, and the rest of the
record couldn't help: **the disc, the grooves and the hub are all circles, so
rotating them changes not a single pixel.** With the wedges unable to turn
either, nothing on the record could show motion. Fixing it meant *adding*
things worth rotating rather than rotating what was already there —
`surfaceHighlights` (short, faint additive arcs scattered at different radii,
light catching the grooves) and print marks on the hub's label. Notes:

- **The bare vinyl between hub and wedges is the valuable space.** It's wide,
  near-black, visible, and nothing else competes for it, so a highlight
  there shows clearly at an alpha that would be invisible over cover art.
  The first pass put the arcs at 0.035–0.055 alpha everywhere and they
  simply could not be seen; the inner ones are ~3x that now, the two over
  the artwork deliberately still dim.
- **Everything turns at one speed, 1800ms — a real 33⅓ RPM**, which is what
  the deck's own lit "33" pill claims. These are all the same spinning
  object in reality, and any disagreement reads as parts sliding over each
  other.
- **Except the strobe dots**, which drift at 9s. That's both authentic (a
  strobe ring's purpose is to appear nearly locked) and necessary: 72 dots
  5° apart moving 3.3° per frame is a wagon-wheel.
- **A still screenshot cannot verify motion.** Capturing two frames a known
  interval apart and scanning luminance around a fixed radius can: the
  highlight bump at 0.29R moved 237°→257° across 150ms, clockwise, which is
  33⅓ RPM within the jitter of two separate process launches.

*As built, later:* the chrome listed here — hub, tonearm, grooves — turned
out not to be enough on its own. With nothing under it, the record and the
arm read as floating on the background rather than sitting on anything, so
`Turntable.cs` now draws the deck they sit on: plinth, platter, a recess
ring, the record's shadow, strobe dots and a control strip.

- **Only a crescent of the deck is ever visible**, since the hub is off the
  right edge, so the detailing is chosen for what survives that crop —
  platter edge, strobe dots, arm base. A plinth outline or a platter centre
  would need the whole deck on screen to say anything.
- **The deck reaches further in than the disc**, so the left-hand column had
  to be re-measured against `Turntable.DeckOverhang` instead of the disc's
  rim, or the panels would sit on top of it.
- **The tonearm had to start pivoting about its own bearing.** It used to
  rotate the whole arm about the *hub*, which meant its pivot end orbited
  along the rim — invisible while the arm floated, obvious the moment a base
  was mounted under it, because the arm slid off its own base on every
  selection change. It now rotates about a fixed bearing at
  `Tonearm.PivotRadius`, which is both how a real arm works and what lets the
  base, the counterweight and the arm stay one assembly. The needle still
  sweeps ~±6.6° about the hub, so the behaviour the wheel was tuned against
  didn't change.
- **The platter had to be dark.** Drawn in the obvious brushed chrome it
  became the brightest thing on that side of the screen and read as the
  subject rather than as the surface the subject rests on.

- **A beat-synced glow around the record** reuses `BeatPulse` — gameplay's
  own flash maths — in the selected song's accent colour, driven off the
  preview track's clock. Getting it to read as a *glow* took three attempts,
  and the two failures share one cause: **a glow needs a falloff, and a
  shape's edge is not one.**

  - `EdgeEffect` on the deck's own rounded-rect mask lit that shape's long
    straight sides as a full-height bar down the window. The deck is far
    larger than the viewport, so its "edge" is mostly nowhere near the
    crescent the player can actually see.
  - A thin `CircularProgress` annulus at the deck's edge is a hard-edged
    outline, so it could only switch on and off.
  - What works is `EdgeEffect` on a masked `CircularContainer` sized to the
    record itself: the edge effect is a *blurred* copy of the masking shape,
    which is a real radial falloff, and a circle is the one shape the
    "EdgeEffect can't glow an arc" caveat above doesn't apply to.

  Two tuning points matter as much as the shape. The pulse needs a **floor**
  (`glow_base_alpha`) — a glow that reaches zero between beats stops being a
  glow and becomes a light being switched — and it wants a **much rounder
  curve** than gameplay's (sharpness 1.8 against 4), because that screen
  wants a flash felt *at* the beat where this one wants something breathing
  along with it. It swells in radius as well as opacity for the same reason.

  Worth knowing for the next effect like this: a still screenshot can't tell
  you whether a pulse is a gradient or a step. Sampling pixels outward from
  the rim across several capture delays can, and did — a smooth 151→58 decay
  at the peak against a flat 53 background, with a resting frame still
  reading 76 at the rim.


Pure decoration, safe to trim under time pressure, ship after 0–8 work:

- Centre spindle/hub circle, a tonearm silhouette pointing at the
  elevated wedge (a rotated thin rect + small circle — no art asset
  needed), thin concentric groove rings across the disc (a handful of
  low-alpha `CircularProgress` outlines).
- **Needle-drop on selection:** the tonearm nudges toward the newly-selected
  wedge with a short overshoot-and-settle (`Easing.OutElastic`-ish) whenever
  the ring snaps. Cheap, and sells the vinyl metaphor far harder than a
  static arm — this is the single highest charm-per-line item in the plan.
- Slow idle rotation of the whole ring when untouched (~1 rev/30s),
  paused the instant the user scrolls or hovers — ambience, not
  navigation.
- Corner "hardware" trim (screw-head dots, brushed-metal-gradient edges)
  extended from the cassette buttons to the info panel and search bar for
  a consistent physical-object feel across all three left-side panels.

## Step 10 — Wire it all together ✅

*As built:* two things the plan didn't anticipate.

- **Building alongside the old screen has a failure mode:** it is finished
  and working and still not reachable. Both entry points (`MainMenuScreen`,
  `DspVisualizationScreen`) kept pushing `SongSelectScreen`, so the game
  showed the old flat list long after the new screen was done. Flip the
  callers in the same change that declares a replacement screen ready.
- **`preferredSetPath` was silently dropped.** The new screen took it in its
  constructor, stored it in a field, and never used it — so a freshly
  generated map no longer arrived highlighted. `VinylCarousel.SetEntries`
  now takes an optional `preferSelecting` matcher, applied only when nothing
  is selected yet, so it can't yank the highlight away from a song the
  player is actively browsing.


**Goal:** search (step 4) filters the wheel's (step 7) song set live,
selection changes refresh the info panel (step 3), the background (step 2),
and the difficulty column (step 5) together, and confirming a selection
still pushes `PlayerScreen` exactly as it does today.

- This is integration, not new UI — if steps 0–9 were each verified in
  isolation, this step is mostly deleting placeholder stubs and connecting
  events that already exist.

## Step 11 — Visual QA, then retire the old carousel ✅

`TestSceneVinylCarousel` covers the empty library, a single song, wrapping
in both directions, selection surviving a filter, confirming, and the
preferred-set hand-off. The screen itself was checked by screenshot at
720p, 900p and 1080p.

*As built — what the retirement actually required.* Three things had to be
relocated before the old files could go, because they were domain logic
that had accreted onto UI classes:

- `BeatmapSelection` (used by `PlayerScreen` and three gameplay test
  scenes) → its own file.
- `BeatmapCarousel.SetNameOf` → `BeatmapLibrary`, which is what it was
  always about: how the library names a set. Its two naming tests moved to
  `BeatmapLibraryTests` — they document a bug that cost a full end-to-end
  run to find, so they outlive the class they were written against.
- `DifficultyIcon.Density` / `.CountObjects` → `Beatmaps/BeatmapStatistics.cs`.
  `ColourFor` and `DescribeDifficulty` were dropped, superseded by
  `RetroPalette` and the cassette buttons.

Then deleted: `BeatmapCarousel.cs`, `DifficultyIcon.cs`,
`SongSelectScreen.cs`, the step-6 throwaway `WedgeSpike.cs`, and the three
test scenes that drove them (`TestSceneBeatmapCarousel`,
`TestSceneSongSelect`, `TestSceneGeneratedSetHighlight`).

*Search-bar and single-result bugs, found together.* Searching down to one
match turned out to expose three separate things:

- **The selected wedge's lift has to scale with how many songs there are.**
  Raising a slice reads as *a slice* being raised only while slices are
  narrow. At one result the slice **is** the disc, so "lift the selection"
  became "grow the whole record", pushing it past its own rim and over the
  platter, strobe ring and beat glow — all of which are positioned against
  that rim. The lift now eases off below five songs and is gone at one.
- **A parent's `LoadComplete` runs before its children's.** `CassetteSearchBar`
  subscribed to its textbox in its own `LoadComplete`, so a query set by the
  screen during *its* `LoadComplete` was assigned before anything was
  listening: the box displayed a search the wheel never heard about. It now
  pushes any non-empty value through once on load. Same root cause made
  `Focus()` silently do nothing, since there's no focus manager to reach
  before the drawable is in the tree — it schedules a frame now.
- **`BasicCaret` cannot be recoloured.** It repaints itself `Color4.White`
  on every move, so a colour set on it survives until the next keystroke —
  and white on this screen's cream paper sticker is invisible regardless.
  Replaced with a caret written out in full.

*Layout bug worth remembering.* The cassette column originally handled
"too many difficulties to fit" by sliding itself up, floored at the window
margin — which is *above* the info panel, so on a 720p window the Easy
cassette sat on top of the stat row. A column boxed in on both sides has no
room to move; it has to shrink in place. It now scales down between the
info panel's bottom edge and the search bar, and never moves.

---

## Art & asset needs

**Short answer: essentially none.** This screen is ambitious, but the
codebase's own precedent (`DrawableSpinner`, `HealthBar`, `GlowBar`,
`BeatBorderFlash`, `RetroText`) is to build every retro/neon effect
procedurally rather than import art, and that approach covers this screen
too:

| Element | Needs new art? | How it's actually built |
|---|---|---|
| Song artwork on wedges & popped-out slice | No | Already-loaded per-set background images (step 0), no new assets |
| Vinyl grooves, hub, tonearm | No | Concentric `CircularProgress`/`Circle` outlines, rotated rectangles |
| Cassette button shell, reels, spokes | No | Rounded `Box`/`Circle` composition, same technique as existing HUD bars |
| Retro display font | No — already have it | `Audiowide-Regular.ttf` + `PressStart2P-Regular.ttf` are already embedded and wired through `RetroText` |
| Background grade/scanlines | No | Gradient `Box` + blur, optional tiny tiling scanline texture generated once in code |
| Neon glow on panels/buttons | No | `EdgeEffect`, same as `GlowBar`/`HealthBar` today |

The one thing worth a deliberate look-and-feel check rather than assuming
it'll work: whether Audiowide/Press Start 2P actually *read* as "vinyl &
cassette 80s" once real content is on screen (step 3), vs. the more generic
"arcade/synthwave" they were originally chosen for in the gameplay HUD. If
they don't land, swapping in one additional OFL-licensed retro/VHS-style
font (e.g. something in the "Monoton"/"VT323" family) is a same-day change —
`RetroText` already supports adding a third `RetroFontFamily` entry — not a
blocking dependency for this plan.

If, once steps 0–9 are actually on screen, the procedural cassette shell or
vinyl label still reads as flat/vector-ish rather than "physical object,"
the fallback isn't hand-drawn sprites — it's souping up the existing
procedural version (subtle noise texture generated once via `ImageSharp`
the same way `RetroText` rasterizes glyphs, sharper `EdgeEffect` shadows) so
the whole game stays asset-free and consistent, rather than opening a
one-off art pipeline for a single screen.

## As built — what steps 0-8 actually taught us

Things that were not in the plan and cost real time, or that changed the
design once it was on screen:

- **A screenshot harness came first, and paid for itself immediately.**
  `dotnet run --project frontend/OsuClient.Tests -- --screenshot <scene>
  <out.png> [delayMs] [w] [h]` renders one named scene to a PNG and exits
  (`Visual/ScreenshotHarness.cs`, scenes registered in its `Scenes` map).
  Nearly every step below was found by looking at its output, not by
  reasoning about the code.
- **`RelativeSizeAxes` + an absolute `Size` is a silent catastrophe.** The
  hub was `RelativeSizeAxes = Both` and got `Size = diameter * 0.085` in
  `Update`, which osu.Framework reads as a *multiplier* — a ~365,000px
  drawable. The symptom was not a huge hub: it was the entire record, the
  grooves, and the blurred background all rendering nothing at all, with no
  exception and a clean log. If a chunk of a screen is inexplicably absent,
  check for this before anything else.
- **`CircularProgress.Texture` exists and polar-maps as hoped** — step 6's
  central bet paid off. But the accent tint has to be a *separate* layer:
  `Colour` multiplies into the texture, so tinting the textured shape
  directly crushes a cover to a monochrome silhouette. `VinylWedge` is
  three stacked slices — art, accent wash, dim — which is also what makes
  "selected shows its art, the rest are accent-tinted" a two-number change.
- **Wedges are an annulus, not a full pie** (`VinylWedge.InnerRadius`,
  0.62). Slices that run to the hub stretch their texture from nothing to
  full width and smear the art past recognising. Keeping them in an outer
  band fixed that and made the disc read as a record — label in the middle,
  grooves outside — at the same time.
- **Wedge colour had to stop coming from the difficulties.** The plan said
  a set's wedge should take its hardest difficulty's ramp colour, tying the
  wheel to the cassette column. In practice the backend emits the same five
  tiers for every song, so that rule painted all fourteen wedges violet.
  They now cycle the ramp by position, which is what the concept art
  actually shows. The palette agreement that survived — cassette column ↔
  info panel — is the one a player can see.
- **Difficulty order needed a third rule** (`DifficultyTier`). Filename
  order puts Expert second; density order printed "Easy, Hard, Normal" for
  a real set whose Hard has fewer objects than its Normal. Ordering by the
  tier *name*, with density only breaking ties, is what matches what the
  player is reading.
- **Screenshotting and then testing corrupts the test host.** The
  screenshot harness runs via `dotnet run` out of the *same*
  `OsuClient.Tests/bin` that `dotnet test` launches its test host from.
  Interleaving the two leaves host resolution broken: the run aborts with
  "A fatal error was encountered. The library 'hostpolicy.dll' ... Failed
  to run as a self-contained app", and vstest reports the remaining ~60
  tests as failures. It is not a real failure and not GPU contention — two
  earlier guesses that were both wrong.

  Tell them apart by *duration*: a healthy run is ~15s, a corrupted one
  ~70s. The fix is a rebuild that actually writes output — note that a
  plain `dotnet build` right after screenshots is a no-op and does **not**
  clear it, so `rm -rf OsuClient.Tests/bin OsuClient.Tests/obj` then build.
  Take screenshots after running tests, not before, to avoid it entirely.
- **Never animate a drawable you only *requested*; animate the one you
  *showed*.** `CarouselBackground` crashed in real use with
  `InvalidThreadForMutationException` ("cannot mutate the Transforms on a
  Loading Drawable") because it faded out whatever had been requested
  before the current layer. Turning the wheel quickly supersedes a layer
  while it is still loading and has never entered the tree, and a `Loading`
  drawable cannot be mutated from the update thread. Fixed by splitting
  "newest request" (`pendingLayer`) from "what is on screen"
  (`displayedLayer`), and only ever animating the latter — which makes the
  bad state unreachable instead of unlikely.

  Worth knowing: this class of bug resists unit testing. Blank test images
  decode so fast that the superseded layer reaches `Ready` rather than
  staying `Loading`, so `TestSceneCarouselBackground` exercises the shape
  but does not reproduce the crash. An update-thread exception *does* fail
  a `TestScene` (verified separately), so the limitation is the timing
  window, not the harness.

- **Wedge alignment: `Scale` pivots about `Origin`.** `VinylWedge` filled
  the disc but kept the default `TopLeft` origin, and the selected wedge is
  scaled up — so growing it walked its arc centre tens of pixels off the
  hub, and only the highlighted slice looked misaligned. Centre the drawable
  whenever you scale or rotate it about the disc.

- **Separator gaps belong on both sides of a wedge.** The gap was subtracted
  from the end of each slice, so every drawn wedge sat half a gap off the
  angle the ring rotated to, and the elevated wedge's outward nudge pointed
  somewhere different again. Splitting the gap either side makes the drawn
  slice centred in its slot, which is what every other calculation assumes.

- **Auto-sized panels pinned by the wrong edge drift.** The selected-song
  card was `Origin = CentreRight` and auto-sized, so a longer title pushed
  the cover thumbnail left — measured at 29px of movement between two
  songs, which read as the wheel landing inconsistently. Pinned by the left
  edge, the thumbnail is now pixel-identical across selections (verified by
  locating the card border in both frames).

- **`CoverArt.Load` returns a fresh texture every call.** The selected-song
  card was overwriting `Sprite.Texture` without disposing the old one,
  leaking a cover's worth of video memory per turn of the wheel. Still
  now cached by path and resolution, so repeat lookups are free and the
  cache owns the textures (callers must not dispose them).

- **Turning the wheel was doing far too much per notch.** Each step ran a
  synchronous JPEG decode, ~24 `RetroText` rasterizations (each uploads a
  texture), and rebuilt five cassette buttons — all on the update thread,
  which is what made rotation feel like it was chugging. Now the wheel turns
  freely and the expensive panel work is debounced by 140ms until it
  settles; the first selection still applies immediately so the screen is
  never blank on entry.

- **A running game locks the build.** `dotnet build OsuClient.sln` fails
  with MSB3027 while `OsuClient.Desktop` is open, because it can't
  overwrite `OsuClient.Game.dll`. Building
  `OsuClient.Tests/OsuClient.Tests.csproj` compiles the same game code
  without touching the desktop project's output, which is enough for
  screenshots and tests.

## Notes from the codebase, found while planning

Things that aren't obvious from a directory listing and cost real time to
rediscover:

- **`EdgeEffect` can't glow an arc.** It renders the container's *masking
  shape* — for a plain container, its bounding rectangle. `DrawableSpinner`
  already hit this and documents it (`Screens/Gameplay/DrawableSpinner.cs`,
  `SideMeter` remarks): glow around anything wedge- or arc-shaped has to be
  a `BufferedContainer` blur of the shape itself, with padding around it for
  the falloff to fade into. `EdgeEffect` is still fine for the rectangular
  left-side panels (steps 3–5).
- **`Anchor`/`Origin = Centre` is load-bearing on a rotating
  `CircularProgress`**, not decoration — `Rotation` pivots about the
  drawable's `Origin`, and the default `TopLeft` swings the whole arc out of
  place instead of turning it where it sits.
- **The real library is artwork-rich already.** `data/output/` holds 14
  generated sets and nearly every one has a hand-dropped cover image, so the
  artwork-driven parts of this design have real data to develop against from
  day one — no need to fake it with placeholders.
- **`.osz` sets can't resolve a background**, only unpacked folders can
  (`OszImporter.LoadFromDirectory` scans for an image; the stream-reading
  `ReadSet` path has no disk to point at). Currently moot — the backend's
  output is all folders, zero `.osz` — so step 0 just exposes what exists
  rather than building zip-extraction plumbing that Phase 8's media work
  would redo properly anyway.
