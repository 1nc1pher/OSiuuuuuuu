using System;
using System.Collections.Generic;
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
    /// Who made the game: a card over the menu, opened from the info button.
    ///
    /// Dressed as the rest of the menu is. The window behind is dimmed rather
    /// than hidden, so the spectrum ring keeps moving under it, with the
    /// same scanlines song select lays over its picture. The card is the
    /// strip's panel colour with its magenta-to-cyan hairline across the top,
    /// and each group of names is set against an accent edge the way the
    /// strip's buttons are — magenta for the people who made it, cyan for
    /// the person who supervised it, the same two colours as CREATE and PLAY.
    /// Headings are in the 8-bit face the logo uses; names in the one the
    /// song titles are set in.
    ///
    /// Anything dismisses it: a click anywhere, or the same keys that back
    /// out of the menu.
    /// </summary>
    public partial class CreditsOverlay : VisibilityContainer
    {
        private const float card_width = 520;
        private const double fade_in = 280;
        private const double fade_out = 200;

        /// <summary>The order the groups arrive in, and how far apart.</summary>
        private const double stagger = 110;

        private readonly Action dismissed;
        private readonly Container card;
        private readonly List<Drawable> groups = new List<Drawable>();

        /// <param name="dismissed">
        /// Called when the player clicks anywhere. The screen closes it, so
        /// that clicking and pressing Escape go through one path.
        /// </param>
        public CreditsOverlay(Action dismissed)
        {
            this.dismissed = dismissed;

            RelativeSizeAxes = Axes.Both;
            Alpha = 0;

            InternalChildren = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = RetroPalette.Void,
                    Alpha = 0.72f,
                },
                new ScanlineOverlay(),
                card = new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Width = card_width,
                    AutoSizeAxes = Axes.Y,
                    Masking = true,
                    CornerRadius = 6,
                    BorderThickness = 1.5f,
                    BorderColour = RetroPalette.Cyan.Opacity(0.35f),
                    EdgeEffect = new EdgeEffectParameters
                    {
                        Type = EdgeEffectType.Glow,
                        Colour = RetroPalette.Magenta.Opacity(0.16f),
                        Radius = 36,
                    },
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = RetroPalette.Panel.Opacity(0.92f),
                        },
                        // The strip's connector hairline, across the top.
                        new Box
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 3,
                            Colour = ColourInfo.GradientHorizontal(RetroPalette.Magenta, RetroPalette.Cyan),
                        },
                        new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Vertical,
                            Padding = new MarginPadding { Horizontal = 48, Top = 44, Bottom = 36 },
                            Spacing = new Vector2(0, 30),
                            Children = new Drawable[]
                            {
                                track(new RetroText
                                {
                                    Font = RetroFontFamily.Display,
                                    TextSize = 20,
                                    Text = "CREDITS",
                                    Colour = RetroPalette.Text,
                                }),
                                track(group("DEVELOPED BY", RetroPalette.Magenta,
                                    ("Arafat Bin A Sattar Inan", "CSE, BUET"),
                                    ("Adiba Noor Morshed", "CSE, BUET"))),
                                track(group("SUPERVISED BY", RetroPalette.Cyan,
                                    ("Rokonuzzaman Sojib", "Lecturer, CSE, BUET"))),
                                track(new RetroText
                                {
                                    Font = RetroFontFamily.Display,
                                    TextSize = 8,
                                    Text = "CLICK ANYWHERE   ·   ESC TO CLOSE",
                                    Colour = RetroPalette.TextDim,
                                    Margin = new MarginPadding { Top = 10 },
                                }),
                            },
                        },
                    },
                },
            };
        }

        protected override bool StartHidden => true;

        /// <summary>Remembers each group so they can be brought in one after another.</summary>
        private T track<T>(T drawable)
            where T : Drawable
        {
            groups.Add(drawable);
            return drawable;
        }

        /// <summary>
        /// A heading over its names, set against an accent edge. Each name is
        /// followed by a dimmer line for where they are from.
        /// </summary>
        private static Drawable group(string heading, Color4 accent, params (string Name, string Detail)[] people)
        {
            var names = new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 16),
            };

            foreach (var (name, detail) in people)
            {
                names.Add(new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 5),
                    Children = new Drawable[]
                    {
                        new RetroText
                        {
                            Font = RetroFontFamily.Body,
                            TextSize = 24,
                            Text = name,
                            Colour = RetroPalette.Text,
                        },
                        new RetroText
                        {
                            Font = RetroFontFamily.Body,
                            TextSize = 14,
                            Text = detail,
                            Colour = RetroPalette.TextDim,
                        },
                    },
                });
            }

            return new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Y,
                        Width = 4,
                        Colour = accent,
                    },
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Padding = new MarginPadding { Left = 24 },
                        Spacing = new Vector2(0, 16),
                        Children = new Drawable[]
                        {
                            new RetroText
                            {
                                Font = RetroFontFamily.Display,
                                TextSize = 10,
                                Text = heading,
                                Colour = accent,
                            },
                            names,
                        },
                    },
                },
            };
        }

        protected override void PopIn()
        {
            this.FadeIn(fade_in, Easing.OutQuint);

            card.ScaleTo(0.94f).ScaleTo(1f, fade_in * 1.8, Easing.OutQuint);
            card.FadeInFromZero(fade_in * 1.2, Easing.OutQuint);

            // Each group arrives a beat after the one above it, sliding in a
            // little from the left like the strip's buttons do — all at once
            // reads as a flash, in the way the menu's own entrance avoids.
            for (int i = 0; i < groups.Count; i++)
            {
                var group = groups[i];

                group.ClearTransforms();
                group.FadeOut().MoveToX(-14)
                     .Delay(140 + i * stagger)
                     .FadeIn(420, Easing.OutQuint)
                     .MoveToX(0, 520, Easing.OutQuint);
            }
        }

        protected override void PopOut()
        {
            this.FadeOut(fade_out, Easing.OutQuint);
            card.ScaleTo(0.97f, fade_out, Easing.OutQuint);
        }

        /// <summary>Clicking anywhere — the card included — puts it away.</summary>
        protected override bool OnClick(ClickEvent e)
        {
            dismissed();
            return true;
        }

        protected override bool OnMouseDown(MouseDownEvent e) => true;

        protected override bool OnScroll(ScrollEvent e) => true;
    }
}
