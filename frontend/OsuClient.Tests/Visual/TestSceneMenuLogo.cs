using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Testing;
using OsuClient.Game.Graphics;
using OsuClient.Game.Screens.MainMenu;
using osuTK;

namespace OsuClient.Tests.Visual
{
    /// <summary>
    /// The menu's record: it spins, its centre does not, the only thing
    /// written on it is RIMO, and the beat no longer makes it pulse.
    /// </summary>
    [TestFixture]
    public partial class TestSceneMenuLogo : TestScene
    {
        private MenuLogo logo = null!;

        [SetUpSteps]
        public void SetUpSteps()
        {
            AddStep("create logo", () => Child = logo = new MenuLogo
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(400),
            });

            AddUntilStep("loaded", () => logo.IsLoaded);
        }

        [Test]
        public void TestTheRecordSpins()
        {
            float before = 0;

            AddStep("note the turn", () => before = logo.Spin);
            AddWaitStep("let it turn", 5);
            AddAssert("it turned", () => Math.Abs(logo.Spin - before) > 1);
        }

        [Test]
        public void TestOnlyRimoIsWrittenAndItStaysUpright()
        {
            AddAssert("the only text is RIMO", () =>
                logo.ChildrenOfType<RetroText>().Select(t => t.Text).SequenceEqual(new[] { "RIMO" }));

            AddWaitStep("let the record turn", 5);

            // Upright on screen: a text that turned with the record would
            // have a tilted quad by now.
            AddAssert("RIMO has not turned", () =>
            {
                var quad = logo.ChildrenOfType<RetroText>().Single().ScreenSpaceDrawQuad;
                return Math.Abs(quad.TopLeft.Y - quad.TopRight.Y) < 0.5f;
            });

            AddAssert("RIMO is centred", () =>
            {
                var text = logo.ChildrenOfType<RetroText>().Single().ScreenSpaceDrawQuad.Centre;
                return Vector2.Distance(text, logo.ScreenSpaceDrawQuad.Centre) < 1;
            });
        }

        [Test]
        public void TestTheBeatNoLongerPulsesIt()
        {
            float width = 0;

            AddStep("note its size", () => width = logo.ChildrenOfType<RetroText>().Single().ScreenSpaceDrawQuad.Width);
            AddStep("hit it with a full beat", () => logo.SetBeat(1));
            AddWaitStep("a few frames", 3);
            AddAssert("same size", () =>
                Math.Abs(logo.ChildrenOfType<RetroText>().Single().ScreenSpaceDrawQuad.Width - width) < 0.5f);
        }
    }
}
