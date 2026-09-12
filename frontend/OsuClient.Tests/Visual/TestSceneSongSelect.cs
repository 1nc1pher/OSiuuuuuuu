using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Screens;
using OsuClient.Game.Screens.MainMenu;
using OsuClient.Game.Screens.SongSelect;
using OsuClient.Tests.Beatmaps;

namespace OsuClient.Tests.Visual
{
    /// <summary>
    /// Screen-level tests: the menu -> song select -> gameplay-placeholder
    /// navigation path, and song select's handling of a missing or broken
    /// songs directory.
    /// </summary>
    [TestFixture]
    public partial class TestSceneSongSelect : osu.Framework.Testing.TestScene
    {
        private ScreenStack stack = null!;
        private string tempDirectory = null!;

        private void CreateStack()
        {
            AddStep("create screen stack", () =>
            {
                tempDirectory = Path.Combine(Path.GetTempPath(), "OsuClientScene_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDirectory);

                Child = stack = new ScreenStack { RelativeSizeAxes = Axes.Both };
            });
        }

        private void CleanUp()
        {
            AddStep("clean up", () =>
            {
                try
                {
                    if (Directory.Exists(tempDirectory))
                        Directory.Delete(tempDirectory, true);
                }
                catch (IOException)
                {
                }
            });
        }

        private void WriteOsz(string setName, params string[] versions)
        {
            string path = Path.Combine(tempDirectory, setName + ".osz");

            using var file = File.Create(path);
            using var archive = new ZipArchive(file, ZipArchiveMode.Create);

            foreach (string version in versions)
            {
                var entry = archive.CreateEntry($"{setName} [{version}].osu");
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write(TestBeatmapFixtures.BackendGenerated.Replace("Version:Hard", $"Version:{version}"));
            }
        }

        [Test]
        public void TestMainMenuPushesSongSelect()
        {
            CreateStack();

            AddStep("push main menu", () => stack.Push(new MainMenuScreen(tempDirectory)));

            // CurrentScreen is set before the screen finishes loading, so wait
            // for the load too — a screen can't push until it is loaded.
            AddUntilStep("menu is current and loaded",
                () => stack.CurrentScreen is MainMenuScreen menu && menu.IsLoaded);

            AddStep("push song select", () =>
                ((MainMenuScreen)stack.CurrentScreen!).Push(new SongSelectScreen(tempDirectory)));

            AddUntilStep("song select is current and loaded",
                () => stack.CurrentScreen is SongSelectScreen select && select.IsLoaded);

            CleanUp();
        }

        [Test]
        public void TestSongSelectLoadsBeatmapsFromDirectory()
        {
            CreateStack();

            AddStep("write beatmaps", () =>
            {
                WriteOsz("Artist - Song A", "Easy", "Hard");
                WriteOsz("Artist - Song B", "Insane");
            });

            AddStep("push song select", () => stack.Push(new SongSelectScreen(tempDirectory)));
            AddUntilStep("song select loaded",
                () => stack.CurrentScreen is SongSelectScreen screen && screen.IsLoaded);

            CleanUp();
        }

        [Test]
        public void TestSongSelectHandlesMissingDirectory()
        {
            CreateStack();

            AddStep("push song select at a missing path", () =>
                stack.Push(new SongSelectScreen(Path.Combine(tempDirectory, "does-not-exist"))));

            AddUntilStep("song select loaded without throwing",
                () => stack.CurrentScreen is SongSelectScreen screen && screen.IsLoaded);

            CleanUp();
        }

        [Test]
        public void TestSongSelectHandlesCorruptBeatmap()
        {
            CreateStack();

            AddStep("write a corrupt .osz", () =>
                File.WriteAllText(Path.Combine(tempDirectory, "broken.osz"), "not a zip"));

            AddStep("push song select", () => stack.Push(new SongSelectScreen(tempDirectory)));

            AddUntilStep("song select loaded without throwing",
                () => stack.CurrentScreen is SongSelectScreen screen && screen.IsLoaded);

            CleanUp();
        }
    }
}
