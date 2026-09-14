using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OsuClient.Game.Backend;

namespace OsuClient.Tests.Backend
{
    /// <summary>
    /// The parts of invoking the backend that can silently go wrong — mangling
    /// a path, dropping a flag, missing the result — without Python running.
    /// </summary>
    [TestFixture]
    public class BackendRunnerTests
    {
        private string root = null!;
        private BackendPaths paths = null!;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "osuclient-runner-" + Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(Path.Combine(root, "src"));
            File.WriteAllText(Path.Combine(root, "src", "main.py"), "# fake");

            string binary = OperatingSystem.IsWindows()
                ? Path.Combine(root, ".venv", "Scripts")
                : Path.Combine(root, ".venv", "bin");

            Directory.CreateDirectory(binary);
            File.WriteAllText(Path.Combine(binary, OperatingSystem.IsWindows() ? "python.exe" : "python"), string.Empty);

            paths = BackendPaths.Locate(root, out _)!;
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }

        private static GenerationRequest request(string audio = "C:\\songs\\my song.mp3",
                                                 string artist = "",
                                                 string title = "",
                                                 params string[] difficulties) =>
            new GenerationRequest
            {
                AudioPath = audio,
                Artist = artist,
                Title = title,
                Difficulties = difficulties,
            };

        [Test]
        public void ScriptAndAudioPathComeFirstAndUnquoted()
        {
            var arguments = BackendRunner.BuildArguments(paths, request());

            Assert.Multiple(() =>
            {
                Assert.That(arguments[0], Is.EqualTo(paths.MainScript));

                // Each argument is passed through ProcessStartInfo.ArgumentList,
                // which does its own quoting — a path with spaces must stay one
                // unquoted element here, not get pre-wrapped or split.
                Assert.That(arguments[1], Is.EqualTo("C:\\songs\\my song.mp3"));
            });
        }

        [Test]
        public void MetadataFlagsAreOmittedWhenBlank()
        {
            var arguments = BackendRunner.BuildArguments(paths, request());

            Assert.Multiple(() =>
            {
                Assert.That(arguments, Does.Not.Contain("--artist"));
                Assert.That(arguments, Does.Not.Contain("--title"));
            });
        }

        [Test]
        public void MetadataFlagsArePassedAndTrimmedWhenSet()
        {
            var arguments = BackendRunner.BuildArguments(paths, request(artist: "  Ado  ", title: " Show "));

            Assert.Multiple(() =>
            {
                Assert.That(arguments[arguments.ToList().IndexOf("--artist") + 1], Is.EqualTo("Ado"));
                Assert.That(arguments[arguments.ToList().IndexOf("--title") + 1], Is.EqualTo("Show"));
            });
        }

        [Test]
        public void WhitespaceOnlyMetadataCountsAsUnset()
        {
            var arguments = BackendRunner.BuildArguments(paths, request(artist: "   "));

            Assert.That(arguments, Does.Not.Contain("--artist"));
        }

        [Test]
        public void DifficultiesAreJoinedWithCommas()
        {
            var arguments = BackendRunner.BuildArguments(paths, request(difficulties: new[] { "Easy", "Hard" }));

            Assert.That(arguments[arguments.ToList().IndexOf("--difficulties") + 1], Is.EqualTo("Easy,Hard"));
        }

        [Test]
        public void NoDifficultiesMeansTheFlagIsLeftOffEntirely()
        {
            // main.py's own default is every tier — passing an empty
            // --difficulties would ask for none instead of all.
            var arguments = BackendRunner.BuildArguments(paths, request());

            Assert.That(arguments, Does.Not.Contain("--difficulties"));
        }

        [Test]
        public void ParsesTheBeatmapFolderAndOszOutOfRealOutput()
        {
            const string output = """
                Beatmap folder: D:\osu-dsp-project\data\output\Ado - Show
                  [Easy   ] 128.00 BPM   138 circles    14 sliders    7 spinners
                Importable set: D:\osu-dsp-project\data\output\Ado - Show.osz  (4821 KB)
                Drag the .osz onto osu!(lazer) to import and play-test.
                """;

            var (folder, osz) = BackendRunner.ParseOutput(output);

            Assert.Multiple(() =>
            {
                Assert.That(folder, Is.EqualTo("D:\\osu-dsp-project\\data\\output\\Ado - Show"));

                // The trailing size belongs to the log line, not the path.
                Assert.That(osz, Is.EqualTo("D:\\osu-dsp-project\\data\\output\\Ado - Show.osz"));
            });
        }

        [Test]
        public void ParsingUnrelatedOutputFindsNothingRatherThanGuessing()
        {
            var (folder, osz) = BackendRunner.ParseOutput("Traceback (most recent call last):\n  ImportError: no librosa");

            Assert.Multiple(() =>
            {
                Assert.That(folder, Is.Null);
                Assert.That(osz, Is.Null);
            });
        }

        [TestCase("song.mp3", true)]
        [TestCase("song.WAV", true)]
        [TestCase("song.ogg", true)]
        [TestCase("song.flac", true)]
        [TestCase("song.txt", false)]
        [TestCase("beatmap.osz", false)]
        [TestCase("", false)]
        public void OnlyAudioTheBackendReadsIsAccepted(string name, bool expected)
        {
            Assert.That(BackendRunner.IsSupportedAudioFile(name), Is.EqualTo(expected));
        }
    }
}
