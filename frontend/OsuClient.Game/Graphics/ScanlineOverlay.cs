using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osuTK.Graphics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OsuClient.Game.Graphics
{
    /// <summary>
    /// CRT scanlines over whatever sits behind them.
    ///
    /// A 1px-wide column of the screen's exact height, stretched horizontally
    /// — stretching across is free and keeps the lines pixel-crisp vertically,
    /// which a tiled or scaled texture wouldn't (it would moiré as the window
    /// resizes). The texture is rebuilt whenever the height changes.
    ///
    /// Written for song select's background and pulled out here when the main
    /// menu needed the same thing (MENU_REDESIGN_PLAN.md step 2) — two copies
    /// of a texture bake drift, and these two screens are meant to look like
    /// the same television.
    /// </summary>
    public partial class ScanlineOverlay : Sprite
    {
        /// <summary>
        /// Lines every this many pixels — 4 (one dark row in four) reads as
        /// CRT without shimmering.
        /// </summary>
        private readonly int period;

        [Resolved]
        private IRenderer renderer { get; set; } = null!;

        private int builtHeight;

        public ScanlineOverlay(int period = 4, float alpha = 0.16f)
        {
            this.period = period;

            RelativeSizeAxes = Axes.Both;
            FillMode = FillMode.Stretch;
            Alpha = alpha;
            Colour = Color4.Black;
        }

        protected override void Update()
        {
            base.Update();

            int height = (int)DrawHeight;

            if (height <= 0 || height == builtHeight)
                return;

            builtHeight = height;

            using var image = new Image<Rgba32>(1, height);

            for (int y = 0; y < height; y++)
                image[0, y] = y % period == 0 ? new Rgba32(0, 0, 0, 255) : new Rgba32(0, 0, 0, 0);

            var built = renderer.CreateTexture(1, height);
            built.SetData(new TextureUpload(image.Clone()));

            Texture?.Dispose();
            Texture = built;
        }
    }
}
