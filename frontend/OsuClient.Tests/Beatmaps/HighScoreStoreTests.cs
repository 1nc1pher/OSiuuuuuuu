using System;
using System.IO;
using NUnit.Framework;
using OsuClient.Game.Beatmaps;
using OsuClient.Game.Scores;
using OsuClient.Game.Screens.Gameplay;
using OsuClient.Game.Screens.Results;

namespace OsuClient.Tests.Beatmaps
{
    /// <summary>
    /// The high score table: what counts as a best, what does not, that it
    /// survives a restart, and that it belongs to a map's content rather
    /// than its name.
    /// </summary>
    [TestFixture]
    public class HighScoreStoreTests
    {
        private string path = null!;

        [SetUp]
        public void SetUp() =>
            path = Path.Combine(Path.GetTempPath(), "osuclient-scores-" + Path.GetRandomFileName(), "highscores.json");

        [TearDown]
        public void TearDown()
        {
            try
            {
                Directory.Delete(Path.GetDirectoryName(path)!, true);
            }
            catch (IOException)
            {
            }
        }

        private static ResultsScreen.Result run(long score, double accuracy = 95, bool failed = false) => new ResultsScreen.Result
        {
            Title = "Artist - Song",
            Difficulty = "Hard",
            Grade = Grade.A,
            Score = score,
            Accuracy = accuracy,
            MaxCombo = 100,
            CountGreat = 90,
            CountOk = 5,
            CountMeh = 3,
            CountMiss = 2,
            Failed = failed,
        };

        [Test]
        public void TestAFirstClearIsABest()
        {
            var store = new HighScoreStore(path);

            Assert.That(store.Submit("map", run(1000)), Is.True);
            Assert.That(store.Get("map")!.Score, Is.EqualTo(1000));
        }

        [Test]
        public void TestOnlyABetterRunReplacesIt()
        {
            var store = new HighScoreStore(path);
            store.Submit("map", run(1000));

            Assert.That(store.Submit("map", run(900)), Is.False, "lower");
            Assert.That(store.Get("map")!.Score, Is.EqualTo(1000));

            Assert.That(store.Submit("map", run(1500)), Is.True, "higher");
            Assert.That(store.Get("map")!.Score, Is.EqualTo(1500));
        }

        [Test]
        public void TestAScoreTieGoesToTheMoreAccurateRun()
        {
            var store = new HighScoreStore(path);
            store.Submit("map", run(1000, accuracy: 90));

            Assert.That(store.Submit("map", run(1000, accuracy: 90)), Is.False, "same run again");
            Assert.That(store.Submit("map", run(1000, accuracy: 96)), Is.True, "same score, cleaner");
        }

        [Test]
        public void TestAFailedRunNeverCounts()
        {
            var store = new HighScoreStore(path);

            Assert.That(store.Submit("map", run(5000, failed: true)), Is.False);
            Assert.That(store.Get("map"), Is.Null);
        }

        [Test]
        public void TestScoresSurviveARestart()
        {
            new HighScoreStore(path).Submit("map", run(1234, accuracy: 97.5));

            var reopened = new HighScoreStore(path);

            Assert.That(reopened.Get("map")!.Score, Is.EqualTo(1234));
            Assert.That(reopened.Get("map")!.Accuracy, Is.EqualTo(97.5));
        }

        [Test]
        public void TestAnUnreadableFileIsAnEmptyTableNotAnError()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "this is not json");

            var store = new HighScoreStore(path);

            Assert.That(store.Count, Is.EqualTo(0));
            Assert.That(store.Submit("map", run(10)), Is.True, "and it can still save");
        }

        [Test]
        public void TestAMapIsKnownByItsContentNotItsName()
        {
            var original = BeatmapDecoder.Decode(TestBeatmapFixtures.BackendGenerated);
            var sameMap = BeatmapDecoder.Decode(TestBeatmapFixtures.BackendGenerated.Replace("\n", "\r\n"));
            var regenerated = BeatmapDecoder.Decode(TestBeatmapFixtures.BackendGenerated.Replace("500,5,0", "520,5,0"));

            Assert.That(original.ContentHash, Is.Not.Empty);
            Assert.That(sameMap.ContentHash, Is.EqualTo(original.ContentHash), "line endings do not make a new map");
            Assert.That(regenerated.ContentHash, Is.Not.EqualTo(original.ContentHash), "a changed map is a new map");
        }
    }
}
