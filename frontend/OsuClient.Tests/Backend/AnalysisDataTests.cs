using System.IO;
using System.Linq;
using NUnit.Framework;
using OsuClient.Game.Backend;

namespace OsuClient.Tests.Backend
{
    /// <summary>
    /// Reading the backend's <c>analysis.json</c> (FRONTEND_PLAN.md Phase 7).
    ///
    /// This is a contract between two languages that never call each other, so
    /// the tests that matter are the ones about the shape of the document —
    /// the field names Python writes, and what happens when the file is
    /// missing, truncated or from a version this build doesn't know. Every one
    /// of those has to come out as "no visualization, go play the map",
    /// because the beatmap is already written and correct by the time this is
    /// read.
    /// </summary>
    [TestFixture]
    public class AnalysisDataTests
    {
        /// <summary>A minimal document in exactly the shape analysis_export.py writes.</summary>
        private const string sample_json = @"{
            ""version"": 1,
            ""track"": { ""name"": ""bad_apple"", ""duration"": 219.15, ""sampleRate"": 22050 },
            ""onsetSource"": ""Expert"",
            ""onsets"": [
                { ""time"": 1.416, ""strength"": 0.626, ""band"": ""low"" },
                { ""time"": 1.834, ""strength"": 0.682, ""band"": ""high"" }
            ],
            ""beatGrid"": {
                ""bpm"": 137.85, ""offset"": 0.42, ""confidence"": 0.73,
                ""beatTimes"": [0.42, 0.855, 1.29]
            },
            ""hitObjects"": {
                ""Easy"": [ { ""kind"": ""circle"", ""time"": 1.375, ""x"": 230.2, ""y"": 153.6,
                              ""strength"": 0.626, ""band"": ""low"", ""snap"": ""1/1"" } ],
                ""Expert"": [
                    { ""kind"": ""slider"", ""time"": 1.375, ""x"": 100.0, ""y"": 120.0,
                      ""strength"": 0.5, ""band"": ""mid"", ""snap"": ""1/2"", ""endTime"": 1.811 },
                    { ""kind"": ""spinner"", ""time"": 8.0, ""x"": 256.0, ""y"": 192.0,
                      ""strength"": 0.9, ""band"": ""low"", ""snap"": ""1/1"", ""endTime"": 10.0 }
                ]
            }
        }";

        [Test]
        public void TestParsesEveryStageTheRevealAnimates()
        {
            var data = AnalysisData.Parse(sample_json);

            Assert.That(data, Is.Not.Null);
            Assert.That(data!.Track.Name, Is.EqualTo("bad_apple"));
            Assert.That(data.Track.Duration, Is.EqualTo(219.15).Within(0.001));
            Assert.That(data.Onsets, Has.Count.EqualTo(2));
            Assert.That(data.BeatGrid.Bpm, Is.EqualTo(137.85).Within(0.001));
            Assert.That(data.BeatGrid.BeatTimes, Has.Count.EqualTo(3));
            Assert.That(data.HitObjects, Has.Count.EqualTo(2));
        }

        [Test]
        public void TestOnsetsKeepTheirStrengthAndBand()
        {
            var data = AnalysisData.Parse(sample_json)!;

            Assert.That(data.Onsets[0].Strength, Is.EqualTo(0.626).Within(0.001));
            Assert.That(data.Onsets[0].Band, Is.EqualTo("low"));
            Assert.That(data.Onsets[1].Band, Is.EqualTo("high"));
        }

        [Test]
        public void TestObjectsKeepTheProvenanceTheOsuFileDrops()
        {
            var slider = AnalysisData.Parse(sample_json)!.ObjectsFor("Expert")[0];

            Assert.That(slider.Kind, Is.EqualTo("slider"));
            Assert.That(slider.Snap, Is.EqualTo("1/2"));
            Assert.That(slider.Band, Is.EqualTo("mid"));
            Assert.That(slider.EndTime, Is.Not.Null);
            Assert.That(slider.Duration, Is.EqualTo(0.436).Within(0.001));
        }

        [Test]
        public void TestACircleHasNoDuration()
        {
            var circle = AnalysisData.Parse(sample_json)!.ObjectsFor("Easy")[0];

            Assert.That(circle.EndTime, Is.Null);
            Assert.That(circle.Duration, Is.Zero);
        }

        [Test]
        public void TestPreferredTierIsTheOneTheOnsetsCameFrom()
        {
            // The onsets on screen in stage 2 and the objects in stage 4
            // should be the same analysis, not two different sensitivities.
            Assert.That(AnalysisData.Parse(sample_json)!.PreferredTier, Is.EqualTo("Expert"));
        }

        [Test]
        public void TestPreferredTierFallsBackToTheFullestWhenTheSourceIsMissing()
        {
            string json = sample_json.Replace(@"""onsetSource"": ""Expert""", @"""onsetSource"": ""Nonexistent""");

            Assert.That(AnalysisData.Parse(json)!.PreferredTier, Is.EqualTo("Expert"));
        }

        [Test]
        public void TestUnknownTierReadsAsEmptyRatherThanThrowing()
        {
            Assert.That(AnalysisData.Parse(sample_json)!.ObjectsFor("Lunatic"), Is.Empty);
        }

        [Test]
        public void TestAFutureVersionIsRefusedRatherThanHalfRead()
        {
            string json = sample_json.Replace(@"""version"": 1", @"""version"": 2");

            Assert.That(AnalysisData.Parse(json), Is.Null);
        }

        [Test]
        public void TestTruncatedJsonIsRefusedRatherThanThrowing()
        {
            // Exactly what a run interrupted mid-write leaves behind.
            Assert.That(AnalysisData.Parse(sample_json[..(sample_json.Length / 2)]), Is.Null);
        }

        [Test]
        public void TestMissingFileReadsAsNoAnalysis()
        {
            Assert.That(AnalysisData.Load(Path.Combine(Path.GetTempPath(), "not-there-" + Path.GetRandomFileName())),
                Is.Null);
            Assert.That(AnalysisData.LoadFromFolder(null), Is.Null);
        }

        [Test]
        public void TestAnEmptyAnalysisIsNotWorthAnimating()
        {
            string json = @"{ ""version"": 1, ""track"": { ""name"": ""silence"", ""duration"": 1, ""sampleRate"": 22050 },
                              ""onsets"": [], ""beatGrid"": { ""bpm"": 120, ""offset"": 0, ""confidence"": 0, ""beatTimes"": [] },
                              ""hitObjects"": {} }";

            var data = AnalysisData.Parse(json);

            Assert.That(data, Is.Not.Null);
            Assert.That(data!.HasContent, Is.False);
        }

        [Test]
        public void TestRoundTripsAFileFromDisk()
        {
            string folder = Path.Combine(Path.GetTempPath(), "osuclient-analysis-" + Path.GetRandomFileName());
            Directory.CreateDirectory(folder);

            File.WriteAllText(Path.Combine(folder, AnalysisData.FileName), sample_json);

            var data = AnalysisData.LoadFromFolder(folder);

            Assert.That(data, Is.Not.Null);
            Assert.That(data!.HasContent, Is.True);

            // No spectrogram written, so the reveal has to cope without one.
            Assert.That(AnalysisData.FindSpectrogram(folder), Is.Null);

            File.WriteAllBytes(Path.Combine(folder, AnalysisData.SpectrogramFileName), new byte[] { 1, 2, 3 });

            Assert.That(AnalysisData.FindSpectrogram(folder), Is.Not.Null);

            Directory.Delete(folder, true);
        }

        [Test]
        public void TestMatchesTheRealBackendsOutputWhenOneIsPresent()
        {
            // The strongest check available without running Python here: if a
            // real generated set is sitting in data/output, the parser has to
            // read the actual file the backend wrote, not just the sample
            // above. Skipped on a clean checkout.
            string? root = OsuClient.Game.Beatmaps.BeatmapLibrary.FindRepositoryRoot(TestContext.CurrentContext.TestDirectory);

            if (root == null)
                Assert.Ignore("not running from inside the repository");

            string output = Path.Combine(root!, "data", "output");

            if (!Directory.Exists(output))
                Assert.Ignore("no data/output to check against");

            string? real = Directory.EnumerateFiles(output, AnalysisData.FileName, SearchOption.AllDirectories)
                                    .FirstOrDefault();

            if (real == null)
                Assert.Ignore("no generated analysis.json present");

            var data = AnalysisData.Load(real);

            Assert.That(data, Is.Not.Null, $"failed to parse the backend's own {real}");
            Assert.That(data!.HasContent, Is.True);
            Assert.That(data.BeatGrid.Bpm, Is.GreaterThan(0));
            Assert.That(data.Track.Duration, Is.GreaterThan(0));
        }
    }
}
