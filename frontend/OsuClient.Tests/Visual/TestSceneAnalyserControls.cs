using System;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Screens;
using osu.Framework.Testing;
using OsuClient.Game.Graphics.Rack;
using OsuClient.Game.Screens.Analysis;
using OsuClient.Game.Screens.SongSelect;
using osuTK;
using osuTK.Input;

namespace OsuClient.Tests.Visual
{
    /// <summary>
    /// The analyser's transport and handles, driven with the real mouse and
    /// keyboard: play/pause and Space, a pause that survives scrubbing, the
    /// playhead's pin lighting under the cursor and while held, knobs that
    /// click only when turned by hand, and the song select plate that leads
    /// here.
    /// </summary>
    [TestFixture]
    public partial class TestSceneAnalyserControls : ManualInputManagerTestScene
    {
        private DspInspectorScreen inspector = null!;

        private void pushInspector()
        {
            AddStep("push inspector", () =>
            {
                var stack = new ScreenStack { RelativeSizeAxes = Axes.Both };
                Child = stack;
                stack.Push(inspector = new DspInspectorScreen(TestSceneDspInspector.folderWithAudio(seconds: 20)));
            });

            AddUntilStep("playing", () => inspector.IsLoaded && inspector.IsPlaying);
        }

        [Test]
        public void TestPauseHoldsThroughAScrubAndPlayResumes()
        {
            pushInspector();

            AddUntilStep("key says pause", () => inspector.PlayPauseLabel == "PAUSE");

            AddStep("pause", () => inspector.TogglePlayPause());
            AddUntilStep("paused", () => !inspector.IsPlaying);
            AddUntilStep("key says play", () => inspector.PlayPauseLabel == "PLAY");

            // Scrubbing while paused is looking for a spot, not asking to play.
            AddStep("scrub", () => inspector.SeekToFraction(0.4));
            AddWaitStep("give it a moment", 3);
            AddAssert("still paused", () => !inspector.IsPlaying);

            AddStep("play", () => inspector.TogglePlayPause());
            AddUntilStep("playing again", () => inspector.IsPlaying);
        }

        [Test]
        public void TestSpaceIsPlayPause()
        {
            pushInspector();

            AddStep("press space", () => InputManager.Key(Key.Space));
            AddUntilStep("paused", () => !inspector.IsPlaying);

            AddStep("press space again", () => InputManager.Key(Key.Space));
            AddUntilStep("playing", () => inspector.IsPlaying);
        }

        [Test]
        public void TestThePinLightsUnderTheCursorAndWhileHeld()
        {
            pushInspector();

            Vector2 pin() => inspector.PlayheadPin.ScreenSpaceDrawQuad.TopLeft
                             + new Vector2(inspector.PlayheadPin.ScreenSpaceDrawQuad.Width / 2, 8);

            AddStep("pause, so the pin holds still", () => inspector.TogglePlayPause());
            AddStep("move off the pin", () => InputManager.MoveMouseTo(Vector2.Zero));
            AddAssert("dark", () => !inspector.PlayheadPin.Lit);

            AddStep("hover the pin", () => InputManager.MoveMouseTo(pin()));
            AddAssert("lit under the cursor", () => inspector.PlayheadPin.Lit);

            AddStep("grab and drag it", () =>
            {
                InputManager.PressButton(MouseButton.Left);
                InputManager.MoveMouseTo(pin() + new Vector2(200, 60));
            });
            AddAssert("lit while held", () => inspector.PlayheadPin.Lit);

            AddStep("let go, away from it", () =>
            {
                InputManager.ReleaseButton(MouseButton.Left);
                InputManager.MoveMouseTo(Vector2.Zero);
            });
            AddAssert("dark again", () => !inspector.PlayheadPin.Lit);
        }

        [Test]
        public void TestAKnobClicksOnlyWhenTurnedByHand()
        {
            ControlKnob knob = null!;
            int detents = 0;

            AddStep("create knob", () =>
            {
                detents = 0;
                Child = knob = new ControlKnob("MARGIN", 1, 3, 1.5f) { Anchor = Anchor.Centre, Origin = Anchor.Centre };
                knob.Detented += _ => detents++;
            });

            AddStep("set it, as a preset would", () => knob.Current.Value = 2.5f);
            AddAssert("no clicks", () => detents == 0);

            AddStep("turn it by hand", () =>
            {
                InputManager.MoveMouseTo(knob.ScreenSpaceDrawQuad.Centre);
                InputManager.PressButton(MouseButton.Left);

                // In steps, as a hand moves: one jump is one drag event.
                for (int step = 1; step <= 8; step++)
                    InputManager.MoveMouseTo(knob.ScreenSpaceDrawQuad.Centre + new Vector2(0, step * 12));

                InputManager.ReleaseButton(MouseButton.Left);
            });
            AddAssert("a click per detent crossed", () => detents > 1);
        }

        [Test]
        public void TestASwitchReportsItsFlick()
        {
            ToggleSwitch toggle = null!;
            bool? last = null;

            AddStep("create switch", () =>
            {
                Child = toggle = new ToggleSwitch("FLUX") { Anchor = Anchor.Centre, Origin = Anchor.Centre };
                toggle.Flicked += on => last = on;
            });

            AddStep("flick it", () => toggle.TriggerClick());
            AddAssert("reported on", () => last == true);

            AddStep("flick it back", () => toggle.TriggerClick());
            AddAssert("reported off", () => last == false);
        }

        [Test]
        public void TestTheAnalysePlateOnlyActsWithSomethingToOpen()
        {
            AnalyseDeckButton plate = null!;
            int opened = 0;

            AddStep("create plate", () =>
            {
                opened = 0;
                Child = plate = new AnalyseDeckButton { Anchor = Anchor.Centre, Origin = Anchor.Centre };
            });

            AddAssert("unavailable to begin with", () => !plate.Available);
            AddStep("press it", () => plate.TriggerClick());
            AddAssert("nothing opened", () => opened == 0);

            AddStep("offer an analysis", () => plate.SetAction(() => opened++));
            AddAssert("available", () => plate.Available);

            AddStep("press it", () =>
            {
                InputManager.MoveMouseTo(plate.ScreenSpaceDrawQuad.Centre);
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("opened", () => opened == 1);
        }
    }
}
