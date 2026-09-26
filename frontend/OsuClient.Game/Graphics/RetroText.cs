using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using osu.Framework.Allocation;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osuTK;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Color = SixLabors.ImageSharp.Color;

namespace OsuClient.Game.Graphics
{
    /// <summary>The two retro faces gameplay text is set in — see <see cref="RetroText"/>.</summary>
    public enum RetroFontFamily
    {
        /// <summary>Press Start 2P — blocky 8-bit arcade pixels, for short punchy numbers: combo, judgements, grade.</summary>
        Display,

        /// <summary>Audiowide — sharp geometric sci-fi, for everything else: titles, labels, menus.</summary>
        Body,
    }

    /// <summary>
    /// Renders text in one of the two retro fonts by rasterizing it to a
    /// texture at construction/change time, rather than through
    /// osu.Framework's own text system.
    ///
    /// osu.Framework's built-in fonts (<see cref="osu.Framework.Game.AddFont"/>)
    /// expect a font pre-baked into its own bitmap-atlas format — individual
    /// glyph images plus a parameters file — not a plain .ttf loaded at
    /// runtime; feeding it a raw TTF fails deep in its glyph cache with a
    /// null texture stream. SixLabors.Fonts (already alongside
    /// SixLabors.ImageSharp, which osu.Framework itself depends on) rasterizes
    /// the actual downloaded font files directly instead.
    /// </summary>
    public partial class RetroText : Sprite
    {
        private const float supersample = 2f;

        private static readonly Dictionary<RetroFontFamily, FontFamily> families = new Dictionary<RetroFontFamily, FontFamily>
        {
            [RetroFontFamily.Display] = loadFamily("PressStart2P-Regular.ttf"),
            [RetroFontFamily.Body] = loadFamily("Audiowide-Regular.ttf"),
        };

        private static FontFamily loadFamily(string fileName)
        {
            string resourceName = $"OsuClient.Game.Resources.Fonts.{fileName}";
            using var stream = typeof(RetroText).Assembly.GetManifestResourceStream(resourceName)
                                ?? throw new InvalidOperationException($"embedded font resource not found: {resourceName}");

            return new FontCollection().Add(stream);
        }

        /// <summary>
        /// Runs the rasterizer once per font, off to the side, so the first
        /// real label does not pay for font parsing, glyph caching and JIT.
        /// Safe from any thread: it touches no drawable and no renderer.
        /// </summary>
        public static void Warm()
        {
            const string sample = "RIMO Warm-up 0123456789 abcdefghijklmnopqrstuvwxyz";

            foreach (var family in families.Values)
            {
                var font = family.CreateFont(20 * supersample, FontStyle.Regular);
                var options = new TextOptions(font);

                var size = TextMeasurer.MeasureSize(sample, options);

                using var image = new Image<Rgba32>(Math.Max(1, (int)size.Width), Math.Max(1, (int)size.Height) + 4);
                image.Mutate(ctx => ctx.DrawText(sample, font, Color.White, new PointF(0, 0)));
            }
        }

        [Resolved]
        private IRenderer renderer { get; set; } = null!;

        private RetroFontFamily fontFamily = RetroFontFamily.Body;
        private string text = string.Empty;
        private float textSize = 20;
        private bool dirty = true;

        /// <summary>Whether the texture on show belongs to the shared cache, and so must not be disposed here.</summary>
        private bool textureShared;

        /// <summary>
        /// Rendered textures shared between every <see cref="Shared"/> text of
        /// the same font, size and string, per renderer (tests run several
        /// hosts in one process, and a texture must never cross renderers).
        /// </summary>
        private static readonly ConditionalWeakTable<IRenderer, Dictionary<(RetroFontFamily, float, string), (Texture Texture, Vector2 Size)>> shared_cache =
            new ConditionalWeakTable<IRenderer, Dictionary<(RetroFontFamily, float, string), (Texture, Vector2)>>();

        /// <summary>
        /// Reuse one rendered texture for every text with this font, size and
        /// string, instead of rendering each afresh. For text that appears
        /// many times over, such as the numbers on hit circles: a map spawns
        /// hundreds of circles but only a few distinct numbers. Set before
        /// the text loads.
        /// </summary>
        public bool Shared { get; init; }

        public RetroFontFamily Font
        {
            get => fontFamily;
            set
            {
                if (fontFamily == value)
                    return;

                fontFamily = value;
                invalidate();
            }
        }

        public string Text
        {
            get => text;
            set
            {
                if (text == value)
                    return;

                text = value;
                invalidate();
            }
        }

        public float TextSize
        {
            get => textSize;
            set
            {
                if (textSize == value)
                    return;

                textSize = value;
                invalidate();
            }
        }

        private void invalidate()
        {
            dirty = true;

            // From Ready, not only once fully loaded: a screen loading in the
            // background often fills in text after its panels have loaded but
            // before they reach the screen (song select's info panel does).
            // Waiting for LoadComplete moved all of that rasterizing, and its
            // texture uploads, onto the update thread in the screen's first
            // frames — a stall right as the transition started to move.
            if (LoadState >= osu.Framework.Graphics.LoadState.Ready)
                render();
        }

        /// <summary>
        /// Rasterizes as part of loading, rather than waiting for
        /// <see cref="LoadComplete"/>. A screen loaded in the background —
        /// song select, while the menu's ripple plays — then does its text
        /// there too; left to LoadComplete, the thirty-odd labels of its first
        /// song all rasterized on the update thread in the frame it appeared,
        /// which stuttered the transition. Text created on the update thread
        /// costs what it did before.
        /// </summary>
        [BackgroundDependencyLoader]
        private void load() => render();

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // Only does anything if the text changed between load and now.
            render();
        }

        private void render()
        {
            if (!dirty)
                return;

            dirty = false;

            if (!textureShared)
                Texture?.Dispose();

            textureShared = false;

            if (string.IsNullOrEmpty(text))
            {
                Texture = null!;
                Size = Vector2.Zero;
                return;
            }

            Dictionary<(RetroFontFamily, float, string), (Texture Texture, Vector2 Size)>? cache = null;
            var key = (fontFamily, textSize, text);

            if (Shared)
            {
                cache = shared_cache.GetValue(renderer, _ => new Dictionary<(RetroFontFamily, float, string), (Texture, Vector2)>());

                lock (cache)
                {
                    if (cache.TryGetValue(key, out var hit))
                    {
                        Texture = hit.Texture;
                        Size = hit.Size;
                        textureShared = true;
                        return;
                    }
                }
            }

            var font = families[fontFamily].CreateFont(textSize * supersample, FontStyle.Regular);
            var options = new TextOptions(font);

            var size = TextMeasurer.MeasureSize(text, options);
            var bounds = TextMeasurer.MeasureBounds(text, options);

            const int padding = 2;
            int width = Math.Max(1, (int)MathF.Ceiling(size.Width) + padding * 2);
            int height = Math.Max(1, (int)MathF.Ceiling(size.Height) + padding * 2);

            var image = new Image<Rgba32>(width, height);
            image.Mutate(ctx => ctx.DrawText(text, font, Color.White, new PointF(padding - bounds.X, padding - bounds.Y)));

            var texture = renderer.CreateTexture(width, height);
            texture.SetData(new TextureUpload(image));

            Texture = texture;
            Size = new Vector2(width / supersample, height / supersample);

            if (cache != null)
            {
                lock (cache)
                {
                    // Another text may have rendered the same thing meanwhile;
                    // keep whichever got there first and use that.
                    if (cache.TryGetValue(key, out var existing))
                    {
                        texture.Dispose();
                        Texture = existing.Texture;
                        Size = existing.Size;
                    }
                    else
                        cache[key] = (texture, Size);
                }

                textureShared = true;
            }
        }
    }
}
