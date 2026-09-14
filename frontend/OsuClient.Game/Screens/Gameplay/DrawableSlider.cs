using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Lines;
using osu.Framework.Graphics.Shapes;
using OsuClient.Game.Beatmaps;
using OsuClient.Game.Beatmaps.HitObjects;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.Gameplay
{
    /// <summary>
    /// A slider: a head circle judged like a hit circle, then a ball that runs
    /// the path (back and forth if the slider repeats) which has to be
    /// followed with a held key to collect the ticks, repeats and tail along
    /// the way.
    ///
    /// The overall 300/100/50 comes from how many of those parts were
    /// collected (<see cref="JudgementProcessor.JudgeSlider"/>), as in osu!;
    /// the parts themselves carry combo but not accuracy.
    /// </summary>
    public partial class DrawableSlider : DrawableHitObject
    {
        private const double exit_duration = 300;
        private const float body_border_width = 6;

        /// <summary>Ticks this close to the end of a span are dropped, as in osu!.</summary>
        private const double tick_end_leniency = 10;

        /// <summary>
        /// Multiplies the map's own <see cref="BeatmapDifficulty.SliderTickRate"/>
        /// when spacing ticks. The backend always writes a tick rate of 1
        /// (see <c>difficulty.py</c>'s <c>to_osu_difficulty_section</c>), which
        /// under real osu!'s tick-distance formula left the shortest sliders on
        /// Insane/Expert — a 1-beat slider at that tier's own multiplier —
        /// with a pixel length exactly at the tick distance itself: zero ticks,
        /// nothing between head and tail no matter how well the ball was
        /// followed. Tripling the effective rate keeps the same formula shape
        /// while giving every slider enough nodes that tracking it well is
        /// actually worth more than the head alone.
        /// </summary>
        private const double tick_density_multiplier = 3;

        private sealed class SliderPart
        {
            public double Time;

            /// <summary>Where along the path this part sits, 0 (head) to 1 (tail).</summary>
            public double Progress;

            /// <summary>A tick, versus a repeat or the tail.</summary>
            public bool IsTick;

            public bool Judged;

            public Drawable? Marker;
        }

        private readonly SliderData slider;
        private readonly SliderPath path;
        private readonly List<SliderPart> parts = new List<SliderPart>();

        private readonly Container head;
        private readonly ApproachCircle approach;
        private readonly Container ball;
        private readonly Container followCircle;
        private readonly SmoothPath body;
        private readonly SmoothPath bodyBorder;

        private bool headJudged;
        private bool tracking;
        private int partsHit;
        private readonly int partsTotal;
        private bool fullySnaked;

        /// <summary>The currently rendered body outline, in playfield coordinates. Grows during the snake-in animation. Exposed for tests.</summary>
        public IReadOnlyList<Vector2> BodyVertices => body.Vertices;

        /// <summary>Number of repeat arrows placed on this slider (0 for a non-repeating slider). Exposed for tests.</summary>
        public int RepeatArrowCount => InternalChildren.OfType<Triangle>().Count();

        /// <summary>Number of tick markers along this slider, excluding repeats and the tail. Exposed for tests.</summary>
        public int TickCount => parts.Count(p => p.IsTick);

        /// <summary>Head plus every tick, repeat and tail — the denominator <see cref="JudgementProcessor.JudgeSlider"/> judges against. Exposed for tests.</summary>
        public int PartsTotal => partsTotal;

        public DrawableSlider(SliderData data, BeatmapDifficulty difficulty, Color4 comboColour, int comboNumber)
            : base(data, difficulty, comboColour)
        {
            slider = data;
            path = SliderPath.FromSlider(data);

            buildParts();
            partsTotal = parts.Count + 1; // + the head

            float diameter = CircleRadius * 2;

            InternalChildren = new Drawable[]
            {
                bodyBorder = new SmoothPath
                {
                    PathRadius = CircleRadius,
                    Colour = new Color4(1f, 1f, 1f, 0.85f),
                },
                body = new SmoothPath
                {
                    PathRadius = Math.Max(1, CircleRadius - body_border_width),
                    Colour = new Color4(comboColour.R * 0.45f, comboColour.G * 0.45f, comboColour.B * 0.45f, 0.85f),
                },
                followCircle = new Container
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.Centre,
                    Size = new Vector2(diameter * (float)JudgementProcessor.FollowCircleRadiusMultiplier),
                    Masking = true,
                    CornerRadius = diameter * (float)JudgementProcessor.FollowCircleRadiusMultiplier / 2,
                    BorderThickness = 4,
                    BorderColour = new Color4(1f, 1f, 1f, 0.9f),
                    Alpha = 0,
                    Child = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0, AlwaysPresent = true },
                },
                ball = new Container
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.Centre,
                    Size = new Vector2(diameter * 0.9f),
                    Alpha = 0,
                    Child = new Circle { RelativeSizeAxes = Axes.Both, Colour = Color4.White },
                },
                head = new Container
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
                },
            };

            setBodyVertices(path.Segment(0));
            addPartMarkers();
        }

        private void buildParts()
        {
            double spanDuration = slider.SpanDuration;

            // One tick per "tick distance" of path travelled — the same
            // distance osu! derives from the slider velocity and tick rate,
            // just at tick_density_multiplier times the map's authored rate
            // (see that constant's remarks for why).
            double tickDistance = Difficulty.SliderTickRate > 0
                ? 100 * Difficulty.SliderMultiplier / (Difficulty.SliderTickRate * tick_density_multiplier)
                : double.MaxValue;

            for (int span = 0; span < Math.Max(1, slider.Slides); span++)
            {
                double spanStart = StartTime + span * spanDuration;
                bool reversed = span % 2 == 1;

                if (tickDistance > 0 && slider.PixelLength > 0)
                {
                    for (double distance = tickDistance; distance < slider.PixelLength; distance += tickDistance)
                    {
                        double progress = distance / slider.PixelLength;
                        double time = spanStart + spanDuration * progress;

                        if (time > spanStart + spanDuration - tick_end_leniency)
                            break;

                        parts.Add(new SliderPart
                        {
                            Time = time,
                            Progress = reversed ? 1 - progress : progress,
                            IsTick = true,
                        });
                    }
                }

                // The end of a span is a repeat, or the tail on the last one.
                parts.Add(new SliderPart
                {
                    Time = spanStart + spanDuration,
                    Progress = reversed ? 0 : 1,
                    IsTick = false,
                });
            }
        }

        private void setBodyVertices(IReadOnlyList<Vector2> vertices)
        {
            bodyBorder.Vertices = vertices;
            body.Vertices = vertices;

            // Path sizes itself around its vertices, so it has to be shifted
            // back by where its own bounding box starts for a vertex to land
            // on the playfield coordinate it was given. The box changes shape
            // as the path snakes in, so this has to be redone on every update
            // until snaking completes.
            bodyBorder.Position = -bodyBorder.PositionInBoundingBox(Vector2.Zero);
            body.Position = -body.PositionInBoundingBox(Vector2.Zero);
        }

        private void addPartMarkers()
        {
            for (int i = 0; i < parts.Count; i++)
            {
                var part = parts[i];

                if (part.IsTick)
                {
                    var tick = new Circle
                    {
                        Anchor = Anchor.TopLeft,
                        Origin = Anchor.Centre,
                        Position = path.PositionAt(part.Progress),
                        Size = new Vector2(CircleRadius * 0.35f),
                        Colour = new Color4(1f, 1f, 1f, 0.9f),
                    };

                    part.Marker = tick;
                    AddInternal(tick);
                    continue;
                }

                // The last part is always the tail — no arrow there, only
                // an actual repeat (the ball reversing) gets one.
                bool isRepeat = i < parts.Count - 1;

                if (!isRepeat)
                    continue;

                // At progress 1 the ball is about to reverse back down the
                // path (backward); at progress 0 it's about to head forward
                // again — either way, the arrow points into the slider body.
                Vector2 direction = part.Progress >= 1
                    ? -path.DirectionAt(1)
                    : path.DirectionAt(0);

                float angle = (float)(Math.Atan2(direction.Y, direction.X) * 180 / Math.PI) + 90f;

                var arrow = new Triangle
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.Centre,
                    Position = path.PositionAt(part.Progress),
                    Size = new Vector2(CircleRadius * 0.9f),
                    Rotation = angle,
                    Colour = new Color4(1f, 1f, 1f, 0.95f),
                };

                part.Marker = arrow;
                AddInternal(arrow);
            }
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            using (BeginAbsoluteSequence(AppearTime))
            {
                this.FadeIn(FadeInDuration);

                // Linear for the same reason as a hit circle's — see
                // DrawableHitCircle.LoadComplete.
                approach.ScaleTo(4f).Then().ScaleTo(1f, Preempt);

                // The pulsing loop needs a live clock to schedule against,
                // which the constructor doesn't have yet — so the arrows are
                // created there, but only start pulsing once loaded.
                foreach (var part in parts)
                {
                    if (part.Marker is Triangle arrow)
                    {
                        arrow.ScaleTo(1f).Then().ScaleTo(1.2f, 400, Easing.InOutSine)
                             .Then().ScaleTo(1f, 400, Easing.InOutSine).Loop();
                    }
                }
            }

            using (BeginAbsoluteSequence(StartTime))
            {
                head.FadeOut(150, Easing.OutQuint);
                ball.FadeIn(50);
            }

            using (BeginAbsoluteSequence(EndTime))
            {
                ball.FadeOut(100);
                followCircle.FadeOut(100);
            }
        }

        public override bool AcceptsPress => !headJudged;

        public override bool TryPress(double time, Vector2 cursorScreenSpace)
        {
            if (headJudged)
                return false;

            if (Math.Abs(time - StartTime) > MehWindow)
                return false;

            if (!CursorWithin(cursorScreenSpace, Data.Position, CircleRadius))
                return false;

            // The head is the only part of a slider with a press to time.
            HitError = time - StartTime;

            judgeHead(true);
            return true;
        }

        private void judgeHead(bool hit)
        {
            headJudged = true;

            if (hit)
                partsHit++;

            ApplyTick(hit);

            approach.FadeOut(50);

            if (!hit)
                head.FadeColour(new Color4(1f, 0.3f, 0.3f, 1f), 100);
        }

        public override void UpdateGameplay(double time, Vector2 cursorScreenSpace, bool anyKeyHeld)
        {
            updateSnaking(time);

            if (!headJudged && time > StartTime + MehWindow)
                judgeHead(false);

            updateBall(time, cursorScreenSpace, anyKeyHeld);
            updateParts(time);

            if (!IsJudged && time >= EndTime)
                ApplyJudgement(time, JudgementProcessor.JudgeSlider(partsHit, partsTotal));
        }

        /// <summary>
        /// Grows the body from the head out to its full length as the object
        /// approaches, over the same duration as its own fade-in — matching
        /// the "snaking in" that makes a slider's shape readable as it appears
        /// rather than popping in fully formed.
        /// </summary>
        private void updateSnaking(double time)
        {
            if (fullySnaked)
                return;

            double progress = (time - AppearTime) / FadeInDuration;

            if (progress >= 1)
            {
                setBodyVertices(path.Segment(1));
                fullySnaked = true;
                return;
            }

            setBodyVertices(path.Segment(Math.Max(0, progress)));
        }

        private void updateBall(double time, Vector2 cursorScreenSpace, bool anyKeyHeld)
        {
            if (time < StartTime || time > EndTime)
            {
                tracking = false;
                return;
            }

            Vector2 ballPosition = path.PositionAt(progressAt(time));

            ball.Position = ballPosition;
            followCircle.Position = ballPosition;

            bool wasTracking = tracking;

            tracking = anyKeyHeld &&
                       CursorWithin(cursorScreenSpace, ballPosition,
                           CircleRadius * (float)JudgementProcessor.FollowCircleRadiusMultiplier);

            if (tracking != wasTracking)
            {
                followCircle.FadeTo(tracking ? 1 : 0, 120, Easing.OutQuint);
                ball.ScaleTo(tracking ? 1.1f : 1f, 120, Easing.OutQuint);
            }
        }

        /// <summary>Path progress at a point in time, accounting for repeats running back down the path.</summary>
        private double progressAt(double time)
        {
            double spanDuration = slider.SpanDuration;

            if (spanDuration <= 0)
                return 0;

            double spanProgress = (time - StartTime) / spanDuration;
            int span = (int)Math.Clamp(Math.Floor(spanProgress), 0, Math.Max(0, slider.Slides - 1));

            double progress = Math.Clamp(spanProgress - span, 0, 1);

            return span % 2 == 1 ? 1 - progress : progress;
        }

        private void updateParts(double time)
        {
            foreach (var part in parts)
            {
                if (part.Judged || time < part.Time)
                    continue;

                part.Judged = true;

                if (tracking)
                {
                    partsHit++;
                    part.Marker?.ScaleTo(1.6f, 150, Easing.OutQuint);
                    part.Marker?.FadeOut(150, Easing.OutQuint);
                }
                else
                {
                    part.Marker?.FadeColour(new Color4(1f, 0.3f, 0.3f, 1f), 80);
                    part.Marker?.FadeOut(200, Easing.OutQuint);
                }

                ApplyTick(tracking);
            }
        }

        protected override double ApplyJudgementAnimation(HitResult result)
        {
            this.FadeOut(exit_duration, Easing.OutQuint);

            if (result == HitResult.Miss)
                bodyBorder.FadeColour(new Color4(1f, 0.4f, 0.4f, 1f), 100);

            return exit_duration;
        }
    }
}
