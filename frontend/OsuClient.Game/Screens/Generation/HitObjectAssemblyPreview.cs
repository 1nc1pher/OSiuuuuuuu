using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using OsuClient.Game.Backend;
using OsuClient.Game.Graphics;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.Generation
{
    /// <summary>
    /// Stage 4 of the DSP reveal (FRONTEND_PLAN.md Phase 7): the objects the
    /// mapper placed, popping onto a playfield in time order at their real
    /// assigned positions.
    ///
    /// Non-interactive on purpose — this is the map being assembled, not
    /// played, so there is no approach circle, no judgement and no cursor. It
    /// draws on the same 512x384 osu!pixel playfield
    /// <see cref="Gameplay.PlayerScreen"/> uses, scaled to fit, so a pattern
    /// seen here is in the same place when it is played a few seconds later.
    ///
    /// Each object keeps the colour of the frequency band that produced it,
    /// carrying stage 2's language through to the end: the map is visibly made
    /// of the onsets the sparks just showed.
    /// </summary>
    public partial class HitObjectAssemblyPreview : CompositeDrawable
    {
        /// <summary>osu!'s playfield, in osu!pixels — the coordinate space every position is in.</summary>
        public const float PlayfieldWidth = 512;

        public const float PlayfieldHeight = 384;

        /// <summary>
        /// How many objects stay on screen at once.
        ///
        /// A whole Expert map is a thousand objects, and drawn all at once the
        /// playfield is a solid mat of circles with no pattern visible at all.
        /// Holding a rolling window means what's on screen always reads as
        /// patterns — the thing worth showing — while every object still gets
        /// its moment.
        /// </summary>
        public const int VisibleWindow = 48;

        /// <summary>Space kept clear under the playfield for the object counter, which would otherwise sit on its border.</summary>
        private const float counter_height = 30;

        private readonly IReadOnlyList<AnalysisHitObject> objects;

        private Container playfield = null!;
        private Container objectLayer = null!;
        private RetroText counter = null!;

        private readonly Queue<Drawable> visible = new Queue<Drawable>();

        private double assemblyDuration;
        private double? assemblyStartTime;
        private int placed;

        public HitObjectAssemblyPreview(IReadOnlyList<AnalysisHitObject> objects)
        {
            this.objects = objects.OrderBy(o => o.Time).ToList();

            RelativeSizeAxes = Axes.Both;
        }

        /// <summary>How many objects have been placed so far. Exposed for tests.</summary>
        public int PlacedCount => placed;

        /// <summary>How many objects this will place in total. Exposed for tests.</summary>
        public int TotalCount => objects.Count;

        /// <summary>Whether every object has been placed. Exposed for tests.</summary>
        public bool Complete => placed >= objects.Count;

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChildren = new Drawable[]
            {
                playfield = new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(PlayfieldWidth, PlayfieldHeight),
                    Children = new Drawable[]
                    {
                        new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Masking = true,
                            CornerRadius = 6,
                            BorderThickness = 2,
                            BorderColour = new Color4(0.28f, 0.3f, 0.42f, 1f),
                            Child = new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = new Color4(0.05f, 0.05f, 0.09f, 1f),
                            },
                        },
                        objectLayer = new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                        },
                    },
                },
                counter = new RetroText
                {
                    Anchor = Anchor.BottomCentre,
                    Origin = Anchor.BottomCentre,
                    Font = RetroFontFamily.Body,
                    TextSize = 14,
                    Colour = new Color4(0.72f, 0.75f, 0.85f, 1f),
                    Margin = new MarginPadding { Bottom = 8 },
                    Text = string.Empty,
                },
            };
        }

        /// <summary>Places every object over <paramref name="duration"/> milliseconds, in time order.</summary>
        public void Assemble(double duration)
        {
            assemblyDuration = Math.Max(1, duration);
            assemblyStartTime = Clock.CurrentTime;
        }

        /// <summary>Jumps to the finished count without drawing the rest, for a skip.</summary>
        public void AssembleImmediately()
        {
            assemblyStartTime = null;
            placed = objects.Count;

            updateCounter();
        }

        protected override void Update()
        {
            base.Update();

            updatePlayfieldScale();

            if (assemblyStartTime == null)
                return;

            double elapsed = Clock.CurrentTime - assemblyStartTime.Value;
            double progress = Math.Clamp(elapsed / assemblyDuration, 0, 1);

            int target = (int)Math.Round(progress * objects.Count);

            while (placed < target)
                place(objects[placed++]);

            updateCounter();

            if (progress >= 1)
                assemblyStartTime = null;
        }

        private void updateCounter()
        {
            var counts = objects.Take(placed).GroupBy(o => o.Kind)
                                .ToDictionary(g => g.Key, g => g.Count());

            counts.TryGetValue("circle", out int circles);
            counts.TryGetValue("slider", out int sliders);
            counts.TryGetValue("spinner", out int spinners);

            counter.Text = $"{circles} circles   {sliders} sliders   {spinners} spinners";
        }

        /// <summary>
        /// Fits the 512x384 playfield inside whatever space this has, keeping
        /// its aspect ratio — the same scaling
        /// <see cref="Gameplay.PlayerScreen"/> does, so positions match
        /// between the preview and actually playing the map.
        /// </summary>
        private void updatePlayfieldScale()
        {
            float available = DrawHeight - counter_height;

            if (DrawWidth <= 0 || available <= 0)
                return;

            float scale = Math.Min(DrawWidth / PlayfieldWidth, available / PlayfieldHeight);

            playfield.Scale = new Vector2(scale);

            // Shifted up by the space the counter takes, so the two don't
            // overlap at the bottom edge.
            playfield.Y = -counter_height / 2;
        }

        private void place(AnalysisHitObject obj)
        {
            var drawable = createDrawable(obj);

            objectLayer.Add(drawable);
            visible.Enqueue(drawable);

            // The pop: a brief overshoot on the way in reads as the object
            // being *placed*, where a plain fade reads as it having always
            // been there.
            drawable.FadeInFromZero(90).ScaleTo(1.35f).ScaleTo(1f, 260, Easing.OutBack);

            while (visible.Count > VisibleWindow)
            {
                var oldest = visible.Dequeue();

                oldest.FadeOut(420, Easing.OutQuint).Expire();
            }
        }

        private Drawable createDrawable(AnalysisHitObject obj)
        {
            var colour = OnsetSparkLayer.ColourForBand(obj.Band);
            var position = new Vector2((float)obj.X, (float)obj.Y);

            return obj.Kind switch
            {
                "spinner" => new SpinnerMark(colour),
                "slider" => new SliderMark(position, colour, (float)obj.Strength),
                _ => new CircleMark(position, colour, (float)obj.Strength),
            };
        }

        /// <summary>A placed circle: a ring at its playfield position.</summary>
        private partial class CircleMark : Container
        {
            public CircleMark(Vector2 position, Color4 colour, float strength)
            {
                // osu!pixels are the container's own coordinates, so a
                // position goes in unchanged; the playfield above does the
                // scaling for the window.
                Position = position;
                Origin = Anchor.Centre;
                Size = new Vector2(40);
                Masking = true;
                CornerRadius = 20;
                BorderThickness = 3;
                BorderColour = colour;

                Child = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = colour,
                    // Stronger onsets sit more solidly, so density of sound
                    // reads as density on the playfield.
                    Alpha = 0.18f + 0.32f * Math.Clamp(strength, 0, 1),
                };
            }
        }

        /// <summary>A placed slider: its head, with a stub of body showing it travels.</summary>
        private partial class SliderMark : CompositeDrawable
        {
            public SliderMark(Vector2 position, Color4 colour, float strength)
            {
                Position = position;
                Origin = Anchor.Centre;
                Size = new Vector2(40);

                // analysis.json carries the head position and duration, not
                // the path anchors — the .osu file is the authority on the
                // curve, and duplicating it here would be a second source of
                // truth for something the preview only hints at.
                InternalChildren = new Drawable[]
                {
                    new Box
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.CentreLeft,
                        Size = new Vector2(34, 8),
                        Colour = colour,
                        Alpha = 0.35f,
                        EdgeSmoothness = new Vector2(1),
                    },
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Masking = true,
                        CornerRadius = 20,
                        BorderThickness = 3,
                        BorderColour = colour,
                        Child = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = colour,
                            Alpha = 0.18f + 0.32f * Math.Clamp(strength, 0, 1),
                        },
                    },
                };
            }
        }

        /// <summary>A placed spinner: always centred, drawn big, the way osu! plays it.</summary>
        private partial class SpinnerMark : Container
        {
            public SpinnerMark(Color4 colour)
            {
                Position = new Vector2(PlayfieldWidth / 2, PlayfieldHeight / 2);
                Origin = Anchor.Centre;
                Size = new Vector2(150);
                Masking = true;
                CornerRadius = 75;
                BorderThickness = 4;
                BorderColour = colour;

                Child = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = colour,
                    Alpha = 0.08f,
                };
            }
        }
    }
}
