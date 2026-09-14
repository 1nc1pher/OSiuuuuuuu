using System;

namespace OsuClient.Game.Screens.Gameplay
{
    /// <summary>The letter rank a play earns, worst to best.</summary>
    public enum Grade
    {
        D,
        C,
        B,
        A,
        S,
        SS,
    }

    /// <summary>
    /// Combo, accuracy and score bookkeeping for one play. Pure logic, no
    /// osu.Framework dependency, so it can be unit tested directly.
    /// </summary>
    public class ScoreProcessor
    {
        public int Combo { get; private set; }
        public int MaxCombo { get; private set; }
        public long Score { get; private set; }

        public int CountGreat { get; private set; }
        public int CountOk { get; private set; }
        public int CountMeh { get; private set; }
        public int CountMiss { get; private set; }

        public int TotalJudged => CountGreat + CountOk + CountMeh + CountMiss;

        /// <summary>Accuracy as a percentage in [0, 100]. 100 when nothing has been judged yet.</summary>
        public double Accuracy
        {
            get
            {
                if (TotalJudged == 0)
                    return 100;

                double achieved = CountGreat * 300 + CountOk * 100 + CountMeh * 50;
                return achieved / (TotalJudged * 300.0) * 100.0;
            }
        }

        /// <summary>
        /// The play's letter rank, by osu!standard's rules: an SS is every
        /// object a 300, then the tiers step down on the share of 300s, with
        /// a clean (no-miss) run promoted a tier over one with misses.
        /// </summary>
        public Grade Grade
        {
            get
            {
                if (TotalJudged == 0)
                    return Grade.D;

                if (CountGreat == TotalJudged)
                    return Grade.SS;

                double greatRatio = CountGreat / (double)TotalJudged;
                double mehRatio = CountMeh / (double)TotalJudged;

                if (greatRatio > 0.9 && mehRatio <= 0.01 && CountMiss == 0)
                    return Grade.S;

                if ((greatRatio > 0.8 && CountMiss == 0) || greatRatio > 0.9)
                    return Grade.A;

                if ((greatRatio > 0.7 && CountMiss == 0) || greatRatio > 0.8)
                    return Grade.B;

                if (greatRatio > 0.6)
                    return Grade.C;

                return Grade.D;
            }
        }

        /// <summary>Records one judgement, updating combo/accuracy/score.</summary>
        public void Apply(HitResult result)
        {
            switch (result)
            {
                case HitResult.Great: CountGreat++; break;
                case HitResult.Ok: CountOk++; break;
                case HitResult.Meh: CountMeh++; break;
                case HitResult.Miss: CountMiss++; break;
            }

            if (result == HitResult.Miss)
            {
                Combo = 0;
                return;
            }

            Combo++;
            MaxCombo = Math.Max(MaxCombo, Combo);

            // A simplified combo-scaled score — not osu!'s exact ranking-score
            // formula, but enough to make combo feel rewarding for MVP testing.
            Score += (long)(JudgementProcessor.ScoreValue(result) * (1 + Combo / 10.0));
        }

        /// <summary>
        /// Records a slider tick, repeat or tail. These carry combo and a
        /// little score but, as in osu!, don't count toward accuracy — the
        /// slider's own overall judgement does that.
        /// </summary>
        public void ApplyTick(bool hit)
        {
            if (!hit)
            {
                Combo = 0;
                return;
            }

            Combo++;
            MaxCombo = Math.Max(MaxCombo, Combo);
            Score += 10;
        }

        /// <summary>
        /// Records one full extra spin a player kept going for after a
        /// spinner's bar already filled. Worth more than a slider tick —
        /// there's no way to earn one without first finishing the spinner
        /// outright, so unlike a tick it can never be missed, only chased for
        /// more. Carries combo the same way a tick does, and likewise doesn't
        /// count toward accuracy.
        /// </summary>
        public void ApplyBonus()
        {
            Combo++;
            MaxCombo = Math.Max(MaxCombo, Combo);
            Score += 100;
        }
    }
}
