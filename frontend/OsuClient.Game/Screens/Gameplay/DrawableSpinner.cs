using System;
using System.Collections.Generic;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using OsuClient.Game.Beatmaps;
using OsuClient.Game.Beatmaps.HitObjects;
using OsuClient.Game.Graphics;
using OsuClient.Game.Graphics.Rack;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.Gameplay
{
    /// <summary>
    /// A spinner: spun by circling the cursor around its centre for the
    /// object's duration. As in osu!, no key needs to be held — only cursor
    /// movement counts.
    ///
    /// Drawn as a turntable: a record on a chrome platter, turning with the
    /// player's spin, its label in <see cref="DrawableHitObject.ComboColour"/>.
    /// The platter's strobe ring tells the player whether they are spinning
    /// fast enough — it drifts until they are, then stands still (see
    /// <c>updateSpeed</c>) — and an RPM readout below the disc turns from
    /// amber to mint at the same moment. The two meters either side of the
    /// disc fill as progress is made.
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

        /// <summary>Dots on the platter's strobe ring.</summary>
        private const int strobe_dots = 60;

        /// <summary>
        /// How opaque the platter's and the record's surfaces are. Low on
        /// purpose: the next objects often appear before a spinner ends, and
        /// they must be visible through it.
        /// </summary>
        private const float platter_alpha = 0.22f;

        private const float record_alpha = 0.35f;

        /// <summary>Grooves pressed into the record, drawn as faint rings.</summary>
        private const int groove_count = 5;

        /// <summary>How quickly the measured spin speed follows the real one, in milliseconds.</summary>
        private const double speed_smoothing = 160;

        /// <summary>
        /// How often the RPM readout may change, in milliseconds. Its text is
        /// rendered to a texture whenever it changes, and a live speed would
        /// change it every frame; a few times a second is all anyone can read.
        /// </summary>
        private const double readout_interval = 120;

        /// <summary>The readout rounds to this many RPM, so small wobbles don't redraw it.</summary>
        private const double readout_step = 5;

        private readonly double requiredSpins;

        /// <summary>The average speed that completes the spinner exactly on time, in degrees per millisecond.</summary>
        private readonly double requiredRate;

        private readonly Container body;
        private readonly Container record;
        private readonly Container strobe;
        private readonly SideMeter leftMeter;
        private readonly SideMeter rightMeter;
        private readonly SegmentReadout rpmReadout;

        private double? lastCursorAngle;
        private double accumulatedDegrees;
        private int bonusSpinsAwarded;

        private double? lastUpdateTime;
        private double lastAccumulated;
        private double spinRate;
        private double lastReadoutAt = double.NegativeInfinity;

        public DrawableSpinner(SpinnerData data, BeatmapDifficulty difficulty, Color4 comboColour)
            : base(data, difficulty, comboColour)
        {
            double spinsPerMinute = JudgementProcessor.SpinsPerMinute(difficulty.OverallDifficulty);

            requiredSpins = Math.Max(1, data.Duration / 1000.0 * spinsPerMinute / 60.0);
            requiredRate = requiredSpins * 360 / Math.Max(1, data.Duration);

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
                        // The platter, see-through: objects arriving behind the
                        // spinner as it ends have to stay visible through it,
                        // so only its chrome rim is solid.
                        new Circle
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = new Color4(0.07f, 0.06f, 0.10f, platter_alpha),
                        },
                        new CircularContainer
                        {
                            RelativeSizeAxes = Axes.Both,
                            Masking = true,
                            BorderThickness = 5,
                            BorderColour = ColourInfo.GradientVertical(RetroPalette.Chrome, RetroPalette.ChromeDark),
                            Child = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0, AlwaysPresent = true },
                        },
                        // Centred origin, so turning it spins the dots round
                        // the rim rather than swinging the ring off the disc.
                        strobe = new Container
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            RelativeSizeAxes = Axes.Both,
                            Children = createStrobeDots(),
                        },
                        record = new Container
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            RelativeSizeAxes = Axes.Both,
                            Size = new Vector2(0.86f),
                            Children = createRecord(comboColour),
                        },
                        leftMeter = new SideMeter(mirrored: false),
                        rightMeter = new SideMeter(mirrored: true),
                        // The spindle.
                        new Circle
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Size = new Vector2(9),
                            Colour = RetroPalette.Chrome,
                        },
                    },
                },
                rpmReadout = new SegmentReadout(14)
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.TopCentre,
                    // Below the disc, where it never covers the record.
                    Position = data.Position + new Vector2(0, disc_radius + 14),
                    Digits = 7,
                    DisplayColour = RetroPalette.Amber,
                    Text = "  0 RPM",
                },
            };
        }

        private static Drawable[] createStrobeDots()
        {
            var dots = new Drawable[strobe_dots];

            for (int i = 0; i < strobe_dots; i++)
            {
                // A zero-sized wrapper pinned to the centre: rotating it swings
                // the offset dot round the rim with no per-dot trigonometry.
                dots[i] = new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Rotation = i * (360f / strobe_dots),
                    Child = new Box
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Y = -disc_radius * 0.905f,
                        Size = new Vector2(4, 6),
                        Colour = RetroPalette.Amber,
                    },
                };
            }

            return dots;
        }

        private static Drawable[] createRecord(Color4 comboColour)
        {
            var parts = new List<Drawable>
            {
                new Circle
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(RetroPalette.Vinyl.R, RetroPalette.Vinyl.G, RetroPalette.Vinyl.B, record_alpha),
                },
            };

            // Faint grooves across the playing surface.
            for (int i = 0; i < groove_count; i++)
            {
                float size = 0.5f + i * 0.1f;

                parts.Add(new CircularContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Size = new Vector2(size),
                    Masking = true,
                    BorderThickness = 1.5f,
                    BorderColour = new Color4(1f, 1f, 1f, 0.07f),
                    Child = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0, AlwaysPresent = true },
                });
            }

            // Light catching the grooves: off-centre, so the record visibly
            // turns — everything else on it is a circle.
            parts.Add(new CircularProgress
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                Size = new Vector2(0.84f),
                Progress = 0.12,
                InnerRadius = 0.05f,
                Blending = BlendingParameters.Additive,
                Colour = new Color4(1f, 1f, 1f, 0.22f),
            });
            parts.Add(new CircularProgress
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                Size = new Vector2(0.64f),
                Progress = 0.09,
                InnerRadius = 0.06f,
                Rotation = 180,
                Blending = BlendingParameters.Additive,
                Colour = new Color4(1f, 1f, 1f, 0.18f),
            });

            // The label, in the combo colour, with a print mark so it visibly
            // turns too.
            parts.Add(new Circle
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                Size = new Vector2(0.34f),
                Colour = new Color4(comboColour.R, comboColour.G, comboColour.B, 0.6f),
            });
            parts.Add(new Box
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.BottomCentre,
                RelativeSizeAxes = Axes.Both,
                Size = new Vector2(0.03f, 0.15f),
                Colour = new Color4(0f, 0f, 0f, 0.45f),
            });

            return parts.ToArray();
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            using (BeginAbsoluteSequence(AppearTime))
                this.FadeIn(FadeInDuration, Easing.OutQuint);
        }

        public override bool AcceptsPress => false;

        public override bool TryPress(double time, Vector2 cursorScreenSpace) => false;

        public override void UpdateGameplay(double time, Vector2 cursorScreenSpace, bool anyKeyHeld)
        {
            if (time >= StartTime && time <= EndTime)
                trackRotation(cursorScreenSpace);
            else
                lastCursorAngle = null;

            updateSpeed(time);

            if (!IsJudged && time >= EndTime)
                ApplyJudgement(time, JudgementProcessor.JudgeSpinner(Progress));
        }

        public double Progress => accumulatedDegrees / 360.0 / requiredSpins;

        public int BonusSpinsAwarded => bonusSpinsAwarded;

        /// <summary>The measured spin speed, in revolutions per minute. Exposed for tests.</summary>
        public double Rpm => spinRate * 60000 / 360;

        /// <summary>Whether the player is spinning at least as fast as the spinner needs. Exposed for tests.</summary>
        public bool FastEnough => spinRate >= requiredRate;

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

                record.Rotation += (float)delta;

                float completion = (float)Math.Clamp(Progress, 0, 1);

                leftMeter.Progress = completion;
                rightMeter.Progress = completion;
            }

            lastCursorAngle = angle;
        }

        /// <summary>
        /// Measures how fast the player is spinning and drives the strobe ring
        /// and the RPM readout from it.
        ///
        /// The strobe dots are drawn turning at the difference between the
        /// player's speed and the speed the spinner needs, the way a real
        /// deck's strobe appears to drift until the platter is at speed. Too
        /// slow and the ring drifts backwards; fast enough and it stands
        /// still, so "the dots have stopped" means "keep this up".
        /// </summary>
        private void updateSpeed(double time)
        {
            if (lastUpdateTime is double last && time > last)
            {
                double dt = time - last;
                double instant = (accumulatedDegrees - lastAccumulated) / dt;

                spinRate += (instant - spinRate) * (1 - Math.Exp(-dt / speed_smoothing));

                if (time >= StartTime && time <= EndTime)
                    strobe.Rotation += (float)(Math.Min(0, spinRate - requiredRate) * dt);
            }

            lastUpdateTime = time;
            lastAccumulated = accumulatedDegrees;

            if (Math.Abs(time - lastReadoutAt) < readout_interval)
                return;

            lastReadoutAt = time;

            bool complete = Progress >= 1;
            double shown = Math.Min(999, Math.Round(Rpm / readout_step) * readout_step);
            string text = complete ? "   FULL" : $"{shown,3:0} RPM";

            if (rpmReadout.Text != text)
                rpmReadout.Text = text;

            var colour = complete || FastEnough ? RetroPalette.Mint : RetroPalette.Amber;

            if (rpmReadout.DisplayColour != colour)
                rpmReadout.DisplayColour = colour;
        }

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
                            half(upperFill = arc(centreAngle, RetroPalette.Amber, 0), flipped: false),
                            half(lowerFill = arc(centreAngle, RetroPalette.Amber, 0), flipped: true),
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
