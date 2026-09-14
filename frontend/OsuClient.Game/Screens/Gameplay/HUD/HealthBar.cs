using osu.Framework.Graphics;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.Gameplay.HUD
{
    /// <summary>
    /// The HP bar: a third of the playfield's width, sitting in the top-left
    /// corner. Positioning/padding is the caller's job (see
    /// <see cref="Gameplay.PlayerScreen"/>) — this only owns its own size and look.
    ///
    /// The fill colour reads off health at a glance rather than requiring the
    /// bar's actual length to be judged: bright green above two-thirds, amber
    /// through the middle third, red below one-third.
    /// </summary>
    public partial class HealthBar : GlowBar
    {
        /// <summary>Exposed so the HUD can lay other elements out clear of the bar.</summary>
        public const float BarHeight = 30;

        private const double danger_threshold = 1 / 3d;
        private const double warning_threshold = 2 / 3d;

        private static readonly Color4 healthy_colour = new Color4(0.25f, 0.95f, 0.3f, 1f);
        private static readonly Color4 warning_colour = new Color4(1f, 0.85f, 0.15f, 1f);
        private static readonly Color4 danger_colour = new Color4(1f, 0.2f, 0.2f, 1f);

        public HealthBar()
            : base(BarHeight)
        {
            RelativeSizeAxes = Axes.X;
            Width = 1 / 3f;

            SetFillColour(healthy_colour);
        }

        /// <summary>Sets the displayed health, 0 to 1.</summary>
        public void SetHealth(double health)
        {
            SetFill(health);
            SetFillColour(ColourFor(health));
        }

        /// <summary>The band a given health value falls into. Exposed for tests.</summary>
        public static Color4 ColourFor(double health)
        {
            // "more than 66%" is green, so exactly two-thirds is already
            // yellow; "below 33%" is red, so exactly one-third is still
            // yellow — both boundaries land in the middle band.
            if (health > warning_threshold)
                return healthy_colour;

            return health >= danger_threshold ? warning_colour : danger_colour;
        }
    }
}
