using System;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using OsuClient.Game.Beatmaps;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.SongSelect
{
    /// <summary>
    /// A clickable chip for one difficulty of a beatmap set: a colour-coded dot,
    /// the difficulty name, and its object count.
    ///
    /// The backend doesn't compute star ratings, so the colour comes from object
    /// density (objects per second) instead. The thresholds are taken from the
    /// real per-tier densities in BACKEND.md's <c>--all</c> table, so a
    /// backend-generated Easy/Normal/Hard/Insane/Expert lands on its own colour.
    /// </summary>
    public partial class DifficultyIcon : ClickableContainer
    {
        private const float height = 30;

        public Beatmap Beatmap { get; }

        private readonly Box background;
        private readonly Circle dot;
        private readonly SpriteText label;

        private bool selected;

        /// <summary>Whether this is the currently highlighted difficulty.</summary>
        public bool Selected
        {
            get => selected;
            set
            {
                if (selected == value)
                    return;

                selected = value;
                updateVisualState();
            }
        }

        public DifficultyIcon(Beatmap beatmap)
        {
            Beatmap = beatmap ?? throw new ArgumentNullException(nameof(beatmap));

            AutoSizeAxes = Axes.X;
            Height = height;
            Masking = true;
            CornerRadius = height / 2;

            Color4 tint = ColourFor(Density(beatmap));

            Children = new Drawable[]
            {
                background = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(1f, 1f, 1f, 0.08f),
                },
                new FillFlowContainer
                {
                    AutoSizeAxes = Axes.X,
                    RelativeSizeAxes = Axes.Y,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(8, 0),
                    Padding = new MarginPadding { Horizontal = 12 },
                    Children = new Drawable[]
                    {
                        dot = new Circle
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Size = new Vector2(12),
                            Colour = tint,
                        },
                        label = new SpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Text = DescribeDifficulty(beatmap),
                            Font = FontUsage.Default.With(size: 15),
                            Colour = new Color4(0.85f, 0.85f, 0.9f, 1f),
                        },
                    },
                },
            };
        }

        protected override bool OnHover(HoverEvent e)
        {
            updateVisualState();
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            updateVisualState();
            base.OnHoverLost(e);
        }

        private void updateVisualState()
        {
            if (selected)
            {
                background.FadeColour(new Color4(1f, 1f, 1f, 0.35f), 120, Easing.OutQuint);
                label.FadeColour(Color4.White, 120, Easing.OutQuint);
                dot.ScaleTo(1.25f, 200, Easing.OutElastic);
            }
            else
            {
                background.FadeColour(
                    IsHovered ? new Color4(1f, 1f, 1f, 0.2f) : new Color4(1f, 1f, 1f, 0.08f),
                    120, Easing.OutQuint);
                label.FadeColour(new Color4(0.85f, 0.85f, 0.9f, 1f), 120, Easing.OutQuint);
                dot.ScaleTo(1f, 200, Easing.OutQuint);
            }
        }

        /// <summary>e.g. "Hard — 511 objects".</summary>
        public static string DescribeDifficulty(Beatmap beatmap)
        {
            string name = string.IsNullOrWhiteSpace(beatmap.Metadata.Version)
                ? "(unnamed)"
                : beatmap.Metadata.Version;

            return $"{name} — {beatmap.HitObjects.Count} objects";
        }

        /// <summary>
        /// Objects per second across the playable span of the map. Returns 0 for
        /// a map with fewer than two objects, where density is meaningless.
        /// </summary>
        public static double Density(Beatmap beatmap)
        {
            if (beatmap.HitObjects.Count < 2)
                return 0;

            double span = (beatmap.LastHitObjectTime - beatmap.FirstHitObjectTime) / 1000.0;

            return span <= 0 ? 0 : beatmap.HitObjects.Count / span;
        }

        /// <summary>
        /// Maps object density onto the difficulty colour ramp. Boundaries sit
        /// between the backend's measured per-tier densities (Easy ~0.73,
        /// Normal ~1.56, Hard ~2.33, Insane ~3.62, Expert ~4.61 obj/s).
        /// </summary>
        public static Color4 ColourFor(double density)
        {
            if (density < 1.1) return new Color4(0.34f, 0.72f, 0.98f, 1f);  // blue
            if (density < 1.95) return new Color4(0.36f, 0.85f, 0.44f, 1f); // green
            if (density < 2.95) return new Color4(0.96f, 0.82f, 0.25f, 1f); // yellow
            if (density < 4.1) return new Color4(0.96f, 0.35f, 0.45f, 1f);  // red
            return new Color4(0.65f, 0.38f, 0.95f, 1f);                     // purple
        }

        /// <summary>Counts of each hit object kind, for the details panel.</summary>
        public static (int circles, int sliders, int spinners) CountObjects(Beatmap beatmap)
        {
            int circles = beatmap.HitObjects.OfType<Beatmaps.HitObjects.HitCircleData>().Count();
            int sliders = beatmap.HitObjects.OfType<Beatmaps.HitObjects.SliderData>().Count();
            int spinners = beatmap.HitObjects.OfType<Beatmaps.HitObjects.SpinnerData>().Count();

            return (circles, sliders, spinners);
        }
    }
}
