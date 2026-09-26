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
    /// Score and accuracy on seven-segment readouts, in a small inset panel
    /// in the top-right corner: the same readout the generation screen uses
    /// for its BPM, with the unlit segments faintly visible behind the value.
    ///
    /// Fixed-width digits with leading zeros are the point: the numbers never
    /// shift sideways as they change, so a glance lands on the same place
    /// every time.
    /// </summary>
    public partial class ScorePanel : CompositeDrawable
    {
        private const int score_digits = 8;

        private readonly SegmentReadout score;
        private readonly SegmentReadout accuracy;

        public ScorePanel()
        {
            AutoSizeAxes = Axes.Both;

            InternalChild = new Container
            {
                AutoSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 3,
                BorderThickness = 1.5f,
                BorderColour = RetroPalette.ChromeDark,
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
                        Direction = FillDirection.Vertical,
                        Padding = new MarginPadding(6),
                        Spacing = new Vector2(0, 5),
                        Children = new Drawable[]
                        {
                            row("SCORE", score = new SegmentReadout(18)
                            {
                                Digits = score_digits,
                                DisplayColour = RetroPalette.Amber,
                            }),
                            row("ACC %", accuracy = new SegmentReadout(12)
                            {
                                Digits = 6,
                                DisplayColour = RetroPalette.Cyan,
                            }),
                        },
                    },
                },
            };

            SetScore(0);
            SetAccuracy(100);
        }

        private static Drawable row(string label, SegmentReadout readout) => new Container
        {
            AutoSizeAxes = Axes.Y,
            Width = 220,
            Children = new Drawable[]
            {
                new RetroText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Font = RetroFontFamily.Display,
                    TextSize = 7,
                    Colour = RetroPalette.TextDim.Opacity(0.9f),
                    Text = label,
                },
                readout.With(r =>
                {
                    r.Anchor = Anchor.CentreRight;
                    r.Origin = Anchor.CentreRight;
                }),
            },
        };

        /// <summary>What the score readout shows. Exposed for tests.</summary>
        public string ScoreText => score.Text;

        /// <summary>What the accuracy readout shows. Exposed for tests.</summary>
        public string AccuracyText => accuracy.Text;

        public void SetScore(long value)
        {
            string text = System.Math.Clamp(value, 0, 99_999_999).ToString($"D{score_digits}");

            if (score.Text != text)
                score.Text = text;
        }

        /// <summary>Accuracy as a percentage, 0 to 100.</summary>
        public void SetAccuracy(double percent)
        {
            string text = System.Math.Clamp(percent, 0, 100).ToString("000.00", System.Globalization.CultureInfo.InvariantCulture);

            if (accuracy.Text != text)
                accuracy.Text = text;
        }
    }
}
