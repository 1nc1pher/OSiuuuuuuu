using System;
using System.Collections.Generic;
using System.IO;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace OsuClient.Game.Graphics
{
    /// <summary>
    /// A song's cover as a full-picture texture — not a square thumbnail like
    /// <see cref="CoverArt"/> — decoded once per song and shared.
    ///
    /// <para>
    /// The cue screen shows one cover three ways at once: blurred behind
    /// everything, sharp inside its reveal, and cropped onto the record's
    /// label. Decoded three times at full size, that was most of a second
    /// between choosing a song and anything moving. Here it is decoded once,
    /// at no more than a screen's worth of pixels, and song select warms it
    /// while the song is merely selected — so by the time it is chosen, the
    /// picture is already waiting.
    /// </para>
    /// </summary>
    public static class CoverTextures
    {
        /// <summary>The longest side kept. A background is never shown larger than the window.</summary>
        private const int max_side = 1600;

        private static readonly Dictionary<string, Texture> cache = new Dictionary<string, Texture>();
        private static readonly object cache_lock = new object();

        /// <summary>
        /// The cover at <paramref name="path"/>, or null when there is no
        /// usable image. Owned by the cache and shared: never dispose it. Safe
        /// to call from a load thread.
        /// </summary>
        public static Texture? Load(IRenderer renderer, string? path)
        {
            if (path == null || !File.Exists(path))
                return null;

            lock (cache_lock)
            {
                if (cache.TryGetValue(path, out var cached))
                    return cached;
            }

            try
            {
                using var image = Image.Load<Rgba32>(path);

                float scale = Math.Min(1f, max_side / (float)Math.Max(image.Width, image.Height));

                if (scale < 1f)
                    image.Mutate(ctx => ctx.Resize((int)(image.Width * scale), (int)(image.Height * scale)));

                var texture = renderer.CreateTexture(image.Width, image.Height);
                texture.SetData(new TextureUpload(image.Clone()));

                lock (cache_lock)
                {
                    // Warmed from song select and asked for by the cue screen:
                    // whichever lands first wins, and both get the same one.
                    if (cache.TryGetValue(path, out var raced))
                    {
                        texture.Dispose();
                        return raced;
                    }

                    cache[path] = texture;
                }

                return texture;
            }
            catch (Exception e) when (e is UnknownImageFormatException or IOException or InvalidImageContentException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// A cover filling its space, from <see cref="CoverTextures"/>: the same
    /// picture as a <see cref="BeatmapBackground"/>, without decoding it again.
    /// </summary>
    public partial class CoverSprite : Sprite
    {
        private readonly string? path;

        [osu.Framework.Allocation.Resolved]
        private IRenderer renderer { get; set; } = null!;

        public CoverSprite(string? path)
        {
            this.path = path;

            RelativeSizeAxes = Axes.Both;
            Anchor = Anchor.Centre;
            Origin = Anchor.Centre;
            FillMode = FillMode.Fill;
        }

        [osu.Framework.Allocation.BackgroundDependencyLoader]
        private void load()
        {
            var texture = CoverTextures.Load(renderer, path);

            if (texture != null)
                Texture = texture;
        }
    }
}
