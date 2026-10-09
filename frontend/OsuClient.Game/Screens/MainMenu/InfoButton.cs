using System;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using OsuClient.Game.Graphics;
using osuTK;

namespace OsuClient.Game.Screens.MainMenu
{
    /// <summary>
    /// The round "i" in the menu's top-left corner, which opens the credits.
    ///
    /// The now-playing credit's counterpart on the other side of the screen:
    /// the same dim, small furniture at rest, and lit cyan under the pointer
    /// with a glow like the strip buttons'. Its label slides out beside it on
    /// hover rather than sitting there permanently, so a corner that is only
    /// occasionally wanted stays quiet.
    /// </summary>
    public partial class InfoButton : CompositeDrawable
    {
        private const float diameter = 44;

        private readonly Container disc;
        private readonly Box fill;
        private readonly RetroText glyph;
        private readonly RetroText hint;

        /// <summary>Raised when the button is pressed.</summary>
        public Action? Clicked;

        public InfoButton()
        {
            // Fixed rather than auto-sized: the label that slides out beside
            // the disc must not widen the area that answers to the pointer.
            Size = new Vector2(diameter);

            InternalChildren = new Drawable[]
            {
                disc = new Container
                {
                    // Centred so that the press shrinks it about its middle.
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(diameter),
                    Masking = true,
                    CornerRadius = diameter / 2,
                    BorderThickness = 2,
                    BorderColour = RetroPalette.Cyan.Opacity(0.5f),
                    EdgeEffect = new EdgeEffectParameters
                    {
                        Type = EdgeEffectType.Glow,
                        Colour = RetroPalette.Cyan.Opacity(0),
                        Radius = 20,
                    },
                    Children = new Drawable[]
                    {
                        fill = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = RetroPalette.Panel,
                        },
                        glyph = new RetroText
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            // Press Start's "i" sits a pixel high in its box.
                            Y = 1,
                            Font = RetroFontFamily.Display,
                            TextSize = 15,
                            Text = "i",
                            Colour = RetroPalette.TextDim,
                        },
                    },
                },
                hint = new RetroText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    X = diameter + 12,
                    Font = RetroFontFamily.Display,
                    TextSize = 9,
                    Text = "CREDITS",
                    Colour = RetroPalette.Cyan,
                    Alpha = 0,
                },
            };
        }

        protected override bool OnHover(HoverEvent e)
        {
            fill.FadeColour(RetroPalette.Cyan.Opacity(0.28f), 160, Easing.OutQuint);
            glyph.FadeColour(RetroPalette.Cyan, 160, Easing.OutQuint);
            disc.FadeEdgeEffectTo(RetroPalette.Cyan.Opacity(0.5f), 200, Easing.OutQuint);

            hint.FadeIn(200, Easing.OutQuint);
            hint.MoveToX(diameter + 6).MoveToX(diameter + 12, 260, Easing.OutQuint);

            return true;
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            fill.FadeColour(RetroPalette.Panel, 260, Easing.OutQuint);
            glyph.FadeColour(RetroPalette.TextDim, 260, Easing.OutQuint);
            disc.FadeEdgeEffectTo(RetroPalette.Cyan.Opacity(0), 260, Easing.OutQuint);

            hint.FadeOut(160, Easing.OutQuint);
        }

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            disc.ScaleTo(0.92f, 80, Easing.OutQuint);
            return true;
        }

        protected override void OnMouseUp(MouseUpEvent e)
        {
            disc.ScaleTo(1f, 220, Easing.OutElastic);
        }

        protected override bool OnClick(ClickEvent e)
        {
            Clicked?.Invoke();
            return true;
        }
    }
}
