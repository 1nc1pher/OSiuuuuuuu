using System.Linq;
using NUnit.Framework;
using OsuClient.Game.Screens.Generation;

namespace OsuClient.Tests.Generation
{
    /// <summary>
    /// Parsing the backend's stdout into stage lamps.
    ///
    /// <para>
    /// The strings below are <b>copied verbatim from <c>src/main.py</c>'s
    /// <c>report()</c> calls</b>, which is the whole point of this file. The
    /// coupling is real and it fails quietly: change the wording in Python and
    /// an unrecognised line is simply treated as log output, so the lamps stop
    /// advancing and nothing anywhere says why. These tests are what turns
    /// that into a red build.
    /// </para>
    ///
    /// <para>
    /// The other half is that the panel must not <i>over</i>-read its input.
    /// The backend prints a good deal after the last stage — the beatmap
    /// folder, per-tier counts, file sizes — and none of it should move a
    /// lamp.
    /// </para>
    /// </summary>
    [TestFixture]
    public class PipelineProgressTests
    {
        // --- verbatim from src/main.py ---------------------------------
        private const string loading = "Loading golden.mp3";
        private const string mapping_first = "[1/5] Analysing and mapping Easy";
        private const string mapping_middle = "[3/5] Analysing and mapping Hard";
        private const string writing = "Writing beatmap files";
        private const string analysing = "Writing analysis data";

        private static PipelineProgress Run(params string[] lines)
        {
            var progress = new PipelineProgress();

            foreach (string line in lines)
                progress.Apply(line);

            return progress;
        }

        // ------------------------------------------------------------------
        // The five lines the backend actually emits
        // ------------------------------------------------------------------

        [Test]
        public void TestStartsWaitingBeforeAnythingIsSaid()
        {
            var progress = new PipelineProgress();

            Assert.That(progress.Stage, Is.EqualTo(PipelineStage.Waiting));
            Assert.That(progress.HasTierProgress, Is.False);
        }

        [Test]
        public void TestReadsTheLoadingLineAndItsFile()
        {
            var progress = Run(loading);

            Assert.That(progress.Stage, Is.EqualTo(PipelineStage.Loading));
            Assert.That(progress.SourceFile, Is.EqualTo("golden.mp3"));
        }

        [Test]
        public void TestReadsTheTierCounterOutOfTheMappingLine()
        {
            var progress = Run(loading, mapping_middle);

            Assert.That(progress.Stage, Is.EqualTo(PipelineStage.Mapping));
            Assert.That(progress.TierIndex, Is.EqualTo(3));
            Assert.That(progress.TierCount, Is.EqualTo(5));
            Assert.That(progress.TierName, Is.EqualTo("Hard"));
            Assert.That(progress.HasTierProgress, Is.True);
        }

        [Test]
        public void TestTheTierCounterAdvances()
        {
            var progress = Run(loading, mapping_first, mapping_middle);

            Assert.That(progress.TierIndex, Is.EqualTo(3));
            Assert.That(progress.TierName, Is.EqualTo("Hard"));
        }

        [Test]
        public void TestReadsTheWritingStages()
        {
            Assert.That(Run(loading, writing).Stage, Is.EqualTo(PipelineStage.Writing));
            Assert.That(Run(loading, writing, analysing).Stage, Is.EqualTo(PipelineStage.Analysing));
        }

        [Test]
        public void TestAFullRunEndsOnTheAnalysisStage()
        {
            var progress = Run(loading, mapping_first, mapping_middle,
                               "[5/5] Analysing and mapping Expert", writing, analysing);

            Assert.That(progress.Stage, Is.EqualTo(PipelineStage.Analysing));
            Assert.That(progress.TierIndex, Is.EqualTo(5));
        }

        [Test]
        public void TestEveryRecognisedLineReportsAChange()
        {
            var progress = new PipelineProgress();

            foreach (string line in new[] { loading, mapping_first, writing, analysing })
                Assert.That(progress.Apply(line), Is.True, line);
        }

        // ------------------------------------------------------------------
        // Lines that must NOT move a lamp
        // ------------------------------------------------------------------

        [Test]
        public void TestTheRunsClosingReportIsIgnored()
        {
            // Everything main.py prints after the last stage. None of it is a
            // stage, and a parser loose enough to match "Analysis data:" on
            // the word "Analysis" would jump the lamps at the wrong moment.
            var progress = Run(loading, mapping_first);

            foreach (string line in new[]
            {
                @"Beatmap folder: data/output/unknown artist - golden",
                "  [Easy   ] 123.02 BPM    94 circles    10 sliders     6 spinners",
                @"Analysis data:  data/output/unknown artist - golden/analysis.json  (66 KB)",
                @"DSP trace:      data/output/unknown artist - golden/dsp.json  (123 KB)",
                @"Importable set: data/output/unknown artist - golden.osz  (4644 KB)",
                "Drag the .osz onto osu!(lazer) to import and play-test.",
            })
            {
                Assert.That(progress.Apply(line), Is.False, line);
            }

            Assert.That(progress.Stage, Is.EqualTo(PipelineStage.Mapping));
        }

        [Test]
        public void TestWarningsAndBlankLinesAreIgnored()
        {
            var progress = Run(loading);

            Assert.That(progress.Apply(""), Is.False);
            Assert.That(progress.Apply("   "), Is.False);
            Assert.That(progress.Apply(null), Is.False);
            Assert.That(progress.Apply("UserWarning: librosa something"), Is.False);
            Assert.That(progress.Stage, Is.EqualTo(PipelineStage.Loading));
        }

        [Test]
        public void TestAMalformedTierCounterIsNotHalfRead()
        {
            var progress = Run(loading);

            Assert.That(progress.Apply("[x/y] Analysing and mapping Hard"), Is.False);
            Assert.That(progress.TierCount, Is.EqualTo(0));
        }

        // ------------------------------------------------------------------
        // Lamp readings
        // ------------------------------------------------------------------

        [Test]
        public void TestThereAreFourLampStagesNotFive()
        {
            // An earlier sketch had DETECT and MAP as separate lamps. The
            // backend emits one line covering both, so two lamps off one
            // signal would be the panel inventing a stage boundary.
            Assert.That(PipelineProgress.LampStages, Is.EqualTo(new[]
            {
                PipelineStage.Loading,
                PipelineStage.Mapping,
                PipelineStage.Writing,
                PipelineStage.Analysing,
            }));
        }

        [Test]
        public void TestEveryLampStageHasALegend()
        {
            foreach (var stage in PipelineProgress.LampStages)
                Assert.That(PipelineProgress.LegendFor(stage), Is.Not.Empty);
        }

        [Test]
        public void TestPassedStagesLightAndLaterOnesStayDark()
        {
            var progress = Run(loading, mapping_first, writing);

            Assert.That(progress.ReadingFor(PipelineStage.Loading), Is.EqualTo(LampReading.Passed));
            Assert.That(progress.ReadingFor(PipelineStage.Mapping), Is.EqualTo(LampReading.Passed));
            Assert.That(progress.ReadingFor(PipelineStage.Writing), Is.EqualTo(LampReading.Running));
            Assert.That(progress.ReadingFor(PipelineStage.Analysing), Is.EqualTo(LampReading.Pending));
        }

        [Test]
        public void TestNothingIsLitBeforeTheRunSaysAnything()
        {
            var progress = new PipelineProgress();

            Assert.That(PipelineProgress.LampStages.All(
                stage => progress.ReadingFor(stage) == LampReading.Pending), Is.True);
        }

        [Test]
        public void TestFinishingLightsEveryLamp()
        {
            // Including any stage whose line was never seen: reaching the end
            // means they all happened, whether or not each printed.
            var progress = Run(loading, mapping_first);
            progress.Finish();

            Assert.That(PipelineProgress.LampStages.All(
                stage => progress.ReadingFor(stage) == LampReading.Passed), Is.True);
        }
    }
}
