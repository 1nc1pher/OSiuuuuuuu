using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Configuration;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Platform;
using osu.Framework.Screens;
using OsuClient.Game.Graphics;
using OsuClient.Game.Scores;
using OsuClient.Game.Graphics.Cursor;
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

        /// <summary>
        /// Above the screen stack so a screen change can ride over both the
        /// screen leaving and the one arriving. Cached for any screen that
        /// wants to leave that way; one that finds none just pushes.
        /// </summary>
        [Cached]
        private readonly ScreenRipple screenRipple = new ScreenRipple();

        /// <summary>
        /// The music outside gameplay, shared by the menu and song select so
        /// moving between them never restarts or reloads the song playing.
        /// </summary>
        [Cached]
        private readonly MenuTrack music;

        /// <summary>The best score on every map played, kept between sessions.</summary>
        private HighScoreStore highScores = null!;

        protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
        {
            var dependencies = new DependencyContainer(base.CreateChildDependencies(parent));

            // In the game's own storage, next to its settings: scores belong
            // to this install, not to the repository the maps came from.
            highScores = new HighScoreStore(Host.Storage.GetFullPath("highscores.json"));
            dependencies.Cache(highScores);

            return dependencies;
        }

        /// <param name="songsDirectory">
        /// Overrides where song select looks for beatmaps. When null, the
        /// default is resolved from the OSUCLIENT_SONGS_DIR environment
        /// variable or the repository's data/output directory.
        /// </param>
        public OsuClientGame(string? songsDirectory = null)
        {
            this.songsDirectory = songsDirectory;
            music = new MenuTrack(songsDirectory);
        }

        [BackgroundDependencyLoader]
        private void load(FrameworkConfigManager frameworkConfig)
        {
            startFullscreen(frameworkConfig);

            Children = new Drawable[]
            {
                music,
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(0.07f, 0.07f, 0.10f, 1f),
                },
                screenStack = new ScreenStack
                {
                    RelativeSizeAxes = Axes.Both,
                },
                screenRipple,
                new GameCursor(),
            };
        }

        /// <summary>
        /// Opens covering the whole screen, every launch.
        ///
        /// Set on each start rather than only as a first-run default: the
        /// framework remembers the last window mode in framework.ini, so a
        /// default would never reach anyone who has run the game before.
        /// Alt+Enter still drops to a window for the rest of a session.
        ///
        /// Borderless rather than exclusive fullscreen: it looks the same, but
        /// the native file dialog behind LOAD TAPE and alt-tab both work
        /// without minimising the game, which exclusive mode does on focus
        /// loss.
        /// </summary>
        private void startFullscreen(FrameworkConfigManager frameworkConfig)
        {
            var window = Host.Window;

            if (window == null)
                return;

            var mode = window.SupportedWindowModes.Contains(WindowMode.Borderless)
                ? WindowMode.Borderless
                : WindowMode.Fullscreen;

            frameworkConfig.SetValue(FrameworkSetting.WindowMode, mode);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            screenStack.Push(new MainMenuScreen(songsDirectory));
        }

        public override void SetHost(GameHost host)
        {
            base.SetHost(host);

            // The glowing orb replaces the OS pointer everywhere in the game.
            if (host.Window != null)
                host.Window.CursorState |= CursorState.Hidden;
        }
    }
}
