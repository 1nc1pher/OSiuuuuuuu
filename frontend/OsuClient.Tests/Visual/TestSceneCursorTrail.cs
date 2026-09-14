using System.Linq;
using NUnit.Framework;
using osu.Framework.Timing;
using OsuClient.Game.Graphics.Cursor;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Tests.Visual
{
    /// <summary>
    /// The cursor's comet tail. Driven by a parked <see cref="ManualClock"/>
    /// and explicit <see cref="CursorTrail.MoveTo"/> calls, so the movement
    /// path under test is exact rather than whatever the test host's frame
    /// pacing happens to produce.
    /// </summary>
    [TestFixture]
    public partial class TestSceneCursorTrail : osu.Framework.Testing.TestScene
    {
        private const float cursor_diameter = 26;

        private ManualClock manualClock = null!;
        private CursorTrail trail = null!;

        private void createTrail()
        {
            AddStep("create trail", () =>
            {
                manualClock = new ManualClock();
                Child = trail = new CursorTrail(cursor_diameter, Color4.White, Color4.Blue)
                {
                    Clock = new FramedClock(manualClock),
                };
            });
            AddUntilStep("loaded", () => trail.IsLoaded);
        }

        private void moveTo(float x, float y) => AddStep($"move to ({x},{y})", () => trail.MoveTo(new Vector2(x, y)));

        private void advanceTo(double time) => AddStep($"clock to {time}ms", () => manualClock.CurrentTime = time);

        [Test]
        public void TestTailLengthStaysWithinAboutTwoCursorWidths()
        {
            createTrail();

            advanceTo(0);
            moveTo(0, 0);

            // A flick clear across the screen: a purely time-based trail would
            // smear the whole way, which is what would cover up what's on
            // screen underneath.
            moveTo(200, 0);

            AddAssert("tail is capped, not screen-long", () => trail.TrailLength <= cursor_diameter * 2);
            AddAssert("tail is still long enough to read as a trail",
                () => trail.TrailLength >= cursor_diameter * 1.5f);
        }

        [Test]
        public void TestSlowMovementStillBuildsATail()
        {
            createTrail();

            advanceTo(0);
            moveTo(0, 0);

            // One pixel per frame is less than the sample spacing. Without
            // carrying the leftover distance between frames, no single frame
            // would ever cover a full spacing and the trail would never
            // appear at all while moving slowly.
            for (int i = 1; i <= 10; i++)
                moveTo(i, 0);

            AddAssert("samples were laid down", () => trail.SampleCount > 0);
        }

        [Test]
        public void TestSamplesFollowTheMovementPath()
        {
            createTrail();

            advanceTo(0);
            moveTo(0, 0);
            moveTo(30, 0);

            AddAssert("all samples sit on the path travelled",
                () => trail.SamplePositions.All(p => p.Y == 0 && p.X > 0 && p.X <= 30));

            AddAssert("samples are evenly spaced along it", () =>
            {
                var xs = trail.SamplePositions.Select(p => p.X).OrderBy(x => x).ToArray();

                for (int i = 1; i < xs.Length; i++)
                {
                    float gap = xs[i] - xs[i - 1];
                    if (gap < 2.4f || gap > 2.6f)
                        return false;
                }

                return xs.Length > 1;
            });
        }

        [Test]
        public void TestCornerIsTracedRatherThanCutAcross()
        {
            createTrail();

            advanceTo(0);
            moveTo(0, 0);
            moveTo(20, 0);
            moveTo(20, 20);

            // The tail should bend around the corner, so it holds samples from
            // both legs rather than a straight line between the endpoints.
            AddAssert("has samples from the horizontal leg",
                () => trail.SamplePositions.Any(p => p.Y == 0 && p.X > 0));
            AddAssert("has samples from the vertical leg",
                () => trail.SamplePositions.Any(p => p.X == 20 && p.Y > 0));
        }

        [Test]
        public void TestTeleportRestartsInsteadOfStreaking()
        {
            createTrail();

            advanceTo(0);
            moveTo(0, 0);
            moveTo(40, 0);
            AddAssert("has a tail", () => trail.SampleCount > 0);

            // A jump this far isn't movement — it's a warp, and drawing a line
            // through everything in between would be wrong.
            moveTo(900, 600);
            AddAssert("tail was dropped, not stretched across", () => trail.SampleCount == 0);
        }

        [Test]
        public void TestTailFadesAwayOnceTheCursorStops()
        {
            createTrail();

            advanceTo(0);
            moveTo(0, 0);
            moveTo(40, 0);
            AddAssert("has a tail", () => trail.SampleCount > 0);

            advanceTo(1000);
            AddStep("hold still", () => trail.MoveTo(new Vector2(40, 0)));

            AddAssert("tail is gone", () => trail.SampleCount == 0);
        }
    }
}
