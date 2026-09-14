using System;
using System.Collections.Generic;
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

        [Resolved]
        private IRenderer renderer { get; set; } = null!;

        private RetroFontFamily fontFamily = RetroFontFamily.Body;
        private string text = string.Empty;
        private float textSize = 20;
        private bool dirty = true;

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

            if (IsLoaded)
                render();
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            render();
        }

        private void render()
        {
            if (!dirty)
                return;

            dirty = false;

            Texture?.Dispose();

            if (string.IsNullOrEmpty(text))
            {
                Texture = null!;
                Size = Vector2.Zero;
                return;
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
        }
    }
}
