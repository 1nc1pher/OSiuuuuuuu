using System;
using System.IO;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OsuClient.Game.Graphics
{
    /// <summary>
    /// A beatmap's background image, covering whatever space it's given
    /// (cropped, not stretched, via <see cref="FillMode.Fill"/>) — or nothing
    /// at all if the beatmap doesn't have one, leaving whatever sits behind it
    /// (currently a flat dark fill) showing through.
    ///
    /// Loaded the same way <see cref="RetroText"/> rasterizes its fonts:
    /// SixLabors.ImageSharp straight to a <see cref="Texture"/>, rather than
    /// through osu.Framework's own texture store — there's no reason to
    /// expect that path to have the bitmap-atlas-only surprise the font store
    /// did, but this is the already-proven route in this codebase and a
    /// single image load isn't worth re-deriving it for.
    /// </summary>
    public partial class BeatmapBackground : Sprite
    {
        private readonly string? path;

        [Resolved]
        private IRenderer renderer { get; set; } = null!;

        public BeatmapBackground(string? path)
        {
            this.path = path;

            RelativeSizeAxes = Axes.Both;
            Anchor = Anchor.Centre;
            Origin = Anchor.Centre;
            FillMode = FillMode.Fill;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            if (path == null || !File.Exists(path))
                return;

            try
            {
                var image = Image.Load<Rgba32>(path);

                var texture = renderer.CreateTexture(image.Width, image.Height);
                texture.SetData(new TextureUpload(image));

                Texture = texture;
            }
            catch (Exception e) when (e is UnknownImageFormatException or IOException)
            {
                // A missing background silently shows nothing (see class
                // remarks); a present-but-unreadable one — wrong format, half
                // a copy still in progress — should fail the same way rather
                // than taking gameplay down with it.
            }
        }
    }
}
