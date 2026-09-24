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
        private const string transition_from_right = "transition-right";
        private const string transition_from_left = "transition-left";

        private readonly Sample whooshSample;
        private readonly Sample transitionFromRight;
        private readonly Sample transitionFromLeft;

        public MenuSoundPlayer(AudioManager audio)
        {
            var store = new InMemorySampleStore(new Dictionary<string, byte[]>
            {
                [whoosh] = MenuSoundSynth.Whoosh(),
                [transition_from_right] = MenuSoundSynth.TransitionWhoosh(fromRight: true),
                [transition_from_left] = MenuSoundSynth.TransitionWhoosh(fromRight: false),
            });

            var samples = audio.GetSampleStore(store);

            whooshSample = samples.Get(whoosh);
            transitionFromRight = samples.Get(transition_from_right);
            transitionFromLeft = samples.Get(transition_from_left);
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

        /// <summary>The sound of a screen change, travelling the way the ripple does.</summary>
        public void PlayTransition(bool fromRight)
        {
            var sample = fromRight ? transitionFromRight : transitionFromLeft;

            if (sample == null)
                return;

            var channel = sample.GetChannel();
            channel.Volume.Value = 0.6;
            channel.Play();
        }
    }
}
