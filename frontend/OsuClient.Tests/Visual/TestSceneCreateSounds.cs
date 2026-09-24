using System;
using System.IO;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Testing;
using OsuClient.Game.Audio;
using OsuClient.Game.Backend;
using OsuClient.Game.Beatmaps;
using OsuClient.Game.Screens.Generation;

namespace OsuClient.Tests.Visual
{
    /// <summary>
    /// The create flow's sound cues are driven by what is actually happening:
    /// the pipeline's lamps and counter, each onset, beat and object as the
    /// reveal draws it, the finale, the spines. These check the events the
    /// sounds hang off fire when they should — the sounds themselves are
    /// checked in <see cref="Generation.TapeDeckSoundSynthTests"/>.
    /// </summary>
    [TestFixture]
    public partial class TestSceneCreateSounds : TestScene
    {
        private static readonly string fixture_folder = Path.Combine(
            BeatmapLibrary.FindRepositoryRoot(AppContext.BaseDirectory) ?? string.Empty,
            "frontend", "OsuClient.Tests", "Fixtures");

        [Test]
        public void TestThePipelineReportsStagesAndTiers()
        {
            GenerationProgressPanel panel = null!;
            int stages = 0, tiers = 0;

            AddStep("create panel", () =>
            {
                Child = panel = new GenerationProgressPanel("golden.mp3");
                panel.StageAdvanced = () => stages++;
                panel.TierAdvanced = () => tiers++;
            });

            AddStep("feed a run", () =>
            {
                // Lines copied from a real run of src/main.py.
                foreach (string line in new[]
                         {
                             "Loading golden.mp3",
                             "[1/5] Analysing and mapping Easy",
                             "[2/5] Analysing and mapping Normal",
                             "[3/5] Analysing and mapping Hard",
                             "Writing beatmap files",
                         })
                {
                    panel.AppendLine(line);
                }
            });

            // Loading, mapping, writing: a relay each. Normal and Hard step
            // the counter within mapping.
            AddAssert("a relay per stage", () => stages == 3);
            AddAssert("a counter tick per further tier", () => tiers == 2);
        }

        [TestCase(3, "onsets")]
        [TestCase(6, "beats")]
        [TestCase(9, "objects")]
        public void TestTheRevealReportsWhatItDraws(int stage, string what)
        {
            DspReveal reveal = null!;
            int onsets = 0, beats = 0, objects = 0, stageStarts = 0;

            AddStep("play the reveal to its stage", () =>
            {
                var analysis = AnalysisData.LoadFromFolder(fixture_folder)
                               ?? throw new InvalidOperationException("no analysis fixture");

                Child = reveal = new DspReveal(analysis, DspDetail.LoadFromFolder(fixture_folder),
                                               AnalysisData.FindSpectrogram(fixture_folder))
                {
                    StartStage = stage,
                    HoldAtStartStage = true,
                    StageStarted = () => stageStarts++,
                    OnsetRevealed = _ => onsets++,
                    BeatRevealed = () => beats++,
                    ObjectRevealed = _ => objects++,
                };
            });

            AddUntilStep($"{what} reported as they are drawn", () => what switch
            {
                "onsets" => onsets > 0,
                "beats" => beats > 0,
                _ => objects > 0,
            });

            AddAssert("a cue for every stage passed through", () => stageStarts == stage + 1);
        }

        [Test]
        public void TestTheFinaleAndASkipAreReported()
        {
            DspReveal reveal = null!;
            bool finale = false, skipped = false;

            AddStep("start the reveal", () =>
            {
                var analysis = AnalysisData.LoadFromFolder(fixture_folder)!;

                Child = reveal = new DspReveal(analysis, DspDetail.LoadFromFolder(fixture_folder),
                                               AnalysisData.FindSpectrogram(fixture_folder))
                {
                    FinaleReached = () => finale = true,
                    Skipped = () => skipped = true,
                    AnalyserRequested = () => { },
                };
            });

            AddUntilStep("playing", () => reveal.StageIndex >= 0);
            AddStep("skip", () => reveal.SkipToEnd());

            AddAssert("fast-forward cued", () => skipped);
            AddAssert("ready cued", () => finale);
        }

        [Test]
        public void TestTheTransportHumStartsAndStops()
        {
            TapeDeckSoundPlayer sounds = null!;

            AddStep("create player", () => Child = sounds = new TapeDeckSoundPlayer());
            AddUntilStep("loaded", () => sounds.IsLoaded);

            AddStep("start the tape", () => sounds.StartTransport());
            AddAssert("running", () => sounds.TransportRunning);

            AddStep("stop the tape", () => sounds.StopTransport());
            AddAssert("stopped", () => !sounds.TransportRunning);
        }

        [Test]
        public void TestASpineReportsWhichWayItWent()
        {
            TierSpine spine = null!;
            bool? last = null;

            AddStep("create spine", () =>
            {
                Child = spine = new TierSpine("Easy", 1.5, 0, 5) { Anchor = Anchor.Centre, Origin = Anchor.Centre };
                spine.Toggled += pushedIn => last = pushedIn;
            });

            AddStep("pull it out", () => spine.TriggerClick());
            AddAssert("reported out", () => last == false);

            AddStep("push it back in", () => spine.TriggerClick());
            AddAssert("reported in", () => last == true);
        }
    }
}
