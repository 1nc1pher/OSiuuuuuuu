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

namespace OsuClient.Game.Screens.MainMenu
{
    /// <summary>
    /// Which side of the logo a strip button sits on. Decides which edge
    /// carries the accent and which way the button leans on hover — both
    /// point away from the circle, so the pair reads as opening outwards.
    /// </summary>
    public enum StripSide
    {
        Left,
        Right,
    }

    /// <summary>
    /// One half of the menu's navigation strip: a flat slab with an icon and
    /// a word, accented on its outer edge (MENU_REDESIGN_PLAN.md step 8).
    ///
    /// The icons are drawn from primitives rather than imported, the way the
    /// rest of this client's retro furniture is — a triangle for play, two
    /// crossed bars for create.
    ///
    /// Nothing here flashes. The screen's beat flash lives on the window's
    /// own left and right edges instead, the way gameplay's does
    /// (<see cref="Graphics.EdgeGlow"/>) — a button lighting up on the beat
    /// competes with the one thing the buttons are for, which is being
    /// pressed.
    /// </summary>
    public partial class MenuStripButton : CompositeDrawable
    {
        private const float accent_thickness = 5;

        private readonly StripSide side;
        private readonly Color4 accent;

        private readonly Box background;
        private readonly Box accentEdge;
        private readonly Container content;

        public Action? Action;

        public MenuStripButton(StripSide side, Color4 accent, Drawable icon, string label)
        {
            this.side = side;
            this.accent = accent;

            Masking = true;
            CornerRadius = 4;

            bool leftSide = side == StripSide.Left;

            InternalChildren = new Drawable[]
            {
                background = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = ColourInfo.GradientHorizontal(
                        leftSide ? RetroPalette.PanelInset : RetroPalette.Panel,
                        leftSide ? RetroPalette.Panel : RetroPalette.PanelInset),
                },
                accentEdge = new Box
                {
                    RelativeSizeAxes = Axes.Y,
                    Width = accent_thickness,
                    Anchor = leftSide ? Anchor.CentreLeft : Anchor.CentreRight,
                    Origin = leftSide ? Anchor.CentreLeft : Anchor.CentreRight,
                    Colour = accent,
                },
                content = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Child = new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(18, 0),
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Children = new[]
                        {
                            icon.With(d =>
                            {
                                d.Anchor = Anchor.CentreLeft;
                                d.Origin = Anchor.CentreLeft;
                                d.Colour = accent;
                            }),
                            new RetroText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Font = RetroFontFamily.Body,
                                TextSize = 22,
                                Text = label,
                                Colour = RetroPalette.Text,
                            },
                        },
                    },
                },
            };
        }

        /// <summary>A right-pointing triangle, for PLAY.</summary>
        public static Drawable PlayIcon(float size) => new Container
        {
            Size = new Vector2(size),
            // The turned triangle is wrapped rather than turned in place.
            // Rotation pivots about a drawable's Origin, and the layout below
            // sets every icon's origin to its centre-left — so a bare
            // Triangle swung down and right around its own left edge, which
            // is why the play arrow used to sit visibly below its label
            // while the unrotated plus beside it looked fine. Rotating the
            // child about its centre inside a plain box keeps the turn and
            // the layout from touching each other.
            Child = new Triangle
            {
                RelativeSizeAxes = Axes.Both,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Rotation = 90,
            },
        };

        /// <summary>Two crossed bars, for CREATE.</summary>
        public static Drawable PlusIcon(float size)
        {
            const float thickness_fraction = 0.26f;

            return new Container
            {
                Size = new Vector2(size),
                Children = new Drawable[]
                {
                    new Box
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Size = new Vector2(size, size * thickness_fraction),
                    },
                    new Box
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Size = new Vector2(size * thickness_fraction, size),
                    },
                },
            };
        }

        protected override bool OnHover(HoverEvent e)
        {
            accentEdge.ResizeWidthTo(accent_thickness * 2.2f, 220, Easing.OutQuint);
            background.FadeColour(ColourInfo.GradientHorizontal(
                accent.Opacity(0.22f), RetroPalette.Panel), 220, Easing.OutQuint);

            content.MoveToX(side == StripSide.Left ? -8 : 8, 260, Easing.OutQuint);

            FadeEdgeEffectTo(accent.Opacity(0.5f), 200, Easing.OutQuint);

            return true;
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            accentEdge.ResizeWidthTo(accent_thickness, 320, Easing.OutQuint);
            background.FadeColour(restingGradient(), 320, Easing.OutQuint);
            content.MoveToX(0, 320, Easing.OutQuint);
            FadeEdgeEffectTo(accent.Opacity(0), 320, Easing.OutQuint);
        }

        protected override bool OnClick(ClickEvent e)
        {
            Action?.Invoke();
            return true;
        }

        private ColourInfo restingGradient()
        {
            bool leftSide = side == StripSide.Left;

            return ColourInfo.GradientHorizontal(
                leftSide ? RetroPalette.PanelInset : RetroPalette.Panel,
                leftSide ? RetroPalette.Panel : RetroPalette.PanelInset);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            EdgeEffect = new EdgeEffectParameters
            {
                Type = EdgeEffectType.Glow,
                Colour = accent.Opacity(0),
                Radius = 22,
            };
        }
    }
}
