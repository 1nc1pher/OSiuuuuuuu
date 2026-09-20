using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using OsuClient.Game.Beatmaps;
using OsuClient.Game.Screens.SongSelect;
using OsuClient.Tests.Beatmaps;
using osuTK;

namespace OsuClient.Tests.Visual
{
    /// <summary>
    /// The vinyl wheel driven through its public API with in-memory entries.
    ///
    /// The cases that matter here are the ones the wheel's angle maths can
    /// divide by: an empty library and a single song. Both are reachable just
    /// by typing in the search box, so neither is hypothetical.
    /// </summary>
    [TestFixture]
    public partial class TestSceneVinylCarousel : osu.Framework.Testing.TestScene
    {
        private VinylCarousel carousel = null!;
        private BeatmapLibraryEntry? lastChanged;
        private BeatmapLibraryEntry? lastConfirmed;
        private int changeCount;

        /// <summary>
        /// An entry that really is named <paramref name="name"/>: the display
        /// name comes from the beatmap's own metadata, not from the path, so
        /// the fixture's artist and title have to be patched rather than just
        /// the file name.
        /// </summary>
        private static BeatmapLibraryEntry Entry(string name, params string[] versions)
        {
            string[] parts = name.Split(" - ", 2);
            string artist = parts[0];
            string title = parts.Length > 1 ? parts[1] : string.Empty;

            var beatmaps = versions
                           .Select(v => BeatmapDecoder.Decode(
                               TestBeatmapFixtures.BackendGenerated
                                                  .Replace("Artist:Test Artist", $"Artist:{artist}")
                                                  .Replace("Title:Test Song", $"Title:{title}")
                                                  .Replace("Version:Hard", $"Version:{v}")))
                           .ToList();

            return new BeatmapLibraryEntry
            {
                Path = $"/songs/{name}.osz",
                Set = new BeatmapSet { Name = name, Beatmaps = beatmaps },
            };
        }

        private void CreateCarousel(IReadOnlyList<BeatmapLibraryEntry> entries)
        {
            AddStep("create wheel", () =>
            {
                lastChanged = null;
                lastConfirmed = null;
                changeCount = 0;

                Child = new Container
                {
                    // Sized and centred the way song select positions it: a
                    // square the size of the disc.
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(700),
                    Child = carousel = new VinylCarousel(),
                };

                carousel.SelectionChanged += e =>
                {
                    lastChanged = e;
                    changeCount++;
                };

                carousel.SelectionConfirmed += e => lastConfirmed = e;
            });

            AddStep("set entries", () => carousel.SetEntries(entries));
        }

        [Test]
        public void EmptyLibraryDoesNotCrashAndSelectsNothing()
        {
            CreateCarousel(new List<BeatmapLibraryEntry>());

            AddAssert("no selection", () => carousel.Selection == null);
            AddStep("try to turn it", () => carousel.SelectRelative(1));
            AddAssert("still no selection", () => carousel.Selection == null);
            AddStep("try to confirm", () => carousel.ConfirmSelection());
            AddAssert("nothing confirmed", () => lastConfirmed == null);
        }

        [Test]
        public void SingleSongFillsTheWholeDisc()
        {
            CreateCarousel(new[] { Entry("Only - Song", "Easy") });

            AddAssert("selected", () => carousel.Selection?.DisplayName == "Only - Song");

            // One slice means every step lands back on the same song rather
            // than running off the end.
            AddStep("turn", () => carousel.SelectRelative(1));
            AddAssert("still selected", () => carousel.Selection?.DisplayName == "Only - Song");
        }

        [Test]
        public void TurningWrapsAroundInBothDirections()
        {
            CreateCarousel(new[]
            {
                Entry("A - one", "Easy"),
                Entry("B - two", "Easy"),
                Entry("C - three", "Easy"),
            });

            AddAssert("starts on first", () => carousel.Selection?.DisplayName == "A - one");

            AddStep("back one", () => carousel.SelectRelative(-1));
            AddAssert("wrapped to last", () => carousel.Selection?.DisplayName == "C - three");

            AddStep("forward one", () => carousel.SelectRelative(1));
            AddAssert("wrapped to first", () => carousel.Selection?.DisplayName == "A - one");
        }

        [Test]
        public void SelectionSurvivesTheListBeingFiltered()
        {
            var all = new[]
            {
                Entry("A - one", "Easy"),
                Entry("B - two", "Easy"),
                Entry("C - three", "Easy"),
            };

            CreateCarousel(all);

            AddStep("select the second", () => carousel.SelectRelative(1));
            AddAssert("on second", () => carousel.Selection?.DisplayName == "B - two");

            // Narrowing the wheel shouldn't throw away what the player was
            // already looking at, as long as it's still in the list.
            AddStep("filter to two of them", () => carousel.SetEntries(new[] { all[1], all[2] }));
            AddAssert("still on second", () => carousel.Selection?.DisplayName == "B - two");

            AddStep("filter it out", () => carousel.SetEntries(new[] { all[2] }));
            AddAssert("falls back to first remaining",
                () => carousel.Selection?.DisplayName == "C - three");
        }

        [Test]
        public void PreferredSetIsSelectedOnFirstEntriesOnly()
        {
            var entries = new[]
            {
                Entry("aaa - first", "Easy"),
                Entry("mmm - middle", "Easy"),
                Entry("zzz - generated", "Easy"),
            };

            CreateCarousel(new List<BeatmapLibraryEntry>());

            AddStep("set entries preferring the generated one", () =>
                carousel.SetEntries(entries, e => e.DisplayName.Contains("generated")));

            AddAssert("starts on the generated set",
                () => carousel.Selection?.DisplayName.Contains("generated") == true);

            // A later SetEntries call — e.g. the player typing in the search
            // box — has a real previous selection now, so the preference
            // must not override it and drag the highlight back.
            AddStep("re-apply with the same preference", () =>
                carousel.SetEntries(entries, e => e.DisplayName.Contains("generated")));

            AddAssert("stays on the generated set (nothing to preserve away from)",
                () => carousel.Selection?.DisplayName.Contains("generated") == true);

            AddStep("select the first instead", () => carousel.SelectIndex(0));
            AddStep("re-apply the same preference again", () =>
                carousel.SetEntries(entries, e => e.DisplayName.Contains("generated")));

            AddAssert("preference is ignored once something is already selected",
                () => carousel.Selection?.DisplayName.Contains("first") == true);
        }

        [Test]
        public void UnmatchedPreferenceFallsBackToTheFirstSet()
        {
            CreateCarousel(new List<BeatmapLibraryEntry>());

            AddStep("set entries preferring something not in the list", () =>
                carousel.SetEntries(
                    new[] { Entry("aaa - first", "Easy"), Entry("zzz - last", "Easy") },
                    e => e.DisplayName.Contains("deleted")));

            AddAssert("falls back to the first", () => carousel.Selection?.DisplayName.Contains("first") == true);
        }

        [Test]
        public void ConfirmingReportsTheSelectedSong()
        {
            CreateCarousel(new[] { Entry("A - one", "Easy"), Entry("B - two", "Easy") });

            AddStep("turn", () => carousel.SelectRelative(1));
            AddStep("confirm", () => carousel.ConfirmSelection());

            AddAssert("confirmed the selected one",
                () => lastConfirmed?.DisplayName == "B - two");
        }

        [Test]
        public void SelectionChangeIsReportedOnce()
        {
            CreateCarousel(new[] { Entry("A - one", "Easy"), Entry("B - two", "Easy") });

            AddStep("reset count", () => changeCount = 0);
            AddStep("turn", () => carousel.SelectRelative(1));

            AddAssert("one change reported", () => changeCount == 1);
            AddAssert("reported the new song", () => lastChanged?.DisplayName == "B - two");

            // Selecting what's already selected shouldn't churn the
            // background, info panel and cassette column.
            AddStep("select same again", () => carousel.SelectIndex(1));
            AddAssert("no further change", () => changeCount == 1);
        }
    }
}
