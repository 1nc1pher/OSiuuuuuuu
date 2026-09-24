using System;
using System.IO;
using NUnit.Framework;
using OsuClient.Game.Beatmaps;

namespace OsuClient.Tests.Beatmaps
{
    /// <summary>
    /// Cover art chosen on the tape deck reaches the rest of the client: the
    /// backend names it as the background in <c>[Events]</c>, and a set that
    /// names one shows that image — not whichever image in the folder sorts
    /// first, which for a generated map is the analysis export's spectrogram.
    /// </summary>
    [TestFixture]
    public class CoverArtImportTests
    {
        private const string background_event = "//Background and Video events";

        private string folder = null!;

        [SetUp]
        public void SetUp()
        {
            folder = Path.Combine(Path.GetTempPath(), "osuclient-cover-" + Path.GetRandomFileName());
            Directory.CreateDirectory(folder);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                Directory.Delete(folder, true);
            }
            catch (IOException)
            {
            }
        }

        private static string withBackground(string file) =>
            TestBeatmapFixtures.BackendGenerated.Replace(background_event,
                background_event + "\n0,0,\"" + file + "\",0,0");

        [Test]
        public void TestDecoderReadsTheBackgroundEvent()
        {
            var beatmap = BeatmapDecoder.Decode(withBackground("cover.png"));

            Assert.That(beatmap.General.BackgroundFilename, Is.EqualTo("cover.png"));
        }

        [Test]
        public void TestNoBackgroundEventMeansNoBackground()
        {
            Assert.That(BeatmapDecoder.Decode(TestBeatmapFixtures.BackendGenerated).General.BackgroundFilename, Is.Null);
        }

        [Test]
        public void TestTheNamedCoverWinsOverTheSpectrogram()
        {
            File.WriteAllText(Path.Combine(folder, "map.osu"), withBackground("cover.png"));
            File.WriteAllText(Path.Combine(folder, "cover.png"), "art");

            // Sorts before cover.png, and would win on name alone.
            File.WriteAllText(Path.Combine(folder, "a-spectrogram.png"), "spectrogram");

            var set = OszImporter.LoadFromDirectory(folder);

            Assert.That(Path.GetFileName(set.BackgroundPath), Is.EqualTo("cover.png"));
        }

        [Test]
        public void TestWithoutANamedCoverAnyImageStillServes()
        {
            // Every map generated before cover art existed: its covers are
            // its spectrograms, and they must not disappear.
            File.WriteAllText(Path.Combine(folder, "map.osu"), TestBeatmapFixtures.BackendGenerated);
            File.WriteAllText(Path.Combine(folder, "spectrogram.png"), "spectrogram");

            var set = OszImporter.LoadFromDirectory(folder);

            Assert.That(Path.GetFileName(set.BackgroundPath), Is.EqualTo("spectrogram.png"));
        }

        [Test]
        public void TestANamedCoverThatIsMissingFallsBack()
        {
            File.WriteAllText(Path.Combine(folder, "map.osu"), withBackground("gone.png"));
            File.WriteAllText(Path.Combine(folder, "spectrogram.png"), "spectrogram");

            var set = OszImporter.LoadFromDirectory(folder);

            Assert.That(Path.GetFileName(set.BackgroundPath), Is.EqualTo("spectrogram.png"));
        }
    }
}
