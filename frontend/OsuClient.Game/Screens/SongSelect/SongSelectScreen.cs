using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Screens;
using OsuClient.Game.Beatmaps;
using OsuClient.Game.Screens.Gameplay;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.SongSelect
{
    /// <summary>
    /// Browses the songs directory and lets a difficulty be picked.
    ///
    /// The directory comes from the constructor when given, otherwise from
    /// <see cref="BeatmapLibrary.ResolveDefaultSongsDirectory"/> — which points
    /// at the backend's <c>data/output/</c> when running from inside the repo,
    /// per FRONTEND_PLAN.md §4.
    /// </summary>
    public partial class SongSelectScreen : Screen
    {
        private readonly string? requestedDirectory;
        private readonly string? preferredSetPath;

        private string? songsDirectory;
        private IReadOnlyList<BeatmapLibraryEntry> entries = new List<BeatmapLibraryEntry>();

        private BeatmapCarousel carousel = null!;
        private SpriteText pathLabel = null!;
        private SpriteText statusLabel = null!;
        private SpriteText detailsLabel = null!;
        private Container errorBanner = null!;
        private SpriteText errorText = null!;

        /// <summary>
        /// <paramref name="preferredSetPath"/> starts the carousel on that set
        /// rather than the first one -- how a freshly generated map arrives
        /// already highlighted (FRONTEND_PLAN.md Phase 7).
        /// </summary>
        public SongSelectScreen(string? songsDirectory = null, string? preferredSetPath = null)
        {
            requestedDirectory = songsDirectory;
            this.preferredSetPath = preferredSetPath;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            // Runs on the load thread, so scanning the directory doesn't stall
            // the update thread even with many beatmaps present.
            songsDirectory = requestedDirectory ?? BeatmapLibrary.ResolveDefaultSongsDirectory();
            entries = BeatmapLibrary.Load(songsDirectory);

            InternalChildren = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(0.08f, 0.08f, 0.12f, 1f),
                },
                new GridContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    RowDimensions = new[]
                    {
                        new Dimension(GridSizeMode.Absolute, 96),
                        new Dimension(),
                        new Dimension(GridSizeMode.Absolute, 96),
                    },
                    Content = new[]
                    {
                        new Drawable[] { createHeader() },
                        new Drawable[] { carousel = new BeatmapCarousel() },
                        new Drawable[] { createFooter() },
                    },
                },
            };

            carousel.SelectionChanged += onSelectionChanged;
            carousel.SelectionConfirmed += onSelectionConfirmed;
        }

        private Drawable createHeader() => new Container
        {
            RelativeSizeAxes = Axes.Both,
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(0.12f, 0.12f, 0.18f, 1f),
                },
                new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 4),
                    Padding = new MarginPadding { Horizontal = 20 },
                    Children = new Drawable[]
                    {
                        new SpriteText
                        {
                            Text = "Song Select",
                            Font = FontUsage.Default.With(size: 26),
                            Colour = Color4.White,
                        },
                        statusLabel = new SpriteText
                        {
                            Font = FontUsage.Default.With(size: 14),
                            Colour = new Color4(0.7f, 0.7f, 0.8f, 1f),
                        },
                        pathLabel = new SpriteText
                        {
                            Font = FontUsage.Default.With(size: 12),
                            Colour = new Color4(0.5f, 0.5f, 0.6f, 1f),
                        },
                    },
                },
            },
        };

        private Drawable createFooter() => new Container
        {
            RelativeSizeAxes = Axes.Both,
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(0.12f, 0.12f, 0.18f, 1f),
                },
                errorBanner = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Alpha = 0,
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = new Color4(0.45f, 0.12f, 0.12f, 1f),
                        },
                        errorText = new SpriteText
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Font = FontUsage.Default.With(size: 15),
                            Colour = Color4.White,
                        },
                    },
                },
                new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 4),
                    Padding = new MarginPadding { Horizontal = 20 },
                    Children = new Drawable[]
                    {
                        detailsLabel = new SpriteText
                        {
                            Font = FontUsage.Default.With(size: 15),
                            Colour = Color4.White,
                        },
                        new SpriteText
                        {
                            Text = "Up/Down select   •   Enter or click again to play   •   Escape to go back",
                            Font = FontUsage.Default.With(size: 12),
                            Colour = new Color4(0.6f, 0.6f, 0.7f, 1f),
                        },
                    },
                },
            },
        };

        protected override void LoadComplete()
        {
            base.LoadComplete();

            pathLabel.Text = songsDirectory ?? "no songs directory configured";

            carousel.SetEntries(entries, emptyMessageFor(), preferredSetPath);
            updateStatus();
        }

        private string emptyMessageFor()
        {
            if (songsDirectory == null)
            {
                return $"No songs directory configured.\n"
                       + $"Set {BeatmapLibrary.SongsDirectoryEnvironmentVariable} or pass --songs <dir>.";
            }

            if (!System.IO.Directory.Exists(songsDirectory))
                return $"Songs directory does not exist:\n{songsDirectory}";

            return "No beatmaps found.\nGenerate one with: python src/main.py data/raw/song.mp3";
        }

        private void updateStatus()
        {
            int valid = entries.Count(e => e.IsValid);
            int broken = entries.Count - valid;

            statusLabel.Text = $"{valid} beatmap set{(valid == 1 ? "" : "s")}"
                               + (broken > 0 ? $"   •   {broken} failed to load" : string.Empty);

            if (broken > 0)
            {
                var first = entries.First(e => !e.IsValid);

                showError($"{broken} file{(broken == 1 ? "" : "s")} could not be loaded — "
                          + $"e.g. {System.IO.Path.GetFileName(first.Path)}: {first.Error}");
            }
        }

        private void showError(string message)
        {
            errorText.Text = message;
            errorBanner.FadeIn(200, Easing.OutQuint);
        }

        private void onSelectionChanged(BeatmapSelection selection)
        {
            var beatmap = selection.Beatmap;
            var (circles, sliders, spinners) = DifficultyIcon.CountObjects(beatmap);

            detailsLabel.Text =
                $"{beatmap.Metadata.Artist} - {beatmap.Metadata.Title} [{beatmap.Metadata.Version}]   •   "
                + $"{beatmap.BPM:0.#} BPM   •   AR {beatmap.Difficulty.ApproachRate} "
                + $"CS {beatmap.Difficulty.CircleSize} OD {beatmap.Difficulty.OverallDifficulty}   •   "
                + $"{circles} circles / {sliders} sliders / {spinners} spinners";
        }

        private void onSelectionConfirmed(BeatmapSelection selection)
        {
            if (!this.IsCurrentScreen())
                return;

            this.Push(new PlayerScreen(selection));
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            switch (e.Key)
            {
                case osuTK.Input.Key.Enter:
                case osuTK.Input.Key.KeypadEnter:
                    carousel.ConfirmSelection();
                    return true;

                case osuTK.Input.Key.Escape:
                    this.Exit();
                    return true;
            }

            return base.OnKeyDown(e);
        }

        public override void OnEntering(ScreenTransitionEvent e)
        {
            base.OnEntering(e);

            this.FadeInFromZero(250, Easing.OutQuint);
        }

        public override void OnResuming(ScreenTransitionEvent e)
        {
            base.OnResuming(e);

            this.FadeIn(200, Easing.OutQuint);
        }

        public override bool OnExiting(ScreenExitEvent e)
        {
            this.FadeOut(200, Easing.OutQuint);

            return base.OnExiting(e);
        }
    }
}
