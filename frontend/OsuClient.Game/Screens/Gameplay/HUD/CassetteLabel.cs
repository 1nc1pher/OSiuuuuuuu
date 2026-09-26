using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using OsuClient.Game.Graphics;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.Gameplay.HUD
{
    /// <summary>
    /// What's playing, written on a cassette's paper label: artist and title
    /// in ink on cream, with the difficulty's colour down the label's edge and
    /// its name printed on it — the same colour its cassette had in song
    /// select, so the choice carries through from picking the song to playing
    /// it.
    /// </summary>
    public partial class CassetteLabel : CompositeDrawable
    {
        private const float height = 22;
        private const float spine_width = 7;

        public CassetteLabel(string artist, string title, string difficulty, Color4 accent)
        {
            AutoSizeAxes = Axes.X;
            Height = height;

            InternalChild = new Container
            {
                AutoSizeAxes = Axes.X,
                RelativeSizeAxes = Axes.Y,
                Masking = true,
                CornerRadius = 2,
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = PaperTextBox.Paper,
                    },
                    new Box
                    {
                        RelativeSizeAxes = Axes.Y,
                        Width = spine_width,
                        Colour = accent,
                    },
                    new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.X,
                        RelativeSizeAxes = Axes.Y,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(8, 0),
                        Padding = new MarginPadding { Left = spine_width + 8, Right = 8 },
                        Children = new Drawable[]
                        {
                            new RetroText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Font = RetroFontFamily.Body,
                                TextSize = 13,
                                Colour = PaperTextBox.Ink,
                                Text = string.IsNullOrWhiteSpace(artist) ? title : $"{artist} - {title}",
                            },
                            // The difficulty, stamped in its own colour on a
                            // dark tab, like the cassette's spine print.
                            new Container
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                AutoSizeAxes = Axes.Both,
                                Masking = true,
                                CornerRadius = 2,
                                Children = new Drawable[]
                                {
                                    new Box
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        Colour = PaperTextBox.Ink,
                                    },
                                    new RetroText
                                    {
                                        Margin = new MarginPadding { Horizontal = 5, Vertical = 3 },
                                        Font = RetroFontFamily.Display,
                                        TextSize = 8,
                                        Colour = accent,
                                        Text = difficulty.ToUpperInvariant(),
                                    },
                                },
                            },
                        },
                    },
                },
            };
        }
    }
}
