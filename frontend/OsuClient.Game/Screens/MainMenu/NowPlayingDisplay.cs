using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using OsuClient.Game.Graphics;
using osuTK;

namespace OsuClient.Game.Screens.MainMenu
{
    /// <summary>
    /// Credits the song playing behind the menu, top-right
    /// (MENU_REDESIGN_PLAN.md step 5).
    ///
    /// Starts hidden and is only ever shown once a song is actually playing —
    /// an empty library leaves this blank rather than showing an empty frame.
    /// </summary>
    public partial class NowPlayingDisplay : CompositeDrawable
    {
        private readonly RetroText label;
        private readonly RetroText title;
        private readonly RetroText artist;

        public NowPlayingDisplay()
        {
            AutoSizeAxes = Axes.Both;
            Alpha = 0;

            InternalChild = new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 7),
                Children = new Drawable[]
                {
                    label = new RetroText
                    {
                        Anchor = Anchor.TopRight,
                        Origin = Anchor.TopRight,
                        Font = RetroFontFamily.Display,
                        TextSize = 9,
                        Text = "NOW PLAYING",
                        Colour = RetroPalette.TextDim,
                    },
                    title = new RetroText
                    {
                        Anchor = Anchor.TopRight,
                        Origin = Anchor.TopRight,
                        Font = RetroFontFamily.Body,
                        TextSize = 21,
                        Colour = RetroPalette.Text,
                    },
                    artist = new RetroText
                    {
                        Anchor = Anchor.TopRight,
                        Origin = Anchor.TopRight,
                        Font = RetroFontFamily.Body,
                        TextSize = 14,
                        Colour = RetroPalette.TextDim,
                    },
                },
            };
        }

        /// <summary>
        /// Shows a song, or hides the whole display when there isn't one.
        ///
        /// Called once when the menu's song is chosen — never per frame:
        /// <see cref="RetroText"/> rasterizes a fresh texture on every text
        /// change.
        /// </summary>
        public void SetSong(string? songTitle, string? songArtist)
        {
            if (songTitle == null && songArtist == null)
            {
                this.FadeOut(200, Easing.OutQuint);
                return;
            }

            title.Text = songTitle ?? "Unknown title";
            artist.Text = songArtist ?? "Unknown artist";

            // Held back a moment so it arrives after the logo rather than
            // with it — three things fading in at once reads as a flash.
            this.Delay(600).FadeIn(700, Easing.OutQuint);
        }

        /// <summary>The "NOW PLAYING" label, exposed for the accent colour.</summary>
        public RetroText Label => label;
    }
}
