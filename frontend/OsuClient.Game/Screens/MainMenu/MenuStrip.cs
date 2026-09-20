using System;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using OsuClient.Game.Graphics;
using osuTK;

namespace OsuClient.Game.Screens.MainMenu
{
    /// <summary>
    /// The navigation strip that grows out from behind the logo when it's
    /// clicked: CREATE to the left, PLAY to the right
    /// (MENU_REDESIGN_PLAN.md step 7).
    ///
    /// <see cref="Expansion"/> is what animates — 0 closed, 1 open — and
    /// everything else is laid out from it in <see cref="Update"/>. The
    /// buttons keep their full width the whole time and are clipped by this
    /// container's masking, so they slide out from under the circle rather
    /// than growing in place; growing in place squashes the lettering
    /// horizontally for the length of the animation, which looks like a bug
    /// even though it settles correctly.
    ///
    /// <see cref="Expansion"/> is animated rather than
    /// <see cref="Drawable.Width"/>, because Update owns Width — two writers
    /// on one property is the trap this screen already avoids for the logo's
    /// Scale. It is a <see cref="BindableFloat"/> driven by
    /// <c>TransformBindableTo</c>, and deliberately not a plain property
    /// driven by <c>TransformTo(nameof(Expansion), …)</c>: that overload
    /// resolves the member by name at runtime, and when it fails to it fails
    /// *silently*. It did exactly that here — the strip stayed shut while the
    /// logo shrank and the hint text swapped, all three started by the same
    /// three lines, so the screen looked like it had opened with the buttons
    /// missing rather than like an animation that never ran.
    /// </summary>
    public partial class MenuStrip : CompositeDrawable
    {
        /// <summary>Full open width, in pixels. Set by the screen as it resizes.</summary>
        public float TargetWidth { get; set; }

        /// <summary>
        /// Clear space kept in the middle for the circle, in pixels. The
        /// buttons sit outside it.
        /// </summary>
        public float CentreGap { get; set; }

        private readonly BindableFloat expansion = new BindableFloat();

        /// <summary>How far open the strip is, 0 (shut) to 1 (fully out).</summary>
        public float Expansion => expansion.Value;

        private readonly Box connector;
        private readonly MenuStripButton createButton;
        private readonly MenuStripButton playButton;

        public MenuStrip(Action create, Action play)
        {
            Anchor = Anchor.Centre;
            Origin = Anchor.Centre;
            Masking = true;

            // Update is what sets Alpha, and a drawable that hides itself in
            // its own Update never runs Update again — so it can never
            // animate back. That deadlock is exactly how the strip failed:
            // shut on the first frame, and from then on unable to open,
            // while the logo beside it (which has no such rule) shrank on
            // cue and made the screen look like it had opened with the
            // buttons missing. AlwaysPresent keeps it updating while
            // invisible; ReceivePositionalInputAt below keeps it unclickable.
            AlwaysPresent = true;

            InternalChildren = new Drawable[]
            {
                // A hairline joining the two halves under the circle, so they
                // read as one strip the logo is sitting on rather than two
                // unrelated slabs.
                connector = new Box
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 2,
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Colour = ColourInfo.GradientHorizontal(
                        RetroPalette.Magenta.Opacity(0.5f),
                        RetroPalette.Cyan.Opacity(0.5f)),
                },
                createButton = new MenuStripButton(
                    StripSide.Left,
                    RetroPalette.Magenta,
                    MenuStripButton.PlusIcon(20),
                    "CREATE")
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    RelativeSizeAxes = Axes.Y,
                    Action = create,
                },
                playButton = new MenuStripButton(
                    StripSide.Right,
                    RetroPalette.Cyan,
                    MenuStripButton.PlayIcon(20),
                    "PLAY")
                {
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.CentreRight,
                    RelativeSizeAxes = Axes.Y,
                    Action = play,
                },
            };
        }

        /// <summary>
        /// Opens or closes the strip over <paramref name="duration"/>
        /// milliseconds. 0 snaps.
        /// </summary>
        public void AnimateTo(bool open, double duration)
        {
            this.TransformBindableTo(expansion, open ? 1f : 0f, duration, Easing.OutQuint);
        }

        protected override void Update()
        {
            base.Update();

            Width = MathF.Max(0, TargetWidth * Expansion);

            float buttonWidth = MathF.Max(0, (TargetWidth - CentreGap) / 2);

            createButton.Width = buttonWidth;
            playButton.Width = buttonWidth;

            Alpha = Expansion <= 0.001f ? 0 : 1;

            connector.Alpha = Expansion;
        }

        /// <summary>
        /// A shut strip takes no clicks. It is
        /// <see cref="Drawable.AlwaysPresent"/> so that it can animate, which
        /// would otherwise leave its buttons live along the centre line while
        /// invisible.
        /// </summary>
        public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) =>
            Expansion > 0.5f && base.ReceivePositionalInputAt(screenSpacePos);
    }
}
