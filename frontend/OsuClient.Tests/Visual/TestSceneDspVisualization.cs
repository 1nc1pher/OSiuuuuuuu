using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using OsuClient.Game.Backend;
using OsuClient.Game.Screens.Generation;

namespace OsuClient.Tests.Visual
{
    /// <summary>
    /// The four stages of the DSP reveal (FRONTEND_PLAN.md Phase 7), each
    /// driven directly with analysis data rather than through a real
    /// generation — the components are the part worth testing, and running
    /// Python for five tiers per test case would be minutes per run.
    ///
    /// What these check is the behaviour the sequence depends on and that is
    /// easy to get silently wrong: that a stage actually progresses on the
    /// clock, that skipping lands on the finished state rather than a partial
    /// one, and that the thinning rules (which exist because a long song has
    /// thousands of onsets) keep order and stay inside their limits.
    /// </summary>
    [TestFixture]
    public partial class TestSceneDspVisualization : osu.Framework.Testing.TestScene
    {
        private static AnalysisOnset onset(double time, double strength, string band = "mid") =>
            new AnalysisOnset { Time = time, Strength = strength, Band = band };

        private static IReadOnlyList<AnalysisOnset> onsets(int count, double duration = 60)
        {
            var random = new Random(42);

            return Enumerable.Range(0, count)
                             .Select(i => onset(i * duration / count, random.NextDouble()))
                             .ToList();
        }

        private static IReadOnlyList<AnalysisHitObject> hitObjects(int count)
        {
            var random = new Random(7);

            return Enumerable.Range(0, count).Select(i => new AnalysisHitObject
            {
                Kind = i % 9 == 8 ? "spinner" : i % 3 == 0 ? "slider" : "circle",
                Time = i * 0.4,
                X = 60 + random.NextDouble() * 390,
                Y = 50 + random.NextDouble() * 280,
                Strength = random.NextDouble(),
                Band = i % 2 == 0 ? "low" : "high",
                Snap = "1/2",
                EndTime = i % 3 == 0 ? i * 0.4 + 0.3 : null,
            }).ToList();
        }

        private static AnalysisBeatGrid beatGrid(double bpm = 137.85, int beats = 120)
        {
            double period = 60.0 / bpm;

            return new AnalysisBeatGrid
            {
                Bpm = bpm,
                Offset = 0.42,
                Confidence = 0.73,
                BeatTimes = Enumerable.Range(0, beats).Select(i => 0.42 + i * period).ToList(),
            };
        }

        private SpectrogramReveal reveal = null!;

        private void pushSpectrogram(double duration = 60)
        {
            AddStep("add spectrogram", () =>
            {
                // No image path: the backend's PNG isn't needed to exercise
                // the wipe, and a missing one is a case that has to work
                // anyway (an older beatmap, or --no-analysis).
                Child = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding(40),
                    Child = reveal = new SpectrogramReveal(null, duration)
                    {
                        RelativeSizeAxes = Axes.Both,
                    },
                };
            });

            AddUntilStep("loaded", () => reveal.IsLoaded);
        }

        [Test]
        public void TestSpectrogramWipesInOverTime()
        {
            pushSpectrogram();

            AddAssert("starts hidden", () => reveal.Progress == 0);
            AddStep("reveal", () => reveal.Reveal(600));
            AddUntilStep("wipes in", () => reveal.Progress > 0.3f);
            AddUntilStep("finishes", () => reveal.Revealed);
        }

        [Test]
        public void TestSpectrogramSkipsStraightToRevealed()
        {
            pushSpectrogram();

            AddStep("reveal slowly", () => reveal.Reveal(20000));
            AddStep("skip", () => reveal.RevealImmediately());
            AddAssert("fully revealed", () => reveal.Revealed);
        }

        [Test]
        public void TestTimeAxisSpansTheTrack()
        {
            pushSpectrogram(duration: 100);

            // Every later stage places itself with this, so a wrong axis puts
            // sparks, beat lines and objects over the wrong moments.
            AddAssert("start is at the left", () => reveal.TimeToX(0) == 0);
            AddAssert("end is at the right", () => Math.Abs(reveal.TimeToX(100) - reveal.DrawWidth) < 0.01f);
            AddAssert("midpoint is halfway",
                () => Math.Abs(reveal.TimeToX(50) - reveal.DrawWidth / 2) < 0.01f);
            AddAssert("past the end clamps", () => reveal.TimeToX(500) <= reveal.DrawWidth);
        }

        [Test]
        public void TestOnsetSparksAppearInTimeOrder()
        {
            OnsetSparkLayer layer = null!;

            AddStep("add spark layer", () =>
            {
                reveal = new SpectrogramReveal(null, 60) { RelativeSizeAxes = Axes.Both };
                reveal.AddOverlay(layer = new OnsetSparkLayer(onsets(120), reveal));

                Child = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding(40),
                    Child = reveal,
                };
            });

            AddUntilStep("loaded", () => layer.IsLoaded);
            AddAssert("nothing sparked yet", () => layer.SparkCount == 0);

            AddStep("sweep", () => layer.Sweep(600));
            AddUntilStep("sparks appear", () => layer.SparkCount > 10);
            AddUntilStep("all sparked", () => layer.SparkCount == layer.TotalSparks);
        }

        [Test]
        public void TestOnsetSparksSkipToFull()
        {
            OnsetSparkLayer layer = null!;

            AddStep("add spark layer", () =>
            {
                reveal = new SpectrogramReveal(null, 60) { RelativeSizeAxes = Axes.Both };
                reveal.AddOverlay(layer = new OnsetSparkLayer(onsets(80), reveal));

                Child = reveal;
            });

            AddUntilStep("loaded", () => layer.IsLoaded);
            AddStep("sweep slowly", () => layer.Sweep(20000));
            AddStep("skip", () => layer.SweepImmediately());
            AddAssert("every spark placed", () => layer.SparkCount == layer.TotalSparks);
        }

        [Test]
        public void TestThinningKeepsTheStrongestOnsetsInTimeOrder()
        {
            var many = onsets(3000);

            var thinned = OnsetSparkLayer.Thin(many, OnsetSparkLayer.MaxSparks);

            Assert.That(thinned, Has.Count.EqualTo(OnsetSparkLayer.MaxSparks));
            Assert.That(thinned.Select(o => o.Time), Is.Ordered);

            // Thinning by strength has to keep the loud hits — those are the
            // ones that became objects, so dropping them would show a quieter
            // song than the map was made from.
            double weakestKept = thinned.Min(o => o.Strength);
            double strongestDropped = many.Except(thinned).Max(o => o.Strength);

            Assert.That(weakestKept, Is.GreaterThanOrEqualTo(strongestDropped));
        }

        [Test]
        public void TestShortOnsetListsAreNotThinned()
        {
            var few = onsets(20);

            Assert.That(OnsetSparkLayer.Thin(few, OnsetSparkLayer.MaxSparks), Has.Count.EqualTo(20));
        }

        [Test]
        public void TestBeatGridCountsUpToTheTrackedBpm()
        {
            BeatGridReveal grid = null!;

            AddStep("add beat grid", () =>
            {
                reveal = new SpectrogramReveal(null, 60) { RelativeSizeAxes = Axes.Both };
                reveal.AddOverlay(grid = new BeatGridReveal(beatGrid(), reveal));

                Child = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding(40),
                    Child = reveal,
                };
            });

            AddUntilStep("loaded", () => grid.IsLoaded);
            AddAssert("counter starts at zero", () => grid.DisplayedBpm == 0);

            AddStep("sweep", () => grid.Sweep(700));
            AddUntilStep("lines snap in", () => grid.LineCount > 5);
            AddUntilStep("settles on the tracked bpm",
                () => Math.Abs(grid.DisplayedBpm - 137.85) < 0.01);
            AddAssert("every beat drawn", () => grid.LineCount == grid.TotalLines);
        }

        [Test]
        public void TestBeatGridSkipsToTheFinalNumbers()
        {
            BeatGridReveal grid = null!;

            AddStep("add beat grid", () =>
            {
                reveal = new SpectrogramReveal(null, 60) { RelativeSizeAxes = Axes.Both };
                reveal.AddOverlay(grid = new BeatGridReveal(beatGrid(), reveal));

                Child = reveal;
            });

            AddUntilStep("loaded", () => grid.IsLoaded);
            AddStep("sweep slowly", () => grid.Sweep(30000));
            AddStep("skip", () => grid.SweepImmediately());

            AddAssert("shows the real bpm", () => Math.Abs(grid.DisplayedBpm - 137.85) < 0.01);
            AddAssert("whole grid drawn", () => grid.LineCount == grid.TotalLines);
        }

        [Test]
        public void TestBeatLineThinningStaysOnTheBeat()
        {
            // A six-minute track at 175 BPM: over a thousand beats, which
            // across a screen-width panel would be a line per pixel.
            var times = Enumerable.Range(0, 1050).Select(i => i * 60.0 / 175).ToList();

            var thinned = BeatGridReveal.Thin(times, BeatGridReveal.MaxBeatLines);

            Assert.That(thinned.Count, Is.LessThanOrEqualTo(BeatGridReveal.MaxBeatLines));
            Assert.That(thinned.First(), Is.EqualTo(times.First()).Within(1e-9));

            // Evenly spaced, so what's left still reads as a beat grid rather
            // than an arbitrary subset.
            var gaps = thinned.Zip(thinned.Skip(1), (a, b) => b - a).ToList();

            Assert.That(gaps.Max() - gaps.Min(), Is.LessThan(1e-6));
        }

        [Test]
        public void TestAssemblyPlacesObjectsInTimeOrder()
        {
            HitObjectAssemblyPreview preview = null!;

            AddStep("add preview", () =>
            {
                Child = preview = new HitObjectAssemblyPreview(hitObjects(200))
                {
                    RelativeSizeAxes = Axes.Both,
                };
            });

            AddUntilStep("loaded", () => preview.IsLoaded);
            AddAssert("nothing placed yet", () => preview.PlacedCount == 0);

            AddStep("assemble", () => preview.Assemble(800));
            AddUntilStep("objects appear", () => preview.PlacedCount > 20);
            AddUntilStep("all placed", () => preview.Complete);
            AddAssert("counted every object", () => preview.PlacedCount == 200);
        }

        [Test]
        public void TestAssemblySkipsToComplete()
        {
            HitObjectAssemblyPreview preview = null!;

            AddStep("add preview", () =>
            {
                Child = preview = new HitObjectAssemblyPreview(hitObjects(500))
                {
                    RelativeSizeAxes = Axes.Both,
                };
            });

            AddUntilStep("loaded", () => preview.IsLoaded);
            AddStep("assemble slowly", () => preview.Assemble(30000));
            AddStep("skip", () => preview.AssembleImmediately());
            AddAssert("complete", () => preview.Complete);
        }

        [Test]
        public void TestAssemblyHandlesAnEmptyMap()
        {
            // A very short or silent track can place nothing at all; the stage
            // still has to load and report itself finished rather than hang
            // the sequence waiting for an object that never comes.
            HitObjectAssemblyPreview preview = null!;

            AddStep("add empty preview", () =>
            {
                Child = preview = new HitObjectAssemblyPreview(Array.Empty<AnalysisHitObject>())
                {
                    RelativeSizeAxes = Axes.Both,
                };
            });

            AddUntilStep("loaded", () => preview.IsLoaded);
            AddStep("assemble", () => preview.Assemble(200));
            AddAssert("already complete", () => preview.Complete);
        }
    }
}
