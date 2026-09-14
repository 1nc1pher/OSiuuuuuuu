using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using OsuClient.Game.Beatmaps;
using OsuClient.Game.Beatmaps.HitObjects;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.Gameplay
{
    /// <summary>
    /// Base for every playable object. Subclasses fill in their own visuals,
    /// their own per-frame state and how they consume a key press.
    ///
    /// All timing is in gameplay time (see <see cref="GameplayClock"/>), which
    /// is also the clock these drawables run their transforms on, so a
    /// transform scheduled at a hit object time lines up with the judgement
    /// for that same time.
    ///
    /// Lifecycle is driven by <see cref="PlayerScreen"/> rather than by the
    /// framework's lifetime system: it calls <see cref="UpdateGameplay"/> every
    /// frame and removes the object once <see cref="IsReadyForRemoval"/>.
    /// </summary>
    public abstract partial class DrawableHitObject : CompositeDrawable
    {
        /// <summary>Fired once, when the object's overall judgement is decided.</summary>
        public event Action<DrawableHitObject, HitResult>? Judged;

        /// <summary>
        /// Fired for each slider tick, repeat and tail as it passes: these
        /// carry combo and a little score but don't count toward accuracy.
        /// </summary>
        public event Action<DrawableHitObject, bool>? TickJudged;

        /// <summary>
        /// Fired for each full extra rotation a spinner completes past what
        /// was required — a reward for continuing to spin after the bar
        /// fills, purely additional score with no bearing on the object's own
        /// judgement (and, like a tick, no bearing on accuracy either).
        /// </summary>
        public event Action<DrawableHitObject>? BonusAwarded;

        public HitObjectData Data { get; }

        public bool IsJudged { get; private set; }

        /// <summary>
        /// Signed milliseconds between the press that hit this object and the
        /// time it should have been hit — negative early, positive late. Null
        /// until a press actually lands, and on objects that are never hit by
        /// a timed press at all (a spinner, or one that expires unhit).
        /// </summary>
        public double? HitError { get; protected set; }

        public double StartTime => Data.StartTime;

        public double EndTime => Data.EndTime;

        protected readonly BeatmapDifficulty Difficulty;
        protected readonly Color4 ComboColour;

        private double? removalTime;

        protected DrawableHitObject(HitObjectData data, BeatmapDifficulty difficulty, Color4 comboColour)
        {
            Data = data;
            Difficulty = difficulty;
            ComboColour = comboColour;

            RelativeSizeAxes = Axes.Both;
            Alpha = 0;

            // Objects spend most of their life fully transparent (before fading
            // in, and while fading out after a judgement). A Drawable that
            // isn't present stops being updated entirely, which would freeze
            // the very transforms and state updates that bring it back.
            AlwaysPresent = true;
        }

        protected double Preempt => JudgementProcessor.Preempt(Difficulty.ApproachRate);

        protected double FadeInDuration => JudgementProcessor.FadeIn(Difficulty.ApproachRate);

        protected double MehWindow => JudgementProcessor.MehWindow(Difficulty.OverallDifficulty);

        protected float CircleRadius => (float)Difficulty.CircleRadius;

        /// <summary>Gameplay time at which this object should first appear.</summary>
        public double AppearTime => StartTime - Preempt;

        /// <summary>Whether a press should be offered to this object at all.</summary>
        public virtual bool AcceptsPress => !IsJudged;

        /// <summary>Whether the object is finished and can be dropped from the playfield.</summary>
        public bool IsReadyForRemoval(double time) => removalTime is double t && time >= t;

        /// <summary>
        /// Offers a hit key press. Returns true if this object consumed it —
        /// a press that lands outside the object's hit window or away from it
        /// must be ignored (returning false) rather than judged, matching
        /// osu!: clicking early doesn't destroy the note.
        /// </summary>
        public abstract bool TryPress(double time, Vector2 cursorScreenSpace);

        /// <summary>Notifies the object that all hit keys were released.</summary>
        public virtual void OnRelease(double time)
        {
        }

        /// <summary>Per-frame state update: expiry, tracking, animation driven by gameplay time.</summary>
        public abstract void UpdateGameplay(double time, Vector2 cursorScreenSpace, bool anyKeyHeld);

        /// <summary>Records the object's overall judgement and starts its exit animation.</summary>
        protected void ApplyJudgement(double time, HitResult result)
        {
            if (IsJudged)
                return;

            IsJudged = true;
            Judged?.Invoke(this, result);

            removalTime = time + ApplyJudgementAnimation(result);
        }

        /// <summary>Reports a slider tick/repeat/tail outcome.</summary>
        protected void ApplyTick(bool hit) => TickJudged?.Invoke(this, hit);

        /// <summary>Reports one full extra spin completed past a spinner's requirement.</summary>
        protected void ApplyBonus() => BonusAwarded?.Invoke(this);

        /// <summary>Plays the hit or miss animation. Returns how long until the object can be removed.</summary>
        protected abstract double ApplyJudgementAnimation(HitResult result);

        /// <summary>True if the cursor is within <paramref name="radius"/> osu!pixels of a playfield position.</summary>
        protected bool CursorWithin(Vector2 cursorScreenSpace, Vector2 playfieldPosition, float radius)
        {
            Vector2 local = ToLocalSpace(cursorScreenSpace);

            return Vector2.Distance(local, playfieldPosition) <= radius;
        }
    }
}
