using System.IO;
using System.Linq;
using NUnit.Framework;
using OsuClient.Game.Backend;

namespace OsuClient.Tests.Backend
{
    /// <summary>
    /// Reading the backend's <c>dsp.json</c>.
    ///
    /// Same contract shape as <see cref="AnalysisDataTests"/>, and the same
    /// rule: every unusable input has to come out as null, because this file
    /// is an enrichment. A map generated before it existed is not a broken
    /// map, it is a map with fewer stages — and there are fourteen of those on
    /// disk right now.
    ///
    /// What is different here is that the document is read against a real
    /// generated file rather than a hand-written sample. Field names,
    /// optional blocks and curve alignment are all things a hand-written
    /// sample would agree with by construction and a real file might not.
    /// </summary>
    [TestFixture]
    public class DspDetailTests
    {
        // ------------------------------------------------------------------
        // A real generated document
        // ------------------------------------------------------------------

        [Test]
        public void TestReadsARealGeneratedTrace()
        {
            var detail = DspFixtures.RequireDetail();

            Assert.That(detail.Version, Is.EqualTo(DspDetail.SupportedVersion));
            Assert.That(detail.HasContent, Is.True);
            Assert.That(detail.Frames.SampleRate, Is.EqualTo(22050));
            Assert.That(detail.Frames.HopLength, Is.EqualTo(512));
            Assert.That(detail.Frames.NMels, Is.EqualTo(128));
            Assert.That(detail.Frames.FrameRate, Is.EqualTo(22050.0 / 512).Within(0.001));
        }

        [Test]
        public void TestCarriesEveryCurveTheVisualizationDraws()
        {
            var detail = DspFixtures.RequireDetail();

            Assert.That(detail.Curves.Keys.OrderBy(name => name), Is.EqualTo(new[]
            {
                "bandHigh", "bandLow", "bandMid", "envelope", "flux", "median", "rms",
            }));
        }

        [Test]
        public void TestEveryCurveSharesTheFrameCount()
        {
            // The promise the whole visualization rests on: frame i is the
            // same moment in every curve and in the spectrogram above them.
            var detail = DspFixtures.RequireDetail();

            foreach (var (name, curve) in detail.Curves)
                Assert.That(curve.Decode(), Has.Length.EqualTo(detail.Frames.Count), name);
        }

        [Test]
        public void TestFluxIsExactAndTheDrawnCurvesAreNot()
        {
            // The reason the file is the size it is. Flux is re-run through an
            // algorithm client-side, so it ships unquantised; the band curves
            // are only ever drawn.
            var detail = DspFixtures.RequireDetail();

            Assert.That(detail.Curve("flux")!.Encoding, Is.EqualTo("float32"));
            Assert.That(detail.Curve("median")!.Encoding, Is.EqualTo("uint16"));
            Assert.That(detail.Curve("bandLow")!.Encoding, Is.EqualTo("uint8"));
        }

        [Test]
        public void TestCurvesAreNormalisedAndNonNegative()
        {
            var detail = DspFixtures.RequireDetail();

            foreach (var (name, curve) in detail.Curves)
            {
                float[] values = curve.Decode();

                Assert.That(values.Min(), Is.GreaterThanOrEqualTo(0), name);
                Assert.That(values.Max(), Is.LessThanOrEqualTo(1.001f), name);
            }
        }

        [Test]
        public void TestTheFrameAxisConvertsBothWays()
        {
            var detail = DspFixtures.RequireDetail();

            Assert.That(detail.Frames.TimeAt(0), Is.EqualTo(0));
            Assert.That(detail.Frames.FrameAt(detail.Frames.TimeAt(100)), Is.EqualTo(100));

            // Clamped rather than out of range: a scrub past the end of the
            // track must not index past the end of a curve.
            Assert.That(detail.Frames.FrameAt(-5), Is.EqualTo(0));
            Assert.That(detail.Frames.FrameAt(99999), Is.EqualTo(detail.Frames.Count - 1));
        }

        [Test]
        public void TestBandEdgesMatchTheDetectorsSplit()
        {
            var detail = DspFixtures.RequireDetail();

            Assert.That(detail.Bands["low"], Is.EqualTo(new[] { 20.0, 200.0 }));
            Assert.That(detail.Bands["mid"], Is.EqualTo(new[] { 200.0, 2000.0 }));
            Assert.That(detail.Bands["high"], Is.EqualTo(new[] { 2000.0, 10000.0 }));
        }

        [Test]
        public void TestDetectionCarriesEveryTiersSensitivity()
        {
            var detail = DspFixtures.RequireDetail();

            Assert.That(detail.Detection.MinSpacingSec, Is.GreaterThan(0));
            Assert.That(detail.Detection.Tiers, Is.Not.Empty);

            foreach (var (name, sensitivity) in detail.Detection.Tiers)
            {
                Assert.That(sensitivity.Margin, Is.GreaterThan(0), name);
                Assert.That(sensitivity.Delta, Is.GreaterThan(0), name);
            }
        }

        // ------------------------------------------------------------------
        // The derived threshold
        // ------------------------------------------------------------------

        [Test]
        public void TestTheThresholdIsDerivedFromTheMedian()
        {
            var detail = DspFixtures.RequireDetail();

            float[] median = detail.Samples("median");
            float[] threshold = detail.ThresholdFor(1.5, 0.05);

            Assert.That(threshold, Has.Length.EqualTo(median.Length));

            for (int i = 0; i < median.Length; i++)
                Assert.That(threshold[i], Is.EqualTo(0.05 + median[i] * 1.5).Within(1e-5));
        }

        [Test]
        public void TestAStricterSensitivityGivesAHigherThresholdEverywhere()
        {
            // What the knobs will do, asserted before anything draws them.
            var detail = DspFixtures.RequireDetail();

            float[] loose = detail.ThresholdFor(1.1, 0.03);
            float[] strict = detail.ThresholdFor(2.2, 0.08);

            Assert.That(strict.Zip(loose, (s, l) => s > l).All(higher => higher), Is.True);
        }

        [Test]
        public void TestAKnownTiersThresholdMatchesItsOwnKnobs()
        {
            var detail = DspFixtures.RequireDetail();
            var (tier, sensitivity) = detail.Detection.Tiers.First();

            Assert.That(detail.ThresholdFor(tier),
                        Is.EqualTo(detail.ThresholdFor(sensitivity.Margin, sensitivity.Delta)));
        }

        [Test]
        public void TestAnUnknownTierHasNoThreshold()
        {
            Assert.That(DspFixtures.RequireDetail().ThresholdFor("Lunatic"), Is.Empty);
        }

        // ------------------------------------------------------------------
        // The tempo block
        // ------------------------------------------------------------------

        [Test]
        public void TestTheTempoSearchIsReadable()
        {
            var tempo = DspFixtures.RequireDetail().Tempo;

            Assert.That(tempo, Is.Not.Null);
            Assert.That(tempo!.ChosenBpm, Is.GreaterThan(0));
            Assert.That(tempo.Bpms, Is.Not.Empty);
            Assert.That(tempo.Acf, Has.Count.EqualTo(tempo.Bpms.Count));
            Assert.That(tempo.Prior, Has.Count.EqualTo(tempo.Bpms.Count));
            Assert.That(tempo.Weighted, Has.Count.EqualTo(tempo.Bpms.Count));
            Assert.That(tempo.PhaseScores, Is.Not.Empty);
        }

        [Test]
        public void TestTheOctaveCandidatesAreReadableAndOneWins()
        {
            var tempo = DspFixtures.RequireDetail().Tempo!;

            Assert.That(tempo.OctaveCandidates.Select(c => c.Factor),
                        Is.EqualTo(new[] { 0.5, 1.0, 2.0 }));

            Assert.That(tempo.OctaveCandidates.Count(c => c.Chosen), Is.EqualTo(1));
            Assert.That(tempo.Winner, Is.Not.Null);
            Assert.That(tempo.Winner!.Bpm, Is.EqualTo(tempo.ChosenBpm).Within(0.02));
        }

        [Test]
        public void TestTheSearchBandSitsInsideTheDrawnRange()
        {
            var tempo = DspFixtures.RequireDetail().Tempo!;

            Assert.That(tempo.LagMin, Is.LessThanOrEqualTo(tempo.SearchLagMin));
            Assert.That(tempo.LagMax, Is.GreaterThanOrEqualTo(tempo.SearchLagMax));
        }

        // ------------------------------------------------------------------
        // The tier funnels
        // ------------------------------------------------------------------

        [Test]
        public void TestEveryTiersFunnelNarrows()
        {
            var tiers = DspFixtures.RequireDetail().Tiers;

            Assert.That(tiers, Is.Not.Null);

            foreach (var (name, tier) in tiers!)
            {
                Assert.That(tier.Funnel.Stages, Is.Ordered.Descending, name);
                Assert.That(tier.Funnel.Detected, Is.GreaterThan(0), name);
            }
        }

        [Test]
        public void TestTierPresetsAreReadable()
        {
            var tier = DspFixtures.RequireDetail().Tier("Easy");

            Assert.That(tier, Is.Not.Null);
            Assert.That(tier!.Preset.SnapDivision, Is.EqualTo(1));
            Assert.That(tier.Preset.MinSpacingBeats, Is.GreaterThan(0));
            Assert.That(tier.Preset.CircleSize, Is.GreaterThan(0));
            Assert.That(tier.SnapDeltasMs, Has.Count.EqualTo(tier.Funnel.AfterSpacing));
        }

        [Test]
        public void TestTheSustainTraceIsOnTheSourceTierOnly()
        {
            var detail = DspFixtures.RequireDetail();

            Assert.That(detail.Tier("Expert")!.Sustain, Is.Not.Empty);
            Assert.That(detail.Tier("Easy")!.Sustain, Is.Empty);
        }

        [Test]
        public void TestTheSustainTraceCarriesTheNumbersBehindEachDecision()
        {
            var sustain = DspFixtures.RequireDetail().Tier("Expert")!.Sustain;

            foreach (var decision in sustain)
            {
                Assert.That(decision.Became, Is.Not.Empty);
                Assert.That(decision.GapBeats, Is.GreaterThan(0));
                Assert.That(decision.Sustain, Is.InRange(0, 1));
                Assert.That(decision.Run, Is.GreaterThanOrEqualTo(1));
            }
        }

        [Test]
        public void TestTheSustainTraceShowsTheRuleFailingAsWellAsSucceeding()
        {
            // A panel that only ever shows the test passing does not show a
            // test. The near-misses — a long clean gap that stayed a circle
            // because the sound had already decayed — are the point.
            var sustain = DspFixtures.RequireDetail().Tier("Expert")!.Sustain;

            Assert.That(sustain.Any(d => d.Became == "circle"), Is.True);
            Assert.That(sustain.Any(d => d.Became != "circle"), Is.True);
        }

        // ------------------------------------------------------------------
        // Everything unusable comes out as null
        // ------------------------------------------------------------------

        [Test]
        public void TestAMissingFileIsNull()
        {
            Assert.That(DspDetail.LoadFromFolder(Path.GetTempPath()), Is.Null);
            Assert.That(DspDetail.LoadFromFolder(null), Is.Null);
            Assert.That(DspDetail.LoadFromFolder("   "), Is.Null);
            Assert.That(DspDetail.Load(Path.Combine(Path.GetTempPath(), "nope.json")), Is.Null);
        }

        [Test]
        public void TestMalformedJsonIsNull()
        {
            Assert.That(DspDetail.Parse("{ not json"), Is.Null);
            Assert.That(DspDetail.Parse(string.Empty), Is.Null);
        }

        [Test]
        public void TestAFutureVersionIsNull()
        {
            // A later backend writing a document this build doesn't know is
            // the same outcome as no document: fewer stages, no crash.
            string json = File.ReadAllText(Path.Combine(DspFixtures.BeatmapFolder, DspDetail.FileName))
                              .Replace("\"version\":1", "\"version\":2");

            Assert.That(DspDetail.Parse(json), Is.Null);
        }

        [Test]
        public void TestAnAnalysisFileHandedHereByMistakeIsNull()
        {
            string json = File.ReadAllText(Path.Combine(DspFixtures.BeatmapFolder, AnalysisData.FileName));

            Assert.That(DspDetail.Parse(json), Is.Null);
        }

        [Test]
        public void TestADocumentWithACorruptCurveIsNull()
        {
            // Rejected whole rather than per-curve: a half-written file from
            // an interrupted run should read as "no trace for this map", not
            // as a screen of traces where one stops partway through the song.
            string json = File.ReadAllText(Path.Combine(DspFixtures.BeatmapFolder, DspDetail.FileName));
            var detail = DspDetail.Parse(json);

            Assert.That(detail, Is.Not.Null);

            string corrupt = json.Replace($"\"count\":{detail!.Frames.Count}",
                                          $"\"count\":{detail.Frames.Count + 7}");

            Assert.That(corrupt, Is.Not.EqualTo(json));
            Assert.That(DspDetail.Parse(corrupt), Is.Null);
        }

        [Test]
        public void TestAskingForSomethingAbsentIsEmptyRatherThanAThrow()
        {
            var detail = DspFixtures.RequireDetail();

            Assert.That(detail.Curve("nosuchcurve"), Is.Null);
            Assert.That(detail.Samples("nosuchcurve"), Is.Empty);
            Assert.That(detail.Tier("Lunatic"), Is.Null);
        }
    }
}
