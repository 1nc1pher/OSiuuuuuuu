using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Input.Events;
using osu.Framework.Input.States;
using osu.Framework.Screens;
using OsuClient.Game.Beatmaps;
using OsuClient.Game.Screens.Gameplay;
using OsuClient.Game.Screens.Results;
using OsuClient.Game.Screens.SongSelect;
using osuTK.Input;

namespace OsuClient.Tests.Visual
{
    /// <summary>
    /// Runs the real gameplay screen end to end, headlessly and without
    /// simulating any input — every hit circle should therefore resolve as a
    /// miss on its own, and gameplay should reach
    /// <see cref="PlayerScreen.Completed"/> without throwing. This exercises
    /// spawning, the audio-lead-in gameplay clock, the approach-circle
    /// animation timing and expiry-to-miss, all without needing a real audio
    /// file or simulated input.
    /// </summary>
    [TestFixture]
    public partial class TestScenePlayerScreen : osu.Framework.Testing.TestScene
    {
        // A circle, a slider and a spinner packed close together: covers every
        // object type while still resolving in a couple of real seconds, well
        // inside the test scene's step timeout.
        private const string quick_map =
            """
            osu file format v14

            [General]
            AudioFilename:audio.mp3

            [Metadata]
            Version:Test

            [Difficulty]
            HPDrainRate:5
            CircleSize:4
            OverallDifficulty:6
            ApproachRate:9
            SliderMultiplier:1.4
            SliderTickRate:1

            [TimingPoints]
            0,500,4,1,0,70,1,0

            [HitObjects]
            100,120,400,5,0,0:0:0:0:
            200,200,800,2,0,L|270:200,1,70.00,0|0,0:0|0:0,0:0:0:0:
            256,192,1200,8,0,1800,0:0:0:0:
            """;

        // HP 10 and a run of circles: missing them all empties the bar before
        // the map ends, which is what a failed run looks like.
        private const string drain_map =
            """
            osu file format v14

            [General]
            AudioFilename:audio.mp3

            [Metadata]
            Version:Drain

            [Difficulty]
            HPDrainRate:10
            CircleSize:4
            OverallDifficulty:6
            ApproachRate:9
            SliderMultiplier:1.4
            SliderTickRate:1

            [TimingPoints]
            0,500,4,1,0,70,1,0

            [HitObjects]
            100,120,400,5,0,0:0:0:0:
            140,140,600,1,0,0:0:0:0:
            180,160,800,1,0,0:0:0:0:
            220,180,1000,1,0,0:0:0:0:
            260,200,1200,1,0,0:0:0:0:
            300,220,1400,1,0,0:0:0:0:
            340,240,1600,1,0,0:0:0:0:
            380,260,1800,1,0,0:0:0:0:
            """;

        private ScreenStack stack = null!;

        // Held directly rather than read back off the stack: gameplay moves on
        // to the results screen once it finishes, so CurrentScreen stops being
        // the player partway through these tests.
        private PlayerScreen player = null!;

        private void pushGameplay(string map)
        {
            AddStep("create screen stack", () => Child = stack = new ScreenStack { RelativeSizeAxes = Axes.Both });

            AddStep("push player screen", () =>
            {
                var beatmap = BeatmapDecoder.Decode(map);

                var entry = new BeatmapLibraryEntry
                {
                    Path = "in-memory",
                    Set = new BeatmapSet { Beatmaps = new[] { beatmap } },
                };

                stack.Push(player = new PlayerScreen(new BeatmapSelection(entry, beatmap)));
            });

            AddUntilStep("player screen loaded", () => player.IsLoaded);
        }

        [Test]
        public void TestGameplayLoopCompletesWithoutInput()
        {
            pushGameplay(quick_map);

            AddUntilStep("gameplay completes", () => player.Completed);

            AddAssert("circle, slider and spinner each judged a miss (no input was simulated)",
                () => player.ScoreState.CountMiss == 3 && player.ScoreState.TotalJudged == 3);

            AddAssert("the run was not failed", () => !player.Failed);
        }

        [Test]
        public void TestResultsScreenFollowsACompletedMap()
        {
            pushGameplay(quick_map);

            AddUntilStep("gameplay completes", () => player.Completed);
            AddUntilStep("results screen takes over", () => stack.CurrentScreen is ResultsScreen);
        }

        [Test]
        public void TestDismissingResultsReturnsPastTheFinishedMap()
        {
            pushGameplay(quick_map);

            AddUntilStep("gameplay completes", () => player.Completed);
            AddUntilStep("results screen takes over", () => stack.CurrentScreen is ResultsScreen);

            AddStep("dismiss the results", () => ((ResultsScreen)stack.CurrentScreen!).Exit());

            // Backing out of results must not drop the player into the map
            // they just finished — it should carry on out to song select.
            AddUntilStep("the finished gameplay screen is gone",
                () => stack.CurrentScreen is not PlayerScreen and not ResultsScreen);
        }

        private void pressEscape() => player.TriggerEvent(
            new KeyDownEvent(new InputState(), Key.Escape));

        [Test]
        public void TestEscapePausesRatherThanEndingTheRun()
        {
            pushGameplay(quick_map);

            AddStep("press escape", pressEscape);

            AddAssert("gameplay is paused", () => player.Paused);
            AddAssert("still on the gameplay screen", () => stack.CurrentScreen is PlayerScreen);
            AddAssert("the run has not ended", () => !player.Completed && !player.Failed);
        }

        [Test]
        public void TestPausedGameplayDoesNotRunOnBehindTheMenu()
        {
            pushGameplay(quick_map);

            // Partway in, so there is something in flight to freeze.
            AddUntilStep("an object has been judged", () => player.ScoreState.TotalJudged >= 1);

            double timeAtPause = 0;
            int judgedAtPause = 0;

            AddStep("pause", () =>
            {
                pressEscape();

                timeAtPause = player.GameplayTime;
                judgedAtPause = player.ScoreState.TotalJudged;
            });

            AddWaitStep("let some frames pass", 10);

            AddAssert("gameplay time is frozen", () => player.GameplayTime == timeAtPause);
            AddAssert("nothing was judged while paused",
                () => player.ScoreState.TotalJudged == judgedAtPause);
        }

        [Test]
        public void TestResumingCarriesTheRunOn()
        {
            pushGameplay(quick_map);

            AddStep("pause", pressEscape);
            AddAssert("paused", () => player.Paused);

            AddStep("resume", pressEscape);
            AddAssert("resumed", () => !player.Paused);

            AddUntilStep("gameplay completes", () => player.Completed);
            AddAssert("every object was still judged", () => player.ScoreState.TotalJudged == 3);
        }

        [Test]
        public void TestEscapeStillLeavesOnceTheRunIsOver()
        {
            pushGameplay(quick_map);

            AddUntilStep("gameplay completes", () => player.Completed);

            AddStep("press escape", pressEscape);

            AddAssert("a finished run is not paused by it", () => !player.Paused);
            AddUntilStep("the gameplay screen is gone", () => stack.CurrentScreen is not PlayerScreen);
        }

        [Test]
        public void TestHealthDrainsWhileMissingEverything()
        {
            pushGameplay(quick_map);

            AddUntilStep("gameplay completes", () => player.Completed);

            AddAssert("health fell from missing every object",
                () => player.HealthState.Health < 1);
        }

        [Test]
        public void TestRunningOutOfHealthFailsTheMap()
        {
            // HP 10 with nothing but misses: health can't survive the map.
            pushGameplay(drain_map);

            AddUntilStep("the run fails", () => player.Failed);

            AddAssert("health is empty", () => player.HealthState.Health <= 0);
            AddAssert("gameplay stopped at the fail", () => player.Completed);

            AddUntilStep("results screen takes over", () => stack.CurrentScreen is ResultsScreen);
        }
    }
}
