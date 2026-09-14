using System;
using System.Collections.Generic;
using osuTK;

namespace OsuClient.Game.Beatmaps.HitObjects
{
    /// <summary>
    /// A slider's curve, sampled into a polyline and trimmed to the slider's
    /// declared pixel length.
    ///
    /// The .osu format stores control points and a length, not the curve
    /// itself: the curve is whatever the curve type says those anchors mean,
    /// and it is then cut (or extended) so it measures exactly
    /// <see cref="SliderData.PixelLength"/>. That trimming matters — the
    /// length is what osu! derives the slider's duration from, so a body drawn
    /// at the raw control-point length would not match the time the slider
    /// actually takes.
    ///
    /// Pure geometry, no osu.Framework dependency beyond <see cref="Vector2"/>.
    /// </summary>
    public sealed class SliderPath
    {
        /// <summary>Below this the three points of a perfect circle are treated as collinear.</summary>
        private const double collinear_epsilon = 0.001;

        /// <summary>Target spacing, in osu!pixels, between sampled points on a curve.</summary>
        private const double sample_spacing = 4;

        private const int min_samples = 4;
        private const int max_samples = 400;

        private readonly List<Vector2> points;

        /// <summary>Distance along the path at each point in <see cref="Points"/>.</summary>
        private readonly List<double> cumulative;

        private SliderPath(List<Vector2> points)
        {
            this.points = points;

            cumulative = new List<double>(points.Count) { 0 };

            for (int i = 1; i < points.Count; i++)
                cumulative.Add(cumulative[i - 1] + Vector2.Distance(points[i - 1], points[i]));
        }

        /// <summary>The sampled polyline, in osu!pixels, starting at the slider head.</summary>
        public IReadOnlyList<Vector2> Points => points;

        /// <summary>Total length of the trimmed path, in osu!pixels.</summary>
        public double Length => cumulative[cumulative.Count - 1];

        /// <summary>Position at a fraction along the path, where 0 is the head and 1 the tail.</summary>
        public Vector2 PositionAt(double progress)
        {
            if (points.Count == 1)
                return points[0];

            double target = Math.Clamp(progress, 0, 1) * Length;

            // Points are dense (one every few osu!pixels), so a linear walk is
            // cheap and avoids the off-by-one traps of a binary search.
            for (int i = 1; i < points.Count; i++)
            {
                if (cumulative[i] < target)
                    continue;

                double segment = cumulative[i] - cumulative[i - 1];

                if (segment <= 0)
                    return points[i];

                float t = (float)((target - cumulative[i - 1]) / segment);

                return Vector2.Lerp(points[i - 1], points[i], t);
            }

            return points[points.Count - 1];
        }

        /// <summary>
        /// The leading portion of the path up to <paramref name="progress"/>,
        /// as its own polyline — what a slider body snaking in actually draws.
        /// Always at least two points, even at progress 0, so it can still be
        /// handed to a path drawable.
        /// </summary>
        public IReadOnlyList<Vector2> Segment(double progress)
        {
            progress = Math.Clamp(progress, 0, 1);
            double target = progress * Length;

            var result = new List<Vector2> { points[0] };

            for (int i = 1; i < points.Count; i++)
            {
                if (cumulative[i] < target)
                {
                    result.Add(points[i]);
                    continue;
                }

                double segment = cumulative[i] - cumulative[i - 1];

                if (segment > 0)
                {
                    float t = (float)((target - cumulative[i - 1]) / segment);
                    result.Add(Vector2.Lerp(points[i - 1], points[i], t));
                }

                break;
            }

            if (result.Count == 1)
                result.Add(result[0] + new Vector2(0.01f, 0));

            return result;
        }

        /// <summary>
        /// Unit direction of travel at a fraction along the path — the way a
        /// ball moving from 0 to 1 would be heading at that point.
        /// </summary>
        public Vector2 DirectionAt(double progress)
        {
            const double epsilon = 0.001;

            Vector2 behind = PositionAt(progress - epsilon);
            Vector2 ahead = PositionAt(progress + epsilon);
            Vector2 direction = ahead - behind;

            return direction.LengthSquared > 0 ? Vector2.Normalize(direction) : new Vector2(1, 0);
        }

        public static SliderPath FromSlider(SliderData slider)
        {
            var anchors = slider.Path;

            if (anchors.Count == 0)
                return new SliderPath(new List<Vector2> { slider.Position });

            if (anchors.Count == 1 || slider.PixelLength <= 0)
                return new SliderPath(new List<Vector2> { anchors[0] });

            var sampled = sample(slider.CurveType, anchors);

            return new SliderPath(trimToLength(sampled, slider.PixelLength));
        }

        // ------------------------------------------------------------------

        private static List<Vector2> sample(SliderCurveType type, IReadOnlyList<Vector2> anchors)
        {
            switch (type)
            {
                case SliderCurveType.Linear:
                    return new List<Vector2>(anchors);

                case SliderCurveType.PerfectCircle:
                    // Only defined for exactly three points; osu! falls back to
                    // a bezier for anything else.
                    return anchors.Count == 3
                        ? sampleCircularArc(anchors[0], anchors[1], anchors[2])
                        : sampleBezierSegments(anchors);

                case SliderCurveType.Catmull:
                    return sampleCatmull(anchors);

                default:
                    return sampleBezierSegments(anchors);
            }
        }

        /// <summary>
        /// A bezier "curve" in the .osu format is really a chain of beziers:
        /// a repeated control point marks the end of one segment and the start
        /// of the next, which is how mappers put a hard corner in a slider.
        /// </summary>
        private static List<Vector2> sampleBezierSegments(IReadOnlyList<Vector2> anchors)
        {
            var result = new List<Vector2> { anchors[0] };
            var segment = new List<Vector2> { anchors[0] };

            for (int i = 1; i < anchors.Count; i++)
            {
                bool repeated = anchors[i] == anchors[i - 1];

                if (repeated)
                {
                    appendBezier(result, segment);
                    segment = new List<Vector2> { anchors[i] };
                    continue;
                }

                segment.Add(anchors[i]);
            }

            appendBezier(result, segment);

            return result;
        }

        private static void appendBezier(List<Vector2> result, List<Vector2> controlPoints)
        {
            if (controlPoints.Count < 2)
                return;

            int steps = sampleCount(polylineLength(controlPoints));

            for (int i = 1; i <= steps; i++)
                result.Add(bezierAt(controlPoints, (double)i / steps));
        }

        private static Vector2 bezierAt(List<Vector2> controlPoints, double t)
        {
            // de Casteljau: repeatedly collapse the control polygon.
            Span<Vector2> working = stackalloc Vector2[controlPoints.Count];

            for (int i = 0; i < controlPoints.Count; i++)
                working[i] = controlPoints[i];

            for (int level = controlPoints.Count - 1; level > 0; level--)
            {
                for (int i = 0; i < level; i++)
                    working[i] = Vector2.Lerp(working[i], working[i + 1], (float)t);
            }

            return working[0];
        }

        private static List<Vector2> sampleCircularArc(Vector2 a, Vector2 b, Vector2 c)
        {
            // Circumcentre of the triangle abc; if the points are collinear
            // there's no circle and the slider is just a line.
            double aSq = b.LengthSquared - c.LengthSquared;
            double bSq = a.LengthSquared - c.LengthSquared;

            double det = 2 * ((b.X - c.X) * (a.Y - c.Y) - (a.X - c.X) * (b.Y - c.Y));

            if (Math.Abs(det) < collinear_epsilon)
                return new List<Vector2> { a, b, c };

            double centreX = (aSq * (a.Y - c.Y) - bSq * (b.Y - c.Y)) / det;
            double centreY = (bSq * (b.X - c.X) - aSq * (a.X - c.X)) / det;

            var centre = new Vector2((float)centreX, (float)centreY);
            double radius = Vector2.Distance(centre, a);

            double startAngle = Math.Atan2(a.Y - centre.Y, a.X - centre.X);
            double midAngle = Math.Atan2(b.Y - centre.Y, b.X - centre.X);
            double endAngle = Math.Atan2(c.Y - centre.Y, c.X - centre.X);

            // Walk from start to end the way round that actually passes through
            // the middle control point.
            while (midAngle < startAngle) midAngle += 2 * Math.PI;
            while (endAngle < startAngle) endAngle += 2 * Math.PI;
            if (midAngle > endAngle) endAngle -= 2 * Math.PI;

            int steps = sampleCount(Math.Abs(endAngle - startAngle) * radius);

            var result = new List<Vector2>(steps + 1);

            for (int i = 0; i <= steps; i++)
            {
                double angle = startAngle + (endAngle - startAngle) * i / steps;

                result.Add(new Vector2(
                    (float)(centre.X + Math.Cos(angle) * radius),
                    (float)(centre.Y + Math.Sin(angle) * radius)));
            }

            return result;
        }

        private static List<Vector2> sampleCatmull(IReadOnlyList<Vector2> anchors)
        {
            var result = new List<Vector2> { anchors[0] };

            for (int i = 0; i < anchors.Count - 1; i++)
            {
                Vector2 p0 = anchors[Math.Max(i - 1, 0)];
                Vector2 p1 = anchors[i];
                Vector2 p2 = anchors[i + 1];
                Vector2 p3 = anchors[Math.Min(i + 2, anchors.Count - 1)];

                int steps = sampleCount(Vector2.Distance(p1, p2));

                for (int step = 1; step <= steps; step++)
                    result.Add(catmullAt(p0, p1, p2, p3, (double)step / steps));
            }

            return result;
        }

        private static Vector2 catmullAt(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, double t)
        {
            double t2 = t * t;
            double t3 = t2 * t;

            double x = 0.5 * (2 * p1.X + (-p0.X + p2.X) * t
                              + (2 * p0.X - 5 * p1.X + 4 * p2.X - p3.X) * t2
                              + (-p0.X + 3 * p1.X - 3 * p2.X + p3.X) * t3);

            double y = 0.5 * (2 * p1.Y + (-p0.Y + p2.Y) * t
                              + (2 * p0.Y - 5 * p1.Y + 4 * p2.Y - p3.Y) * t2
                              + (-p0.Y + 3 * p1.Y - 3 * p2.Y + p3.Y) * t3);

            return new Vector2((float)x, (float)y);
        }

        private static int sampleCount(double length) =>
            (int)Math.Clamp(Math.Ceiling(length / sample_spacing), min_samples, max_samples);

        private static double polylineLength(List<Vector2> pts)
        {
            double total = 0;

            for (int i = 1; i < pts.Count; i++)
                total += Vector2.Distance(pts[i - 1], pts[i]);

            return total;
        }

        /// <summary>
        /// Cuts the sampled curve down to <paramref name="targetLength"/>, or
        /// extends it along its final direction if the control points describe
        /// something shorter than the declared length.
        /// </summary>
        private static List<Vector2> trimToLength(List<Vector2> sampled, double targetLength)
        {
            var result = new List<Vector2> { sampled[0] };
            double travelled = 0;

            for (int i = 1; i < sampled.Count; i++)
            {
                double segment = Vector2.Distance(sampled[i - 1], sampled[i]);

                if (segment <= 0)
                    continue;

                if (travelled + segment >= targetLength)
                {
                    float t = (float)((targetLength - travelled) / segment);

                    result.Add(Vector2.Lerp(sampled[i - 1], sampled[i], t));

                    return result;
                }

                travelled += segment;
                result.Add(sampled[i]);
            }

            if (result.Count >= 2 && travelled < targetLength)
            {
                Vector2 last = result[result.Count - 1];
                Vector2 previous = result[result.Count - 2];
                Vector2 direction = last - previous;

                if (direction.LengthSquared > 0)
                {
                    direction.Normalize();
                    result.Add(last + direction * (float)(targetLength - travelled));
                }
            }

            return result;
        }
    }
}
