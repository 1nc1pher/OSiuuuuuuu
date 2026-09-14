using NUnit.Framework;
using OsuClient.Game.Graphics;

namespace OsuClient.Tests.Visual
{
    /// <summary>
    /// Loading a beatmap's background image: a missing/absent path shows
    /// nothing rather than throwing, so an un-customized beatmap plays
    /// exactly as it always has.
    ///
    /// Loading a real JPEG end to end (a 1024x572 image manually dropped into
    /// a real beatmap set's folder, per FRONTEND_PLAN.md's Phase 6 notes) was
    /// verified by hand rather than kept as a permanent test here: pinning an
    /// absolute path into `data/output/` would make the suite depend on a
    /// file outside the repo that only exists on this machine.
    /// </summary>
    [TestFixture]
    public partial class TestSceneBeatmapBackground : osu.Framework.Testing.TestScene
    {
        [Test]
        public void TestMissingPathShowsNoTexture()
        {
            BeatmapBackground background = null!;

            AddStep("create with null path", () => Child = background = new BeatmapBackground(null));
            AddUntilStep("loaded", () => background.IsLoaded);

            AddAssert("no texture", () => background.Texture == null);
        }

        [Test]
        public void TestNonExistentFileShowsNoTextureRatherThanThrowing()
        {
            BeatmapBackground background = null!;

            AddStep("create with a path that doesn't exist",
                () => Child = background = new BeatmapBackground(@"D:\nowhere\nope.jpg"));
            AddUntilStep("loaded", () => background.IsLoaded);

            AddAssert("no texture", () => background.Texture == null);
        }
    }
}
