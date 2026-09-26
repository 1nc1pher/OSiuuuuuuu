using System;
using System.Runtime.CompilerServices;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Textures;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OsuClient.Game.Graphics
{
    /// <summary>
    /// Frames of the noise a VCR shows when it loses tracking: horizontal
    /// streaks of light at random heights, a faint grain between them, and
    /// the odd line fringed cyan or magenta where the colour signal slips.
    ///
    /// Drawn once into a handful of small textures and flipped between, so a
    /// band of "live" static costs one stretched quad a frame instead of a
    /// noise shader. Each texel row stretches to several screen lines, which
    /// is the blocky scanline look of the real thing. The texture fades out
    /// towards its left and right edges, so the band it draws has no hard
    /// sides.
    /// </summary>
    public static class VhsNoise
    {
        public const int FrameCount = 6;

        private const int width = 96;
        private const int height = 240;

        private static readonly ConditionalWeakTable<IRenderer, Texture[]> frames = new ConditionalWeakTable<IRenderer, Texture[]>();

        /// <summary>The noise frames for <paramref name="renderer"/>, generated on first use.</summary>
        public static Texture[] Get(IRenderer renderer) =>
            frames.GetValue(renderer, r =>
            {
                var result = new Texture[FrameCount];

                for (int i = 0; i < FrameCount; i++)
                {
                    var texture = r.CreateTexture(width, height);
                    // Seeded, so the static looks the same every run and a
                    // screenshot of it is reproducible.
                    texture.SetData(new TextureUpload(generate(new Random(1981 + i))));
                    result[i] = texture;
                }

                return result;
            });

        private static Image<Rgba32> generate(Random random)
        {
            var image = new Image<Rgba32>(width, height);

            for (int y = 0; y < height; y++)
            {
                // Most rows are faint grain; some carry a bright streak.
                float grain = 0.08f + 0.22f * MathF.Pow(random.NextSingle(), 3);

                bool streak = random.NextSingle() < 0.28f;
                float streakStart = random.NextSingle() * width;
                float streakLength = 10 + random.NextSingle() * 60;
                float streakBrightness = 0.55f + 0.45f * random.NextSingle();

                // Where the colour slips: a whole row tinted one way.
                float tint = random.NextSingle();
                (float r, float g, float b) colour = tint < 0.1f ? (0.35f, 0.95f, 1f)
                    : tint < 0.2f ? (1f, 0.35f, 0.7f)
                    : (1f, 1f, 1f);

                for (int x = 0; x < width; x++)
                {
                    float value = grain * (0.5f + random.NextSingle());

                    if (streak)
                    {
                        // Soft ends, so a streak reads as a smear of light.
                        float along = (x - streakStart) / streakLength;

                        if (along is >= 0 and <= 1)
                            value = MathF.Max(value, streakBrightness * MathF.Sin(along * MathF.PI));
                    }

                    // Clear at the texture's left and right edges, full in the
                    // middle.
                    float across = x / (float)(width - 1);
                    float envelope = MathF.Pow(MathF.Sin(across * MathF.PI), 0.6f);

                    float alpha = Math.Clamp(value * envelope, 0, 1);

                    image[x, y] = new Rgba32(colour.r, colour.g, colour.b, alpha);
                }
            }

            return image;
        }
    }
}
