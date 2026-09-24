using System;
using System.Linq;
using NUnit.Framework;
using OsuClient.Game.Audio;

namespace OsuClient.Tests.Generation
{
    /// <summary>
    /// The create flow's sounds: each one-shot short, audible and clean of
    /// clipping, and the transport hum able to loop without a click where
    /// its end meets its start.
    /// </summary>
    [TestFixture]
    public class TapeDeckSoundSynthTests
    {
        private const int header_size = 44;

        private static readonly Func<byte[]>[] one_shots =
        {
            TapeDeckSoundSynth.TapeInsert,
            TapeDeckSoundSynth.CaseClose,
            TapeDeckSoundSynth.CaseOpen,
            TapeDeckSoundSynth.SpineIn,
            TapeDeckSoundSynth.SpineOut,
            TapeDeckSoundSynth.RecordEngage,
            TapeDeckSoundSynth.AutoStop,
            TapeDeckSoundSynth.FastForward,
            TapeDeckSoundSynth.Relay,
            TapeDeckSoundSynth.CounterTick,
            TapeDeckSoundSynth.OnsetTick,
            TapeDeckSoundSynth.PlaceBlip,
            TapeDeckSoundSynth.ReadyBeep,
            TapeDeckSoundSynth.Refuse,
            TapeDeckSoundSynth.Scrub,
            TapeDeckSoundSynth.PinGrab,
            TapeDeckSoundSynth.PinDrop,
            TapeDeckSoundSynth.KnobDetent,
            TapeDeckSoundSynth.SwitchFlick,
            TapeDeckSoundSynth.Rewind,
        };

        [TestCaseSource(nameof(one_shots))]
        public void TestOneShotsAreShortAudibleAndUnclipped(Func<byte[]> make)
        {
            short[] samples = decode(make());
            int peak = samples.Max(s => Math.Abs((int)s));

            Assert.That(samples.Length / 44100.0, Is.LessThan(0.75), "short");
            Assert.That(peak, Is.GreaterThan(short.MaxValue / 2), "audible");
            Assert.That(peak, Is.LessThan(short.MaxValue), "unclipped");
        }

        [Test]
        public void TestTheTransportHumLoopsWithoutASeam()
        {
            short[] samples = decode(TapeDeckSoundSynth.TransportLoop());

            // The jump across the join, against the largest step anywhere
            // inside the loop: a seam shows up as a step bigger than any the
            // sound itself makes.
            int seam = Math.Abs(samples[^1] - samples[0]);
            int largestStep = Enumerable.Range(1, samples.Length - 1).Max(i => Math.Abs(samples[i] - samples[i - 1]));

            Assert.That(seam, Is.LessThanOrEqualTo(largestStep));
        }

        private static short[] decode(byte[] wav)
        {
            var samples = new short[(wav.Length - header_size) / 2];

            for (int i = 0; i < samples.Length; i++)
                samples[i] = BitConverter.ToInt16(wav, header_size + i * 2);

            return samples;
        }
    }
}
