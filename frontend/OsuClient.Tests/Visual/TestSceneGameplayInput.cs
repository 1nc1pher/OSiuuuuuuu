using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osu.Framework.Testing.Input;
using OsuClient.Game.Beatmaps;
using OsuClient.Game.Graphics;
using OsuClient.Game.Input;
using OsuClient.Game.Screens.Gameplay;
using OsuClient.Game.Screens.Gameplay.HUD;
using OsuClient.Game.Screens.SongSelect;
using osuTK;
using osuTK.Input;

namespace OsuClient.Tests.Visual
{
    /// <summary>
    /// Drives real input through the framework's input pipeline (rather than
    /// calling judgement methods directly), which is the only way to catch
    /// input-routing problems in <see cref="PlayerScreen"/>.
    ///
    /// The single circle sits at playfield centre (256,192), which is also
    /// screen centre, so moving the cursor to the screen's middle puts it
    /// over the circle. OD 0 gives a generous ±200 ms window so the simulated
    /// press can't fall outside it because of frame timing.
    /// </summary>
    [TestFixture]
    public partial class TestSceneGameplayInput : osu.Framework.Testing.TestScene
    {
        private const string one_circle_map =
            """
            osu file format v14

            [General]
            AudioFilename:audio.mp3

            [Metadata]
            Version:Test

            [Difficulty]
            HPDrainRate:5
            CircleSize:4
            OverallDifficulty:0
            ApproachRate:9
            SliderMultiplier:1.4
            SliderTickRate:1

            [TimingPoints]
            0,500,4,1,0,70,1,0

            [HitObjects]
            256,192,1000,5,0,0:0:0:0:
            """;

        private ManualInputManager input = null!;
        private ScreenStack stack = null!;

        // Held directly rather than read back off the stack: gameplay moves on
        // to the results screen once it finishes.
        private PlayerScreen player = null!;

        private void pushGameplay()
        {
            AddStep("push gameplay", () =>
            {
                Child = input = new ManualInputManager
                {
                    RelativeSizeAxes = Axes.Both,
                    Child = stack = new ScreenStack { RelativeSizeAxes = Axes.Both },
                };

                var beatmap = BeatmapDecoder.Decode(one_circle_map);

                var entry = new BeatmapLibraryEntry
                {
                    Path = "in-memory",
                    Set = new BeatmapSet { Beatmaps = new[] { beatmap } },
                };

                stack.Push(player = new PlayerScreen(new BeatmapSelection(entry, beatmap)));
            });

            AddUntilStep("gameplay loaded", () => player.IsLoaded);
        }

        /// <summary>
        /// Acts in the same frame the circle becomes hittable. A plain
        /// AddStep after an AddUntilStep runs a frame or more later, which at
        /// these hit windows is long enough for the note to have expired.
        /// </summary>
        private void actWhenHittable(string description, Action act)
        {
            AddUntilStep(description, () =>
            {
                if (player.GameplayTime < 1000)
                    return false;

                act();
                return true;
            });
        }

        /// <summary>Opens the pause menu and waits for it to actually be up.</summary>
        private void pauseGameplay()
        {
            AddStep("press escape", () =>
            {
                input.PressKey(Key.Escape);
                input.ReleaseKey(Key.Escape);
            });

            AddUntilStep("pause menu is up",
                () => player.ChildrenOfType<PauseOverlay>().Single().Alpha > 0.9f);
        }

        /// <summary>
        /// The menu's buttons are private to the overlay, so they're found by
        /// their label — clicking the label lands on the button behind it.
        /// </summary>
        private Drawable pauseMenuButton(string label) =>
            player.ChildrenOfType<RetroText>().Single(t => t.Text == label);

        private void clickPauseMenuButton(string label) => AddStep($"click \"{label}\"", () =>
        {
            input.MoveMouseTo(pauseMenuButton(label));
            input.Click(MouseButton.Left);
        });

        [Test]
        public void TestContinueButtonResumesGameplay()
        {
            pushGameplay();
            pauseGameplay();

            clickPauseMenuButton("Continue");

            AddAssert("gameplay resumed", () => !player.Paused);
            AddAssert("still on the gameplay screen", () => stack.CurrentScreen is PlayerScreen);
        }

        [Test]
        public void TestQuitButtonLeavesGameplay()
        {
            pushGameplay();
            pauseGameplay();

            clickPauseMenuButton("Quit to song select");

            AddUntilStep("the gameplay screen is gone", () => stack.CurrentScreen is not PlayerScreen);
        }

        [Test]
        public void TestHitKeysDoNothingWhilePaused()
        {
            pushGameplay();

            AddStep("put cursor on the circle", () => input.MoveMouseTo(player));

            // Paused at the exact moment the circle is hittable, so the clock
            // stays frozen inside its hit window: without the input guard a
            // press here would score it through the menu.
            actWhenHittable("pause as the circle becomes hittable", () => input.PressKey(Key.Escape));
            AddStep("release escape", () => input.ReleaseKey(Key.Escape));

            AddAssert("gameplay is paused", () => player.Paused);

            AddStep("press Z", () =>
            {
                input.PressKey(Key.Z);
                input.ReleaseKey(Key.Z);
            });

            AddAssert("the circle was not judged", () => player.ScoreState.TotalJudged == 0);
        }

        private KeyTimingBar keyBar(HitKeyColumn column) =>
            player.ChildrenOfType<KeyTimingBar>().ElementAt(column == HitKeyColumn.First ? 0 : 1);

        [Test]
        public void TestAHitIsPlottedOnTheBarOfTheKeyThatMadeIt()
        {
            pushGameplay();

            AddStep("put cursor on the circle", () => input.MoveMouseTo(player));
            actWhenHittable("press Z on time", () =>
            {
                input.PressKey(Key.Z);
                input.ReleaseKey(Key.Z);
            });

            AddAssert("the circle was hit", () => player.ScoreState.CountMiss == 0);
            AddAssert("the hit landed on Z's bar",
                () => keyBar(HitKeyColumn.First).PlottedHitCount == 1);
            AddAssert("and not on X's", () => keyBar(HitKeyColumn.Second).PlottedHitCount == 0);
        }

        [Test]
        public void TestAPressThatHitsNothingPlotsNothing()
        {
            pushGameplay();

            // Away from the circle, so the press is never taken by it.
            AddStep("move the cursor off the circle", () => input.MoveMouseTo(player, new Vector2(-200, -150)));
            actWhenHittable("press Z", () =>
            {
                input.PressKey(Key.Z);
                input.ReleaseKey(Key.Z);
            });

            AddAssert("nothing was plotted", () => keyBar(HitKeyColumn.First).PlottedHitCount == 0);
        }

        [Test]
        public void TestKeyPressOverCircleRegistersAHit()
        {
            pushGameplay();

            AddStep("put cursor on the circle", () => input.MoveMouseTo(player));
            actWhenHittable("press Z on time", () =>
            {
                input.PressKey(Key.Z);
                input.ReleaseKey(Key.Z);
            });

            AddAssert("circle was hit, not missed",
                () => player.ScoreState.TotalJudged == 1 && player.ScoreState.CountMiss == 0);
        }

        [Test]
        public void TestMouseClickOverCircleRegistersAHit()
        {
            pushGameplay();

            AddStep("put cursor on the circle", () => input.MoveMouseTo(player));
            actWhenHittable("click on time", () => input.Click(MouseButton.Left));

            AddAssert("circle was hit, not missed",
                () => player.ScoreState.TotalJudged == 1 && player.ScoreState.CountMiss == 0);
        }

        [Test]
        public void TestPressAwayFromTheCircleDoesNotHitIt()
        {
            pushGameplay();

            AddStep("put cursor in the corner", () => input.MoveMouseTo(new Vector2(5, 5)));
            actWhenHittable("press Z on time but off-target", () =>
            {
                input.PressKey(Key.Z);
                input.ReleaseKey(Key.Z);
            });

            AddUntilStep("gameplay completes", () => player.Completed);
            AddAssert("circle missed", () => player.ScoreState.CountMiss == 1);
        }

        [Test]
        public void TestEarlyPressDoesNotDestroyTheCircle()
        {
            pushGameplay();

            AddStep("put cursor on the circle", () => input.MoveMouseTo(player));

            // Well before the hit window: osu! ignores this rather than
            // judging the upcoming note, so the circle must still be hittable.
            AddStep("press Z far too early", () =>
            {
                input.PressKey(Key.Z);
                input.ReleaseKey(Key.Z);
            });

            AddAssert("nothing judged yet", () => player.ScoreState.TotalJudged == 0);

            actWhenHittable("press Z on time", () =>
            {
                input.PressKey(Key.Z);
                input.ReleaseKey(Key.Z);
            });

            AddAssert("circle was hit, not missed",
                () => player.ScoreState.TotalJudged == 1 && player.ScoreState.CountMiss == 0);
        }
    }
}
