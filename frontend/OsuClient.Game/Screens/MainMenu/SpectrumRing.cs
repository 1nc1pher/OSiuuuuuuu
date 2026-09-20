using System;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osuTK;
using osuTK.Graphics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OsuClient.Game.Screens.MainMenu
{
    /// <summary>
    /// The live spectrum of the playing song, drawn as a ring of tiny square
    /// blocks around the logo (MENU_REDESIGN_PLAN.md step 6b).
    ///
    /// Each of the 128 columns is a single masked sprite over one baked
    /// block-stack texture, not a stack of block drawables: setting a
    /// container's height to a whole number of cells reveals whole blocks and
    /// clips the rest, which gives the LED-meter look for one drawable and one
    /// height assignment per column per frame. A drawable per block would be
    /// 1,280 of them, all mutated every frame.
    ///
    /// The texture is baked the same way <see cref="Graphics.ScanlineOverlay"/>
    /// bakes its lines, and rebuilt only when the cell size actually changes,
    /// so the blocks stay pixel-crisp as the window resizes rather than being
    /// a stretched, blurred copy of one fixed bake.
    /// </summary>
    public partial class SpectrumRing : CompositeDrawable
    {
        private const int columns = 144;

        /// <summary>
        /// Distinct frequency bands. Half the column count, because the ring
        /// is mirrored: the spectrum runs from bass at the top down to treble
        /// at the bottom on both sides at once.
        ///
        /// Mirroring is why the ring reads as a shape rather than as a graph
        /// that happens to be bent into a circle — an unmirrored ring has a
        /// seam where its loud end meets its quiet one.
        /// </summary>
        private const int bands = columns / 2;

        /// <summary>Blocks in a full-height column.</summary>
        private const int blocks = 10;

        /// <summary>
        /// Where a column starts, as a fraction of this drawable's size. Just
        /// outside the logo's outline, which reaches 0.36.
        /// </summary>
        private const float base_radius = 0.385f;

        /// <summary>
        /// How far a full-height column reaches, in the same units.
        ///
        /// Not picked freely: it is what makes the blocks square. A block is
        /// as wide as its share of the ring's circumference, so its height —
        /// this, divided by <see cref="blocks"/> and multiplied by
        /// <see cref="cell_duty"/> — has to match. Too small and they read as
        /// dashes rather than the square pixels this whole screen is built
        /// from. Both sides of that equation scale with the ring, so the
        /// blocks stay square at any window size.
        /// </summary>
        private const float max_length = 0.145f;

        /// <summary>
        /// Block width as a fraction of the gap between column centres.
        /// Under 1 so there's air between columns — a solid ring of touching
        /// blocks reads as a disc with a jagged edge, not as a meter.
        /// </summary>
        private const float block_duty = 0.62f;

        /// <summary>Lit fraction of each cell, the rest being the gap above it.</summary>
        private const float cell_duty = 0.72f;

        /// <summary>
        /// How much of the hue wheel the ring spans. A full 360 would put red
        /// next to red where the ring closes; a bit over half keeps it a
        /// gradient with two ends.
        /// </summary>
        private const float hue_span = 220;

        private readonly Container[] columnContainers = new Container[columns];
        private readonly Sprite[] columnSprites = new Sprite[columns];
        private readonly SpectrumAnalyser analyser = new SpectrumAnalyser(bands);

        /// <summary>
        /// Scratch buffer the live amplitudes are copied into.
        ///
        /// The array a track hands out is mutated in place on the audio
        /// thread, so it is copied once, immediately, rather than read column
        /// by column across a frame.
        /// </summary>
        private readonly float[] binBuffer = new float[256];

        [Resolved]
        private IRenderer renderer { get; set; } = null!;

        /// <summary>
        /// Where the amplitudes come from. Left null, the ring reads the
        /// menu's own track; a test scene substitutes a fixed spectrum so a
        /// single captured frame shows something predictable instead of
        /// whatever the song happened to be doing.
        /// </summary>
        public Func<float[]?>? AmplitudeSource;

        private MenuTrack? track;

        private int builtCellPixels;
        private float builtSize;

        public SpectrumRing()
        {
            for (int i = 0; i < columns; i++)
            {
                float angle = i / (float)columns * MathF.PI * 2;

                // Same placement the turntable's strobe ring uses: a unit
                // vector rotated off twelve o'clock, scaled by a radius given
                // as a fraction of the parent.
                var column = new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.BottomCentre,
                    RelativePositionAxes = Axes.Both,
                    Position = new Vector2(MathF.Sin(angle), -MathF.Cos(angle)) * base_radius,
                    Rotation = angle * 180 / MathF.PI,
                    Masking = true,
                    Child = columnSprites[i] = new Sprite
                    {
                        Anchor = Anchor.BottomCentre,
                        Origin = Anchor.BottomCentre,
                        FillMode = FillMode.Stretch,
                    },
                    Colour = Color4Extensions.FromHSV(bandOf(i) / (float)bands * hue_span, 0.62f, 1f),
                };

                columnContainers[i] = column;
                AddInternal(column);
            }
        }

        /// <summary>
        /// Current low-end energy, 0–1 — see
        /// <see cref="SpectrumAnalyser.LowEnergy"/>. The menu's logo uses it
        /// to pulse harder in loud passages than in quiet ones.
        /// </summary>
        public float LowEnergy => analyser.LowEnergy;

        /// <summary>
        /// The band a column shows. Columns run clockwise from the top; the
        /// second half walks back down the same bands, so the two sides
        /// mirror each other.
        /// </summary>
        private static int bandOf(int column) => column < bands ? column : columns - 1 - column;

        /// <summary>The track to visualise. Null leaves the ring dark.</summary>
        public void SetTrack(MenuTrack? menuTrack) => track = menuTrack;

        protected override void Update()
        {
            base.Update();

            float size = MathF.Min(DrawWidth, DrawHeight);

            if (size <= 0)
                return;

            rebuildIfResized(size);
            updateHeights();
        }

        /// <summary>
        /// Rebakes the block texture and re-measures the columns, but only
        /// when the size actually changed — this runs every frame and the
        /// bake allocates an image and a texture.
        /// </summary>
        private void rebuildIfResized(float size)
        {
            if (Math.Abs(size - builtSize) < 0.5f)
                return;

            builtSize = size;

            float maxLengthPixels = size * max_length;
            int cellPixels = Math.Max(2, (int)MathF.Round(maxLengthPixels / blocks));

            if (cellPixels != builtCellPixels)
            {
                builtCellPixels = cellPixels;
                bakeBlockTexture(cellPixels);
            }

            // Column centres are spaced along the circle at the base radius;
            // the block is a fraction of that spacing, leaving a gap.
            float spacing = MathF.PI * 2 * (size * base_radius) / columns;
            float width = MathF.Max(1, MathF.Round(spacing * block_duty));
            float height = cellPixels * blocks;

            foreach (var sprite in columnSprites)
                sprite.Size = new Vector2(width, height);

            foreach (var column in columnContainers)
                column.Width = width;
        }

        /// <summary>
        /// One column of cells: a lit block at the bottom of each cell, a gap
        /// above it. 1px wide and stretched across the column, so only the
        /// vertical edges — the ones that make it read as blocks — have to be
        /// pixel-exact.
        /// </summary>
        private void bakeBlockTexture(int cellPixels)
        {
            int litPixels = Math.Max(1, (int)MathF.Round(cellPixels * cell_duty));
            int height = cellPixels * blocks;

            using var image = new Image<Rgba32>(1, height);

            for (int y = 0; y < height; y++)
            {
                // Measured from the bottom, so the lowest row of the column is
                // always lit: a column's first block should sit exactly on the
                // ring's base radius rather than a gap's width above it.
                int withinCell = (height - 1 - y) % cellPixels;

                image[0, y] = withinCell < litPixels
                    ? new Rgba32(255, 255, 255, 255)
                    : new Rgba32(0, 0, 0, 0);
            }

            var texture = renderer.CreateTexture(1, height);
            texture.SetData(new TextureUpload(image.Clone()));

            // Every column shares one texture, so the outgoing one is
            // disposed once after they have all been repointed — disposing it
            // inside the loop would free it under the columns still using it,
            // and then free it again for each of them.
            var previous = columnSprites[0].Texture;

            foreach (var sprite in columnSprites)
                sprite.Texture = texture;

            previous?.Dispose();
        }

        private void updateHeights()
        {
            var bins = readAmplitudes();

            if (bins == null)
                analyser.Reset();
            else
                analyser.Update(bins, Time.Elapsed);

            var values = analyser.Values;
            float cell = builtCellPixels;

            for (int i = 0; i < columns; i++)
            {
                // Rounded to whole cells: a fractional height would clip a
                // block in half, and half a block is what makes an LED meter
                // look like a bar chart instead.
                int lit = (int)MathF.Round(values[bandOf(i)] * blocks);

                columnContainers[i].Height = lit * cell;

                // Hidden outright at zero rather than left at zero height: a
                // masked container still feathers its edge by a pixel, which
                // showed up as a faint dotted ring around the logo whenever
                // nothing was playing.
                columnContainers[i].Alpha = lit > 0 ? 1 : 0;
            }
        }

        /// <summary>
        /// This frame's amplitudes, copied into <see cref="binBuffer"/>, or
        /// null when there's nothing playing.
        /// </summary>
        private float[]? readAmplitudes()
        {
            if (AmplitudeSource != null)
                return AmplitudeSource();

            if (track?.Track == null || !track.IsPlaying)
                return null;

            var live = track.Track.CurrentAmplitudes.FrequencyAmplitudes;

            // Sliced as a span, which doesn't allocate — this runs every
            // frame, and the source is a ReadOnlyMemory the audio thread
            // keeps writing to, so it is copied out in one go.
            live.Span[..Math.Min(binBuffer.Length, live.Length)].CopyTo(binBuffer);

            return binBuffer;
        }
    }
}
