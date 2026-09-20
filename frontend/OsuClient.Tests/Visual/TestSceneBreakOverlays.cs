using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osu.Framework.Testing.Input;
using OsuClient.Game.Beatmaps;
using OsuClient.Game.Screens.Gameplay;
using OsuClient.Game.Screens.Gameplay.HUD;
using OsuClient.Game.Screens.SongSelect;
using osuTK.Input;

namespace OsuClient.Tests.Visual
{
    /// <summary>
    /// The break overlays: a skip prompt over a long gap with nothing to hit,
    /// a live performance readout over a shorter one. Both maps below use a
    /// single circle, so the gap under test is the leading one — from the
    /// start of the track to the object's own approach — which only needs a
    /// couple of real seconds' lead-in to reach, rather than waiting out the
    /// gap itself (which for the skip case would otherwise mean a genuine
    /// 20-second test). <see cref="BreakPeriodTests"/> already covers finding
    /// every gap in a map, including ones between later objects; this only
    /// has to prove <see cref="PlayerScreen"/> wires one up correctly once
    /// it's found.
    ///
    /// AR9/OD6, same as <see cref="BreakPeriodTests"/>: preempt 600ms, meh
    /// window 140ms.
    /// </summary>
    [TestFixture]
    public partial class TestSceneBreakOverlays : osu.Framework.Testing.TestScene
    {
        private const string map_header =
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

            """;

        // Leading gap = 20000 - preempt(600) = 19400ms — comfortably past the
        // 5000ms skip threshold.
        private static string skipMap(int circleTime = 20000) => map_header + $"256,192,{circleTime},5,0,0:0:0:0:";

        // Leading gap = 4000 - preempt(600) = 3400ms — inside the
        // [1500, 5000) performance band.
        private const string performance_map = map_header + "256,192,4000,5,0,0:0:0:0:";

        // Leading gap = 1200 - preempt(600) = 600ms — below even the
        // performance threshold, so neither overlay should ever show.
        private const string trivial_gap_map = map_header + "256,192,1200,5,0,0:0:0:0:";

        private ManualInputManager input = null!;
        private ScreenStack stack = null!;
        private PlayerScreen player = null!;

        private void pushGameplay(string map)
        {
            AddStep("push gameplay", () =>
            {
                Child = input = new ManualInputManager
                {
                    RelativeSizeAxes = Axes.Both,
                    Child = stack = new ScreenStack { RelativeSizeAxes = Axes.Both },
                };

                var beatmap = BeatmapDecoder.Decode(map);

                var entry = new BeatmapLibraryEntry
                {
                    Path = "in-memory",
                    Set = new BeatmapSet { Beatmaps = new[] { beatmap } },
                };

                stack.Push(player = new PlayerScreen(new BeatmapSelection(entry, beatmap)));
            });

            AddUntilStep("gameplay loaded", () => player.IsLoaded);
        }

        private SkipOverlay skipOverlay => player.ChildrenOfType<SkipOverlay>().Single();

        [Test]
        public void TestSkipOverlayAppearsDuringALongGap()
        {
            pushGameplay(skipMap());

            AddUntilStep("skip overlay shown", () => player.SkipOverlayVisible);
            AddAssert("performance overlay is not", () => !player.PerformanceOverlayVisible);
        }

        [Test]
        public void TestPerformanceOverlayAppearsDuringAModerateGap()
        {
            pushGameplay(performance_map);

            AddUntilStep("performance overlay shown", () => player.PerformanceOverlayVisible);
            AddAssert("skip overlay is not", () => !player.SkipOverlayVisible);
        }

        [Test]
        public void TestNeitherOverlayShowsDuringOrdinaryObjectSpacing()
        {
            pushGameplay(trivial_gap_map);

            // Give it the same lead-in time the other tests wait out for
            // their overlay to appear, so this is a real "still nothing" check
            // rather than just catching the state before it had a chance to.
            AddWaitStep("wait through the lead-in", 90);

            AddAssert("skip overlay never showed", () => !player.SkipOverlayVisible);
            AddAssert("performance overlay never showed", () => !player.PerformanceOverlayVisible);
        }

        [Test]
        public void TestSkipBarShrinksAsTheGapRunsOut()
        {
            pushGameplay(skipMap());

            AddUntilStep("skip overlay shown", () => player.SkipOverlayVisible);

            float first = 0;
            AddStep("record the bar's extent", () => first = skipOverlay.CurrentBarExtent);

            AddWaitStep("let time pass", 20);

            AddAssert("the bar has shrunk", () => skipOverlay.CurrentBarExtent < first);
        }

        [Test]
        public void TestPressingSpaceSkipsTheGap()
        {
            pushGameplay(skipMap());

            AddUntilStep("skip overlay shown", () => player.SkipOverlayVisible);

            AddStep("press space", () =>
            {
                input.PressKey(Key.Space);
                input.ReleaseKey(Key.Space);
            });

            AddAssert("time jumped to just before the object's approach",
                () => player.GameplayTime > 19000);
            AddAssert("skip overlay is gone", () => !player.SkipOverlayVisible);

            // Proves the skip actually lands somewhere the map can carry on
            // from, rather than just moving the clock: the object it skipped
            // up to still spawns, gets auto-missed, and the map finishes —
            // now only a preempt's worth of real time away instead of 20s.
            AddUntilStep("gameplay still completes", () => player.Completed);
            AddAssert("the skipped-to circle was judged", () => player.ScoreState.TotalJudged == 1);
        }

        [Test]
        public void TestClickingTheSkipButtonAlsoSkips()
        {
            pushGameplay(skipMap());

            AddUntilStep("skip overlay shown", () => player.SkipOverlayVisible);

            AddStep("click the skip overlay", () =>
            {
                input.MoveMouseTo(skipOverlay);
                input.Click(MouseButton.Left);
            });

            AddAssert("time jumped forward", () => player.GameplayTime > 19000);
            AddAssert("skip overlay is gone", () => !player.SkipOverlayVisible);
        }

        [Test]
        public void TestSpaceDoesNothingWithoutAnActiveBreak()
        {
            pushGameplay(trivial_gap_map);

            AddWaitStep("wait through the lead-in", 90);

            double before = 0;
            AddStep("record the time and press space", () =>
            {
                before = player.GameplayTime;
                input.PressKey(Key.Space);
                input.ReleaseKey(Key.Space);
            });

            AddAssert("nothing jumped", () => player.GameplayTime < before + 100);
        }

        [Test]
        public void TestHealthDoesNotDrainDuringABreak()
        {
            pushGameplay(skipMap());

            AddUntilStep("skip overlay shown", () => player.SkipOverlayVisible);

            double healthAtBreakStart = 0;
            AddStep("record health", () => healthAtBreakStart = player.HealthState.Health);

            AddWaitStep("let time pass inside the break", 30);

            AddAssert("health hasn't moved", () => player.HealthState.Health == healthAtBreakStart);
        }
    }
}
