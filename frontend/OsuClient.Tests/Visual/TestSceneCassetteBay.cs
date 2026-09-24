using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Testing;
using OsuClient.Game.Screens.Generation;
using osuTK.Input;

namespace OsuClient.Tests.Visual
{
    /// <summary>
    /// The empty bay is a way to load a tape: clicking it asks for the file
    /// browser, just as LOAD TAPE does. With a tape in, it does not — clicks
    /// there belong to the label's fields.
    ///
    /// Clicked with the real mouse, through the input manager, so this also
    /// proves nothing on top of the empty bay swallows the click.
    /// </summary>
    [TestFixture]
    public partial class TestSceneCassetteBay : ManualInputManagerTestScene
    {
        private CassetteBay bay = null!;
        private int browseRequests;

        [SetUpSteps]
        public void SetUpSteps()
        {
            AddStep("create bay", () =>
            {
                browseRequests = 0;
                Child = bay = new CassetteBay { BrowseRequested = () => browseRequests++ };
            });
        }

        private void clickTheMiddle() => AddStep("click the middle of the bay", () =>
        {
            InputManager.MoveMouseTo(bay.ScreenSpaceDrawQuad.Centre);
            InputManager.Click(MouseButton.Left);
        });

        [Test]
        public void TestClickingTheEmptyBayAsksToBrowse()
        {
            clickTheMiddle();
            AddAssert("asked for the browser", () => browseRequests == 1);
        }

        [Test]
        public void TestClickingALoadedTapeDoesNot()
        {
            AddStep("load a tape", () => bay.SetFile("song.mp3"));
            AddWaitStep("let it slide in", 5);

            clickTheMiddle();
            AddAssert("no browser", () => browseRequests == 0);
        }
    }
}
