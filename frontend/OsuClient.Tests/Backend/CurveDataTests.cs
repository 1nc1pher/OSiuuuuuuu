using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using OsuClient.Game.Backend;

namespace OsuClient.Tests.Backend
{
    /// <summary>
    /// Decoding the packed curves in <c>dsp.json</c>.
    ///
    /// This is half of a wire format whose other half is
    /// <c>src/export/curve_pack.py</c>, and the two never call each other. So
    /// the tests that matter are the ones driven by
    /// <c>tests/fixtures/curve_pack_reference.json</c> — a file the Python
    /// suite asserts against as well. Either implementation drifting turns
    /// both suites red, which is the only way a cross-language format stays
    /// honest.
    ///
    /// The rest are about a reader's own failure modes: an encoding it
    /// doesn't know, a payload that has been truncated, a count that doesn't
    /// match. All of them have to come out as "no curve", never as a curve
    /// that quietly stops partway through the song.
    /// </summary>
    [TestFixture]
    public class CurveDataTests
    {
        private sealed class ReferenceCase
        {
            public string Encoding { get; set; } = string.Empty;
            public CurveData Packed { get; set; } = new CurveData();
            public double Tolerance { get; set; }
        }

        private sealed class Reference
        {
            public List<double> Values { get; set; } = new List<double>();
            public List<ReferenceCase> Cases { get; set; } = new List<ReferenceCase>();
        }

        private static readonly JsonSerializerOptions options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        private static Reference LoadReference()
        {
            string path = DspFixtures.CurvePackReferencePath;

            if (!File.Exists(path))
            {
                Assert.Ignore("no shared curve reference — regenerate with "
                              + "python tests/fixtures/make_curve_pack_reference.py");
            }

            return JsonSerializer.Deserialize<Reference>(File.ReadAllText(path), options)!;
        }

        // ------------------------------------------------------------------
        // The shared reference
        // ------------------------------------------------------------------

        [Test]
        public void TestDecodesEveryEncodingTheBackendWrites()
        {
            var reference = LoadReference();

            Assert.That(reference.Cases, Is.Not.Empty);

            foreach (var reading in reference.Cases)
            {
                float[] decoded = reading.Packed.Decode();

                Assert.That(decoded, Has.Length.EqualTo(reference.Values.Count), reading.Encoding);

                for (int i = 0; i < decoded.Length; i++)
                {
                    Assert.That(decoded[i], Is.EqualTo(reference.Values[i]).Within(reading.Tolerance),
                                $"{reading.Encoding} sample {i}");
                }
            }
        }

        [Test]
        public void TestCoversTheThreeEncodings()
        {
            // An encoding with no case here is one this reader is never
            // tested against.
            var encodings = LoadReference().Cases.Select(c => c.Encoding).OrderBy(name => name);

            Assert.That(encodings, Is.EqualTo(new[] { "float32", "uint16", "uint8" }));
        }

        [Test]
        public void TestHonoursTheScaleRatherThanAssumingOne()
        {
            // The reference is written on a scale of 2.5 precisely so a reader
            // that ignores the field fails loudly instead of looking almost
            // right.
            var reference = LoadReference();
            var quantised = reference.Cases.First(c => c.Encoding == "uint8");

            Assert.That(quantised.Packed.Scale, Is.Not.EqualTo(1));
            Assert.That(quantised.Packed.Decode().Max(),
                        Is.EqualTo(reference.Values.Max()).Within(quantised.Tolerance));
        }

        [Test]
        public void TestSixteenBitIsReadLittleEndian()
        {
            // 0x0001 little-endian is 1; read the other way round it is 256,
            // which on a 65535-level scale is the difference between silence
            // and a visible step.
            var curve = new CurveData
            {
                Scale = 65535,
                Encoding = "uint16",
                Count = 1,
                Data = Convert.ToBase64String(new byte[] { 0x01, 0x00 }),
            };

            Assert.That(curve.Decode()[0], Is.EqualTo(1).Within(0.001));
        }

        [Test]
        public void TestFloat32IsReadLittleEndian()
        {
            var curve = new CurveData
            {
                Scale = 1,
                Encoding = "float32",
                Count = 1,
                Data = Convert.ToBase64String(new byte[] { 0x00, 0x00, 0x80, 0x3F }),
            };

            Assert.That(curve.Decode()[0], Is.EqualTo(1f).Within(1e-6));
        }

        // ------------------------------------------------------------------
        // Reader failure modes
        // ------------------------------------------------------------------

        [Test]
        public void TestAnUnknownEncodingIsRefused()
        {
            var curve = new CurveData { Encoding = "float64", Count = 1, Data = "AAAAAAAA8D8=" };

            Assert.That(curve.IsValid, Is.False);
            Assert.That(curve.Decode(), Is.Empty);
        }

        [Test]
        public void TestATruncatedPayloadIsRefused()
        {
            var full = new CurveData
            {
                Scale = 1,
                Encoding = "uint8",
                Count = 8,
                Data = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }),
            };

            Assert.That(full.IsValid, Is.True);

            var cut = new CurveData
            {
                Scale = 1,
                Encoding = "uint8",
                Count = 8,
                Data = Convert.ToBase64String(new byte[] { 1, 2, 3, 4 }),
            };

            Assert.That(cut.IsValid, Is.False);
            Assert.That(cut.Decode(), Is.Empty);
        }

        [Test]
        public void TestAPayloadThatIsNotWholeSamplesIsRefused()
        {
            var curve = new CurveData
            {
                Scale = 1,
                Encoding = "float32",
                Count = 1,
                Data = Convert.ToBase64String(new byte[] { 1, 2, 3 }),
            };

            Assert.That(curve.IsValid, Is.False);
        }

        [Test]
        public void TestMalformedBase64IsRefusedRatherThanThrowing()
        {
            var curve = new CurveData { Encoding = "uint8", Count = 3, Data = "not base64!" };

            Assert.That(curve.IsValid, Is.False);
            Assert.That(curve.Decode(), Is.Empty);
        }

        [Test]
        public void TestAnEmptyCurveIsValidAndEmpty()
        {
            // What a zero-length track exports. Not an error.
            var curve = new CurveData { Encoding = "uint8", Count = 0, Data = string.Empty };

            Assert.That(curve.IsValid, Is.True);
            Assert.That(curve.Decode(), Is.Empty);
        }

        [Test]
        public void TestARecordWithoutAnEncodingReadsAsUint8()
        {
            // The encoding that was implicit before the format named one.
            var curve = new CurveData
            {
                Scale = 255,
                Count = 2,
                Data = Convert.ToBase64String(new byte[] { 0, 255 }),
            };

            Assert.That(curve.Encoding, Is.EqualTo(CurveData.DefaultEncoding));
            Assert.That(curve.Decode(), Is.EqualTo(new[] { 0f, 255f }).Within(0.01f));
        }

        [Test]
        public void TestDecodingTwiceReturnsTheSameArray()
        {
            // Curves are decoded on demand and cached: a trace redrawn every
            // frame must not re-run base64 over 11,000 samples each time.
            var curve = LoadReference().Cases.First().Packed;

            Assert.That(curve.Decode(), Is.SameAs(curve.Decode()));
        }
    }
}
