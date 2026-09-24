using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Screens;
using osu.Framework.Timing;
using OsuClient.Game.Graphics;
using OsuClient.Game.Screens.MainMenu;
using OsuClient.Game.Screens.SongSelect;

namespace OsuClient.Tests.Visual
{
    /// <summary>
    /// The <c>play-cue-*</c> screenshot scenes: song select set up as the
    /// game sets it up — shared music and the ripple cached above it — with
    /// the song confirmed once it has settled, and the whole scene's clock
    /// stopped <c>freezeAfter</c> milliseconds later so a capture can land
    /// mid-glide. With <c>then</c>, the landed record is then played or sent
    /// back with Escape, and the clock stops that long after that instead.
    /// </summary>
    public partial class PlayCueScene : CompositeDrawable
    {
        /// <summary>What happens after the record lands, and so what the freeze times from.</summary>
        public enum Then
        {
            /// <summary>Nothing: freeze counts from the song being confirmed.</summary>
            Nothing,

            /// <summary>The record is played: freeze counts from the press.</summary>
            Launch,

            /// <summary>Escape is pressed: freeze counts from that.</summary>
            Escape,
        }

        private const double settle_time = 1500;

        [Cached]
        private readonly MenuTrack music = new MenuTrack(pickOnLoad: false);

        [Cached]
        private readonly ScreenRipple ripple = new ScreenRipple();

        private readonly double freezeAfter;
        private readonly Then then;

        private readonly StopwatchClock stopwatch = new StopwatchClock(true);

        private ScreenStack stack = null!;
        private double? markAt;

        public PlayCueScene(double freezeAfter, Then then = Then.Nothing)
        {
            this.freezeAfter = freezeAfter;
            this.then = then;

            RelativeSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            var songSelect = new RetroSongSelectScreen();
            stack = new ScreenStack { RelativeSizeAxes = Axes.Both };

            InternalChild = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Clock = new FramedClock(stopwatch),
                Children = new Drawable[] { music, stack, ripple },
            };

            stack.Push(songSelect);

            Scheduler.AddDelayed(() =>
            {
                songSelect.ConfirmSelection();

                if (then == Then.Nothing)
                {
                    markAt = stopwatch.CurrentTime;
                    return;
                }

                // Let the record land, then play it or send it back.
                Scheduler.AddDelayed(() =>
                {
                    if (stack.CurrentScreen is PlayCueScreen cue)
                    {
                        if (then == Then.Launch)
                            cue.Launch();
                        else
                            cue.Exit();
                    }

                    markAt = stopwatch.CurrentTime;
                }, 1500);
            }, settle_time);
        }

        protected override void Update()
        {
            base.Update();

            if (markAt != null && stopwatch.IsRunning && stopwatch.CurrentTime - markAt >= freezeAfter)
                stopwatch.Stop();
        }
    }
}
