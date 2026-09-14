using NUnit.Framework;
using OsuClient.Game.Screens.Gameplay;

namespace OsuClient.Tests.Gameplay
{
    /// <summary>
    /// Pure timing maths, no osu.Framework dependency — runs headless and
    /// deterministically.
    /// </summary>
    [TestFixture]
    public class JudgementProcessorTests
    {
        private const double tolerance = 1e-9;

        [TestCase(0, 80)]
        [TestCase(5, 50)]
        [TestCase(10, 20)]
        public void GreatWindowMatchesOsuFormula(double od, double expected)
        {
            Assert.That(JudgementProcessor.GreatWindow(od), Is.EqualTo(expected).Within(tolerance));
        }

        [TestCase(0, 140)]
        [TestCase(5, 100)]
        [TestCase(10, 60)]
        public void OkWindowMatchesOsuFormula(double od, double expected)
        {
            Assert.That(JudgementProcessor.OkWindow(od), Is.EqualTo(expected).Within(tolerance));
        }

        [TestCase(0, 200)]
        [TestCase(5, 150)]
        [TestCase(10, 100)]
        public void MehWindowMatchesOsuFormula(double od, double expected)
        {
            Assert.That(JudgementProcessor.MehWindow(od), Is.EqualTo(expected).Within(tolerance));
        }

        [Test]
        public void JudgeReturnsGreatWithinGreatWindow()
        {
            Assert.That(JudgementProcessor.Judge(30, 5), Is.EqualTo(HitResult.Great));
            Assert.That(JudgementProcessor.Judge(-30, 5), Is.EqualTo(HitResult.Great));
        }

        [Test]
        public void JudgeReturnsOkJustOutsideGreatWindow()
        {
            double greatWindow = JudgementProcessor.GreatWindow(5);
            Assert.That(JudgementProcessor.Judge(greatWindow + 1, 5), Is.EqualTo(HitResult.Ok));
        }

        [Test]
        public void JudgeReturnsMehJustOutsideOkWindow()
        {
            double okWindow = JudgementProcessor.OkWindow(5);
            Assert.That(JudgementProcessor.Judge(okWindow + 1, 5), Is.EqualTo(HitResult.Meh));
        }

        [Test]
        public void JudgeReturnsMissOutsideMehWindow()
        {
            double mehWindow = JudgementProcessor.MehWindow(5);
            Assert.That(JudgementProcessor.Judge(mehWindow + 1, 5), Is.EqualTo(HitResult.Miss));
        }

        [Test]
        public void PreemptDecreasesAsApproachRateIncreases()
        {
            Assert.That(JudgementProcessor.Preempt(0), Is.EqualTo(1800).Within(tolerance));
            Assert.That(JudgementProcessor.Preempt(5), Is.EqualTo(1200).Within(tolerance));
            Assert.That(JudgementProcessor.Preempt(10), Is.EqualTo(450).Within(tolerance));
        }

        [Test]
        public void FadeInDecreasesAsApproachRateIncreases()
        {
            Assert.That(JudgementProcessor.FadeIn(0), Is.EqualTo(1200).Within(tolerance));
            Assert.That(JudgementProcessor.FadeIn(5), Is.EqualTo(800).Within(tolerance));
            Assert.That(JudgementProcessor.FadeIn(10), Is.EqualTo(300).Within(tolerance));
        }

        [TestCase(HitResult.Great, 300)]
        [TestCase(HitResult.Ok, 100)]
        [TestCase(HitResult.Meh, 50)]
        [TestCase(HitResult.Miss, 0)]
        public void ScoreValueMatchesJudgement(HitResult result, int expected)
        {
            Assert.That(JudgementProcessor.ScoreValue(result), Is.EqualTo(expected));
        }
    }
}
