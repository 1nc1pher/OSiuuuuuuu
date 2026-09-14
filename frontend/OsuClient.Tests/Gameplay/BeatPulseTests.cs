using NUnit.Framework;
using OsuClient.Game.Beatmaps;
using OsuClient.Game.Screens.Gameplay;

namespace OsuClient.Tests.Gameplay
{
    /// <summary>
    /// Pure timing maths, no osu.Framework dependency — runs headless and
    /// deterministically, the same way as <see cref="JudgementProcessorTests"/>.
    /// </summary>
    [TestFixture]
    public class BeatPulseTests
    {
        private const double tolerance = 1e-6;

        // 120 BPM (500ms/beat) starting at 1000ms.
        private static Beatmap beatmap => BeatmapDecoder.Decode(
            """
            osu file format v14

            [General]
            AudioFilename:audio.mp3

            [Metadata]
            Version:Test

            [Difficulty]
            OverallDifficulty:5

            [TimingPoints]
            1000,500,4,1,0,70,1,0

            [HitObjects]
            100,100,2000,5,0,0:0:0:0:
            200,200,8000,5,0,0:0:0:0:
            """);

        [TestCase(1000, 0)]
        [TestCase(1250, 0.5)]
        [TestCase(1499, 0.998)]
        [TestCase(1500, 0)]
        public void PhaseAtMatchesExpectedPointInTheBeat(double time, double expectedPhase)
        {
            Assert.That(BeatPulse.PhaseAt(beatmap, time), Is.EqualTo(expectedPhase).Within(0.01));
        }

        [Test]
        public void PhaseAtHandlesNegativeLeadInTimeWithoutThrowing()
        {
            // 500ms before the timing point is exactly one whole beat back —
            // same phase as the point itself.
            Assert.That(BeatPulse.PhaseAt(beatmap, 500), Is.EqualTo(0).Within(0.01));

            // A quarter-beat before the point.
            Assert.That(BeatPulse.PhaseAt(beatmap, 875), Is.EqualTo(0.75).Within(0.01));
        }

        [Test]
        public void PhaseAtIsNaNWithoutATimingPoint()
        {
            var untimed = new Beatmap();

            Assert.That(double.IsNaN(BeatPulse.PhaseAt(untimed, 1000)), Is.True);
        }

        [Test]
        public void IntensityAtPeaksOnTheBeatAndBottomsOutHalfway()
        {
            Assert.That(BeatPulse.IntensityAt(0, sharpness: 4), Is.EqualTo(1).Within(tolerance));
            Assert.That(BeatPulse.IntensityAt(0.5, sharpness: 4), Is.EqualTo(0).Within(tolerance));
        }

        [Test]
        public void IntensityAtIsSymmetricAroundTheBeat()
        {
            Assert.That(BeatPulse.IntensityAt(0.1, sharpness: 4),
                Is.EqualTo(BeatPulse.IntensityAt(0.9, sharpness: 4)).Within(tolerance));
        }

        [Test]
        public void IntensityAtIsZeroForANaNPhase()
        {
            Assert.That(BeatPulse.IntensityAt(double.NaN, sharpness: 4), Is.EqualTo(0));
        }

        [Test]
        public void HigherSharpnessNarrowsThePulse()
        {
            // Away from the beat, a sharper pulse should have already fallen
            // further than a gentler one.
            double gentle = BeatPulse.IntensityAt(0.2, sharpness: 1);
            double sharp = BeatPulse.IntensityAt(0.2, sharpness: 8);

            Assert.That(sharp, Is.LessThan(gentle));
        }

        [Test]
        public void PlayableEnvelopeIsOpenInTheMiddleOfTheSpan()
        {
            // Hit objects at 2000 and 8000 in the shared beatmap.
            Assert.That(BeatPulse.PlayableEnvelope(beatmap, 5000), Is.EqualTo(1).Within(tolerance));
        }

        [Test]
        public void PlayableEnvelopeIsClosedWellBeforeAndAfterTheSpan()
        {
            Assert.That(BeatPulse.PlayableEnvelope(beatmap, 0), Is.EqualTo(0));
            Assert.That(BeatPulse.PlayableEnvelope(beatmap, 20000), Is.EqualTo(0));
        }

        [Test]
        public void PlayableEnvelopeCrossfadesAtTheEdges()
        {
            // 250ms before the first object: halfway through the 500ms fade-in.
            Assert.That(BeatPulse.PlayableEnvelope(beatmap, 1750), Is.EqualTo(0.5).Within(0.01));
        }

        [Test]
        public void PlayableEnvelopeIsZeroWhenTheMapHasNoPlayableSpan()
        {
            Assert.That(BeatPulse.PlayableEnvelope(new Beatmap(), 1000), Is.EqualTo(0));
        }
    }
}
