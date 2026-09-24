using NUnit.Framework;
using OsuClient.Game.Screens.Generation;

namespace OsuClient.Tests.Generation
{
    /// <summary>
    /// How long the reveal runs.
    ///
    /// Pacing is the one thing in this sequence that drifts without anyone
    /// noticing: each new stage adds a few seconds, every individual addition
    /// looks reasonable, and nothing anywhere states the total. These tests
    /// are the statement.
    ///
    /// They are a budget, not a specification — if a stage genuinely needs
    /// longer, raise the ceiling deliberately and say why, rather than
    /// letting it creep.
    /// </summary>
    [TestFixture]
    public class DspRevealPacingTests
    {
        [Test]
        public void TestTheFullSequenceStaysWithinItsBudget()
        {
            // Eleven stages, about 34 seconds. Over the plan's original
            // 30-second sketch, which assumed nine — detection and tempo each
            // split into method and result once there was data for both.
            Assert.That(RevealPacing.FullSequence, Is.LessThan(36_000));
        }

        [Test]
        public void TestNoStageIsTooBriefToRead()
        {
            // Below about two and a half seconds a stage stops being
            // readable, and eleven unreadable stages are worse than a longer
            // sequence. This is the floor that stops "it is too long" being
            // answered by compressing everything.
            foreach (double duration in new[]
            {
                RevealPacing.Spectrogram, RevealPacing.Flux, RevealPacing.Threshold,
                RevealPacing.Onsets, RevealPacing.TempoSearch, RevealPacing.PhaseFit,
                RevealPacing.BeatGrid, RevealPacing.Snap, RevealPacing.Funnel,
                RevealPacing.Assembly,
            })
            {
                Assert.That(duration, Is.GreaterThanOrEqualTo(2_500));
            }
        }

        [Test]
        public void TestAMapWithoutATraceIsMuchShorter()
        {
            // The five original stages, for every map generated before the
            // trace existed. It should not have got slower because newer maps
            // show more.
            Assert.That(RevealPacing.WithoutTrace, Is.LessThan(18_000));
            Assert.That(RevealPacing.WithoutTrace, Is.LessThan(RevealPacing.FullSequence / 1.8));
        }
    }
}
