using System;
using System.Collections.Generic;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Lines;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Graphics
{
    /// <summary>
    /// Draws one of the backend's per-frame curves — spectral flux, its
    /// threshold, the onset envelope, a frequency band — over a time window.
    ///
    /// Every graph in the generation reveal and the analyser is one of these,
    /// which is why it lives in <c>Graphics/</c> rather than beside either
    /// screen.
    ///
    /// <para>
    /// <b>It reduces for display, never in the file.</b> A four-minute track
    /// is ~11,000 samples and a panel is maybe 1,200 pixels wide, so the curve
    /// is walked one pixel column at a time and each column keeps the minimum
    /// and maximum it spans. That is the standard waveform draw, and it is the
    /// reason <c>dsp.json</c> could afford to ship full resolution: a spike one
    /// sample wide still reaches full height at any zoom, where averaging into
    /// 2,048 stored columns would have lost it permanently — and would have put
    /// every onset marker beside its own flux peak.
    /// </para>
    ///
    /// <para>
    /// <b>Rendering is one zig-zag path.</b> Each column contributes two
    /// vertices, its top and its bottom, and the polyline alternates direction
    /// so it never backtracks. A smooth curve has top ≈ bottom and reads as a
    /// line; a spiky one fills the band between them. <see cref="TraceStyle.Fill"/>
    /// simply pins every column's bottom to the baseline, so the same single
    /// path renders as a filled area — no second code path, and no polygon
    /// primitive, which osu.Framework does not have.
    /// </para>
    ///
    /// <para>
    /// <b>The value axis can be curved, and usually must be.</b> Measured on a
    /// real track, spectral flux has a median of 0.0001 and a maximum of 1:
    /// drawn linearly, the body of the curve is a flat line along the bottom
    /// and the adaptive threshold over it sits at 3% of the height, which is
    /// invisible. <see cref="ValueCurve"/> is the same perceptual exponent
    /// <c>SpectrumAnalyser</c> applies to the menu's FFT ring, for the same
    /// reason level meters are drawn in dB rather than linearly.
    /// </para>
    ///
    /// <para>
    /// It is a display transform, not a lie, and one property is what makes
    /// that true: the curve is monotonic, so applying the same exponent to a
    /// signal and to its threshold leaves every crossing exactly where it was.
    /// A trace drawn this way still says "detected here" in precisely the
    /// frames the detector did. Two graphs compared against each other must
    /// therefore share a <see cref="ValueCurve"/>, and there is a test that
    /// crossings survive.
    /// </para>
    ///
    /// <para>
    /// Vertices are rebuilt when the data, the window or the size changes —
    /// <b>never per frame</b>. A 2,000-vertex path rebuilt every frame is the
    /// one way this becomes a performance problem, which is also why
    /// <see cref="Progress"/> drives a masking container rather than the vertex
    /// list: the reveal is a wipe over a finished path, exactly as
    /// <c>SpectrogramReveal</c> uncovers its image.
    /// </para>
    /// </summary>
    public partial class TraceGraph : CompositeDrawable
    {
        /// <summary>How a curve is drawn between its top and bottom envelope.</summary>
        public enum TraceStyle
        {
            /// <summary>The band the samples actually span. A smooth curve reads as a line.</summary>
            Line,

            /// <summary>Every column pinned to the baseline, so the band becomes a filled area.</summary>
            Fill,
        }

        /// <summary>
        /// Hard cap on columns, whatever the window is.
        ///
        /// One column per pixel is already the most a display can show; this
        /// only bounds the path on an absurdly wide window, since vertex count
        /// is what makes <see cref="SmoothPath"/> expensive.
        /// </summary>
        public const int MaxColumns = 2000;

        private readonly Container revealMask;
        private readonly SmoothPath path;

        private float[] samples = Array.Empty<float>();
        private double frameRate = 1;
        private double windowStart;
        private double windowEnd = 1;
        private float valueRange = 1;
        private float valueCurve = 1;
        private float progress = 1;
        private float thickness = 2;
        private TraceStyle style = TraceStyle.Line;
        private float columnWidth = 1;

        private Vector2 builtSize;
        private bool dirty = true;

        public TraceGraph()
        {
            InternalChild = revealMask = new Container
            {
                // Full height, width driven by Progress in Update. The path
                // inside is laid out in absolute pixels against this
                // component's *full* width, so a wipe uncovers it rather than
                // squashing it into a sliver — the same arrangement
                // SpectrogramReveal uses for its image.
                RelativeSizeAxes = Axes.Y,
                Masking = true,
                Width = 0,
                Child = path = new SmoothPath
                {
                    PathRadius = 1,
                    Colour = Color4.White,
                },
            };
        }

        /// <summary>The curve to draw, and the frame rate its samples are spaced at.</summary>
        public void SetCurve(IReadOnlyList<float>? curve, double samplesPerSecond)
        {
            samples = curve as float[] ?? (curve == null ? Array.Empty<float>() : toArray(curve));
            frameRate = samplesPerSecond > 0 ? samplesPerSecond : 1;
            dirty = true;
        }

        /// <summary>The span of the track on screen, in seconds.</summary>
        public void SetWindow(double startSeconds, double endSeconds)
        {
            windowStart = startSeconds;
            windowEnd = Math.Max(startSeconds + 1e-6, endSeconds);
            dirty = true;
        }

        /// <summary>The whole curve, end to end.</summary>
        public void ShowWholeCurve() => SetWindow(0, samples.Length / frameRate);

        /// <summary>
        /// Sets <see cref="ValueRange"/> to this curve's own maximum, so it
        /// fills the panel.
        ///
        /// Right for a curve shown on its own — the local median peaks around
        /// 0.005 on a real track and is a flat line at the bottom of a 0..1
        /// axis. <b>Wrong for curves drawn over each other</b>: a flux trace
        /// and its threshold must share a range and a
        /// <see cref="ValueCurve"/>, or they will appear to cross in places
        /// the detector never did.
        /// </summary>
        public void FitValueRange()
        {
            float max = 0;

            foreach (float value in samples)
            {
                if (value > max)
                    max = value;
            }

            ValueRange = max > 0 ? max : 1;
        }

        /// <summary>Value drawn at full height. Samples above it are clipped to the top.</summary>
        public float ValueRange
        {
            get => valueRange;
            set
            {
                if (valueRange == value)
                    return;

                valueRange = Math.Max(1e-6f, value);
                dirty = true;
            }
        }

        /// <summary>
        /// Exponent applied to every value before it is placed on the axis:
        /// 1 is linear, below 1 stretches quiet detail and compresses loud.
        ///
        /// Monotonic, so a signal and its threshold drawn at the same exponent
        /// still cross in the same frames — see the class remarks.
        /// </summary>
        public float ValueCurve
        {
            get => valueCurve;
            set
            {
                if (valueCurve == value)
                    return;

                valueCurve = Math.Clamp(value, 0.05f, 4f);
                dirty = true;
            }
        }

        /// <summary>Stroke width in pixels.</summary>
        public float Thickness
        {
            get => thickness;
            set
            {
                if (thickness == value)
                    return;

                thickness = Math.Max(0.5f, value);
                path.PathRadius = thickness / 2;
            }
        }

        public TraceStyle Style
        {
            get => style;
            set
            {
                if (style == value)
                    return;

                style = value;
                dirty = true;
            }
        }

        /// <summary>
        /// Pixels per column. One is a column per pixel — the most a display
        /// can show; two halves the vertex count for a trace drawn small.
        /// </summary>
        public float ColumnWidth
        {
            get => columnWidth;
            set
            {
                if (columnWidth == value)
                    return;

                columnWidth = Math.Clamp(value, 1, 16);
                dirty = true;
            }
        }

        /// <summary>The trace's colour. Shorthand for <see cref="Drawable.Colour"/> on the path itself.</summary>
        public Color4 TraceColour
        {
            get => path.Colour;
            set => path.Colour = value;
        }

        /// <summary>
        /// How much of the trace is uncovered, 0 to 1 — the draw-in for the
        /// reveal. A wipe over a finished path, not a growing one.
        /// </summary>
        public float Progress
        {
            get => progress;
            set => progress = Math.Clamp(value, 0, 1);
        }

        /// <summary>How many columns the last rebuild produced. Exposed for tests.</summary>
        public int ColumnCount { get; private set; }

        /// <summary>Where a moment in the track sits, in local pixels.</summary>
        public float TimeToX(double seconds) =>
            (float)((seconds - windowStart) / (windowEnd - windowStart)) * DrawWidth;

        /// <summary>The moment a local pixel position falls on.</summary>
        public double XToTime(float x) =>
            windowStart + x / Math.Max(1, DrawWidth) * (windowEnd - windowStart);

        protected override void Update()
        {
            base.Update();

            if (DrawSize != builtSize)
            {
                builtSize = DrawSize;
                dirty = true;
            }

            if (dirty)
                rebuild();

            revealMask.Width = DrawWidth * progress;
        }

        /// <summary>
        /// Reduces a span of samples to one (minimum, maximum) pair per
        /// column.
        ///
        /// Pure and public so the property the whole approach rests on — that
        /// a one-sample spike survives any reduction ratio — can be tested
        /// without a window open.
        ///
        /// <paramref name="first"/> and <paramref name="last"/> are sample
        /// indices and may sit outside the curve: a window scrolled past
        /// either end yields empty columns there rather than clamping, which
        /// would smear the first or last value across the margin.
        /// </summary>
        public static (float Min, float Max)[] Reduce(IReadOnlyList<float> samples,
                                                      double first, double last, int columns)
        {
            if (columns <= 0)
                return Array.Empty<(float, float)>();

            var reduced = new (float Min, float Max)[columns];
            double perColumn = (last - first) / columns;

            for (int column = 0; column < columns; column++)
            {
                double from = first + column * perColumn;
                double to = from + perColumn;

                int start = (int)Math.Floor(from);
                int end = (int)Math.Ceiling(to);

                // At least one sample per column, so a window zoomed in past
                // one sample per pixel still draws the sample it is over
                // rather than nothing.
                if (end <= start)
                    end = start + 1;

                float min = float.MaxValue;
                float max = float.MinValue;

                for (int i = start; i < end; i++)
                {
                    if (i < 0 || i >= samples.Count)
                        continue;

                    float value = samples[i];

                    if (value < min)
                        min = value;

                    if (value > max)
                        max = value;
                }

                reduced[column] = min > max ? (0f, 0f) : (min, max);
            }

            return reduced;
        }

        private void rebuild()
        {
            dirty = false;

            float width = DrawWidth;
            float height = DrawHeight;

            if (width <= 0 || height <= 0 || samples.Length == 0)
            {
                path.Vertices = Array.Empty<Vector2>();
                ColumnCount = 0;
                return;
            }

            int columns = Math.Clamp((int)(width / columnWidth), 1, MaxColumns);
            ColumnCount = columns;

            var reduced = Reduce(samples, windowStart * frameRate, windowEnd * frameRate, columns);

            var vertices = new List<Vector2>(columns * 2);
            float step = width / columns;

            for (int column = 0; column < columns; column++)
            {
                float x = column * step;

                float top = toY(reduced[column].Max, height);
                float bottom = style == TraceStyle.Fill ? height : toY(reduced[column].Min, height);

                // Alternate the order so the path never doubles back on
                // itself between columns: down, then up, then down again.
                if (column % 2 == 0)
                {
                    vertices.Add(new Vector2(x, bottom));
                    vertices.Add(new Vector2(x, top));
                }
                else
                {
                    vertices.Add(new Vector2(x, top));
                    vertices.Add(new Vector2(x, bottom));
                }
            }

            path.Vertices = vertices;

            // Path sizes itself around its vertices, so it lands offset by
            // wherever its own bounding box starts unless it is shifted back.
            // Same correction DrawableSlider applies to its body.
            path.Position = -path.PositionInBoundingBox(Vector2.Zero);
        }

        /// <summary>
        /// A value's height on the axis, 0 at the bottom and 1 at the top,
        /// after clamping and the perceptual curve. Pure and public so the
        /// crossing-preservation property can be tested without a window.
        /// </summary>
        public static float Normalise(float value, float range, float curve)
        {
            float fraction = Math.Clamp(value / Math.Max(1e-6f, range), 0, 1);

            return curve == 1 ? fraction : MathF.Pow(fraction, curve);
        }

        private float toY(float value, float height) =>
            height - Normalise(value, valueRange, valueCurve) * height;

        private static float[] toArray(IReadOnlyList<float> values)
        {
            var copy = new float[values.Count];

            for (int i = 0; i < copy.Length; i++)
                copy[i] = values[i];

            return copy;
        }
    }
}
