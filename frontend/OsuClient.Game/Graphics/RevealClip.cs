using System;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Graphics
{
    /// <summary>
    /// Holds a screen that another is arriving over through a
    /// <see cref="ScreenReveal"/>: draws only the part of it the wipe has not
    /// covered yet, and fades that part away as the wipe crosses it.
    ///
    /// <para>
    /// The screen underneath stays on screen for the whole sweep, and drawn
    /// in full it doubled the cost of every frame: the menu and song select
    /// each fit a frame on their own, but not both, so the sweep ran at half
    /// rate. The arriving screen is opaque behind its edge, so nothing there
    /// can ever show. This clips the screen beneath to exactly the strip the
    /// wipe has yet to reach, and stops drawing it altogether once the wipe
    /// covers the window.
    /// </para>
    ///
    /// <para>
    /// The fade is a black wash over the clipped strip, not the screen's own
    /// alpha: fading a screen made of layers lets each layer show through the
    /// ones above it on the way out. A shadow runs just ahead of the edge, so
    /// the picture darkens into the tracking band rather than being cut by it.
    /// </para>
    ///
    /// <para>
    /// Outside a reveal it is an ordinary full-size container with no mask.
    /// </para>
    /// </summary>
    public partial class RevealClip : Container
    {
        /// <summary>How dark the whole screen gets by the time the wipe is most of the way across.</summary>
        private const float fade_depth = 0.55f;

        /// <summary>How dark the screen gets right at the edge.</summary>
        private const float shadow_depth = 0.6f;

        /// <summary>How far ahead of the edge the shadow reaches, as a fraction of the window height.</summary>
        private const float shadow_reach = 0.3f;

        private readonly Container content;
        private readonly Box fade;
        private readonly Box shadow;

        private ScreenReveal? reveal;

        protected override Container<Drawable> Content => content;

        public RevealClip()
        {
            InternalChildren = new Drawable[]
            {
                content = new Container(),
                fade = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = Color4.Black,
                    Alpha = 0,
                },
                shadow = new Box
                {
                    RelativeSizeAxes = Axes.Y,
                    Alpha = 0,
                },
            };
        }

        /// <summary>Starts clipping to, and fading, what <paramref name="covering"/> leaves uncovered.</summary>
        public void Follow(ScreenReveal covering) => reveal = covering;

        /// <summary>Back to drawing everything, unclipped and unfaded.</summary>
        public void Release()
        {
            reveal = null;
            content.Alpha = 1;
            fade.Alpha = 0;
            shadow.Alpha = 0;
        }

        protected override void Update()
        {
            base.Update();

            Vector2 size = Parent!.DrawSize;

            content.Size = size;

            if (reveal == null)
            {
                unclipped(size);
                return;
            }

            if (!reveal.Revealing)
            {
                // The wipe has finished: the arriving screen covers the whole
                // window. Hidden on the child, never this container, so this
                // keeps updating and can bring it back (a drawable that hides
                // itself stops being updated).
                content.Alpha = 0;
                fade.Alpha = shadow.Alpha = 0;
                unclipped(size);
                return;
            }

            var (coveredLeft, coveredRight) = ScreenReveal.CoveredSpan(reveal.Origin, size.X, reveal.Progress);

            // What's still showing: the side of the window the wipe is
            // heading towards. A wipe from somewhere in the middle leaves a
            // strip either side; the box around both is the whole width, so
            // it simply isn't clipped.
            bool showingLeft = coveredLeft > 0;
            bool showingRight = coveredRight < size.X;

            float left = showingLeft ? 0 : coveredRight;
            float right = showingRight ? size.X : coveredLeft;

            if (right - left <= 0)
            {
                content.Alpha = 0;
                fade.Alpha = shadow.Alpha = 0;
                unclipped(size);
                return;
            }

            content.Alpha = 1;
            Masking = true;
            Position = new Vector2(left, 0);
            Size = new Vector2(right - left, size.Y);

            // Counter to the mask, so the picture stays where it is.
            content.Position = -Position;

            fade.Alpha = fade_depth * Math.Min(1, reveal.Progress * 1.6f);

            // The shadow lies against whichever edge is moving into this
            // strip, darkest there and clear a little way ahead of it.
            float reach = size.Y * shadow_reach;
            bool edgeOnRight = showingLeft;

            shadow.Alpha = 1;
            shadow.Width = reach;
            shadow.X = edgeOnRight ? Size.X - reach : 0;
            shadow.Colour = edgeOnRight
                ? ColourInfo.GradientHorizontal(Color4.Black.Opacity(0), Color4.Black.Opacity(shadow_depth))
                : ColourInfo.GradientHorizontal(Color4.Black.Opacity(shadow_depth), Color4.Black.Opacity(0));
        }

        private void unclipped(Vector2 size)
        {
            Masking = false;
            Position = Vector2.Zero;
            Size = size;
            content.Position = Vector2.Zero;
        }
    }
}
