using System;
using System.Linq;
using NUnit.Framework;
using OsuClient.Game.Screens.MainMenu;

namespace OsuClient.Tests.MainMenu
{
    /// <summary>
    /// The maths behind the menu's spectrum ring.
    ///
    /// Pinned here rather than by looking at the ring, because the failures
    /// that matter are invisible in a still frame: a NaN bin that freezes one
    /// column forever, a decay that depends on the frame rate, a column whose
    /// bin group is empty and so never moves at all.
    /// </summary>
    [TestFixture]
    public class SpectrumAnalyserTests
    {
        private static float[] Bins(params (int bin, float value)[] spikes)
        {
            var bins = new float[256];

            foreach (var (bin, value) in spikes)
                bins[bin] = value;

            return bins;
        }

        [Test]
        public void SilenceStaysAtZero()
        {
            var analyser = new SpectrumAnalyser(64);

            analyser.Update(new float[256], 16);

            Assert.That(analyser.Values.ToArray(), Is.All.Zero);
        }

        [Test]
        public void EveryColumnCoversSomeBins()
        {
            var analyser = new SpectrumAnalyser(128);

            // Feed a full-scale spectrum: with every bin loud, no column may
            // be left at zero — one that is has an empty bin group and would
            // be a permanently dead segment of the ring.
            var loud = Enumerable.Repeat(1f, 256).ToArray();

            analyser.Update(loud, 16);

            Assert.That(analyser.Values.ToArray(), Is.All.GreaterThan(0.5f));
        }

        [Test]
        public void ALoudBinLandsInOneRegionOfTheRing()
        {
            var analyser = new SpectrumAnalyser(64);

            analyser.Update(Bins((100, 1f)), 16);

            var values = analyser.Values.ToArray();
            int peak = Array.IndexOf(values, values.Max());

            // Bin 100 of 1..128 sits near the top of the log range.
            Assert.That(peak, Is.GreaterThan(values.Length * 3 / 4));

            // And the far end of the ring — the low frequencies — is quiet.
            Assert.That(values.Take(values.Length / 2), Is.All.LessThan(0.05f));
        }

        [Test]
        public void LowAndHighBinsLandInDifferentColumns()
        {
            var low = new SpectrumAnalyser(64);
            var high = new SpectrumAnalyser(64);

            low.Update(Bins((2, 1f)), 16);
            high.Update(Bins((120, 1f)), 16);

            int lowPeak = Array.IndexOf(low.Values.ToArray(), low.Values.ToArray().Max());
            int highPeak = Array.IndexOf(high.Values.ToArray(), high.Values.ToArray().Max());

            Assert.That(lowPeak, Is.LessThan(highPeak));
        }

        [Test]
        public void BarsFallMonotonicallyOnceTheMusicStops()
        {
            var analyser = new SpectrumAnalyser(64);

            analyser.Update(Enumerable.Repeat(1f, 256).ToArray(), 16);

            float previous = analyser.Values.ToArray().Max();
            Assert.That(previous, Is.GreaterThan(0.5f));

            var silence = new float[256];

            // A full second of silence: decay_per_second is what survives
            // one second, so anything shorter is still visibly lit.
            for (int frame = 0; frame < 63; frame++)
            {
                analyser.Update(silence, 16);

                float now = analyser.Values.ToArray().Max();

                Assert.That(now, Is.LessThan(previous));
                previous = now;
            }

            Assert.That(previous, Is.LessThan(0.05f));
        }

        [Test]
        public void DecayIsFramerateIndependent()
        {
            var fast = new SpectrumAnalyser(64);
            var slow = new SpectrumAnalyser(64);

            var loud = Enumerable.Repeat(1f, 256).ToArray();
            var silence = new float[256];

            fast.Update(loud, 16);
            slow.Update(loud, 16);

            // Half a second of silence, at 120fps and at 30fps.
            for (int i = 0; i < 60; i++)
                fast.Update(silence, 8.333);

            for (int i = 0; i < 15; i++)
                slow.Update(silence, 33.333);

            Assert.That(fast.Values.ToArray().Max(), Is.EqualTo(slow.Values.ToArray().Max()).Within(0.02f));
        }

        [Test]
        public void ValuesStayInRangeForAdversarialInput()
        {
            var analyser = new SpectrumAnalyser(64);

            var nasty = new float[256];

            for (int i = 0; i < nasty.Length; i++)
                nasty[i] = i % 4 switch
                {
                    0 => float.NaN,
                    1 => float.PositiveInfinity,
                    2 => -50f,
                    _ => 1e9f,
                };

            for (int frame = 0; frame < 10; frame++)
                analyser.Update(nasty, 16);

            Assert.That(analyser.Values.ToArray(), Is.All.InRange(0f, 1f));
            Assert.That(analyser.Values.ToArray().Any(float.IsNaN), Is.False);
        }

        [Test]
        public void ShortOrEmptyInputIsTolerated()
        {
            var analyser = new SpectrumAnalyser(64);

            Assert.DoesNotThrow(() => analyser.Update(ReadOnlySpan<float>.Empty, 16));
            Assert.DoesNotThrow(() => analyser.Update(new float[8], 16));

            Assert.That(analyser.Values.ToArray(), Is.All.InRange(0f, 1f));
        }

        [Test]
        public void ZeroElapsedTimeDoesNotWipeTheRing()
        {
            var analyser = new SpectrumAnalyser(64);

            analyser.Update(Enumerable.Repeat(1f, 256).ToArray(), 16);
            float before = analyser.Values.ToArray().Max();

            // A paused clock, or two updates in the same frame.
            analyser.Update(new float[256], 0);

            Assert.That(analyser.Values.ToArray().Max(), Is.EqualTo(before).Within(0.001f));
        }

        [Test]
        public void ResetClearsEverything()
        {
            var analyser = new SpectrumAnalyser(64);

            analyser.Update(Enumerable.Repeat(1f, 256).ToArray(), 16);
            analyser.Reset();

            Assert.That(analyser.Values.ToArray(), Is.All.Zero);
        }

        [Test]
        public void LowEnergyFollowsTheBottomOfTheSpectrum()
        {
            var analyser = new SpectrumAnalyser(64);

            // Bass only.
            analyser.Update(Bins((1, 1f), (2, 1f), (3, 1f)), 16);
            float bass = analyser.LowEnergy;

            // Treble only, on a fresh analyser so the decay doesn't carry
            // the bass reading over.
            var treble = new SpectrumAnalyser(64);
            treble.Update(Bins((110, 1f), (120, 1f)), 16);

            Assert.That(bass, Is.GreaterThan(0.5f));
            Assert.That(treble.LowEnergy, Is.LessThan(0.05f));
        }

        [Test]
        public void LowEnergyIsClearedByReset()
        {
            var analyser = new SpectrumAnalyser(64);

            analyser.Update(Enumerable.Repeat(1f, 256).ToArray(), 16);
            Assert.That(analyser.LowEnergy, Is.GreaterThan(0));

            analyser.Reset();

            Assert.That(analyser.LowEnergy, Is.Zero);
        }

        [Test]
        public void ColumnCountMustBePositive()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SpectrumAnalyser(0));
        }
    }
}
