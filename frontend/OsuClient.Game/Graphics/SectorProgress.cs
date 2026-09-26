using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Rendering.Vertices;
using osu.Framework.Graphics.UserInterface;
using osuTK;

namespace OsuClient.Game.Graphics
{
    /// <summary>
    /// A <see cref="CircularProgress"/> that only rasterises the part of the
    /// ring it actually shows.
    ///
    /// The stock draw node covers the whole ring whatever
    /// <see cref="CircularProgress.Progress"/> is, and leaves the shader to
    /// throw away everything past the arc's end. For a full ring that's
    /// nothing wasted; for song select's wheel it was most of the frame: every
    /// song is three stacked slices, and each slice paid for a whole ring, so
    /// the record cost 3 × (song count) rings of fill every frame and the
    /// wheel fell to 30 fps at a few dozen songs.
    ///
    /// This draws the same shader over a fan of triangles covering just the
    /// arc (plus a sliver either side for the anti-aliased edges), with the
    /// same texture coordinates at every point, so each pixel it does cover
    /// comes out exactly as before. A wheel of slices now costs one ring's
    /// worth of fill per layer however many songs it holds.
    /// </summary>
    public partial class SectorProgress : CircularProgress
    {
        protected override DrawNode CreateDrawNode() => new SectorDrawNode(this);

        private class SectorDrawNode : CircularProgressDrawNode
        {
            /// <summary>
            /// Widest step between fan vertices. The outer edge is pushed out
            /// so its chords stay outside the true circle (see
            /// <see cref="Blit"/>), and a finer step keeps that overshoot —
            /// pixels the shader will discard anyway — small.
            /// </summary>
            private static readonly float max_step = MathHelper.DegreesToRadians(9);

            private const int max_segments = 42;

            private Vector2 drawSize;
            private RectangleF textureRect;

            private IVertexBatch<TexturedVertex2D>? batch;

            public SectorDrawNode(SectorProgress source)
                : base(source)
            {
            }

            public override void ApplyState()
            {
                base.ApplyState();

                drawSize = Source.DrawSize;
                textureRect = Texture.GetTextureRect();
            }

            protected override void Blit(IRenderer renderer)
            {
                if (InnerRadius == 0 || Progress == 0)
                    return;

                if (Progress >= 1)
                {
                    // Nothing to trim on a closed ring.
                    base.Blit(renderer);
                    return;
                }

                if (!renderer.BindTexture(Texture))
                    return;

                batch ??= renderer.CreateLinearBatch<TexturedVertex2D>(max_segments * 6, 1, PrimitiveTopology.Triangles);

                // Radii in the unit square the shader works in (0.5 is the
                // rim). The inner edge sits a texel inside the ring's own, as
                // the stock node places it, so the anti-aliased inner rim is
                // never clipped.
                float inner = Math.Max(0.5f - Math.Max(InnerRadius * 0.5f + TexelSize, TexelSize * 2), 0);

                // Room past each straight edge for its anti-aliasing: the
                // shader fades over one texel, measured at the narrowest
                // point of the ring. Clamped so a thin highlight near the hub
                // doesn't ask for a runaway margin.
                float margin = Math.Min(3 * TexelSize / Math.Max(inner, 0.05f), 0.2f);

                float start = -margin;
                float end = MathF.Tau * Progress + margin;

                int segments = Math.Clamp((int)MathF.Ceiling((end - start) / max_step), 1, max_segments);
                float step = (end - start) / segments;

                // Outer vertices sit on a slightly larger circle so the
                // straight chord between neighbours still clears the rim.
                float outer = 0.5f / MathF.Cos(step / 2) + TexelSize;

                var centre = new Vector2(0.5f);

                for (int i = 0; i < segments; i++)
                {
                    float a0 = start + i * step;
                    float a1 = a0 + step;

                    var outer0 = vertex(renderer, pointAt(centre, outer, a0));
                    var outer1 = vertex(renderer, pointAt(centre, outer, a1));
                    var inner0 = vertex(renderer, pointAt(centre, inner, a0));
                    var inner1 = vertex(renderer, pointAt(centre, inner, a1));

                    batch.Add(outer0);
                    batch.Add(inner0);
                    batch.Add(outer1);

                    batch.Add(outer1);
                    batch.Add(inner0);
                    batch.Add(inner1);
                }
            }

            /// <summary>
            /// The point <paramref name="radius"/> out from the centre at
            /// <paramref name="angle"/>, measured clockwise from 12 o'clock —
            /// the same zero and direction the shader measures progress in.
            /// </summary>
            private static Vector2 pointAt(Vector2 centre, float radius, float angle) =>
                centre + new Vector2(MathF.Sin(angle), -MathF.Cos(angle)) * radius;

            private TexturedVertex2D vertex(IRenderer renderer, Vector2 position) => new TexturedVertex2D(renderer)
            {
                Position = Vector2Extensions.Transform(position * drawSize, DrawInfo.Matrix),
                Colour = DrawColourInfo.Colour.Interpolate(position).SRGB,
                TextureRect = new Vector4(textureRect.Left, textureRect.Top, textureRect.Right, textureRect.Bottom),
                TexturePosition = new Vector2(textureRect.Left + textureRect.Width * position.X,
                    textureRect.Top + textureRect.Height * position.Y),
            };

            protected override void Dispose(bool isDisposing)
            {
                base.Dispose(isDisposing);
                batch?.Dispose();
            }
        }
    }
}
