using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Screens;
using OsuClient.Game.Backend;
using OsuClient.Game.Screens.Generation;

namespace OsuClient.Tests.Visual
{
    /// <summary>
    /// The upload screen's gatekeeping: what it accepts, and when it will let
    /// a generation start.
    ///
    /// This fixture runs from inside the repository (the test assembly builds
    /// into <c>frontend/OsuClient.Tests/bin/</c>), so unlike the pure
    /// <see cref="Tests.Backend.BackendPathsTests"/> against a fake tree, it
    /// exercises the real checkout discovery against the real venv — the part
    /// that can't be faked and still mean anything.
    /// </summary>
    [TestFixture]
    public partial class TestSceneUploadScreen : osu.Framework.Testing.TestScene
    {
        private ScreenStack stack = null!;
        private UploadScreen upload = null!;

        private string audioFile = null!;
        private string textFile = null!;

        [SetUp]
        public void SetUpFiles()
        {
            string directory = Path.Combine(Path.GetTempPath(), "osuclient-upload-" + Path.GetRandomFileName());
            Directory.CreateDirectory(directory);

            audioFile = Path.Combine(directory, "a song with spaces.mp3");
            textFile = Path.Combine(directory, "notes.txt");

            File.WriteAllText(audioFile, string.Empty);
            File.WriteAllText(textFile, string.Empty);
        }

        private void pushUpload()
        {
            AddStep("push upload screen", () =>
            {
                Child = stack = new ScreenStack { RelativeSizeAxes = Axes.Both };
                stack.Push(upload = new UploadScreen());
            });

            AddUntilStep("loaded", () => upload.IsLoaded);
        }

        [Test]
        public void TestFindsTheRealBackendFromInsideTheRepository()
        {
            pushUpload();

            // If this fails, the venv/marker-file walk has drifted from the
            // actual layout — which no amount of fake-directory testing would
            // catch.
            AddAssert("backend located", () => upload.BackendAvailable);
            AddAssert("and it points at a real interpreter", () =>
            {
                var paths = BackendPaths.Locate(BackendPaths.FindRepositoryRoot(), out _);
                return paths != null && File.Exists(paths.PythonExecutable) && File.Exists(paths.MainScript);
            });
        }

        [Test]
        public void TestNothingIsQueuedUntilAFileIsChosen()
        {
            pushUpload();

            AddAssert("no file yet", () => upload.SelectedAudioPath == null);
            AddAssert("generate is not offered", () => !upload.CanGenerate);
        }

        [Test]
        public void TestChoosingAnAudioFileArmsGeneration()
        {
            pushUpload();

            AddStep("choose an audio file", () => upload.ChooseFile(audioFile));

            AddAssert("file queued", () => upload.SelectedAudioPath == audioFile);
            AddAssert("generate is offered", () => upload.CanGenerate);
            AddAssert("no complaint shown", () => upload.StatusMessage.Length == 0);
        }

        [Test]
        public void TestANonAudioFileIsRejectedWithAReason()
        {
            pushUpload();

            AddStep("drop a text file", () => upload.ChooseFile(textFile));

            AddAssert("nothing queued", () => upload.SelectedAudioPath == null);
            AddAssert("still not generatable", () => !upload.CanGenerate);
            AddAssert("said why", () => upload.StatusMessage.Contains("audio file"));
        }

        [Test]
        public void TestAMissingFileIsRejectedRatherThanQueued()
        {
            pushUpload();

            AddStep("choose a path that isn't there",
                () => upload.ChooseFile(Path.Combine(Path.GetTempPath(), "gone-" + Path.GetRandomFileName() + ".mp3")));

            AddAssert("nothing queued", () => upload.SelectedAudioPath == null);
            AddAssert("said why", () => upload.StatusMessage.Length > 0);
        }

        [Test]
        public void TestBrowseHasAPickerToOpen()
        {
            pushUpload();

            // The desktop host's own CreateSystemFileSelector returns null, so
            // this only holds while the native fallback is wired up — which is
            // exactly the regression that left Browse doing nothing.
            AddAssert("browse can open something", () => upload.FilePickerAvailable);
        }

        [Test]
        public void TestEveryTierIsSelectedByDefault()
        {
            pushUpload();

            // Matches main.py's own default of building the whole set.
            AddAssert("all five tiers ticked", () => upload.SelectedDifficulties.SequenceEqual(
                new[] { "Easy", "Normal", "Hard", "Insane", "Expert" }));
        }
    }
}
