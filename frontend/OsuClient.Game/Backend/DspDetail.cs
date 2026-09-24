using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OsuClient.Game.Backend
{
    /// <summary>The shared time axis every curve sits on, and the STFT it came from.</summary>
    public sealed class DspFrames
    {
        public int SampleRate { get; init; }

        public int HopLength { get; init; }

        public int NFft { get; init; }

        public int NMels { get; init; }

        /// <summary>Frames per second — <c>sampleRate / hopLength</c>, about 43.</summary>
        public double FrameRate { get; init; }

        /// <summary>How many frames every curve holds.</summary>
        public int Count { get; init; }

        /// <summary>Seconds into the track that a frame index sits at.</summary>
        public double TimeAt(int frame) => FrameRate > 0 ? frame / FrameRate : 0;

        /// <summary>The frame covering a moment, clamped into the track.</summary>
        public int FrameAt(double seconds) =>
            Math.Clamp((int)Math.Round(seconds * FrameRate), 0, Math.Max(0, Count - 1));

        /// <summary>The track's length, as the frames measure it.</summary>
        public double Duration => TimeAt(Math.Max(0, Count - 1));
    }

    /// <summary>One difficulty's detector sensitivity — what its threshold was built from.</summary>
    public sealed class DspSensitivity
    {
        /// <summary>Multiplier on the local median.</summary>
        public double Margin { get; init; }

        /// <summary>Additive floor, which is what keeps silence silent.</summary>
        public double Delta { get; init; }
    }

    /// <summary>How the onset detector was configured.</summary>
    public sealed class DspDetection
    {
        public double MedianWindowSec { get; init; }

        /// <summary>Closest two picked peaks may be, in seconds.</summary>
        public double MinSpacingSec { get; init; }

        public IReadOnlyDictionary<string, DspSensitivity> Tiers { get; init; } =
            new Dictionary<string, DspSensitivity>();
    }

    /// <summary>One of the three tempi the octave stage scored.</summary>
    public sealed class DspOctaveCandidate
    {
        /// <summary>0.5, 1 or 2 — what the raw estimate was multiplied by.</summary>
        public double Factor { get; init; }

        public double Bpm { get; init; }

        /// <summary>Whether the search considered it at all, or it fell outside 50–210 BPM.</summary>
        public bool InRange { get; init; }

        /// <summary>How well a pulse train at this tempo lands on real onset energy.</summary>
        public double PhaseStrength { get; init; }

        /// <summary>The log-normal tempo prior at this tempo.</summary>
        public double PriorWeight { get; init; }

        /// <summary><see cref="PhaseStrength"/> × <see cref="PriorWeight"/> — what was compared.</summary>
        public double Score { get; init; }

        /// <summary>Whether this is the tempo the map was actually built on.</summary>
        public bool Chosen { get; init; }
    }

    /// <summary>
    /// How Step 3 found the tempo, rather than what it found.
    ///
    /// <c>analysis.json</c> already carries the answer. This is the search:
    /// an autocorrelation over the onset envelope, weighted by a prior, then
    /// re-scored at half and double speed, then slid across one beat period
    /// to find its phase.
    /// </summary>
    public sealed class DspTempo
    {
        public double SearchMinBpm { get; init; }

        public double SearchMaxBpm { get; init; }

        public double PriorBpm { get; init; }

        public double PriorWidthOctaves { get; init; }

        /// <summary>First lag the curves cover. They reach an octave past the search band so the 1×/2×/½× peaks are all in frame.</summary>
        public int LagMin { get; init; }

        public int LagMax { get; init; }

        /// <summary>First lag the search itself looked at — the fastest tempo, since a shorter lag is a faster tempo.</summary>
        public int SearchLagMin { get; init; }

        public int SearchLagMax { get; init; }

        /// <summary>The tempo each lag corresponds to; the axis to label.</summary>
        public IReadOnlyList<double> Bpms { get; init; } = Array.Empty<double>();

        /// <summary>Normalised autocorrelation of the onset envelope at each lag.</summary>
        public IReadOnlyList<double> Acf { get; init; } = Array.Empty<double>();

        /// <summary>The log-normal tempo prior at each lag.</summary>
        public IReadOnlyList<double> Prior { get; init; } = Array.Empty<double>();

        /// <summary><see cref="Acf"/> × <see cref="Prior"/> — the curve the peak is taken from.</summary>
        public IReadOnlyList<double> Weighted { get; init; } = Array.Empty<double>();

        /// <summary>What the autocorrelation alone said, before octave correction.</summary>
        public double RawBpm { get; init; }

        public double RawConfidence { get; init; }

        /// <summary>The tempo the map was built on.</summary>
        public double ChosenBpm { get; init; }

        /// <summary>Whether octave correction actually moved the answer — true on about one track in seven.</summary>
        public bool OctaveCorrected { get; init; }

        public IReadOnlyList<DspOctaveCandidate> OctaveCandidates { get; init; } =
            Array.Empty<DspOctaveCandidate>();

        /// <summary>One beat, in frames.</summary>
        public double PeriodFrames { get; init; }

        /// <summary>Onset energy under a pulse train started at each whole-frame offset within one beat.</summary>
        public IReadOnlyList<double> PhaseScores { get; init; } = Array.Empty<double>();

        /// <summary>Where the slide locked — the argmax of <see cref="PhaseScores"/>.</summary>
        public int PhaseOffsetFrames { get; init; }

        /// <summary>On-beat onset energy over average onset energy. Above 1 means the beats landed somewhere real.</summary>
        public double PhaseStrength { get; init; }

        /// <summary>First beat, in seconds.</summary>
        public double Offset { get; init; }

        /// <summary>Autocorrelation height at the chosen lag, 0..1.</summary>
        public double Confidence { get; init; }

        /// <summary>The candidate that won, or null if the file didn't mark one.</summary>
        public DspOctaveCandidate? Winner =>
            OctaveCandidates.FirstOrDefault(candidate => candidate.Chosen);
    }

    /// <summary>Every knob one difficulty preset was built with (Step 5).</summary>
    public sealed class DspPreset
    {
        public double Stars { get; init; }

        public double Margin { get; init; }

        public double Delta { get; init; }

        /// <summary>1 = 1/1, 2 = 1/2, 4 = 1/4.</summary>
        public int SnapDivision { get; init; }

        public double MinSpacingBeats { get; init; }

        public double CircleSize { get; init; }

        public double ApproachRate { get; init; }

        public double OverallDifficulty { get; init; }

        public double HpDrain { get; init; }

        public double SliderMultiplier { get; init; }

        public double DistanceSpacing { get; init; }

        public double MaxTurnDegrees { get; init; }

        /// <summary>Onsets in a fast run needed before it folds into one slider. 999 means never.</summary>
        public int StreamMinLen { get; init; }

        public double SpinnerMinBeats { get; init; }

        public double SliderMinBeats { get; init; }

        public double SliderMaxBeats { get; init; }

        public double SustainThreshold { get; init; }
    }

    /// <summary>One tier's journey from detected onsets to placed objects.</summary>
    public sealed class DspFunnel
    {
        /// <summary>Onsets the detector found at this tier's sensitivity.</summary>
        public int Detected { get; init; }

        /// <summary>What survived quantisation onto the beat grid — collisions merge, keeping the stronger.</summary>
        public int Snapped { get; init; }

        /// <summary>What survived this tier's minimum spacing.</summary>
        public int AfterSpacing { get; init; }

        /// <summary>Objects actually placed. Lower than <see cref="AfterSpacing"/> wherever a run folded into one slider.</summary>
        public int Objects { get; init; }

        /// <summary>The four counts in order, for drawing the funnel.</summary>
        public IReadOnlyList<int> Stages => new[] { Detected, Snapped, AfterSpacing, Objects };
    }

    /// <summary>One classification decision, with the numbers that drove it.</summary>
    public sealed class DspSustainDecision
    {
        public double Time { get; init; }

        /// <summary>How long the gap to the next onset was, in beats.</summary>
        public double GapBeats { get; init; }

        /// <summary>Fraction of that gap the sound stayed loud — what the slider/spinner test measures.</summary>
        public double Sustain { get; init; }

        /// <summary>Whether density thinning had removed a real onset inside the gap. A re-attacked sound is not a held one.</summary>
        public bool Clean { get; init; }

        /// <summary>How many onsets the run starting here held.</summary>
        public int Run { get; init; }

        /// <summary><c>circle</c>, <c>heldSlider</c>, <c>streamSlider</c> or <c>spinner</c>.</summary>
        public string Became { get; init; } = string.Empty;
    }

    /// <summary>Everything recorded about one difficulty tier.</summary>
    public sealed class DspTier
    {
        public DspPreset Preset { get; init; } = new DspPreset();

        public DspFunnel Funnel { get; init; } = new DspFunnel();

        /// <summary>Objects by kind: circle, slider, spinner.</summary>
        public IReadOnlyDictionary<string, int> Kinds { get; init; } = new Dictionary<string, int>();

        /// <summary>Objects by the subdivision they snapped to: 1/1, 1/2, 1/4.</summary>
        public IReadOnlyDictionary<string, int> SnapHistogram { get; init; } = new Dictionary<string, int>();

        /// <summary>How many onsets lost a grid slot to a stronger neighbour.</summary>
        public int Merged { get; init; }

        /// <summary>How far each surviving onset moved to reach the grid, in milliseconds, signed.</summary>
        public IReadOnlyList<double> SnapDeltasMs { get; init; } = Array.Empty<double>();

        /// <summary>
        /// The slider / spinner / rest decisions, on the one tier that carries
        /// them. Filtered to the decisions where sustain could actually have
        /// changed the outcome — a circle after a 1/4 gap never consulted it.
        /// </summary>
        public IReadOnlyList<DspSustainDecision> Sustain { get; init; } =
            Array.Empty<DspSustainDecision>();
    }

    /// <summary>
    /// The backend's <c>dsp.json</c>: the per-frame signals underneath the
    /// decisions <see cref="AnalysisData"/> records
    /// (GENERATION_REDESIGN_PLAN.md, <c>src/export/dsp_trace.py</c>).
    ///
    /// A second file rather than a new version of <c>analysis.json</c>, and
    /// that is the whole degradation story: every beatmap generated before
    /// this existed still reads, still animates its four original stages, and
    /// simply has no deeper ones. A client asking for this gets null and says
    /// so, exactly as it does for a missing analysis.
    ///
    /// Every block past <see cref="Curves"/> is optional in the same way.
    /// A document written without a beat grid has no <see cref="Tempo"/>; one
    /// written without per-tier data has no <see cref="Tiers"/>. Ask, get
    /// null, show less.
    /// </summary>
    public sealed class DspDetail
    {
        /// <summary>The file name the backend writes inside the beatmap folder.</summary>
        public const string FileName = "dsp.json";

        /// <summary>The schema version this build understands.</summary>
        public const int SupportedVersion = 1;

        private static readonly JsonSerializerOptions options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };

        public int Version { get; init; }

        public DspFrames Frames { get; init; } = new DspFrames();

        /// <summary>Which frequency range each band curve covers, in Hz: low, mid, high.</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<double>> Bands { get; init; } =
            new Dictionary<string, IReadOnlyList<double>>();

        public DspDetection Detection { get; init; } = new DspDetection();

        /// <summary>Curves by name: flux, median, envelope, rms, bandLow, bandMid, bandHigh.</summary>
        public IReadOnlyDictionary<string, CurveData> Curves { get; init; } =
            new Dictionary<string, CurveData>();

        /// <summary>How the tempo was found, or null when the document was written without a grid.</summary>
        public DspTempo? Tempo { get; init; }

        /// <summary>Per-difficulty funnels, or null when the document was written without them.</summary>
        public IReadOnlyDictionary<string, DspTier>? Tiers { get; init; }

        /// <summary>Whether there is anything here worth drawing.</summary>
        public bool HasContent => Frames.Count > 0 && Curves.Count > 0;

        /// <summary>One curve by name, or null if this document doesn't carry it.</summary>
        public CurveData? Curve(string name) =>
            Curves.TryGetValue(name, out var curve) ? curve : null;

        /// <summary>One curve's samples, or an empty array if it isn't here.</summary>
        public float[] Samples(string name) => Curve(name)?.Decode() ?? Array.Empty<float>();

        /// <summary>One tier's record, or null if this document doesn't carry it.</summary>
        public DspTier? Tier(string name) =>
            Tiers != null && Tiers.TryGetValue(name, out var tier) ? tier : null;

        /// <summary>
        /// The onset threshold at a given sensitivity:
        /// <c>delta + margin × median[n]</c>.
        ///
        /// Derived here rather than read, because the median is the only part
        /// of the threshold that doesn't depend on the difficulty preset. One
        /// curve in the file therefore yields every tier's threshold — and
        /// thresholds no tier uses, which is what makes the detector's
        /// sensitivity something to explore rather than just look at.
        /// </summary>
        public float[] ThresholdFor(double margin, double delta)
        {
            float[] median = Samples("median");
            var threshold = new float[median.Length];

            for (int i = 0; i < median.Length; i++)
                threshold[i] = (float)(delta + median[i] * margin);

            return threshold;
        }

        /// <summary>The same, for a named difficulty, or an empty array if that tier isn't in the file.</summary>
        public float[] ThresholdFor(string tier)
        {
            if (!Detection.Tiers.TryGetValue(tier, out var sensitivity))
                return Array.Empty<float>();

            return ThresholdFor(sensitivity.Margin, sensitivity.Delta);
        }

        /// <summary>Reads the trace out of a beatmap folder, or null when there isn't a usable one.</summary>
        public static DspDetail? LoadFromFolder(string? beatmapFolder)
        {
            if (string.IsNullOrWhiteSpace(beatmapFolder))
                return null;

            return Load(Path.Combine(beatmapFolder, FileName));
        }

        /// <summary>Reads one <c>dsp.json</c>, or returns null if it can't be used.</summary>
        public static DspDetail? Load(string path)
        {
            if (!File.Exists(path))
                return null;

            try
            {
                return Parse(File.ReadAllText(path));
            }
            catch (IOException)
            {
                return null;
            }
        }

        /// <summary>
        /// Parses a trace document, or returns null if it isn't one this build
        /// can use.
        ///
        /// Curves are validated here — a known encoding, a payload of the
        /// length it claims — because a half-written file from an interrupted
        /// run should come out as "no trace for this map" rather than as a
        /// screen of curves that stop partway through the song.
        /// </summary>
        public static DspDetail? Parse(string json)
        {
            try
            {
                var data = JsonSerializer.Deserialize<DspDetail>(json, options);

                if (data == null || data.Version != SupportedVersion)
                    return null;

                if (!data.HasContent)
                    return null;

                foreach (var curve in data.Curves.Values)
                {
                    if (curve == null || !curve.IsValid || curve.Count != data.Frames.Count)
                        return null;
                }

                return data;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
