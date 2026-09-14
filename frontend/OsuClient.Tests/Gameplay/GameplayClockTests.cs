using NUnit.Framework;
using OsuClient.Game.Screens.Gameplay;

namespace OsuClient.Tests.Gameplay
{
    /// <summary>
    /// The clock's job is to run on wall time until the audio takes over, and
    /// then stay locked to the audio — which is what keeps what you hear, what
    /// you see and what gets judged from drifting apart.
    /// </summary>
    [TestFixture]
    public class GameplayClockTests
    {
        [Test]
        public void FreeRunsOnWallTimeWhileThereIsNoAudio()
        {
            var clock = new GameplayClock(-1000);

            for (int i = 0; i < 10; i++)
                clock.Advance(16, null);

            Assert.That(clock.CurrentTime, Is.EqualTo(-840).Within(1e-6));
        }

        [Test]
        public void SnapsWhenTheAudioIsFarFromTheClock()
        {
            var clock = new GameplayClock(0);

            // The track starting, or hitching, puts it far from where the
            // clock had free-run to: follow the audio immediately.
            clock.Advance(16, 5000);

            Assert.That(clock.CurrentTime, Is.EqualTo(5000).Within(1e-6));
        }

        [Test]
        public void EasesTowardTheAudioForSmallDrift()
        {
            var clock = new GameplayClock(1000);

            // 20ms adrift: correct gradually rather than jumping, which would
            // show up as hit objects visibly stuttering.
            clock.Advance(0, 1020);

            Assert.That(clock.CurrentTime, Is.GreaterThan(1000).And.LessThan(1020));
        }

        [Test]
        public void ConvergesOnTheAudioOverSuccessiveFrames()
        {
            var clock = new GameplayClock(1000);

            for (int i = 0; i < 60; i++)
                clock.Advance(0, 1020);

            Assert.That(clock.CurrentTime, Is.EqualTo(1020).Within(0.5));
        }

        [Test]
        public void TracksASteadilyAdvancingTrack()
        {
            var clock = new GameplayClock(0);
            double audio = 0;

            for (int i = 0; i < 100; i++)
            {
                audio += 16;
                clock.Advance(16, audio);
            }

            Assert.That(clock.CurrentTime, Is.EqualTo(audio).Within(1.0));
        }
    }
}
