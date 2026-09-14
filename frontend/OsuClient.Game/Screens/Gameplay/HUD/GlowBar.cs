using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.Gameplay.HUD
{
    /// <summary>
    /// The lazer-style meter look shared by <see cref="HealthBar"/> and
    /// <see cref="SongProgressBar"/>: a thick white pill outline with a
    /// separate glowing white fill sitting inside it, with a gap between the
    /// two so the fill reads as distinct from the outline rather than
    /// merging into one white blob.
    ///
    /// The fill is a sibling of the outlined track, not a child of it: a
    /// masked container clips its children's edge effects too, so nesting the
    /// fill inside the track would cut its glow off at the outline instead of
    /// letting it bloom.
    /// </summary>
    public abstract partial class GlowBar : CompositeDrawable
    {
        private const float border_thickness = 4;

        /// <summary>Clear space between the inside of the outline and the fill.</summary>
        private const float fill_gap = 6;

        /// <summary>
        /// Kept tighter than <see cref="fill_gap"/> on purpose: the glow
        /// radiates into the gap as much as outward, so a wide one floods the
        /// gap and merges the fill back into the outline — the exact thing the
        /// gap is there to prevent. Intensity comes from a full-alpha colour
        /// instead of a large radius.
        /// </summary>
        private const float glow_radius = 8;

        private readonly Container fill;
        private readonly Box fillBox;

        private Color4 fillColour = Color4.White;

        protected GlowBar(float height)
        {
            Height = height;

            float inset = border_thickness + fill_gap;
            float fillHeight = Math.Max(1, height - inset * 2);

            InternalChildren = new Drawable[]
            {
                new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Masking = true,
                    CornerRadius = height / 2,
                    BorderThickness = border_thickness,
                    BorderColour = Color4.White,
                    Child = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = new Color4(1f, 1f, 1f, 0.06f),
                    },
                },
                new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding(inset),
                    Child = fill = new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Width = 0,
                        Masking = true,
                        CornerRadius = fillHeight / 2,
                        EdgeEffect = glowFor(fillColour),
                        Child = fillBox = new Box { RelativeSizeAxes = Axes.Both, Colour = fillColour },
                    },
                },
            };
        }

        /// <summary>Sets how full the bar is, 0 to 1.</summary>
        protected void SetFill(double fraction) => fill.Width = (float)Math.Clamp(fraction, 0, 1);

        /// <summary>
        /// Sets the fill's colour, and the colour of its glow to match — kept
        /// full-alpha regardless of the fill colour, otherwise a darker fill
        /// (red, say) would glow noticeably weaker than a bright one for no
        /// reason a player would read as intentional.
        /// </summary>
        protected void SetFillColour(Color4 colour)
        {
            if (fillColour == colour)
                return;

            fillColour = colour;
            fillBox.Colour = colour;
            fill.EdgeEffect = glowFor(colour);
        }

        /// <summary>Current fill colour. Exposed for tests.</summary>
        public Color4 CurrentFillColour => fillColour;

        private static EdgeEffectParameters glowFor(Color4 colour) => new EdgeEffectParameters
        {
            Type = EdgeEffectType.Glow,
            Colour = new Color4(colour.R, colour.G, colour.B, 1f),
            Radius = glow_radius,
        };
    }
}
