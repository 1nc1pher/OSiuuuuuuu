using System;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osu.Framework.Screens;
using OsuClient.Game.Beatmaps;
using OsuClient.Game.Graphics;
using OsuClient.Game.Screens.Gameplay;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.Results
{
    /// <summary>
    /// The end-of-play summary: grade, score, accuracy, max combo and the
    /// judgement breakdown, or a failed run's state at the point it ended.
    ///
    /// Takes a snapshot of the numbers rather than the live
    /// <see cref="ScoreProcessor"/>, so what's shown can't shift underneath
    /// the screen after the play is over.
    /// </summary>
    public partial class ResultsScreen : Screen
    {
        /// <summary>One play's final numbers.</summary>
        public sealed class Result
        {
            public required string Title { get; init; }
            public required string Difficulty { get; init; }
            public required Grade Grade { get; init; }
            public required long Score { get; init; }
            public required double Accuracy { get; init; }
            public required int MaxCombo { get; init; }
            public required int CountGreat { get; init; }
            public required int CountOk { get; init; }
            public required int CountMeh { get; init; }
            public required int CountMiss { get; init; }
            public required bool Failed { get; init; }
            public string? BackgroundPath { get; init; }

            /// <summary>Whether this run set a new best on its map. Filled in by gameplay once it has asked the store.</summary>
            public bool NewHighScore { get; set; }

            /// <summary>The best score the map had before this run, or null for a first play.</summary>
            public long? PreviousBest { get; set; }

            public static Result From(Beatmap beatmap, ScoreProcessor score, bool failed, string? backgroundPath = null) => new Result
            {
                Title = $"{beatmap.Metadata.Artist} - {beatmap.Metadata.Title}",
                Difficulty = beatmap.Metadata.Version,
                Grade = score.Grade,
                Score = score.Score,
                Accuracy = score.Accuracy,
                MaxCombo = score.MaxCombo,
                CountGreat = score.CountGreat,
                CountOk = score.CountOk,
                CountMeh = score.CountMeh,
                CountMiss = score.CountMiss,
                Failed = failed,
                BackgroundPath = backgroundPath,
            };
        }

        private readonly Result result;

        /// <summary>The run on display. Exposed for tests.</summary>
        public Result ShownResult => result;

        /// <summary>
        /// Plays the same map again. Offered on a failed run, where trying
        /// again is the likeliest next move; the screen that pushed this one
        /// does the actual restarting once this has closed.
        /// </summary>
        public Action? Retry { get; init; }

        private bool offersRetry => result.Failed && Retry != null;

        public ResultsScreen(Result result)
        {
            this.result = result;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChildren = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(0.07f, 0.07f, 0.11f, 1f),
                },
                // Blurred rather than shown sharp: this screen is read top to
                // bottom, not played over, so the background can sit further
                // back and stay purely atmospheric instead of competing with
                // the numbers for attention.
                new BufferedContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    BlurSigma = new Vector2(16f),
                    Child = new BeatmapBackground(result.BackgroundPath),
                },
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(0f, 0f, 0f, 0.45f),
                },
                new FillFlowContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 8),
                    Children = new Drawable[]
                    {
                        new RetroText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Text = result.Title,
                            Font = RetroFontFamily.Body,
                            TextSize = 24,
                            Colour = Color4.White,
                        },
                        new RetroText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Text = $"[{result.Difficulty}]",
                            Font = RetroFontFamily.Body,
                            TextSize = 14,
                            Colour = new Color4(0.7f, 0.7f, 0.8f, 1f),
                            Margin = new MarginPadding { Bottom = 10 },
                        },
                        createGrade(),
                        new RetroText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Text = $"{result.Score:N0}",
                            Font = RetroFontFamily.Display,
                            TextSize = 32,
                            Colour = Color4.White,
                            Margin = new MarginPadding { Top = 6 },
                        },
                        new RetroText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Text = $"{result.Accuracy:0.00}%   •   {result.MaxCombo}x max combo",
                            Font = RetroFontFamily.Body,
                            TextSize = 17,
                            Colour = new Color4(0.85f, 0.85f, 0.92f, 1f),
                        },
                        createHighScoreLine(),
                        createJudgementBreakdown(),
                        offersRetry
                            ? new PauseButton("Retry", retry_colour, retry)
                            {
                                Margin = new MarginPadding { Top = 26 },
                            }
                            : Empty(),
                        new RetroText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Text = offersRetry
                                ? "R to retry   ·   Escape or click elsewhere to return to song select"
                                : "Escape or click to return to song select",
                            Font = RetroFontFamily.Body,
                            TextSize = 13,
                            Colour = new Color4(0.55f, 0.55f, 0.65f, 1f),
                            Margin = new MarginPadding { Top = 24 },
                        },
                    },
                },
            };
        }

        /// <summary>
        /// Where this run stands against the map's best: a badge for a new
        /// one, with what it beat, or the standing best to aim at. Nothing on
        /// a first play that failed, where there is neither.
        /// </summary>
        private Drawable createHighScoreLine()
        {
            if (result.NewHighScore)
            {
                var badge = new FillFlowContainer
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 4),
                    Margin = new MarginPadding { Top = 6 },
                    Children = new Drawable[]
                    {
                        new RetroText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Text = "NEW HIGH SCORE",
                            Font = RetroFontFamily.Display,
                            TextSize = 18,
                            Colour = RetroPalette.Amber,
                        },
                        new RetroText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Text = result.PreviousBest is long previous
                                ? $"previous best {previous:N0}"
                                : "first clear of this map",
                            Font = RetroFontFamily.Body,
                            TextSize = 12,
                            Colour = RetroPalette.Amber.Opacity(0.7f),
                        },
                    },
                };

                // A slow pulse, so it is noticed without shouting.
                badge.OnLoadComplete += _ =>
                    badge.ScaleTo(1.06f, 600, Easing.InOutSine).Then().ScaleTo(1f, 600, Easing.InOutSine).Loop();

                return badge;
            }

            if (result.PreviousBest is long best)
            {
                return new RetroText
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Text = $"HIGH SCORE  {best:N0}",
                    Font = RetroFontFamily.Body,
                    TextSize = 13,
                    Colour = new Color4(0.7f, 0.7f, 0.8f, 1f),
                    Margin = new MarginPadding { Top = 6 },
                };
            }

            return Empty();
        }

        private Drawable createGrade() => new RetroText
        {
            Anchor = Anchor.TopCentre,
            Origin = Anchor.TopCentre,
            Text = result.Failed ? "FAILED" : result.Grade.ToString(),
            Font = RetroFontFamily.Display,
            TextSize = result.Failed ? 34 : 48,
            Colour = result.Failed ? new Color4(1f, 0.35f, 0.35f, 1f) : ColourForGrade(result.Grade),
        };

        private Drawable createJudgementBreakdown() => new FillFlowContainer
        {
            Anchor = Anchor.TopCentre,
            Origin = Anchor.TopCentre,
            AutoSizeAxes = Axes.Both,
            Direction = FillDirection.Horizontal,
            Spacing = new Vector2(24, 0),
            Margin = new MarginPadding { Top = 18 },
            Children = new Drawable[]
            {
                judgementCount("300", result.CountGreat, new Color4(0.4f, 0.8f, 1f, 1f)),
                judgementCount("100", result.CountOk, new Color4(0.4f, 0.9f, 0.4f, 1f)),
                judgementCount("50", result.CountMeh, new Color4(0.95f, 0.8f, 0.3f, 1f)),
                judgementCount("miss", result.CountMiss, new Color4(1f, 0.3f, 0.3f, 1f)),
            },
        };

        private static Drawable judgementCount(string label, int count, Color4 colour) => new FillFlowContainer
        {
            AutoSizeAxes = Axes.Both,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, 2),
            Children = new Drawable[]
            {
                new RetroText
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Text = label,
                    Font = RetroFontFamily.Body,
                    TextSize = 13,
                    Colour = colour,
                },
                new RetroText
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Text = count.ToString(),
                    Font = RetroFontFamily.Display,
                    TextSize = 18,
                    Colour = Color4.White,
                },
            },
        };

        public static Color4 ColourForGrade(Grade grade) => grade switch
        {
            Grade.SS => new Color4(1f, 0.85f, 0.3f, 1f),
            Grade.S => new Color4(1f, 0.8f, 0.2f, 1f),
            Grade.A => new Color4(0.4f, 0.9f, 0.4f, 1f),
            Grade.B => new Color4(0.4f, 0.75f, 1f, 1f),
            Grade.C => new Color4(0.85f, 0.5f, 1f, 1f),
            _ => new Color4(1f, 0.45f, 0.45f, 1f),
        };

        private static readonly Color4 retry_colour = new Color4(1f, 0.78f, 0.35f, 1f);

        private bool retrying;

        private void retry()
        {
            if (retrying || !this.IsCurrentScreen())
                return;

            retrying = true;
            Retry?.Invoke();
            this.Exit();
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (e.Key == osuTK.Input.Key.Escape)
            {
                this.Exit();
                return true;
            }

            if (e.Key == osuTK.Input.Key.R && offersRetry && !e.Repeat)
            {
                retry();
                return true;
            }

            return base.OnKeyDown(e);
        }

        protected override bool OnClick(ClickEvent e)
        {
            this.Exit();
            return true;
        }

        public override void OnEntering(ScreenTransitionEvent e)
        {
            base.OnEntering(e);

            this.FadeInFromZero(300, Easing.OutQuint);
        }

        public override bool OnExiting(ScreenExitEvent e)
        {
            this.FadeOut(200, Easing.OutQuint);

            return base.OnExiting(e);
        }
    }
}
