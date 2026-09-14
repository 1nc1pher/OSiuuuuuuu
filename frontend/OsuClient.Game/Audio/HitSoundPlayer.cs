using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
using osu.Framework.IO.Stores;

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

        /// <summary>A byte-array resource store backed by an in-memory dictionary, so no disk or embedded resource is needed.</summary>
        private sealed class InMemorySampleStore : IResourceStore<byte[]>
        {
            private readonly Dictionary<string, byte[]> data;

            public InMemorySampleStore(Dictionary<string, byte[]> data)
            {
                this.data = data;
            }

            public byte[] Get(string name) => data.TryGetValue(name, out var bytes) ? bytes : null!;

            public Task<byte[]> GetAsync(string name, CancellationToken cancellationToken = default) =>
                Task.FromResult(Get(name));

            public Stream GetStream(string name) =>
                data.TryGetValue(name, out var bytes) ? new MemoryStream(bytes) : null!;

            public IEnumerable<string> GetAvailableResources() => data.Keys;

            public void Dispose()
            {
            }
        }
    }
}
