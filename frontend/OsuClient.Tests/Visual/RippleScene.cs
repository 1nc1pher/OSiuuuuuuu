using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Screens;
using osu.Framework.Timing;
using OsuClient.Game.Graphics;
using OsuClient.Game.Screens.MainMenu;

namespace OsuClient.Tests.Visual
{
    /// <summary>
    /// The <c>ripple-*</c> screenshot scenes: the menu, set up the way the
    /// game sets it up — a screen stack with the ripple cached above it — and
    /// PLAY or CREATE pressed once the menu has settled.
    ///
    /// The press waits a fixed <see cref="SettleTime"/>, so a capture delay of
    /// <c>SettleTime + t</c> lands <c>t</c> milliseconds into the transition.
    ///
    /// That cannot catch the next screen arriving, because when it arrives
    /// depends on how long it took to load — and song select's first frame
    /// after arriving is one long stall on top of that. For that,
    /// <c>freezeAfterArrival</c> stops the scene's clock that many
    /// milliseconds after the new screen's curve actually starts sweeping, so any
    /// later capture shows that exact moment of it.
    /// </summary>
    public partial class RippleScene : CompositeDrawable
    {
        public const double SettleTime = 1500;

        [Cached]
        private readonly ScreenRipple ripple = new ScreenRipple();

        private readonly bool play;
        private readonly double? freezeAfterArrival;

        private readonly StopwatchClock stopwatch = new StopwatchClock(true);

        private ScreenStack stack = null!;
        private MainMenuScreen menu = null!;
        private double? arrivedAt;

        public RippleScene(bool play, double? freezeAfterArrival = null)
        {
            this.play = play;
            this.freezeAfterArrival = freezeAfterArrival;

            RelativeSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            menu = new MainMenuScreen();
            stack = new ScreenStack { RelativeSizeAxes = Axes.Both };

            InternalChild = new Container
            {
                RelativeSizeAxes = Axes.Both,
                // Its own clock, so the whole transition can be stopped dead
                // for a capture.
                Clock = new FramedClock(stopwatch),
                Children = new Drawable[] { stack, ripple },
            };

            stack.Push(menu);

            Scheduler.AddDelayed(() =>
            {
                menu.Expand();

                if (play)
                    menu.Play();
                else
                    menu.Create();
            }, SettleTime);
        }

        protected override void Update()
        {
            base.Update();

            if (freezeAfterArrival == null || !stopwatch.IsRunning)
                return;

            if (arrivedAt == null && stack.CurrentScreen is IRevealable arriving && arriving != menu
                && arriving.Reveal.Revealing && arriving.Reveal.Progress > 0)
            {
                arrivedAt = stopwatch.CurrentTime;
            }

            if (arrivedAt != null && stopwatch.CurrentTime - arrivedAt >= freezeAfterArrival)
                stopwatch.Stop();
        }
    }
}
