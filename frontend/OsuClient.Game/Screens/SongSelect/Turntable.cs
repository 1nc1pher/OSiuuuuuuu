using System;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using OsuClient.Game.Beatmaps;
using OsuClient.Game.Graphics;
using OsuClient.Game.Screens.Gameplay;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.SongSelect
{
    /// <summary>
    /// The deck the record sits on: plinth, platter, strobe ring, the arm's
    /// base and a small control cluster.
    ///
    /// Drawn behind <see cref="VinylCarousel"/> and sized to exactly the same
    /// square, so everything here can be expressed as a multiple of the disc's
    /// own size — a platter at 1.075 is a platter that reaches 7.5% of the
    /// radius past the record's rim. Nothing masks, so the pieces that reach
    /// past that square (all of them) render fine.
    ///
    /// Only a crescent of any of this is ever on screen: the hub sits off the
    /// right edge by design, so the deck is really a band around the visible
    /// left rim. That's what the detailing is chosen for — a platter edge,
    /// strobe dots and an arm base read as "a record lying on a deck" from a
    /// sliver, where a plinth outline or a platter centre would need the whole
    /// thing visible to say anything at all.
    /// </summary>
    public partial class Turntable : CompositeDrawable
    {
        /// <summary>
        /// How far the deck reaches past the record's rim, as a fraction of
        /// the disc's radius.
        ///
        /// Public because the screen's left-hand column has to stop short of
        /// it: the panels were laid out against the disc's edge, and the deck
        /// now reaches further in than the disc does.
        /// </summary>
        public const float DeckOverhang = 0.17f;

        /// <summary>The platter, as a multiple of the disc's own size.</summary>
        private const float platter_size = 1.075f;

        /// <summary>The recess the platter sits in — a dark ring around it.</summary>
        private const float well_size = 1.115f;

        /// <summary>
        /// Where the strobe dots ring sits, as a fraction of the disc's size
        /// (so 0.5 would be exactly the record's rim).
        ///
        /// Outside the record's drop shadow and inside the platter's edge,
        /// which is a band about 35px wide at a 900p window — the reason the
        /// dots are as small as they are.
        /// </summary>
        private const float strobe_radius = 0.524f;

        private const int strobe_dots = 72;

        /// <summary>
        /// How long the strobe ring takes to drift round once.
        ///
        /// Nothing like the record's own 1800ms, and deliberately so. A
        /// strobe ring's whole purpose on a real deck is to sit *almost*
        /// still: it's lit by mains-frequency flicker, and at the correct
        /// speed the dots appear locked, creeping only as far as the platter
        /// is off. Spinning these at the platter's real speed would also
        /// alias badly — 72 dots 5° apart, moving 3.3° per frame, is a
        /// wagon-wheel waiting to happen.
        /// </summary>
        private const double strobe_drift_duration = 9000;

        /// <summary>
        /// Diameter the control cluster's absolute sizes were drawn against.
        /// It's scaled from this, because the band it sits in is a fraction of
        /// the disc and would otherwise be narrower than the controls on a
        /// small window.
        /// </summary>
        private const float reference_diameter = 1368;

        /// <summary>
        /// Where the deck's furniture sits across the strip left of the
        /// platter, as a fraction of the disc's size — the midpoint between
        /// the deck's outer edge and the platter's.
        /// </summary>
        private const float strip_centre = -((1 + DeckOverhang) + platter_size) / 4f;

        /// <summary>
        /// How sharply the glow narrows around the beat. Gameplay's own edge
        /// flash uses 4 — a brief spike, felt *at* the beat. This is a glow
        /// that breathes with the music rather than a flash, so it stays much
        /// rounder: a high value here is what makes a pulse read as a light
        /// being switched rather than one swelling.
        /// </summary>
        private const double pulse_sharpness = 1.8;

        /// <summary>
        /// Glow alpha between beats. Deliberately never zero: a glow that
        /// drops out entirely stops reading as a glow at all and becomes an
        /// outline blinking on and off, which is exactly what a pulse with no
        /// floor looked like.
        /// </summary>
        private const float glow_base_alpha = 0.24f;

        /// <summary>How much brighter the beat makes it, on top of the floor.</summary>
        private const float glow_pulse_alpha = 0.32f;

        /// <summary>
        /// How far the glow reaches past the record's rim, as a fraction of
        /// the disc's radius, at rest and on the beat. It swells as well as
        /// brightens — a halo that only changes opacity still reads as a
        /// light switching rather than one breathing.
        /// </summary>
        private const float glow_base_radius = 0.045f;

        private const float glow_peak_radius = 0.085f;

        private readonly Container deck;
        private readonly CircularContainer glow;
        private readonly Drawable strobe;
        private readonly Drawable controls;
        private readonly Drawable badge;

        public Turntable()
        {
            Anchor = Anchor.Centre;
            Origin = Anchor.Centre;
            RelativeSizeAxes = Axes.Both;

            InternalChildren = new Drawable[]
            {
                deck = new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    // Relative, so this is a multiple of the disc — an
                    // absolute size on a relatively-sized drawable is read as
                    // a multiplier and is the one mistake in this screen that
                    // renders nothing at all rather than something wrong.
                    Size = new Vector2(1 + DeckOverhang),
                    Masking = true,
                    BorderThickness = 3,
                    BorderColour = RetroPalette.ChromeDark.Opacity(0.9f),
                    EdgeEffect = new EdgeEffectParameters
                    {
                        Type = EdgeEffectType.Shadow,
                        Colour = new Color4(0f, 0f, 0f, 0.55f),
                        Radius = 40,
                    },
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = ColourInfo.GradientVertical(
                                new Color4(0.14f, 0.12f, 0.19f, 1f),
                                new Color4(0.05f, 0.04f, 0.08f, 1f)),
                        },
                        // The strip left of the platter is the only bare deck
                        // this layout ever shows, so the light falls there:
                        // without it the deck reads as a flat black rectangle
                        // rather than a surface.
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = ColourInfo.GradientHorizontal(
                                new Color4(0.42f, 0.40f, 0.52f, 0.30f),
                                new Color4(0.42f, 0.40f, 0.52f, 0f)),
                            Width = 0.14f,
                        },
                        // Front rail: the lit metal edge of the deck's body.
                        new Box
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            RelativeSizeAxes = Axes.Both,
                            Size = new Vector2(0.009f, 1f),
                            Colour = ColourInfo.GradientHorizontal(
                                RetroPalette.Chrome.Opacity(0.55f),
                                RetroPalette.ChromeDark.Opacity(0.25f)),
                        },
                    },
                },
                // The recess, showing as a dark ring between the platter's
                // edge and the deck surface.
                new Circle
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Size = new Vector2(well_size),
                    Colour = new Color4(0.025f, 0.02f, 0.04f, 1f),
                },
                new Circle
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Size = new Vector2(platter_size),
                    // Dark brushed aluminium, not bright chrome: the platter
                    // is a ring around the busiest thing on screen, and a
                    // light one reads as the subject rather than the surface
                    // the subject is resting on.
                    Colour = ColourInfo.GradientVertical(
                        new Color4(0.27f, 0.26f, 0.33f, 1f),
                        new Color4(0.12f, 0.11f, 0.16f, 1f)),
                },
                recordShadow(),
                strobe = strobeRing(),
                armBase(),
                // The beat glow, hugging the record's own rim.
                //
                // A masked CircularContainer, because EdgeEffect renders a
                // *blurred* copy of its container's masking shape — a real
                // radial falloff, which is what makes this a glow. The two
                // shapes this was tried as first both failed for the same
                // underlying reason, that they have edges rather than a
                // falloff: the deck's rounded rect lit its long straight
                // sides as a full-height bar across the window, and a thin
                // CircularProgress annulus is a hard-edged outline that can
                // only switch on and off. A circle is also the one shape the
                // codebase's "EdgeEffect can't glow an arc" caveat doesn't
                // apply to — it's a masking shape, not an arc.
                glow = new CircularContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    // Exactly the record's size, so the halo radiates out
                    // from its rim onto the platter. Edge effects draw behind
                    // their own content, and the record covers everything
                    // inside this, so only the part past the rim ever shows.
                    Size = new Vector2(1),
                    Masking = true,
                    Child = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0, AlwaysPresent = true },
                },
                controls = controlCluster(),
                badge = new RetroText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativePositionAxes = Axes.Both,
                    Position = new Vector2(strip_centre, 0.26f),
                    // Reading up the deck's front edge, where a deck of this
                    // era puts its model badge.
                    Rotation = -90,
                    Font = RetroFontFamily.Display,
                    TextSize = 11,
                    Colour = RetroPalette.Chrome.Opacity(0.4f),
                    Text = "DIRECT DRIVE",
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            strobe.RotateTo(0)
                  .RotateTo(360, strobe_drift_duration, Easing.None)
                  .Loop();
        }

        protected override void Update()
        {
            base.Update();

            deck.CornerRadius = DrawWidth * 0.06f;

            // The strip these sit in is a fraction of the disc, so they have
            // to be too — at a small window they'd otherwise be wider than
            // the deck they're screwed to.
            var scale = new Vector2(DrawWidth / reference_diameter);

            controls.Scale = scale;
            badge.Scale = scale;
        }

        /// <summary>
        /// Swells the glow around the record in time with the selected song,
        /// in that song's accent colour — the same phase/intensity/envelope
        /// maths <c>BeatBorderFlash</c> drives gameplay's own beat-synced
        /// light with, softened (see <see cref="pulse_sharpness"/>) into a
        /// glow that breathes rather than a flash that spikes.
        ///
        /// <paramref name="beatmap"/> null (nothing previewing yet, or the
        /// selection has no usable timing point) holds the glow at its
        /// resting floor rather than dropping it out, so the record keeps its
        /// accent either way.
        /// </summary>
        public void SetBeatFlash(Beatmap? beatmap, double time, Color4 accent)
        {
            float pulse = 0;

            if (beatmap != null)
            {
                double phase = BeatPulse.PhaseAt(beatmap, time);
                double intensity = BeatPulse.IntensityAt(phase, pulse_sharpness);
                double envelope = BeatPulse.PlayableEnvelope(beatmap, time);

                pulse = (float)(intensity * envelope);
            }

            glow.EdgeEffect = new EdgeEffectParameters
            {
                Type = EdgeEffectType.Glow,
                Colour = accent.Opacity(glow_base_alpha + glow_pulse_alpha * pulse),
                // Absolute pixels, so it's taken off the disc's own radius to
                // keep the same reach at any window size.
                Radius = DrawWidth / 2 * (glow_base_radius + (glow_peak_radius - glow_base_radius) * pulse),
            };
        }

        /// <summary>
        /// The record's shadow on the platter, as rings that darken towards
        /// the rim. Stacked largest-first at low alpha, so they accumulate
        /// inwards into a falloff without needing a blur — and the record
        /// itself, drawn above this, covers everything inside its own edge.
        /// </summary>
        private static Drawable recordShadow()
        {
            float[] sizes = { 1.024f, 1.017f, 1.010f, 1.004f };
            float[] alphas = { 0.12f, 0.18f, 0.24f, 0.34f };

            var container = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
            };

            for (int i = 0; i < sizes.Length; i++)
            {
                container.Add(new Circle
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Size = new Vector2(sizes[i]),
                    Colour = Color4.Black.Opacity(alphas[i]),
                });
            }

            return container;
        }

        /// <summary>
        /// The strobe dots around the platter's edge — the detail that says
        /// "turntable" faster than anything else on the deck, and one that
        /// happens to live exactly on the band this screen has room for.
        ///
        /// Sized and positioned relatively so the ring needs no per-frame
        /// work: 72 dots that each re-measured themselves every update would
        /// be 72 pointless transforms a frame.
        /// </summary>
        private static Drawable strobeRing()
        {
            var ring = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
            };

            for (int i = 0; i < strobe_dots; i++)
            {
                float angle = i / (float)strobe_dots * MathF.PI * 2;

                ring.Add(new Circle
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    RelativePositionAxes = Axes.Both,
                    Size = new Vector2(0.0062f),
                    Position = new Vector2(MathF.Sin(angle), -MathF.Cos(angle)) * strobe_radius,
                    // Every sixth dot brighter, so the ring reads as marked
                    // out rather than as a dotted line.
                    Colour = RetroPalette.Amber.Opacity(i % 6 == 0 ? 0.95f : 0.5f),
                });
            }

            return ring;
        }

        /// <summary>
        /// The arm's mounting base, under <see cref="Tonearm"/>'s bearing. The
        /// record overlaps its inner edge, which is what sells the arm as
        /// mounted on the deck beside the platter rather than floating over
        /// the record.
        /// </summary>
        private static Drawable armBase()
        {
            float radians = MathHelper.DegreesToRadians(Tonearm.RestAngle);

            return new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                RelativePositionAxes = Axes.Both,
                Position = new Vector2(MathF.Sin(radians), -MathF.Cos(radians)) * (Tonearm.PivotRadius / 2),
                // Small enough that the arm's counterweight swings clear of
                // it rather than disappearing behind it.
                Size = new Vector2(0.058f),
                Children = new Drawable[]
                {
                    new Circle
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = new Color4(0.10f, 0.09f, 0.14f, 1f),
                    },
                    new Circle
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        RelativeSizeAxes = Axes.Both,
                        Size = new Vector2(0.74f),
                        Colour = ColourInfo.GradientVertical(
                            RetroPalette.Chrome.Darken(0.3f), RetroPalette.ChromeDark.Darken(0.2f)),
                    },
                    new Circle
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        RelativeSizeAxes = Axes.Both,
                        Size = new Vector2(0.3f),
                        Colour = RetroPalette.Void,
                    },
                },
            };
        }

        /// <summary>
        /// Power lamp, speed selector and pitch fader, stacked in the strip of
        /// deck left of the platter — the one piece of bare deck this layout
        /// leaves, and where a real deck keeps the same three controls.
        /// </summary>
        private static Drawable controlCluster() => new FillFlowContainer
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativePositionAxes = Axes.Both,
            Position = new Vector2(strip_centre, -0.17f),
            AutoSizeAxes = Axes.Both,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, 10),
            Children = new Drawable[]
            {
                new Circle
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Size = new Vector2(10),
                    Colour = RetroPalette.Amber,
                    EdgeEffect = new EdgeEffectParameters
                    {
                        Type = EdgeEffectType.Glow,
                        Colour = RetroPalette.Amber.Opacity(0.6f),
                        Radius = 10,
                    },
                },
                speedPill("33", lit: true),
                speedPill("45", lit: false),
                pitchFader(),
            },
        };

        private static Drawable speedPill(string label, bool lit) => new Container
        {
            Anchor = Anchor.TopCentre,
            Origin = Anchor.TopCentre,
            Size = new Vector2(46, 22),
            Masking = true,
            CornerRadius = 4,
            BorderThickness = 1.5f,
            BorderColour = lit ? RetroPalette.Amber.Opacity(0.8f) : RetroPalette.ChromeDark.Opacity(0.7f),
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = lit
                        ? RetroPalette.Amber.Opacity(0.18f)
                        : new Color4(0.03f, 0.025f, 0.05f, 0.9f),
                },
                new RetroText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Font = RetroFontFamily.Display,
                    TextSize = 10,
                    Colour = lit ? RetroPalette.Amber : RetroPalette.TextDim.Opacity(0.7f),
                    Text = label,
                },
            },
        };

        private static Drawable pitchFader() => new Container
        {
            Anchor = Anchor.TopCentre,
            Origin = Anchor.TopCentre,
            Size = new Vector2(24, 116),
            Children = new Drawable[]
            {
                new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(8, 116),
                    Masking = true,
                    CornerRadius = 4,
                    Child = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = new Color4(0.02f, 0.015f, 0.035f, 1f),
                    },
                },
                // Centre detent, where a fader sits at 0%.
                new Box
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(20, 1.5f),
                    Colour = RetroPalette.Chrome.Opacity(0.45f),
                },
                new Circle
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    // Parked slightly above centre — a fader dead on the
                    // detent reads as a drawn line rather than a knob.
                    Y = -18,
                    Size = new Vector2(22, 12),
                    Colour = ColourInfo.GradientVertical(
                        RetroPalette.Chrome, RetroPalette.ChromeDark),
                },
            },
        };
    }
}
