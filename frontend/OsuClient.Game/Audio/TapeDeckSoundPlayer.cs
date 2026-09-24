using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
using osu.Framework.Graphics;

namespace OsuClient.Game.Audio
{
    /// <summary>
    /// Plays the tape-deck sounds of the create flow and the analyser — see
    /// <see cref="TapeDeckSoundSynth"/>.
    ///
    /// <para>
    /// A <see cref="Component"/> rather than a plain class like the other
    /// players, for the transport hum: it loops, fades in and out, and has to
    /// stop when the screen it belongs to goes away, all of which want a place
    /// in the drawable tree.
    /// </para>
    ///
    /// <para>
    /// The per-item sounds of the reveal — an onset, a beat line, a placed
    /// object — arrive hundreds at a time. Each kind has a minimum spacing, so
    /// a dense stretch becomes a crackle and a sparse one stays individual
    /// ticks, rather than everything fusing into a buzz.
    /// </para>
    /// </summary>
    public partial class TapeDeckSoundPlayer : Component
    {
        private const double transport_volume = 0.2;

        private const double onset_gap = 32;
        private const double beat_gap = 55;
        private const double place_gap = 35;
        private const double scrub_gap = 45;
        private const double detent_gap = 22;

        [Resolved]
        private AudioManager audio { get; set; } = null!;

        private readonly Dictionary<string, Sample?> samples = new Dictionary<string, Sample?>();
        private readonly Random random = new Random();

        private SampleChannel? transport;

        private double lastOnset = double.NegativeInfinity;
        private double lastBeat = double.NegativeInfinity;
        private double lastPlace = double.NegativeInfinity;
        private double lastScrub = double.NegativeInfinity;
        private double lastDetent = double.NegativeInfinity;

        [BackgroundDependencyLoader]
        private void load()
        {
            var sources = new Dictionary<string, byte[]>
            {
                ["key"] = SongSelectSoundSynth.DeckKey(),
                ["insert"] = TapeDeckSoundSynth.TapeInsert(),
                ["case-close"] = TapeDeckSoundSynth.CaseClose(),
                ["case-open"] = TapeDeckSoundSynth.CaseOpen(),
                ["spine-in"] = TapeDeckSoundSynth.SpineIn(),
                ["spine-out"] = TapeDeckSoundSynth.SpineOut(),
                ["record"] = TapeDeckSoundSynth.RecordEngage(),
                ["transport"] = TapeDeckSoundSynth.TransportLoop(),
                ["auto-stop"] = TapeDeckSoundSynth.AutoStop(),
                ["fast-forward"] = TapeDeckSoundSynth.FastForward(),
                ["relay"] = TapeDeckSoundSynth.Relay(),
                ["counter"] = TapeDeckSoundSynth.CounterTick(),
                ["onset"] = TapeDeckSoundSynth.OnsetTick(),
                ["place"] = TapeDeckSoundSynth.PlaceBlip(),
                ["ready"] = TapeDeckSoundSynth.ReadyBeep(),
                ["refuse"] = TapeDeckSoundSynth.Refuse(),
                ["scrub"] = TapeDeckSoundSynth.Scrub(),
                ["pin-grab"] = TapeDeckSoundSynth.PinGrab(),
                ["pin-drop"] = TapeDeckSoundSynth.PinDrop(),
                ["detent"] = TapeDeckSoundSynth.KnobDetent(),
                ["flick"] = TapeDeckSoundSynth.SwitchFlick(),
                ["rewind"] = TapeDeckSoundSynth.Rewind(),
            };

            var store = audio.GetSampleStore(new InMemorySampleStore(sources));

            foreach (string name in sources.Keys)
                samples[name] = store.Get(name);
        }

        /// <summary>A transport key — LOAD TAPE, LOAD COVER, the finale's buttons.</summary>
        public void PlayKey() => play("key", 0.5, 0.97 + random.NextDouble() * 0.06);

        /// <summary>A tape going into the bay.</summary>
        public void PlayTapeInsert() => play("insert", 0.55);

        /// <summary>Cover art going into its case.</summary>
        public void PlayCaseClose() => play("case-close", 0.5);

        /// <summary>Cover art coming out of its case.</summary>
        public void PlayCaseOpen() => play("case-open", 0.45);

        /// <summary>A difficulty cassette pushed in (armed) or pulled out.</summary>
        public void PlaySpine(bool pushedIn) => play(pushedIn ? "spine-in" : "spine-out", 0.45, 0.96 + random.NextDouble() * 0.08);

        /// <summary>RECORD going down, and the tape starting to move.</summary>
        public void PlayRecord() => play("record", 0.6);

        /// <summary>The end of a run: the key popping up, the motor winding down.</summary>
        public void PlayAutoStop() => play("auto-stop", 0.5);

        /// <summary>Skipping ahead through the reveal.</summary>
        public void PlayFastForward() => play("fast-forward", 0.45);

        /// <summary>A stage starting.</summary>
        public void PlayRelay() => play("relay", 0.35, 0.95 + random.NextDouble() * 0.1);

        /// <summary>A counter stepping on — the difficulty being worked on.</summary>
        public void PlayCounter() => play("counter", 0.4);

        /// <summary>The map is ready.</summary>
        public void PlayReady() => play("ready", 0.35);

        /// <summary>Something was refused: a file it can't use, a picker that failed.</summary>
        public void PlayRefuse() => play("refuse", 0.4);

        /// <summary>
        /// An onset found, pitched by the band it was strongest in: low ticks
        /// low, highs tick high, so a sweep through the song is heard as well
        /// as seen.
        /// </summary>
        public void PlayOnset(string band)
        {
            if (!spaced(ref lastOnset, onset_gap))
                return;

            double pitch = band switch
            {
                "low" => 0.72,
                "high" => 1.4,
                _ => 1.0,
            };

            play("onset", 0.22, pitch * (0.96 + random.NextDouble() * 0.08));
        }

        /// <summary>A beat line of the grid laid down.</summary>
        public void PlayBeat()
        {
            if (spaced(ref lastBeat, beat_gap))
                play("counter", 0.2, 1.1 + random.NextDouble() * 0.05);
        }

        /// <summary>A hit object placed, pitched by what it is.</summary>
        public void PlayPlace(string kind)
        {
            if (!spaced(ref lastPlace, place_gap))
                return;

            double pitch = kind switch
            {
                "slider" => 0.84,
                "spinner" => 0.67,
                _ => 1.0,
            };

            play("place", 0.2, pitch);
        }

        /// <summary>
        /// A grain of tape scrubbed across the head. <paramref name="speed"/>
        /// is how fast the playhead is being dragged, 0 to 1: faster is higher
        /// and louder, as tape pulled faster past a head is.
        /// </summary>
        public void PlayScrub(double speed)
        {
            if (!spaced(ref lastScrub, scrub_gap))
                return;

            speed = Math.Clamp(speed, 0, 1);
            play("scrub", 0.18 + 0.2 * speed, 0.7 + 0.9 * speed + random.NextDouble() * 0.05);
        }

        /// <summary>The playhead's pin picked up.</summary>
        public void PlayPinGrab() => play("pin-grab", 0.4);

        /// <summary>The playhead's pin set down.</summary>
        public void PlayPinDrop() => play("pin-drop", 0.4);

        /// <summary>A playhead cued by a single click on the strip.</summary>
        public void PlayCue() => play("relay", 0.35, 1.1);

        /// <summary>
        /// One detent of a knob. <paramref name="position"/> is where the knob
        /// is, 0 to 1 — the click climbs as it turns up, so a sweep is heard
        /// going somewhere.
        /// </summary>
        public void PlayDetent(double position)
        {
            if (spaced(ref lastDetent, detent_gap))
                play("detent", 0.32, 0.8 + 0.6 * Math.Clamp(position, 0, 1));
        }

        /// <summary>A toggle switch flicked on or off.</summary>
        public void PlayFlick(bool on) => play("flick", 0.4, on ? 1.05 : 0.92);

        /// <summary>The tape rewinding to the top.</summary>
        public void PlayRewind() => play("rewind", 0.4);

        /// <summary>Starts the transport hum, fading up. Does nothing if it is already running.</summary>
        public void StartTransport()
        {
            if (transport != null || !samples.TryGetValue("transport", out var sample) || sample == null)
                return;

            var channel = sample.GetChannel();

            channel.Looping = true;
            channel.Volume.Value = 0;
            channel.Play();

            transport = channel;

            this.TransformBindableTo(channel.Volume, transport_volume, 600, Easing.OutQuad);
        }

        /// <summary>Fades the transport hum out and stops it.</summary>
        public void StopTransport()
        {
            var channel = transport;

            if (channel == null)
                return;

            transport = null;

            this.TransformBindableTo(channel.Volume, 0, 250, Easing.OutQuad)
                .OnComplete(_ => channel.Stop());
        }

        /// <summary>Whether the transport hum is running. Exposed for tests.</summary>
        public bool TransportRunning => transport != null;

        private bool spaced(ref double last, double gap)
        {
            if (Time.Current - last < gap)
                return false;

            last = Time.Current;
            return true;
        }

        private void play(string name, double volume, double frequency = 1)
        {
            if (!samples.TryGetValue(name, out var sample) || sample == null)
                return;

            var channel = sample.GetChannel();
            channel.Volume.Value = volume;
            channel.Frequency.Value = frequency;
            channel.Play();
        }

        protected override void Dispose(bool isDisposing)
        {
            // Leaving mid-run: a looping channel outlives everything unless
            // someone stops it.
            transport?.Stop();
            transport = null;

            base.Dispose(isDisposing);
        }
    }
}
