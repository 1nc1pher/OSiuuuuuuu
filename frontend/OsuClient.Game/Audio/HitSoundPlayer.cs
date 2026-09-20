using System.Collections.Generic;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;

namespace OsuClient.Game.Audio
{
    /// <summary>
    /// Loads the synthesized hitsounds into osu.Framework's audio system once,
    /// then plays them by name. A fresh <see cref="SampleChannel"/> per call
    /// (rather than replaying one shared channel) is what lets overlapping
    /// hits — the same tick sound firing twice in quick succession — actually
    /// overlap instead of cutting each other off.
    /// </summary>
    public sealed class HitSoundPlayer
    {
        private const string hit = "hit";
        private const string tick = "tick";

        private readonly Sample hitSample;
        private readonly Sample tickSample;

        public HitSoundPlayer(AudioManager audio)
        {
            var store = new InMemorySampleStore(new Dictionary<string, byte[]>
            {
                [hit] = HitSoundSynth.Kick(),
                [tick] = HitSoundSynth.Tick(),
            });

            var samples = audio.GetSampleStore(store);

            hitSample = samples.Get(hit);
            tickSample = samples.Get(tick);
        }

        /// <summary>The main hit sound, for a judged circle, slider head/tail or a completed spinner.</summary>
        public void PlayHit() => hitSample?.Play();

        /// <summary>The lighter tick sound, for a collected slider tick or repeat.</summary>
        public void PlayTick() => tickSample?.Play();
    }
}
