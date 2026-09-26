using System;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
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
    /// a fast-forward key in the middle of the screen, the tape deck's own,
    /// with a bar either side of it that shrinks in from both edges toward
    /// the key as the gap runs out.
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
        /// Clear space kept between screen centre and the nearest edge of each
        /// bar half, so a bar never visibly runs underneath the key — past
        /// this point it should read as gone, not hidden. A little wider than
        /// the key's own half-width (66px), so the gap is obviously deliberate
        /// rather than looking like a rounding error.
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
        /// only exists on the former.
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
        /// The skip control, as the tape deck's fast-forward key: a chrome
        /// transport key with an amber ▶▶ legend and SKIP under it, lit from
        /// behind like the deck's engaged keys.
        /// </summary>
        private partial class SkipButton : CompositeDrawable
        {
            /// <summary>Kept inside <see cref="button_clearance"/> either side, where the bars stop.</summary>
            private static readonly Vector2 key_size = new Vector2(132, 76);

            private static readonly ColourInfo face_idle = ColourInfo.GradientVertical(
                RetroPalette.Chrome.Darken(0.15f), RetroPalette.ChromeDark.Darken(0.35f));

            private static readonly ColourInfo face_hover = ColourInfo.GradientVertical(
                RetroPalette.Chrome, RetroPalette.ChromeDark.Darken(0.1f));

            private readonly Box face;
            private readonly FillFlowContainer legend;
            private readonly Action action;

            public SkipButton(Action action)
            {
                this.action = action;

                Anchor = Anchor.Centre;
                Origin = Anchor.Centre;
                Size = key_size;

                InternalChild = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Masking = true,
                    CornerRadius = 6,
                    BorderThickness = 2,
                    BorderColour = RetroPalette.ChromeDark,
                    EdgeEffect = new EdgeEffectParameters
                    {
                        Type = EdgeEffectType.Glow,
                        Colour = new Color4(accent.R, accent.G, accent.B, 0.4f),
                        Radius = 12,
                    },
                    Children = new Drawable[]
                    {
                        face = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = face_idle,
                        },
                        // The key's dark inset, where its legend is printed.
                        new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Padding = new MarginPadding(8),
                            Child = new Container
                            {
                                RelativeSizeAxes = Axes.Both,
                                Masking = true,
                                CornerRadius = 3,
                                Children = new Drawable[]
                                {
                                    new Box
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        Colour = RetroPalette.PanelInset,
                                    },
                                    legend = new FillFlowContainer
                                    {
                                        Anchor = Anchor.Centre,
                                        Origin = Anchor.Centre,
                                        AutoSizeAxes = Axes.Both,
                                        Direction = FillDirection.Vertical,
                                        Spacing = new Vector2(0, 5),
                                        Colour = accent,
                                        Children = new Drawable[]
                                        {
                                            // ▶▶, drawn rather than typed:
                                            // the pixel font has no arrows.
                                            new FillFlowContainer
                                            {
                                                Anchor = Anchor.TopCentre,
                                                Origin = Anchor.TopCentre,
                                                AutoSizeAxes = Axes.Both,
                                                Direction = FillDirection.Horizontal,
                                                Spacing = new Vector2(1, 0),
                                                Children = new Drawable[] { arrow(), arrow() },
                                            },
                                            new RetroText
                                            {
                                                Anchor = Anchor.TopCentre,
                                                Origin = Anchor.TopCentre,
                                                Font = RetroFontFamily.Display,
                                                TextSize = 10,
                                                Text = "SKIP",
                                            },
                                        },
                                    },
                                },
                            },
                        },
                    },
                };
            }

            /// <summary>One ▶ of the legend: a triangle turned to point right.</summary>
            private static Drawable arrow() => new Container
            {
                Size = new Vector2(16, 18),
                Child = new Triangle
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(18, 16),
                    Rotation = 90,
                },
            };

            protected override bool OnHover(HoverEvent e)
            {
                face.FadeColour(face_hover, 120, Easing.OutQuint);
                legend.FadeColour(Color4.White, 120, Easing.OutQuint);
                return true;
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                face.FadeColour(face_idle, 120, Easing.OutQuint);
                legend.FadeColour(accent, 120, Easing.OutQuint);
            }

            protected override bool OnClick(ClickEvent e)
            {
                action();
                return true;
            }
        }
    }
}
