using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OsuClient.Game.Beatmaps;

namespace OsuClient.Tests.Beatmaps
{
    /// <summary>
    /// Importer tests. Archives are built in memory with
    /// <see cref="ZipArchive"/>, so no fixture files are checked in and the
    /// tests stay deterministic. The few that need a real directory use a
    /// per-test temp folder and clean it up.
    /// </summary>
    [TestFixture]
    public class OszImporterTests
    {
        private string tempDirectory = null!;

        [SetUp]
        public void SetUp()
        {
            tempDirectory = Path.Combine(Path.GetTempPath(), "OsuClientTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(tempDirectory))
                    Directory.Delete(tempDirectory, true);
            }
            catch (IOException)
            {
                // A locked file shouldn't fail an otherwise-passing test.
            }
        }

        /// <summary>Builds an .osz-shaped zip in memory.</summary>
        private static MemoryStream BuildOsz(IEnumerable<(string name, string content)> textEntries,
                                             IEnumerable<(string name, byte[] bytes)>? binaryEntries = null)
        {
            var stream = new MemoryStream();

            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
            {
                foreach (var (name, content) in textEntries)
                {
                    var entry = archive.CreateEntry(name);
                    using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                    writer.Write(content);
                }

                foreach (var (name, bytes) in binaryEntries ?? Enumerable.Empty<(string, byte[])>())
                {
                    var entry = archive.CreateEntry(name);
                    using var entryStream = entry.Open();
                    entryStream.Write(bytes, 0, bytes.Length);
                }
            }

            stream.Position = 0;
            return stream;
        }

        private static MemoryStream BuildTypicalOsz()
        {
            string easy = TestBeatmapFixtures.BackendGenerated.Replace("Version:Hard", "Version:Easy");

            return BuildOsz(
                new[]
                {
                    ("Test Artist - Test Song (osu-dsp-generator) [Hard].osu", TestBeatmapFixtures.BackendGenerated),
                    ("Test Artist - Test Song (osu-dsp-generator) [Easy].osu", easy),
                },
                new[] { ("audio.mp3", new byte[] { 0xFF, 0xFB, 0x90, 0x00 }) });
        }

        // ------------------------------------------------------------------
        // Reading from a stream
        // ------------------------------------------------------------------

        [Test]
        public void EveryDifficultyInTheArchiveIsDecoded()
        {
            using var osz = BuildTypicalOsz();

            var set = OszImporter.ReadSet(osz, "Test Artist - Test Song");

            Assert.Multiple(() =>
            {
                Assert.That(set.Beatmaps, Has.Count.EqualTo(2));
                Assert.That(set.Name, Is.EqualTo("Test Artist - Test Song"));
                Assert.That(set.Beatmaps.All(b => b.HitObjects.Count == 6), Is.True);
            });
        }

        [Test]
        public void AudioTrackIsLocated()
        {
            using var osz = BuildTypicalOsz();

            var set = OszImporter.ReadSet(osz);

            Assert.Multiple(() =>
            {
                Assert.That(set.AudioFilename, Is.EqualTo("audio.mp3"));
                Assert.That(set.HasAudio, Is.True);
                Assert.That(set.Files, Does.Contain("audio.mp3"));

                // Reading from a stream never touches the disk.
                Assert.That(set.AudioPath, Is.Null);
            });
        }

        [Test]
        public void DifficultyOrderIsDeterministic()
        {
            // Ordinal filename sort: "[Easy]" sorts before "[Hard]".
            using var first = BuildTypicalOsz();
            using var second = BuildTypicalOsz();

            var a = OszImporter.ReadSet(first);
            var b = OszImporter.ReadSet(second);

            Assert.Multiple(() =>
            {
                Assert.That(a.Beatmaps.Select(m => m.Metadata.Version),
                    Is.EqualTo(new[] { "Easy", "Hard" }));
                Assert.That(b.Beatmaps.Select(m => m.Metadata.Version),
                    Is.EqualTo(a.Beatmaps.Select(m => m.Metadata.Version)));
            });
        }

        [Test]
        public void HasAudioIsFalseWhenTheDeclaredTrackIsMissing()
        {
            using var osz = BuildOsz(new[] { ("map.osu", TestBeatmapFixtures.BackendGenerated) });

            var set = OszImporter.ReadSet(osz);

            Assert.Multiple(() =>
            {
                Assert.That(set.AudioFilename, Is.EqualTo("audio.mp3"));
                Assert.That(set.HasAudio, Is.False);
            });
        }

        [Test]
        public void ArchiveWithNoOsuFilesIsRejected()
        {
            using var osz = BuildOsz(
                Array.Empty<(string, string)>(),
                new[] { ("audio.mp3", new byte[] { 1, 2, 3 }) });

            Assert.Throws<OszImportException>(() => OszImporter.ReadSet(osz));
        }

        [Test]
        public void MalformedOsuInsideTheArchiveIsReportedWithItsFilename()
        {
            using var osz = BuildOsz(new[] { ("broken.osu", "this is not a beatmap\n") });

            var exception = Assert.Throws<OszImportException>(() => OszImporter.ReadSet(osz));

            Assert.That(exception!.Message, Does.Contain("broken.osu"));
        }

        [Test]
        public void NonZipInputIsRejected()
        {
            using var notAZip = new MemoryStream(Encoding.UTF8.GetBytes("definitely not a zip file"));

            Assert.Throws<OszImportException>(() => OszImporter.ReadSet(notAZip));
        }

        [Test]
        public void NestedFoldersInsideTheArchiveAreSearched()
        {
            using var osz = BuildOsz(new[]
            {
                ("Test Artist - Test Song/map.osu", TestBeatmapFixtures.BackendGenerated),
            });

            var set = OszImporter.ReadSet(osz);

            Assert.That(set.Beatmaps, Has.Count.EqualTo(1));
        }

        // ------------------------------------------------------------------
        // Extracting to disk
        // ------------------------------------------------------------------

        [Test]
        public void ImportExtractsFilesAndResolvesTheAudioPath()
        {
            string oszPath = Path.Combine(tempDirectory, "Test Artist - Test Song.osz");

            using (var osz = BuildTypicalOsz())
            using (var file = File.Create(oszPath))
                osz.CopyTo(file);

            var set = OszImporter.Import(oszPath, Path.Combine(tempDirectory, "imported"));

            Assert.Multiple(() =>
            {
                Assert.That(set.Name, Is.EqualTo("Test Artist - Test Song"));
                Assert.That(set.Beatmaps, Has.Count.EqualTo(2));
                Assert.That(set.AudioPath, Is.Not.Null);
                Assert.That(File.Exists(set.AudioPath!), Is.True);
                Assert.That(new FileInfo(set.AudioPath!).Length, Is.EqualTo(4));
            });
        }

        [Test]
        public void ImportRejectsArchiveEntriesThatEscapeTheTargetDirectory()
        {
            // Zip-slip: an entry name with ".." would otherwise write outside
            // the destination folder.
            string oszPath = Path.Combine(tempDirectory, "evil.osz");

            using (var osz = BuildOsz(new[]
                   {
                       ("map.osu", TestBeatmapFixtures.BackendGenerated),
                       ("../escaped.txt", "pwned"),
                   }))
            using (var file = File.Create(oszPath))
                osz.CopyTo(file);

            Assert.Throws<OszImportException>(() =>
                OszImporter.Import(oszPath, Path.Combine(tempDirectory, "imported")));
        }

        [Test]
        public void MissingOszFileIsReported()
        {
            Assert.Throws<FileNotFoundException>(() =>
                OszImporter.ReadSet(Path.Combine(tempDirectory, "nope.osz")));
        }

        // ------------------------------------------------------------------
        // Loading an already-extracted folder (the backend's data/output shape)
        // ------------------------------------------------------------------

        [Test]
        public void ExtractedBeatmapFolderLoadsDirectly()
        {
            string folder = Path.Combine(tempDirectory, "Test Artist - Test Song");
            Directory.CreateDirectory(folder);

            File.WriteAllText(Path.Combine(folder, "Test Artist - Test Song (osu-dsp-generator) [Hard].osu"),
                TestBeatmapFixtures.BackendGenerated);
            File.WriteAllBytes(Path.Combine(folder, "audio.mp3"), new byte[] { 0xFF, 0xFB });

            var set = OszImporter.LoadFromDirectory(folder);

            Assert.Multiple(() =>
            {
                Assert.That(set.Name, Is.EqualTo("Test Artist - Test Song"));
                Assert.That(set.Beatmaps, Has.Count.EqualTo(1));
                Assert.That(set.Beatmaps[0].Metadata.Version, Is.EqualTo("Hard"));
                Assert.That(set.HasAudio, Is.True);
                Assert.That(set.AudioPath, Is.Not.Null);
            });
        }

        [Test]
        public void FolderWithNoOsuFilesIsRejected()
        {
            Assert.Throws<OszImportException>(() => OszImporter.LoadFromDirectory(tempDirectory));
        }

        [Test]
        public void MissingFolderIsReported()
        {
            Assert.Throws<DirectoryNotFoundException>(() =>
                OszImporter.LoadFromDirectory(Path.Combine(tempDirectory, "nope")));
        }
    }
}
