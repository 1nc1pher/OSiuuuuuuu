using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Framework.Threading;
using OsuClient.Game.Beatmaps;
using OsuClient.Game.Graphics;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.SongSelect
{
    /// <summary>
    /// The record itself: one wedge per song, turning under a fixed selection
    /// point (CAROUSEL_REDESIGN_PLAN.md steps 7-9).
    ///
    /// Fills a container that is sized to the disc's diameter and centred on
    /// the hub, so everything here can work in "angles about my own centre"
    /// and the screen decides where that centre sits — just off the right
    /// edge, which is what leaves a half-record showing.
    ///
    /// The whole ring rotates rather than the selection moving around it: the
    /// highlighted song is always at 9 o'clock, the part of the disc furthest
    /// into the screen and least likely to be clipped.
    /// </summary>
    public partial class VinylCarousel : CompositeDrawable
    {
        /// <summary>Screen angle the selected wedge sits at, clockwise from 12.</summary>
        private const float selection_angle = 270;

        /// <summary>Gap between wedges, in degrees, so slice edges read.</summary>
        private const float wedge_gap = 1.2f;

        private const float selected_scale = 1.055f;
        private const float selected_nudge = 26;

        /// <summary>Hub radius as a fraction of the disc's.</summary>
        private const float hub_fraction = 0.085f;

        private const double snap_duration = 420;

        /// <summary>
        /// How long the record takes to turn once. 1800ms is a real 33⅓ RPM,
        /// which is the speed the deck's own lit "33" pill claims — the two
        /// agree on purpose. Everything that turns here turns at this speed,
        /// because on a real deck every one of these things is the same
        /// rotating object seen a different way.
        /// </summary>
        private const double revolution_duration = 1800;

        /// <summary>
        /// How long the wheel sits still before the selected-song card
        /// refreshes — see <see cref="updateSelectionCard"/>.
        /// </summary>
        private const double settle_delay = 140;

        /// <summary>Fired when the highlighted song changes; null when the wheel empties.</summary>
        public Action<BeatmapLibraryEntry?>? SelectionChanged;

        /// <summary>Fired when the highlighted song is confirmed (clicked again).</summary>
        public Action<BeatmapLibraryEntry>? SelectionConfirmed;

        private readonly Container ring;
        private readonly CircularContainer sheen;
        private readonly Container sheenBand;
        private readonly Container surface;
        private readonly Container label;
        private readonly Container hub;
        private readonly Container selectionCard;
        private readonly Container selectionArtFrame;
        private readonly Sprite selectionArt;
        private readonly Tonearm tonearm;

        private readonly List<VinylWedge> wedges = new List<VinylWedge>();
        private IReadOnlyList<BeatmapLibraryEntry> entries = Array.Empty<BeatmapLibraryEntry>();

        private int selectedIndex;
        private float sweep = 360;

        private ScheduledDelegate? pendingCardUpdate;

        [Resolved]
        private IRenderer renderer { get; set; } = null!;

        /// <summary>The highlighted song, or null when the wheel is empty.</summary>
        public BeatmapLibraryEntry? Selection =>
            selectedIndex >= 0 && selectedIndex < entries.Count ? entries[selectedIndex] : null;

        /// <summary>
        /// The selected wedge's accent colour — the same one its glow border
        /// uses. Exposed so other deck furniture (the turntable's own
        /// beat-synced edge light) can match it instead of guessing at a
        /// second copy of the ramp logic.
        /// </summary>
        public Color4 SelectionAccent => accentFor(Math.Max(selectedIndex, 0));

        public VinylCarousel()
        {
            RelativeSizeAxes = Axes.Both;

            InternalChildren = new Drawable[]
            {
                new Circle
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Colour = RetroPalette.Vinyl,
                },
                ring = new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                },
                grooves(),
                surface = surfaceHighlights(),
                sheen = new CircularContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Masking = true,
                    Child = sheenBand = new Container
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        RelativeSizeAxes = Axes.Both,
                        Children = new Drawable[]
                        {
                            sheenHalf(leading: true),
                            sheenHalf(leading: false),
                        },
                    },
                },
                hub = new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    // Relative, so this is a fraction of the disc — assigning
                    // an absolute pixel size here would be read as a
                    // multiplier and blow the hub up past the window.
                    Size = new Vector2(hub_fraction),
                    Children = new Drawable[]
                    {
                        new Circle
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            RelativeSizeAxes = Axes.Both,
                            Colour = RetroPalette.ChromeDark,
                        },
                        new Circle
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            RelativeSizeAxes = Axes.Both,
                            Size = new Vector2(0.82f),
                            Colour = RetroPalette.Chrome,
                        },
                        // Print marks on the label. A label is a circle, so
                        // without something off-centre printed on it the hub
                        // turns completely invisibly — and the hub is the one
                        // part of a real record your eye actually tracks the
                        // rotation by.
                        label = new Container
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            RelativeSizeAxes = Axes.Both,
                            Children = new Drawable[]
                            {
                                labelMark(0),
                                labelMark(120),
                                labelMark(240),
                            },
                        },
                        new Circle
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            RelativeSizeAxes = Axes.Both,
                            Size = new Vector2(0.17f),
                            Colour = RetroPalette.Void,
                        },
                    },
                },
                selectionCard = new Container
                {
                    Anchor = Anchor.Centre,
                    // Pinned by its left edge rather than centred: a centred
                    // origin on an auto-sized container would still be exact
                    // here (there's only ever the one child now), but left
                    // keeps this consistent with how every other anchor in
                    // this class is expressed relative to the hub.
                    Origin = Anchor.CentreLeft,
                    AutoSizeAxes = Axes.Both,
                    Alpha = 0,
                    // Song name and artist already live in the top-left info
                    // panel (SongInfoPanel) — repeating them here just
                    // crowded the one spot on the wheel that's supposed to be
                    // all artwork. A glow in the song's own accent colour
                    // replaces the plain white border, tying this frame back
                    // to the ring the way the border glow ties the selected
                    // cassette to its wedge.
                    Child = selectionArtFrame = new Container
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        // A wedge is an annular sector: constant angular width
                        // but a chord that widens with radius, so its most
                        // spacious point is out near the rim, not near the
                        // hub. Pushed left to land there, dead on the wedge's
                        // bisector (Y=0, same latitude as the hub) rather than
                        // nudged off it — that bisector *is* the wedge's
                        // widest line, so centring on it needs no vertical
                        // offset once the frame sits out at the rim.
                        Position = new Vector2(-32, 0),
                        Size = new Vector2(148),
                        Masking = true,
                        CornerRadius = 6,
                        BorderThickness = 3f,
                        Children = new Drawable[]
                        {
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = RetroPalette.PanelInset,
                            },
                            selectionArt = new Sprite
                            {
                                RelativeSizeAxes = Axes.Both,
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                FillMode = FillMode.Fill,
                            },
                        },
                    },
                },
                tonearm = new Tonearm(),
            };
        }

        /// <summary>
        /// Half of the light band that sweeps the record, fading out from the
        /// centre line. Two mirrored halves make a soft band; a single
        /// gradient only has two stops and would read as a hard edge on one
        /// side.
        /// </summary>
        private static Drawable sheenHalf(bool leading)
        {
            var bright = Color4.White.Opacity(0.09f);
            var clear = Color4.White.Opacity(0);

            return new Box
            {
                Anchor = Anchor.Centre,
                Origin = leading ? Anchor.CentreLeft : Anchor.CentreRight,
                RelativeSizeAxes = Axes.Both,
                Size = new Vector2(0.22f, 1.5f),
                Colour = leading
                    ? ColourInfo.GradientHorizontal(bright, clear)
                    : ColourInfo.GradientHorizontal(clear, bright),
            };
        }

        /// <summary>
        /// Light catching the grooves: short, faint arcs scattered across the
        /// record at different radii, which turn with it.
        ///
        /// This is what actually makes the disc read as spinning, and the
        /// reason it has to exist at all is that everything else on the
        /// record is rotationally symmetric — the disc, the grooves and the
        /// hub are all circles, so turning them changes not one pixel. The
        /// wedges can't be turned either (the selection is pinned to 9
        /// o'clock, see the class remarks), so the motion has to come from
        /// something off-centre that no other part of the design depends on
        /// the position of.
        ///
        /// Additive and very faint, so they read as light moving across a
        /// surface rather than as marks painted on one.
        /// </summary>
        private static Container surfaceHighlights()
        {
            // radius (in disc radii), arc length, where it starts, how bright,
            // how thick. Deliberately irregular: evenly spaced highlights
            // read as a mechanism, scattered ones as a reflection.
            //
            // The bare vinyl between the hub and the wedges is where most of
            // these live and where they're brightest. It's a wide, near-black
            // band — the one part of the record with nothing else competing
            // for it — so it's both the easiest place to see motion and the
            // place a faint highlight is worth the most. The two out over the
            // artwork are dimmer on purpose: the same light falling on a
            // surface that's already bright barely shows, and pushing them
            // until it did would wash out the covers.
            (float radius, float sweep, float angle, float alpha, float thickness)[] arcs =
            {
                (0.92f, 26, 150, 0.10f, 0.022f),
                (0.78f, 20, 320, 0.09f, 0.018f),
                (0.58f, 34, 5, 0.17f, 0.016f),
                (0.55f, 14, 130, 0.12f, 0.011f),
                (0.50f, 22, 240, 0.15f, 0.013f),
                (0.44f, 40, 190, 0.16f, 0.014f),
                (0.40f, 12, 30, 0.11f, 0.009f),
                (0.34f, 18, 285, 0.14f, 0.012f),
                (0.29f, 28, 80, 0.13f, 0.010f),
                (0.24f, 16, 200, 0.12f, 0.009f),
            };

            var container = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
            };

            foreach (var (radius, sweep, angle, alpha, thickness) in arcs)
            {
                container.Add(new CircularProgress
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Size = new Vector2(radius),
                    Progress = sweep / 360.0,
                    // InnerRadius is a fraction of this shape's own radius,
                    // so dividing keeps every arc the same thickness on
                    // screen regardless of which radius it sits at.
                    InnerRadius = thickness / radius,
                    Rotation = angle,
                    Blending = BlendingParameters.Additive,
                    Colour = Color4.White.Opacity(alpha),
                });
            }

            return container;
        }

        /// <summary>One print mark on the record's label — see <see cref="label"/>.</summary>
        private static Drawable labelMark(float angle)
        {
            float radians = MathHelper.DegreesToRadians(angle);

            return new Box
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                RelativePositionAxes = Axes.Both,
                Size = new Vector2(0.055f, 0.17f),
                Position = new Vector2(MathF.Sin(radians), -MathF.Cos(radians)) * 0.29f,
                Rotation = angle,
                Colour = RetroPalette.Void.Opacity(0.5f),
            };
        }

        /// <summary>
        /// Faint concentric rings across the whole disc. Drawn once, above the
        /// wedges, so a slice's art reads as pressed into the record rather
        /// than pasted on top of it.
        /// </summary>
        private static Drawable grooves()
        {
            var container = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
            };

            for (int i = 0; i < 26; i++)
            {
                float size = 0.20f + i * 0.031f;

                container.Add(new CircularProgress
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Size = new Vector2(size),
                    Progress = 1,
                    InnerRadius = 0.006f / size,
                    Colour = Color4.Black.Opacity(i % 4 == 0 ? 0.22f : 0.12f),
                });
            }

            return container;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // The wedges themselves still can't turn — the selected song is
            // pinned to 9 o'clock by design, so rotating them would drag the
            // selection out from under the card and the tonearm. Everything
            // that *can* turn does, together and at the record's real speed:
            // the surface highlights, the label's print marks, and the broad
            // sheen. On a real deck these are all the same spinning object,
            // so anything less than one shared speed reads as several things
            // sliding over each other.
            spinForever(surface);
            spinForever(label);
            spinForever(sheenBand);
        }

        /// <summary>
        /// Turns <paramref name="drawable"/> at the record's speed, forever.
        /// The explicit <c>RotateTo(0)</c> first is what makes the loop
        /// seamless — without a known starting angle the second transform has
        /// nothing fixed to repeat from.
        /// </summary>
        private static void spinForever(Drawable drawable) =>
            drawable.RotateTo(0)
                    .RotateTo(360, revolution_duration, Easing.None)
                    .Loop();

        protected override void Update()
        {
            base.Update();

            float diameter = DrawWidth;

            // The card sits along the 9 o'clock spoke: far enough out that the
            // thumbnail stays on the record, with the title block extending
            // back towards the hub, where there is room for any length.
            selectionCard.X = -diameter * 0.42f;

            tonearm.Size = new Vector2(diameter);
        }

        /// <summary>
        /// Replaces the wheel's contents. Keeps the current song selected when
        /// it survives the change — a search that narrows the list shouldn't
        /// throw away what the player was already looking at.
        ///
        /// <paramref name="preferSelecting"/> only matters the first time
        /// entries are ever set (nothing is selected yet to preserve) — how a
        /// freshly generated map arrives already highlighted instead of
        /// leaving the player to hunt for it, same as the old carousel's
        /// <c>preferredSetPath</c> did.
        /// </summary>
        public void SetEntries(IReadOnlyList<BeatmapLibraryEntry> newEntries,
                               Func<BeatmapLibraryEntry, bool>? preferSelecting = null)
        {
            var previous = Selection;

            entries = newEntries;

            ring.Clear();
            wedges.Clear();

            if (entries.Count == 0)
            {
                selectedIndex = -1;
                selectionCard.FadeOut(160, Easing.OutQuint);
                SelectionChanged?.Invoke(null);
                return;
            }

            // Equal slices, so the record is always a complete disc. With a
            // fixed slice width instead, a short library would leave a wedge
            // of empty space and stop reading as a record at all.
            sweep = 360f / entries.Count;

            for (int i = 0; i < entries.Count; i++)
            {
                float drawnSweep = Math.Max(sweep - wedge_gap, sweep * 0.5f);

                var wedge = new VinylWedge(drawnSweep, accentFor(i), entries[i].BackgroundPath)
                {
                    // Half the gap sits either side, so the drawn slice is
                    // centred in its slot. With the whole gap trailing each
                    // wedge instead, every slice sat half a gap off the angle
                    // rotationFor aims at — the selected one never quite lined
                    // up with the selection point, and its outward nudge
                    // pointed somewhere slightly different again.
                    Angle = i * sweep + (sweep - drawnSweep) / 2,
                };

                wedges.Add(wedge);
                ring.Add(wedge);
            }

            int restored;

            if (previous != null)
                restored = Math.Max(entries.ToList().FindIndex(e => ReferenceEquals(e, previous)), 0);
            else if (preferSelecting != null)
                restored = Math.Max(entries.ToList().FindIndex(e => preferSelecting(e)), 0);
            else
                restored = 0;

            selectedIndex = restored;

            ring.Rotation = rotationFor(selectedIndex);
            applySelectionVisuals(0);
            notifySelection();
        }

        /// <summary>
        /// Wedge colours cycle around the ramp by position, which is what
        /// makes the record read as the multicoloured wheel the concept art
        /// shows.
        ///
        /// Deriving the colour from the set's difficulties instead — the
        /// original plan — turns out to make every wedge identical: the
        /// backend generates the same five tiers for every song, so "colour of
        /// the hardest difficulty" is violet, every time. The cassette column
        /// and info panel still share one palette with each other, where the
        /// agreement is actually visible.
        /// </summary>
        private static Color4 accentFor(int index) =>
            RetroPalette.DifficultyRamp[index % RetroPalette.DifficultyRamp.Length];

        /// <summary>Ring rotation that puts wedge <paramref name="index"/> under the selection point.</summary>
        private float rotationFor(int index) => selection_angle - (index * sweep + sweep / 2);

        /// <summary>Moves the highlight by <paramref name="delta"/> slices, wrapping.</summary>
        public void SelectRelative(int delta)
        {
            if (entries.Count == 0)
                return;

            int next = selectedIndex + delta;

            // Wrapping is the point of a wheel — running off the end of a
            // record's worth of songs and stopping would feel broken.
            next = ((next % entries.Count) + entries.Count) % entries.Count;

            SelectIndex(next);
        }

        /// <summary>Highlights a song by index, turning the record to it.</summary>
        public void SelectIndex(int index)
        {
            if (entries.Count == 0 || index == selectedIndex)
                return;

            selectedIndex = index;

            animateRingTo(rotationFor(index));
            applySelectionVisuals(snap_duration);
            notifySelection();
        }

        /// <summary>
        /// Turns to a target rotation the short way round. Without unwrapping
        /// against the current value, stepping from the last slice to the
        /// first spins the record all the way backwards.
        /// </summary>
        private void animateRingTo(float target)
        {
            float current = ring.Rotation;
            float delta = ((target - current + 180) % 360 + 360) % 360 - 180;

            ring.RotateTo(current + delta, snap_duration, Easing.OutQuint);
            tonearm.PointAt(selectedIndex, entries.Count);
        }

        private void applySelectionVisuals(double duration)
        {
            float lift = liftStrength();

            for (int i = 0; i < wedges.Count; i++)
            {
                bool isSelected = i == selectedIndex;

                wedges[i].SetTint(isSelected ? 0.10f : 0.52f, isSelected ? 0f : 0.34f, duration);
                wedges[i].SetElevation(isSelected ? 1 + (selected_scale - 1) * lift : 1f,
                    isSelected ? selected_nudge * lift : 0f, duration);
            }

            updateSelectionCard(duration);
        }

        /// <summary>
        /// How much of the selected wedge's lift to actually apply, 0 to 1.
        ///
        /// Raising a slice only reads as *a slice* being raised while slices
        /// are narrow. A search that narrows the wheel to a handful makes
        /// each one an enormous fraction of the disc, and at a single result
        /// the slice simply *is* the disc — so "lift the selection" becomes
        /// "grow the whole record", pushing it out past its own rim and over
        /// the platter, the strobe ring and the beat glow, all of which are
        /// positioned against that rim. Hence: full lift once there are
        /// enough songs for a slice to be a slice, easing off to none at one.
        /// </summary>
        private float liftStrength() => Math.Clamp((entries.Count - 1) / 4f, 0, 1);

        private void updateSelectionCard(double duration)
        {
            pendingCardUpdate?.Cancel();

            var entry = Selection;

            if (entry == null)
            {
                selectionCard.FadeOut(duration * 0.5, Easing.OutQuint);
                return;
            }

            // Captured now rather than inside the delayed callback: by the
            // time it runs, selectedIndex may have moved on again, and the
            // glow has to match the song the art belongs to, not whatever's
            // highlighted when the delay expires.
            Color4 accent = accentFor(selectedIndex);

            // Decoding the art is a real image load, so a fast spin would do
            // it once per notch. Let the wheel turn and update the card when
            // it stops; duration 0 means "no animation", i.e. the initial
            // population, which has to land immediately.
            if (duration == 0)
                applySelectionCard(entry, accent, duration);
            else
                pendingCardUpdate = Scheduler.AddDelayed(() => applySelectionCard(entry, accent, duration), settle_delay);
        }

        private void applySelectionCard(BeatmapLibraryEntry entry, Color4 accent, double duration)
        {
            // Same resolution the wedges ask for, so the card is a cache hit
            // on art that is already decoded. Textures are owned by the cache
            // and shared — the one being replaced must not be disposed.
            selectionArt.Texture = CoverArt.Load(renderer, entry.BackgroundPath, VinylWedge.ArtResolution);
            selectionArt.Alpha = selectionArt.Texture == null ? 0 : 1;

            selectionArtFrame.BorderColour = accent;
            selectionArtFrame.EdgeEffect = new EdgeEffectParameters
            {
                Type = EdgeEffectType.Glow,
                Colour = accent.Opacity(0.65f),
                Radius = 20,
            };

            selectionCard.FadeIn(duration == 0 ? 0 : 200, Easing.OutQuint);
        }

        private void notifySelection() => SelectionChanged?.Invoke(Selection);

        /// <summary>Confirms the current selection.</summary>
        public void ConfirmSelection()
        {
            if (Selection != null)
                SelectionConfirmed?.Invoke(Selection);
        }

        // ------------------------------------------------------------------
        // Input
        // ------------------------------------------------------------------

        /// <summary>
        /// Accumulated scroll, in slices. Scroll wheels report in notches and
        /// trackpads in fractions, so this carries the remainder rather than
        /// rounding each event away.
        /// </summary>
        private float scrollAccumulator;

        protected override bool OnScroll(ScrollEvent e)
        {
            if (entries.Count == 0)
                return false;

            scrollAccumulator -= e.ScrollDelta.Y;

            int steps = (int)scrollAccumulator;

            if (steps != 0)
            {
                scrollAccumulator -= steps;
                SelectRelative(steps);
            }

            return true;
        }

        protected override bool OnClick(ClickEvent e)
        {
            if (entries.Count == 0)
                return false;

            int index = wedgeAt(e.MousePosition);

            if (index < 0)
                return false;

            if (index == selectedIndex)
                ConfirmSelection();
            else
                SelectIndex(index);

            return true;
        }

        /// <summary>
        /// Which wedge covers a point, or -1 for the hub and everything
        /// outside the rim.
        ///
        /// Hit-testing by angle here rather than per-wedge: the wedges are
        /// <see cref="CircularProgress"/> shapes whose input area is their
        /// whole bounding square, so every one of them would claim clicks
        /// across the entire disc.
        /// </summary>
        private int wedgeAt(Vector2 localPosition)
        {
            var centre = DrawSize / 2;
            var offset = localPosition - centre;

            float radius = offset.Length;
            float outer = DrawWidth / 2;

            // Only the band the wedges actually occupy is clickable — the
            // label area inside them belongs to the hub and the song card.
            if (radius > outer || radius < outer * (1 - VinylWedge.InnerRadius))
                return -1;

            // atan2 gives counter-clockwise from +x; wedges are laid out
            // clockwise from 12 o'clock.
            float screenAngle = MathHelper.RadiansToDegrees(MathF.Atan2(offset.Y, offset.X)) + 90;

            float baseAngle = ((screenAngle - ring.Rotation) % 360 + 360) % 360;

            int index = (int)(baseAngle / sweep);

            return Math.Clamp(index, 0, entries.Count - 1);
        }
    }
}
