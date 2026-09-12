using System;
using System.Collections.Generic;
using osuTK;

namespace OsuClient.Game.Beatmaps.HitObjects
{
    /// <summary>The curve type prefix of a slider's path (<c>L|</c>, <c>B|</c>, ...).</summary>
    public enum SliderCurveType
    {
        /// <summary><c>L</c> — straight segments between anchors. The only type the backend emits.</summary>
        Linear,

        /// <summary><c>B</c> — bezier.</summary>
        Bezier,

        /// <summary><c>P</c> — perfect circle through three points.</summary>
        PerfectCircle,

        /// <summary><c>C</c> — Catmull (legacy).</summary>
        Catmull,
    }

    /// <summary>
    /// A slider.
    ///
    /// Record layout:
    /// <c>x,y,time,type,hitSound,curveType|p1|p2|...,slides,length,edgeSounds,edgeSets,hitSample</c>
    ///
    /// A slider's duration is NOT stored in the file — osu! derives it from the
    /// path length, the slide count and the timing in force at the start time:
    /// <code>
    ///     beats = length * slides / (100 * SliderMultiplier * SV)
    /// </code>
    /// <see cref="BeatmapDecoder"/> evaluates that against the beatmap's timing
    /// points and fills in <see cref="EndTime"/>. The backend sizes
    /// <see cref="PixelLength"/> against the same SliderMultiplier it writes
    /// into <c>[Difficulty]</c> (with SV = 1, no green lines), so the resolved
    /// duration matches the number of beats the generator intended.
    /// </summary>
    public class SliderData : HitObjectData
    {
        private double endTime;

        public SliderCurveType CurveType { get; set; } = SliderCurveType.Linear;

        /// <summary>
        /// Absolute anchor points in osu!pixels, starting with the slider head.
        /// The file stores only the points after the head; the decoder prepends
        /// <see cref="HitObjectData.Position"/> so this is a complete path.
        /// </summary>
        public IReadOnlyList<Vector2> Path { get; set; } = Array.Empty<Vector2>();

        /// <summary>Number of slides. 1 = no repeat, 2 = one repeat, and so on.</summary>
        public int Slides { get; set; } = 1;

        /// <summary>Length of a single slide, in osu!pixels.</summary>
        public double PixelLength { get; set; }

        /// <summary>Raw per-edge hitsound bitfields (<c>edgeSounds</c>). May be empty.</summary>
        public IReadOnlyList<int> EdgeSounds { get; set; } = Array.Empty<int>();

        /// <summary>
        /// Total duration across every slide, resolved by the decoder.
        /// </summary>
        public override double EndTime => endTime;

        /// <summary>Duration of one slide, in milliseconds.</summary>
        public double SpanDuration => Slides > 0 ? Duration / Slides : Duration;

        public void SetEndTime(double value) => endTime = value;

        public override string ToString() =>
            $"Slider @ {StartTime:0}ms ({X:0},{Y:0}) {CurveType} " +
            $"len={PixelLength:0.##} slides={Slides}{(NewCombo ? " [NC]" : string.Empty)}";
    }
}
