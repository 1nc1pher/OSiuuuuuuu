using System;
using System.IO;
using NUnit.Framework;
using osu.Framework.Graphics;
using OsuClient.Game.Backend;
using OsuClient.Game.Beatmaps;
using OsuClient.Game.Screens.Generation;

namespace OsuClient.Tests.Visual
{
    /// <summary>
    /// The end of the reveal: it is where the player chooses between PLAY and
    /// the analyser, so nothing may carry them past it on a timer — not the
    /// finale itself, and not a stage that was queued before a skip.
    /// </summary>
    [TestFixture]
    public partial class TestSceneDspRevealFinale : osu.Framework.Testing.TestScene
    {
        private static readonly string fixture_folder = Path.Combine(
            BeatmapLibrary.FindRepositoryRoot(AppContext.BaseDirectory) ?? string.Empty,
            "frontend", "OsuClient.Tests", "Fixtures");

        private DspReveal reveal = null!;
        private bool completed;

        private void load(int startStage = 0, bool hold = false)
        {
            AddStep("load reveal", () =>
            {
                completed = false;

                var analysis = AnalysisData.LoadFromFolder(fixture_folder)
                               ?? throw new InvalidOperationException("no analysis fixture");

                Child = reveal = new DspReveal(analysis, DspDetail.LoadFromFolder(fixture_folder),
                                               AnalysisData.FindSpectrogram(fixture_folder))
                {
                    StartStage = startStage,
                    HoldAtStartStage = hold,
                    Completed = () => completed = true,
                    AnalyserRequested = () => { },
                };
            });

            AddUntilStep("playing", () => reveal.StageIndex >= startStage);
        }

        [Test]
        public void TestSkipLandsOnTheFinaleAndStaysThere()
        {
            load();

            AddStep("skip", () => reveal.SkipToEnd());
            AddAssert("on the finale", () => reveal.AtFinale && reveal.StageTitle == "MAP READY");

            // Longer than the stage that was queued when the skip happened.
            AddWaitStep("wait past the queued stage", 40);

            AddAssert("still waiting for a choice", () => reveal.AtFinale && !reveal.Finished && !completed);
        }

        [Test]
        public void TestPlacementIsStepSix()
        {
            int placement = 0;

            load();
            AddStep("find placement", () => placement = reveal.StageCount - 2);

            AddStep("load at placement", () =>
            {
                var analysis = AnalysisData.LoadFromFolder(fixture_folder)!;

                Child = reveal = new DspReveal(analysis, DspDetail.LoadFromFolder(fixture_folder),
                                               AnalysisData.FindSpectrogram(fixture_folder))
                {
                    StartStage = placement,
                    HoldAtStartStage = true,
                };
            });

            AddUntilStep("on placement", () => reveal.StageIndex == placement);
            AddAssert("numbered step 6", () => reveal.StageTitle.StartsWith("STEP 6", StringComparison.Ordinal));
        }
    }
}
