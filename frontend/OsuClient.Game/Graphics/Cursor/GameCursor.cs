using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Graphics.Cursor
{
    /// <summary>
    /// A glowing ball of blue light that follows the cursor, replacing the OS
    /// pointer everywhere in the game. Layered from a soft outer glow (an
    /// edge-effect blur, so it isn't a hard-edged circle) down to a
    /// near-white hot core, with a gentle idle pulse and a snappier scale-down
    /// on click for feedback.
    ///
    /// Only <see cref="CreateCursor"/> is overridden here — <see cref="CursorContainer"/>
    /// (via <see cref="VisibilityContainer"/>) already has working show/hide
    /// behaviour on mouse movement built in; overriding its <c>PopIn</c>/<c>PopOut</c>
    /// here as well, even to replicate the same fade, left the cursor
    /// permanently invisible (tracking the mouse correctly — right position,
    /// right alpha, when inspected directly — but never actually drawn).
    /// </summary>
    public partial class GameCursor : CursorContainer
    {
        private const float cursor_diameter = 26;

        private CursorTrail trail = null!;

        protected override Drawable CreateCursor() => new GlowOrb();

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // Depth 1 keeps the tail behind the orb itself.
            AddInternal(trail = new CursorTrail(cursor_diameter, PulsingOrb.CoreColour, PulsingOrb.GlowColour)
            {
                Depth = 1,
            });
        }

        protected override void Update()
        {
            base.Update();

            // Fed from the cursor's own position rather than from mouse
            // events, so the trail can't drift out of step with the orb it's
            // trailing — whatever moved the cursor, the tail follows the same
            // point.
            trail.MoveTo(ActiveCursor.Position);
        }

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            ActiveCursor.ScaleTo(0.8f, 100, Easing.OutQuint);
            return base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseUpEvent e)
        {
            ActiveCursor.ScaleTo(1f, 200, Easing.OutElastic);
            base.OnMouseUp(e);
        }

        /// <summary>
        /// The outer wrapper: its <see cref="Scale"/> is reserved for click
        /// feedback (see <see cref="GameCursor"/>) so it doesn't fight the
        /// idle pulse, which lives on the inner <see cref="PulsingOrb"/>
        /// instead.
        /// </summary>
        private partial class GlowOrb : CompositeDrawable
        {
            public GlowOrb()
            {
                // Origin.Centre alone puts the mouse point at the middle of
                // the orb, which is what's wanted here. Also setting
                // Anchor.Centre — which would seem equally reasonable, and is
                // a completely ordinary combination anywhere else in the UI —
                // leaves CursorContainer's own positioning logic pointed
                // somewhere degenerate: the orb still reports the right
                // Alpha and Position when inspected directly, but nothing
                // ever actually draws.
                Origin = Anchor.Centre;
                Size = new Vector2(cursor_diameter);

                InternalChild = new PulsingOrb();
            }
        }

        private partial class PulsingOrb : CompositeDrawable
        {
            /// <summary>The near-white centre of the orb; also the near end of the trail.</summary>
            public static readonly Color4 CoreColour = new Color4(0.75f, 0.9f, 1f, 1f);

            /// <summary>The blue the orb glows with; also the far end of the trail.</summary>
            public static readonly Color4 GlowColour = new Color4(0.25f, 0.55f, 1f, 1f);

            public PulsingOrb()
            {
                RelativeSizeAxes = Axes.Both;
                Anchor = Anchor.Centre;
                Origin = Anchor.Centre;

                InternalChildren = new Drawable[]
                {
                    // A soft halo well outside the orb's own bounds — the
                    // actual light-spill look — via a blurred edge effect
                    // rather than a hard-edged circle.
                    new Circle
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = GlowColour,
                        Alpha = 0.5f,
                        EdgeEffect = new EdgeEffectParameters
                        {
                            Type = EdgeEffectType.Glow,
                            Colour = new Color4(GlowColour.R, GlowColour.G, GlowColour.B, 0.85f),
                            Radius = 30,
                        },
                    },
                    // The visible ball: a darker blue rim thinning into a
                    // near-white core, the way a bright point of light blows
                    // out its own centre.
                    new Circle
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = ColourInfo.GradientVertical(GlowColour, CoreColour),
                    },
                    new Circle
                    {
                        RelativeSizeAxes = Axes.Both,
                        Size = new Vector2(0.45f),
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Colour = Color4.White,
                        Alpha = 0.9f,
                    },
                };
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                // Needs a live clock to schedule against, which isn't
                // available yet in the constructor.
                this.ScaleTo(1f).Then()
                    .ScaleTo(1.12f, 700, Easing.InOutSine)
                    .Then()
                    .ScaleTo(1f, 700, Easing.InOutSine)
                    .Loop();
            }
        }
    }
}
