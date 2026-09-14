using System;
using System.IO;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osuTK;
using osuTK.Graphics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OsuClient.Game.Screens.Generation
{
    /// <summary>
    /// Stage 1 of the DSP reveal (FRONTEND_PLAN.md Phase 7): the backend's
    /// <c>spectrogram.png</c> wiping in from left to right, a scan line riding
    /// the leading edge.
    ///
    /// A wipe rather than a fade, and the reason is the whole point of the
    /// stage: a fade says "here is a picture of the song", a wipe says "the
    /// computer is reading through the song" — which is what the three stages
    /// after it then act on. It's the same reveal-by-progress idea
    /// <see cref="Beatmaps.HitObjects.SliderPath.Segment"/> uses to snake a
    /// slider in, done with a masking container whose width is the progress.
    ///
    /// This component owns the shared time axis: every later stage places
    /// itself with <see cref="TimeToX"/>, so an onset spark, a beat line and a
    /// hit object all line up over the same moment of the song.
    /// </summary>
    public partial class SpectrogramReveal : CompositeDrawable
    {
        private readonly string? imagePath;

        [Resolved]
        private IRenderer renderer { get; set; } = null!;

        private Container revealMask = null!;
        private Sprite spectrogram = null!;
        private Box placeholder = null!;
        private Box scanLine = null!;

        private double revealDuration;
        private double? revealStartTime;
        private float progress;

        /// <summary>The track's length in seconds — what the horizontal axis spans.</summary>
        public double TrackDuration { get; }

        /// <summary>Whether the backend's image was found and loaded.</summary>
        public bool HasImage { get; private set; }

        /// <summary>How far the wipe has got, 0..1. Exposed for tests.</summary>
        public float Progress => progress;

        /// <summary>Whether the wipe has finished (or was skipped to the end).</summary>
        public bool Revealed => progress >= 1;

        public SpectrogramReveal(string? imagePath, double trackDuration)
        {
            this.imagePath = imagePath;
            TrackDuration = Math.Max(0.001, trackDuration);

            Masking = true;
            CornerRadius = 8;

            // Built here rather than in the dependency loader because
            // AddOverlay is called before this is added to the hierarchy:
            // assigning InternalChildren at load time would replace the
            // overlays that were already added and they would never appear.
            InternalChildren = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(0.03f, 0.03f, 0.06f, 1f),
                },
                revealMask = new Container
                {
                    // Full height, width driven by progress in Update. The
                    // children inside are sized in absolute pixels to this
                    // component's full width, so the image is *uncovered* by
                    // the growing mask rather than stretched out of a sliver.
                    RelativeSizeAxes = Axes.Y,
                    Masking = true,
                    Width = 0,
                    Children = new Drawable[]
                    {
                        placeholder = new Box
                        {
                            RelativeSizeAxes = Axes.Y,
                            Colour = new Color4(0.10f, 0.09f, 0.16f, 1f),
                        },
                        spectrogram = new Sprite
                        {
                            RelativeSizeAxes = Axes.Y,
                            FillMode = FillMode.Stretch,
                            Alpha = 0,
                        },
                    },
                },
                scanLine = new Box
                {
                    RelativeSizeAxes = Axes.Y,
                    Width = 2,
                    Colour = new Color4(0.75f, 0.95f, 1f, 1f),
                    Alpha = 0,
                    EdgeSmoothness = new Vector2(1),
                },
            };
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            // Needs the renderer, which is only resolved by this point.
            loadImage();
        }

        private void loadImage()
        {
            if (imagePath == null || !File.Exists(imagePath))
                return;

            try
            {
                // Straight through ImageSharp, the way
                // Graphics/BeatmapBackground.cs already loads a beatmap's
                // background: the file sits in the beatmap's own folder, not
                // in the game's resources.
                // Deliberately not disposed here, exactly as
                // Graphics/BeatmapBackground.cs leaves it: TextureUpload takes
                // ownership and the upload is consumed later on the draw
                // thread, so disposing it at the end of this method pulls the
                // pixels out from under it and the sprite draws plain white.
                var image = SixLabors.ImageSharp.Image.Load<Rgba32>(imagePath);

                var texture = renderer.CreateTexture(image.Width, image.Height);
                texture.SetData(new TextureUpload(image));

                spectrogram.Texture = texture;
                spectrogram.Alpha = 1;
                placeholder.Alpha = 0;
                HasImage = true;
            }
            catch (Exception e) when (e is UnknownImageFormatException or IOException or InvalidOperationException)
            {
                // A missing or half-written image costs this stage its
                // picture, not the sequence: the flat panel still wipes in,
                // and the onset and beat stages still have their own data to
                // draw over it.
            }
        }

        /// <summary>
        /// Adds a layer drawn over the image — the onset sparks and the beat
        /// grid. They live inside this component rather than beside it so they
        /// share its exact bounds, which is what makes
        /// <see cref="TimeToX"/> mean the same thing for all three.
        /// </summary>
        public void AddOverlay(Drawable overlay) => AddInternal(overlay);

        /// <summary>
        /// Where a moment in the track sits horizontally, in local pixels. The
        /// image spans the whole track across the full width, which makes this
        /// the shared time axis for every stage drawn over it.
        /// </summary>
        public float TimeToX(double seconds) =>
            (float)(Math.Clamp(seconds / TrackDuration, 0, 1) * DrawWidth);

        /// <summary>
        /// Starts the wipe, taking <paramref name="duration"/> milliseconds.
        ///
        /// Advanced linearly from the clock rather than eased: this reads as a
        /// playhead moving through the song at a constant rate, and any easing
        /// makes it look like the analysis sped up or stalled.
        /// </summary>
        public void Reveal(double duration)
        {
            revealDuration = Math.Max(1, duration);
            revealStartTime = Clock.CurrentTime;

            scanLine.Alpha = 1;
        }

        /// <summary>Jumps to fully revealed, for a skip.</summary>
        public void RevealImmediately()
        {
            revealStartTime = null;
            progress = 1;
            scanLine.Alpha = 0;
        }

        protected override void Update()
        {
            base.Update();

            if (revealStartTime != null)
            {
                double elapsed = Clock.CurrentTime - revealStartTime.Value;

                progress = (float)Math.Clamp(elapsed / revealDuration, 0, 1);

                if (progress >= 1)
                {
                    revealStartTime = null;
                    scanLine.FadeOut(220, Easing.OutQuint);
                }
            }

            // Re-derived every frame rather than captured when the animation
            // started: DrawWidth only settles after layout, and changes again
            // whenever the window is resized mid-sequence.
            float width = DrawWidth;

            revealMask.Width = progress * width;
            spectrogram.Width = width;
            placeholder.Width = width;
            scanLine.X = progress * width;
        }
    }
}
