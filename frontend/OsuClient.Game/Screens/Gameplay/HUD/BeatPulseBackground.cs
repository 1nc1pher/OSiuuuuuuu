using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using OsuClient.Game.Beatmaps;
using OsuClient.Game.Graphics;
using osuTK;

namespace OsuClient.Game.Screens.Gameplay.HUD
{
    /// <summary>
    /// The beatmap background, pulsing with the track's beat like a heartbeat:
    /// a brief zoom outward right on each beat, easing back to its resting
    /// size before the next one.
    ///
    /// Only the background scales — the playfield and hit objects never move,
    /// so aim and timing are never at the mercy of a cosmetic effect. The
    /// pulse only ever scales up from 1, never down, so it can never reveal a
    /// gap at the background's own edge (already fully covered at scale 1 by
    /// <see cref="BeatmapBackground"/>'s own fill mode).
    /// </summary>
    public partial class BeatPulseBackground : CompositeDrawable
    {
        /// <summary>How much larger the background gets at the peak of each pulse.</summary>
        private const float pulse_amount = 0.035f;

        /// <summary>Sharper than the side flash's — a quick pump, not a slow sway.</summary>
        private const double pulse_sharpness = 6;

        private readonly Beatmap beatmap;
        private readonly BeatmapBackground background;

        /// <summary>
        /// Lets a future settings screen switch the effect off entirely
        /// without this component needing to know anything about settings —
        /// it just stops contributing when asked.
        /// </summary>
        public bool Enabled { get; set; } = true;

        public BeatPulseBackground(Beatmap beatmap, string? backgroundPath)
        {
            this.beatmap = beatmap;

            RelativeSizeAxes = Axes.Both;

            InternalChild = background = new BeatmapBackground(backgroundPath);
        }

        /// <summary>Current scale factor applied to the background. Exposed for tests.</summary>
        public float CurrentScale => background.Scale.X;

        /// <summary>
        /// Sets the background's scale for <paramref name="time"/>: resting at
        /// 1 between beats, pulsing up to <c>1 + pulse_amount</c> right on
        /// each one, faded in and out across the map's own playable span like
        /// the rest of the beat-synced effects.
        /// </summary>
        public void SetTime(double time)
        {
            if (!Enabled)
            {
                background.Scale = Vector2.One;
                return;
            }

            double phase = BeatPulse.PhaseAt(beatmap, time);
            double pulse = BeatPulse.IntensityAt(phase, pulse_sharpness);
            double envelope = BeatPulse.PlayableEnvelope(beatmap, time);

            float scale = 1f + (float)(pulse_amount * pulse * envelope);

            background.Scale = new Vector2(scale);
        }
    }
}
