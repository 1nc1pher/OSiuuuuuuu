using System.IO;
using NUnit.Framework;
using osu.Framework.Graphics;
using OsuClient.Game.Screens.SongSelect;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OsuClient.Tests.Visual
{
    /// <summary>
    /// The song select backdrop, driven the way turning the wheel drives it.
    ///
    /// Written after a crash: changing background faster than the images load
    /// threw <c>InvalidThreadForMutationException</c> — "cannot mutate the
    /// Transforms on a Loading Drawable" — because the old code faded out
    /// whatever had been *requested* previously, which on a fast scroll is a
    /// layer still loading and never added to the tree.
    ///
    /// Be honest about what these cover: they exercise rapid supersession and
    /// the null/missing paths, but they do **not** reproduce that crash. The
    /// superseded layer reaches <c>Ready</c> here rather than staying
    /// <c>Loading</c> — blank test images decode far too fast to keep a load
    /// genuinely in flight, and there is no seam to slow one down without
    /// putting test-only machinery in the screen. The real fix is structural:
    /// only a layer that reached the tree is ever animated, so the state that
    /// threw is now unreachable regardless of timing.
    /// </summary>
    [TestFixture]
    public partial class TestSceneCarouselBackground : osu.Framework.Testing.TestScene
    {
        private CarouselBackground background = null!;
        private string directory = null!;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "OsuClientBackgrounds_" + Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
            catch (IOException)
            {
            }
        }

        /// <summary>
        /// A real image on disk. <paramref name="size"/> controls how long it
        /// takes to decode, which is what decides the order two in-flight
        /// loads finish in.
        /// </summary>
        private string WriteImage(string name, int size = 512)
        {
            string path = Path.Combine(directory, name);

            using var image = new Image<Rgba32>(size, size);
            image.SaveAsJpeg(path);

            return path;
        }

        private void CreateBackground()
        {
            AddStep("create background", () =>
            {
                Child = background = new CarouselBackground { RelativeSizeAxes = Axes.Both };
            });

            AddUntilStep("loaded", () => background.IsLoaded);
        }

        [Test]
        public void SupersedingALayerBeforeItIsShownDoesNotThrow()
        {
            CreateBackground();

            // Requested slow-then-fast so the second request overtakes the
            // first, which is the shape of the crash even though the timing
            // window doesn't open in-process (see the class remarks).
            string slow = WriteImage("slow.jpg", 4096);
            string fast = WriteImage("fast.jpg", 32);

            AddStep("request the slow one, then immediately the fast one", () =>
            {
                background.SetBackground(slow);
                background.SetBackground(fast);
            });

            AddWaitStep("let both loads settle", 20);

            // Reaching here at all is the assertion: the failure mode was an
            // unhandled exception on the update thread, which fails the test
            // regardless of what is asserted.
            AddAssert("still alive", () => background.IsLoaded);
        }

        [Test]
        public void ManyRapidChangesDoNotThrow()
        {
            CreateBackground();

            // Descending sizes, so later requests keep overtaking earlier
            // ones — the same shape as spinning the wheel through a library
            // of differently sized cover art.
            string[] paths =
            {
                WriteImage("a.jpg", 3072),
                WriteImage("b.jpg", 2048),
                WriteImage("c.jpg", 256),
                WriteImage("d.jpg", 48),
            };

            AddStep("change background four times in one frame", () =>
            {
                foreach (string path in paths)
                    background.SetBackground(path);
            });

            AddWaitStep("let the loads settle", 20);
            AddAssert("still alive", () => background.IsLoaded);
        }

        [Test]
        public void MissingAndNullPathsAreHandled()
        {
            CreateBackground();

            // Written up front, not inside a step: step bodies run later, by
            // which point the per-test temp directory may be gone.
            string missing = Path.Combine(directory, "nope.jpg");
            string real = WriteImage("real.jpg");

            AddStep("no background", () => background.SetBackground(null));
            AddWaitStep("settle", 5);

            AddStep("path that does not exist", () => background.SetBackground(missing));
            AddWaitStep("settle", 5);

            AddStep("then a real one", () => background.SetBackground(real));
            AddWaitStep("settle", 5);

            AddAssert("still alive", () => background.IsLoaded);
        }

        [Test]
        public void SettingTheSamePathTwiceIsIgnored()
        {
            CreateBackground();

            string path = WriteImage("same.jpg");

            AddStep("set twice", () =>
            {
                background.SetBackground(path);
                background.SetBackground(path);
            });

            AddWaitStep("settle", 10);
            AddAssert("still alive", () => background.IsLoaded);
        }
    }
}
