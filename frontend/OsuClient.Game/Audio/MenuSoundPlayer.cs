using System;
using System.Collections.Generic;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;

namespace OsuClient.Game.Audio
{
    /// <summary>
    /// Loads the menu's synthesized sounds once and plays them by name — the
    /// same shape as <see cref="HitSoundPlayer"/>, and for the same reason:
    /// the synthesis happens at startup, not on every press.
    ///
    /// The panels that open over the menu borrow the deck's own sounds rather
    /// than inventing new ones: the song list is a cassette case opening, the
    /// credits a tape going into the bay, and a pick is a deck key going
    /// down. The same few mechanical sounds then mean the same few things
    /// wherever the player meets them.
    /// </summary>
    public sealed class MenuSoundPlayer
    {
        private const string whoosh = "whoosh";
        private const string transition_from_right = "transition-right";
        private const string transition_from_left = "transition-left";
        private const string case_open = "case-open";
        private const string case_close = "case-close";
        private const string tape_insert = "tape-insert";
        private const string deck_key = "deck-key";
        private const string detent = "detent";

        /// <summary>
        /// Closest two list ticks may start. A flicked scroll wheel can cross
        /// several rows in a frame, and one tick per row at that rate fuses
        /// into a buzz where a few per spin still read as notches.
        /// </summary>
        private const double min_tick_gap = 45;

        private readonly Sample? whooshSample;
        private readonly Sample? transitionFromRight;
        private readonly Sample? transitionFromLeft;
        private readonly Sample? caseOpen;
        private readonly Sample? caseClose;
        private readonly Sample? tapeInsert;
        private readonly Sample? deckKey;
        private readonly Sample? detentSample;

        private readonly Random random = new Random();

        private double lastTickAt = double.NegativeInfinity;

        public MenuSoundPlayer(AudioManager audio)
        {
            var store = new InMemorySampleStore(new Dictionary<string, byte[]>
            {
                [whoosh] = MenuSoundSynth.Whoosh(),
                [transition_from_right] = MenuSoundSynth.TransitionWhoosh(fromRight: true),
                [transition_from_left] = MenuSoundSynth.TransitionWhoosh(fromRight: false),
                [case_open] = TapeDeckSoundSynth.CaseOpen(),
                [case_close] = TapeDeckSoundSynth.CaseClose(),
                [tape_insert] = TapeDeckSoundSynth.TapeInsert(),
                [deck_key] = SongSelectSoundSynth.DeckKey(),
                [detent] = TapeDeckSoundSynth.KnobDetent(),
            });

            var samples = audio.GetSampleStore(store);

            whooshSample = samples.Get(whoosh);
            transitionFromRight = samples.Get(transition_from_right);
            transitionFromLeft = samples.Get(transition_from_left);
            caseOpen = samples.Get(case_open);
            caseClose = samples.Get(case_close);
            tapeInsert = samples.Get(tape_insert);
            deckKey = samples.Get(deck_key);
            detentSample = samples.Get(detent);
        }

        /// <summary>The sound of the strip sliding out from behind the logo.</summary>
        public void PlayWhoosh() => play(whooshSample, 0.55, 1);

        /// <summary>The sound of a screen change, travelling the way the ripple does.</summary>
        public void PlayTransition(bool fromRight) =>
            play(fromRight ? transitionFromRight : transitionFromLeft, 0.6, 1);

        /// <summary>The song list swinging open: a cassette case's lid.</summary>
        public void PlayListOpen() => play(caseOpen, 0.5, 1);

        /// <summary>A panel put away: the lid shutting again.</summary>
        public void PlayClose() => play(caseClose, 0.5, 1);

        /// <summary>A song picked from the list: a deck key going down.</summary>
        public void PlayPick() => play(deckKey, 0.55, 0.97 + random.NextDouble() * 0.06);

        /// <summary>The credits opening: a cassette sliding into the bay.</summary>
        public void PlayCredits() => play(tapeInsert, 0.5, 1);

        /// <summary>
        /// One row of the list passing under the pointer. <paramref name="now"/>
        /// is the caller's clock time, used only to space ticks out.
        /// </summary>
        public void PlayTick(double now)
        {
            if (now - lastTickAt < min_tick_gap)
                return;

            lastTickAt = now;

            // A little pitch drift per tick, so a spin sounds like a knob
            // turning rather than one sample on repeat.
            play(detentSample, 0.4, 0.94 + random.NextDouble() * 0.12);
        }

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
