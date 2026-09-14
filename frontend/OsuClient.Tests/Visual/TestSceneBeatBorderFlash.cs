using NUnit.Framework;
using osu.Framework.Graphics;
using OsuClient.Game.Beatmaps;
using OsuClient.Game.Screens.Gameplay.HUD;

namespace OsuClient.Tests.Visual
{
    /// <summary>
    /// The beat border flash's brightness is computed fresh from gameplay
    /// time every frame rather than driven by scheduled transforms (see the
    /// class remarks on <see cref="BeatBorderFlash"/>), so it can be tested
    /// the same direct way as the rest of the pure timing maths in this
    /// project — set a time, read the result — without needing real frames to
    /// pass in between.
    /// </summary>
    [TestFixture]
    public partial class TestSceneBeatBorderFlash : osu.Framework.Testing.TestScene
    {
        // 120 BPM (500ms/beat) starting at 1000ms, with hit objects spanning
        // 2000-8000ms so the playable-span envelope has clear inside/outside
        // regions to test against.
        private const string map =
            """
            osu file format v14

            [General]
            AudioFilename:audio.mp3

            [Metadata]
            Version:Test

            [Difficulty]
            HPDrainRate:5
            CircleSize:4
            OverallDifficulty:8
            ApproachRate:9
            SliderMultiplier:1.4
            SliderTickRate:1

            [TimingPoints]
            1000,500,4,1,0,70,1,0

            [HitObjects]
            100,100,2000,5,0,0:0:0:0:
            200,200,8000,5,0,0:0:0:0:
            """;

        private BeatBorderFlash flash = null!;

        private void createFlash()
        {
            AddStep("create flash", () =>
            {
                var beatmap = BeatmapDecoder.Decode(map);

                Child = flash = new BeatBorderFlash(beatmap, beatmap.Difficulty, reach: 29f)
                {
                    RelativeSizeAxes = Axes.Both,
                };
            });

            AddUntilStep("loaded", () => flash.IsLoaded);
        }

        [Test]
        public void TestBrightnessPeaksExactlyOnTheBeat()
        {
            createFlash();

            // Beats land at 1000, 1500, 2000, 2500... — well inside the
            // playable span (2000-8000), so the envelope is fully open here.
            AddStep("set time to a beat", () => flash.SetTime(3000));

            AddAssert("at (or extremely close to) full brightness",
                () => flash.Brightness > 0.98f * peakAlpha());
        }

        [Test]
        public void TestBrightnessFallsBetweenBeats()
        {
            createFlash();

            AddStep("set time to a beat", () => flash.SetTime(3000));
            float onBeat = 0;
            AddStep("read on-beat brightness", () => onBeat = flash.Brightness);

            // Exactly halfway to the next beat (3250ms): as far from any beat
            // as gameplay time ever gets.
            AddStep("set time to halfway between beats", () => flash.SetTime(3250));

            AddAssert("far dimmer than on the beat", () => flash.Brightness < onBeat * 0.05f);
        }

        [Test]
        public void TestBrightnessIsSymmetricAroundTheBeat()
        {
            createFlash();

            AddStep("set time just before a beat", () => flash.SetTime(2980));
            float before = 0;
            AddStep("read it", () => before = flash.Brightness);

            AddStep("set time just after the same beat", () => flash.SetTime(3020));

            AddAssert("early and late sides of the beat match",
                () => System.Math.Abs(flash.Brightness - before) < 0.01f);
        }

        [Test]
        public void TestSilentBeforeTheFirstHitObject()
        {
            createFlash();

            // On a beat (1000 + 500*n), but a full second before anything is
            // playable (first object at 2000, envelope fade only 500ms wide).
            AddStep("set time well before the map starts", () => flash.SetTime(1000));

            AddAssert("no brightness yet", () => flash.Brightness == 0f);
        }

        [Test]
        public void TestSilentAfterTheLastHitObject()
        {
            createFlash();

            AddStep("set time well after the map ends", () => flash.SetTime(9000));

            AddAssert("no brightness left", () => flash.Brightness == 0f);
        }

        [Test]
        public void TestNegativeLeadInTimeDoesNotThrow()
        {
            createFlash();

            // Gameplay time is negative throughout the lead-in, before the
            // timing point's own offset — this is what exercises the modulo
            // wraparound in BeatBorderFlash.SetTime.
            AddStep("set a negative gameplay time", () => flash.SetTime(-800));

            AddAssert("still silent this early, and nothing threw",
                () => flash.Brightness == 0f);
        }

        [Test]
        public void TestDisablingSilencesTheFlashEvenOnTheBeat()
        {
            createFlash();

            AddStep("disable", () => flash.Enabled = false);
            AddStep("set time to a beat", () => flash.SetTime(3000));

            AddAssert("no brightness while disabled", () => flash.Brightness == 0f);
        }

        /// <summary>Reproduces the constructor's own OD-based peak brightness for comparison.</summary>
        private static float peakAlpha() => 0.21f + 0.21f * 0.8f;
    }
}
