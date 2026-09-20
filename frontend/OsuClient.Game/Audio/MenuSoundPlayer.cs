using System.Collections.Generic;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;

namespace OsuClient.Game.Audio
{
    /// <summary>
    /// Loads the menu's synthesized sounds once and plays them by name — the
    /// same shape as <see cref="HitSoundPlayer"/>, and for the same reason:
    /// the synthesis happens at startup, not on every press.
    /// </summary>
    public sealed class MenuSoundPlayer
    {
        private const string whoosh = "whoosh";

        private readonly Sample whooshSample;

        public MenuSoundPlayer(AudioManager audio)
        {
            var store = new InMemorySampleStore(new Dictionary<string, byte[]>
            {
                [whoosh] = MenuSoundSynth.Whoosh(),
            });

            whooshSample = audio.GetSampleStore(store).Get(whoosh);
        }

        /// <summary>The sound of the strip sliding out from behind the logo.</summary>
        public void PlayWhoosh()
        {
            if (whooshSample == null)
                return;

            var channel = whooshSample.GetChannel();
            channel.Volume.Value = 0.55;
            channel.Play();
        }
    }
}
