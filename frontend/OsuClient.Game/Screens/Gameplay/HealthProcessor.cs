using System;

namespace OsuClient.Game.Screens.Gameplay
{
    /// <summary>
    /// Health (the HP bar): drains continuously while the map plays, refilled
    /// by hits and cut by misses, and the run is failed once it empties.
    ///
    /// osu!stable derives its exact drain rate by simulating the whole beatmap
    /// and binary-searching for a rate a perfect play barely survives. That's
    /// far more machinery than this needs, so the rates here are a
    /// straightforward scaling off HP drain rate instead — same shape of
    /// behaviour (higher HP drains faster and punishes misses harder), tuned
    /// so a run of missed objects fails in roughly the handful it takes in
    /// osu!, without claiming to reproduce stable's numbers exactly.
    ///
    /// Pure logic, no osu.Framework dependency, so it can be unit tested.
    /// </summary>
    public sealed class HealthProcessor
    {
        private const double starting_health = 1;

        private const double great_gain = 0.10;
        private const double ok_gain = 0.06;
        private const double meh_gain = 0.02;

        private const double tick_gain = 0.02;
        private const double tick_penalty = 0.04;

        /// <summary>Fraction of health lost per millisecond of play.</summary>
        private readonly double drainPerMillisecond;

        /// <summary>Health lost for a missed object.</summary>
        private readonly double missPenalty;

        public HealthProcessor(double hpDrainRate)
        {
            drainPerMillisecond = (0.005 + 0.003 * hpDrainRate) / 1000.0;

            // Tuned so a run of nothing but misses empties the bar in roughly
            // the 8-10 objects it takes in osu!, rather than the 5-6 a harsher
            // penalty would: on the denser generated maps that difference is
            // seconds of play versus failing almost immediately.
            missPenalty = 0.06 + 0.010 * hpDrainRate;
        }

        /// <summary>Current health, 0 (empty) to 1 (full).</summary>
        public double Health { get; private set; } = starting_health;

        /// <summary>
        /// Whether health has run out. Latched: recovering health after the
        /// bar empties doesn't un-fail a run, as in osu!.
        /// </summary>
        public bool HasFailed { get; private set; }

        /// <summary>Applies passive drain for a span of gameplay time.</summary>
        public void Drain(double elapsedMilliseconds)
        {
            if (elapsedMilliseconds <= 0)
                return;

            adjust(-drainPerMillisecond * elapsedMilliseconds);
        }

        /// <summary>Applies the health change for an object's judgement.</summary>
        public void Apply(HitResult result)
        {
            adjust(result switch
            {
                HitResult.Great => great_gain,
                HitResult.Ok => ok_gain,
                HitResult.Meh => meh_gain,
                _ => -missPenalty,
            });
        }

        /// <summary>Applies the health change for a slider tick, repeat or tail.</summary>
        public void ApplyTick(bool hit) => adjust(hit ? tick_gain : -tick_penalty);

        private void adjust(double amount)
        {
            Health = Math.Clamp(Health + amount, 0, 1);

            if (Health <= 0)
                HasFailed = true;
        }
    }
}
