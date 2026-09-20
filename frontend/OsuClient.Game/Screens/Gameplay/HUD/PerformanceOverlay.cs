using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using OsuClient.Game.Graphics;
using OsuClient.Game.Screens.Results;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.Gameplay.HUD
{
    /// <summary>
    /// Shown over a short gap with nothing to hit
    /// (<see cref="BreakKind.Performance"/>) — too brief to be worth a skip
    /// button, but long enough that idling through it blank would feel like
    /// dead air. Dims the screen and reads out the same numbers the results
    /// screen ends on: grade, score, accuracy and the judgement breakdown, all
    /// live off the run so far.
    /// </summary>
    public partial class PerformanceOverlay : VisibilityContainer
    {
        private const double fade_duration = 150;

        private RetroText gradeText = null!;
        private RetroText scoreText = null!;
        private RetroText accuracyText = null!;
        private RetroText greatCount = null!;
        private RetroText okCount = null!;
        private RetroText mehCount = null!;
        private RetroText missCount = null!;

        public PerformanceOverlay()
        {
            RelativeSizeAxes = Axes.Both;

            InternalChildren = new Drawable[]
            {
                // Lighter than the skip overlay's dim: the point here is
                // "here's how you're doing," not "gameplay has stepped away."
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(0f, 0f, 0f, 0.55f),
                },
                new FillFlowContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 6),
                    Children = new Drawable[]
                    {
                        new RetroText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Font = RetroFontFamily.Body,
                            TextSize = 13,
                            Colour = new Color4(0.6f, 0.6f, 0.7f, 1f),
                            Text = "CURRENT PERFORMANCE",
                            Margin = new MarginPadding { Bottom = 4 },
                        },
                        gradeText = new RetroText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Font = RetroFontFamily.Display,
                            TextSize = 40,
                            Colour = Color4.White,
                        },
                        scoreText = new RetroText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Font = RetroFontFamily.Display,
                            TextSize = 26,
                            Colour = Color4.White,
                        },
                        accuracyText = new RetroText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Font = RetroFontFamily.Body,
                            TextSize = 15,
                            Colour = new Color4(0.85f, 0.85f, 0.92f, 1f),
                        },
                        new FillFlowContainer
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            AutoSizeAxes = Axes.Both,
                            Direction = FillDirection.Horizontal,
                            Spacing = new Vector2(18, 0),
                            Margin = new MarginPadding { Top = 10 },
                            Children = new Drawable[]
                            {
                                judgementCount("300", out greatCount, new Color4(0.4f, 0.8f, 1f, 1f)),
                                judgementCount("100", out okCount, new Color4(0.4f, 0.9f, 0.4f, 1f)),
                                judgementCount("50", out mehCount, new Color4(0.95f, 0.8f, 0.3f, 1f)),
                                judgementCount("miss", out missCount, new Color4(1f, 0.3f, 0.3f, 1f)),
                            },
                        },
                    },
                },
            };
        }

        private static Drawable judgementCount(string label, out RetroText countText, Color4 colour)
        {
            RetroText count;

            var container = new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 1),
                Children = new Drawable[]
                {
                    new RetroText
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Text = label,
                        Font = RetroFontFamily.Body,
                        TextSize = 11,
                        Colour = colour,
                    },
                    count = new RetroText
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Font = RetroFontFamily.Display,
                        TextSize = 15,
                        Colour = Color4.White,
                    },
                },
            };

            countText = count;
            return container;
        }

        protected override bool StartHidden => true;

        protected override void PopIn() => this.FadeIn(fade_duration, Easing.OutQuint);

        protected override void PopOut() => this.FadeOut(fade_duration, Easing.OutQuint);

        /// <summary>Refreshes every number from the live score state.</summary>
        public void UpdateStats(ScoreProcessor score)
        {
            gradeText.Text = score.Grade.ToString();
            gradeText.Colour = ResultsScreen.ColourForGrade(score.Grade);

            scoreText.Text = $"{score.Score:N0}";
            accuracyText.Text = $"{score.Accuracy:0.00}%   •   {score.Combo}x combo";

            greatCount.Text = score.CountGreat.ToString();
            okCount.Text = score.CountOk.ToString();
            mehCount.Text = score.CountMeh.ToString();
            missCount.Text = score.CountMiss.ToString();
        }
    }
}
