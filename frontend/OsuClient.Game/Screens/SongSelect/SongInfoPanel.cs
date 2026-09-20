using System;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using OsuClient.Game.Beatmaps;
using OsuClient.Game.Graphics;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.SongSelect
{
    /// <summary>
    /// The top-left readout: what the song is, what its rhythm looks like, and
    /// what the selected difficulty is made of (CAROUSEL_REDESIGN_PLAN.md
    /// step 3) — the three groupings the brief asks for, in that order.
    ///
    /// Children are built once and their text swapped on selection, rather
    /// than rebuilt: <see cref="RetroText"/> rasterizes to a texture on every
    /// change, so churning the whole panel through the wheel's scroll would
    /// mean a texture upload per frame.
    /// </summary>
    public partial class SongInfoPanel : CompositeDrawable
    {
        private readonly RetroText title;
        private readonly RetroText artist;

        private readonly RetroText bpmValue;
        private readonly RetroText lengthValue;
        private readonly RetroText objectsValue;

        private readonly RetroText difficultyName;
        private readonly StatColumn circles;
        private readonly StatColumn sliders;
        private readonly StatColumn spinners;
        private readonly StatColumn circleSize;
        private readonly StatColumn approachRate;
        private readonly StatColumn overallDifficulty;

        public SongInfoPanel()
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;

            InternalChildren = new Drawable[]
            {
                new Container
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Masking = true,
                    CornerRadius = 14,
                    BorderThickness = 1.5f,
                    BorderColour = RetroPalette.Cyan.Opacity(0.5f),
                    EdgeEffect = new osu.Framework.Graphics.Effects.EdgeEffectParameters
                    {
                        Type = osu.Framework.Graphics.Effects.EdgeEffectType.Shadow,
                        Colour = RetroPalette.Cyan.Opacity(0.22f),
                        Radius = 18,
                    },
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = RetroPalette.Panel,
                        },
                        screw(Anchor.TopLeft, new Vector2(11, 11)),
                        screw(Anchor.TopRight, new Vector2(-11, 11)),
                        screw(Anchor.BottomLeft, new Vector2(11, -11)),
                        screw(Anchor.BottomRight, new Vector2(-11, -11)),
                        new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Vertical,
                            Spacing = new Vector2(0, 10),
                            Padding = new MarginPadding { Horizontal = 24, Vertical = 20 },
                            Children = new Drawable[]
                            {
                                title = new RetroText
                                {
                                    Font = RetroFontFamily.Body,
                                    TextSize = 27,
                                    Colour = RetroPalette.Text,
                                },
                                artist = new RetroText
                                {
                                    Font = RetroFontFamily.Body,
                                    TextSize = 15,
                                    Colour = RetroPalette.TextDim,
                                },
                                rhythmInfoBox(out bpmValue, out lengthValue, out objectsValue),
                                difficultyName = new RetroText
                                {
                                    Font = RetroFontFamily.Body,
                                    TextSize = 14,
                                    Colour = RetroPalette.Text,
                                },
                                new FillFlowContainer
                                {
                                    RelativeSizeAxes = Axes.X,
                                    AutoSizeAxes = Axes.Y,
                                    Direction = FillDirection.Horizontal,
                                    Spacing = new Vector2(18, 0),
                                    Children = new Drawable[]
                                    {
                                        circles = new StatColumn("CIRCLES", RetroPalette.Cyan),
                                        sliders = new StatColumn("SLIDERS", RetroPalette.Mint),
                                        spinners = new StatColumn("SPINNERS", RetroPalette.Amber),
                                        circleSize = new StatColumn("CS", RetroPalette.Magenta),
                                        approachRate = new StatColumn("AR", RetroPalette.Magenta),
                                        overallDifficulty = new StatColumn("OD", RetroPalette.Violet),
                                    },
                                },
                            },
                        },
                    },
                },
            };
        }

        /// <summary>
        /// A screw head in the panel's corner — the same detail the cassette
        /// buttons carry, so the whole left column reads as one piece of
        /// hardware rather than three unrelated cards.
        /// </summary>
        private static Drawable screw(Anchor anchor, Vector2 position) => new Circle
        {
            Anchor = anchor,
            Origin = Anchor.Centre,
            Position = position,
            Size = new Vector2(5),
            Colour = RetroPalette.Chrome.Opacity(0.28f),
        };

        /// <summary>
        /// The concept art's "Rhythm Info" card: the two numbers that describe
        /// the generated rhythm itself, set apart from the map's osu! stats
        /// because they're the ones this project's backend actually decides.
        /// </summary>
        private static Drawable rhythmInfoBox(out RetroText bpm, out RetroText length, out RetroText objects)
        {
            RetroText bpmText;
            RetroText lengthText;
            RetroText objectsText;

            var box = new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Masking = true,
                CornerRadius = 8,
                BorderThickness = 1.5f,
                BorderColour = RetroPalette.Magenta.Opacity(0.55f),
                Margin = new MarginPadding { Vertical = 4 },
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = RetroPalette.PanelInset,
                    },
                    new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(26, 0),
                        Padding = new MarginPadding { Horizontal = 16, Vertical = 12 },
                        Children = new Drawable[]
                        {
                            readout("BPM", out bpmText),
                            readout("LENGTH", out lengthText),
                            readout("OBJECTS", out objectsText),
                        },
                    },
                },
            };

            bpm = bpmText;
            length = lengthText;
            objects = objectsText;

            return box;
        }

        private static Drawable readout(string label, out RetroText value)
        {
            RetroText valueText;

            var flow = new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 5),
                Children = new Drawable[]
                {
                    new RetroText
                    {
                        Font = RetroFontFamily.Body,
                        TextSize = 10,
                        Text = label,
                        Colour = RetroPalette.TextDim,
                    },
                    valueText = new RetroText
                    {
                        // Press Start 2P: chunky enough that the numbers read
                        // as an instrument panel rather than body copy.
                        Font = RetroFontFamily.Display,
                        TextSize = 15,
                        Colour = RetroPalette.Text,
                    },
                },
            };

            value = valueText;
            return flow;
        }

        /// <summary>
        /// Shows the panel's data for a difficulty, or clears it when null.
        /// <paramref name="accent"/> is the colour that difficulty's cassette
        /// carries, so the two agree; null falls back to the density ramp.
        /// </summary>
        public void SetBeatmap(Beatmap? beatmap, Color4? accent = null)
        {
            if (beatmap == null)
            {
                title.Text = "No beatmap selected";
                artist.Text = string.Empty;
                bpmValue.Text = "--";
                lengthValue.Text = "--";
                objectsValue.Text = "--";
                difficultyName.Text = string.Empty;

                circles.Value = sliders.Value = spinners.Value = "-";
                circleSize.Value = approachRate.Value = overallDifficulty.Value = "-";
                return;
            }

            title.Text = string.IsNullOrWhiteSpace(beatmap.Metadata.Title)
                ? "(untitled)"
                : beatmap.Metadata.Title;

            artist.Text = beatmap.Metadata.Artist;

            bpmValue.Text = $"{beatmap.BPM:0}";
            lengthValue.Text = formatLength(beatmap);
            objectsValue.Text = $"{beatmap.HitObjects.Count}";

            string version = string.IsNullOrWhiteSpace(beatmap.Metadata.Version)
                ? "(unnamed)"
                : beatmap.Metadata.Version;

            difficultyName.Text = $"{version}  —  mapped by {beatmap.Metadata.Creator}";
            difficultyName.Colour = accent ?? RetroPalette.ForDensity(BeatmapStatistics.Density(beatmap));

            var (circleCount, sliderCount, spinnerCount) = BeatmapStatistics.CountObjects(beatmap);

            circles.Value = circleCount.ToString();
            sliders.Value = sliderCount.ToString();
            spinners.Value = spinnerCount.ToString();
            circleSize.Value = $"{beatmap.Difficulty.CircleSize:0.#}";
            approachRate.Value = $"{beatmap.Difficulty.ApproachRate:0.#}";
            overallDifficulty.Value = $"{beatmap.Difficulty.OverallDifficulty:0.#}";
        }

        private static string formatLength(Beatmap beatmap)
        {
            if (beatmap.HitObjects.Count == 0)
                return "0:00";

            var span = TimeSpan.FromMilliseconds(beatmap.LastHitObjectTime);

            return $"{(int)span.TotalMinutes}:{span.Seconds:00}";
        }

        /// <summary>
        /// One labelled stat with a coloured rule under it, like the stat row
        /// in the concept art.
        /// </summary>
        private partial class StatColumn : CompositeDrawable
        {
            private readonly RetroText valueText;

            public string Value
            {
                set => valueText.Text = value;
            }

            public StatColumn(string label, Color4 accent)
            {
                // Fixed width, not auto: the coloured rule is relatively sized
                // on X, which an auto-sizing parent can't resolve.
                Width = 74;
                AutoSizeAxes = Axes.Y;

                InternalChild = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 4),
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 2,
                            Colour = accent,
                        },
                        new RetroText
                        {
                            Font = RetroFontFamily.Body,
                            TextSize = 9,
                            Text = label,
                            Colour = RetroPalette.TextDim,
                        },
                        valueText = new RetroText
                        {
                            Font = RetroFontFamily.Body,
                            TextSize = 14,
                            Colour = RetroPalette.Text,
                        },
                    },
                };
            }
        }
    }
}
