using System;
using System.Linq;
using NUnit.Framework;
using OsuClient.Game.Beatmaps.HitObjects;
using osuTK;

namespace OsuClient.Tests.Beatmaps
{
    /// <summary>
    /// Slider curve geometry: pure maths, so these run headless with no game
    /// host and no framework clock.
    /// </summary>
    [TestFixture]
    public class SliderPathTests
    {
        private const double tolerance = 0.5;

        private static SliderData slider(SliderCurveType type, double pixelLength, params Vector2[] anchors) =>
            new SliderData
            {
                Position = anchors[0],
                StartTime = 0,
                CurveType = type,
                Path = anchors,
                Slides = 1,
                PixelLength = pixelLength,
            };

        [Test]
        public void LinearPathRunsBetweenItsAnchors()
        {
            var path = SliderPath.FromSlider(
                slider(SliderCurveType.Linear, 100, new Vector2(100, 100), new Vector2(200, 100)));

            Assert.Multiple(() =>
            {
                Assert.That(path.Length, Is.EqualTo(100).Within(tolerance));
                Assert.That(path.PositionAt(0).X, Is.EqualTo(100).Within(tolerance));
                Assert.That(path.PositionAt(1).X, Is.EqualTo(200).Within(tolerance));
                Assert.That(path.PositionAt(0.5).X, Is.EqualTo(150).Within(tolerance));
            });
        }

        [Test]
        public void PathIsTrimmedToTheDeclaredPixelLength()
        {
            // Anchors describe 200px, but the slider says it is 100px long —
            // the declared length is what osu! times the slider against, so
            // the curve has to be cut to match.
            var path = SliderPath.FromSlider(
                slider(SliderCurveType.Linear, 100, new Vector2(0, 0), new Vector2(200, 0)));

            Assert.Multiple(() =>
            {
                Assert.That(path.Length, Is.EqualTo(100).Within(tolerance));
                Assert.That(path.PositionAt(1).X, Is.EqualTo(100).Within(tolerance));
            });
        }

        [Test]
        public void ShortPathIsExtendedToTheDeclaredPixelLength()
        {
            var path = SliderPath.FromSlider(
                slider(SliderCurveType.Linear, 100, new Vector2(0, 0), new Vector2(50, 0)));

            Assert.Multiple(() =>
            {
                Assert.That(path.Length, Is.EqualTo(100).Within(tolerance));
                Assert.That(path.PositionAt(1).X, Is.EqualTo(100).Within(tolerance));
            });
        }

        [Test]
        public void PerfectCircleStaysOnItsCircumcircle()
        {
            // Three points of a circle centred at (0,0) with radius 100.
            var path = SliderPath.FromSlider(
                slider(SliderCurveType.PerfectCircle, 100,
                    new Vector2(100, 0), new Vector2(0, 100), new Vector2(-100, 0)));

            foreach (var point in path.Points)
                Assert.That(point.Length, Is.EqualTo(100).Within(1.0), $"{point} left the circle");
        }

        [Test]
        public void CollinearPerfectCircleFallsBackToALine()
        {
            // No circle passes through three points on a line; osu! draws these
            // as a plain line rather than failing.
            var path = SliderPath.FromSlider(
                slider(SliderCurveType.PerfectCircle, 200,
                    new Vector2(0, 0), new Vector2(100, 0), new Vector2(200, 0)));

            Assert.Multiple(() =>
            {
                Assert.That(path.Length, Is.EqualTo(200).Within(tolerance));
                Assert.That(path.PositionAt(0.5).Y, Is.EqualTo(0).Within(tolerance));
            });
        }

        [Test]
        public void BezierStartsAndEndsOnItsOuterAnchors()
        {
            var path = SliderPath.FromSlider(
                slider(SliderCurveType.Bezier, 300,
                    new Vector2(0, 0), new Vector2(100, 200), new Vector2(200, 0)));

            Assert.Multiple(() =>
            {
                Assert.That(path.PositionAt(0).X, Is.EqualTo(0).Within(tolerance));
                Assert.That(path.PositionAt(0).Y, Is.EqualTo(0).Within(tolerance));

                // A quadratic bezier bows toward its middle control point
                // without reaching it.
                Assert.That(path.Points.Max(p => p.Y), Is.GreaterThan(50).And.LessThan(200));
            });
        }

        [Test]
        public void RepeatedAnchorSplitsBezierIntoSegmentsThroughThatPoint()
        {
            // A doubled control point is how a mapper puts a hard corner in a
            // slider: the curve must actually pass through it.
            var corner = new Vector2(100, 100);

            var path = SliderPath.FromSlider(
                slider(SliderCurveType.Bezier, 400,
                    new Vector2(0, 0), corner, corner, new Vector2(200, 0)));

            Assert.That(path.Points.Any(p => Vector2.Distance(p, corner) < 1),
                "the curve should pass through the doubled anchor");
        }

        [Test]
        public void DegenerateSliderDoesNotThrow()
        {
            var path = SliderPath.FromSlider(
                slider(SliderCurveType.Linear, 0, new Vector2(50, 50)));

            Assert.Multiple(() =>
            {
                Assert.That(path.Length, Is.EqualTo(0));
                Assert.That(path.PositionAt(0.5), Is.EqualTo(new Vector2(50, 50)));
            });
        }

        [Test]
        public void SegmentAtZeroIsATinyStubAtTheHead()
        {
            var path = SliderPath.FromSlider(
                slider(SliderCurveType.Linear, 100, new Vector2(0, 0), new Vector2(100, 0)));

            var segment = path.Segment(0);

            Assert.Multiple(() =>
            {
                Assert.That(segment.Count, Is.GreaterThanOrEqualTo(2), "a path needs at least 2 vertices to draw");
                Assert.That(segment[0].X, Is.EqualTo(0).Within(tolerance));
            });
        }

        [Test]
        public void SegmentAtOneIsTheWholePath()
        {
            var path = SliderPath.FromSlider(
                slider(SliderCurveType.Linear, 100, new Vector2(0, 0), new Vector2(100, 0)));

            var segment = path.Segment(1);

            Assert.That(segment[segment.Count - 1].X, Is.EqualTo(100).Within(tolerance));
        }

        [Test]
        public void SegmentAtHalfStopsHalfway()
        {
            var path = SliderPath.FromSlider(
                slider(SliderCurveType.Linear, 100, new Vector2(0, 0), new Vector2(100, 0)));

            var segment = path.Segment(0.5);

            Assert.That(segment[segment.Count - 1].X, Is.EqualTo(50).Within(tolerance));
        }

        [Test]
        public void DirectionAtStartPointsTowardTheTail()
        {
            var path = SliderPath.FromSlider(
                slider(SliderCurveType.Linear, 100, new Vector2(0, 0), new Vector2(100, 0)));

            var direction = path.DirectionAt(0);

            Assert.Multiple(() =>
            {
                Assert.That(direction.X, Is.EqualTo(1).Within(0.01));
                Assert.That(direction.Y, Is.EqualTo(0).Within(0.01));
            });
        }

        [Test]
        public void DirectionAtEndPointsTowardTheTail()
        {
            var path = SliderPath.FromSlider(
                slider(SliderCurveType.Linear, 100, new Vector2(0, 0), new Vector2(100, 0)));

            var direction = path.DirectionAt(1);

            Assert.That(direction.X, Is.EqualTo(1).Within(0.01));
        }

        [Test]
        public void PositionAtIsClampedOutsideThePath()
        {
            var path = SliderPath.FromSlider(
                slider(SliderCurveType.Linear, 100, new Vector2(0, 0), new Vector2(100, 0)));

            Assert.Multiple(() =>
            {
                Assert.That(path.PositionAt(-5).X, Is.EqualTo(0).Within(tolerance));
                Assert.That(path.PositionAt(5).X, Is.EqualTo(100).Within(tolerance));
            });
        }
    }
}
