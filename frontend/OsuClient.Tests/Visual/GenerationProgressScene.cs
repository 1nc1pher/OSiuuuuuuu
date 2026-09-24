using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using OsuClient.Game.Graphics;
using OsuClient.Game.Screens.Generation;
using osuTK;

namespace OsuClient.Tests.Visual
{
    /// <summary>
    /// The <c>generation-running</c> and <c>generation-failed</c> screenshot
    /// scenes.
    ///
    /// Both are driven by feeding the panel the exact lines the backend
    /// prints, so no Python process has to run to capture them — and so the
    /// frame is showing the same parse path the real screen uses rather than
    /// a hand-set state that could drift from it.
    /// </summary>
    public partial class GenerationProgressScene : CompositeDrawable
    {
        /// <summary>Lines copied from a real run of <c>src/main.py</c>.</summary>
        private static readonly string[] successful_run =
        {
            "Loading golden.mp3",
            "[1/5] Analysing and mapping Easy",
            "[2/5] Analysing and mapping Normal",
            "[3/5] Analysing and mapping Hard",
        };

        private static readonly string[] failed_run =
        {
            "Loading broken.mp3",
            "Traceback (most recent call last):",
            "  File \"src/main.py\", line 134, in <module>",
            "    main()",
            "  File \"src/audio/loader.py\", line 61, in load_audio",
            "    y, sr = librosa.load(path, sr=sr, mono=mono)",
            "soundfile.LibsndfileError: Error opening 'broken.mp3': "
            + "File contains data in an unknown format.",
        };

        public GenerationProgressScene(bool failed)
        {
            RelativeSizeAxes = Axes.Both;

            var panel = new GenerationProgressPanel(failed ? "broken.mp3" : "golden.mp3")
            {
                RelativeSizeAxes = Axes.Both,
            };

            var backdrop = new Game.Screens.MainMenu.MenuBackground { Drifting = false };

            InternalChildren = new Drawable[]
            {
                // What DspVisualizationScreen draws behind the panel: the
                // deck's wallpaper, still and dimmed.
                backdrop,
                new Box { RelativeSizeAxes = Axes.Both, Colour = new osuTK.Graphics.Color4(0f, 0f, 0f, 0.42f) },
                new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding { Horizontal = 44, Vertical = 34 },
                    Child = panel,
                },
            };

            // Only once it has loaded: SetBackground loads the texture
            // asynchronously through the background itself.
            backdrop.OnLoadComplete += _ => backdrop.SetBackground(
                WallpaperLibrary.PickRandom(WallpaperLibrary.Load(WallpaperLibrary.ResolveDefaultDirectory())));

            // Fed after load so the panel's own lamps and readouts are built
            // before anything drives them.
            panel.OnLoadComplete += _ =>
            {
                foreach (string line in failed ? failed_run : successful_run)
                    panel.AppendLine(line);

                if (failed)
                    panel.Finish(false, "generation failed — exit code 1");
            };
        }
    }
}
