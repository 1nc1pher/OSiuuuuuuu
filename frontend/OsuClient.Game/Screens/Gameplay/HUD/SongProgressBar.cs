using System;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using OsuClient.Game.Graphics;
using OsuClient.Game.Graphics.Rack;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.Gameplay.HUD
{
    /// <summary>
    /// How far through the beatmap's playable span the run is — from the
    /// first hit object to the last, not the length of the audio file — shown
    /// as tape running between two reels: the supply reel on the left empties
    /// as the take-up reel on the right fills, a playhead rides the tape
    /// between them, and a counter reads the elapsed and total time.
    ///
    /// Here the reels are honest about progress, unlike the generation
    /// screen's, which only say that something is running: their wound sizes
    /// are the fraction played. They turn only while the song does
    /// (<see cref="Running"/>), so pausing stops the tape.
    /// </summary>
    public partial class SongProgressBar : CompositeDrawable
    {
        /// <summary>Exposed so the HUD can lay other elements out clear of the strip.</summary>
        public const float BarHeight = 42;

        private const float reel_size = 40;
        private const float counter_width = 132;
        private const float rail_height = 4;

        private readonly TapeReel supply;
        private readonly TapeReel takeUp;
        private readonly Container played;
        private readonly Container playhead;
        private readonly SegmentReadout counter;

        private double progress;

        public SongProgressBar()
        {
            RelativeSizeAxes = Axes.X;
            Width = 0.92f;
            Height = BarHeight;

            InternalChildren = new Drawable[]
            {
                supply = new TapeReel
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Size = new Vector2(reel_size),
                },
                new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding { Left = reel_size + 10, Right = reel_size + counter_width + 22 },
                    Children = new Drawable[]
                    {
                        // The tape itself, oxide brown, running reel to reel.
                        new Box
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            RelativeSizeAxes = Axes.X,
                            Height = rail_height,
                            Colour = new Color4(0.30f, 0.19f, 0.14f, 0.95f),
                        },
                        // What has already played past the head.
                        played = new Container
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            RelativeSizeAxes = Axes.X,
                            Height = rail_height,
                            Width = 0,
                            Child = new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = RetroPalette.Amber.Opacity(0.85f),
                            },
                        },
                        playhead = new Container
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.Centre,
                            RelativePositionAxes = Axes.X,
                            Size = new Vector2(10, 18),
                            Masking = true,
                            CornerRadius = 2,
                            BorderThickness = 1,
                            BorderColour = RetroPalette.ChromeDark,
                            Children = new Drawable[]
                            {
                                new Box { RelativeSizeAxes = Axes.Both, Colour = RetroPalette.Chrome },
                                // The head's gap, lit where it meets the tape.
                                new Box
                                {
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.Centre,
                                    Size = new Vector2(4, rail_height + 2),
                                    Colour = RetroPalette.Amber,
                                },
                            },
                        },
                    },
                },
                takeUp = new TapeReel
                {
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.CentreRight,
                    X = -(counter_width + 12),
                    Size = new Vector2(reel_size),
                    Reverse = true,
                },
                counter = new SegmentReadout(11)
                {
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.CentreRight,
                    Digits = 11,
                    Text = "00:00/00:00",
                },
            };

            SetProgress(0);
        }

        /// <summary>Whether the tape is moving: false while paused or before the song starts.</summary>
        public bool Running
        {
            set => supply.Running = takeUp.Running = value;
        }

        /// <summary>How far through the map the run is, 0 to 1. Exposed for tests.</summary>
        public double Progress => progress;

        /// <summary>Sets how far through the map the run is, 0 to 1.</summary>
        public void SetProgress(double value)
        {
            progress = Math.Clamp(value, 0, 1);

            float p = (float)progress;

            played.Width = p;
            playhead.X = p;
            supply.TapeFill = 1 - p;
            takeUp.TapeFill = p;
        }

        /// <summary>Sets the counter: time into the map's playable span, and that span's length, in milliseconds.</summary>
        public void SetTime(double elapsed, double total)
        {
            string text = $"{format(elapsed)}/{format(total)}";

            if (counter.Text != text)
                counter.Text = text;
        }

        private static string format(double milliseconds)
        {
            int seconds = (int)Math.Max(0, Math.Floor(milliseconds / 1000));
            return $"{Math.Min(99, seconds / 60):00}:{seconds % 60:00}";
        }
    }
}
