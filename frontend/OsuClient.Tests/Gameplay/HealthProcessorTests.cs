using NUnit.Framework;
using OsuClient.Game.Screens.Gameplay;

namespace OsuClient.Tests.Gameplay
{
    /// <summary>
    /// HP behaviour: drains with time, refills on hits, and ends the run when
    /// it empties. Pure logic, no game host.
    /// </summary>
    [TestFixture]
    public class HealthProcessorTests
    {
        [Test]
        public void StartsFullAndUnfailed()
        {
            var health = new HealthProcessor(5);

            Assert.Multiple(() =>
            {
                Assert.That(health.Health, Is.EqualTo(1).Within(1e-9));
                Assert.That(health.HasFailed, Is.False);
            });
        }

        [Test]
        public void DrainsOverTime()
        {
            var health = new HealthProcessor(5);

            health.Drain(1000);

            Assert.That(health.Health, Is.LessThan(1).And.GreaterThan(0.9));
        }

        [Test]
        public void HigherDrainRateDrainsFaster()
        {
            var gentle = new HealthProcessor(0);
            var harsh = new HealthProcessor(10);

            gentle.Drain(1000);
            harsh.Drain(1000);

            Assert.That(harsh.Health, Is.LessThan(gentle.Health));
        }

        [Test]
        public void HitsRestoreHealthInJudgementOrder()
        {
            double healthAfter(HitResult result)
            {
                var processor = new HealthProcessor(5);
                processor.Drain(10000); // make room to recover into
                double before = processor.Health;
                processor.Apply(result);
                return processor.Health - before;
            }

            Assert.Multiple(() =>
            {
                Assert.That(healthAfter(HitResult.Great), Is.GreaterThan(healthAfter(HitResult.Ok)));
                Assert.That(healthAfter(HitResult.Ok), Is.GreaterThan(healthAfter(HitResult.Meh)));
                Assert.That(healthAfter(HitResult.Meh), Is.GreaterThan(0));
            });
        }

        [Test]
        public void MissesCostHealthAndCostMoreAtHigherDrainRate()
        {
            var gentle = new HealthProcessor(0);
            var harsh = new HealthProcessor(10);

            gentle.Apply(HitResult.Miss);
            harsh.Apply(HitResult.Miss);

            Assert.Multiple(() =>
            {
                Assert.That(gentle.Health, Is.LessThan(1));
                Assert.That(harsh.Health, Is.LessThan(gentle.Health));
            });
        }

        [Test]
        public void HealthCannotExceedFull()
        {
            var health = new HealthProcessor(5);

            for (int i = 0; i < 20; i++)
                health.Apply(HitResult.Great);

            Assert.That(health.Health, Is.EqualTo(1).Within(1e-9));
        }

        [Test]
        public void RunFailsOnceHealthEmpties()
        {
            var health = new HealthProcessor(5);

            for (int i = 0; i < 50; i++)
                health.Apply(HitResult.Miss);

            Assert.Multiple(() =>
            {
                Assert.That(health.Health, Is.EqualTo(0).Within(1e-9));
                Assert.That(health.HasFailed, Is.True);
            });
        }

        [Test]
        public void FailingLatchesEvenIfHealthRecovers()
        {
            var health = new HealthProcessor(5);

            for (int i = 0; i < 50; i++)
                health.Apply(HitResult.Miss);

            for (int i = 0; i < 20; i++)
                health.Apply(HitResult.Great);

            // Health came back, but the run is still a fail — recovering
            // doesn't un-fail a play in osu!.
            Assert.Multiple(() =>
            {
                Assert.That(health.Health, Is.GreaterThan(0));
                Assert.That(health.HasFailed, Is.True);
            });
        }

        [Test]
        public void SliderTicksNudgeHealthEitherWay()
        {
            var processor = new HealthProcessor(5);
            processor.Drain(10000);

            double before = processor.Health;
            processor.ApplyTick(true);
            double afterHit = processor.Health;

            processor.ApplyTick(false);

            Assert.Multiple(() =>
            {
                Assert.That(afterHit, Is.GreaterThan(before));
                Assert.That(processor.Health, Is.LessThan(afterHit));
            });
        }
    }
}
