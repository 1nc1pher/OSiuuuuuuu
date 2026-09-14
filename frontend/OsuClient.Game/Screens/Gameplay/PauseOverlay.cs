using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using OsuClient.Game.Graphics;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.Gameplay
{
    /// <summary>
    /// The menu shown while gameplay is paused: continue where you left off,
    /// or give up and go back to song select.
    ///
    /// Styled like <see cref="Results.ResultsScreen"/> — the beatmap's own
    /// background blurred behind a dim, with the retro faces over it — since
    /// both are moments where the player is reading rather than playing.
    ///
    /// A <see cref="VisibilityContainer"/> so that while hidden it takes no
    /// input at all: the buttons can't be clicked through, and the blurred
    /// background costs nothing to draw.
    /// </summary>
    public partial class PauseOverlay : VisibilityContainer
    {
        private const double fade_duration = 250;

        private static readonly Color4 continue_colour = new Color4(0.4f, 0.8f, 1f, 1f);
        private static readonly Color4 quit_colour = new Color4(1f, 0.4f, 0.4f, 1f);

        public PauseOverlay(string? backgroundPath, string title, string difficulty, Action onContinue, Action onQuit)
        {
            RelativeSizeAxes = Axes.Both;
            Alpha = 0;

            InternalChildren = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(0.07f, 0.07f, 0.11f, 1f),
                },
                new BufferedContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    BlurSigma = new Vector2(16f),
                    Child = new BeatmapBackground(backgroundPath),
                },
                // Dimmed harder than the results screen's 0.45: this screen
                // carries more small text — button labels and the hint — over
                // the same bright, busy blur.
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(0f, 0f, 0f, 0.62f),
                },
                new FillFlowContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 10),
                    Children = new Drawable[]
                    {
                        new RetroText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Text = "PAUSED",
                            Font = RetroFontFamily.Display,
                            TextSize = 40,
                            Colour = Color4.White,
                        },
                        new RetroText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Text = title,
                            Font = RetroFontFamily.Body,
                            TextSize = 18,
                            Colour = new Color4(0.85f, 0.85f, 0.92f, 1f),
                            Margin = new MarginPadding { Top = 8 },
                        },
                        new RetroText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Text = $"[{difficulty}]",
                            Font = RetroFontFamily.Body,
                            TextSize = 13,
                            Colour = new Color4(0.7f, 0.7f, 0.8f, 1f),
                            Margin = new MarginPadding { Bottom = 18 },
                        },
                        new PauseButton("Continue", continue_colour, onContinue),
                        new PauseButton("Quit to song select", quit_colour, onQuit),
                        new RetroText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Text = "Escape to resume",
                            Font = RetroFontFamily.Body,
                            TextSize = 12,
                            Colour = new Color4(0.55f, 0.55f, 0.65f, 1f),
                            Margin = new MarginPadding { Top = 18 },
                        },
                    },
                },
            };
        }

        protected override bool StartHidden => true;

        protected override void PopIn() => this.FadeIn(fade_duration, Easing.OutQuint);

        protected override void PopOut() => this.FadeOut(fade_duration, Easing.OutQuint);

        /// <summary>Swallows clicks that miss the buttons, so they can't reach gameplay behind.</summary>
        protected override bool OnClick(ClickEvent e) => true;

        protected override bool OnMouseDown(MouseDownEvent e) => true;

        private partial class PauseButton : CompositeDrawable
        {
            private static readonly Color4 idle_colour = new Color4(1f, 1f, 1f, 0.1f);

            private readonly Box background;
            private readonly Color4 accent;
            private readonly Action action;

            public PauseButton(string label, Color4 accent, Action action)
            {
                this.accent = accent;
                this.action = action;

                Anchor = Anchor.TopCentre;
                Origin = Anchor.TopCentre;
                Size = new Vector2(340, 52);
                Masking = true;
                CornerRadius = 6;
                BorderThickness = 2;
                BorderColour = new Color4(accent.R, accent.G, accent.B, 0.5f);

                InternalChildren = new Drawable[]
                {
                    background = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = idle_colour,
                    },
                    new RetroText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Text = label,
                        Font = RetroFontFamily.Body,
                        TextSize = 16,
                        Colour = accent,
                    },
                };
            }

            protected override bool OnHover(HoverEvent e)
            {
                background.FadeColour(new Color4(accent.R, accent.G, accent.B, 0.3f), 120, Easing.OutQuint);
                return true;
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                background.FadeColour(idle_colour, 120, Easing.OutQuint);
            }

            protected override bool OnClick(ClickEvent e)
            {
                action();
                return true;
            }
        }
    }
}
