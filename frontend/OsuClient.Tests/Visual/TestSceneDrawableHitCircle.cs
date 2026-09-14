using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Framework.Timing;
using OsuClient.Game.Beatmaps;
using OsuClient.Game.Beatmaps.HitObjects;
using OsuClient.Game.Screens.Gameplay;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Tests.Visual
{
    /// <summary>
    /// Covers a hit circle's press handling directly: whether the cursor is
    /// over it, and whether presses outside the hit window are ignored rather
    /// than judged (clicking early must not destroy the note).
    /// </summary>
    [TestFixture]
    public partial class TestSceneDrawableHitCircle : osu.Framework.Testing.TestScene
    {
        private const double start_time = 1000;

        private Container playfield = null!;
        private ManualClock clock = null!;
        private DrawableHitCircle circle = null!;
        private HitResult? result;

        private Vector2 circleCentreScreenSpace => playfield.ToScreenSpace(new Vector2(256, 192));

        private void createCircle()
        {
            AddStep("create circle", () =>
            {
                result = null;

                var difficulty = new BeatmapDifficulty { CircleSize = 4, OverallDifficulty = 5, ApproachRate = 9 };
                var data = new HitCircleData { Position = new Vector2(256, 192), StartTime = start_time };

                Child = playfield = new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(512, 384),
                    // Park gameplay time before the object so nothing expires
                    // while the test steps run.
                    Clock = new FramedClock(clock = new ManualClock { CurrentTime = start_time - 5000 }),
                };

                circle = new DrawableHitCircle(data, difficulty, Color4.White, 1);
                circle.Judged += (_, r) => result = r;

                playfield.Add(circle);
            });

            AddUntilStep("circle loaded", () => circle.IsLoaded);
        }

        [Test]
        public void TestPressOnTimeOverCircleIsAGreat()
        {
            createCircle();

            AddAssert("press is consumed", () => circle.TryPress(start_time, circleCentreScreenSpace));
            AddAssert("judged a Great", () => result == HitResult.Great);
        }

        [Test]
        public void TestPressAwayFromCircleIsIgnored()
        {
            createCircle();

            AddAssert("press is not consumed",
                () => !circle.TryPress(start_time, playfield.ToScreenSpace(new Vector2(10, 10))));

            AddAssert("nothing was judged", () => result == null && !circle.IsJudged);
        }

        [Test]
        public void TestPressLongBeforeTheHitWindowIsIgnored()
        {
            createCircle();

            // The press is way early, so it isn't meant for this circle —
            // osu! ignores it rather than judging it a miss.
            AddAssert("press is not consumed",
                () => !circle.TryPress(start_time - 800, circleCentreScreenSpace));

            AddAssert("circle survives unjudged", () => result == null && !circle.IsJudged);
        }

        /// <summary>
        /// Steps gameplay time to <paramref name="time"/> and asserts how far
        /// the approach ring has closed by then.
        ///
        /// The ring has to close at a constant rate, so the gap between it and
        /// the circle reads as the time still left. An eased curve spends
        /// nearly all its travel up front and looks shut well before the note
        /// is due — which is exactly what this pins down, and what neither the
        /// compiler nor a judgement test can see.
        /// </summary>
        private void assertApproachScale(double time, float expected)
        {
            AddStep($"gameplay time {time:0}", () => clock.CurrentTime = time);

            // Asserted on a later frame than the one that moved the clock, so
            // the transform has actually been applied by the time it's read.
            AddAssert($"approach ring at {expected:0.00}x",
                () => circle.ChildrenOfType<ApproachCircle>().Single().Scale.X,
                () => Is.EqualTo(expected).Within(0.15f));
        }

        [Test]
        public void TestApproachCircleClosesAtAConstantRate()
        {
            createCircle();

            double preempt = JudgementProcessor.Preempt(9);
            double appearTime = start_time - preempt;

            assertApproachScale(appearTime, 4f);
            assertApproachScale(appearTime + preempt * 0.25, 3.25f);
            assertApproachScale(appearTime + preempt * 0.5, 2.5f);
            assertApproachScale(appearTime + preempt * 0.75, 1.75f);
            assertApproachScale(start_time, 1f);
        }

        [Test]
        public void TestCircleMissesOnceItsWindowCloses()
        {
            createCircle();

            AddStep("run past the hit window",
                () => circle.UpdateGameplay(start_time + 500, circleCentreScreenSpace, false));

            AddAssert("judged a Miss", () => result == HitResult.Miss);
        }
    }
}
