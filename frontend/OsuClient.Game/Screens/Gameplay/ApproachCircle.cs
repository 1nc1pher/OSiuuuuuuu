using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.Gameplay
{
    /// <summary>
    /// The shrinking ring around a hit circle: starts large and shrinks so its
    /// edge reaches the circle's edge exactly at the object's start time.
    /// </summary>
    public partial class ApproachCircle : Container
    {
        public ApproachCircle(float diameter, Color4 colour)
        {
            Anchor = Anchor.Centre;
            Origin = Anchor.Centre;
            Size = new Vector2(diameter);
            Masking = true;
            CornerRadius = diameter / 2f;
            BorderThickness = Math.Max(2f, diameter * 0.02f);
            BorderColour = colour;

            // A faint glow in the same colour, so the ring reads as lit neon
            // and links to its circle a little faster in a dense pattern.
            // Hollow, or the glow floods the disc inside the ring.
            EdgeEffect = new EdgeEffectParameters
            {
                Type = EdgeEffectType.Glow,
                Colour = new Color4(colour.R, colour.G, colour.B, 0.35f),
                Radius = 6,
                Hollow = true,
            };

            Child = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Alpha = 0,
                AlwaysPresent = true,
            };
        }
    }
}
