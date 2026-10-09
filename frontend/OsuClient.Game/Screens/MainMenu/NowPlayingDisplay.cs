using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
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
    ///
    /// Also the button that opens the song list: the chevron beside the label
    /// says so, the label and chevron light up under the pointer, and the
    /// chevron turns over while the list is out.
    /// </summary>
    public partial class NowPlayingDisplay : CompositeDrawable
    {
        private readonly RetroText label;
        private readonly RetroText title;
        private readonly RetroText artist;
        private readonly Triangle chevron;

        private bool open;

        /// <summary>Raised when the credit is clicked. The screen decides what that opens.</summary>
        public Action? Clicked;

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
                    new FillFlowContainer
                    {
                        Anchor = Anchor.TopRight,
                        Origin = Anchor.TopRight,
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(8, 0),
                        Children = new Drawable[]
                        {
                            label = new RetroText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Font = RetroFontFamily.Display,
                                TextSize = 9,
                                Text = "NOW PLAYING",
                                Colour = RetroPalette.TextDim,
                            },
                            // The triangle is turned inside a plain box: a drawable
                            // rotates about its origin, and the flow sets this
                            // one's origin to its left edge, so turning the
                            // triangle itself swung it over the label.
                            new Container
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Size = new Vector2(9, 6),
                                Child = chevron = new Triangle
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.Centre,
                                    Rotation = 180,
                                    Colour = RetroPalette.TextDim,
                                },
                            },
                        },
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

        /// <summary>
        /// Swaps the credit for a song that has just started: the old one
        /// fades out, the new one fades in — no held-back entrance, which is
        /// for the menu opening, not for a song following another.
        /// </summary>
        public void ChangeSong(string? songTitle, string? songArtist)
        {
            ClearTransforms();

            this.FadeOut(250, Easing.OutQuint).OnComplete(_ =>
            {
                if (songTitle == null && songArtist == null)
                    return;

                title.Text = songTitle ?? "Unknown title";
                artist.Text = songArtist ?? "Unknown artist";

                this.FadeIn(450, Easing.OutQuint);
            });
        }

        /// <summary>The "NOW PLAYING" label, exposed for the accent colour.</summary>
        public RetroText Label => label;

        /// <summary>
        /// Whether the song list is out, which keeps the label lit and turns
        /// the chevron over to point back up at what will close it.
        /// </summary>
        public void SetOpen(bool value)
        {
            open = value;

            // Points down shut, up open.
            chevron.RotateTo(open ? 0 : 180, 260, Easing.OutQuint);
            applyHighlight(IsHovered);
        }

        private void applyHighlight(bool hovered)
        {
            var colour = hovered || open ? RetroPalette.Cyan : RetroPalette.TextDim;

            label.FadeColour(colour, 160, Easing.OutQuint);
            chevron.FadeColour(colour, 160, Easing.OutQuint);
        }

        protected override bool OnHover(HoverEvent e)
        {
            applyHighlight(true);
            title.FadeColour(RetroPalette.Cyan, 160, Easing.OutQuint);
            return true;
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            applyHighlight(false);
            title.FadeColour(RetroPalette.Text, 260, Easing.OutQuint);
        }

        protected override bool OnClick(ClickEvent e)
        {
            Clicked?.Invoke();
            return true;
        }
    }
}
