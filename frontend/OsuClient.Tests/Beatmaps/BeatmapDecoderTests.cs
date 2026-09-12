using System.Linq;
using NUnit.Framework;
using OsuClient.Game.Beatmaps;
using OsuClient.Game.Beatmaps.HitObjects;

namespace OsuClient.Tests.Beatmaps
{
    /// <summary>
    /// Decoder tests against the exact .osu v14 subset the Python backend emits.
    /// All fixtures are in-memory strings, so these run headless and
    /// deterministically — no game host, no disk, no audio.
    /// </summary>
    [TestFixture]
    public class BeatmapDecoderTests
    {
        private const double tolerance = 1e-6;

        private Beatmap backendMap = null!;

        [SetUp]
        public void SetUp()
        {
            backendMap = BeatmapDecoder.Decode(TestBeatmapFixtures.BackendGenerated);
        }

        // ------------------------------------------------------------------
        // Header and sections
        // ------------------------------------------------------------------

        [Test]
        public void FormatVersionIsRead()
        {
            Assert.That(backendMap.FormatVersion, Is.EqualTo(14));
        }

        [Test]
        public void GeneralSectionIsDecoded()
        {
            Assert.Multiple(() =>
            {
                Assert.That(backendMap.General.AudioFilename, Is.EqualTo("audio.mp3"));
                Assert.That(backendMap.General.AudioLeadIn, Is.EqualTo(0));
                Assert.That(backendMap.General.PreviewTime, Is.EqualTo(-1));
                Assert.That(backendMap.General.SampleSet, Is.EqualTo("Normal"));
                Assert.That(backendMap.General.StackLeniency, Is.EqualTo(0.7).Within(tolerance));
                Assert.That(backendMap.General.Mode, Is.EqualTo(0));
            });
        }

        [Test]
        public void MetadataSectionIsDecoded()
        {
            Assert.Multiple(() =>
            {
                Assert.That(backendMap.Metadata.Title, Is.EqualTo("Test Song"));
                Assert.That(backendMap.Metadata.TitleUnicode, Is.EqualTo("Test Song"));
                Assert.That(backendMap.Metadata.Artist, Is.EqualTo("Test Artist"));
                Assert.That(backendMap.Metadata.ArtistUnicode, Is.EqualTo("Test Artist"));
                Assert.That(backendMap.Metadata.Creator, Is.EqualTo("osu-dsp-generator"));
                Assert.That(backendMap.Metadata.Version, Is.EqualTo("Hard"));
                Assert.That(backendMap.Metadata.Source, Is.Empty);
                Assert.That(backendMap.Metadata.Tags, Is.EqualTo("dsp generated procedural"));
                Assert.That(backendMap.Metadata.BeatmapID, Is.EqualTo(0));
                Assert.That(backendMap.Metadata.BeatmapSetID, Is.EqualTo(-1));
            });
        }

        [Test]
        public void DifficultySectionIsDecoded()
        {
            Assert.Multiple(() =>
            {
                Assert.That(backendMap.Difficulty.HPDrainRate, Is.EqualTo(5.0).Within(tolerance));
                Assert.That(backendMap.Difficulty.CircleSize, Is.EqualTo(4.0).Within(tolerance));
                Assert.That(backendMap.Difficulty.OverallDifficulty, Is.EqualTo(7.0).Within(tolerance));
                Assert.That(backendMap.Difficulty.ApproachRate, Is.EqualTo(8.5).Within(tolerance));
                Assert.That(backendMap.Difficulty.SliderMultiplier, Is.EqualTo(1.4).Within(tolerance));
                Assert.That(backendMap.Difficulty.SliderTickRate, Is.EqualTo(1).Within(tolerance));
            });
        }

        [Test]
        public void CircleRadiusMatchesTheBackendFormula()
        {
            // radius = 54.4 - 4.48 * CS, mirroring src/mapping/hit_object.py
            Assert.That(backendMap.Difficulty.CircleRadius, Is.EqualTo(36.48).Within(1e-9));
        }

        [Test]
        public void EditorAndEventsSectionsAreSkippedWithoutError()
        {
            // The fixture carries both, including the // comment lines in [Events].
            Assert.That(backendMap.HitObjects, Has.Count.EqualTo(6));
        }

        // ------------------------------------------------------------------
        // Timing
        // ------------------------------------------------------------------

        [Test]
        public void SingleUninheritedTimingPointIsDecoded()
        {
            Assert.That(backendMap.TimingPoints, Has.Count.EqualTo(1));

            var point = backendMap.TimingPoints[0];

            Assert.Multiple(() =>
            {
                Assert.That(point.Time, Is.EqualTo(123).Within(tolerance));
                Assert.That(point.BeatLength, Is.EqualTo(468.75).Within(tolerance));
                Assert.That(point.Meter, Is.EqualTo(4));
                Assert.That(point.SampleSet, Is.EqualTo(1));
                Assert.That(point.SampleIndex, Is.EqualTo(0));
                Assert.That(point.Volume, Is.EqualTo(70));
                Assert.That(point.Uninherited, Is.True);
                Assert.That(point.Effects, Is.EqualTo(0));
            });
        }

        [Test]
        public void BpmIsDerivedFromBeatLength()
        {
            Assert.That(backendMap.BPM, Is.EqualTo(128).Within(1e-9));
            Assert.That(backendMap.PrimaryTimingPoint, Is.SameAs(backendMap.TimingPoints[0]));
        }

        [Test]
        public void BeatLengthAppliesBeforeAndAfterTheTimingPoint()
        {
            // Only one red line, so it governs the whole map — including times
            // before its own offset.
            Assert.That(backendMap.BeatLengthAt(0), Is.EqualTo(468.75).Within(tolerance));
            Assert.That(backendMap.BeatLengthAt(99999), Is.EqualTo(468.75).Within(tolerance));
        }

        [Test]
        public void SliderVelocityIsOneWithoutGreenLines()
        {
            Assert.That(backendMap.SliderVelocityAt(2000), Is.EqualTo(1).Within(tolerance));
        }

        [Test]
        public void InheritedTimingPointEncodesSliderVelocity()
        {
            // -50 encodes SV x2, and a later red line resets it back to x1.
            var map = BeatmapDecoder.Decode(
                "osu file format v14\n"
                + "[TimingPoints]\n"
                + "0,500,4,1,0,70,1,0\n"
                + "1000,-50,4,1,0,70,0,0\n"
                + "2000,500,4,1,0,70,1,0\n");

            Assert.Multiple(() =>
            {
                Assert.That(map.TimingPoints, Has.Count.EqualTo(3));
                Assert.That(map.SliderVelocityAt(500), Is.EqualTo(1).Within(tolerance));
                Assert.That(map.SliderVelocityAt(1500), Is.EqualTo(2).Within(tolerance));
                Assert.That(map.SliderVelocityAt(2500), Is.EqualTo(1).Within(tolerance));
            });
        }

        // ------------------------------------------------------------------
        // Hit objects
        // ------------------------------------------------------------------

        [Test]
        public void AllSixObjectsAreDecodedInTimeOrder()
        {
            Assert.That(backendMap.HitObjects, Has.Count.EqualTo(6));
            Assert.That(backendMap.HitObjects.Select(h => h.StartTime),
                Is.EqualTo(new double[] { 500, 969, 1438, 2000, 3500, 8000 }));
        }

        [Test]
        public void ObjectKindsMatchTheTypeBitfield()
        {
            Assert.That(backendMap.HitObjects.Select(h => h.GetType()), Is.EqualTo(new[]
            {
                typeof(HitCircleData),
                typeof(HitCircleData),
                typeof(SliderData),
                typeof(SliderData),
                typeof(SpinnerData),
                typeof(HitCircleData),
            }));
        }

        [Test]
        public void CircleIsDecoded()
        {
            var circle = (HitCircleData)backendMap.HitObjects[1];

            Assert.Multiple(() =>
            {
                Assert.That(circle.X, Is.EqualTo(180));
                Assert.That(circle.Y, Is.EqualTo(141));
                Assert.That(circle.StartTime, Is.EqualTo(969).Within(tolerance));
                Assert.That(circle.EndTime, Is.EqualTo(circle.StartTime), "a circle is instantaneous");
                Assert.That(circle.Duration, Is.Zero);
                Assert.That(circle.NewCombo, Is.False);
            });
        }

        [Test]
        public void NewComboFlagsMatchTheBackendRule()
        {
            // First object, and the first object after a spinner.
            Assert.That(backendMap.HitObjects.Select(h => h.NewCombo),
                Is.EqualTo(new[] { true, false, false, false, false, true }));
        }

        [Test]
        public void SingleSlideSliderIsDecoded()
        {
            var slider = (SliderData)backendMap.HitObjects[2];

            Assert.Multiple(() =>
            {
                Assert.That(slider.CurveType, Is.EqualTo(SliderCurveType.Linear));
                Assert.That(slider.Slides, Is.EqualTo(1));
                Assert.That(slider.PixelLength, Is.EqualTo(140).Within(tolerance));
                Assert.That(slider.EdgeSounds, Is.EqualTo(new[] { 0, 0 }));

                // The head is prepended, so Path is a complete polyline.
                Assert.That(slider.Path, Has.Count.EqualTo(2));
                Assert.That(slider.Path[0].X, Is.EqualTo(256));
                Assert.That(slider.Path[0].Y, Is.EqualTo(192));
                Assert.That(slider.Path[1].X, Is.EqualTo(320));
                Assert.That(slider.Path[1].Y, Is.EqualTo(220));
            });
        }

        [Test]
        public void SliderDurationIsDerivedFromLengthAndTiming()
        {
            // beats = length * slides / (100 * SliderMultiplier * SV)
            //       = 140 * 1 / (100 * 1.4 * 1) = 1 beat = 468.75 ms
            var slider = (SliderData)backendMap.HitObjects[2];

            Assert.Multiple(() =>
            {
                Assert.That(slider.Duration, Is.EqualTo(468.75).Within(tolerance));
                Assert.That(slider.EndTime, Is.EqualTo(1906.75).Within(tolerance));
                Assert.That(slider.SpanDuration, Is.EqualTo(468.75).Within(tolerance));
            });
        }

        [Test]
        public void RepeatSliderDurationCountsEverySlide()
        {
            // 105 * 2 / (100 * 1.4) = 1.5 beats = 703.125 ms across 2 slides.
            var slider = (SliderData)backendMap.HitObjects[3];

            Assert.Multiple(() =>
            {
                Assert.That(slider.Slides, Is.EqualTo(2));
                Assert.That(slider.PixelLength, Is.EqualTo(105).Within(tolerance));
                Assert.That(slider.Duration, Is.EqualTo(703.125).Within(tolerance));
                Assert.That(slider.EndTime, Is.EqualTo(2703.125).Within(tolerance));
                Assert.That(slider.SpanDuration, Is.EqualTo(351.5625).Within(tolerance));
                Assert.That(slider.EdgeSounds, Is.EqualTo(new[] { 0, 0, 0 }));
            });
        }

        [Test]
        public void SpinnerCarriesItsEndTimeDirectly()
        {
            var spinner = (SpinnerData)backendMap.HitObjects[4];

            Assert.Multiple(() =>
            {
                Assert.That(spinner.StartTime, Is.EqualTo(3500).Within(tolerance));
                Assert.That(spinner.EndTime, Is.EqualTo(5500).Within(tolerance));
                Assert.That(spinner.Duration, Is.EqualTo(2000).Within(tolerance));

                // The backend always centres spinners on the 512x384 playfield.
                Assert.That(spinner.X, Is.EqualTo(256));
                Assert.That(spinner.Y, Is.EqualTo(192));
            });
        }

        [Test]
        public void LastHitObjectTimeUsesObjectEndsNotStarts()
        {
            Assert.That(backendMap.FirstHitObjectTime, Is.EqualTo(500).Within(tolerance));
            Assert.That(backendMap.LastHitObjectTime, Is.EqualTo(8000).Within(tolerance));
        }

        [TestCase(1, false, 0)]
        [TestCase(5, true, 0)]    // circle + new combo
        [TestCase(6, true, 0)]    // slider + new combo
        [TestCase(12, true, 0)]   // spinner + new combo
        [TestCase(37, true, 2)]   // circle + new combo + skip 2 colours (2 << 4)
        public void NewComboAndColourSkipBitsAreParsed(int type, bool newCombo, int skip)
        {
            string record = (type & 8) != 0
                ? $"256,192,1000,{type},0,2000,0:0:0:0:"
                : (type & 2) != 0
                    ? $"100,100,1000,{type},0,L|200:200,1,100.00,0|0,0:0|0:0,0:0:0:0:"
                    : $"100,100,1000,{type},0,0:0:0:0:";

            var map = BeatmapDecoder.Decode(TestBeatmapFixtures.WithHitObject(record));

            Assert.Multiple(() =>
            {
                Assert.That(map.HitObjects[0].NewCombo, Is.EqualTo(newCombo));
                Assert.That(map.HitObjects[0].ComboColourSkip, Is.EqualTo(skip));
            });
        }

        // ------------------------------------------------------------------
        // Format tolerance
        // ------------------------------------------------------------------

        [Test]
        public void KeyValueLinesTolerateSpacesAfterTheColon()
        {
            // The backend writes "Key:Value"; osu!'s own editor writes "Key: Value".
            var map = BeatmapDecoder.Decode(
                "osu file format v14\n[Metadata]\nTitle: Spaced Out \nArtist:  Someone\n");

            Assert.Multiple(() =>
            {
                Assert.That(map.Metadata.Title, Is.EqualTo("Spaced Out"));
                Assert.That(map.Metadata.Artist, Is.EqualTo("Someone"));
            });
        }

        [Test]
        public void ValuesMayContainColons()
        {
            // Split on the FIRST colon only.
            var map = BeatmapDecoder.Decode(
                "osu file format v14\n[Metadata]\nTitle:Song: The Sequel\n");

            Assert.That(map.Metadata.Title, Is.EqualTo("Song: The Sequel"));
        }

        [Test]
        public void CrlfLineEndingsAreHandled()
        {
            var map = BeatmapDecoder.Decode(
                TestBeatmapFixtures.BackendGenerated.Replace("\n", "\r\n"));

            Assert.That(map.HitObjects, Has.Count.EqualTo(6));
            Assert.That(map.BPM, Is.EqualTo(128).Within(1e-9));
        }

        [Test]
        public void Utf8ByteOrderMarkIsStripped()
        {
            var map = BeatmapDecoder.Decode("﻿" + TestBeatmapFixtures.BackendGenerated);

            Assert.That(map.FormatVersion, Is.EqualTo(14));
        }

        [Test]
        public void BlankLinesAndCommentsAreIgnored()
        {
            var map = BeatmapDecoder.Decode(
                "osu file format v14\n\n\n// a comment\n[Metadata]\n\nTitle:X\n// another\n");

            Assert.That(map.Metadata.Title, Is.EqualTo("X"));
        }

        [Test]
        public void UnknownSectionsAreIgnored()
        {
            var map = BeatmapDecoder.Decode(
                "osu file format v14\n[Colours]\nCombo1 : 255,0,0\n[Metadata]\nTitle:X\n");

            Assert.That(map.Metadata.Title, Is.EqualTo("X"));
        }

        [Test]
        public void HeaderOnlyFileDecodesToAnEmptyMap()
        {
            var map = BeatmapDecoder.Decode(TestBeatmapFixtures.HeaderOnly);

            Assert.Multiple(() =>
            {
                Assert.That(map.HitObjects, Is.Empty);
                Assert.That(map.TimingPoints, Is.Empty);
                Assert.That(map.PrimaryTimingPoint, Is.Null);
                Assert.That(map.BPM, Is.Zero);
                Assert.That(map.FirstHitObjectTime, Is.Zero);
                Assert.That(map.LastHitObjectTime, Is.Zero);
            });
        }

        [Test]
        public void OutOfOrderRecordsAreSortedByTime()
        {
            var map = BeatmapDecoder.Decode(
                "osu file format v14\n[TimingPoints]\n0,500,4,1,0,70,1,0\n[HitObjects]\n"
                + "10,10,3000,1,0,0:0:0:0:\n"
                + "20,20,1000,1,0,0:0:0:0:\n"
                + "30,30,2000,1,0,0:0:0:0:\n");

            Assert.That(map.HitObjects.Select(h => h.StartTime),
                Is.EqualTo(new double[] { 1000, 2000, 3000 }));
        }

        [Test]
        [SetCulture("de-DE")]
        public void NumbersParseUnderACommaDecimalCulture()
        {
            // de-DE uses ',' as the decimal separator. Parsing must stay
            // invariant or "468.750000" would come back as 468750.
            var map = BeatmapDecoder.Decode(TestBeatmapFixtures.BackendGenerated);

            Assert.Multiple(() =>
            {
                Assert.That(map.TimingPoints[0].BeatLength, Is.EqualTo(468.75).Within(tolerance));
                Assert.That(map.Difficulty.ApproachRate, Is.EqualTo(8.5).Within(tolerance));
                Assert.That(map.Difficulty.SliderMultiplier, Is.EqualTo(1.4).Within(tolerance));
                Assert.That(((SliderData)map.HitObjects[2]).PixelLength, Is.EqualTo(140).Within(tolerance));
                Assert.That(map.HitObjects[2].EndTime, Is.EqualTo(1906.75).Within(tolerance));
            });
        }

        // ------------------------------------------------------------------
        // Malformed input
        // ------------------------------------------------------------------

        [Test]
        public void MissingHeaderIsRejected()
        {
            Assert.Throws<BeatmapDecodeException>(() => BeatmapDecoder.Decode("[Metadata]\nTitle:X\n"));
        }

        [Test]
        public void EmptyFileIsRejected()
        {
            Assert.Throws<BeatmapDecodeException>(() => BeatmapDecoder.Decode(string.Empty));
        }

        [Test]
        public void TypeFieldWithNoKindBitIsRejected()
        {
            Assert.Throws<BeatmapDecodeException>(() =>
                BeatmapDecoder.Decode(TestBeatmapFixtures.WithHitObject("100,100,1000,4,0,0:0:0:0:")));
        }

        [Test]
        public void ManiaHoldNoteIsRejected()
        {
            Assert.Throws<BeatmapDecodeException>(() =>
                BeatmapDecoder.Decode(TestBeatmapFixtures.WithHitObject("100,100,1000,128,0,2000:0:0:0:0:")));
        }

        [Test]
        public void SliderWithoutAnchorsIsRejected()
        {
            Assert.Throws<BeatmapDecodeException>(() =>
                BeatmapDecoder.Decode(TestBeatmapFixtures.WithHitObject(
                    "100,100,1000,2,0,L,1,100.00,0|0,0:0|0:0,0:0:0:0:")));
        }

        [Test]
        public void SliderWithTruncatedRecordIsRejected()
        {
            Assert.Throws<BeatmapDecodeException>(() =>
                BeatmapDecoder.Decode(TestBeatmapFixtures.WithHitObject("100,100,1000,2,0,L|200:200")));
        }

        [Test]
        public void SliderWithZeroSlidesIsRejected()
        {
            Assert.Throws<BeatmapDecodeException>(() =>
                BeatmapDecoder.Decode(TestBeatmapFixtures.WithHitObject(
                    "100,100,1000,2,0,L|200:200,0,100.00,0|0,0:0|0:0,0:0:0:0:")));
        }

        [Test]
        public void SliderWithUnknownCurveTypeIsRejected()
        {
            Assert.Throws<BeatmapDecodeException>(() =>
                BeatmapDecoder.Decode(TestBeatmapFixtures.WithHitObject(
                    "100,100,1000,2,0,Z|200:200,1,100.00,0|0,0:0|0:0,0:0:0:0:")));
        }

        [Test]
        public void SpinnerEndingBeforeItStartsIsRejected()
        {
            Assert.Throws<BeatmapDecodeException>(() =>
                BeatmapDecoder.Decode(TestBeatmapFixtures.WithHitObject("256,192,5000,8,0,1000,0:0:0:0:")));
        }

        [Test]
        public void NonNumericFieldIsRejected()
        {
            Assert.Throws<BeatmapDecodeException>(() =>
                BeatmapDecoder.Decode(TestBeatmapFixtures.WithHitObject("100,abc,1000,1,0,0:0:0:0:")));
        }

        [Test]
        public void DecodeErrorReportsTheLineNumber()
        {
            var exception = Assert.Throws<BeatmapDecodeException>(() =>
                BeatmapDecoder.Decode(TestBeatmapFixtures.WithHitObject("100,abc,1000,1,0,0:0:0:0:")));

            Assert.That(exception!.Message, Does.Contain("line "));
        }
    }
}
