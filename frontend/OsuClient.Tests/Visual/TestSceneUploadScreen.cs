using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Screens;
using osu.Framework.Testing;
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

        /// <summary>A second of real tone, so the deck has a "menu song" that actually loads.</summary>
        private static string toneFile()
        {
            string path = Path.Combine(Path.GetTempPath(), "osuclient-deck-" + Path.GetRandomFileName() + ".wav");
            const int rate = 44100;

            using var writer = new BinaryWriter(File.Create(path));

            writer.Write("RIFF"u8.ToArray());
            writer.Write(36 + rate * 2);
            writer.Write("WAVEfmt "u8.ToArray());
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(rate);
            writer.Write(rate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write("data"u8.ToArray());
            writer.Write(rate * 2);

            for (int i = 0; i < rate; i++)
                writer.Write((short)(System.Math.Sin(2 * System.Math.PI * 440 * i / rate) * 8000));

            return path;
        }

        [Test]
        public void TestTheMenuSongPlaysOnUnderTheDeckAndStopsForGeneration()
        {
            TapeDeckMusic music = null!;

            AddStep("push upload screen with the menu's song", () =>
            {
                Child = stack = new ScreenStack { RelativeSizeAxes = Axes.Both };
                stack.Push(upload = new UploadScreen(null, toneFile(), 0));
            });

            AddUntilStep("loaded", () => upload.IsLoaded);
            AddStep("find the music", () => music = upload.ChildrenOfType<TapeDeckMusic>().Single());

            AddUntilStep("playing", () => music.IsPlaying);
            AddAssert("slowed, as a remix", () => music.Track!.Frequency.Value == TapeDeckMusic.Rate);
            AddUntilStep("fades up to its quiet level", () => music.Track!.Volume.Value > TapeDeckMusic.Volume * 0.95);

            // Anything pushed over the deck — in practice the generation
            // screen, which plays the new map's own audio — silences it.
            AddStep("push a screen over the deck", () => upload.Push(new Screen()));
            AddUntilStep("stopped", () => !music.IsPlaying);

            AddStep("come back", () => stack.CurrentScreen.Exit());
            AddUntilStep("playing again", () => music.IsPlaying);
        }

        [Test]
        public void TestCoverArtGoesInTheCaseAndCanBeCleared()
        {
            string image = Path.Combine(Path.GetTempPath(), "osuclient-cover-" + Path.GetRandomFileName() + ".png");

            using (var picture = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(64, 64))
                SixLabors.ImageSharp.ImageExtensions.SaveAsPng(picture, image);

            pushUpload();

            CoverArtCard card = null!;
            AddStep("find the case", () => card = upload.ChildrenOfType<CoverArtCard>().Single());
            AddAssert("empty to begin with", () => !card.HasImage && upload.SelectedCoverPath == null);

            AddStep("choose cover art", () => upload.ChooseCover(image));
            AddAssert("queued", () => upload.SelectedCoverPath == image);
            AddAssert("in the case", () => card.HasImage);

            AddStep("press clear", () =>
                card.ChildrenOfType<OsuClient.Game.Graphics.RetroText>().Single(t => t.Text == "CLEAR").Parent!.TriggerClick());
            AddAssert("gone again", () => !card.HasImage && upload.SelectedCoverPath == null);
        }

        [Test]
        public void TestANonImageIsRefusedAsCoverArt()
        {
            pushUpload();

            AddStep("offer a text file as cover", () => upload.ChooseCover(textFile));
            AddAssert("nothing queued", () => upload.SelectedCoverPath == null);
            AddAssert("said why", () => upload.StatusMessage.Contains("cover art"));
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
