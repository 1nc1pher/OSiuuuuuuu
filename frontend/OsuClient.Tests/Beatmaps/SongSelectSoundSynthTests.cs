using System;
using NUnit.Framework;
using OsuClient.Game.Audio;

namespace OsuClient.Tests.Beatmaps
{
    /// <summary>
    /// Song select's synthesized sounds: each has to be short enough that a
    /// spin or a run of clicks stays a texture, loud enough to hear, clean of
    /// clipping — and the record's scratch has to move the way the record
    /// does, rising when it turns forward and falling when it turns back.
    /// </summary>
    [TestFixture]
    public class SongSelectSoundSynthTests
    {
        private const int header_size = 44;
        private const int sample_rate = 44100;

        private static readonly Func<byte[]>[] all_sounds =
        {
            () => SongSelectSoundSynth.VinylTurn(forward: true),
            () => SongSelectSoundSynth.VinylTurn(forward: false),
            SongSelectSoundSynth.SearchClick,
            SongSelectSoundSynth.DeckKey,
            SongSelectSoundSynth.NeedleDrop,
        };

        [TestCaseSource(nameof(all_sounds))]
        public void TestShortAudibleAndUnclipped(Func<byte[]> make)
        {
            short[] samples = decode(make());

            Assert.That(samples.Length / (double)sample_rate, Is.LessThan(0.6), "short");

            int peak = 0;

            foreach (short sample in samples)
                peak = Math.Max(peak, Math.Abs((int)sample));

            Assert.That(peak, Is.GreaterThan(short.MaxValue / 2), "audible");
            Assert.That(peak, Is.LessThan(short.MaxValue), "unclipped");
        }

        [Test]
        public void TestScratchRisesForwardAndFallsBack()
        {
            short[] forward = decode(SongSelectSoundSynth.VinylTurn(forward: true));
            short[] back = decode(SongSelectSoundSynth.VinylTurn(forward: false));

            // Brightness by zero-crossing rate, first half against second:
            // crude, but a band-pass sweeping from 0.9 to 2.6 kHz moves it a
            // long way either direction.
            Assert.That(crossings(forward, true), Is.LessThan(crossings(forward, false)), "forward rises");
            Assert.That(crossings(back, true), Is.GreaterThan(crossings(back, false)), "back falls");
        }

        private static short[] decode(byte[] wav)
        {
            var samples = new short[(wav.Length - header_size) / 2];

            for (int i = 0; i < samples.Length; i++)
                samples[i] = BitConverter.ToInt16(wav, header_size + i * 2);

            return samples;
        }

        /// <summary>Zero crossings in the first or second half, skipping the opening click.</summary>
        private static int crossings(short[] samples, bool firstHalf)
        {
            int skip = sample_rate / 200;
            int half = samples.Length / 2;
            int start = firstHalf ? skip : half;
            int end = firstHalf ? half : samples.Length;
            int count = 0;

            for (int i = start + 1; i < end; i++)
            {
                if ((samples[i - 1] < 0) != (samples[i] < 0))
                    count++;
            }

            return count;
        }
    }
}
