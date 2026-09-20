using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using OsuClient.Game.Beatmaps;
using OsuClient.Tests.Beatmaps;
using OsuClient.Game.Screens.MainMenu;

namespace OsuClient.Tests.MainMenu
{
    /// <summary>
    /// Which song the menu picks to play behind itself.
    ///
    /// Only the selection rule is covered here — loading the track needs an
    /// audio device and a game host, which is what the visual scenes are for.
    /// The rule itself matters on its own: an entry that failed to load, or
    /// one whose audio isn't on disk, would leave the menu silently broken
    /// rather than falling through to a song that works.
    /// </summary>
    [TestFixture]
    public class MenuTrackTests
    {
        private string songs = null!;

        [SetUp]
        public void SetUp()
        {
            songs = Path.Combine(Path.GetTempPath(), "OsuClientMenuTrack_" + Guid.NewGuid().ToString("N"));
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

        /// <summary>An entry whose audio really is on disk.</summary>
        private BeatmapLibraryEntry Playable(string name)
        {
            string audio = Path.Combine(songs, name + ".mp3");
            File.WriteAllBytes(audio, new byte[] { 0xFF, 0xFB });

            return new BeatmapLibraryEntry
            {
                Path = Path.Combine(songs, name),
                Set = new BeatmapSet
                {
                    Name = name,
                    AudioPath = audio,
                    Beatmaps = new List<Beatmap> { BeatmapDecoder.Decode(TestBeatmapFixtures.BackendGenerated) },
                },
            };
        }

        private static BeatmapLibraryEntry Broken() =>
            new BeatmapLibraryEntry { Path = "/songs/broken.osz", Error = "not a zip" };

        /// <summary>A set read straight out of an .osz never resolves an audio path.</summary>
        private static BeatmapLibraryEntry WithoutAudioPath() =>
            new BeatmapLibraryEntry
            {
                Path = "/songs/packed.osz",
                Set = new BeatmapSet
                {
                    Name = "packed",
                    Beatmaps = new List<Beatmap> { BeatmapDecoder.Decode(TestBeatmapFixtures.BackendGenerated) },
                },
            };

        private BeatmapLibraryEntry WithMissingAudioFile()
        {
            return new BeatmapLibraryEntry
            {
                Path = Path.Combine(songs, "deleted"),
                Set = new BeatmapSet
                {
                    Name = "deleted",
                    AudioPath = Path.Combine(songs, "deleted.mp3"),
                    Beatmaps = new List<Beatmap> { BeatmapDecoder.Decode(TestBeatmapFixtures.BackendGenerated) },
                },
            };
        }

        [Test]
        public void PicksOnlyFromPlayableEntries()
        {
            var playable = Playable("good");

            var library = new[] { Broken(), WithoutAudioPath(), WithMissingAudioFile(), playable };

            // Every seed must land on the one entry that can actually play.
            for (int seed = 0; seed < 20; seed++)
                Assert.That(MenuTrack.ChooseSong(library, new Random(seed)), Is.SameAs(playable));
        }

        [Test]
        public void ReturnsNullWhenNothingIsPlayable()
        {
            var library = new[] { Broken(), WithoutAudioPath(), WithMissingAudioFile() };

            Assert.That(MenuTrack.ChooseSong(library, new Random(7)), Is.Null);
        }

        [Test]
        public void ReturnsNullForAnEmptyLibrary()
        {
            Assert.That(MenuTrack.ChooseSong(Array.Empty<BeatmapLibraryEntry>(), new Random(7)), Is.Null);
        }

        [Test]
        public void SameSeedPicksTheSameSong()
        {
            var library = new[] { Playable("a"), Playable("b"), Playable("c"), Playable("d") };

            Assert.That(MenuTrack.ChooseSong(library, new Random(99)),
                        Is.SameAs(MenuTrack.ChooseSong(library, new Random(99))));
        }

        [Test]
        public void DifferentSeedsReachEverySong()
        {
            var library = new[] { Playable("a"), Playable("b"), Playable("c") };

            var chosen = new HashSet<string?>();

            for (int seed = 0; seed < 50; seed++)
                chosen.Add(MenuTrack.ChooseSong(library, new Random(seed))?.Set?.Name);

            Assert.That(chosen, Is.EquivalentTo(new[] { "a", "b", "c" }));
        }
    }
}
