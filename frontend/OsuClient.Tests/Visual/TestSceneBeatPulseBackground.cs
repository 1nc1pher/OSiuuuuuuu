using NUnit.Framework;
using osu.Framework.Graphics;
using OsuClient.Game.Beatmaps;
using OsuClient.Game.Screens.Gameplay.HUD;

namespace OsuClient.Tests.Visual
{
    /// <summary>
    /// Same direct set-a-time-read-the-result approach as
    /// <see cref="TestSceneBeatBorderFlash"/> — brightness there, scale here —
    /// since both are driven by the same pure <see cref="OsuClient.Game.Screens.Gameplay.BeatPulse"/>
    /// maths every frame rather than by scheduled transforms.
    /// </summary>
    [TestFixture]
    public partial class TestSceneBeatPulseBackground : osu.Framework.Testing.TestScene
    {
        // 120 BPM (500ms/beat) starting at 1000ms, with hit objects spanning
        // 2000-8000ms.
        private const string map =
            """
            osu file format v14

            [General]
            AudioFilename:audio.mp3

            [Metadata]
            Version:Test

            [Difficulty]
            OverallDifficulty:5

            [TimingPoints]
            1000,500,4,1,0,70,1,0

            [HitObjects]
            100,100,2000,5,0,0:0:0:0:
            200,200,8000,5,0,0:0:0:0:
            """;

        private BeatPulseBackground background = null!;

        private void createBackground()
        {
            AddStep("create background", () =>
            {
                var beatmap = BeatmapDecoder.Decode(map);

                Child = background = new BeatPulseBackground(beatmap, null)
                {
                    RelativeSizeAxes = Axes.Both,
                };
            });

            AddUntilStep("loaded", () => background.IsLoaded);
        }

        [Test]
        public void TestScaleIsAtItsLargestExactlyOnTheBeat()
        {
            createBackground();

            AddStep("set time to a beat", () => background.SetTime(3000));

            AddAssert("scaled up noticeably from resting size",
                () => background.CurrentScale > 1.03f);
        }

        [Test]
        public void TestScaleRestsAtOneBetweenBeats()
        {
            createBackground();

            AddStep("set time halfway between beats", () => background.SetTime(3250));

            AddAssert("back to its resting size", () => System.Math.Abs(background.CurrentScale - 1f) < 0.001f);
        }

        [Test]
        public void TestScaleNeverDropsBelowOne()
        {
            createBackground();

            // Sample all across a beat cycle — the pulse should only ever add
            // to the resting scale, never subtract from it (which would tear
            // a gap at the background's own edge).
            for (double t = 3000; t <= 3500; t += 50)
            {
                double time = t;
                AddStep($"set time to {time}", () => background.SetTime(time));
                AddAssert($"scale at or above 1 at {time}", () => background.CurrentScale >= 1f);
            }
        }

        [Test]
        public void TestDisablingHoldsScaleAtOneEvenOnTheBeat()
        {
            createBackground();

            AddStep("disable", () => background.Enabled = false);
            AddStep("set time to a beat", () => background.SetTime(3000));

            AddAssert("resting size while disabled",
                () => System.Math.Abs(background.CurrentScale - 1f) < 0.001f);
        }

        [Test]
        public void TestSilentBeforeThePlayableSpanBegins()
        {
            createBackground();

            AddStep("set time well before the map starts", () => background.SetTime(1000));

            AddAssert("resting size before anything is playable",
                () => System.Math.Abs(background.CurrentScale - 1f) < 0.001f);
        }
    }
}
