using osuTK;

namespace OsuClient.Game.Beatmaps.HitObjects
{
    /// <summary>
    /// osu!'s hit object <c>type</c> bitfield, as written in the 4th comma-separated
    /// field of a <c>[HitObjects]</c> record.
    /// </summary>
    public static class HitObjectFlags
    {
        public const int Circle = 1;
        public const int Slider = 2;
        public const int NewCombo = 4;
        public const int Spinner = 8;

        /// <summary>osu!mania hold note. Not produced by the backend, not supported.</summary>
        public const int Hold = 128;

        /// <summary>
        /// Bits 4-6 encode how many combo colours to skip on a new combo.
        /// Parsed out so they don't get mistaken for a type bit.
        /// </summary>
        public const int ComboColourSkipMask = 0b0111_0000;

        public const int ComboColourSkipShift = 4;
    }

    /// <summary>
    /// Base type for a parsed hit object. Times are in milliseconds, matching
    /// both the .osu file format and osu.Framework's clocks; positions are in
    /// osu!pixels on the 512x384 playfield.
    /// </summary>
    public abstract class HitObjectData
    {
        /// <summary>Centre position in osu!pixels.</summary>
        public Vector2 Position { get; set; }

        /// <summary>Start time in milliseconds.</summary>
        public double StartTime { get; set; }

        /// <summary>Whether this object starts a new combo.</summary>
        public bool NewCombo { get; set; }

        /// <summary>Number of combo colours to skip (bits 4-6 of the type field).</summary>
        public int ComboColourSkip { get; set; }

        /// <summary>The raw hitSound bitfield. Unused until hitsounds land in Phase 4.</summary>
        public int HitSound { get; set; }

        /// <summary>End time in milliseconds. Equal to <see cref="StartTime"/> for a circle.</summary>
        public abstract double EndTime { get; }

        public double Duration => EndTime - StartTime;

        public float X => Position.X;

        public float Y => Position.Y;
    }
}
