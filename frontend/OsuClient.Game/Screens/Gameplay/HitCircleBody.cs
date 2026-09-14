using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.Gameplay
{
    /// <summary>
    /// The lazer-style hit circle look, shared by <see cref="DrawableHitCircle"/>
    /// and a slider's head: a thick white ring, a darker combo-colour disc
    /// inside it, and a lighter combo-colour disc inside that with a
    /// top-to-bottom gradient for a glassy highlight — rather than the flat
    /// single-colour fill the very first version of this client used.
    /// </summary>
    public partial class HitCircleBody : CompositeDrawable
    {
        /// <summary>Ring thickness as a fraction of the circle's diameter.</summary>
        private const float ring_thickness = 0.10f;

        /// <summary>Where the darker outer disc's edge sits, inside the ring.</summary>
        private const float outer_disc_scale = 1f - ring_thickness;

        /// <summary>Where the lighter inner disc sits, inside the outer disc.</summary>
        private const float inner_disc_scale = 0.72f;

        public HitCircleBody(float diameter, Color4 comboColour, int comboNumber)
        {
            Anchor = Anchor.Centre;
            Origin = Anchor.Centre;
            Size = new Vector2(diameter);

            Color4 outer = darken(comboColour, 0.62f);
            Color4 innerTop = lighten(comboColour, 0.55f);
            Color4 innerBottom = lighten(comboColour, 0.15f);

            InternalChildren = new Drawable[]
            {
                // Thick white ring — the full circle showing through at the
                // edges of the smaller discs stacked on top of it.
                new Circle
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = Color4.White,
                },
                new Circle
                {
                    RelativeSizeAxes = Axes.Both,
                    Size = new Vector2(outer_disc_scale),
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Colour = outer,
                },
                new Circle
                {
                    RelativeSizeAxes = Axes.Both,
                    Size = new Vector2(inner_disc_scale),
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    // The "gradient vibe": lighter at the top fading toward
                    // the disc's own (still lighter-than-outer) base colour.
                    Colour = ColourInfo.GradientVertical(innerTop, innerBottom),
                },
                new SpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Font = FontUsage.Default.With(size: diameter * 0.4f),
                    Text = comboNumber.ToString(),
                    Colour = Color4.White,
                },
            };
        }

        private static Color4 darken(Color4 colour, float amount) =>
            new Color4(colour.R * (1 - amount), colour.G * (1 - amount), colour.B * (1 - amount), colour.A);

        private static Color4 lighten(Color4 colour, float amount) => new Color4(
            colour.R + (1 - colour.R) * amount,
            colour.G + (1 - colour.G) * amount,
            colour.B + (1 - colour.B) * amount,
            colour.A);
    }
}
