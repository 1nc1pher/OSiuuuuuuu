using System;

namespace OsuClient.Game.Screens.Gameplay
{
    /// <summary>The outcome of judging one hit circle.</summary>
    public enum HitResult
    {
        Miss,
        Meh,   // 50
        Ok,    // 100
        Great, // 300
    }

    /// <summary>
    /// Pure osu!-standard timing maths: hit windows from Overall Difficulty,
    /// preempt/fade-in from Approach Rate. No osu.Framework dependency, so it
    /// can be unit tested directly.
    /// </summary>
    public static class JudgementProcessor
    {
        /// <summary>Half-width of the 300 (Great) hit window, in milliseconds.</summary>
        public static double GreatWindow(double overallDifficulty) => 80 - 6 * overallDifficulty;

        /// <summary>Half-width of the 100 (Ok) hit window, in milliseconds.</summary>
        public static double OkWindow(double overallDifficulty) => 140 - 8 * overallDifficulty;

        /// <summary>Half-width of the 50 (Meh) hit window, in milliseconds. A hit circle
        /// expires into a miss once this window closes.</summary>
        public static double MehWindow(double overallDifficulty) => 200 - 10 * overallDifficulty;

        /// <summary>Judges a hit given its signed offset from the object's start time.</summary>
        public static HitResult Judge(double hitDelta, double overallDifficulty)
        {
            double abs = Math.Abs(hitDelta);

            if (abs <= GreatWindow(overallDifficulty))
                return HitResult.Great;
            if (abs <= OkWindow(overallDifficulty))
                return HitResult.Ok;
            if (abs <= MehWindow(overallDifficulty))
                return HitResult.Meh;

            return HitResult.Miss;
        }

        /// <summary>Milliseconds before an object's start time that its approach circle begins closing in.</summary>
        public static double Preempt(double approachRate) =>
            approachRate <= 5
                ? 1200 + 600 * (5 - approachRate) / 5
                : 1200 - 750 * (approachRate - 5) / 5;

        /// <summary>Milliseconds the object takes to fade in once it appears.</summary>
        public static double FadeIn(double approachRate) =>
            approachRate <= 5
                ? 800 + 400 * (5 - approachRate) / 5
                : 800 - 500 * (approachRate - 5) / 5;

        /// <summary>Base score value of a judgement, before combo scaling.</summary>
        public static int ScoreValue(HitResult result) => result switch
        {
            HitResult.Great => 300,
            HitResult.Ok => 100,
            HitResult.Meh => 50,
            _ => 0,
        };

        /// <summary>
        /// osu!'s difficulty scaling: <paramref name="min"/> at difficulty 0,
        /// <paramref name="mid"/> at 5, <paramref name="max"/> at 10.
        /// </summary>
        public static double DifficultyRange(double difficulty, double min, double mid, double max)
        {
            if (difficulty > 5)
                return mid + (max - mid) * (difficulty - 5) / 5;

            if (difficulty < 5)
                return mid - (mid - min) * (5 - difficulty) / 5;

            return mid;
        }

        /// <summary>How fast a spinner has to be spun, in spins per minute.</summary>
        public static double SpinsPerMinute(double overallDifficulty) =>
            DifficultyRange(overallDifficulty, 90, 150, 225);

        /// <summary>
        /// How far the cursor may stray from the slider ball before tracking
        /// breaks, as a multiple of the circle radius.
        /// </summary>
        public const double FollowCircleRadiusMultiplier = 2.4;

        /// <summary>
        /// A slider's overall judgement, from how many of its parts (head,
        /// ticks, repeats, tail) were collected: everything is a 300, at least
        /// half is a 100, anything at all is a 50.
        /// </summary>
        public static HitResult JudgeSlider(int partsHit, int totalParts)
        {
            if (totalParts <= 0 || partsHit <= 0)
                return HitResult.Miss;

            if (partsHit >= totalParts)
                return HitResult.Great;

            return partsHit * 2 >= totalParts ? HitResult.Ok : HitResult.Meh;
        }

        /// <summary>A spinner's judgement from how much of its required spinning was completed.</summary>
        public static HitResult JudgeSpinner(double completionRatio)
        {
            if (completionRatio >= 1)
                return HitResult.Great;

            if (completionRatio >= 0.9)
                return HitResult.Ok;

            if (completionRatio >= 0.75)
                return HitResult.Meh;

            return HitResult.Miss;
        }
    }
}
