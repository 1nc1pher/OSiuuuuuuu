using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OsuClient.Game.Beatmaps;

namespace OsuClient.Tests.Beatmaps
{
    /// <summary>
    /// Songs-directory discovery. Uses a per-test temp directory so results
    /// don't depend on whatever is in the real data/output folder.
    /// </summary>
    [TestFixture]
    public class BeatmapLibraryTests
    {
        private string songs = null!;

        [SetUp]
        public void SetUp()
        {
            songs = Path.Combine(Path.GetTempPath(), "OsuClientLibrary_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(songs);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(songs))
                    Directory.Delete(songs, true);
            }
            catch (IOException)
            {
            }
        }

        /// <summary>Writes a valid .osz holding the given difficulty names.</summary>
        private string WriteOsz(string setName, params string[] versions)
        {
            string path = Path.Combine(songs, setName + ".osz");

            using var file = File.Create(path);
            using var archive = new ZipArchive(file, ZipArchiveMode.Create);

            foreach (string version in versions)
            {
                var entry = archive.CreateEntry($"{setName} [{version}].osu");
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write(TestBeatmapFixtures.BackendGenerated.Replace("Version:Hard", $"Version:{version}"));
            }

            var audio = archive.CreateEntry("audio.mp3");
            using (var audioStream = audio.Open())
                audioStream.Write(new byte[] { 0xFF, 0xFB }, 0, 2);

            return path;
        }

        // ------------------------------------------------------------------
        // Discovery
        // ------------------------------------------------------------------

        [Test]
        public void OszFilesAreDiscovered()
        {
            WriteOsz("Artist A - Song One", "Easy", "Hard");
            WriteOsz("Artist B - Song Two", "Normal");

            var entries = BeatmapLibrary.Load(songs);

            Assert.Multiple(() =>
            {
                Assert.That(entries, Has.Count.EqualTo(2));
                Assert.That(entries.All(e => e.IsValid), Is.True);
            });
        }

        [Test]
        public void MetadataAndDifficultiesAreParsed()
        {
            WriteOsz("Set", "Easy", "Insane");

            var entry = BeatmapLibrary.Load(songs).Single();

            Assert.Multiple(() =>
            {
                Assert.That(entry.DisplayName, Is.EqualTo("Test Artist - Test Song"));
                Assert.That(entry.Difficulties, Has.Count.EqualTo(2));
                Assert.That(entry.Difficulties.Select(d => d.Metadata.Version),
                    Is.EquivalentTo(new[] { "Easy", "Insane" }));
                Assert.That(entry.Difficulties.All(d => d.HitObjects.Count == 6), Is.True);
                Assert.That(entry.Set!.AudioFilename, Is.EqualTo("audio.mp3"));
            });
        }

        [Test]
        public void ExtractedFoldersAreDiscovered()
        {
            string folder = Path.Combine(songs, "Unpacked Set");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "map.osu"), TestBeatmapFixtures.BackendGenerated);

            var entries = BeatmapLibrary.Load(songs);

            Assert.That(entries, Has.Count.EqualTo(1));
            Assert.That(entries[0].IsValid, Is.True);
        }

        [Test]
        public void FolderMatchingAnOszIsNotListedTwice()
        {
            // The backend writes both "<set>.osz" and the "<set>/" folder it
            // was zipped from. Song select should show one entry, not two.
            WriteOsz("Test Artist - Test Song", "Hard");

            string folder = Path.Combine(songs, "Test Artist - Test Song");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "map.osu"), TestBeatmapFixtures.BackendGenerated);

            var entries = BeatmapLibrary.Load(songs);

            Assert.That(entries, Has.Count.EqualTo(1));
            Assert.That(entries[0].Path, Does.EndWith(".osz"));
        }

        [Test]
        public void UnrelatedSubdirectoriesAreIgnored()
        {
            Directory.CreateDirectory(Path.Combine(songs, "not-a-beatmap"));
            File.WriteAllText(Path.Combine(songs, "not-a-beatmap", "readme.txt"), "hello");

            Assert.That(BeatmapLibrary.Load(songs), Is.Empty);
        }

        [Test]
        public void NonBeatmapFilesAreIgnored()
        {
            File.WriteAllText(Path.Combine(songs, "notes.txt"), "hello");
            File.WriteAllBytes(Path.Combine(songs, "song.mp3"), new byte[] { 1, 2, 3 });

            Assert.That(BeatmapLibrary.Load(songs), Is.Empty);
        }

        [Test]
        public void OrderingIsDeterministic()
        {
            WriteOsz("Zebra - Last", "Hard");
            WriteOsz("Alpha - First", "Hard");

            // Both fixtures carry the same metadata, so ordering falls through
            // to the path tiebreak — the point is that it's stable.
            var first = BeatmapLibrary.Load(songs).Select(e => e.Path).ToArray();
            var second = BeatmapLibrary.Load(songs).Select(e => e.Path).ToArray();

            Assert.That(second, Is.EqualTo(first));
            Assert.That(first, Is.Ordered.Using<string>(StringComparer.Ordinal));
        }

        // ------------------------------------------------------------------
        // Invalid input
        // ------------------------------------------------------------------

        [Test]
        public void CorruptOszBecomesAnErrorEntryInsteadOfThrowing()
        {
            File.WriteAllText(Path.Combine(songs, "broken.osz"), "this is not a zip file");

            var entry = BeatmapLibrary.Load(songs).Single();

            Assert.Multiple(() =>
            {
                Assert.That(entry.IsValid, Is.False);
                Assert.That(entry.Error, Is.Not.Null.And.Not.Empty);
                Assert.That(entry.Difficulties, Is.Empty);

                // Falls back to the filename so the panel is still identifiable.
                Assert.That(entry.DisplayName, Is.EqualTo("broken"));
            });
        }

        [Test]
        public void OszWithMalformedOsuBecomesAnErrorEntry()
        {
            string path = Path.Combine(songs, "bad-map.osz");

            using (var file = File.Create(path))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("map.osu");
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write("this is not a beatmap");
            }

            var loaded = BeatmapLibrary.Load(songs).Single();

            Assert.That(loaded.IsValid, Is.False);
            Assert.That(loaded.Error, Does.Contain("map.osu"));
        }

        [Test]
        public void ValidAndInvalidSetsCoexist()
        {
            WriteOsz("Good Set", "Hard");
            File.WriteAllText(Path.Combine(songs, "broken.osz"), "nope");

            var entries = BeatmapLibrary.Load(songs);

            Assert.Multiple(() =>
            {
                Assert.That(entries, Has.Count.EqualTo(2));
                Assert.That(entries.Count(e => e.IsValid), Is.EqualTo(1));
                Assert.That(entries.Count(e => !e.IsValid), Is.EqualTo(1));
            });
        }

        [Test]
        public void MissingDirectoryYieldsAnEmptyListRatherThanThrowing()
        {
            Assert.That(BeatmapLibrary.Load(Path.Combine(songs, "nope")), Is.Empty);
        }

        [Test]
        public void NullOrEmptyDirectoryYieldsAnEmptyList()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BeatmapLibrary.Load(null), Is.Empty);
                Assert.That(BeatmapLibrary.Load(string.Empty), Is.Empty);
                Assert.That(BeatmapLibrary.Load("   "), Is.Empty);
            });
        }

        [Test]
        public void EmptyDirectoryYieldsAnEmptyList()
        {
            Assert.That(BeatmapLibrary.Load(songs), Is.Empty);
        }

        // ------------------------------------------------------------------
        // Default directory resolution
        // ------------------------------------------------------------------

        [Test]
        public void RepositoryRootIsFoundByWalkingUpwards()
        {
            string root = Path.Combine(songs, "repo");
            string nested = Path.Combine(root, "frontend", "OsuClient.Desktop", "bin", "Debug", "net8.0");
            Directory.CreateDirectory(nested);

            File.WriteAllText(Path.Combine(root, "FRONTEND_PLAN.md"), "plan");
            File.WriteAllText(Path.Combine(root, "BACKEND.md"), "backend");

            Assert.That(BeatmapLibrary.FindRepositoryRoot(nested), Is.EqualTo(root));
        }

        [Test]
        public void RepositoryRootIsNullWhenMarkersAreAbsent()
        {
            string nested = Path.Combine(songs, "a", "b", "c");
            Directory.CreateDirectory(nested);

            // songs lives under the system temp directory, which has no markers.
            Assert.That(BeatmapLibrary.FindRepositoryRoot(nested), Is.Null);
        }

        [Test]
        public void PartialMarkerSetDoesNotCountAsTheRepositoryRoot()
        {
            string root = Path.Combine(songs, "repo");
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "FRONTEND_PLAN.md"), "plan only");

            Assert.That(BeatmapLibrary.FindRepositoryRoot(root), Is.Null);
        }

        [Test]
        public void EnvironmentVariableOverridesTheDefaultDirectory()
        {
            string previous = Environment.GetEnvironmentVariable(
                BeatmapLibrary.SongsDirectoryEnvironmentVariable) ?? string.Empty;

            try
            {
                Environment.SetEnvironmentVariable(
                    BeatmapLibrary.SongsDirectoryEnvironmentVariable, songs);

                Assert.That(BeatmapLibrary.ResolveDefaultSongsDirectory(), Is.EqualTo(songs));
            }
            finally
            {
                Environment.SetEnvironmentVariable(
                    BeatmapLibrary.SongsDirectoryEnvironmentVariable,
                    previous.Length == 0 ? null : previous);
            }
        }

        [Test]
        public void DefaultDirectoryPointsAtDataOutputWhenRunningInsideTheRepository()
        {
            // The test binary lives under frontend/OsuClient.Tests/bin/..., so the
            // markers are found by walking up and the default lands on data/output.
            string previous = Environment.GetEnvironmentVariable(
                BeatmapLibrary.SongsDirectoryEnvironmentVariable) ?? string.Empty;

            try
            {
                Environment.SetEnvironmentVariable(
                    BeatmapLibrary.SongsDirectoryEnvironmentVariable, null);

                string? resolved = BeatmapLibrary.ResolveDefaultSongsDirectory();

                Assert.That(resolved, Is.Not.Null);
                Assert.That(resolved, Does.EndWith(Path.Combine("data", "output")));
            }
            finally
            {
                Environment.SetEnvironmentVariable(
                    BeatmapLibrary.SongsDirectoryEnvironmentVariable,
                    previous.Length == 0 ? null : previous);
            }
        }
    }
}
