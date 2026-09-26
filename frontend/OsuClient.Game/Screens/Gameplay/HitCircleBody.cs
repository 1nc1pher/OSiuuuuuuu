using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using OsuClient.Game.Graphics;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.Gameplay
{
    /// <summary>
    /// A hit circle drawn as a small record, shared by
    /// <see cref="DrawableHitCircle"/> and a slider's head: a vinyl face
    /// with grooves (<see cref="VinylTexture"/>), a label in the combo colour
    /// carrying the number in the retro pixel font, and a ring round the edge.
    ///
    /// Only the ring is drawn in deep, solid colour. The vinyl and the label
    /// are translucent, so the song's background stays visible through every
    /// circle; the outline is what the eye aims at, and it is the one thing
    /// here that never fades into the picture. The record doesn't turn — it
    /// is a texture, not an animation, so nothing on the circle moves except
    /// what tells the player about timing.
    /// </summary>
    public partial class HitCircleBody : CompositeDrawable
    {
        /// <summary>The outline's thickness, as a fraction of the diameter.</summary>
        private const float ring_thickness = 0.085f;

        private const float label_alpha = 0.55f;

        private readonly Sprite vinyl;

        public HitCircleBody(float diameter, Color4 comboColour, int comboNumber)
        {
            Anchor = Anchor.Centre;
            Origin = Anchor.Centre;
            Size = new Vector2(diameter);

            InternalChildren = new Drawable[]
            {
                vinyl = new Sprite
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Size = new Vector2(1 - ring_thickness),
                },
                // The label, in the combo colour but see-through.
                new Circle
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Size = new Vector2(VinylTexture.LabelFraction * (1 - ring_thickness) + 0.02f),
                    Colour = new Color4(comboColour.R, comboColour.G, comboColour.B, label_alpha),
                },
                // The outline: the one solid, deep-coloured part.
                new CircularContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Masking = true,
                    BorderThickness = diameter * ring_thickness,
                    BorderColour = comboColour,
                    Child = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0, AlwaysPresent = true },
                },
                new RetroText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Font = RetroFontFamily.Display,
                    // Rounded so a map's circles all share one size, and so
                    // one rendered texture per number (see RetroText.Shared).
                    TextSize = (int)(diameter * 0.26f),
                    Colour = Color4.White,
                    Text = comboNumber.ToString(),
                    Shared = true,
                },
            };
        }

        [BackgroundDependencyLoader]
        private void load(IRenderer renderer) => vinyl.Texture = VinylTexture.Get(renderer);
    }
}
