using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Screens;
using OsuClient.Game.Screens.MainMenu;
using osuTK.Graphics;

namespace OsuClient.Game
{
    /// <summary>
    /// Root game class. Owns the background and the <see cref="ScreenStack"/>
    /// that later phases navigate with (Menu -> Song Select -> Player -> Results).
    ///
    /// Phase 0 scope: stand up the window and push a single screen that proves
    /// the transform/animation system is running. No beatmaps, no gameplay.
    /// </summary>
    public partial class OsuClientGame : osu.Framework.Game
    {
        private readonly string? songsDirectory;

        private ScreenStack screenStack = null!;

        /// <param name="songsDirectory">
        /// Overrides where song select looks for beatmaps. When null, the
        /// default is resolved from the OSUCLIENT_SONGS_DIR environment
        /// variable or the repository's data/output directory.
        /// </param>
        public OsuClientGame(string? songsDirectory = null)
        {
            this.songsDirectory = songsDirectory;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(0.07f, 0.07f, 0.10f, 1f),
                },
                screenStack = new ScreenStack
                {
                    RelativeSizeAxes = Axes.Both,
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            screenStack.Push(new MainMenuScreen(songsDirectory));
        }
    }
}
