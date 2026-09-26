using System.Collections.Generic;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;

namespace OsuClient.Game.Audio
{
    /// <summary>
    /// The deck's own mechanical sounds, for the moments around play rather
    /// than the hits themselves: the count-in ticking, the needle dropping
    /// and lifting, the tape counter clacking back to zero on a combo break,
    /// and the auto-stop when a run fails.
    ///
    /// The same synthesized sounds song select and the tape deck already
    /// use, loaded here on their own rather than through
    /// <see cref="TapeDeckSoundPlayer"/>, which synthesizes twenty-odd sounds
    /// gameplay never plays. Built like <see cref="HitSoundPlayer"/>: a fresh
    /// channel per play, so overlapping sounds overlap.
    /// </summary>
    public sealed class GameplayDeckSounds
    {
        private const string counter = "counter";
        private const string needle = "needle";
        private const string auto_stop = "auto-stop";

        private readonly Sample? counterSample;
        private readonly Sample? needleSample;
        private readonly Sample? autoStopSample;

        public GameplayDeckSounds(AudioManager audio)
        {
            var store = new InMemorySampleStore(new Dictionary<string, byte[]>
            {
                [counter] = TapeDeckSoundSynth.CounterTick(),
                [needle] = SongSelectSoundSynth.NeedleDrop(),
                [auto_stop] = TapeDeckSoundSynth.AutoStop(),
            });

            var samples = audio.GetSampleStore(store);

            counterSample = samples.Get(counter);
            needleSample = samples.Get(needle);
            autoStopSample = samples.Get(auto_stop);
        }

        /// <summary>One beat of the count-in: the counter tick, pitched up so it reads as a metronome.</summary>
        public void PlayCountTick(bool last) => play(counterSample, 0.3, last ? 1.35 : 1.15);

        /// <summary>The needle meeting the record as the count-in ends.</summary>
        public void PlayNeedleDrop() => play(needleSample, 0.45, 1);

        /// <summary>The needle lifting off at the end of the song: the drop, higher and lighter.</summary>
        public void PlayNeedleLift() => play(needleSample, 0.35, 1.3);

        /// <summary>The tape counter snapping back to zero on a combo break.</summary>
        public void PlayCounterReset() => play(counterSample, 0.35, 0.8);

        /// <summary>The deck's auto-stop catching as a failed run's tape runs down.</summary>
        public void PlayAutoStop() => play(autoStopSample, 0.5, 1);

        private static void play(Sample? sample, double volume, double frequency)
        {
            if (sample == null)
                return;

            var channel = sample.GetChannel();
            channel.Volume.Value = volume;
            channel.Frequency.Value = frequency;
            channel.Play();
        }
    }
}
