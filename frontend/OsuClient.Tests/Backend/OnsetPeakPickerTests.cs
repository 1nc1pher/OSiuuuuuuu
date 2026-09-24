using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OsuClient.Game.Backend;

namespace OsuClient.Tests.Backend
{
    /// <summary>
    /// The ported onset peak-picker.
    ///
    /// One test in here is the reason the whole feature is allowed to exist:
    /// <see cref="TestReproducesTheBackendsOwnOnsetsAtEveryTier"/>. The
    /// analyser screen will let a user turn the detector's sensitivity knobs
    /// and watch the onset count change, and that is a demonstration of the
    /// algorithm only if the numbers match what the backend would have
    /// produced. If it drifts, the honest fix is this port or the export —
    /// never a looser assertion here.
    ///
    /// The rest pin the three details of <c>pick_peaks</c> that are easy to
    /// tidy into wrongness: the asymmetric local-maximum test, keeping the
    /// stronger of two candidates inside the spacing window, and rounding the
    /// spacing up rather than truncating it.
    /// </summary>
    [TestFixture]
    public class OnsetPeakPickerTests
    {
        private static float[] Flat(int length, float value)
        {
            var values = new float[length];

            for (int i = 0; i < length; i++)
                values[i] = value;

            return values;
        }

        // ------------------------------------------------------------------
        // The test the live knobs rest on
        // ------------------------------------------------------------------

        [Test]
        public void TestReproducesTheBackendsOwnOnsetsAtEveryTier()
        {
            var detail = DspFixtures.RequireDetail();
            var analysis = DspFixtures.RequireAnalysis();

            // analysis.json records which tier's detection pass its onset
            // list came from, so that is the one pass the two files can be
            // compared across.
            string tier = analysis.OnsetSource;

            Assert.That(tier, Is.Not.Empty);
            Assert.That(detail.Detection.Tiers.ContainsKey(tier), Is.True);

            var picked = OnsetPeakPicker.PickTimes(detail,
                                                   detail.Detection.Tiers[tier].Margin,
                                                   detail.Detection.Tiers[tier].Delta);

            Assert.That(picked, Has.Count.EqualTo(analysis.Onsets.Count),
                        "re-picking from dsp.json found a different number of onsets "
                        + "than the backend recorded in analysis.json");

            for (int i = 0; i < picked.Count; i++)
            {
                // analysis.json rounds times to the millisecond, so that is
                // the only slack this comparison gets.
                Assert.That(picked[i], Is.EqualTo(analysis.Onsets[i].Time).Within(0.001),
                            $"onset {i}");
            }
        }

        [Test]
        public void TestEveryTiersPickCountMatchesItsRecordedFunnel()
        {
            // The same claim again, for the tiers analysis.json does not
            // carry onsets for: dsp.json records how many each tier detected,
            // and re-picking at that tier's sensitivity must find exactly
            // that many.
            var detail = DspFixtures.RequireDetail();

            Assert.That(detail.Tiers, Is.Not.Null);

            foreach (var (name, tier) in detail.Tiers!)
            {
                Assert.That(OnsetPeakPicker.PickForTier(detail, name),
                            Has.Count.EqualTo(tier.Funnel.Detected), name);
            }
        }

        [Test]
        public void TestALooserSensitivityFindsMoreOnsets()
        {
            // What the knobs promise on screen, and the direction is easy to
            // get backwards: a *higher* margin is a *stricter* detector.
            var detail = DspFixtures.RequireDetail();

            int strict = OnsetPeakPicker.Pick(detail, 2.2, 0.08).Count;
            int loose = OnsetPeakPicker.Pick(detail, 1.1, 0.03).Count;

            Assert.That(loose, Is.GreaterThan(strict));
        }

        [Test]
        public void TestAnImpossiblyHighThresholdFindsNothing()
        {
            var detail = DspFixtures.RequireDetail();

            Assert.That(OnsetPeakPicker.Pick(detail, 1.0, 10.0), Is.Empty);
        }

        [Test]
        public void TestPickedFramesAreOrderedAndInsideTheTrack()
        {
            var detail = DspFixtures.RequireDetail();
            var frames = OnsetPeakPicker.Pick(detail, 1.5, 0.05);

            Assert.That(frames, Is.Ordered.Ascending);
            Assert.That(frames.All(f => f >= 0 && f < detail.Frames.Count), Is.True);
        }

        // ------------------------------------------------------------------
        // The details that are easy to tidy into wrongness
        // ------------------------------------------------------------------

        [Test]
        public void TestPicksALocalMaximumAboveTheThreshold()
        {
            float[] flux = { 0f, 0.1f, 0.9f, 0.1f, 0f };

            Assert.That(OnsetPeakPicker.Pick(flux, Flat(5, 0.5f), 1), Is.EqualTo(new[] { 2 }));
        }

        [Test]
        public void TestIgnoresAPeakThatDoesNotClearTheThreshold()
        {
            float[] flux = { 0f, 0.1f, 0.4f, 0.1f, 0f };

            Assert.That(OnsetPeakPicker.Pick(flux, Flat(5, 0.5f), 1), Is.Empty);
        }

        [Test]
        public void TestTheThresholdTestIsStrictlyGreater()
        {
            // Python's `flux > threshold`, not `>=`. One sample sitting
            // exactly on its threshold is not an onset.
            float[] flux = { 0f, 0.1f, 0.5f, 0.1f, 0f };

            Assert.That(OnsetPeakPicker.Pick(flux, Flat(5, 0.5f), 1), Is.Empty);
        }

        [Test]
        public void TestPicksTheFirstSampleOfAPlateauNotEveryOne()
        {
            // The local-maximum test is strict on the left and loose on the
            // right. Both strict would miss a plateau entirely; both loose
            // would pick all of it.
            float[] flux = { 0f, 0.1f, 0.9f, 0.9f, 0.9f, 0.1f, 0f };

            Assert.That(OnsetPeakPicker.Pick(flux, Flat(7, 0.5f), 1), Is.EqualTo(new[] { 2 }));
        }

        [Test]
        public void TestKeepsTheStrongerOfTwoCandidatesInsideTheSpacing()
        {
            // Not "skip the second one": the picker replaces the peak it
            // already kept when a stronger one turns up too close to it.
            float[] flux = { 0f, 0.6f, 0.2f, 0.9f, 0.1f, 0f };

            Assert.That(OnsetPeakPicker.Pick(flux, Flat(6, 0.05f), 4), Is.EqualTo(new[] { 3 }));
        }

        [Test]
        public void TestKeepsTheEarlierOfTwoWhenItIsTheStronger()
        {
            float[] flux = { 0f, 0.9f, 0.2f, 0.6f, 0.1f, 0f };

            Assert.That(OnsetPeakPicker.Pick(flux, Flat(6, 0.05f), 4), Is.EqualTo(new[] { 1 }));
        }

        [Test]
        public void TestKeepsBothPeaksOnceTheyAreFarEnoughApart()
        {
            float[] flux = { 0f, 0.9f, 0.1f, 0.1f, 0.1f, 0.8f, 0f };

            Assert.That(OnsetPeakPicker.Pick(flux, Flat(7, 0.05f), 4), Is.EqualTo(new[] { 1, 5 }));
        }

        [Test]
        public void TestSpacingInFramesRoundsUp()
        {
            // Truncating would round the requirement *down* and quietly
            // enforce a shorter gap than asked for. 0.05s at 43.07 fps is
            // 2.15 frames, which must become 3.
            Assert.That(OnsetPeakPicker.SpacingInFrames(0.05, 22050.0 / 512), Is.EqualTo(3));
            Assert.That(OnsetPeakPicker.SpacingInFrames(0.0001, 43.07), Is.EqualTo(1));
            Assert.That(OnsetPeakPicker.SpacingInFrames(0, 43.07), Is.EqualTo(1));
        }

        [Test]
        public void TestSpacingSurvivesAMissingFrameRate()
        {
            Assert.That(OnsetPeakPicker.SpacingInFrames(0.05, 0), Is.EqualTo(1));
        }

        // ------------------------------------------------------------------
        // Degenerate input
        // ------------------------------------------------------------------

        [Test]
        public void TestASilentCurveFindsNothing()
        {
            Assert.That(OnsetPeakPicker.Pick(Flat(64, 0f), Flat(64, 0.05f), 3), Is.Empty);
        }

        [Test]
        public void TestTooShortACurveFindsNothing()
        {
            // The picker never examines the first or last frame, so anything
            // shorter than three has no interior to look at.
            Assert.That(OnsetPeakPicker.Pick(new[] { 1f, 1f }, Flat(2, 0f), 1), Is.Empty);
            Assert.That(OnsetPeakPicker.Pick(Array.Empty<float>(), Array.Empty<float>(), 1), Is.Empty);
        }

        [Test]
        public void TestAShortThresholdIsRefusedRatherThanPickingHalfTheSong()
        {
            // A mismatched pair would otherwise pick peaks until the
            // threshold ran out and then silently stop.
            Assert.That(OnsetPeakPicker.Pick(Flat(64, 1f), Flat(8, 0f), 1), Is.Empty);
        }

        [Test]
        public void TestNullsAreRefusedRatherThanThrowing()
        {
            Assert.That(OnsetPeakPicker.Pick(null!, Flat(4, 0f), 1), Is.Empty);
            Assert.That(OnsetPeakPicker.Pick(Flat(4, 1f), null!, 1), Is.Empty);
            Assert.That(OnsetPeakPicker.Pick(null!, 1.5, 0.05), Is.Empty);
            Assert.That(OnsetPeakPicker.PickForTier(null!, "Easy"), Is.Empty);
        }

        [Test]
        public void TestAnUnknownTierPicksNothing()
        {
            Assert.That(OnsetPeakPicker.PickForTier(DspFixtures.RequireDetail(), "Lunatic"), Is.Empty);
        }

        [Test]
        public void TestThresholdMatchesTheFormula()
        {
            float[] median = { 0f, 0.2f, 0.5f };

            var threshold = OnsetPeakPicker.Threshold(median, 1.5, 0.05);

            Assert.That(threshold[0], Is.EqualTo(0.05).Within(1e-6));
            Assert.That(threshold[1], Is.EqualTo(0.05 + 0.2 * 1.5).Within(1e-6));
            Assert.That(threshold[2], Is.EqualTo(0.05 + 0.5 * 1.5).Within(1e-6));
        }
    }
}
