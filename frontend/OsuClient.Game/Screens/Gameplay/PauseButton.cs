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
    /// A wide labelled button in the reading screens around gameplay — the
    /// pause menu's choices and the results screen's retry — so a choice
    /// looks and answers the same wherever it is offered.
    /// </summary>
    public partial class PauseButton : CompositeDrawable
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
