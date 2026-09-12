using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Graphics
{
    /// <summary>
    /// The Phase 0 toolchain sanity check: one Drawable animated entirely by
    /// osu.Framework's transform system — the same easing/tween engine
    /// osu!(lazer) drives its own animations with.
    ///
    /// Three independent looping transform sequences run concurrently on
    /// separate properties (rotation, scale, colour). If all three play
    /// smoothly and stay in sync, the renderer, the scene graph and the
    /// transform stack are all live.
    /// </summary>
    public partial class AnimatedLogo : CompositeDrawable
    {
        /// <summary>
        /// Stand-in for a beat length until Phase 2 wires a real beatmap clock
        /// up to this. 600 ms == 100 BPM.
        /// </summary>
        private const double beat_length = 600;

        private Box square = null!;

        public AnimatedLogo()
        {
            Size = new Vector2(160);
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChild = square = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Colour = Color4.DeepSkyBlue,
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            square.RotateTo(0)
                  .RotateTo(360, beat_length * 4, Easing.InOutSine)
                  .Loop();

            square.ScaleTo(1f)
                  .ScaleTo(1.25f, beat_length / 2, Easing.OutQuint)
                  .Then()
                  .ScaleTo(1f, beat_length / 2, Easing.InOutSine)
                  .Loop();

            square.FadeColour(Color4.DeepSkyBlue)
                  .FadeColour(Color4.HotPink, beat_length * 2, Easing.InOutSine)
                  .Then()
                  .FadeColour(Color4.DeepSkyBlue, beat_length * 2, Easing.InOutSine)
                  .Loop();
        }
    }
}
