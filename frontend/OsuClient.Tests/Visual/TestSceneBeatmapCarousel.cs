using System.Collections.Generic;
using NUnit.Framework;
using osu.Framework.Graphics;
using OsuClient.Game.Beatmaps;
using OsuClient.Game.Screens.SongSelect;
using OsuClient.Tests.Beatmaps;

namespace OsuClient.Tests.Visual
{
    /// <summary>
    /// Carousel behaviour driven through its public API with in-memory entries,
    /// so nothing here touches the disk.
    /// </summary>
    [TestFixture]
    public partial class TestSceneBeatmapCarousel : osu.Framework.Testing.TestScene
    {
        private BeatmapCarousel carousel = null!;
        private BeatmapSelection? lastChanged;
        private BeatmapSelection? lastConfirmed;

        private static BeatmapLibraryEntry ValidEntry(params string[] versions)
        {
            var beatmaps = new List<Beatmap>();

            foreach (string version in versions)
            {
                var beatmap = BeatmapDecoder.Decode(
                    TestBeatmapFixtures.BackendGenerated.Replace("Version:Hard", $"Version:{version}"));
                beatmaps.Add(beatmap);
            }

            return new BeatmapLibraryEntry
            {
                Path = "/songs/Test Artist - Test Song.osz",
                Set = new BeatmapSet
                {
                    Name = "Test Artist - Test Song",
                    Beatmaps = beatmaps,
                    Files = new[] { "audio.mp3" },
                    AudioFilename = "audio.mp3",
                },
            };
        }

        private static BeatmapLibraryEntry BrokenEntry() => new BeatmapLibraryEntry
        {
            Path = "/songs/broken.osz",
            Error = "file is not a valid zip archive",
        };

        private void CreateCarousel(IReadOnlyList<BeatmapLibraryEntry> entries)
        {
            AddStep("create carousel", () =>
            {
                lastChanged = null;
                lastConfirmed = null;

                Child = carousel = new BeatmapCarousel
                {
                    RelativeSizeAxes = Axes.Both,
                };

                carousel.SelectionChanged += s => lastChanged = s;
                carousel.SelectionConfirmed += s => lastConfirmed = s;

                carousel.SetEntries(entries, "nothing here");
            });
        }

        [Test]
        public void TestDifficultiesAreListedAndFirstIsSelected()
        {
            CreateCarousel(new[] { ValidEntry("Easy", "Hard", "Expert") });

            AddAssert("a selection exists", () => carousel.Selection != null);
            AddAssert("first difficulty selected",
                () => carousel.Selection!.Beatmap.Metadata.Version == "Easy");
            AddAssert("selection was broadcast", () => lastChanged != null);
        }

        [Test]
        public void TestSelectionMovesWithSelectRelative()
        {
            CreateCarousel(new[] { ValidEntry("Easy", "Hard", "Expert") });

            AddStep("move down", () => carousel.SelectRelative(1));
            AddAssert("second selected", () => carousel.Selection!.Beatmap.Metadata.Version == "Hard");

            AddStep("move down again", () => carousel.SelectRelative(1));
            AddAssert("third selected", () => carousel.Selection!.Beatmap.Metadata.Version == "Expert");

            AddStep("move past the end", () => carousel.SelectRelative(5));
            AddAssert("clamped to last", () => carousel.Selection!.Beatmap.Metadata.Version == "Expert");

            AddStep("move back to start", () => carousel.SelectRelative(-10));
            AddAssert("clamped to first", () => carousel.Selection!.Beatmap.Metadata.Version == "Easy");
        }

        [Test]
        public void TestConfirmSelectionFires()
        {
            CreateCarousel(new[] { ValidEntry("Easy", "Hard") });

            AddStep("confirm", () => carousel.ConfirmSelection());

            AddAssert("confirmed the selected difficulty",
                () => lastConfirmed != null && lastConfirmed.Beatmap.Metadata.Version == "Easy");
        }

        [Test]
        public void TestSelectionCarriesTheOwningEntry()
        {
            var entry = ValidEntry("Easy");
            CreateCarousel(new[] { entry });

            AddAssert("entry matches",
                () => ReferenceEquals(carousel.Selection!.Entry, entry));
        }

        [Test]
        public void TestBrokenEntryIsShownButNotSelectable()
        {
            CreateCarousel(new[] { BrokenEntry() });

            AddAssert("nothing selectable", () => carousel.Selection == null);

            AddStep("try to confirm", () => carousel.ConfirmSelection());
            AddAssert("nothing confirmed", () => lastConfirmed == null);
        }

        [Test]
        public void TestBrokenAndValidEntriesCoexist()
        {
            CreateCarousel(new[] { BrokenEntry(), ValidEntry("Normal") });

            AddAssert("valid difficulty still selected",
                () => carousel.Selection?.Beatmap.Metadata.Version == "Normal");
        }

        [Test]
        public void TestEmptyCarousel()
        {
            CreateCarousel(new List<BeatmapLibraryEntry>());

            AddAssert("no selection", () => carousel.Selection == null);
            AddStep("confirm does nothing", () => carousel.ConfirmSelection());
            AddAssert("nothing confirmed", () => lastConfirmed == null);
        }

        [Test]
        public void TestSetEntriesReplacesPreviousContents()
        {
            CreateCarousel(new[] { ValidEntry("Easy", "Hard") });

            AddStep("replace with one difficulty", () =>
                carousel.SetEntries(new[] { ValidEntry("Insane") }, "nothing here"));

            AddAssert("selection points at the new list",
                () => carousel.Selection?.Beatmap.Metadata.Version == "Insane");

            AddStep("replace with nothing", () =>
                carousel.SetEntries(new List<BeatmapLibraryEntry>(), "nothing here"));

            AddAssert("selection cleared", () => carousel.Selection == null);
        }
    }
}
