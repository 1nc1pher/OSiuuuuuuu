using NUnit.Framework;
using osu.Framework.Timing;
using OsuClient.Game.Screens.Gameplay.HUD;
using osuTK.Graphics;

namespace OsuClient.Tests.Visual
{
    /// <summary>
    /// The combo counter's two animations: its digit wheels rolling up to the
    /// new combo on every hit (carrying into the next wheel past 9, as a
    /// mechanical tape counter does), and on a combo break spinning back to
    /// zero with a red flash.
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
        public void TestIncrementRollsTheWheels()
        {
            createCounter();

            advanceTo(0);
            AddStep("combo to 1", () => counter.SetCombo(1));
            AddAssert("still rolling at the start", () => counter.Reading == "0000");

            advanceTo(300);
            AddAssert("reads 0001 once rolled", () => counter.Reading == "0001");

            AddStep("combo to 12", () => counter.SetCombo(12));
            advanceTo(600);
            AddAssert("reads 0012", () => counter.Reading == "0012");
        }

        [Test]
        public void TestNineCarriesIntoTheNextWheel()
        {
            createCounter();

            advanceTo(0);
            AddStep("combo to 9", () => counter.SetCombo(9));
            advanceTo(300);
            AddAssert("reads 0009", () => counter.Reading == "0009");

            // The ones wheel rolls on through 9 to 0 while the tens wheel
            // steps up, as a mechanical counter carries.
            AddStep("combo to 10", () => counter.SetCombo(10));
            advanceTo(700);
            AddAssert("reads 0010", () => counter.Reading == "0010");
        }

        [Test]
        public void TestComboBreakSpinsBackToZero()
        {
            createCounter();

            advanceTo(0);
            AddStep("build up a combo", () => counter.SetCombo(123));
            advanceTo(400);
            AddAssert("reads 0123", () => counter.Reading == "0123");

            AddStep("break the combo", () => counter.SetCombo(0));
            advanceTo(800);
            AddAssert("back to 0000", () => counter.Reading == "0000");
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
