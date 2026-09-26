using System;
using System.Runtime.CompilerServices;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Textures;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OsuClient.Game.Graphics
{
    /// <summary>
    /// The playing surface of a small record, drawn once into a texture: a
    /// translucent dark disc with fine concentric grooves and a faint sheen
    /// catching them, open in the middle where a label goes.
    ///
    /// Hit circles show it as their face. One texture, generated on first use
    /// and shared by every circle, so a circle costs one quad for all its
    /// grooves rather than one per groove. It is translucent throughout, so
    /// the song's background shows through the record; the grooves read on
    /// top without hiding it.
    /// </summary>
    public static class VinylTexture
    {
        private const int size = 256;

        /// <summary>Where the label hole ends, as a fraction of the radius.</summary>
        public const float LabelFraction = 0.44f;

        private static readonly ConditionalWeakTable<IRenderer, Texture> textures = new ConditionalWeakTable<IRenderer, Texture>();

        public static Texture Get(IRenderer renderer) =>
            textures.GetValue(renderer, r =>
            {
                var texture = r.CreateTexture(size, size);
                texture.SetData(new TextureUpload(generate()));
                return texture;
            });

        private static Image<Rgba32> generate()
        {
            var image = new Image<Rgba32>(size, size);

            const float half = size / 2f;
            const float groove_count = 11;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float r = MathF.Sqrt(dx * dx + dy * dy);

                    // Soft edges, a pixel wide, at the rim and the label hole.
                    float outer = Math.Clamp((1f - r) * half, 0, 1);
                    float inner = Math.Clamp((r - LabelFraction) * half, 0, 1);
                    float coverage = outer * inner;

                    if (coverage <= 0)
                        continue;

                    // Vinyl: near-black with a violet cast, mostly see-through.
                    float red = 0.05f, green = 0.04f, blue = 0.09f;
                    float alpha = 0.38f;

                    // Grooves: thin lighter rings across the playing surface.
                    float groove = (r - LabelFraction) / (1 - LabelFraction) * groove_count;
                    float distance = MathF.Abs(groove - MathF.Round(groove));

                    if (distance < 0.12f)
                    {
                        red += 0.24f;
                        green += 0.22f;
                        blue += 0.29f;
                        alpha += 0.18f;
                    }

                    // Sheen: two opposite wedges of light across the grooves,
                    // the way a record catches a lamp. Fixed, not turning.
                    float angle = MathF.Atan2(dy, dx);
                    float sheen = MathF.Max(wedge(angle, -2.4f), wedge(angle, 0.74f));

                    red += 0.3f * sheen;
                    green += 0.3f * sheen;
                    blue += 0.34f * sheen;
                    alpha += 0.14f * sheen;

                    image[x, y] = new Rgba32(
                        Math.Clamp(red, 0, 1),
                        Math.Clamp(green, 0, 1),
                        Math.Clamp(blue, 0, 1),
                        Math.Clamp(alpha * coverage, 0, 1));
                }
            }

            return image;
        }

        /// <summary>1 at <paramref name="centre"/>, falling to 0 about 0.35 radians either side.</summary>
        private static float wedge(float angle, float centre)
        {
            float delta = MathF.Abs(MathF.IEEERemainder(angle - centre, MathF.Tau));
            return MathF.Max(0, 1 - delta / 0.35f);
        }
    }
}
