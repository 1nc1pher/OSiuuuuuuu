using System;
using System.IO;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using OsuClient.Game.Backend;
using OsuClient.Game.Beatmaps;
using OsuClient.Game.Graphics;
using OsuClient.Game.Screens.MainMenu;
using OsuClient.Game.Screens.Generation;

namespace OsuClient.Tests.Visual
{
    /// <summary>
    /// Renders the DSP reveal at a chosen stage, from the committed fixture,
    /// with no Python process involved.
    ///
    /// Each stage of the sequence needs looking at as it is built, and a
    /// timed sequence is the one thing a single capture cannot catch: by the
    /// time the harness takes its frame the interesting stage has usually
    /// been and gone. So the scene advances to a named stage and holds there.
    /// </summary>
    public partial class DspRevealScene : CompositeDrawable
    {
        private readonly int targetStage;

        private readonly string? mapFolder;

        /// <param name="targetStage">The stage to hold on.</param>
        /// <param name="mapFolder">A generated map to play instead of the committed fixture — for a real five-tier set.</param>
        public DspRevealScene(int targetStage, string? mapFolder = null)
        {
            this.targetStage = targetStage;
            this.mapFolder = mapFolder;

            RelativeSizeAxes = Axes.Both;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            string? root = BeatmapLibrary.FindRepositoryRoot(AppContext.BaseDirectory);
            string folder = mapFolder ?? (root == null
                ? string.Empty
                : Path.Combine(root, "frontend", "OsuClient.Tests", "Fixtures"));

            var analysis = AnalysisData.LoadFromFolder(folder);

            if (analysis == null)
            {
                InternalChild = new RetroText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Font = RetroFontFamily.Body,
                    TextSize = 14,
                    Colour = RetroPalette.TextDim,
                    Text = "no analysis fixture — run tests/fixtures/make_client_fixture.py",
                };

                return;
            }

            var backdrop = new Game.Screens.MainMenu.MenuBackground { Drifting = false };

            InternalChildren = new Drawable[]
            {
                // What DspVisualizationScreen draws behind the reveal: the
                // deck's wallpaper, still and dimmed.
                backdrop,
                new Box { RelativeSizeAxes = Axes.Both, Colour = new osuTK.Graphics.Color4(0f, 0f, 0f, 0.42f) },
                new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding { Horizontal = 40, Vertical = 30 },
                    Child = new DspReveal(analysis,
                                                   DspDetail.LoadFromFolder(folder),
                                                   AnalysisData.FindSpectrogram(folder))
                    {
                        StartStage = targetStage,
                        HoldAtStartStage = true,
                        // So the finale offers both of its choices, as it does
                        // on the real screen with a folder to analyse.
                        AnalyserRequested = () => { },
                    },
                },
            };

            // Only once it is in the tree: SetBackground loads the texture
            // asynchronously through the background itself.
            backdrop.SetBackground(
                WallpaperLibrary.PickRandom(WallpaperLibrary.Load(WallpaperLibrary.ResolveDefaultDirectory())));
        }
    }
}
