using System;
using System.Collections.Generic;
using System.IO;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Textures;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace OsuClient.Game.Graphics
{
    /// <summary>
    /// Loads a beatmap's cover image as a texture, downscaled.
    ///
    /// Song select puts a dozen covers on screen at once and none of them is
    /// more than a few hundred pixels across, so decoding them at full size
    /// would spend texture memory on detail nothing can show. Shared by the
    /// wheel's wedges and its selected-song card so they agree on that.
    /// </summary>
    public static class CoverArt
    {
        /// <summary>
        /// Decoded covers, keyed by path and resolution.
        ///
        /// Song select asks for the same handful of images constantly —
        /// rebuilding every wedge on each search keystroke, and re-reading the
        /// selected song's cover on every turn of the wheel. Decoding a
        /// multi-megabyte JPEG on the update thread each time is what made
        /// turning the record stutter.
        ///
        /// Textures live here for the life of the process. That's bounded by
        /// the library size (one small square per set), and they'd be resident
        /// anyway — every set already has a wedge holding one.
        ///
        /// Assumes one renderer per process, which holds for both the game and
        /// the test host. Caching across renderers would hand out textures
        /// belonging to a disposed one.
        /// </summary>
        private static readonly Dictionary<string, Texture> cache = new Dictionary<string, Texture>();

        private static readonly object cache_lock = new object();

        /// <summary>
        /// The texture for <paramref name="path"/>, resized to a square of
        /// <paramref name="resolution"/>, or null when there's no usable image
        /// there. Callers are expected to cope with null rather than treat it
        /// as an error — most sets simply have no art.
        ///
        /// The returned texture is owned by the cache and shared between
        /// callers — never dispose it.
        /// </summary>
        public static Texture? Load(IRenderer renderer, string? path, int resolution)
        {
            if (path == null || !File.Exists(path))
                return null;

            string key = $"{resolution}|{path}";

            lock (cache_lock)
            {
                if (cache.TryGetValue(key, out var cached))
                    return cached;
            }

            try
            {
                using var image = Image.Load<Rgba32>(path);

                image.Mutate(ctx => ctx.Resize(resolution, resolution));

                var texture = renderer.CreateTexture(image.Width, image.Height);
                texture.SetData(new TextureUpload(image.Clone()));

                lock (cache_lock)
                {
                    // Two threads can decode the same cover at once — a wedge
                    // loading in the background while the card asks for it on
                    // the update thread. Whichever lands first wins, so every
                    // caller ends up with the same texture.
                    if (cache.TryGetValue(key, out var raced))
                    {
                        texture.Dispose();
                        return raced;
                    }

                    cache[key] = texture;
                }

                return texture;
            }
            catch (Exception e) when (e is UnknownImageFormatException or IOException
                                          or InvalidImageContentException)
            {
                return null;
            }
        }
    }
}
