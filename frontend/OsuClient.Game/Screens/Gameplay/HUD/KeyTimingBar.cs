using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.Gameplay.HUD
{
    /// <summary>
    /// One key's panel in the key overlay: a vertical bar that both shows the
    /// key being pressed and plots how well timed each of its hits was.
    ///
    /// The bar spans the full <see cref="JudgementProcessor.MehWindow"/>, early
    /// at the top through perfect at the middle to late at the bottom, and its
    /// colours are the judgement windows themselves rather than decoration:
    /// blue out to the edge of the Great window, green out to the Ok window,
    /// yellow over the rest. They're the same three colours the judgement
    /// popups use, so where a hit lands on the bar matches the "300" or "100"
    /// that flies off the circle.
    ///
    /// The gradient is built from a stack of two-colour boxes that share the
    /// colour at each seam, which reads as one continuous fade rather than
    /// three blocks.
    /// </summary>
    public partial class KeyTimingBar : CompositeDrawable
    {
        /// <summary>Exposed so the HUD can line other elements up against the bar's own edge.</summary>
        public const float BarWidth = 9;

        private const float bar_width = BarWidth;
        private const float bar_height = 280;

        /// <summary>How long a hit's marker stays at full strength before it starts fading.</summary>
        private const double tick_hold_duration = 700;

        private const double tick_fade_duration = 1600;

        /// <summary>A hit's marker, wider than the bar so it reads against the gradient.</summary>
        private const float tick_width = bar_width * 2.6f;

        private const float tick_height = 3;

        private static readonly Color4 perfect_colour = new Color4(0.4f, 0.8f, 1f, 1f);
        private static readonly Color4 good_colour = new Color4(0.4f, 0.9f, 0.4f, 1f);
        private static readonly Color4 ok_colour = new Color4(0.95f, 0.8f, 0.3f, 1f);

        private readonly double mehWindow;

        private readonly Container bar;
        private readonly Container ticks;
        private readonly Container pressGlow;

        /// <param name="overallDifficulty">Sets where the colour bands fall — they are this map's hit windows.</param>
        public KeyTimingBar(double overallDifficulty)
        {
            mehWindow = JudgementProcessor.MehWindow(overallDifficulty);

            double greatFraction = JudgementProcessor.GreatWindow(overallDifficulty) / mehWindow;
            double okFraction = JudgementProcessor.OkWindow(overallDifficulty) / mehWindow;

            Size = new Vector2(bar_width, bar_height);

            InternalChildren = new Drawable[]
            {
                // A masked pill whose own fill is transparent: all that shows
                // is its edge effect, so the held key reads as a halo around
                // the bar rather than a rectangle behind it.
                pressGlow = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Masking = true,
                    CornerRadius = bar_width / 2,
                    EdgeEffect = new EdgeEffectParameters
                    {
                        Type = EdgeEffectType.Glow,
                        Colour = new Color4(1f, 1f, 1f, 0.9f),
                        Radius = 12,
                    },
                    Alpha = 0,
                    Child = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0, AlwaysPresent = true },
                },
                bar = new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Masking = true,
                    CornerRadius = bar_width / 2,
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = new Color4(0f, 0f, 0f, 0.45f),
                        },
                        // The early half read upward from the middle, then the
                        // same thing mirrored downward for the late half.
                        gradientHalf(true, greatFraction, okFraction),
                        gradientHalf(false, greatFraction, okFraction),
                    },
                },
                // Outside the masked bar, and the same size as it so a marker
                // centres on the bar while overhanging its edges.
                ticks = new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                },
            };
        }

        /// <summary>
        /// One half of the bar, from the perfect-timing line out to the edge
        /// of the Meh window.
        ///
        /// Each colour holds across the inner half of its window and fades
        /// into the next across the outer half. Holding first is what gives
        /// blue, green and yellow each a band you can actually read, while
        /// every seam still shares a colour with its neighbour, so the whole
        /// thing stays one continuous fade rather than three stacked blocks.
        /// </summary>
        private static Drawable gradientHalf(bool early, double greatFraction, double okFraction)
        {
            double greatFadeFrom = greatFraction / 2;
            double okFadeFrom = greatFraction + (okFraction - greatFraction) / 2;

            return new Container
            {
                RelativeSizeAxes = Axes.Both,
                Children = new[]
                {
                    segment(early, 0, greatFadeFrom, perfect_colour, perfect_colour),
                    segment(early, greatFadeFrom, greatFraction, perfect_colour, good_colour),
                    segment(early, greatFraction, okFadeFrom, good_colour, good_colour),
                    segment(early, okFadeFrom, okFraction, good_colour, ok_colour),
                    segment(early, okFraction, 1, ok_colour, ok_colour),
                },
            };
        }

        /// <summary>
        /// One band of the gradient, measured outward from the middle of the
        /// bar as a fraction of its half height. Anchoring each band at the
        /// centre and growing it outward keeps the two halves symmetric about
        /// the perfect-timing line.
        /// </summary>
        private static Box segment(bool early, double from, double to, Color4 inner, Color4 outer) => new Box
        {
            Anchor = Anchor.Centre,
            Origin = early ? Anchor.BottomCentre : Anchor.TopCentre,
            Y = (float)(early ? -from : from) * bar_height / 2,
            RelativeSizeAxes = Axes.X,
            Height = (float)(to - from) * bar_height / 2,
            Colour = early
                ? ColourInfo.GradientVertical(outer, inner)
                : ColourInfo.GradientVertical(inner, outer),
        };

        /// <summary>How many hits are currently plotted on this bar. Exposed for tests.</summary>
        public int PlottedHitCount => ticks.Children.Count;

        /// <summary>Lights the panel up while the key is held.</summary>
        public void Press()
        {
            pressGlow.FadeTo(0.75f, 40, Easing.OutQuint);
            bar.ScaleTo(new Vector2(1.35f, 1f), 40, Easing.OutQuint);
        }

        public void Release()
        {
            pressGlow.FadeOut(220, Easing.OutQuint);
            bar.ScaleTo(Vector2.One, 220, Easing.OutQuint);
        }

        /// <summary>
        /// Plots a hit this key landed. <paramref name="error"/> is signed
        /// milliseconds from the object's start time — negative early, positive
        /// late — so the marker sits above the middle for an early hit and
        /// below it for a late one, and on the blue centre for a perfect one.
        /// </summary>
        public void RecordHit(double error)
        {
            float offset = (float)(Math.Clamp(error / mehWindow, -1, 1) * bar_height / 2);

            // A Box is a Sprite and can't carry an edge effect; a masked
            // container can, and its rounded-rectangle mask is the tick's
            // shape anyway.
            var tick = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Y = offset,
                Size = new Vector2(tick_width, tick_height),
                Masking = true,
                CornerRadius = tick_height / 2,
                EdgeEffect = new EdgeEffectParameters
                {
                    Type = EdgeEffectType.Glow,
                    Colour = new Color4(1f, 1f, 1f, 0.5f),
                    Radius = 5,
                },
                Child = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = Color4.White,
                },
            };

            ticks.Add(tick);

            // Held at full strength before fading, so a marker is readable for
            // a moment rather than being a flash: an eased fade straight from
            // full is already near-invisible within a few hundred ms.
            tick.Delay(tick_hold_duration).FadeOut(tick_fade_duration, Easing.InQuad);

            // Explicit removal rather than Expire(), matching how the
            // judgement popups are cleaned up: a long map lands thousands of
            // these and none of them should outlive their own fade.
            Scheduler.AddDelayed(() => ticks.Remove(tick, true), tick_hold_duration + tick_fade_duration);
        }
    }
}
