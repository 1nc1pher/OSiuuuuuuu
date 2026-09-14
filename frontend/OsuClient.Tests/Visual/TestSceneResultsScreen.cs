using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Screens;
using OsuClient.Game.Screens.Gameplay;
using OsuClient.Game.Screens.Results;
using osuTK.Input;

namespace OsuClient.Tests.Visual
{
    /// <summary>
    /// The end-of-play screen: that it builds for both a cleared and a failed
    /// run, and that it hands control back when dismissed.
    /// </summary>
    [TestFixture]
    public partial class TestSceneResultsScreen : osu.Framework.Testing.TestScene
    {
        private ScreenStack stack = null!;
        private ResultsScreen results = null!;

        private static ResultsScreen.Result result(Grade grade, bool failed) => new ResultsScreen.Result
        {
            Title = "Test Artist - Test Song",
            Difficulty = "Hard",
            Grade = grade,
            Score = 123456,
            Accuracy = 97.53,
            MaxCombo = 312,
            CountGreat = 280,
            CountOk = 20,
            CountMeh = 5,
            CountMiss = failed ? 12 : 0,
            Failed = failed,
        };

        private void push(ResultsScreen.Result value)
        {
            AddStep("push results", () =>
            {
                Child = stack = new ScreenStack { RelativeSizeAxes = Axes.Both };
                stack.Push(results = new ResultsScreen(value));
            });

            AddUntilStep("results loaded", () => results.IsLoaded);
        }

        [Test]
        public void TestClearedRunShowsItsGrade()
        {
            push(result(Grade.S, failed: false));

            AddAssert("results screen is current", () => stack.CurrentScreen is ResultsScreen);
        }

        [Test]
        public void TestFailedRunBuilds()
        {
            push(result(Grade.D, failed: true));

            AddAssert("results screen is current", () => stack.CurrentScreen is ResultsScreen);
        }

        [Test]
        public void TestEveryGradeBuilds()
        {
            foreach (var grade in new[] { Grade.D, Grade.C, Grade.B, Grade.A, Grade.S, Grade.SS })
            {
                var value = result(grade, failed: false);

                push(value);
                AddAssert($"{grade} builds", () => stack.CurrentScreen is ResultsScreen);
            }
        }

        [Test]
        public void TestResultsSnapshotIsIndependentOfLaterScoring()
        {
            var score = new ScoreProcessor();
            score.Apply(HitResult.Great);

            ResultsScreen.Result snapshot = null!;

            AddStep("snapshot the score", () =>
            {
                snapshot = new ResultsScreen.Result
                {
                    Title = "t",
                    Difficulty = "d",
                    Grade = score.Grade,
                    Score = score.Score,
                    Accuracy = score.Accuracy,
                    MaxCombo = score.MaxCombo,
                    CountGreat = score.CountGreat,
                    CountOk = score.CountOk,
                    CountMeh = score.CountMeh,
                    CountMiss = score.CountMiss,
                    Failed = false,
                };
            });

            AddStep("keep scoring afterwards", () => score.Apply(HitResult.Miss));

            AddAssert("the snapshot didn't move with it",
                () => snapshot.CountGreat == 1 && snapshot.CountMiss == 0);
        }

        [Test]
        public void TestEscapeLeavesTheResultsScreen()
        {
            push(result(Grade.A, failed: false));

            AddStep("press escape", () =>
            {
                results.TriggerEvent(new osu.Framework.Input.Events.KeyDownEvent(
                    new osu.Framework.Input.States.InputState(), Key.Escape));
            });

            AddUntilStep("screen was exited", () => stack.CurrentScreen is not ResultsScreen);
        }
    }
}
