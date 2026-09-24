using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Screens;
using osu.Framework.Testing;
using OsuClient.Game.Beatmaps;
using OsuClient.Game.Scores;
using OsuClient.Game.Screens.Gameplay;
using OsuClient.Game.Screens.Results;
using OsuClient.Game.Screens.SongSelect;

namespace OsuClient.Tests.Visual
{
    /// <summary>
    /// High scores through the real game loop: a finished run is kept as the
    /// map's best, the results screen is told so, and the song select panel
    /// shows what is kept.
    /// </summary>
    [TestFixture]
    public partial class TestSceneHighScores : TestScene
    {
        private ScoresHost host = null!;

        private void play(string which = "")
        {
            AddStep($"play the quick map {which}", () =>
            {
                var beatmap = BeatmapDecoder.Decode(TestScenePlayerScreen.quick_map);
                var entry = new BeatmapLibraryEntry
                {
                    Path = "in-memory",
                    Set = new BeatmapSet { Beatmaps = new[] { beatmap } },
                };

                host.Stack.Push(new PlayerScreen(new BeatmapSelection(entry, beatmap)));
            });

            AddUntilStep($"results shown {which}", () => host.Stack.CurrentScreen is ResultsScreen);
        }

        [Test]
        public void TestAFinishedRunIsKeptAndAnnounced()
        {
            string hash = BeatmapDecoder.Decode(TestScenePlayerScreen.quick_map).ContentHash;

            AddStep("create game with an empty table", () => Child = host = new ScoresHost());

            play("first");

            AddAssert("kept as the map's best", () => host.Store.Get(hash) != null);
            AddAssert("results say it is new", () => ((ResultsScreen)host.Stack.CurrentScreen).ShownResult.NewHighScore);

            // The same run again scores the same: not a new best.
            AddStep("leave results", () => host.Stack.CurrentScreen.Exit());
            AddUntilStep("back to nothing", () => host.Stack.CurrentScreen == null);
            play("second");
            AddAssert("an equal run is not a new best", () => !((ResultsScreen)host.Stack.CurrentScreen).ShownResult.NewHighScore);
            AddAssert("and it knows what it had to beat", () => ((ResultsScreen)host.Stack.CurrentScreen).ShownResult.PreviousBest != null);
        }

        [Test]
        public void TestThePanelShowsTheBest()
        {
            SongInfoPanel panel = null!;

            AddStep("create panel", () => Child = panel = new SongInfoPanel());
            AddAssert("nothing yet", () => panel.HighScoreText == "--");

            AddStep("show a best", () => panel.SetHighScore(new HighScore { Score = 1234567, Accuracy = 98.5, MaxCombo = 321, Grade = Grade.S }));
            AddAssert("shown with separators", () => panel.HighScoreText == 1234567.ToString("N0"));

            AddStep("clear it", () => panel.SetHighScore(null));
            AddAssert("nothing again", () => panel.HighScoreText == "--");
        }

        /// <summary>A screen stack with an in-memory high score table cached above it, as the game has.</summary>
        private partial class ScoresHost : CompositeDrawable
        {
            [Cached]
            public readonly HighScoreStore Store = new HighScoreStore(null);

            public readonly ScreenStack Stack = new ScreenStack { RelativeSizeAxes = Axes.Both };

            public ScoresHost()
            {
                RelativeSizeAxes = Axes.Both;
                InternalChild = Stack;
            }
        }
    }
}
