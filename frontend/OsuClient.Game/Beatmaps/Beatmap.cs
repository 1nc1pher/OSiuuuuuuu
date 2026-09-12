using System;
using System.Collections.Generic;
using System.Linq;
using OsuClient.Game.Beatmaps.HitObjects;

namespace OsuClient.Game.Beatmaps
{
    /// <summary>The <c>[General]</c> section.</summary>
    public class BeatmapGeneral
    {
        public string AudioFilename { get; set; } = string.Empty;
        public int AudioLeadIn { get; set; }
        public int PreviewTime { get; set; } = -1;
        public string SampleSet { get; set; } = "Normal";
        public double StackLeniency { get; set; } = 0.7;

        /// <summary>Ruleset id. 0 = osu!standard, the only mode supported here.</summary>
        public int Mode { get; set; }
    }

    /// <summary>The <c>[Metadata]</c> section.</summary>
    public class BeatmapMetadata
    {
        public string Title { get; set; } = string.Empty;
        public string TitleUnicode { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string ArtistUnicode { get; set; } = string.Empty;
        public string Creator { get; set; } = string.Empty;

        /// <summary>The difficulty name, e.g. "Hard". Stored as <c>Version</c> in the file.</summary>
        public string Version { get; set; } = string.Empty;

        public string Source { get; set; } = string.Empty;
        public string Tags { get; set; } = string.Empty;
        public int BeatmapID { get; set; }
        public int BeatmapSetID { get; set; } = -1;
    }

    /// <summary>The <c>[Difficulty]</c> section.</summary>
    public class BeatmapDifficulty
    {
        public double HPDrainRate { get; set; } = 5;
        public double CircleSize { get; set; } = 5;
        public double OverallDifficulty { get; set; } = 5;
        public double ApproachRate { get; set; } = 5;
        public double SliderMultiplier { get; set; } = 1.4;
        public double SliderTickRate { get; set; } = 1;

        /// <summary>
        /// Hit circle radius in osu!pixels, per the osu! wiki:
        /// <c>radius = 54.4 - 4.48 * CS</c>. Mirrors the backend's
        /// <c>circle_radius()</c> so both halves agree on object size.
        /// </summary>
        public double CircleRadius => 54.4 - 4.48 * CircleSize;
    }

    /// <summary>
    /// One line of <c>[TimingPoints]</c>.
    ///
    /// An uninherited (red) point sets the tempo: <see cref="BeatLength"/> is the
    /// millisecond length of one beat. An inherited (green) point instead carries
    /// a negative value encoding a slider-velocity multiplier as <c>-100 / SV</c>.
    /// The backend writes exactly one uninherited point and no inherited ones.
    /// </summary>
    public class TimingPoint
    {
        public double Time { get; set; }

        /// <summary>The raw second field: ms per beat if uninherited, <c>-100/SV</c> if not.</summary>
        public double BeatLength { get; set; }

        public int Meter { get; set; } = 4;
        public int SampleSet { get; set; } = 1;
        public int SampleIndex { get; set; }
        public int Volume { get; set; } = 100;
        public bool Uninherited { get; set; } = true;
        public int Effects { get; set; }

        /// <summary>Tempo in BPM. Only meaningful on an uninherited point.</summary>
        public double BPM => BeatLength > 0 ? 60000.0 / BeatLength : 0;

        /// <summary>
        /// The slider-velocity multiplier an inherited point encodes.
        /// Returns 1 for an uninherited point.
        /// </summary>
        public double SliderVelocity
        {
            get
            {
                if (Uninherited || BeatLength >= 0)
                    return 1;

                // -100/SV  =>  SV = -100/BeatLength
                return Math.Clamp(-100.0 / BeatLength, 0.1, 10.0);
            }
        }

        public override string ToString() =>
            Uninherited
                ? $"Red @ {Time:0}ms {BPM:0.##} BPM"
                : $"Green @ {Time:0}ms SV x{SliderVelocity:0.##}";
    }

    /// <summary>
    /// A decoded .osu beatmap: one difficulty of a beatmap set.
    ///
    /// This is the in-memory model Phase 2 gameplay will read from. It is a
    /// plain data object with no osu.Framework dependency beyond
    /// <c>osuTK.Vector2</c>, so it can be unit tested without a game host.
    /// </summary>
    public class Beatmap
    {
        /// <summary>The <c>v</c> number from the <c>osu file format vN</c> header.</summary>
        public int FormatVersion { get; set; } = 14;

        public BeatmapGeneral General { get; } = new BeatmapGeneral();
        public BeatmapMetadata Metadata { get; } = new BeatmapMetadata();
        public BeatmapDifficulty Difficulty { get; } = new BeatmapDifficulty();

        /// <summary>Timing points, sorted by time.</summary>
        public List<TimingPoint> TimingPoints { get; } = new List<TimingPoint>();

        /// <summary>Hit objects, sorted by start time.</summary>
        public List<HitObjectData> HitObjects { get; } = new List<HitObjectData>();

        /// <summary>
        /// The first uninherited timing point — for the backend's output, the
        /// only one. Null if the beatmap has no tempo information.
        /// </summary>
        public TimingPoint? PrimaryTimingPoint =>
            TimingPoints.FirstOrDefault(t => t.Uninherited);

        /// <summary>Tempo of <see cref="PrimaryTimingPoint"/>, or 0.</summary>
        public double BPM => PrimaryTimingPoint?.BPM ?? 0;

        /// <summary>Start time of the first hit object, or 0 for an empty map.</summary>
        public double FirstHitObjectTime => HitObjects.Count > 0 ? HitObjects[0].StartTime : 0;

        /// <summary>End time of the last hit object, or 0 for an empty map.</summary>
        public double LastHitObjectTime => HitObjects.Count > 0 ? HitObjects.Max(h => h.EndTime) : 0;

        /// <summary>
        /// Milliseconds per beat in force at <paramref name="time"/>: the last
        /// uninherited point at or before it, falling back to the first one.
        /// </summary>
        public double BeatLengthAt(double time)
        {
            TimingPoint? active = null;

            foreach (var point in TimingPoints)
            {
                if (!point.Uninherited)
                    continue;

                if (point.Time > time && active != null)
                    break;

                active = point;
            }

            // Default to 60 BPM rather than 0 so callers can't divide by zero on
            // a malformed map with no timing information at all.
            return active?.BeatLength ?? 1000.0;
        }

        /// <summary>
        /// Slider-velocity multiplier in force at <paramref name="time"/>, taken
        /// from the last inherited point at or before it. Always 1 for the
        /// backend's output, which writes no green lines.
        /// </summary>
        public double SliderVelocityAt(double time)
        {
            double sv = 1;

            foreach (var point in TimingPoints)
            {
                if (point.Time > time)
                    break;

                if (point.Uninherited)
                    sv = 1; // a red line resets velocity
                else
                    sv = point.SliderVelocity;
            }

            return sv;
        }

        /// <summary>
        /// Duration in milliseconds of a slider with the given length and slide
        /// count starting at <paramref name="startTime"/>:
        /// <c>beats = length * slides / (100 * SliderMultiplier * SV)</c>.
        /// </summary>
        public double SliderDuration(double startTime, double pixelLength, int slides)
        {
            double denominator = 100.0 * Difficulty.SliderMultiplier * SliderVelocityAt(startTime);

            if (denominator <= 0)
                return 0;

            return pixelLength * slides / denominator * BeatLengthAt(startTime);
        }

        public override string ToString() =>
            $"{Metadata.Artist} - {Metadata.Title} [{Metadata.Version}] " +
            $"({HitObjects.Count} objects, {BPM:0.##} BPM)";
    }
}
