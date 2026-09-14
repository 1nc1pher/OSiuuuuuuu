using NUnit.Framework;
using osu.Framework.Timing;
using OsuClient.Game.Screens.Gameplay.HUD;
using osuTK.Graphics;

namespace OsuClient.Tests.Visual
{
    /// <summary>
    /// The combo counter's two animations: the classic beatmania pop (jump
    /// up in size, then ease smoothly back down — no bounce) on every
    /// increment, and a red flash on a combo break.
    ///
    /// Driven by a parked <see cref="ManualClock"/> rather than real time:
    /// headless test frames can jump hundreds of milliseconds in a single
    /// step, easily enough to run a ~400ms animation start to finish before
    /// an assertion checking "right after" gets a turn.
    /// </summary>
    [TestFixture]
    public partial class TestSceneComboCounter : osu.Framework.Testing.TestScene
    {
        private ManualClock manualClock = null!;
        private ComboCounter counter = null!;

        private void createCounter()
        {
            AddStep("create counter", () =>
            {
                manualClock = new ManualClock();
                Child = counter = new ComboCounter { Clock = new FramedClock(manualClock) };
            });
            AddUntilStep("loaded", () => counter.IsLoaded);
        }

        private void advanceTo(double time) => AddStep($"clock to {time}ms", () => manualClock.CurrentTime = time);

        [Test]
        public void TestIncrementPopsThenSettlesSmoothly()
        {
            createCounter();

            advanceTo(0);
            AddStep("combo to 1", () => counter.SetCombo(1));
            AddAssert("pops up larger immediately", () => counter.CurrentScale > 1.4f);

            advanceTo(200);
            AddAssert("still settling partway through", () => counter.CurrentScale is > 1f and < 1.6f);

            advanceTo(401);
            AddAssert("fully settled at normal size", () => counter.CurrentScale == 1f);
        }

        [Test]
        public void TestSettleNeverOvershootsPastNormalSize()
        {
            createCounter();

            advanceTo(0);
            AddStep("combo to 1", () => counter.SetCombo(1));

            // OutQuint decays monotonically from 1.6 down to 1 — unlike an
            // elastic ease, it should never dip below 1 on the way there.
            for (double t = 0; t <= 400; t += 40)
            {
                double time = t;
                advanceTo(time);
                AddAssert($"scale >= 1 at {time}ms", () => counter.CurrentScale >= 1f);
            }
        }

        [Test]
        public void TestComboBreakFlashesRedThenFadesToWhite()
        {
            createCounter();

            advanceTo(0);
            AddStep("build up a combo", () => counter.SetCombo(5));

            advanceTo(1000);
            AddStep("break the combo", () => counter.SetCombo(0));
            AddAssert("flashes red immediately", () => counter.CurrentColour.R > 0.9f && counter.CurrentColour.G < 0.6f);

            advanceTo(1250);
            AddAssert("partway back toward white", () => counter.CurrentColour.G is > 0.35f and < 1f);

            advanceTo(1501);
            AddAssert("fully faded back to white", () => counter.CurrentColour == Color4.White);
        }

        [Test]
        public void TestZeroComboWithNoPriorRunDoesNotFlash()
        {
            createCounter();

            // SetCombo(0) with nothing built up yet isn't a "break" — no
            // flash should fire.
            advanceTo(0);
            AddStep("combo stays at 0", () => counter.SetCombo(0));
            AddAssert("stays white, no break animation", () => counter.CurrentColour == Color4.White);
        }
    }
}
