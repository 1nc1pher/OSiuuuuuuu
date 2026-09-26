using System;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using OsuClient.Game.Graphics;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.Gameplay.HUD
{
    /// <summary>
    /// The HP meter, built as a tape deck's LED recording-level ladder: a row
    /// of hard-edged rectangular segments in a dark inset, lit from the left
    /// up to the current health. A third of the playfield's width, in the
    /// top-left corner; positioning is the caller's job (see
    /// <see cref="Gameplay.PlayerScreen"/>).
    ///
    /// <para>
    /// The lit segments all take the colour of the band health is in, so it
    /// reads at a glance without judging the length: mint above two-thirds,
    /// amber through the middle third, red below one-third. Unlit segments
    /// stay faintly visible, as on a real ladder, which is what makes it
    /// read as hardware rather than a bar. A peak-hold segment lingers where
    /// health last was and then falls, so a sudden loss shows as a gap; below
    /// a third, the lit segments blink.
    /// </para>
    /// </summary>
    public partial class HealthBar : CompositeDrawable
    {
        /// <summary>Exposed so the HUD can lay other elements out clear of the meter.</summary>
        public const float BarHeight = 26;

        private const int segment_count = 28;
        private const float segment_gap = 3;
        private const float label_width = 30;

        private const double danger_threshold = 1 / 3d;
        private const double warning_threshold = 2 / 3d;

        /// <summary>How long the peak segment holds before it starts to fall.</summary>
        private const double peak_hold = 650;

        /// <summary>How long the peak segment takes to fall by one segment once it does.</summary>
        private const double peak_fall_step = 70;

        /// <summary>One full on/off cycle of the danger blink.</summary>
        private const double blink_period = 520;

        private static readonly Color4 healthy_colour = RetroPalette.Mint;
        private static readonly Color4 warning_colour = RetroPalette.Amber;
        private static readonly Color4 danger_colour = new Color4(1f, 0.26f, 0.28f, 1f);

        private readonly Box[] segments = new Box[segment_count];

        private double health = 1;
        private Color4 fillColour = healthy_colour;

        private int peakSegment;
        private double peakSetAt;
        private double lastFallAt;

        public HealthBar()
        {
            RelativeSizeAxes = Axes.X;
            Width = 1 / 3f;
            Height = BarHeight;

            var row = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding { Left = label_width, Right = 5, Vertical = 5 },
            };

            for (int i = 0; i < segment_count; i++)
            {
                row.Add(new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    RelativePositionAxes = Axes.X,
                    X = i / (float)segment_count,
                    Width = 1f / segment_count,
                    Padding = new MarginPadding { Right = segment_gap },
                    Child = segments[i] = new Box { RelativeSizeAxes = Axes.Both },
                });
            }

            InternalChild = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 2,
                BorderThickness = 1.5f,
                BorderColour = RetroPalette.ChromeDark,
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = new Color4(0.035f, 0.03f, 0.06f, 0.92f),
                    },
                    new RetroText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        X = 7,
                        Font = RetroFontFamily.Display,
                        TextSize = 8,
                        Colour = RetroPalette.TextDim,
                        Text = "HP",
                    },
                    row,
                },
            };

            peakSegment = segment_count;
            paint(0);
        }

        /// <summary>Sets the displayed health, 0 to 1.</summary>
        public void SetHealth(double value)
        {
            health = Math.Clamp(value, 0, 1);
            fillColour = ColourFor(health);

            int lit = litSegments;

            if (lit >= peakSegment)
            {
                peakSegment = lit;
                peakSetAt = Time.Current;
            }
        }

        /// <summary>The colour the lit segments currently show. Exposed for tests.</summary>
        public Color4 CurrentFillColour => fillColour;

        /// <summary>How many segments are lit. Exposed for tests.</summary>
        public int LitSegments => litSegments;

        private int litSegments => (int)Math.Ceiling(health * segment_count - 1e-9);

        /// <summary>The band a given health value falls into. Exposed for tests.</summary>
        public static Color4 ColourFor(double health)
        {
            // "more than 66%" is green, so exactly two-thirds is already
            // amber; "below 33%" is red, so exactly one-third is still
            // amber — both boundaries land in the middle band.
            if (health > warning_threshold)
                return healthy_colour;

            return health >= danger_threshold ? warning_colour : danger_colour;
        }

        protected override void Update()
        {
            base.Update();

            double now = Time.Current;
            int lit = litSegments;

            // The peak holds, then falls a segment at a time until it meets
            // the lit segments again.
            if (peakSegment > lit && now - peakSetAt > peak_hold && now - lastFallAt > peak_fall_step)
            {
                peakSegment--;
                lastFallAt = now;
            }

            if (peakSegment < lit)
                peakSegment = lit;

            paint(now);
        }

        private void paint(double now)
        {
            int lit = litSegments;

            bool blinkOff = health < danger_threshold && health > 0
                            && now % blink_period > blink_period / 2;

            Color4 on = blinkOff ? fillColour.Opacity(0.35f) : fillColour;
            Color4 off = fillColour.Opacity(0.1f);
            // Halfway to white: the same LED, driven harder.
            Color4 peak = new Color4((fillColour.R + 1) / 2, (fillColour.G + 1) / 2, (fillColour.B + 1) / 2, 0.8f);

            for (int i = 0; i < segment_count; i++)
            {
                if (i < lit)
                    segments[i].Colour = on;
                else if (i == peakSegment - 1)
                    segments[i].Colour = peak;
                else
                    segments[i].Colour = off;
            }
        }
    }
}
