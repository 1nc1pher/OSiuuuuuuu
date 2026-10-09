using System;
using System.Collections.Generic;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using OsuClient.Game.Audio;
using OsuClient.Game.Beatmaps;
using OsuClient.Game.Graphics;
using OsuClient.Game.Graphics.Rack;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.MainMenu
{
    /// <summary>
    /// The song list that drops down from the now-playing credit: every song
    /// the menu can play, scrollable, with the one playing marked. Picking a
    /// row switches the menu's music to it.
    ///
    /// A slab of the menu strip's own panel colour, left translucent so the
    /// spectrum ring and the wallpaper keep showing through it, with the
    /// strip's magenta-to-cyan hairline under its header. Rows borrow the
    /// strip buttons' behaviour too — an accent edge that widens and a wash
    /// of colour on hover — so the list reads as more of the same furniture.
    ///
    /// The screen lays it out every frame through <see cref="Layout"/>
    /// rather than the picker doing so in its own <c>Update</c>: a hidden
    /// <see cref="VisibilityContainer"/> is not present and is never updated,
    /// so a layout done there would be a frame late the first time it opened.
    /// </summary>
    public partial class SongPicker : VisibilityContainer
    {
        public const float PanelWidth = 400;

        private const float row_height = 54;
        private const float header_height = 40;
        private const float footer_padding = 6;
        private const int visible_rows = 7;
        private const float gutter = 52;

        private const double fade_in = 240;
        private const double fade_out = 170;

        private static readonly Color4 panel_fill = RetroPalette.Panel.Opacity(0.64f);

        private readonly Action<BeatmapLibraryEntry> picked;
        private readonly Action dismissed;
        private readonly MenuSoundPlayer? sounds;

        private readonly Container host;
        private readonly Container panel;
        private readonly RackScrollContainer scroll;
        private readonly RetroText countLabel;

        private FillFlowContainer<SongPickerRow>? list;
        private int songCount;
        private string? currentSetName;

        /// <summary>Scroll the current row to the middle as soon as it has been laid out.</summary>
        private bool scrollPending;

        private int lastTickRow = -1;

        /// <summary>Lists still loading in the background.</summary>
        private int pendingLoads;

        /// <summary>
        /// Present while a list is loading even though the picker is hidden.
        /// A hidden container is never updated, and a background load only
        /// finishes when the container's scheduler runs — so without this the
        /// rows would sit loaded but unadded until the first time the list was
        /// opened, which then showed an empty panel for a frame or two.
        /// </summary>
        public override bool IsPresent => pendingLoads > 0 || base.IsPresent;

        /// <param name="picked">Called with the song a row was clicked for.</param>
        /// <param name="dismissed">
        /// Called when the player clicks away from the list. The screen closes
        /// it, so that closing from here and from the credit above it go
        /// through one path and make one sound.
        /// </param>
        public SongPicker(Action<BeatmapLibraryEntry> picked, Action dismissed, MenuSoundPlayer? sounds = null)
        {
            this.picked = picked;
            this.dismissed = dismissed;
            this.sounds = sounds;

            RelativeSizeAxes = Axes.Both;
            Alpha = 0;

            Child = host = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Child = panel = new PanelCatcher
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    Width = PanelWidth,
                    Masking = true,
                    CornerRadius = 4,
                    BorderThickness = 1.5f,
                    BorderColour = RetroPalette.Cyan.Opacity(0.35f),
                    EdgeEffect = new EdgeEffectParameters
                    {
                        Type = EdgeEffectType.Glow,
                        Colour = RetroPalette.Cyan.Opacity(0.12f),
                        Radius = 22,
                    },
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = panel_fill,
                        },
                        new Container
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = header_height,
                            Children = new Drawable[]
                            {
                                new RetroText
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    X = 20,
                                    Font = RetroFontFamily.Display,
                                    TextSize = 9,
                                    Text = "SELECT TRACK",
                                    Colour = RetroPalette.Text,
                                },
                                countLabel = new RetroText
                                {
                                    Anchor = Anchor.CentreRight,
                                    Origin = Anchor.CentreRight,
                                    X = -20,
                                    Font = RetroFontFamily.Display,
                                    TextSize = 8,
                                    Colour = RetroPalette.TextDim,
                                },
                                // The strip's connector hairline, under the header.
                                new Box
                                {
                                    RelativeSizeAxes = Axes.X,
                                    Height = 2,
                                    Anchor = Anchor.BottomLeft,
                                    Origin = Anchor.BottomLeft,
                                    Colour = ColourInfo.GradientHorizontal(
                                        RetroPalette.Magenta.Opacity(0.5f),
                                        RetroPalette.Cyan.Opacity(0.5f)),
                                },
                            },
                        },
                        // The list sits in its own padded box rather than the
                        // scroll container being padded: padding only moves the
                        // content, and the scroll area's own bounds — what it
                        // clips to — would still run up under the header.
                        new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Padding = new MarginPadding { Top = header_height + 2, Bottom = footer_padding, Right = 4 },
                            Child = scroll = new RackScrollContainer
                            {
                                RelativeSizeAxes = Axes.Both,
                                ScrollbarOverlapsContent = false,
                            },
                        },
                    },
                },
            };
        }

        protected override bool StartHidden => true;

        /// <summary>The songs the list holds.</summary>
        public int SongCount => songCount;

        /// <summary>The rows, for tests.</summary>
        public IReadOnlyList<SongPickerRow> Rows => list?.Children ?? (IReadOnlyList<SongPickerRow>)Array.Empty<SongPickerRow>();

        /// <summary>
        /// Sizes and places the panel: its top edge at <paramref name="top"/>,
        /// on the right, and as tall as its rows need up to
        /// <paramref name="available"/> pixels.
        /// </summary>
        public void Layout(float top, float availableHeight, float availableWidth)
        {
            host.Padding = new MarginPadding { Top = top, Right = gutter };

            panel.Width = MathF.Min(PanelWidth, MathF.Max(240, availableWidth - gutter * 2));

            float wanted = header_height + 2 + footer_padding + MathF.Max(1, MathF.Min(songCount, visible_rows)) * row_height;
            float room = MathF.Max(header_height + 2 + footer_padding + row_height, availableHeight - top - gutter);

            panel.Height = MathF.Min(wanted, room);
        }

        /// <summary>
        /// Replaces the list. Built off the update thread, because a row's two
        /// lines of text are each rasterized to a texture and a long library
        /// would otherwise stall the frame the list is built in.
        /// </summary>
        public void SetSongs(IReadOnlyList<BeatmapLibraryEntry> songs)
        {
            var rows = new FillFlowContainer<SongPickerRow>
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
            };

            for (int i = 0; i < songs.Count; i++)
            {
                var entry = songs[i];
                rows.Add(new SongPickerRow(i + 1, entry, () => picked(entry)));
            }

            pendingLoads++;

            LoadComponentAsync(rows, loaded =>
            {
                pendingLoads--;

                list?.Expire();
                list = loaded;
                scroll.Add(loaded);

                songCount = songs.Count;
                countLabel.Text = songs.Count == 1 ? "1 TRACK" : $"{songs.Count} TRACKS";

                applyCurrent();
            });
        }

        /// <summary>Marks the song playing, by set name — the way the rest of the client matches sets.</summary>
        public void SetCurrent(string? setPath)
        {
            currentSetName = setPath == null ? null : BeatmapLibrary.SetNameOf(setPath);
            applyCurrent();
        }

        private void applyCurrent()
        {
            if (list == null)
                return;

            foreach (var row in list.Children)
                row.SetCurrent(currentSetName != null && row.SetName.Equals(currentSetName, StringComparison.OrdinalIgnoreCase));
        }

        protected override void PopIn()
        {
            scrollPending = true;

            this.FadeIn(fade_in, Easing.OutQuint);

            // Drops from under the credit it belongs to.
            panel.MoveToY(-14).MoveToY(0, fade_in * 1.6, Easing.OutQuint);
        }

        protected override void PopOut()
        {
            scrollPending = false;

            this.FadeOut(fade_out, Easing.OutQuint);
            panel.MoveToY(-8, fade_out, Easing.OutQuint);
        }

        protected override void Update()
        {
            base.Update();

            if (list == null)
                return;

            if (scrollPending && list.DrawHeight > 0)
            {
                scrollPending = false;
                scrollToCurrent();
                lastTickRow = tickRow();
                return;
            }

            // One tick for each row the list passes, like the vinyl wheel's
            // notches — only once the scroll has settled where it opened, so
            // that opening isn't itself a run of ticks.
            int row = tickRow();

            if (row != lastTickRow)
            {
                lastTickRow = row;
                sounds?.PlayTick(Time.Current);
            }
        }

        private int tickRow() => (int)MathF.Floor((float)scroll.Current / row_height);

        private void scrollToCurrent()
        {
            if (list == null)
                return;

            int index = 0;

            foreach (var row in list.Children)
            {
                if (row.IsCurrent)
                    break;

                index++;
            }

            if (index >= list.Children.Count)
                index = 0;

            float centred = index * row_height - (scroll.DisplayableContent - row_height) / 2;

            scroll.ScrollTo(MathF.Max(0, centred), false);
        }

        /// <summary>A click that misses the panel dismisses it.</summary>
        protected override bool OnClick(ClickEvent e)
        {
            dismissed();
            return true;
        }

        protected override bool OnMouseDown(MouseDownEvent e) => true;

        /// <summary>
        /// Takes the wheel everywhere while open. Left to fall through, a
        /// scroll over the gap between the panel and the window would reach
        /// whatever is underneath.
        /// </summary>
        protected override bool OnScroll(ScrollEvent e) => true;

        /// <summary>
        /// The panel itself swallows clicks that land between rows or on its
        /// header, so they don't count as clicking away.
        /// </summary>
        private partial class PanelCatcher : Container
        {
            protected override bool OnClick(ClickEvent e) => true;

            protected override bool OnMouseDown(MouseDownEvent e) => true;
        }

        /// <summary>One song in the list.</summary>
        public partial class SongPickerRow : CompositeDrawable
        {
            private const float accent_thickness = 4;
            private const float text_left = 58;
            private const float text_right = 40;

            private readonly Action action;

            private readonly Box wash;
            private readonly Box accentEdge;
            private readonly RetroText title;
            private readonly RetroText artist;
            private readonly Drawable marker;

            private bool current;

            /// <summary>The set's name, the key a row is matched to the playing song by.</summary>
            public string SetName { get; }

            public bool IsCurrent => current;

            public string TitleText { get; }

            public SongPickerRow(int number, BeatmapLibraryEntry entry, Action action)
            {
                this.action = action;

                SetName = BeatmapLibrary.SetNameOf(entry.Path);

                var first = entry.Difficulties.Count > 0 ? entry.Difficulties[0] : null;
                string songTitle = string.IsNullOrWhiteSpace(first?.Metadata.Title) ? entry.DisplayName : first!.Metadata.Title;
                string songArtist = string.IsNullOrWhiteSpace(first?.Metadata.Artist) ? "Unknown artist" : first!.Metadata.Artist;

                TitleText = songTitle;

                RelativeSizeAxes = Axes.X;
                Height = row_height;

                InternalChildren = new Drawable[]
                {
                    wash = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = ColourInfo.GradientHorizontal(RetroPalette.Magenta.Opacity(0), RetroPalette.Magenta.Opacity(0)),
                    },
                    accentEdge = new Box
                    {
                        RelativeSizeAxes = Axes.Y,
                        Width = accent_thickness,
                        Colour = RetroPalette.Cyan,
                        Alpha = 0,
                    },
                    new RetroText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        X = 20,
                        Font = RetroFontFamily.Display,
                        TextSize = 8,
                        Text = number.ToString("00"),
                        Colour = RetroPalette.TextDim,
                        Alpha = 0.7f,
                    },
                    title = new RetroText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.BottomLeft,
                        X = text_left,
                        Y = -1,
                        Font = RetroFontFamily.Body,
                        TextSize = 15,
                        Text = songTitle,
                        Colour = RetroPalette.Text,
                    },
                    artist = new RetroText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.TopLeft,
                        X = text_left,
                        Y = 4,
                        Font = RetroFontFamily.Body,
                        TextSize = 11,
                        Text = songArtist,
                        Colour = RetroPalette.TextDim,
                    },
                    // Playing marker: the strip's own play triangle, small.
                    marker = new Container
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        X = -18,
                        Size = new Vector2(9),
                        Alpha = 0,
                        Child = new Triangle
                        {
                            RelativeSizeAxes = Axes.Both,
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Rotation = 90,
                            Colour = RetroPalette.Cyan,
                        },
                    },
                    // A hairline between rows.
                    new Box
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 1,
                        Anchor = Anchor.BottomLeft,
                        Origin = Anchor.BottomLeft,
                        Colour = Color4.White,
                        Alpha = 0.06f,
                    },
                };
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                // A long title is cut to fit, with dots, rather than running
                // under the marker or out of the panel.
                float room = SongPicker.PanelWidth - text_left - text_right;

                fit(title, room);
                fit(artist, room);
            }

            private static void fit(RetroText text, float room)
            {
                string full = text.Text;

                // The text's width is only known once rasterized, so shorten in
                // proportion to how far over it is rather than a letter at a
                // time, which would rasterize it again for every one.
                for (int attempt = 0; attempt < 3 && text.Width > room && text.Text.Length > 4; attempt++)
                {
                    int keep = (int)(text.Text.Length * (room / text.Width)) - 3;

                    text.Text = full[..Math.Max(1, Math.Min(keep, full.Length - 1))].TrimEnd() + "...";
                }
            }

            /// <summary>Whether this is the song playing: lit cyan, with an edge and a marker.</summary>
            public void SetCurrent(bool value)
            {
                current = value;

                accentEdge.FadeTo(value || IsHovered ? 1 : 0, 160, Easing.OutQuint);
                accentEdge.FadeColour(value ? RetroPalette.Cyan : RetroPalette.Magenta, 160, Easing.OutQuint);
                title.FadeColour(value ? RetroPalette.Cyan : RetroPalette.Text, 200, Easing.OutQuint);
                marker.FadeTo(value ? 1 : 0, 200, Easing.OutQuint);

                restWash();
            }

            private void restWash()
            {
                var accent = current ? RetroPalette.Cyan : RetroPalette.Magenta;

                wash.FadeColour(
                    ColourInfo.GradientHorizontal(accent.Opacity(current ? 0.12f : 0), accent.Opacity(0)),
                    200, Easing.OutQuint);
            }

            protected override bool OnHover(HoverEvent e)
            {
                var accent = current ? RetroPalette.Cyan : RetroPalette.Magenta;

                accentEdge.FadeTo(1, 120, Easing.OutQuint);
                accentEdge.ResizeWidthTo(accent_thickness * 1.6f, 200, Easing.OutQuint);
                wash.FadeColour(ColourInfo.GradientHorizontal(accent.Opacity(0.24f), accent.Opacity(0)), 160, Easing.OutQuint);

                return true;
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                accentEdge.FadeTo(current ? 1 : 0, 220, Easing.OutQuint);
                accentEdge.ResizeWidthTo(accent_thickness, 260, Easing.OutQuint);
                restWash();
            }

            protected override bool OnClick(ClickEvent e)
            {
                action();
                return true;
            }
        }
    }
}
