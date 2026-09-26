using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osuTK;

namespace OsuClient.Game.Graphics
{
    /// <summary>A screen that can arrive through a <see cref="ScreenReveal"/>.</summary>
    public interface IRevealable
    {
        ScreenReveal Reveal { get; }
    }

    /// <summary>
    /// Everything a screen draws, held inside a circle that can grow out of a
    /// point until it covers the window: the screen arriving through one
    /// curve, with whatever was there before still showing outside it.
    ///
    /// <para>
    /// The picture inside is held still while the circle grows round it, so
    /// the edge is seen sweeping across a screen already in place rather than
    /// the screen zooming out of a hole. Outside a reveal it is an ordinary
    /// full-size container with no mask at all.
    /// </para>
    /// </summary>
    public partial class ScreenReveal : Container
    {
        private readonly CircularContainer mask;
        private readonly Container content;

        private bool active;
        private Vector2 centre;

        protected override Container<Drawable> Content => content;

        public ScreenReveal()
        {
            RelativeSizeAxes = Axes.Both;

            InternalChild = mask = new CircularContainer
            {
                Child = content = new Container(),
            };
        }

        /// <summary>The circle's diameter while revealing. Transformable.</summary>
        public float Diameter { get; set; }

        /// <summary>Whether a reveal is under way. Exposed for tests and screenshot scenes.</summary>
        public bool Revealing => active;

        /// <summary>
        /// Hides everything behind a circle of nothing at <paramref name="at"/>
        /// (local, and so window, space), ready for <see cref="Sweep"/>.
        /// </summary>
        public void Collapse(Vector2 at)
        {
            ClearTransforms(false, nameof(Diameter));

            active = true;
            centre = at;
            Diameter = 0;
        }

        /// <summary>
        /// Grows the circle from where <see cref="Collapse"/> put it until it
        /// covers the window, then drops the mask.
        /// </summary>
        public void Sweep(double duration, Easing easing)
        {
            if (!active)
                return;

            this.TransformTo(nameof(Diameter), CoverDiameter(centre, DrawSize), duration, easing)
                .OnComplete(_ => active = false);
        }

        /// <summary>A circle this wide, centred on <paramref name="point"/>, reaches every corner of <paramref name="size"/>.</summary>
        public static float CoverDiameter(Vector2 point, Vector2 size)
        {
            float x = Math.Max(point.X, size.X - point.X);
            float y = Math.Max(point.Y, size.Y - point.Y);

            // A pixel over, so no antialiased rim is left in a corner.
            return 2 * new Vector2(x, y).Length + 2;
        }

        protected override void Update()
        {
            base.Update();

            content.Size = DrawSize;

            if (active)
            {
                mask.Masking = true;
                mask.Size = new Vector2(Diameter);
                mask.Position = centre - mask.Size / 2;

                // Counter to the mask, so the picture stays where it is.
                content.Position = -mask.Position;
            }
            else
            {
                mask.Masking = false;
                mask.Size = DrawSize;
                mask.Position = Vector2.Zero;
                content.Position = Vector2.Zero;
            }
        }
    }
}
