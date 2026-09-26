using System;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using OsuClient.Game.Graphics;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.Gameplay.HUD
{
    /// <summary>
    /// The combo, as a cassette deck's mechanical tape counter: four digit
    /// wheels behind a window that roll up with every hit, carrying into the
    /// next wheel as a real counter does, and spin back to 0000 when the
    /// combo breaks, with the digits flashing red.
    ///
    /// Each wheel is a strip of pre-rendered digits scrolled inside a masked
    /// window, so rolling never re-renders any text: the numerals are drawn
    /// once, when the counter loads.
    /// </summary>
    public partial class ComboCounter : CompositeDrawable
    {
        private const int wheel_count = 4;
        private const float wheel_width = 19;
        private const float wheel_height = 28;
        private const float digit_size = 16;

        private const double roll_duration = 140;
        private const double reset_duration = 260;
        private const double break_fade = 500;

        private static readonly Color4 break_colour = new Color4(1f, 0.3f, 0.32f, 1f);

        private readonly Container digits;
        private readonly Wheel[] wheels = new Wheel[wheel_count];

        private int displayedCombo;

        /// <summary>Current colour of the numerals. Exposed for tests.</summary>
        public Color4 CurrentColour => digits.Colour;

        /// <summary>What the wheels currently show, read off their positions. Exposed for tests.</summary>
        public string Reading => string.Concat(wheels.Select(w => w.Reading));

        public ComboCounter()
        {
            AutoSizeAxes = Axes.Both;
            Anchor = Anchor.BottomLeft;
            Origin = Anchor.BottomLeft;

            var window = new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(2, 0),
            };

            // Most significant first, so the flow lays them out left to right.
            for (int i = 0; i < wheel_count; i++)
                window.Add(wheels[i] = new Wheel());

            InternalChild = new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 4),
                Children = new Drawable[]
                {
                    new RetroText
                    {
                        Font = RetroFontFamily.Display,
                        TextSize = 8,
                        Colour = RetroPalette.TextDim,
                        Text = "COMBO",
                    },
                    new Container
                    {
                        AutoSizeAxes = Axes.Both,
                        Masking = true,
                        CornerRadius = 3,
                        BorderThickness = 1.5f,
                        BorderColour = RetroPalette.ChromeDark,
                        Children = new Drawable[]
                        {
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = new Color4(0.03f, 0.025f, 0.05f, 0.95f),
                            },
                            digits = new Container
                            {
                                AutoSizeAxes = Axes.Both,
                                Padding = new MarginPadding(4),
                                Colour = Color4.White,
                                Child = window,
                            },
                        },
                    },
                },
            };
        }

        public void SetCombo(int combo)
        {
            combo = Math.Max(0, combo);

            bool broke = combo == 0 && displayedCombo > 0;
            displayedCombo = combo;

            if (broke)
            {
                foreach (var wheel in wheels)
                    wheel.ResetToZero();

                digits.FadeColour(break_colour, 0)
                      .Then().FadeColour(Color4.White, break_fade, Easing.OutQuint);
                return;
            }

            int value = combo;

            for (int i = wheel_count - 1; i >= 0; i--)
            {
                wheels[i].RollTo(value % 10);
                value /= 10;
            }
        }

        /// <summary>
        /// One digit wheel: 0-9 and a second 0 stacked in a strip, so rolling
        /// on from 9 lands on a 0 below it and then quietly snaps back to the
        /// top one.
        /// </summary>
        private partial class Wheel : CompositeDrawable
        {
            private readonly Container strip;

            private int target;

            public Wheel()
            {
                Size = new Vector2(wheel_width, wheel_height);
                Masking = true;
                CornerRadius = 1.5f;

                strip = new Container
                {
                    RelativeSizeAxes = Axes.X,
                    Height = wheel_height * 11,
                };

                for (int i = 0; i <= 10; i++)
                {
                    strip.Add(new RetroText
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.Centre,
                        Y = wheel_height * (i + 0.5f),
                        Font = RetroFontFamily.Display,
                        TextSize = digit_size,
                        Text = (i % 10).ToString(),
                    });
                }

                // The wheel's curve: darker at the top and bottom edges of the
                // window, lighter where it faces the viewer.
                var edge = new Color4(0f, 0f, 0f, 0.55f);
                var face = new Color4(0f, 0f, 0f, 0f);

                InternalChildren = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = new Color4(0.13f, 0.11f, 0.17f, 1f),
                    },
                    strip,
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Height = 0.5f,
                        Colour = ColourInfo.GradientVertical(edge, face),
                    },
                    new Box
                    {
                        Anchor = Anchor.BottomLeft,
                        Origin = Anchor.BottomLeft,
                        RelativeSizeAxes = Axes.Both,
                        Height = 0.5f,
                        Colour = ColourInfo.GradientVertical(face, edge),
                    },
                };
            }

            /// <summary>The digit this wheel currently shows.</summary>
            public string Reading => ((int)Math.Round(-strip.Y / wheel_height) % 10).ToString();

            /// <summary>Rolls forward to <paramref name="digit"/>, wrapping through 9 → 0 as a counter does.</summary>
            public void RollTo(int digit)
            {
                if (digit == target)
                    return;

                int from = target;
                target = digit;

                strip.ClearTransforms();
                strip.Y = -from * wheel_height;

                if (digit > from)
                {
                    strip.MoveToY(-digit * wheel_height, roll_duration, Easing.OutQuad);
                    return;
                }

                // Past 9: roll on to the second 0 at the bottom of the strip,
                // then jump back up to the first, which looks identical.
                strip.MoveToY(-10 * wheel_height, roll_duration, Easing.OutQuad)
                     .Then()
                     .MoveToY(0)
                     .Then()
                     .MoveToY(-digit * wheel_height, digit == 0 ? 0 : roll_duration, Easing.OutQuad);
            }

            /// <summary>Spins straight back to 0, the way a counter's reset button drops it.</summary>
            public void ResetToZero()
            {
                target = 0;
                strip.ClearTransforms();
                strip.MoveToY(0, reset_duration, Easing.OutQuint);
            }
        }
    }
}
