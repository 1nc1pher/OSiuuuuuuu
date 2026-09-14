using NUnit.Framework;
using OsuClient.Game.Screens.Gameplay.HUD;
using osuTK.Graphics;

namespace OsuClient.Tests.Visual
{
    /// <summary>
    /// The HP bar's colour bands: bright green above two-thirds health, amber
    /// through the middle third, red below one-third — a glance at colour
    /// should tell the story without having to judge the bar's length.
    /// </summary>
    [TestFixture]
    public partial class TestSceneHealthBar : osu.Framework.Testing.TestScene
    {
        private HealthBar bar = null!;

        private void createBar()
        {
            AddStep("create health bar", () => Child = bar = new HealthBar());
            AddUntilStep("loaded", () => bar.IsLoaded);
        }

        // Yellow has R slightly above G (and both high), which would trip a
        // naive "R > G" red check — these instead key off which channels are
        // clearly high/low rather than their exact ordering.
        private static bool isGreen(Color4 c) => c.G > 0.7f && c.R < 0.5f;
        private static bool isRed(Color4 c) => c.R > 0.7f && c.G < 0.5f;
        private static bool isYellowish(Color4 c) => c.R > 0.7f && c.G > 0.7f && c.B < 0.5f;

        [Test]
        public void TestFullHealthIsGreen()
        {
            createBar();
            AddStep("full health", () => bar.SetHealth(1.0));
            AddAssert("green", () => isGreen(bar.CurrentFillColour));
        }

        [Test]
        public void TestAboveTwoThirdsIsGreen()
        {
            createBar();
            AddStep("70% health", () => bar.SetHealth(0.70));
            AddAssert("green", () => isGreen(bar.CurrentFillColour));
        }

        [Test]
        public void TestMiddleThirdIsYellow()
        {
            createBar();
            AddStep("50% health", () => bar.SetHealth(0.50));
            AddAssert("yellow", () => isYellowish(bar.CurrentFillColour));
        }

        [Test]
        public void TestJustBelowTwoThirdsIsYellowNotGreen()
        {
            createBar();
            AddStep("65% health", () => bar.SetHealth(0.65));
            AddAssert("yellow", () => isYellowish(bar.CurrentFillColour));
            AddAssert("not green", () => !isGreen(bar.CurrentFillColour));
        }

        [Test]
        public void TestBelowOneThirdIsRed()
        {
            createBar();
            AddStep("20% health", () => bar.SetHealth(0.20));
            AddAssert("red", () => isRed(bar.CurrentFillColour));
        }

        [Test]
        public void TestJustAboveOneThirdIsYellowNotRed()
        {
            createBar();
            AddStep("34% health", () => bar.SetHealth(0.34));
            AddAssert("yellow", () => isYellowish(bar.CurrentFillColour));
            AddAssert("not red", () => !isRed(bar.CurrentFillColour));
        }

        [Test]
        public void TestZeroHealthIsRed()
        {
            createBar();
            AddStep("zero health", () => bar.SetHealth(0.0));
            AddAssert("red", () => isRed(bar.CurrentFillColour));
        }

        [Test]
        public void TestColourFollowsHealthDownThroughAllThreeBands()
        {
            createBar();

            AddStep("start full", () => bar.SetHealth(1.0));
            AddAssert("starts green", () => isGreen(bar.CurrentFillColour));

            AddStep("drop to middle band", () => bar.SetHealth(0.5));
            AddAssert("becomes yellow", () => isYellowish(bar.CurrentFillColour));

            AddStep("drop to low band", () => bar.SetHealth(0.1));
            AddAssert("becomes red", () => isRed(bar.CurrentFillColour));

            AddStep("recover to full", () => bar.SetHealth(1.0));
            AddAssert("goes back to green", () => isGreen(bar.CurrentFillColour));
        }

        [Test]
        public void TestExactThresholdBoundariesMatchStatedRanges()
        {
            // "more than 66%" is green, so exactly at the boundary it should
            // already have rolled over to yellow.
            createBar();

            AddStep("exactly two-thirds", () => bar.SetHealth(2 / 3d));
            AddAssert("yellow at the boundary, not green", () => isYellowish(bar.CurrentFillColour));

            AddStep("exactly one-third", () => bar.SetHealth(1 / 3d));
            AddAssert("yellow at the boundary, not red", () => isYellowish(bar.CurrentFillColour));
        }
    }
}
