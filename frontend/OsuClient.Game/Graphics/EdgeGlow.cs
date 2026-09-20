using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osuTK.Graphics;

namespace OsuClient.Game.Graphics
{
    /// <summary>
    /// A wash of light in from the left and right edges of the window, its
    /// brightness driven from outside.
    ///
    /// Each side is a plain gradient box pinned to its edge, fading from full
    /// strength at the edge to nothing across <see cref="Reach"/> — no
    /// outline, no shape, just colour. Additive, so it lifts whatever is
    /// behind it rather than greying it out.
    ///
    /// Written for gameplay's beat flash
    /// (<see cref="Screens.Gameplay.HUD.BeatBorderFlash"/>) and pulled out
    /// here when the main menu wanted the same effect. The two differ only in
    /// what drives them — gameplay from a beatmap's timing points, the menu
    /// from its own pulse — so what they share is exactly this, and no more.
    /// </summary>
    public partial class EdgeGlow : CompositeDrawable
    {
        private readonly Box left;
        private readonly Box right;

        private Color4 colour = Color4.White;
        private float intensity;

        public EdgeGlow(float reach)
        {
            RelativeSizeAxes = Axes.Both;

            InternalChildren = new Drawable[]
            {
                left = new Box
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    RelativeSizeAxes = Axes.Y,
                    Width = reach,
                    Alpha = 0,
                    Blending = BlendingParameters.Additive,
                },
                right = new Box
                {
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.CentreRight,
                    RelativeSizeAxes = Axes.Y,
                    Width = reach,
                    Alpha = 0,
                    Blending = BlendingParameters.Additive,
                },
            };

            applyColour();
        }

        /// <summary>How far in from each edge the wash extends, in pixels.</summary>
        public float Reach
        {
            get => left.Width;
            set => left.Width = right.Width = value;
        }

        /// <summary>The colour at the very edge, fading to nothing inwards.</summary>
        public Color4 GlowColour
        {
            get => colour;
            set
            {
                if (colour.Equals(value))
                    return;

                colour = value;
                applyColour();
            }
        }

        /// <summary>How bright both sides are, 0 to 1.</summary>
        public float Intensity
        {
            get => intensity;
            set => left.Alpha = right.Alpha = intensity = value;
        }

        private void applyColour()
        {
            var transparent = new Color4(colour.R, colour.G, colour.B, 0f);

            left.Colour = ColourInfo.GradientHorizontal(colour, transparent);
            right.Colour = ColourInfo.GradientHorizontal(transparent, colour);
        }
    }
}
