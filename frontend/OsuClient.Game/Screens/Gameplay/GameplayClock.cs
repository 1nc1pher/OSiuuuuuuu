using System;
using osu.Framework.Timing;

namespace OsuClient.Game.Screens.Gameplay
{
    /// <summary>
    /// The clock everything in gameplay runs on: hit object transforms, hit
    /// windows and judgement all read from it, so what you see, what you hear
    /// and what gets judged can't drift apart.
    ///
    /// It free-runs on wall time during the lead-in (before the track starts,
    /// when time is still negative), then follows the audio track, which is
    /// the authority once playing. Following is smoothed rather than snapped:
    /// BASS reports its position in coarse steps, and assigning those directly
    /// every frame makes objects visibly stutter.
    /// </summary>
    public sealed class GameplayClock
    {
        /// <summary>Drift beyond which the clock hard-seeks instead of easing (audio hiccup, or the track just started).</summary>
        private const double max_drift = 40;

        /// <summary>Fraction of the remaining drift corrected per frame while within <see cref="max_drift"/>.</summary>
        private const double drift_correction = 0.2;

        private readonly ManualClock manual = new ManualClock();
        private readonly FramedClock framed;

        public GameplayClock(double startTime)
        {
            manual.CurrentTime = startTime;
            manual.IsRunning = true;

            framed = new FramedClock(manual, false);
        }

        /// <summary>Assign this to the gameplay drawables so their transforms run on gameplay time.</summary>
        public IFrameBasedClock FrameClock => framed;

        /// <summary>Milliseconds into the audio track. Negative during the lead-in.</summary>
        public double CurrentTime => manual.CurrentTime;

        /// <summary>
        /// Advances the clock by one frame.
        /// </summary>
        /// <param name="elapsedRealTime">Wall-clock milliseconds since the last frame.</param>
        /// <param name="audioTime">
        /// The track's current position, or null while it isn't playing (during
        /// the lead-in, or when the beatmap has no audio at all).
        /// </param>
        public void Advance(double elapsedRealTime, double? audioTime)
        {
            double time = manual.CurrentTime + elapsedRealTime;

            if (audioTime is double audio)
            {
                double drift = audio - time;

                if (Math.Abs(drift) > max_drift)
                    time = audio;
                else
                    time += drift * drift_correction;
            }

            manual.CurrentTime = time;
        }
    }
}
