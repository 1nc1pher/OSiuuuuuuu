using System.Collections.Generic;
using NUnit.Framework;
using OsuClient.Game.Beatmaps.HitObjects;
using OsuClient.Game.Screens.Gameplay;

namespace OsuClient.Tests.Gameplay
{
    /// <summary>
    /// Pure gap-finding logic, no osu.Framework dependency — runs headless and
    /// deterministically, the same way as <see cref="JudgementProcessorTests"/>.
    ///
    /// AR9/OD6 throughout: preempt and meh-window come out to round-ish
    /// numbers that are easy to reason about by hand
    /// (<see cref="JudgementProcessorTests"/> covers the formulas themselves).
    /// </summary>
    [TestFixture]
    public class BreakPeriodTests
    {
        private const double approach_rate = 9;
        private const double overall_difficulty = 6;

        private static double preempt => JudgementProcessor.Preempt(approach_rate);
        private static double mehWindow => JudgementProcessor.MehWindow(overall_difficulty);

        private static HitCircleData circle(double time) => new HitCircleData { StartTime = time };

        private static List<BreakPeriod> compute(params HitObjectData[] objects) =>
            BreakPeriods.Compute(objects, approach_rate, overall_difficulty);

        [Test]
        public void NoHitObjectsMeansNoBreaks()
        {
            Assert.That(compute(), Is.Empty);
        }

        [Test]
        public void OneObjectCloseToTheStartOfTheTrackHasNoLeadingBreak()
        {
            // Its approach starts almost immediately — nowhere near the
            // performance threshold.
            Assert.That(compute(circle(1000)), Is.Empty);
        }

        [Test]
        public void ANormalRunOfObjectsHasNoBreaksBetweenThem()
        {
            Assert.That(compute(circle(1000), circle(1300), circle(1600), circle(1900)), Is.Empty);
        }

        [Test]
        public void ALongGapFromTheStartOfTheTrackIsASkippableBreak()
        {
            // A song with a 20-second intro before the first object.
            var breaks = compute(circle(20000));

            Assert.That(breaks, Has.Count.EqualTo(1));
            Assert.That(breaks[0].Kind, Is.EqualTo(BreakKind.Skip));
            Assert.That(breaks[0].Start, Is.EqualTo(0));
            Assert.That(breaks[0].End, Is.EqualTo(20000 - preempt).Within(0.001));
        }

        [Test]
        public void AGapBetweenTwoLaterObjectsIsFoundTheSameWay()
        {
            var breaks = compute(circle(1000), circle(30000));

            Assert.That(breaks, Has.Count.EqualTo(1));
            Assert.That(breaks[0].Kind, Is.EqualTo(BreakKind.Skip));

            // Starts once the first circle stops being interactive, not at
            // its raw StartTime/EndTime.
            Assert.That(breaks[0].Start, Is.EqualTo(1000 + mehWindow).Within(0.001));
            Assert.That(breaks[0].End, Is.EqualTo(30000 - preempt).Within(0.001));
        }

        [Test]
        public void AModerateGapIsAPerformanceBreakNotASkippableOne()
        {
            // Comfortably inside the performance band and nowhere near the
            // skip threshold.
            double gap = (BreakPeriods.PerformanceThreshold + BreakPeriods.SkipThreshold) / 2;
            var breaks = compute(circle(1000), circle(1000 + mehWindow + preempt + gap));

            Assert.That(breaks, Has.Count.EqualTo(1));
            Assert.That(breaks[0].Kind, Is.EqualTo(BreakKind.Performance));
        }

        [Test]
        public void AGapRightAtTheSkipThresholdCountsAsSkippable()
        {
            double start = 1000 + mehWindow;
            double approachStart = start + BreakPeriods.SkipThreshold;

            var breaks = compute(circle(1000), circle(approachStart + preempt));

            Assert.That(breaks, Has.Count.EqualTo(1));
            Assert.That(breaks[0].Kind, Is.EqualTo(BreakKind.Skip));
        }

        [Test]
        public void AGapJustBelowThePerformanceThresholdIsNotReportedAtAll()
        {
            double start = 1000 + mehWindow;
            double approachStart = start + BreakPeriods.PerformanceThreshold - 1;

            Assert.That(compute(circle(1000), circle(approachStart + preempt)), Is.Empty);
        }

        [Test]
        public void MultipleGapsAreAllFoundInOrder()
        {
            var breaks = compute(
                circle(500),      // no leading gap
                circle(25000),    // long gap before this one
                circle(25300),    // no gap
                circle(50000));   // long gap before this one

            Assert.That(breaks, Has.Count.EqualTo(2));
            Assert.That(breaks[0].End, Is.LessThan(breaks[1].Start));
            Assert.That(breaks.TrueForAll(b => b.Kind == BreakKind.Skip));
        }

        [Test]
        public void ADurationBearingObjectUsesItsOwnEndTimeWhenItOutlastsTheMissWindow()
        {
            // A spinner running well past its own meh-window — the gap after
            // it should be measured from when it actually finishes, not from
            // StartTime + mehWindow.
            var spinner = new SpinnerData { StartTime = 1000 };
            spinner.SetEndTime(5000);

            var breaks = compute(spinner, circle(30000));

            Assert.That(breaks, Has.Count.EqualTo(1));
            Assert.That(breaks[0].Start, Is.EqualTo(5000));
        }

        [Test]
        public void RemainingFractionCountsDownFromOneToZero()
        {
            var period = new BreakPeriod(1000, 2000, BreakKind.Skip);

            Assert.That(period.RemainingFraction(1000), Is.EqualTo(1).Within(0.001));
            Assert.That(period.RemainingFraction(1500), Is.EqualTo(0.5).Within(0.001));
            Assert.That(period.RemainingFraction(2000), Is.EqualTo(0).Within(0.001));
        }

        [Test]
        public void RemainingFractionClampsOutsideTheBreak()
        {
            var period = new BreakPeriod(1000, 2000, BreakKind.Skip);

            Assert.That(period.RemainingFraction(500), Is.EqualTo(1));
            Assert.That(period.RemainingFraction(2500), Is.EqualTo(0));
        }

        [Test]
        public void ContainsIsHalfOpenAtTheEnd()
        {
            var period = new BreakPeriod(1000, 2000, BreakKind.Skip);

            Assert.That(period.Contains(1000), Is.True);
            Assert.That(period.Contains(1999), Is.True);

            // The next object's approach begins exactly here, so the break
            // has to have already handed off by this instant.
            Assert.That(period.Contains(2000), Is.False);
        }
    }
}
