using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Framework.Testing.Input;
using OsuClient.Game.Beatmaps;
using OsuClient.Game.Graphics;
using OsuClient.Game.Screens.MainMenu;
using OsuClient.Tests.Beatmaps;
using osuTK.Input;

namespace OsuClient.Tests.Visual
{
    /// <summary>
    /// The main menu's song list on its own, against made-up songs.
    ///
    /// The menu's own scenes use whatever library the machine has, which may
    /// be empty. This one always has songs — including one with a title far
    /// too long for the panel — so the list's behaviour is checked everywhere.
    /// </summary>
    [TestFixture]
    public partial class TestSceneSongPicker : osu.Framework.Testing.TestScene
    {
        private const string long_title = "A Truly Enormous Song Title That Goes On And On Far Past The Edge Of The Panel";

        private SongPicker picker = null!;
        private ManualInputManager input = null!;

        private BeatmapLibraryEntry? pickedEntry;
        private int dismissCount;

        private static BeatmapLibraryEntry Song(string title, string artist)
        {
            var beatmap = BeatmapDecoder.Decode(TestBeatmapFixtures.BackendGenerated);
            beatmap.Metadata.Title = title;
            beatmap.Metadata.Artist = artist;

            return new BeatmapLibraryEntry
            {
                Path = "/songs/" + artist + " - " + title,
                Set = new BeatmapSet { Name = title, Beatmaps = new List<Beatmap> { beatmap } },
            };
        }

        private static List<BeatmapLibraryEntry> songs() => new List<BeatmapLibraryEntry>
        {
            Song("Eden", "Arekta Rock Band"),
            Song("One Last Kiss", "Hikaru Utada"),
            Song(long_title, "Somebody With A Rather Long Artist Name Too, Honestly"),
            Song("Unity", "TheFatRat"),
            Song("Closer", "The Chainsmokers"),
            Song("Show", "Ado"),
            Song("Unravel", "Tokyo Ghoul"),
            Song("Bad Apple!!", "Masayoshi Minoshima"),
            Song("New Horizon", "Ado"),
        };

        [SetUpSteps]
        public void SetUpSteps()
        {
            AddStep("build the list", () =>
            {
                pickedEntry = null;
                dismissCount = 0;

                Child = input = new ManualInputManager
                {
                    RelativeSizeAxes = Axes.Both,
                    Child = new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Child = picker = new SongPicker(e => pickedEntry = e, () => dismissCount++),
                    },
                };

                picker.SetSongs(songs());
                picker.SetCurrent("/songs/Ado - Show");
            });

            AddUntilStep("rows loaded", () => picker.SongCount == 9 && picker.Rows.Count == 9);

            // Laid out by the menu every frame in the real game.
            AddStep("lay out", () => picker.OnUpdate += _ => picker.Layout(120, 1080, 1920));

            AddStep("open", () => picker.Show());
            AddUntilStep("fully shown", () => picker.Alpha > 0.99f);
        }

        [Test]
        public void TestListsEverySongAndMarksTheOnePlaying()
        {
            AddAssert("one row per song", () => picker.Rows.Count == 9);
            AddAssert("exactly one row is current", () => picker.Rows.Count(r => r.IsCurrent) == 1);
            AddAssert("it is the right one", () => picker.Rows.Single(r => r.IsCurrent).TitleText == "Show");

            AddStep("another song starts", () => picker.SetCurrent("/songs/TheFatRat - Unity"));
            AddAssert("the mark moves", () => picker.Rows.Single(r => r.IsCurrent).TitleText == "Unity");
        }

        [Test]
        public void TestALongTitleIsCutToFitThePanel()
        {
            AddAssert("long title stays inside the panel", () =>
            {
                var row = picker.Rows.Single(r => r.TitleText == long_title);

                // Each line's right edge has to clear the row's right edge, with
                // room for the playing marker.
                return row.ChildrenOfType<RetroText>()
                          .Where(t => t.Font == RetroFontFamily.Body)
                          .All(t => t.ToSpaceOfOtherDrawable(new osuTK.Vector2(t.DrawWidth, 0), row).X < row.DrawWidth - 20);
            });
        }

        [Test]
        public void TestClickingARowPicksThatSong()
        {
            AddStep("point at Closer", () => input.MoveMouseTo(picker.Rows.Single(r => r.TitleText == "Closer")));
            AddStep("click", () => input.Click(MouseButton.Left));

            AddAssert("it was picked", () => pickedEntry != null && pickedEntry.DisplayName.Contains("Closer"));
            AddAssert("and that is not a dismissal", () => dismissCount == 0);
        }

        [Test]
        public void TestClickingAwayDismisses()
        {
            AddStep("point at an empty part of the window", () => input.MoveMouseTo(input.ToScreenSpace(new osuTK.Vector2(300, 600))));
            AddStep("click", () => input.Click(MouseButton.Left));

            AddAssert("dismissed once", () => dismissCount == 1);
            AddAssert("nothing was picked", () => pickedEntry == null);
        }

        [Test]
        public void TestClickingThePanelsHeaderDoesNotDismiss()
        {
            // Between rows and on the header there is no row to take the click;
            // it must not fall through to the "clicked away" handler.
            AddStep("point at the header", () => input.MoveMouseTo(
                input.ToScreenSpace(new osuTK.Vector2(1920 - 52 - SongPicker.PanelWidth / 2, 120 + 14))));
            AddStep("click", () => input.Click(MouseButton.Left));

            AddAssert("still open, nothing dismissed", () => dismissCount == 0 && picker.State.Value == Visibility.Visible);
        }

        [Test]
        public void TestScrollingMovesTheList()
        {
            float before = 0;

            AddStep("note where Eden is", () => before = picker.Rows[0].ScreenSpaceDrawQuad.TopLeft.Y);
            AddStep("point at the list", () => input.MoveMouseTo(picker.Rows[3]));
            AddStep("scroll down", () => input.ScrollBy(new osuTK.Vector2(0, -6)));
            AddUntilStep("the list moved", () => picker.Rows[0].ScreenSpaceDrawQuad.TopLeft.Y < before - 50);
        }

        [Test]
        public void TestOpensWithTheCurrentSongInView()
        {
            // The last song is current, far below the first screenful.
            AddStep("close", () => picker.Hide());
            AddStep("make the last song current", () => picker.SetCurrent("/songs/Ado - New Horizon"));
            AddStep("open again", () => picker.Show());
            AddUntilStep("the current row is on screen", () =>
            {
                var row = picker.Rows.Single(r => r.IsCurrent);
                var quad = row.ScreenSpaceDrawQuad;

                return quad.TopLeft.Y > 120 && quad.BottomLeft.Y < 120 + 600;
            });
        }
    }
}
