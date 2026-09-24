using System;
using System.Linq;
using NUnit.Framework;
using OsuClient.Game.Graphics;

namespace OsuClient.Tests.Graphics
{
    /// <summary>
    /// The display-time reduction behind every curve the analyser draws.
    ///
    /// This is the half of <see cref="TraceGraph"/> that can be tested
    /// without a window, and it is the half that carries the argument: the
    /// backend ships curves at full resolution specifically because reducing
    /// here keeps peaks, where reducing in the file would have lost them
    /// permanently. If a one-sample spike can vanish in this function, that
    /// decision was wrong and ~60 KB per map is being spent for nothing.
    ///
    /// Whether it actually renders is a separate question and a separate
    /// tool — the <c>trace-graph</c> screenshot scene.
    /// </summary>
    [TestFixture]
    public class TraceGraphTests
    {
        private static float[] Ramp(int length)
        {
            var values = new float[length];

            for (int i = 0; i < length; i++)
                values[i] = i / (float)(length - 1);

            return values;
        }

        // ------------------------------------------------------------------
        // The property the whole approach rests on
        // ------------------------------------------------------------------

        [Test]
        public void TestASingleSampleSpikeSurvivesA100To1Reduction()
        {
            var samples = new float[10000];
            samples[4321] = 1f;

            var reduced = TraceGraph.Reduce(samples, 0, samples.Length, 100);

            Assert.That(reduced.Max(column => column.Max), Is.EqualTo(1f));
        }

        [Test]
        public void TestASpikeStaysInItsOwnColumn()
        {
            // Not just "the peak is somewhere": a peak that survived at the
            // wrong x would put an onset marker beside its own flux peak,
            // which is the exact failure the file format avoids.
            var samples = new float[1000];
            samples[500] = 1f;

            var reduced = TraceGraph.Reduce(samples, 0, samples.Length, 10);

            Assert.That(Array.FindIndex(reduced, column => column.Max > 0), Is.EqualTo(5));
        }

        [Test]
        public void TestEveryPeakOfADenseSignalSurvives()
        {
            // 10,000 samples into 500 columns is 20 samples per column, and a
            // spike every 13 — so every column spans at least one spike and
            // must reach full height. This is a real song's flux against a
            // real panel width: far more onsets than pixels, and none of them
            // allowed to flatten.
            var samples = new float[10000];

            for (int i = 0; i < samples.Length; i += 13)
                samples[i] = 1f;

            var reduced = TraceGraph.Reduce(samples, 0, samples.Length, 500);

            Assert.That(reduced.All(column => column.Max == 1f), Is.True);
        }

        [Test]
        public void TestPeaksSparserThanTheColumnsAreNotInvented()
        {
            // The other direction: with spikes further apart than a column is
            // wide, the columns between them must stay empty. A reduction
            // that smeared a peak sideways would draw onsets that are not
            // there.
            var samples = new float[10000];

            for (int i = 0; i < samples.Length; i += 200)
                samples[i] = 1f;

            var reduced = TraceGraph.Reduce(samples, 0, samples.Length, 500);

            Assert.That(reduced.Count(column => column.Max == 1f), Is.EqualTo(50));
            Assert.That(reduced.Count(column => column.Max == 0f), Is.EqualTo(450));
        }

        [Test]
        public void TestTheMinimumIsKeptAsWellAsTheMaximum()
        {
            // A smooth curve draws as a line only because top and bottom
            // track each other; keeping just the maximum would turn every
            // trace into a filled area.
            var samples = Ramp(1000);

            var reduced = TraceGraph.Reduce(samples, 0, samples.Length, 10);

            Assert.That(reduced[0].Min, Is.EqualTo(0f).Within(0.001));
            Assert.That(reduced[9].Max, Is.EqualTo(1f).Within(0.001));
            Assert.That(reduced.All(column => column.Min <= column.Max), Is.True);
        }

        // ------------------------------------------------------------------
        // The perceptual value curve
        // ------------------------------------------------------------------

        [Test]
        public void TestALinearCurveIsTheFractionItself()
        {
            Assert.That(TraceGraph.Normalise(0.25f, 1f, 1f), Is.EqualTo(0.25f).Within(1e-6));
            Assert.That(TraceGraph.Normalise(0.5f, 2f, 1f), Is.EqualTo(0.25f).Within(1e-6));
        }

        [Test]
        public void TestTheEndpointsAreFixedWhateverTheCurve()
        {
            foreach (float curve in new[] { 0.3f, 0.5f, 1f, 2f })
            {
                Assert.That(TraceGraph.Normalise(0f, 1f, curve), Is.EqualTo(0f), $"curve {curve}");
                Assert.That(TraceGraph.Normalise(1f, 1f, curve), Is.EqualTo(1f).Within(1e-6), $"curve {curve}");
            }
        }

        [Test]
        public void TestACurveBelowOneLiftsQuietDetail()
        {
            // The whole reason it exists: measured on a real track, the
            // adaptive threshold sits at 3% of a linear axis, which is
            // invisible. This is what makes it a line you can see.
            Assert.That(TraceGraph.Normalise(0.035f, 1f, 0.4f),
                        Is.GreaterThan(TraceGraph.Normalise(0.035f, 1f, 1f) * 5));
        }

        [Test]
        public void TestValuesAreClampedIntoThePanel()
        {
            Assert.That(TraceGraph.Normalise(5f, 1f, 1f), Is.EqualTo(1f));
            Assert.That(TraceGraph.Normalise(-5f, 1f, 1f), Is.EqualTo(0f));
        }

        [Test]
        public void TestAZeroRangeDoesNotDivideByZero()
        {
            Assert.That(float.IsFinite(TraceGraph.Normalise(1f, 0f, 1f)), Is.True);
        }

        [Test]
        public void TestTheCurveIsMonotonicSoOrderingSurvives()
        {
            // Monotonicity is the property that makes the transform a display
            // choice rather than a distortion.
            float previous = -1;

            for (int i = 0; i <= 100; i++)
            {
                float mapped = TraceGraph.Normalise(i / 100f, 1f, 0.4f);

                Assert.That(mapped, Is.GreaterThanOrEqualTo(previous));
                previous = mapped;
            }
        }

        [Test]
        public void TestEveryThresholdCrossingSurvivesTheCurve()
        {
            // The claim the analyser's flux-versus-threshold panel rests on:
            // curving both axes identically leaves the picture saying
            // "detected here" in exactly the frames the detector did. If this
            // ever fails, the drawn crossings are not the real ones.
            var random = new Random(20260920);

            for (int i = 0; i < 2000; i++)
            {
                float flux = (float)random.NextDouble();
                float threshold = (float)random.NextDouble();

                bool crossedLinearly = flux > threshold;
                bool crossedOnScreen = TraceGraph.Normalise(flux, 1f, 0.4f)
                                       > TraceGraph.Normalise(threshold, 1f, 0.4f);

                Assert.That(crossedOnScreen, Is.EqualTo(crossedLinearly),
                            $"flux {flux}, threshold {threshold}");
            }
        }

        // ------------------------------------------------------------------
        // Windows
        // ------------------------------------------------------------------

        [Test]
        public void TestReducesOnlyTheWindowAskedFor()
        {
            var samples = new float[1000];
            samples[100] = 1f;
            samples[900] = 1f;

            var early = TraceGraph.Reduce(samples, 0, 500, 10);
            var late = TraceGraph.Reduce(samples, 500, 1000, 10);

            Assert.That(early.Max(column => column.Max), Is.EqualTo(1f));
            Assert.That(late.Max(column => column.Max), Is.EqualTo(1f));
            Assert.That(Array.FindIndex(early, column => column.Max > 0), Is.EqualTo(2));
            Assert.That(Array.FindIndex(late, column => column.Max > 0), Is.EqualTo(8));
        }

        [Test]
        public void TestAWindowPastTheEndIsEmptyRatherThanSmeared()
        {
            // Clamping would repeat the last sample across the margin, which
            // reads as the song continuing past where it stops.
            var samples = Ramp(100);

            var reduced = TraceGraph.Reduce(samples, 200, 300, 10);

            Assert.That(reduced.All(column => column.Min == 0 && column.Max == 0), Is.True);
        }

        [Test]
        public void TestAWindowBeforeTheStartIsEmptyToo()
        {
            var reduced = TraceGraph.Reduce(Ramp(100), -200, -100, 10);

            Assert.That(reduced.All(column => column.Max == 0), Is.True);
        }

        [Test]
        public void TestAWindowStraddlingTheStartKeepsTheSamplesItCovers()
        {
            var samples = new float[100];
            samples[5] = 1f;

            var reduced = TraceGraph.Reduce(samples, -50, 50, 10);

            Assert.That(reduced.Take(5).All(column => column.Max == 0), Is.True);
            Assert.That(reduced.Skip(5).Max(column => column.Max), Is.EqualTo(1f));
        }

        [Test]
        public void TestZoomingPastOneSamplePerColumnStillDrawsSamples()
        {
            // Zoomed far in, several columns share one sample. They must show
            // it rather than coming back empty.
            var samples = Ramp(100);

            var reduced = TraceGraph.Reduce(samples, 10, 12, 40);

            Assert.That(reduced.All(column => column.Max > 0), Is.True);
        }

        // ------------------------------------------------------------------
        // Degenerate input
        // ------------------------------------------------------------------

        [Test]
        public void TestNoColumnsReducesToNothing()
        {
            Assert.That(TraceGraph.Reduce(Ramp(100), 0, 100, 0), Is.Empty);
            Assert.That(TraceGraph.Reduce(Ramp(100), 0, 100, -5), Is.Empty);
        }

        [Test]
        public void TestAnEmptyCurveReducesToFlatColumns()
        {
            var reduced = TraceGraph.Reduce(Array.Empty<float>(), 0, 100, 10);

            Assert.That(reduced, Has.Length.EqualTo(10));
            Assert.That(reduced.All(column => column.Min == 0 && column.Max == 0), Is.True);
        }

        [Test]
        public void TestAZeroWidthWindowDoesNotDivideByZero()
        {
            var reduced = TraceGraph.Reduce(Ramp(100), 50, 50, 10);

            Assert.That(reduced, Has.Length.EqualTo(10));
            Assert.That(reduced.All(column => float.IsFinite(column.Max)), Is.True);
        }

        [Test]
        public void TestASilentCurveReducesToZero()
        {
            var reduced = TraceGraph.Reduce(new float[500], 0, 500, 50);

            Assert.That(reduced.All(column => column.Min == 0 && column.Max == 0), Is.True);
        }
    }
}
