using NUnit.Framework;
using OsuClient.Game.Screens.Gameplay;

namespace OsuClient.Tests.Gameplay
{
    [TestFixture]
    public class ScoreProcessorTests
    {
        private const double tolerance = 1e-9;

        private ScoreProcessor processor = null!;

        [SetUp]
        public void SetUp()
        {
            processor = new ScoreProcessor();
        }

        [Test]
        public void StartsAtFullAccuracyWithNothingJudged()
        {
            Assert.That(processor.Accuracy, Is.EqualTo(100).Within(tolerance));
            Assert.That(processor.TotalJudged, Is.EqualTo(0));
        }

        [Test]
        public void ComboIncrementsOnNonMissJudgements()
        {
            processor.Apply(HitResult.Great);
            processor.Apply(HitResult.Ok);
            processor.Apply(HitResult.Meh);

            Assert.That(processor.Combo, Is.EqualTo(3));
            Assert.That(processor.MaxCombo, Is.EqualTo(3));
        }

        [Test]
        public void MissResetsComboButKeepsMaxCombo()
        {
            processor.Apply(HitResult.Great);
            processor.Apply(HitResult.Great);
            processor.Apply(HitResult.Miss);

            Assert.That(processor.Combo, Is.EqualTo(0));
            Assert.That(processor.MaxCombo, Is.EqualTo(2));
            Assert.That(processor.CountMiss, Is.EqualTo(1));
        }

        [Test]
        public void AccuracyIsWeightedByJudgementValue()
        {
            processor.Apply(HitResult.Great); // 300
            processor.Apply(HitResult.Miss);  // 0

            // (300 + 0) / (2 * 300) * 100 = 50%
            Assert.That(processor.Accuracy, Is.EqualTo(50).Within(tolerance));
        }

        [Test]
        public void PerfectPlayHasFullAccuracy()
        {
            processor.Apply(HitResult.Great);
            processor.Apply(HitResult.Great);
            processor.Apply(HitResult.Great);

            Assert.That(processor.Accuracy, Is.EqualTo(100).Within(tolerance));
        }

        [Test]
        public void ScoreDoesNotIncreaseOnMiss()
        {
            processor.Apply(HitResult.Great);
            long scoreAfterHit = processor.Score;

            processor.Apply(HitResult.Miss);

            Assert.That(processor.Score, Is.EqualTo(scoreAfterHit));
        }

        [Test]
        public void PerfectPlayEarnsAnSS()
        {
            for (int i = 0; i < 20; i++)
                processor.Apply(HitResult.Great);

            Assert.That(processor.Grade, Is.EqualTo(Grade.SS));
        }

        [Test]
        public void OneImperfectHitDropsBelowSS()
        {
            for (int i = 0; i < 19; i++)
                processor.Apply(HitResult.Great);

            processor.Apply(HitResult.Ok);

            Assert.That(processor.Grade, Is.EqualTo(Grade.S));
        }

        [Test]
        public void AMissKeepsTheGradeOffS()
        {
            for (int i = 0; i < 99; i++)
                processor.Apply(HitResult.Great);

            processor.Apply(HitResult.Miss);

            // Over 90% greats, but an S requires a clean run.
            Assert.That(processor.Grade, Is.EqualTo(Grade.A));
        }

        [Test]
        public void CleanRunIsPromotedOverOneWithMisses()
        {
            var clean = new ScoreProcessor();
            var messy = new ScoreProcessor();

            // Both land 85% greats; only one of them dropped an object.
            for (int i = 0; i < 85; i++)
            {
                clean.Apply(HitResult.Great);
                messy.Apply(HitResult.Great);
            }

            for (int i = 0; i < 15; i++)
            {
                clean.Apply(HitResult.Ok);
                messy.Apply(HitResult.Miss);
            }

            Assert.Multiple(() =>
            {
                Assert.That(clean.Grade, Is.EqualTo(Grade.A));
                Assert.That(messy.Grade, Is.EqualTo(Grade.B));
            });
        }

        [Test]
        public void SloppyPlayLandsLow()
        {
            for (int i = 0; i < 50; i++)
                processor.Apply(HitResult.Great);

            for (int i = 0; i < 50; i++)
                processor.Apply(HitResult.Meh);

            Assert.That(processor.Grade, Is.EqualTo(Grade.D));
        }

        [Test]
        public void NothingJudgedIsNotAnSS()
        {
            Assert.That(processor.Grade, Is.EqualTo(Grade.D));
        }

        [Test]
        public void ApplyTickMissedResetsCombo()
        {
            processor.Apply(HitResult.Great);
            processor.ApplyTick(false);

            Assert.That(processor.Combo, Is.EqualTo(0));
        }

        [Test]
        public void ApplyTickHitAddsScoreAndCombo()
        {
            processor.ApplyTick(true);

            Assert.Multiple(() =>
            {
                Assert.That(processor.Combo, Is.EqualTo(1));
                Assert.That(processor.Score, Is.EqualTo(10));
            });
        }

        [Test]
        public void ApplyTickNeverCountsTowardJudgementsOrAccuracy()
        {
            processor.ApplyTick(true);
            processor.ApplyTick(false);

            Assert.Multiple(() =>
            {
                Assert.That(processor.TotalJudged, Is.EqualTo(0));
                Assert.That(processor.Accuracy, Is.EqualTo(100).Within(tolerance));
            });
        }

        [Test]
        public void ApplyBonusAddsMoreScoreThanATick()
        {
            processor.ApplyBonus();

            Assert.Multiple(() =>
            {
                Assert.That(processor.Combo, Is.EqualTo(1));
                Assert.That(processor.MaxCombo, Is.EqualTo(1));
                Assert.That(processor.Score, Is.GreaterThan(10));
            });
        }

        [Test]
        public void ApplyBonusNeverCountsTowardJudgementsOrAccuracy()
        {
            processor.ApplyBonus();
            processor.ApplyBonus();
            processor.ApplyBonus();

            Assert.Multiple(() =>
            {
                Assert.That(processor.TotalJudged, Is.EqualTo(0));
                Assert.That(processor.Accuracy, Is.EqualTo(100).Within(tolerance));
                Assert.That(processor.CountGreat, Is.EqualTo(0));
            });
        }

        [Test]
        public void ApplyBonusBuildsOnExistingCombo()
        {
            processor.Apply(HitResult.Great);
            processor.Apply(HitResult.Great);

            processor.ApplyBonus();

            Assert.That(processor.Combo, Is.EqualTo(3));
            Assert.That(processor.MaxCombo, Is.EqualTo(3));
        }

        [Test]
        public void ScoreIncreasesWithCombo()
        {
            processor.Apply(HitResult.Great);
            long firstHitScore = processor.Score;

            for (int i = 0; i < 20; i++)
                processor.Apply(HitResult.Great);

            long marginalGain = processor.Score - firstHitScore;

            // 20 more greats at higher combo should earn more than 20x the
            // first (combo-1) great's contribution.
            Assert.That(marginalGain, Is.GreaterThan(firstHitScore * 20));
        }
    }
}
