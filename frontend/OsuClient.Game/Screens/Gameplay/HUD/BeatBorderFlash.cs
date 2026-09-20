using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using OsuClient.Game.Beatmaps;
using OsuClient.Game.Graphics;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.Gameplay.HUD
{
    /// <summary>
    /// A soft wash of light from the left and right edges of the window,
    /// pulsing once per beat — an ambient sense of the track's tempo that sits
    /// behind the playfield rather than a HUD element in its own right.
    ///
    /// The drawing is <see cref="EdgeGlow"/>'s — a gradient box pinned to
    /// each edge, sized to slightly overlap the key overlay's own bars rather
    /// than stopping short of them. What lives here is the beat maths that
    /// drives it.
    ///
    /// Brightness is driven purely by <see cref="SetTime"/> every frame rather
    /// than by scheduling a transform per beat: a beat count over a long map
    /// is a lot of transforms to keep queued, and unlike a transform this
    /// reads correct immediately after a pause, a seek, or the gameplay
    /// clock's own audio-resync jumps, none of which a scheduled sequence
    /// would survive cleanly.
    /// </summary>
    public partial class BeatBorderFlash : CompositeDrawable
    {
        /// <summary>
        /// How sharply the pulse narrows around the beat. 1 would be a plain
        /// cosine, spending half of every beat above half brightness — this
        /// keeps it a brief flash felt "at" the beat rather than a slow
        /// breathing glow between beats.
        /// </summary>
        private const double pulse_sharpness = 4;

        private static readonly Color4 flash_colour = new Color4(0.5f, 0.85f, 1f, 1f);

        private readonly Beatmap beatmap;
        private readonly float peakAlpha;

        private readonly EdgeGlow glow;

        /// <summary>
        /// Lets a future settings screen switch the effect off entirely
        /// without this component needing to know anything about settings —
        /// it just stops contributing when asked.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <param name="reach">
        /// How far in from each edge the flash extends, in pixels — matched to
        /// where the key overlay's own bars sit, so the flash slightly
        /// overlaps them rather than stopping short.
        /// </param>
        public BeatBorderFlash(Beatmap beatmap, BeatmapDifficulty difficulty, float reach)
        {
            this.beatmap = beatmap;

            // Scaled by difficulty, but kept within a range still modest at
            // its lowest — this is meant to be felt more than seen even on
            // the hardest map, let alone the easiest. 60% of the original
            // 0.35-0.70 range, which read too bright in practice.
            peakAlpha = 0.21f + 0.21f * (float)Math.Clamp(difficulty.OverallDifficulty / 10.0, 0, 1);

            RelativeSizeAxes = Axes.Both;

            InternalChild = glow = new EdgeGlow(reach) { GlowColour = flash_colour };
        }

        /// <summary>Current brightness of both sides, 0 to <see cref="peakAlpha"/>. Exposed for tests.</summary>
        public float Brightness => glow.Intensity;

        /// <summary>
        /// Sets how bright each side is for <paramref name="time"/>: a smooth
        /// pulse that peaks exactly on the beat and eases back to nothing
        /// between beats, faded in and out again across the map's own
        /// playable span so it doesn't appear or vanish abruptly.
        /// </summary>
        public void SetTime(double time)
        {
            if (!Enabled)
            {
                glow.Intensity = 0;
                return;
            }

            double phase = BeatPulse.PhaseAt(beatmap, time);
            double pulse = BeatPulse.IntensityAt(phase, pulse_sharpness);
            double envelope = BeatPulse.PlayableEnvelope(beatmap, time);

            glow.Intensity = (float)(peakAlpha * pulse * envelope);
        }
    }
}
