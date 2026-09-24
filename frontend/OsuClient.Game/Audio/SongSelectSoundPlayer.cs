using System;
using System.Collections.Generic;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;

namespace OsuClient.Game.Audio
{
    /// <summary>
    /// Loads song select's synthesized sounds once and plays them — the same
    /// shape as <see cref="MenuSoundPlayer"/>.
    /// </summary>
    public sealed class SongSelectSoundPlayer
    {
        private const string turn_forward = "vinyl-forward";
        private const string turn_back = "vinyl-back";
        private const string search = "search-click";
        private const string deck_key = "deck-key";
        private const string needle_drop = "needle-drop";
        private const string whoosh = "whoosh";
        private const string launch = "launch";

        /// <summary>
        /// Closest two turn sounds may start. A flicked scroll wheel can turn
        /// several notches in a frame; one scratch per notch at that rate
        /// fuses into a buzz, where a few per spin still read as notches.
        /// </summary>
        private const double min_turn_gap = 45;

        private readonly Sample? turnForward;
        private readonly Sample? turnBack;
        private readonly Sample? searchClick;
        private readonly Sample? deckKey;
        private readonly Sample? needleDrop;
        private readonly Sample? whooshSample;
        private readonly Sample? launchSample;

        private readonly Random random = new Random();

        private double lastTurnAt = double.NegativeInfinity;

        public SongSelectSoundPlayer(AudioManager audio)
        {
            var store = new InMemorySampleStore(new Dictionary<string, byte[]>
            {
                [turn_forward] = SongSelectSoundSynth.VinylTurn(forward: true),
                [turn_back] = SongSelectSoundSynth.VinylTurn(forward: false),
                [search] = SongSelectSoundSynth.SearchClick(),
                [deck_key] = SongSelectSoundSynth.DeckKey(),
                [needle_drop] = SongSelectSoundSynth.NeedleDrop(),
                [whoosh] = MenuSoundSynth.Whoosh(),
                [launch] = MenuSoundSynth.TransitionWhoosh(fromRight: false, centred: true),
            });

            var samples = audio.GetSampleStore(store);

            turnForward = samples.Get(turn_forward);
            turnBack = samples.Get(turn_back);
            searchClick = samples.Get(search);
            deckKey = samples.Get(deck_key);
            needleDrop = samples.Get(needle_drop);
            whooshSample = samples.Get(whoosh);
            launchSample = samples.Get(launch);
        }

        /// <summary>
        /// One notch of the record turning. <paramref name="now"/> is the
        /// caller's clock time, used only to space notches out.
        /// </summary>
        public void PlayTurn(bool forward, double now)
        {
            if (now - lastTurnAt < min_turn_gap)
                return;

            lastTurnAt = now;

            // A little pitch drift per notch, so a spin sounds like a record
            // turning rather than one sample on repeat.
            play(forward ? turnForward : turnBack, 0.45, 0.94 + random.NextDouble() * 0.12);
        }

        /// <summary>The search sticker clicked into.</summary>
        public void PlaySearchClick() => play(searchClick, 0.5, 1);

        /// <summary>A song confirmed: the record lifting off the wheel to be cued up.</summary>
        public void PlayWhoosh() => play(whooshSample, 0.55, 1);

        /// <summary>The cued record played: the big whoosh the ripple into gameplay rides on.</summary>
        public void PlayLaunch() => play(launchSample, 0.6, 1);

        /// <summary>The needle going down: opening the analyser.</summary>
        public void PlayNeedleDrop() => play(needleDrop, 0.55, 1);

        /// <summary>A difficulty cassette's deck key going down.</summary>
        public void PlayDeckKey() => play(deckKey, 0.55, 0.97 + random.NextDouble() * 0.06);

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
