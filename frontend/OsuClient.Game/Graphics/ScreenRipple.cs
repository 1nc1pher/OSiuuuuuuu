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
    /// The edge of a screen change: one glowing ring sweeping out from an
    /// edge of the window, riding the rim of the next screen's
    /// <see cref="ScreenReveal"/> as it grows over the one being left. It
    /// draws no background of its own — inside the ring is the new screen,
    /// outside it the old one.
    ///
    /// <para>
    /// It lives above the screen stack, in the game itself, rather than in
    /// either screen: it has to outlast the screen that started it and still
    /// be on top when the next one arrives underneath.
    /// </para>
    ///
    /// <para>
    /// From <see cref="Begin"/> until the next screen has settled it also
    /// owns input, so nothing lands on a screen on its way out. The ring is
    /// sent by <see cref="Sweep"/>, when the next screen actually starts to
    /// show — not at the press, which would leave it running ahead of a
    /// screen still loading.
    /// </para>
    /// </summary>
    public partial class ScreenRipple : CompositeDrawable
    {
        /// <summary>How long input stays blocked after <see cref="End"/>, while the next screen settles.</summary>
        private const double settle_time = 400;

        /// <summary>
        /// Ends a ripple nobody ended. A screen that never arrives should cost
        /// a few seconds, not a game that ignores every click.
        /// </summary>
        private const double max_duration = 5000;

        private bool running;
        private bool waiting;

        private double startedAt;

        private Vector2 origin;

        public ScreenRipple()
        {
            RelativeSizeAxes = Axes.Both;
        }

        /// <summary>Whether a ripple is under way, input blocked. Exposed for tests.</summary>
        public bool Running => running;

        /// <summary>Where the ring will start, in this drawable's space — the window's.</summary>
        public Vector2 RingOrigin => origin;

        /// <summary>
        /// Takes input and sets where the ring will start, until
        /// <see cref="End"/>. Ignored while one is already running, so a
        /// double click cannot start two screen changes.
        /// </summary>
        /// <returns>False when a ripple was already running and this one was ignored.</returns>
        public bool Begin(RippleEdge edge) => start(edge switch
        {
            RippleEdge.Right => new Vector2(DrawWidth, DrawHeight / 2),
            RippleEdge.Left => new Vector2(0, DrawHeight / 2),
            _ => DrawSize / 2,
        });

        /// <summary>As <see cref="Begin"/>, from a point on screen — the thing that was pressed.</summary>
        public bool BeginAt(Vector2 screenSpacePoint) => start(ToLocalSpace(screenSpacePoint));

        private bool start(Vector2 from)
        {
            if (running)
                return false;

            running = true;
            waiting = true;
            startedAt = Time.Current;
            origin = from;

            return true;
        }

        /// <summary>
        /// Sends the one ring from the origin until it has left the window,
        /// on the same curve as the <see cref="ScreenReveal"/> it rims.
        /// </summary>
        public void Sweep(double duration, Easing easing)
        {
            var ring = createRing(RetroPalette.Magenta);

            ring.Position = origin;
            ring.Size = Vector2.Zero;
            AddInternal(ring);

            ring.ResizeTo(ScreenReveal.CoverDiameter(origin, DrawSize), duration, easing)
                // Bright for nearly the whole way, then gone as it runs off
                // the corners.
                .FadeOut(duration, Easing.InQuint)
                .Expire();
        }

        /// <summary>
        /// The next screen has arrived: input returns once it has settled.
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

            if (waiting && Time.Current - startedAt > max_duration)
                End();
        }

        private static Drawable createRing(Color4 colour) => new CircularContainer
        {
            Origin = Anchor.Centre,
            Masking = true,
            BorderThickness = 4,
            BorderColour = colour,
            EdgeEffect = new EdgeEffectParameters
            {
                Type = EdgeEffectType.Glow,
                Colour = colour.Opacity(0.6f),
                Radius = 22,
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
