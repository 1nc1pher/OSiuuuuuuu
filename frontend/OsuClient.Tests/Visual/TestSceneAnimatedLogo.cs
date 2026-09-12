using NUnit.Framework;
using osu.Framework.Graphics;
using OsuClient.Game.Graphics;

namespace OsuClient.Tests.Visual
{
    /// <summary>
    /// Phase 0 visual test: drops the animated logo into an empty scene so the
    /// animation can be eyeballed in the test browser, and asserts it actually
    /// loaded (which is what catches a broken renderer/scene-graph setup).
    /// </summary>
    [TestFixture]
    public partial class TestSceneAnimatedLogo : osu.Framework.Testing.TestScene
    {
        private AnimatedLogo logo = null!;

        [Test]
        public void TestLogoLoads()
        {
            AddStep("add logo", () => Child = logo = new AnimatedLogo
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
            });

            AddUntilStep("logo is loaded", () => logo.IsLoaded);
        }
    }
}
