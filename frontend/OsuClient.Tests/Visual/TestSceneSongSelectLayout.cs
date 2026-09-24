using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Screens;
using osu.Framework.Testing;
using OsuClient.Game.Screens.SongSelect;

namespace OsuClient.Tests.Visual
{
    /// <summary>
    /// The left column's three pieces: the difficulty cassettes sit centred
    /// in the gap between the info panel and the search bar, not hung from
    /// the top of it.
    ///
    /// Runs against the repository's own library, like the other song select
    /// scenes; with nothing in it there are no cassettes to measure and the
    /// test says so rather than passing vacuously.
    /// </summary>
    [TestFixture]
    public partial class TestSceneSongSelectLayout : TestScene
    {
        /// <summary>
        /// The menu loads song select in the background while its ripple
        /// plays. Everything heavy has to happen in that load — the first
        /// song's cassettes built and their labels rasterized — not in the
        /// first frame on screen, where it stalled the transition.
        /// </summary>
        [Test]
        public void TestFirstSongIsBuiltDuringTheBackgroundLoad()
        {
            RetroSongSelectScreen screen = null!;
            bool loaded = false;

            AddStep("load in the background", () =>
            {
                screen = new RetroSongSelectScreen();
                LoadComponentAsync(screen, _ => loaded = true);
            });

            AddUntilStep("loaded", () => loaded);

            AddAssert("cassettes already built, before it is on screen", () =>
                screen.ChildrenOfType<CassetteButton>().Any());

            AddAssert("and their text already rendered", () =>
                screen.ChildrenOfType<CassetteButton>()
                      .SelectMany(c => c.ChildrenOfType<OsuClient.Game.Graphics.RetroText>())
                      .Where(t => t.Text.Length > 0)
                      .All(t => t.Texture != null));
        }

        [Test]
        public void TestDifficultiesSitCentredBetweenPanelAndSearch()
        {
            RetroSongSelectScreen screen = null!;

            AddStep("open song select", () =>
            {
                var stack = new ScreenStack { RelativeSizeAxes = Axes.Both };
                Child = stack;
                stack.Push(screen = new RetroSongSelectScreen());
            });

            AddUntilStep("cassettes shown", () =>
                screen.IsLoaded && screen.ChildrenOfType<CassetteButton>().Any());

            // Layout settles over a few frames as the panel's text measures.
            AddWaitStep("let layout settle", 5);

            AddAssert("the analyse plate sits right under the panel, as wide as it", () =>
            {
                var panel = screen.ChildrenOfType<SongInfoPanel>().Single().ScreenSpaceDrawQuad;
                var plate = screen.ChildrenOfType<AnalyseDeckButton>().Single().ScreenSpaceDrawQuad;

                float gap = plate.TopLeft.Y - panel.BottomLeft.Y;

                return gap > 0 && gap < 30
                       && Math.Abs(plate.TopLeft.X - panel.TopLeft.X) < 2
                       && Math.Abs(plate.Width - panel.Width) < 2;
            });

            AddAssert("gap above equals gap below", () =>
            {
                // Measured from the analyse plate, which is what now sits
                // above the cassettes.
                var panel = screen.ChildrenOfType<AnalyseDeckButton>().Single().ScreenSpaceDrawQuad;
                var search = screen.ChildrenOfType<CassetteSearchBar>().Single().ScreenSpaceDrawQuad;
                var cassettes = screen.ChildrenOfType<CassetteButton>().Select(c => c.ScreenSpaceDrawQuad).ToList();

                float columnTop = cassettes.Min(q => q.TopLeft.Y);
                float columnBottom = cassettes.Max(q => q.BottomLeft.Y);

                float above = columnTop - panel.BottomLeft.Y;
                float below = search.TopLeft.Y - columnBottom;

                return above > 0 && below > 0 && Math.Abs(above - below) < 2;
            });
        }
    }
}
