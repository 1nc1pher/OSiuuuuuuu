using System;
using NUnit.Framework;
using OsuClient.Game.Audio;

namespace OsuClient.Tests.MainMenu
{
    /// <summary>
    /// The screen-change whoosh has to travel the way its ripple does: PLAY's
    /// comes in from the right and leaves to the left, CREATE's the reverse.
    /// A swapped pan would sound fine on its own and wrong against the
    /// picture, which no amount of listening in isolation would catch.
    /// </summary>
    [TestFixture]
    public class TransitionWhooshTests
    {
        private const int header_size = 44;

        [Test]
        public void TestIsStereo()
        {
            byte[] wav = MenuSoundSynth.TransitionWhoosh(fromRight: true);

            Assert.That(BitConverter.ToInt16(wav, 22), Is.EqualTo(2), "channel count");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void TestTravelsAwayFromTheSideItStartsOn(bool fromRight)
        {
            var (left, right) = channels(MenuSoundSynth.TransitionWhoosh(fromRight));

            int fifth = left.Length / 5;

            double startLeft = energy(left, 0, fifth);
            double startRight = energy(right, 0, fifth);
            double endLeft = energy(left, left.Length - fifth, fifth);
            double endRight = energy(right, right.Length - fifth, fifth);

            if (fromRight)
            {
                Assert.That(startRight, Is.GreaterThan(startLeft * 2), "starts on the right");
                Assert.That(endLeft, Is.GreaterThan(endRight * 2), "ends on the left");
            }
            else
            {
                Assert.That(startLeft, Is.GreaterThan(startRight * 2), "starts on the left");
                Assert.That(endRight, Is.GreaterThan(endLeft * 2), "ends on the right");
            }
        }

        [Test]
        public void TestDoesNotClip()
        {
            var (left, right) = channels(MenuSoundSynth.TransitionWhoosh(fromRight: true));

            foreach (var channel in new[] { left, right })
            {
                foreach (short sample in channel)
                    Assert.That(Math.Abs((int)sample), Is.LessThan(short.MaxValue));
            }
        }

        private static (short[] Left, short[] Right) channels(byte[] wav)
        {
            int frames = (wav.Length - header_size) / 4;
            var left = new short[frames];
            var right = new short[frames];

            for (int i = 0; i < frames; i++)
            {
                left[i] = BitConverter.ToInt16(wav, header_size + i * 4);
                right[i] = BitConverter.ToInt16(wav, header_size + i * 4 + 2);
            }

            return (left, right);
        }

        private static double energy(short[] samples, int start, int count)
        {
            double sum = 0;

            for (int i = start; i < start + count; i++)
                sum += (double)samples[i] * samples[i];

            return sum / count;
        }
    }
}
