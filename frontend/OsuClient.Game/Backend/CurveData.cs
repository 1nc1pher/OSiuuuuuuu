using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace OsuClient.Game.Backend
{
    /// <summary>
    /// One per-frame curve out of the backend's <c>dsp.json</c> — spectral
    /// flux, its local median, the onset envelope, the RMS envelope, or flux
    /// within one frequency band.
    ///
    /// The payload is base64'd binary rather than JSON numbers: a four-minute
    /// track is around 11,000 frames, and seven curves of that written as
    /// decimals is several hundred kilobytes of digits for precision no drawn
    /// line can show. See <c>src/export/curve_pack.py</c> for the full
    /// reasoning, including why downsampling instead would have put every
    /// onset marker beside its own flux peak.
    ///
    /// Three encodings, named in the record so this class never has to know
    /// which curve is which:
    ///
    /// <list type="bullet">
    /// <item><c>uint8</c> — one byte per frame, for the five curves that are
    /// only ever drawn.</item>
    /// <item><c>uint16</c> — the median, which the live threshold is derived
    /// from.</item>
    /// <item><c>float32</c> — flux, which the peak-picker re-runs over. A
    /// quantisation that flattens two near-equal neighbours does not blur a
    /// peak there, it deletes it; the backend measured 7 onsets in 499 lost at
    /// uint8, and one peak still shifting a frame at uint16.</item>
    /// </list>
    ///
    /// Decoding is one rule for all three:
    /// <c>value = raw / divisor * scale</c>.
    ///
    /// The format is pinned from both ends by
    /// <c>tests/fixtures/curve_pack_reference.json</c>, which this project's
    /// tests and the backend's own assert against. Change either side and a
    /// test goes red rather than the two halves quietly disagreeing about what
    /// a curve means.
    /// </summary>
    public sealed class CurveData
    {
        /// <summary>Encoding name → (bytes per sample, decode divisor).</summary>
        private static readonly Dictionary<string, (int Width, double Divisor)> encodings =
            new Dictionary<string, (int, double)>(StringComparer.Ordinal)
            {
                ["uint8"] = (1, 255),
                ["uint16"] = (2, 65535),
                // Stored as-is; the divisor exists only so the decode rule is
                // the same sentence for all three.
                ["float32"] = (4, 1),
            };

        /// <summary>The encoding assumed when a record doesn't name one.</summary>
        public const string DefaultEncoding = "uint8";

        /// <summary>What a decoded value of the encoding's maximum means.</summary>
        public double Scale { get; init; } = 1;

        /// <summary><c>uint8</c>, <c>uint16</c> or <c>float32</c>.</summary>
        public string Encoding { get; init; } = DefaultEncoding;

        /// <summary>
        /// How many samples the payload should hold.
        ///
        /// Derivable from its length, and carried anyway: it is the only
        /// corruption this format can detect, and a truncated curve that
        /// silently draws short is exactly the failure worth catching.
        /// </summary>
        public int Count { get; init; }

        /// <summary>The base64'd payload.</summary>
        public string Data { get; init; } = string.Empty;

        private float[]? decoded;

        /// <summary>
        /// Whether this record can be decoded at all: a known encoding, a
        /// payload that is a whole number of samples, and a length matching
        /// the count it claims.
        ///
        /// Checked without decoding, so a whole document can be validated on
        /// load for the price of a base64 length calculation.
        /// </summary>
        public bool IsValid
        {
            get
            {
                if (!encodings.TryGetValue(Encoding ?? string.Empty, out var spec))
                    return false;

                if (Count < 0)
                    return false;

                if (!tryMeasure(Data, out int bytes))
                    return false;

                return bytes % spec.Width == 0 && bytes / spec.Width == Count;
            }
        }

        /// <summary>
        /// The curve as floats, decoded once and cached.
        ///
        /// Returns an empty array for a record that isn't valid, rather than
        /// throwing: by the time anything asks for a curve the screen is
        /// already up, and an empty trace draws as nothing, which is the same
        /// outcome as the file having been absent.
        /// </summary>
        public float[] Decode()
        {
            if (decoded != null)
                return decoded;

            if (!IsValid)
                return decoded = Array.Empty<float>();

            var spec = encodings[Encoding];
            byte[] raw = Convert.FromBase64String(Data);

            var values = new float[Count];
            double scale = Scale;

            for (int i = 0; i < Count; i++)
            {
                var sample = raw.AsSpan(i * spec.Width, spec.Width);

                // Read explicitly little-endian rather than through
                // BitConverter, whose byte order follows the machine. The
                // file is written little-endian on purpose; a format whose
                // meaning depends on the reader's architecture is not one.
                double value = Encoding switch
                {
                    "uint8" => sample[0],
                    "uint16" => BinaryPrimitives.ReadUInt16LittleEndian(sample),
                    _ => BinaryPrimitives.ReadSingleLittleEndian(sample),
                };

                values[i] = (float)(value / spec.Divisor * scale);
            }

            return decoded = values;
        }

        /// <summary>
        /// How many bytes a base64 string decodes to, without decoding it.
        /// Returns false for anything that isn't well-formed base64 of a
        /// plausible length.
        /// </summary>
        private static bool tryMeasure(string? data, out int bytes)
        {
            bytes = 0;

            if (data == null)
                return false;

            if (data.Length == 0)
                return true;

            if (data.Length % 4 != 0)
                return false;

            int padding = 0;

            if (data[^1] == '=')
                padding++;

            if (data.Length > 1 && data[^2] == '=')
                padding++;

            bytes = data.Length / 4 * 3 - padding;
            return true;
        }
    }
}
