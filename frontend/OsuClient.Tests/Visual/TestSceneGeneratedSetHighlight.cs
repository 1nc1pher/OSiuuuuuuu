using System;
using System.Collections.Generic;
using NUnit.Framework;
using osu.Framework.Graphics;
using OsuClient.Game.Beatmaps;
using OsuClient.Game.Screens.SongSelect;
using OsuClient.Tests.Beatmaps;

namespace OsuClient.Tests.Visual
{
    /// <summary>
    /// Song select starting on a just-generated set (FRONTEND_PLAN.md Phase 7,
    /// the hand-off at the end of the DSP reveal).
    ///
    /// This exists because the obvious implementation is silently wrong. The
    /// backend reports the folder it wrote; <see cref="BeatmapLibrary"/> lists
    /// that same set by its <c>.osz</c> and skips the folder as a duplicate. A
    /// path comparison between the two therefore never matches, and the only
    /// symptom is the carousel sitting on the wrong song — no error, nothing in
    /// a log. It cost a full end-to-end run to notice.
    /// </summary>
    [TestFixture]
    public partial class TestSceneGeneratedSetHighlight : osu.Framework.Testing.TestScene
    {
        [Test]
        public void TestAFolderAndItsOszNameTheSameSet()
        {
            Assert.That(BeatmapCarousel.SetNameOf(@"D:\out\unknown artist - bad_apple"),
                Is.EqualTo("unknown artist - bad_apple"));

            Assert.That(BeatmapCarousel.SetNameOf(@"D:\out\unknown artist - bad_apple.osz"),
                Is.EqualTo("unknown artist - bad_apple"));

            Assert.That(BeatmapCarousel.SetNameOf(@"D:\out\unknown artist - bad_apple\"),
                Is.EqualTo("unknown artist - bad_apple"));
        }

        [Test]
        public void TestADotInTheTitleIsNotTreatedAsAnExtension()
        {
            // Only ".osz" is packaging; everything else is part of the name.
            Assert.That(BeatmapCarousel.SetNameOf(@"D:\out\artist - song v1.5"),
                Is.EqualTo("artist - song v1.5"));

            Assert.That(BeatmapCarousel.SetNameOf(@"D:\out\artist - song v1.5.osz"),
                Is.EqualTo("artist - song v1.5"));
        }

        [Test]
        public void TestCarouselStartsOnTheRequestedSet()
        {
            var entries = fakeEntries("aaa - first", "mmm - middle", "zzz - generated");
            BeatmapCarousel carousel = null!;

            AddStep("show carousel", () =>
            {
                Child = carousel = new BeatmapCarousel { RelativeSizeAxes = Axes.Both };
            });

            AddUntilStep("loaded", () => carousel.IsLoaded);

            AddStep("set entries preferring the generated one", () =>
                // Deliberately the folder form, which is what the backend
                // reports, against a library listing its .osz.
                carousel.SetEntries(entries, "nothing here", @"C:\songs\zzz - generated"));

            AddAssert("starts on the generated set",
                () => carousel.Selection?.Entry.DisplayName.Contains("generated") == true);
        }

        [Test]
        public void TestCarouselFallsBackToTheFirstSetWhenTheRequestIsUnknown()
        {
            var entries = fakeEntries("aaa - first", "zzz - last");
            BeatmapCarousel carousel = null!;

            AddStep("show carousel", () =>
            {
                Child = carousel = new BeatmapCarousel { RelativeSizeAxes = Axes.Both };
            });

            AddUntilStep("loaded", () => carousel.IsLoaded);

            AddStep("prefer a set that isn't listed", () =>
                carousel.SetEntries(entries, "nothing here", @"C:\songs\deleted - gone"));

            AddAssert("falls back to the first",
                () => carousel.Selection?.Entry.DisplayName.Contains("first") == true);
        }

        [Test]
        public void TestNoPreferenceStillStartsOnTheFirstSet()
        {
            var entries = fakeEntries("aaa - first", "zzz - last");
            BeatmapCarousel carousel = null!;

            AddStep("show carousel", () =>
            {
                Child = carousel = new BeatmapCarousel { RelativeSizeAxes = Axes.Both };
            });

            AddUntilStep("loaded", () => carousel.IsLoaded);
            AddStep("set entries", () => carousel.SetEntries(entries, "nothing here"));

            AddAssert("first set selected",
                () => carousel.Selection?.Entry.DisplayName.Contains("first") == true);
        }

        /// <summary>
        /// Library entries shaped the way a real generated set appears: listed
        /// by <c>.osz</c>, decoded from the same backend-shaped fixture the
        /// rest of the carousel tests use.
        /// </summary>
        private static IReadOnlyList<BeatmapLibraryEntry> fakeEntries(params string[] setNames)
        {
            var entries = new List<BeatmapLibraryEntry>();

            foreach (string name in setNames)
            {
                var beatmap = BeatmapDecoder.Decode(
                    TestBeatmapFixtures.BackendGenerated
                                       .Replace("Title:Test Song", "Title:" + name));

                entries.Add(new BeatmapLibraryEntry
                {
                    Path = @"C:\songs\" + name + ".osz",
                    Set = new BeatmapSet
                    {
                        Name = name,
                        Beatmaps = new List<Beatmap> { beatmap },
                        Files = new[] { "audio.mp3" },
                        AudioFilename = "audio.mp3",
                    },
                });
            }

            return entries;
        }
    }
}
