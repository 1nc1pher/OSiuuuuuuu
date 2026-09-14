using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using OsuClient.Game.Beatmaps;
using OsuClient.Game.Beatmaps.HitObjects;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.Gameplay
{
    /// <summary>
    /// A spinner: spun by circling the cursor around its centre for the
    /// object's duration. As in osu!, no key needs to be held — only cursor
    /// movement counts.
    ///
    /// Visuals follow lazer's argon-style spinner: a ring of tick marks that
    /// rotates with the spin, two glowing meters either side of the circle
    /// that fill as progress is made, and a colour glow that grows out of the
    /// centre to fill the whole disc. That glow's colour is
    /// <see cref="DrawableHitObject.ComboColour"/> for now — the same value
    /// Phase 6 of FRONTEND_PLAN.md earmarks to become skin/theme provided, so
    /// the spinner picks up per-song theming for free once that lands.
    ///
    /// How much spinning is required comes from the beatmap's Overall
    /// Difficulty via <see cref="JudgementProcessor.SpinsPerMinute"/>, and the
    /// judgement is how much of that was actually completed.
    /// </summary>
    public partial class DrawableSpinner : DrawableHitObject
    {
        private const double exit_duration = 300;

        private const float disc_radius = 140;
        private const float diameter = disc_radius * 2;

        /// <summary>Dashes evenly spaced around the rim, pointing outward.</summary>
        private const int tick_count = 24;
        private const float tick_length = 22f;
        private const float tick_thickness = 3f;

        /// <summary>Radius the inner end of a tick sits at, just inside the outline.</summary>
        private const float tick_inset = disc_radius * 0.80f;

        private readonly double requiredSpins;

        private readonly Container body;
        private readonly Container rotor;
        private readonly Circle centreGlow;
        private readonly SideMeter leftMeter;
        private readonly SideMeter rightMeter;
        private readonly SpriteText progressText;

        private double? lastCursorAngle;
        private double accumulatedDegrees;
        private int bonusSpinsAwarded;

        public DrawableSpinner(SpinnerData data, BeatmapDifficulty difficulty, Color4 comboColour)
            : base(data, difficulty, comboColour)
        {
            double spinsPerMinute = JudgementProcessor.SpinsPerMinute(difficulty.OverallDifficulty);

            requiredSpins = Math.Max(1, data.Duration / 1000.0 * spinsPerMinute / 60.0);

            InternalChildren = new Drawable[]
            {
                body = new Container
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.Centre,
                    Position = data.Position,
                    Size = new Vector2(diameter),
                    Children = new Drawable[]
                    {
                        // Dark backdrop, so the playfield behind doesn't show
                        // through the disc before any colour builds up.
                        new Circle
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = new Color4(0.05f, 0.05f, 0.08f, 0.55f),
                        },
                        // The theme-coloured glow that grows out of the centre.
                        // A Circle is a CircularContainer, so its masking shape
                        // really is a circle and an edge effect on it glows as
                        // a circle — unlike a plain Container, whose effect
                        // would come out as the rectangle it masks to.
                        centreGlow = new Circle
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            RelativeSizeAxes = Axes.Both,
                            Size = new Vector2(centre_glow_min_scale),
                            Colour = comboColour,
                            Alpha = 0,
                            EdgeEffect = new EdgeEffectParameters
                            {
                                Type = EdgeEffectType.Glow,
                                Colour = new Color4(comboColour.R, comboColour.G, comboColour.B, 0.6f),
                                Radius = 40,
                            },
                        },
                        // Centred origin, or the accumulated spin rotates the
                        // whole ring about the corner of the disc and throws
                        // the ticks off it entirely.
                        rotor = new Container
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            RelativeSizeAxes = Axes.Both,
                            Children = createTicks(),
                        },
                        // The fixed circular boundary of the spinner.
                        new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Masking = true,
                            CornerRadius = disc_radius,
                            BorderThickness = 4,
                            BorderColour = new Color4(1f, 1f, 1f, 0.9f),
                            Child = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0, AlwaysPresent = true },
                        },
                        leftMeter = new SideMeter(mirrored: false),
                        rightMeter = new SideMeter(mirrored: true),
                        // Centre marker: a ring around a dot, as in the
                        // reference skin.
                        new CircularContainer
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Size = new Vector2(22),
                            Masking = true,
                            BorderThickness = 3,
                            BorderColour = Color4.White,
                            Child = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0, AlwaysPresent = true },
                        },
                        new Circle
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Size = new Vector2(6),
                            Colour = Color4.White,
                        },
                    },
                },
                progressText = new SpriteText
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.Centre,
                    // Below the centre marker rather than behind it.
                    Position = data.Position + new Vector2(0, 54),
                    Font = FontUsage.Default.With(size: 24),
                    Colour = new Color4(1f, 1f, 1f, 0.85f),
                    Text = "Spin!",
                },
            };
        }

        /// <summary>How wide the centre glow starts out, as a fraction of the disc.</summary>
        private const float centre_glow_min_scale = 0.06f;

        private static Drawable[] createTicks()
        {
            var ticks = new Drawable[tick_count];

            for (int i = 0; i < tick_count; i++)
            {
                // A zero-sized wrapper pinned to the centre: rotating it swings
                // the offset tick around the rim without any per-tick trigonometry.
                ticks[i] = new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Rotation = i * (360f / tick_count),
                    Child = new Box
                    {
                        Anchor = Anchor.BottomCentre,
                        Origin = Anchor.BottomCentre,
                        Y = -tick_inset,
                        Size = new Vector2(tick_thickness, tick_length),
                        Colour = Color4.White,
                        Alpha = 0.75f,
                    },
                };
            }

            return ticks;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            using (BeginAbsoluteSequence(AppearTime))
                this.FadeIn(FadeInDuration, Easing.OutQuint);
        }

        /// <summary>Spinners are spun, not clicked, so they never take a press.</summary>
        public override bool AcceptsPress => false;

        public override bool TryPress(double time, Vector2 cursorScreenSpace) => false;

        public override void UpdateGameplay(double time, Vector2 cursorScreenSpace, bool anyKeyHeld)
        {
            if (time >= StartTime && time <= EndTime)
                trackRotation(cursorScreenSpace);
            else
                lastCursorAngle = null;

            if (!IsJudged && time >= EndTime)
                ApplyJudgement(time, JudgementProcessor.JudgeSpinner(Progress));
        }

        /// <summary>How much of the required spinning has been done, 0 to 1 (and beyond).</summary>
        public double Progress => accumulatedDegrees / 360.0 / requiredSpins;

        /// <summary>Full extra rotations completed past the requirement, each already paid out as a bonus. Exposed for tests.</summary>
        public int BonusSpinsAwarded => bonusSpinsAwarded;

        private void trackRotation(Vector2 cursorScreenSpace)
        {
            Vector2 local = ToLocalSpace(cursorScreenSpace);
            Vector2 offset = local - Data.Position;

            if (offset.LengthSquared < 1)
                return;

            double angle = Math.Atan2(offset.Y, offset.X) * 180 / Math.PI;

            if (lastCursorAngle is double previous)
            {
                double delta = angle - previous;

                // Normalise to (-180, 180] so crossing the ±180 seam doesn't
                // register as most of a rotation.
                while (delta > 180) delta -= 360;
                while (delta < -180) delta += 360;

                accumulatedDegrees += Math.Abs(delta);
                checkForBonusSpins();

                rotor.Rotation += (float)delta;

                float completion = (float)Math.Clamp(Progress, 0, 1);

                centreGlow.Size = new Vector2(centre_glow_min_scale + (1 - centre_glow_min_scale) * completion);
                centreGlow.Alpha = 0.25f + 0.37f * completion;

                rotor.Alpha = 0.55f + 0.45f * completion;

                leftMeter.Progress = completion;
                rightMeter.Progress = completion;

                progressText.Text = completion >= 1 ? "Full!" : $"{completion * 100:0}%";
            }

            lastCursorAngle = angle;
        }

        /// <summary>
        /// Pays out a bonus for each full extra rotation completed once the
        /// required amount is already met — the reward for continuing to
        /// spin after the bar fills rather than letting go. Counted in whole
        /// 360° units, and never re-paid for the same rotation twice even
        /// though this runs every frame.
        /// </summary>
        private void checkForBonusSpins()
        {
            double requiredDegrees = requiredSpins * 360.0;
            int extraFullSpins = (int)Math.Floor((accumulatedDegrees - requiredDegrees) / 360.0);

            if (extraFullSpins <= bonusSpinsAwarded)
                return;

            for (int i = bonusSpinsAwarded; i < extraFullSpins; i++)
                ApplyBonus();

            bonusSpinsAwarded = extraFullSpins;
        }

        protected override double ApplyJudgementAnimation(HitResult result)
        {
            this.FadeOut(exit_duration, Easing.OutQuint);
            body.ScaleTo(result == HitResult.Miss ? 0.9f : 1.1f, exit_duration, Easing.OutQuint);

            return exit_duration;
        }

        /// <summary>
        /// One of the two bracket-shaped meters either side of the spinner: a
        /// dim static track with a bright white fill that grows out of the
        /// middle of the bracket in both directions as the spin progresses.
        ///
        /// Each bracket is one arc plus a copy mirrored about the horizontal
        /// axis, so the two halves stay symmetric about the point they grow
        /// from whichever way <see cref="CircularProgress"/> itself sweeps.
        ///
        /// The glow is a <see cref="BufferedContainer"/> blur of the fills
        /// themselves rather than an <see cref="EdgeEffectParameters"/>: an
        /// edge effect renders the container's *masking shape*, which for a
        /// plain container is its rectangle — a white rectangle the size of
        /// the playfield, not anything arc-shaped.
        /// </summary>
        private partial class SideMeter : CompositeDrawable
        {
            /// <summary>Degrees of the circle one whole bracket covers.</summary>
            private const float span = 112;

            /// <summary>Arc thickness, as a fraction of its radius.</summary>
            private const float thickness = 0.15f;

            /// <summary>Clockwise-from-top angle the bracket is centred on.</summary>
            private const float left_centre_angle = 270;

            /// <summary>
            /// Sized so the bracket clears the outline with a visible gap:
            /// the arc's inner edge lands at <c>radius * (1 - thickness)</c>,
            /// which has to stay outside the disc, not sit on top of it.
            /// </summary>
            private const float arc_diameter = diameter + 70;

            /// <summary>
            /// Slack around the arcs for the glow to fade out in. The blur is
            /// clipped to the buffer, so without room around the content it
            /// would be cut off mid-falloff.
            /// </summary>
            private const float blur_padding = 34;

            private const float blur_sigma = 9;

            private readonly CircularProgress upperFill;
            private readonly CircularProgress lowerFill;

            public SideMeter(bool mirrored)
            {
                float centreAngle = left_centre_angle + (mirrored ? 180 : 0);

                Anchor = Anchor.Centre;
                Origin = Anchor.Centre;
                Size = new Vector2(arc_diameter + blur_padding * 2);

                InternalChildren = new Drawable[]
                {
                    half(arc(centreAngle, track_colour, span / 2 / 360.0), flipped: false),
                    half(arc(centreAngle, track_colour, span / 2 / 360.0), flipped: true),
                    new BufferedContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        DrawOriginal = true,
                        BlurSigma = new Vector2(blur_sigma),
                        EffectBlending = BlendingParameters.Additive,
                        EffectPlacement = EffectPlacement.Behind,
                        EffectColour = new Color4(1f, 1f, 1f, 0.9f),
                        Children = new Drawable[]
                        {
                            half(upperFill = arc(centreAngle, Color4.White, 0), flipped: false),
                            half(lowerFill = arc(centreAngle, Color4.White, 0), flipped: true),
                        },
                    },
                };
            }

            private static readonly Color4 track_colour = new Color4(1f, 1f, 1f, 0.15f);

            // Anchor/Origin centred is load-bearing, not decoration: Rotation
            // pivots about a drawable's Origin, and the default (TopLeft)
            // swings the whole arc away from the spinner instead of turning it
            // in place.
            private static CircularProgress arc(float centreAngle, Color4 colour, double progress) => new CircularProgress
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                InnerRadius = thickness,
                RoundedCaps = true,
                Rotation = centreAngle,
                Progress = progress,
                Colour = colour,
            };

            private static Drawable half(CircularProgress halfArc, bool flipped) => new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(arc_diameter),
                Scale = new Vector2(1, flipped ? -1 : 1),
                Child = halfArc,
            };

            /// <summary>How far the bright fill has grown along the bracket, 0 to 1.</summary>
            public double Progress
            {
                set
                {
                    upperFill.Progress = span / 2 / 360.0 * value;
                    lowerFill.Progress = span / 2 / 360.0 * value;
                }
            }
        }
    }
}
