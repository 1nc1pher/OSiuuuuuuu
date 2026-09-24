using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Graphics
{
    /// <summary>Which edge a <see cref="ScreenRipple"/> starts from.</summary>
    public enum RippleEdge
    {
        Left,
        Right,

        /// <summary>From the middle of the screen, going out every way at once.</summary>
        Centre,
    }

    /// <summary>
    /// The rings of a screen change: glowing ripples rolling out from one edge
    /// and across the whole window, over both the screen leaving and the one
    /// arriving. The screens do their own fading and sliding underneath —
    /// this draws no background, so what shows through the rings is always a
    /// real screen, never a curtain.
    ///
    /// <para>
    /// It lives above the screen stack, in the game itself, rather than in
    /// either screen: it has to outlast the screen that started it and still
    /// be on top when the next one arrives underneath.
    /// </para>
    ///
    /// <para>
    /// Between <see cref="Begin"/> and <see cref="End"/> it keeps sending
    /// rings, so a screen that is slow to load reads as the ripple still
    /// travelling rather than as the game stalling. Every ring is built fresh
    /// and expires when done, rather than being kept hidden: a drawable held
    /// at Alpha 0 stops updating, and a fade-in queued on it never plays.
    /// </para>
    /// </summary>
    public partial class ScreenRipple : CompositeDrawable
    {
        /// <summary>How long one ring takes to cross the window.</summary>
        public const double RingDuration = 880;

        /// <summary>Gap between one ring and the next in the opening wave.</summary>
        private const double ring_stagger = 85;

        /// <summary>After the opening wave, one more ring this often until <see cref="End"/>.</summary>
        private const double pulse_interval = 280;

        /// <summary>How long input stays blocked after <see cref="End"/>, while the next screen settles.</summary>
        private const double settle_time = 400;

        /// <summary>
        /// Ends a ripple nobody ended. A screen that never arrives should cost
        /// a few seconds of rings, not a game that ignores every click.
        /// </summary>
        private const double max_duration = 5000;

        private static readonly Color4[] ring_colours =
        {
            RetroPalette.Magenta,
            RetroPalette.Cyan,
            RetroPalette.Violet,
            RetroPalette.Magenta,
        };

        private bool running;
        private bool waiting;

        private double startedAt;
        private double lastRingAt;
        private int ringCount;

        private Vector2 origin;
        private float crossDiameter;

        public ScreenRipple()
        {
            RelativeSizeAxes = Axes.Both;
        }

        /// <summary>Whether a ripple is under way, input blocked. Exposed for tests.</summary>
        public bool Running => running;

        /// <summary>
        /// Starts rings from <paramref name="edge"/>, and keeps them coming
        /// until <see cref="End"/>. Ignored while one is already running, so a
        /// double click cannot start two screen changes.
        /// </summary>
        /// <returns>False when a ripple was already running and this one was ignored.</returns>
        public bool Begin(RippleEdge edge) => start(edge switch
        {
            RippleEdge.Right => new Vector2(DrawWidth, DrawHeight / 2),
            RippleEdge.Left => new Vector2(0, DrawHeight / 2),
            _ => DrawSize / 2,
        });

        /// <summary>
        /// Starts rings from a point on screen — the thing that was pressed,
        /// so the ripple visibly comes out of it — and keeps them coming until
        /// <see cref="End"/>.
        /// </summary>
        public bool BeginAt(Vector2 screenSpacePoint) => start(ToLocalSpace(screenSpacePoint));

        private bool start(Vector2 from)
        {
            if (running)
                return false;

            running = true;
            waiting = true;
            startedAt = Time.Current;
            ringCount = 0;

            origin = from;

            // Far enough that a ring has fully left the window, corners
            // included, by the time it finishes.
            crossDiameter = 2.4f * new Vector2(DrawWidth, DrawHeight / 2).Length;

            for (int i = 0; i < ring_colours.Length; i++)
                spawnRing(i == 0, i * ring_stagger);

            lastRingAt = startedAt + (ring_colours.Length - 1) * ring_stagger;

            return true;
        }

        /// <summary>
        /// The next screen has arrived: no more rings after the ones already
        /// out, and input returns once it has settled.
        /// </summary>
        public void End()
        {
            if (!waiting)
                return;

            waiting = false;

            Scheduler.AddDelayed(() => running = false, settle_time);
        }

        protected override void Update()
        {
            base.Update();

            if (!waiting)
                return;

            if (Time.Current - startedAt > max_duration)
            {
                End();
                return;
            }

            if (Time.Current - lastRingAt >= pulse_interval)
            {
                lastRingAt = Time.Current;
                spawnRing(false, 0);
            }
        }

        private void spawnRing(bool leading, double delay)
        {
            var colour = ring_colours[ringCount++ % ring_colours.Length];
            var ring = createRing(colour, leading);

            ring.Position = origin;
            AddInternal(ring);

            ring.Delay(delay)
                .ResizeTo(crossDiameter, RingDuration, Easing.OutCubic)
                .FadeOut(RingDuration, Easing.InQuad)
                .Expire();
        }

        private static Drawable createRing(Color4 colour, bool leading) => new CircularContainer
        {
            Origin = Anchor.Centre,
            Masking = true,
            BorderThickness = leading ? 4 : 3,
            BorderColour = colour,
            EdgeEffect = new EdgeEffectParameters
            {
                Type = EdgeEffectType.Glow,
                Colour = colour.Opacity(leading ? 0.6f : 0.4f),
                Radius = leading ? 22 : 14,
                // Otherwise the glow floods the whole disc, not just its rim.
                Hollow = true,
            },
            // The border only draws with something inside the circle; this is
            // that something, and invisible.
            Child = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Alpha = 0,
                AlwaysPresent = true,
            },
        };

        // While running, the ripple owns input: a click or key landing on the
        // screen being left would act on a screen that is on its way out.
        public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) => running;

        protected override bool OnMouseDown(MouseDownEvent e) => running;

        protected override bool OnClick(ClickEvent e) => running;

        protected override bool OnScroll(ScrollEvent e) => running;

        protected override bool OnKeyDown(KeyDownEvent e) => running;
    }
}
