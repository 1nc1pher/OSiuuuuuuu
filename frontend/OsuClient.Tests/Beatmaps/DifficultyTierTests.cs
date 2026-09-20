using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OsuClient.Game.Beatmaps;
using OsuClient.Game.Screens.SongSelect;

namespace OsuClient.Tests.Beatmaps
{
    /// <summary>
    /// Difficulty ordering in the cassette column.
    /// </summary>
    [TestFixture]
    public class DifficultyTierTests
    {
        private static Beatmap WithVersion(string version, int objectCount = 100)
        {
            var beatmap = BeatmapDecoder.Decode(
                TestBeatmapFixtures.BackendGenerated.Replace("Version:Hard", $"Version:{version}"));

            // Density is the tie-breaker, so tests that care about it need to
            // be able to set the object count independently of the name.
            while (beatmap.HitObjects.Count > objectCount)
                beatmap.HitObjects.RemoveAt(beatmap.HitObjects.Count - 1);

            return beatmap;
        }

        private static List<string> OrderedVersions(params Beatmap[] beatmaps) =>
            beatmaps.OrderBy(DifficultyTier.SortKey).Select(b => b.Metadata.Version).ToList();

        [Test]
        public void KnownTiersSortByNameNotByObjectCount()
        {
            // The case that motivated this: a Hard with fewer objects than its
            // own Normal must still come after it.
            var order = OrderedVersions(
                WithVersion("Expert", 90),
                WithVersion("Hard", 20),
                WithVersion("Easy", 80),
                WithVersion("Normal", 60));

            Assert.That(order, Is.EqualTo(new[] { "Easy", "Normal", "Hard", "Expert" }));
        }

        [Test]
        public void UnrecognisedNamesSortAfterEveryKnownTier()
        {
            var order = OrderedVersions(
                WithVersion("Duality"),
                WithVersion("Easy"),
                WithVersion("Expert"));

            Assert.That(order, Is.EqualTo(new[] { "Easy", "Expert", "Duality" }));
        }

        [Test]
        public void TierLookupIsCaseInsensitiveAndRejectsUnknowns()
        {
            Assert.Multiple(() =>
            {
                Assert.That(DifficultyTier.RankOf("EASY"), Is.EqualTo(0));
                Assert.That(DifficultyTier.RankOf("insane"), Is.EqualTo(3));
                Assert.That(DifficultyTier.RankOf("Duality"), Is.Null);
                Assert.That(DifficultyTier.RankOf(""), Is.Null);
                Assert.That(DifficultyTier.RankOf(null), Is.Null);
            });
        }
    }
}
