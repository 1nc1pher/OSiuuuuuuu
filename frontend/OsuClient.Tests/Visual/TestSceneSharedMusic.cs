using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Screens;
using osu.Framework.Testing;
using OsuClient.Game.Graphics;
using OsuClient.Game.Screens.MainMenu;
using OsuClient.Game.Screens.SongSelect;
using OsuClient.Tests.Beatmaps;

namespace OsuClient.Tests.Visual
{
    /// <summary>
    /// The music outside gameplay is one song at a time, shared by the menu
    /// and song select: the menu plays through the library, and moving
    /// between the two screens never restarts, reloads or swaps what is
    /// playing.
    ///
    /// Run against a library of three generated songs, ten seconds of tone
    /// each, so a song can actually be run to its end.
    /// </summary>
    [TestFixture]
    public partial class TestSceneSharedMusic : TestScene
    {
        private string songs = null!;

        private ScreenStack stack = null!;
        private MainMenuScreen menu = null!;
        private MenuTrack music = null!;

        [SetUpSteps]
        public void SetUpSteps()
        {
            AddStep("write a library", () =>
            {
                songs = Path.Combine(Path.GetTempPath(), "osuclient-shared-music-" + Path.GetRandomFileName());

                foreach (string name in new[] { "Alpha", "Bravo", "Charlie" })
                    writeSong(name);
            });

            AddStep("open the menu with shared music", () =>
            {
                Child = new MusicHost(songs, out stack, out music);
                stack.Push(menu = new MainMenuScreen(songs));
            });

            AddUntilStep("menu playing", () => menu.IsCurrentScreen() && music.IsPlaying);
        }

        [Test]
        public void TestTheMenuMovesOnToAnotherSongAndSaysSo()
        {
            string? first = null;

            AddStep("run the song to its end", () =>
            {
                first = music.Title;
                music.Track!.Seek(music.Track.Length - 50);
            });

            AddUntilStep("a different song plays", () => music.Title != first && music.IsPlaying);

            AddUntilStep("the credit shows it", () =>
                menu.ChildrenOfType<NowPlayingDisplay>().Single()
                    .ChildrenOfType<RetroText>().Any(t => t.Text == music.Title));
        }

        [Test]
        public void TestSongSelectCarriesOnWithTheMenusSong()
        {
            Track? before = null;
            double timeBefore = 0;
            RetroSongSelectScreen songSelect = null!;

            AddStep("go to song select", () =>
            {
                before = music.Track;
                timeBefore = music.CurrentTime;

                stack.CurrentScreen.Push(songSelect = new RetroSongSelectScreen(songs, music.Entry!.Path));
            });

            AddUntilStep("song select showing", () => songSelect.IsCurrentScreen());
            AddWaitStep("let it settle", 3);

            AddAssert("same track, never reloaded", () => ReferenceEquals(music.Track, before));
            AddAssert("still playing", () => music.IsPlaying);

            // Carried on from where the menu was, not restarted at the
            // preview point — which is also where the menu started it, so
            // "ahead of before" is the check that separates the two.
            AddAssert("from where the menu had got to", () => music.CurrentTime >= timeBefore);
        }

        [Test]
        public void TestLeavingSongSelectKeepsItsSongPlayingInTheMenu()
        {
            RetroSongSelectScreen songSelect = null!;
            string? menuSong = null;
            Track? chosen = null;

            AddStep("go to song select", () =>
            {
                menuSong = music.Title;
                stack.CurrentScreen.Push(songSelect = new RetroSongSelectScreen(songs, music.Entry!.Path));
            });

            AddUntilStep("song select showing", () => songSelect.IsCurrentScreen());

            AddStep("turn to another song", () => songSelect.SelectRelative(1));
            AddUntilStep("that song plays", () => music.Title != menuSong && music.IsPlaying);

            AddStep("back to the menu", () =>
            {
                chosen = music.Track;
                songSelect.Exit();
            });

            AddUntilStep("menu showing", () => menu.IsCurrentScreen());
            AddWaitStep("let it settle", 3);

            AddAssert("song select's song still playing", () => ReferenceEquals(music.Track, chosen) && music.IsPlaying);

            AddUntilStep("the credit shows it", () =>
                menu.ChildrenOfType<NowPlayingDisplay>().Single()
                    .ChildrenOfType<RetroText>().Any(t => t.Text == music.Title));
        }

        [Test]
        public void TestTheTapeDeckStillSilencesTheOriginal()
        {
            AddStep("press create", () => menu.Create());
            AddUntilStep("menu song stopped", () => !music.IsPlaying);
        }

        private void writeSong(string name) => WriteSong(songs, name);

        /// <summary>One generated song — ten seconds of tone and a map — in <paramref name="songs"/>.</summary>
        internal static void WriteSong(string songs, string name)
        {
            string folder = Path.Combine(songs, $"Test Artist - {name}");
            Directory.CreateDirectory(folder);

            File.WriteAllText(Path.Combine(folder, $"{name}.osu"),
                TestBeatmapFixtures.BackendGenerated
                                   .Replace("Title:Test Song", $"Title:{name}")
                                   .Replace("AudioFilename:audio.mp3", "AudioFilename:audio.wav"));

            const int rate = 22050;
            const int seconds = 10;

            using var writer = new BinaryWriter(File.Create(Path.Combine(folder, "audio.wav")));

            writer.Write("RIFF"u8.ToArray());
            writer.Write(36 + rate * seconds * 2);
            writer.Write("WAVEfmt "u8.ToArray());
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(rate);
            writer.Write(rate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write("data"u8.ToArray());
            writer.Write(rate * seconds * 2);

            for (int i = 0; i < rate * seconds; i++)
                writer.Write((short)(Math.Sin(2 * Math.PI * 330 * i / rate) * 4000));
        }

        /// <summary>A screen stack with the shared music cached above it, as the game has.</summary>
        private partial class MusicHost : CompositeDrawable
        {
            [Cached]
            private readonly MenuTrack music;

            public MusicHost(string songs, out ScreenStack stack, out MenuTrack music)
            {
                RelativeSizeAxes = Axes.Both;

                music = this.music = new MenuTrack(songs, new Random(1));
                stack = new ScreenStack { RelativeSizeAxes = Axes.Both };

                InternalChildren = new Drawable[] { this.music, stack };
            }
        }
    }
}
