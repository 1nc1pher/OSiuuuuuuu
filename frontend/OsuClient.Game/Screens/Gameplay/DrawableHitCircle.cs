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
    /// A hit circle: the filled body, its combo number and the approach ring
    /// that closes in on it, reaching the circle's edge exactly at the
    /// object's start time.
    /// </summary>
    public partial class DrawableHitCircle : DrawableHitObject
    {
        private const double exit_duration = 300;

        private readonly Container content;
        private readonly ApproachCircle approach;

        public DrawableHitCircle(HitCircleData data, BeatmapDifficulty difficulty, Color4 comboColour, int comboNumber)
            : base(data, difficulty, comboColour)
        {
            float diameter = CircleRadius * 2;

            InternalChild = content = new Container
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.Centre,
                Position = data.Position,
                Size = new Vector2(diameter),
                Children = new Drawable[]
                {
                    new HitCircleBody(diameter, comboColour, comboNumber),
                    approach = new ApproachCircle(diameter, comboColour),
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            using (BeginAbsoluteSequence(AppearTime))
            {
                this.FadeIn(FadeInDuration);

                // Linear, as in osu!: the ring's distance from the circle has
                // to read as time remaining, so it can only be trusted if it
                // closes at a constant rate. An eased curve spends almost all
                // of its travel up front — OutQuint is already down to 1.09x
                // at the halfway point — so the ring looks shut long before
                // the note is actually due.
                approach.ScaleTo(4f).Then().ScaleTo(1f, Preempt);
            }
        }

        public override bool TryPress(double time, Vector2 cursorScreenSpace)
        {
            if (IsJudged)
                return false;

            // Outside the hit window the press simply isn't for this object —
            // ignoring it is what lets a player click early without killing
            // the note they were aiming at.
            if (Math.Abs(time - StartTime) > MehWindow)
                return false;

            if (!CursorWithin(cursorScreenSpace, Data.Position, CircleRadius))
                return false;

            HitError = time - StartTime;

            ApplyJudgement(time, JudgementProcessor.Judge(time - StartTime, Difficulty.OverallDifficulty));
            return true;
        }

        public override void UpdateGameplay(double time, Vector2 cursorScreenSpace, bool anyKeyHeld)
        {
            if (!IsJudged && time > StartTime + MehWindow)
                ApplyJudgement(time, HitResult.Miss);
        }

        protected override double ApplyJudgementAnimation(HitResult result)
        {
            approach.FadeOut(50);

            if (result == HitResult.Miss)
            {
                content.FadeColour(new Color4(1f, 0.2f, 0.2f, 1f), 100);
                content.ScaleTo(0.8f, exit_duration, Easing.OutQuint);
            }
            else
            {
                content.ScaleTo(1.4f, exit_duration, Easing.OutQuint);
            }

            this.FadeOut(exit_duration, Easing.OutQuint);

            return exit_duration;
        }
    }
}
