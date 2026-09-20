using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OsuClient.Game.Graphics;

namespace OsuClient.Tests.Graphics
{
    /// <summary>
    /// Menu wallpaper discovery. Uses a per-test temp directory, so results
    /// don't depend on whatever the real data/default wallpapers folder holds
    /// — which on a clean clone is nothing at all, since data/ is gitignored.
    /// </summary>
    [TestFixture]
    public class WallpaperLibraryTests
    {
        private string wallpapers = null!;

        [SetUp]
        public void SetUp()
        {
            wallpapers = Path.Combine(Path.GetTempPath(), "OsuClientWallpapers_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(wallpapers);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(wallpapers))
                    Directory.Delete(wallpapers, true);
            }
            catch (IOException)
            {
            }
        }

        private string Write(string name)
        {
            string path = Path.Combine(wallpapers, name);
            File.WriteAllBytes(path, new byte[] { 0x00 });
            return path;
        }

        [Test]
        public void LoadFindsEveryImageExtension()
        {
            Write("a.jpg");
            Write("b.PNG");
            Write("c.jpeg");
            Write("d.webp");
            Write("e.bmp");

            var found = WallpaperLibrary.Load(wallpapers);

            Assert.That(found.Select(Path.GetFileName), Is.EquivalentTo(new[] { "a.jpg", "b.PNG", "c.jpeg", "d.webp", "e.bmp" }));
        }

        [Test]
        public void LoadIgnoresNonImages()
        {
            Write("keep.png");
            Write("readme.txt");
            Write("credits.md");
            Directory.CreateDirectory(Path.Combine(wallpapers, "subfolder"));

            var found = WallpaperLibrary.Load(wallpapers);

            Assert.That(found.Select(Path.GetFileName), Is.EqualTo(new[] { "keep.png" }));
        }

        [Test]
        public void LoadSortsByName()
        {
            Write("zulu.png");
            Write("alpha.png");
            Write("mike.png");

            var found = WallpaperLibrary.Load(wallpapers);

            Assert.That(found.Select(Path.GetFileName), Is.EqualTo(new[] { "alpha.png", "mike.png", "zulu.png" }));
        }

        [Test]
        public void EmptyDirectoryYieldsNothing()
        {
            Assert.That(WallpaperLibrary.Load(wallpapers), Is.Empty);
        }

        [Test]
        public void MissingDirectoryYieldsNothing()
        {
            Assert.That(WallpaperLibrary.Load(Path.Combine(wallpapers, "nope")), Is.Empty);
            Assert.That(WallpaperLibrary.Load(null), Is.Empty);
            Assert.That(WallpaperLibrary.Load("   "), Is.Empty);
        }

        [Test]
        public void PickRandomIsDeterministicForASeed()
        {
            Write("a.png");
            Write("b.png");
            Write("c.png");

            var found = WallpaperLibrary.Load(wallpapers);

            string? first = WallpaperLibrary.PickRandom(found, new Random(1234));
            string? again = WallpaperLibrary.PickRandom(found, new Random(1234));

            Assert.That(first, Is.Not.Null);
            Assert.That(again, Is.EqualTo(first));
        }

        [Test]
        public void PickRandomReturnsNullWithNothingToPick()
        {
            Assert.That(WallpaperLibrary.PickRandom(Array.Empty<string>(), new Random(1)), Is.Null);
        }

        [Test]
        public void EnvironmentVariableOverridesTheDefaultDirectory()
        {
            string? original = Environment.GetEnvironmentVariable(WallpaperLibrary.DirectoryEnvironmentVariable);

            try
            {
                Environment.SetEnvironmentVariable(WallpaperLibrary.DirectoryEnvironmentVariable, wallpapers);

                Assert.That(WallpaperLibrary.ResolveDefaultDirectory(), Is.EqualTo(wallpapers));
            }
            finally
            {
                Environment.SetEnvironmentVariable(WallpaperLibrary.DirectoryEnvironmentVariable, original);
            }
        }
    }
}
