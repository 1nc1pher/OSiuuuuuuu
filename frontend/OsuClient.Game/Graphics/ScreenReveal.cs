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
    /// Everything a screen draws, held behind a straight vertical edge that
    /// wipes across the window from one side: the screen arriving the way a
    /// VCR's picture rolls in behind a tracking band, with whatever was there
    /// before still showing ahead of the edge.
    ///
    /// <para>
    /// The picture inside is held still while the edge moves across it, so
    /// the band is seen sweeping over a screen already in place rather than
    /// the screen sliding in. Outside a reveal it is an ordinary full-size
    /// container with no mask at all.
    /// </para>
    ///
    /// <para>
    /// The mask is a plain rectangle on purpose. It was a circle, and on the
    /// integrated GPU this game is tuned on, rounded masking of anything much
    /// bigger than a few hundred pixels silently fell back to its bounding
    /// square; a rectangle is exact there, and is the cheapest mask there is.
    /// </para>
    /// </summary>
    public partial class ScreenReveal : Container
    {
        /// <summary>How strongly a collapsed reveal draws its screen: enough to be drawn, too little to be seen.</summary>
        private const float priming_alpha = 0.004f;

        private readonly Container mask;
        private readonly Container content;

        private bool active;
        private Vector2 origin;

        protected override Container<Drawable> Content => content;

        public ScreenReveal()
        {
            RelativeSizeAxes = Axes.Both;

            InternalChild = mask = new Container
            {
                Child = content = new Container(),
            };
        }

        /// <summary>How far the wipe has got, 0 (nothing showing) to 1 (the whole window). Transformable.</summary>
        public float Progress { get; set; }

        /// <summary>Whether a reveal is under way. Exposed for tests and screenshot scenes.</summary>
        public bool Revealing => active;

        /// <summary>Where the wipe started from (local, and so window, space).</summary>
        public Vector2 Origin => origin;

        /// <summary>
        /// Hides everything behind an edge at <paramref name="at"/> (local, and
        /// so window, space), ready for <see cref="Sweep"/>.
        /// </summary>
        public void Collapse(Vector2 at)
        {
            ClearTransforms(false, nameof(Progress));

            active = true;
            origin = at;
            Progress = 0;
        }

        /// <summary>
        /// Moves the edge from where <see cref="Collapse"/> put it until the
        /// window is covered, then drops the mask.
        /// </summary>
        public void Sweep(double duration, Easing easing)
        {
            if (!active)
                return;

            this.TransformTo(nameof(Progress), 1f, duration, easing)
                .OnComplete(_ => active = false);
        }

        /// <summary>
        /// The horizontal span a wipe from <paramref name="from"/> covers at
        /// <paramref name="progress"/> across a window <paramref name="width"/>
        /// wide. From an edge it grows across the whole window from that edge;
        /// from anywhere in between it grows both ways, reaching both edges
        /// together.
        /// </summary>
        public static (float Left, float Right) CoveredSpan(Vector2 from, float width, float progress)
        {
            float x = Math.Clamp(from.X, 0, width);

            return (x - x * progress, x + (width - x) * progress);
        }

        protected override void Update()
        {
            base.Update();

            content.Size = DrawSize;

            if (active && Progress <= 0)
            {
                // Collapsed, waiting for the sweep: drawn whole but too faint
                // to see (under one step of 8-bit colour). A zero-width mask
                // would cull everything, and the screen's first real draw —
                // every texture upload, the blurred background's first render
                // — would then land on the sweep's first frame, as a stall
                // right as the edge starts to move. Drawn like this, it is
                // paid while the screen waits instead.
                mask.Masking = false;
                mask.Size = DrawSize;
                mask.Position = Vector2.Zero;
                content.Position = Vector2.Zero;
                content.Alpha = priming_alpha;
            }
            else if (active)
            {
                content.Alpha = 1;

                var (left, right) = CoveredSpan(origin, DrawWidth, Progress);

                mask.Masking = true;
                mask.Position = new Vector2(left, 0);
                mask.Size = new Vector2(Math.Max(right - left, 0), DrawHeight);

                // Counter to the mask, so the picture stays where it is.
                content.Position = -mask.Position;
            }
            else
            {
                content.Alpha = 1;
                mask.Masking = false;
                mask.Size = DrawSize;
                mask.Position = Vector2.Zero;
                content.Position = Vector2.Zero;
            }
        }
    }
}
