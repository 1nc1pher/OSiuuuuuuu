using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OsuClient.Game.Beatmaps;
using OsuClient.Game.Screens.SongSelect;

namespace OsuClient.Tests.Beatmaps
{
    /// <summary>
    /// Song search matching. The wheel downstream has to handle all, some and
    /// none of the library matching, so those three are pinned here rather
    /// than discovered through the UI.
    /// </summary>
    [TestFixture]
    public class BeatmapFilterTests
    {
        private static BeatmapLibraryEntry Entry(string artist, string title, string creator = "osu-dsp-generator")
        {
            string source = TestBeatmapFixtures.BackendGenerated
                                               .Replace("Artist:Test Artist", $"Artist:{artist}")
                                               .Replace("Title:Test Song", $"Title:{title}")
                                               .Replace("Creator:osu-dsp-generator", $"Creator:{creator}");

            return new BeatmapLibraryEntry
            {
                Path = $"/songs/{artist} - {title}.osz",
                Set = new BeatmapSet
                {
                    Name = $"{artist} - {title}",
                    Beatmaps = new List<Beatmap> { BeatmapDecoder.Decode(source) },
                },
            };
        }

        private static readonly IReadOnlyList<BeatmapLibraryEntry> library = new[]
        {
            Entry("ado", "new horizon"),
            Entry("ado", "show"),
            Entry("yoasobi", "idol"),
        };

        [Test]
        public void EmptyQueryMatchesEverything()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BeatmapFilter.Apply(library, string.Empty), Has.Count.EqualTo(3));
                Assert.That(BeatmapFilter.Apply(library, "   "), Has.Count.EqualTo(3));
                Assert.That(BeatmapFilter.Apply(library, null), Has.Count.EqualTo(3));
            });
        }

        [Test]
        public void MatchesArtistAndTitleCaseInsensitively()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BeatmapFilter.Apply(library, "ADO"), Has.Count.EqualTo(2));
                Assert.That(BeatmapFilter.Apply(library, "IdOl").Single().DisplayName,
                    Does.Contain("idol"));
            });
        }

        [Test]
        public void EveryTermHasToMatch()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BeatmapFilter.Apply(library, "ado horizon"), Has.Count.EqualTo(1));
                Assert.That(BeatmapFilter.Apply(library, "ado idol"), Is.Empty);
            });
        }

        [Test]
        public void NoMatchesYieldsEmptyRatherThanEverything()
        {
            Assert.That(BeatmapFilter.Apply(library, "zzzznotasong"), Is.Empty);
        }

        [Test]
        public void MatchesDifficultyNameAndMapper()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BeatmapFilter.Apply(library, "Hard"), Has.Count.EqualTo(3));
                Assert.That(BeatmapFilter.Apply(library, "osu-dsp-generator"), Has.Count.EqualTo(3));
            });
        }

        [Test]
        public void BrokenEntryStaysFindableByItsFileName()
        {
            var broken = new BeatmapLibraryEntry
            {
                Path = "/songs/corrupted song.osz",
                Error = "file is not a valid zip archive",
            };

            Assert.That(BeatmapFilter.Matches(broken, "corrupted"), Is.True);
        }
    }
}
