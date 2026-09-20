using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using OsuClient.Game.Graphics;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.SongSelect
{
    /// <summary>
    /// What sits behind song select: the selected song's cover art, blurred,
    /// under a sunset colour grade and CRT scanlines
    /// (CAROUSEL_REDESIGN_PLAN.md step 2).
    ///
    /// Art crossfades rather than cutting, because the wheel can move through
    /// several songs in a second and a hard cut on each one strobes.
    /// </summary>
    public partial class CarouselBackground : CompositeDrawable
    {
        private const float blur_sigma = 26;

        /// <summary>
        /// The art is drawn larger than the screen so the blur's edge falloff
        /// happens outside the visible area. A <see cref="BufferedContainer"/>
        /// clips its effect to the buffer, so art drawn exactly to the edges
        /// fades to transparent along every side.
        /// </summary>
        private const float art_overscan = 1.18f;

        private readonly Container artLayers;

        private string? currentPath;

        /// <summary>
        /// The newest requested layer, which may still be loading and may
        /// never be shown at all if another request supersedes it.
        /// </summary>
        private Drawable? pendingLayer;

        /// <summary>
        /// The layer actually on screen. Kept separate from
        /// <see cref="pendingLayer"/> because only a drawable that made it
        /// into the tree can safely be animated.
        /// </summary>
        private Drawable? displayedLayer;

        public CarouselBackground()
        {
            RelativeSizeAxes = Axes.Both;
            Masking = true;

            InternalChildren = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = RetroPalette.Void,
                },
                artLayers = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                },
                // Knocks the art back before it's graded. Without this the
                // wash sits on a bright cover and the whole screen turns to
                // fog — every panel above it needs somewhere dark to land.
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(0f, 0f, 0f, 0.46f),
                },
                // Sunset wash. Sits over the art rather than under it so a
                // bright cover still reads as "this screen's palette".
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Alpha = 0.34f,
                    Colour = ColourInfo.GradientVertical(
                        RetroPalette.GradeTop,
                        RetroPalette.GradeBottom),
                },
                // Darkens the left third, where all the text lives.
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = ColourInfo.GradientHorizontal(
                        new Color4(0f, 0f, 0f, 0.70f),
                        new Color4(0f, 0f, 0f, 0f)),
                },
                new ScanlineOverlay(),
            };
        }

        /// <summary>
        /// Shows a new background, crossfading from whatever is showing.
        /// A null or missing path fades to the bare gradient.
        /// </summary>
        public void SetBackground(string? path)
        {
            if (path == currentPath)
                return;

            currentPath = path;

            var incoming = new BufferedContainer(cachedFrameBuffer: true)
            {
                RelativeSizeAxes = Axes.Both,
                BlurSigma = new Vector2(blur_sigma),
                DrawOriginal = false,
                Alpha = 0,
                Child = new BeatmapBackground(path)
                {
                    RelativeSizeAxes = Axes.Both,
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Scale = new Vector2(art_overscan),
                    FillMode = FillMode.Fill,
                },
            };

            pendingLayer = incoming;

            LoadComponentAsync(incoming, loaded =>
            {
                // A second selection change can land while this one is still
                // loading; only the newest layer should be shown.
                if (pendingLayer != loaded)
                {
                    loaded.Dispose();
                    return;
                }

                artLayers.Add(loaded);
                loaded.FadeIn(320, Easing.OutQuint);

                // Only the layer that actually reached the screen may be
                // faded out. Fading out "whatever was requested before this
                // one" crashes: turning the wheel quickly supersedes a layer
                // while it is still loading, and a Loading drawable can't have
                // its transforms mutated from the update thread.
                displayedLayer?.FadeOut(320, Easing.OutQuint).Expire();

                displayedLayer = loaded;
                pendingLayer = null;
            });
        }
    }
}
