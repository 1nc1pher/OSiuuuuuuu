using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using OsuClient.Game.Backend;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.Generation
{
    /// <summary>
    /// Stage 2 of the DSP reveal (FRONTEND_PLAN.md Phase 7): every onset the
    /// detector found, sparking in over the revealed spectrogram in time
    /// order.
    ///
    /// Each spark is a vertical streak at the onset's own moment, positioned
    /// through the spectrogram's <see cref="SpectrogramReveal.TimeToX"/> so it
    /// lands on the streak in the image that produced it. Height and
    /// brightness come from the onset's strength, and colour from its dominant
    /// band — the same glow-and-fade language the gameplay HUD's
    /// <see cref="Gameplay.HUD.KeyTimingBar"/> uses for a player's presses,
    /// pointed instead at the computer finding the hits.
    ///
    /// Sparks are *placed* by a sweep rather than animated individually: a
    /// long song has well over a thousand onsets, and a drawable per onset
    /// with its own transform sequence is thousands of transforms for
    /// something on screen for four seconds.
    /// </summary>
    public partial class OnsetSparkLayer : CompositeDrawable
    {
        /// <summary>
        /// Sparks past this many are thinned out, strongest kept.
        ///
        /// A real song detects hundreds to thousands of onsets. Tuned against
        /// an actual generated map rather than guessed: 420 of them across a
        /// ~1300px panel is a spark every three pixels, which renders as one
        /// solid salmon block with no individual hits visible at all. At this
        /// count they sit far enough apart to read as separate detections,
        /// which is the entire point of the stage. Thinning by strength keeps
        /// the loud hits — the ones that became objects.
        /// </summary>
        public const int MaxSparks = 160;

        private static readonly Color4 low_colour = new Color4(1f, 0.42f, 0.32f, 1f);
        private static readonly Color4 mid_colour = new Color4(0.45f, 0.95f, 0.75f, 1f);
        private static readonly Color4 high_colour = new Color4(0.6f, 0.75f, 1f, 1f);

        private readonly IReadOnlyList<AnalysisOnset> onsets;
        private readonly SpectrogramReveal spectrogram;
        private readonly List<Spark> sparks = new List<Spark>();

        private double sweepDuration;
        private double? sweepStartTime;
        private int placed;

        public OnsetSparkLayer(IReadOnlyList<AnalysisOnset> onsets, SpectrogramReveal spectrogram)
        {
            this.spectrogram = spectrogram;
            this.onsets = Thin(onsets, MaxSparks);

            RelativeSizeAxes = Axes.Both;
        }

        /// <summary>How many sparks are currently on screen. Exposed for tests.</summary>
        public int SparkCount => placed;

        /// <summary>How many onsets this layer will show once it finishes. Exposed for tests.</summary>
        public int TotalSparks => onsets.Count;

        /// <summary>
        /// Keeps at most <paramref name="limit"/> onsets, the strongest ones,
        /// back in time order. Pure and public so the thinning rule can be
        /// tested without a screen — see <see cref="MaxSparks"/> for why it
        /// exists at all.
        /// </summary>
        public static IReadOnlyList<AnalysisOnset> Thin(IReadOnlyList<AnalysisOnset> onsets, int limit)
        {
            if (onsets.Count <= limit || limit <= 0)
                return onsets.OrderBy(o => o.Time).ToList();

            return onsets.OrderByDescending(o => o.Strength)
                         .Take(limit)
                         .OrderBy(o => o.Time)
                         .ToList();
        }

        /// <summary>The colour a band's onsets spark in — bass warm, treble cool.</summary>
        public static Color4 ColourForBand(string band) => band switch
        {
            "low" => low_colour,
            "high" => high_colour,
            _ => mid_colour,
        };

        /// <summary>
        /// Sparks the onsets in over <paramref name="duration"/> milliseconds,
        /// in time order, so the layer fills left to right the way the
        /// spectrogram did.
        /// </summary>
        public void Sweep(double duration)
        {
            sweepDuration = Math.Max(1, duration);
            sweepStartTime = Clock.CurrentTime;
        }

        /// <summary>Places every remaining spark at once, for a skip.</summary>
        public void SweepImmediately()
        {
            sweepStartTime = null;

            while (placed < onsets.Count)
                addSpark(onsets[placed++], animate: false);
        }

        protected override void Update()
        {
            base.Update();

            if (sweepStartTime != null)
            {
                double elapsed = Clock.CurrentTime - sweepStartTime.Value;
                double progress = Math.Clamp(elapsed / sweepDuration, 0, 1);

                int target = (int)Math.Round(progress * onsets.Count);

                while (placed < target)
                    addSpark(onsets[placed++], animate: true);

                if (progress >= 1)
                    sweepStartTime = null;
            }

            // Positions are re-derived from the spectrogram's current width
            // every frame, so a resize mid-sequence keeps every spark over the
            // moment it belongs to.
            foreach (var spark in sparks)
                spark.UpdatePosition(spectrogram.TimeToX(spark.Time), DrawHeight);
        }

        private void addSpark(AnalysisOnset onset, bool animate)
        {
            // Strength is normalised spectral flux, so it is already 0..1;
            // the floor keeps a weak onset visible rather than a hairline.
            float strength = (float)Math.Clamp(onset.Strength, 0.12, 1);
            var colour = ColourForBand(onset.Band);

            var spark = new Spark(onset.Time, strength, colour);

            sparks.Add(spark);
            AddInternal(spark);

            if (animate)
                spark.Flash();
            else
                spark.Settle();
        }

        /// <summary>One onset: a vertical streak that flares and settles.</summary>
        private partial class Spark : CompositeDrawable
        {
            // Low enough that the spectrogram underneath stays visible: the
            // sparks are an annotation of that image, not a replacement for it.
            private const float settled_alpha = 0.4f;

            /// <summary>
            /// The moment in the track this onset was detected at. Named over
            /// Transformable.Time (the drawable's own transform clock), which
            /// this has nothing to do with.
            /// </summary>
            public new double Time { get; }

            private readonly float strength;
            private readonly Color4 bandColour;
            private readonly Box line;
            private readonly Box head;

            public Spark(double time, float strength, Color4 colour)
            {
                Time = time;
                this.strength = strength;
                bandColour = colour;

                Origin = Anchor.BottomCentre;
                Anchor = Anchor.BottomLeft;
                AutoSizeAxes = Axes.None;
                Width = Math.Max(1.5f, 1f + strength * 2f);

                InternalChildren = new Drawable[]
                {
                    line = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = colour,
                        EdgeSmoothness = new Vector2(1),
                    },
                    head = new Box
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.Centre,
                        Size = new Vector2(Math.Max(4f, strength * 9f)),
                        Colour = Color4.White,
                        Rotation = 45,
                        Alpha = 0,
                    },
                };
            }

            /// <summary>Places the streak over its moment, scaled to the layer's height.</summary>
            public void UpdatePosition(float x, float layerHeight)
            {
                X = x;

                // Stronger onsets reach further up the panel — the height is
                // the readable part of "how sure the detector was".
                Height = layerHeight * (0.2f + 0.55f * strength);
            }

            /// <summary>The moment of detection: a bright flare that settles back.</summary>
            public void Flash()
            {
                Alpha = 1;

                // Captured at construction: reading line.Colour here would
                // read back the white this same call just set.
                line.FadeColour(Color4.White).FadeColour(bandColour, 260, Easing.OutQuint);
                head.FadeTo(1).FadeOut(420, Easing.OutQuint);

                this.FadeTo(settled_alpha, 320, Easing.OutQuint);
            }

            /// <summary>Skips straight to the settled look.</summary>
            public void Settle()
            {
                Alpha = settled_alpha;
                head.Alpha = 0;
            }
        }
    }
}
