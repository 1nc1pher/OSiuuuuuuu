using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Screens;
using osu.Framework.Testing;
using OsuClient.Game.Graphics;
using OsuClient.Game.Screens.Generation;
using OsuClient.Game.Screens.MainMenu;
using OsuClient.Game.Screens.SongSelect;

namespace OsuClient.Tests.Visual
{
    /// <summary>
    /// The menu's screen changes ride a ripple: the next screen only arrives
    /// once the ripple has covered the menu, and a second press while one is
    /// running changes nothing.
    /// </summary>
    [TestFixture]
    public partial class TestSceneScreenRipple : osu.Framework.Testing.TestScene
    {
        private ScreenStack stack = null!;
        private MainMenuScreen menu = null!;
        private ScreenRipple ripple = null!;

        [SetUpSteps]
        public void SetUpSteps()
        {
            AddStep("load menu with ripple", () =>
            {
                Child = new RippleHost(out stack, out ripple);
                stack.Push(menu = new MainMenuScreen());
            });

            AddUntilStep("menu loaded", () => menu.IsCurrentScreen());
        }

        [Test]
        public void TestPlayWaitsForTheRippleThenOpensSongSelect()
        {
            AddStep("press play", () => menu.Play());

            AddAssert("ripple running", () => ripple.Running);
            AddAssert("menu still showing under it", () => menu.IsCurrentScreen());

            AddUntilStep("song select arrives", () => stack.CurrentScreen is RetroSongSelectScreen);
            AddUntilStep("ripple finishes", () => !ripple.Running);
        }

        [Test]
        public void TestMenuComesBackWholeAfterARippleExit()
        {
            AddStep("press play", () => menu.Play());
            AddUntilStep("song select arrives", () => stack.CurrentScreen is RetroSongSelectScreen);
            AddUntilStep("menu faded out", () => menu.Alpha < 0.01f);

            AddStep("back out", () => stack.CurrentScreen.Exit());
            AddUntilStep("menu current", () => menu.IsCurrentScreen());

            // It left faded out; it has to come back whole, not reappear
            // half-transparent.
            AddUntilStep("menu fully visible", () => menu.Alpha > 0.99f);
        }

        [Test]
        public void TestCreateOpensTheTapeDeck()
        {
            AddStep("press create", () => menu.Create());
            AddUntilStep("tape deck arrives", () => stack.CurrentScreen is UploadScreen);
        }

        [Test]
        public void TestASecondPressDuringTheRippleIsIgnored()
        {
            AddStep("press play twice", () =>
            {
                menu.Play();
                menu.Play();
            });

            AddUntilStep("song select arrives", () => stack.CurrentScreen is RetroSongSelectScreen);
            AddUntilStep("ripple finishes", () => !ripple.Running);
            AddAssert("only one screen pushed", () =>
                menu.GetChildScreen() is RetroSongSelectScreen songSelect && songSelect.GetChildScreen() == null);
        }

        /// <summary>A screen stack with the ripple cached above it, as the game has.</summary>
        private partial class RippleHost : CompositeDrawable
        {
            [Cached]
            private readonly ScreenRipple ripple = new ScreenRipple();

            public RippleHost(out ScreenStack stack, out ScreenRipple ripple)
            {
                RelativeSizeAxes = Axes.Both;

                ripple = this.ripple;
                stack = new ScreenStack { RelativeSizeAxes = Axes.Both };

                InternalChildren = new Drawable[] { stack, this.ripple };
            }
        }
    }
}
