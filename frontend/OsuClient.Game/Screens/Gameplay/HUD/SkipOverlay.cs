using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using OsuClient.Game.Graphics;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.Gameplay.HUD
{
    /// <summary>
    /// Shown over a long gap with nothing to hit (<see cref="BreakKind.Skip"/>):
    /// a diamond "skip" button in the middle of the screen, styled after
    /// osu!(lazer)'s own, with a bar either side of it that shrinks in from
    /// both edges toward the button as the gap runs out.
    ///
    /// The bar's two halves reading as one continuous span is the whole point
    /// of it: their combined width *is* the gap's remaining length, so the
    /// player can see at a glance whether skipping is worth it before they
    /// even reach for the button.
    /// </summary>
    public partial class SkipOverlay : VisibilityContainer
    {
        private const double fade_duration = 200;

        /// <summary>
        /// Clear space kept between the button and the nearest edge of each
        /// bar half, so a bar never visibly runs underneath the diamond —
        /// past this point it should read as gone, not hidden. A little wider
        /// than <see cref="SkipButton.diamond_size"/>'s own half-reach once
        /// rotated (~68px), so the gap is obviously deliberate rather than
        /// looking like a rounding error.
        /// </summary>
        private const float button_clearance = 76;

        /// <summary>How far a bar half reaches from screen centre — clearance included — at the very start of a break.</summary>
        private const float max_reach = 420;

        private const float bar_height = 6;

        private static readonly Color4 accent = new Color4(1f, 0.78f, 0.2f, 1f);

        private readonly Action onSkip;
        private readonly Container leftBar;
        private readonly Container rightBar;

        public SkipOverlay(Action onSkip)
        {
            this.onSkip = onSkip;

            RelativeSizeAxes = Axes.Both;

            InternalChildren = new Drawable[]
            {
                // Nothing is ever on screen during a skippable break (that's
                // the whole reason it qualifies as one), so this only has to
                // read as "gameplay has stepped back," not obscure anything.
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(0f, 0f, 0f, 0.35f),
                },
                leftBar = createBarHalf(Anchor.CentreRight, -button_clearance),
                rightBar = createBarHalf(Anchor.CentreLeft, button_clearance),
                new SkipButton(triggerSkip),
            };
        }

        /// <summary>
        /// One shrinking half of the bar: a small glowing pill whose <see cref="Container.Width"/>
        /// is driven by <see cref="SetRemaining"/>. Its near edge — the one
        /// given by <paramref name="origin"/> — is pinned <see cref="button_clearance"/>
        /// pixels off screen centre via <paramref name="xOffset"/> rather than
        /// sitting flush against it, so growing/shrinking this box never
        /// reaches back in behind the button: it always stops at that fixed
        /// gap and simply vanishes there. A <see cref="Container"/> rather
        /// than a bare <see cref="Box"/> because <see cref="Drawable.EdgeEffect"/>
        /// only exists on the former — the same reason <see cref="GlowBar"/>
        /// wraps its own fill.
        /// </summary>
        private static Container createBarHalf(Anchor origin, float xOffset) => new Container
        {
            Anchor = Anchor.Centre,
            Origin = origin,
            X = xOffset,
            Height = bar_height,
            Width = 0,
            Masking = true,
            CornerRadius = bar_height / 2,
            EdgeEffect = new EdgeEffectParameters
            {
                Type = EdgeEffectType.Glow,
                Colour = new Color4(accent.R, accent.G, accent.B, 0.6f),
                Radius = 6,
            },
            Child = new Box { RelativeSizeAxes = Axes.Both, Colour = accent },
        };

        protected override bool StartHidden => true;

        protected override void PopIn() => this.FadeIn(fade_duration, Easing.OutQuint);

        protected override void PopOut() => this.FadeOut(fade_duration, Easing.OutQuint);

        /// <summary>Sets how much of the break is left, 1 at its start down to 0 at its end. Exposed for tests.</summary>
        public void SetRemaining(double fraction)
        {
            // The clearance is spoken for regardless of fraction — only the
            // reach past it grows or shrinks — otherwise a fraction near 0
            // would try to draw a negative-width bar as it crossed below the
            // clearance itself.
            float extent = (float)Math.Clamp(fraction, 0, 1) * (max_reach - button_clearance);

            leftBar.Width = extent;
            rightBar.Width = extent;
        }

        /// <summary>Current half-length of the shrinking bar, in pixels. Exposed for tests.</summary>
        public float CurrentBarExtent => leftBar.Width;

        private void triggerSkip() => onSkip();

        /// <summary>Anywhere on the dimmed overlay skips, not just the button itself — forgiving, the way a full-screen prompt should be.</summary>
        protected override bool OnClick(ClickEvent e)
        {
            triggerSkip();
            return true;
        }

        protected override bool OnMouseDown(MouseDownEvent e) => true;

        /// <summary>
        /// The diamond itself: a rotated rounded square for the lazer-style
        /// shape, with its ">>> SKIP" label counter-rotated back to upright so
        /// it stays readable.
        /// </summary>
        private partial class SkipButton : CompositeDrawable
        {
            private const float diamond_size = 96;

            private readonly Box fill;
            private readonly Action action;

            public SkipButton(Action action)
            {
                this.action = action;

                Anchor = Anchor.Centre;
                Origin = Anchor.Centre;
                Size = new Vector2(diamond_size);

                InternalChildren = new Drawable[]
                {
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Rotation = 45,
                        Masking = true,
                        CornerRadius = 14,
                        BorderThickness = 3,
                        BorderColour = accent,
                        EdgeEffect = new EdgeEffectParameters
                        {
                            Type = EdgeEffectType.Glow,
                            Colour = new Color4(accent.R, accent.G, accent.B, 0.5f),
                            Radius = 14,
                        },
                        Child = fill = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = new Color4(accent.R, accent.G, accent.B, 0.22f),
                        },
                    },
                    // Deliberately a sibling of the rotated diamond rather than
                    // a child of it — a child would inherit the 45° rotation
                    // and the chevrons/label would read sideways.
                    new FillFlowContainer
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 2),
                        Children = new Drawable[]
                        {
                            new RetroText
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                Font = RetroFontFamily.Display,
                                TextSize = 16,
                                Colour = Color4.White,
                                Text = ">>>",
                            },
                            new RetroText
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                Font = RetroFontFamily.Body,
                                TextSize = 11,
                                Colour = Color4.White,
                                Text = "SKIP",
                            },
                        },
                    },
                };
            }

            protected override bool OnHover(HoverEvent e)
            {
                fill.FadeColour(new Color4(accent.R, accent.G, accent.B, 0.45f), 120, Easing.OutQuint);
                return true;
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                fill.FadeColour(new Color4(accent.R, accent.G, accent.B, 0.22f), 120, Easing.OutQuint);
            }

            protected override bool OnClick(ClickEvent e)
            {
                action();
                return true;
            }
        }
    }
}
