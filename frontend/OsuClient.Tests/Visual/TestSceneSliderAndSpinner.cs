using System;
using System.Collections.Generic;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Lines;
using osu.Framework.Timing;
using OsuClient.Game.Beatmaps;
using OsuClient.Game.Beatmaps.HitObjects;
using OsuClient.Game.Screens.Gameplay;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Tests.Visual
{
    /// <summary>
    /// Slider and spinner behaviour, driven deterministically: the playfield
    /// is parked on a manual clock and the objects are stepped through
    /// gameplay time by hand, so nothing depends on how fast the test host
    /// happens to render frames.
    /// </summary>
    [TestFixture]
    public partial class TestSceneSliderAndSpinner : osu.Framework.Testing.TestScene
    {
        private const double start_time = 1000;
        private const double end_time = 2000;

        private Container playfield = null!;
        private HitResult? result;

        private static BeatmapDifficulty difficulty => new BeatmapDifficulty
        {
            CircleSize = 4,
            OverallDifficulty = 0,
            ApproachRate = 9,
            SliderMultiplier = 1.4,
            SliderTickRate = 4,
        };

        private Vector2 screenSpace(Vector2 playfieldPosition) => playfield.ToScreenSpace(playfieldPosition);

        private void createPlayfield()
        {
            AddStep("create playfield", () =>
            {
                result = null;

                Child = playfield = new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(512, 384),
                    Clock = new FramedClock(new ManualClock { CurrentTime = start_time }),
                };
            });
        }

        // ------------------------------------------------------------------
        // Sliders
        // ------------------------------------------------------------------

        private static SliderData sliderData(int slides = 1)
        {
            var data = new SliderData
            {
                Position = new Vector2(100, 100),
                StartTime = start_time,
                CurveType = SliderCurveType.Linear,
                Path = new[] { new Vector2(100, 100), new Vector2(200, 100) },
                Slides = slides,
                PixelLength = 100,
            };

            data.SetEndTime(end_time);

            return data;
        }

        private DrawableSlider addSlider(int slides = 1)
        {
            var slider = new DrawableSlider(sliderData(slides), difficulty, Color4.White, 1);
            slider.Judged += (_, r) => result = r;

            playfield.Add(slider);

            return slider;
        }

        private static double polylineLength(IReadOnlyList<Vector2> points)
        {
            double total = 0;

            for (int i = 1; i < points.Count; i++)
                total += Vector2.Distance(points[i - 1], points[i]);

            return total;
        }

        [Test]
        public void TestSliderHeldThroughoutIsAGreat()
        {
            createPlayfield();

            DrawableSlider slider = null!;

            AddStep("add slider", () => slider = addSlider());
            AddUntilStep("loaded", () => slider.IsLoaded);

            AddStep("hit the head and hold along the path", () =>
            {
                Assert.That(slider.TryPress(start_time, screenSpace(new Vector2(100, 100))), Is.True,
                    "the head should accept an on-time press");

                // Parked mid-path: the follow circle is wide enough to keep
                // tracking the ball for this slider's whole length.
                for (double time = start_time; time <= end_time + 1; time += 50)
                    slider.UpdateGameplay(time, screenSpace(new Vector2(150, 100)), true);
            });

            AddAssert("slider judged a Great", () => result == HitResult.Great);
        }

        [Test]
        public void TestSliderHeadHitButNotFollowedLosesItsParts()
        {
            createPlayfield();

            DrawableSlider slider = null!;

            AddStep("add slider", () => slider = addSlider());
            AddUntilStep("loaded", () => slider.IsLoaded);

            AddStep("hit the head then let go", () =>
            {
                slider.TryPress(start_time, screenSpace(new Vector2(100, 100)));

                for (double time = start_time; time <= end_time + 1; time += 50)
                    slider.UpdateGameplay(time, screenSpace(new Vector2(150, 100)), false);
            });

            // The head counted, every tick and the tail did not.
            AddAssert("slider judged below a Great",
                () => result != null && result != HitResult.Great && result != HitResult.Miss);
        }

        [Test]
        public void TestSliderIgnoredEntirelyIsAMiss()
        {
            createPlayfield();

            DrawableSlider slider = null!;

            AddStep("add slider", () => slider = addSlider());
            AddUntilStep("loaded", () => slider.IsLoaded);

            AddStep("never touch it", () =>
            {
                for (double time = start_time; time <= end_time + 1; time += 50)
                    slider.UpdateGameplay(time, screenSpace(new Vector2(400, 300)), false);
            });

            AddAssert("slider judged a Miss", () => result == HitResult.Miss);
        }

        [Test]
        public void TestSliderBodySnakesInDuringItsApproach()
        {
            createPlayfield();

            DrawableSlider slider = null!;
            double fadeIn = JudgementProcessor.FadeIn(difficulty.ApproachRate);

            AddStep("add slider", () => slider = addSlider());
            AddUntilStep("loaded", () => slider.IsLoaded);

            AddStep("right as it appears, the body has barely started",
                () => slider.UpdateGameplay(slider.AppearTime, screenSpace(new Vector2(400, 300)), false));
            AddAssert("body length is near zero", () => polylineLength(slider.BodyVertices) < 5);

            AddStep("partway through the fade-in, the body is partially drawn",
                () => slider.UpdateGameplay(slider.AppearTime + fadeIn / 2, screenSpace(new Vector2(400, 300)), false));
            AddAssert("body length is between zero and full",
                () => polylineLength(slider.BodyVertices) is > 20 and < 80);

            AddStep("once the fade-in finishes, the body is fully drawn",
                () => slider.UpdateGameplay(slider.AppearTime + fadeIn, screenSpace(new Vector2(400, 300)), false));
            AddAssert("body reaches its full length", () => polylineLength(slider.BodyVertices) > 95);
        }

        [Test]
        public void TestNonRepeatingSliderHasNoArrows()
        {
            createPlayfield();

            DrawableSlider slider = null!;

            AddStep("add a 1-slide slider", () => slider = addSlider());
            AddUntilStep("loaded", () => slider.IsLoaded);

            AddAssert("no repeat arrows", () => slider.RepeatArrowCount == 0);
        }

        [Test]
        public void TestRepeatingSliderGetsAnArrowPerRepeat()
        {
            createPlayfield();

            DrawableSlider slider = null!;

            AddStep("add a 3-slide slider (2 repeats)", () => slider = addSlider(3));
            AddUntilStep("loaded", () => slider.IsLoaded);

            AddAssert("one arrow per repeat, none on the final tail",
                () => slider.RepeatArrowCount == 2);
        }

        [Test]
        public void TestShortSliderGetsAtLeastOneTickEvenAtTheBackendsRealTickRate()
        {
            // The backend always writes SliderTickRate 1 (difficulty.py).
            // Under the real osu! tick-distance formula alone
            // (100 * multiplier / rate = 140 here), this 100px slider would
            // land zero ticks — exactly the bug tick_density_multiplier
            // exists to fix, so nothing between the head and tail rewards
            // actually tracking the slider.
            var backendDifficulty = new BeatmapDifficulty
            {
                CircleSize = 4,
                OverallDifficulty = 0,
                ApproachRate = 9,
                SliderMultiplier = 1.4,
                SliderTickRate = 1,
            };

            createPlayfield();

            DrawableSlider slider = null!;

            AddStep("add a short slider at the backend's real tick rate", () =>
            {
                slider = new DrawableSlider(sliderData(), backendDifficulty, Color4.White, 1);
                playfield.Add(slider);
            });
            AddUntilStep("loaded", () => slider.IsLoaded);

            AddAssert("at least one tick node", () => slider.TickCount > 0);
            AddAssert("the head alone can no longer nearly match a full completion",
                () => slider.PartsTotal > 2);
        }

        [Test]
        public void TestSliderBodyIsDrawnAlongItsPlayfieldPath()
        {
            // Guards the alignment trick the slider body relies on: a Path
            // sizes itself around its vertices, so it has to be offset by
            // where its own bounding box starts for a vertex to land on the
            // playfield coordinate it was given.
            createPlayfield();

            SmoothPath path = null!;
            var vertex = new Vector2(180, 140);

            AddStep("add a path", () =>
            {
                path = new SmoothPath
                {
                    PathRadius = 20,
                    Vertices = new[] { new Vector2(100, 100), vertex },
                };

                playfield.Add(path);
                path.Position = -path.PositionInBoundingBox(Vector2.Zero);
            });

            AddUntilStep("loaded", () => path.IsLoaded);

            AddAssert("a vertex renders at its playfield coordinate", () =>
            {
                Vector2 rendered = path.ToScreenSpace(path.PositionInBoundingBox(vertex));

                return Vector2.Distance(rendered, screenSpace(vertex)) < 1f;
            });
        }

        // ------------------------------------------------------------------
        // Spinners
        // ------------------------------------------------------------------

        private DrawableSpinner addSpinner()
        {
            var data = new SpinnerData { Position = new Vector2(256, 192), StartTime = start_time };
            data.SetEndTime(end_time);

            var spinner = new DrawableSpinner(data, difficulty, Color4.White);
            spinner.Judged += (_, r) => result = r;

            playfield.Add(spinner);

            return spinner;
        }

        /// <summary>Sweeps the cursor around the spinner centre by the given number of degrees.</summary>
        private void spin(DrawableSpinner spinner, double degrees)
        {
            const double step = 10;
            var centre = new Vector2(256, 192);

            for (double swept = 0; swept <= degrees; swept += step)
            {
                double radians = swept * Math.PI / 180;

                var cursor = centre + new Vector2((float)Math.Cos(radians), (float)Math.Sin(radians)) * 80;

                spinner.UpdateGameplay(start_time + 1, screenSpace(cursor), false);
            }
        }

        [Test]
        public void TestFullySpunSpinnerIsAGreat()
        {
            createPlayfield();

            DrawableSpinner spinner = null!;

            AddStep("add spinner", () => spinner = addSpinner());
            AddUntilStep("loaded", () => spinner.IsLoaded);

            AddStep("spin it plenty", () =>
            {
                // OD 0 over one second needs 1.5 spins; do well past that.
                spin(spinner, 360 * 4);
                spinner.UpdateGameplay(end_time + 1, screenSpace(new Vector2(256, 192)), false);
            });

            AddAssert("spinner judged a Great", () => result == HitResult.Great);
        }

        [Test]
        public void TestUntouchedSpinnerIsAMiss()
        {
            createPlayfield();

            DrawableSpinner spinner = null!;

            AddStep("add spinner", () => spinner = addSpinner());
            AddUntilStep("loaded", () => spinner.IsLoaded);

            AddStep("leave the cursor still", () =>
            {
                spinner.UpdateGameplay(start_time + 1, screenSpace(new Vector2(256, 272)), false);
                spinner.UpdateGameplay(end_time + 1, screenSpace(new Vector2(256, 272)), false);
            });

            AddAssert("spinner judged a Miss", () => result == HitResult.Miss);
        }

        [Test]
        public void TestPartiallySpunSpinnerLandsBetweenMissAndGreat()
        {
            createPlayfield();

            DrawableSpinner spinner = null!;

            AddStep("add spinner", () => spinner = addSpinner());
            AddUntilStep("loaded", () => spinner.IsLoaded);

            AddStep("spin a little", () =>
            {
                spin(spinner, 360);
                spinner.UpdateGameplay(end_time + 1, screenSpace(new Vector2(256, 192)), false);
            });

            // One of the 1.5 required spins is ~67% complete: short of a 50.
            AddAssert("spinner judged a Miss", () => result == HitResult.Miss);
            AddAssert("but progress was recorded", () => spinner.Progress > 0.6 && spinner.Progress < 0.75);
        }

        // ------------------------------------------------------------------
        // Spinner bonus (OD 0 needs 1.5 spins = 540 degrees over this map)
        // ------------------------------------------------------------------

        [Test]
        public void TestSpinningExactlyToTheRequirementAwardsNoBonus()
        {
            createPlayfield();

            DrawableSpinner spinner = null!;

            AddStep("add spinner", () => spinner = addSpinner());
            AddUntilStep("loaded", () => spinner.IsLoaded);

            AddStep("spin exactly the required 540 degrees", () => spin(spinner, 540));

            AddAssert("no bonus yet", () => spinner.BonusSpinsAwarded == 0);
        }

        [Test]
        public void TestOneFullExtraRotationAwardsOneBonus()
        {
            createPlayfield();

            DrawableSpinner spinner = null!;
            int bonusEvents = 0;

            AddStep("add spinner", () =>
            {
                spinner = addSpinner();
                spinner.BonusAwarded += _ => bonusEvents++;
            });
            AddUntilStep("loaded", () => spinner.IsLoaded);

            // 540 required + one full 360 degree rotation past it.
            AddStep("spin past the requirement by one full rotation", () => spin(spinner, 900));

            AddAssert("one bonus spin recorded", () => spinner.BonusSpinsAwarded == 1);
            AddAssert("exactly one bonus event fired", () => bonusEvents == 1);
        }

        [Test]
        public void TestEveryExtraFullRotationAwardsAnotherBonus()
        {
            createPlayfield();

            DrawableSpinner spinner = null!;
            int bonusEvents = 0;

            AddStep("add spinner", () =>
            {
                spinner = addSpinner();
                spinner.BonusAwarded += _ => bonusEvents++;
            });
            AddUntilStep("loaded", () => spinner.IsLoaded);

            // 540 required + three full 360 degree rotations past it.
            AddStep("spin past the requirement by three full rotations", () => spin(spinner, 540 + 360 * 3));

            AddAssert("three bonus spins recorded", () => spinner.BonusSpinsAwarded == 3);
            AddAssert("exactly three bonus events fired, none repeated", () => bonusEvents == 3);
        }

        [Test]
        public void TestPartialExtraRotationAwardsNoBonusYet()
        {
            createPlayfield();

            DrawableSpinner spinner = null!;

            AddStep("add spinner", () => spinner = addSpinner());
            AddUntilStep("loaded", () => spinner.IsLoaded);

            // 540 required + 350 of the next rotation — short of a full one.
            AddStep("spin most, but not all, of an extra rotation", () => spin(spinner, 540 + 350));

            AddAssert("no bonus until the rotation is actually completed",
                () => spinner.BonusSpinsAwarded == 0);
        }
    }
}
