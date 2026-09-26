using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Configuration;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Framework.Testing;
using SixLabors.ImageSharp;

namespace OsuClient.Tests.Visual
{
    /// <summary>
    /// Renders one named scene into a real window and writes a single frame to
    /// a PNG, then exits — <c>dotnet run --project frontend/OsuClient.Tests --
    /// --screenshot &lt;scene&gt; &lt;out.png&gt; [delayMs]</c>.
    ///
    /// Exists because a green test suite says nothing about whether a screen
    /// actually renders: FRONTEND_PLAN.md's Phase 5 notes record a cursor that
    /// was flatly invisible while every property read back correct, found only
    /// by screenshotting a running window. This automates that loop so it's
    /// cheap enough to do after every visual change rather than once at the end.
    /// </summary>
    public partial class ScreenshotHarness : osu.Framework.Game
    {
        /// <summary>
        /// Scenes this harness can render, by the name passed on the command
        /// line. A factory returning a <see cref="Screen"/> gets wrapped in a
        /// <see cref="ScreenStack"/>; anything else is added directly.
        /// </summary>
        public static readonly Dictionary<string, Func<Drawable>> Scenes =
            new Dictionary<string, Func<Drawable>>(StringComparer.OrdinalIgnoreCase)
            {
                ["main-menu"] = () => new Game.Screens.MainMenu.MainMenuScreen(),
                // The menu with its navigation strip open — the state a click
                // on the logo leads to, which a capture can't reach on its own.
                ["main-menu-expanded"] = () =>
                {
                    var screen = new Game.Screens.MainMenu.MainMenuScreen();
                    screen.OnLoadComplete += _ => screen.Expand();
                    return screen;
                },
                // The same state, but reached by a real click, inside the
                // tree the real game actually builds — cursor layer included.
                // The scene above calls Expand() directly and so cannot show
                // a click that never lands, or a strip that something else
                // is covering.
                ["main-menu-clicked"] = () =>
                {
                    var screen = new Game.Screens.MainMenu.MainMenuScreen();

                    var stack = new ScreenStack { RelativeSizeAxes = Axes.Both };

                    var input = new osu.Framework.Testing.Input.ManualInputManager
                    {
                        RelativeSizeAxes = Axes.Both,
                        Children = new Drawable[]
                        {
                            stack,
                            new Game.Graphics.Cursor.GameCursor(),
                        },
                    };

                    stack.Push(screen);

                    // Delayed rather than immediate: the click needs a laid
                    // out logo to aim at, and layout happens on the frame
                    // after load.
                    // Move, then click a frame later, then park the pointer.
                    // Moving and clicking in one frame hit-tests against the
                    // pointer's *previous* position, so the click lands on
                    // nothing and the capture silently shows a closed menu.
                    screen.OnLoadComplete += _ => input.Delay(600).Schedule(() => input.MoveMouseTo(logoOf(screen)));
                    screen.OnLoadComplete += _ => input.Delay(700).Schedule(() => input.Click(osuTK.Input.MouseButton.Left));
                    screen.OnLoadComplete += _ => input.Delay(900).Schedule(() => input.MoveMouseTo(input.ToScreenSpace(new osuTK.Vector2(40, 40))));

                    return input;
                },
                // The spectrum ring on a fixed signal. A capture of the real
                // menu shows whatever the song was doing in that millisecond,
                // which is no use for checking the ring's own geometry.
                ["menu-spectrum"] = () => new osu.Framework.Graphics.Containers.Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new osuTK.Vector2(620),
                    Child = new Game.Screens.MainMenu.SpectrumRing
                    {
                        RelativeSizeAxes = Axes.Both,
                        AmplitudeSource = () =>
                        {
                            var bins = new float[256];

                            // A slow ramp across the audible bins: every
                            // column should be lit, each a little taller
                            // than the one before it.
                            for (int bin = 1; bin <= 128; bin++)
                                bins[bin] = 0.05f + 0.30f * (bin / 128f);

                            return bins;
                        },
                    },
                },
                // Real spectral flux out of the committed dsp.json fixture,
                // with the Hard preset's threshold derived over it. The one
                // check a reduction unit test cannot make: does the path
                // render, the right way up, with the threshold visibly riding
                // the curve's local level?
                ["trace-graph"] = () => new TraceGraphScene(),
                // Every part of the studio-rack kit, in every state, on one
                // frame — four screens are built from these, so they have to
                // be comparable side by side.
                ["rack-kit"] = () => new RackKitScene(),
                // The running view, driven by the exact lines the backend
                // prints — so no Python has to run to capture it, and the
                // frame exercises the real parse path.
                // The tape deck, empty and loaded. The loaded one goes
                // through ChooseFile, the same path a real drop takes.
                ["upload-empty"] = () => UploadScene.Empty(),
                ["upload-loaded"] = () => UploadScene.Loaded(),
                ["upload-labelled"] = () => UploadScene.Labelled(),
                ["upload-with-cover"] = () => UploadScene.Covered(),
                // Gameplay of a beatmap folder on disk -- the first
                // difficulty in OSU_SCREENSHOT_SET -- for looking at what
                // the generator actually produced (slider shapes, say) as
                // the game draws it. Nobody is playing, so capture before
                // the first object would be missed.
                ["gameplay-folder"] = () =>
                {
                    string folder = Environment.GetEnvironmentVariable("OSU_SCREENSHOT_SET")
                                    ?? throw new InvalidOperationException("set OSU_SCREENSHOT_SET to a beatmap folder");
                    var set = Game.Beatmaps.OszImporter.LoadFromDirectory(folder);
                    var entry = new Game.Beatmaps.BeatmapLibraryEntry { Path = folder, Set = set };
                    return new Game.Screens.Gameplay.PlayerScreen(
                        new Game.Screens.SongSelect.BeatmapSelection(entry, set.Beatmaps[0]));
                },
                // The same folder, played by a simple autoplay (see
                // AutoplayScene), for judgements, the combo counter and a
                // spinner in motion.
                ["gameplay-autoplay"] = () => new AutoplayScene(
                    Environment.GetEnvironmentVariable("OSU_SCREENSHOT_SET")
                    ?? throw new InvalidOperationException("set OSU_SCREENSHOT_SET to a beatmap folder")),
                // The pause menu on its own, shown, with all three choices.
                ["pause-menu"] = () =>
                {
                    var overlay = new Game.Screens.Gameplay.PauseOverlay(null, "Ado - Show", "Hard", () => { }, () => { }, () => { });
                    overlay.OnLoadComplete += _ => overlay.Show();
                    return overlay;
                },
                // The deck with a real library song under it, muffled and
                // slowed as the menu hands it over — run on a real audio
                // device, which the headless tests do not have.
                ["upload-with-music"] = () => new Game.Screens.Generation.UploadScreen(null,
                    Game.Beatmaps.BeatmapLibrary.Load(Game.Beatmaps.BeatmapLibrary.ResolveDefaultSongsDirectory())
                        .Select(e => e.Set?.AudioPath).FirstOrDefault(p => p != null && System.IO.File.Exists(p)),
                    30_000),
                ["generation-running"] = () => new GenerationProgressScene(false),
                // The real generation screen, on the deck's wallpaper. Pointed
                // at a file that isn't there so the backend fails in a moment
                // and writes nothing into the song library.
                ["generation-screen"] = () =>
                {
                    var paths = Game.Backend.BackendPaths.Locate(Game.Backend.BackendPaths.FindRepositoryRoot(), out _)!;

                    return new Game.Screens.Generation.DspVisualizationScreen(paths,
                        new Game.Backend.GenerationRequest
                        {
                            AudioPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "no such tape.mp3"),
                            Difficulties = new[] { "Easy" },
                        },
                        null,
                        Game.Graphics.WallpaperLibrary.PickRandom(
                            Game.Graphics.WallpaperLibrary.Load(Game.Graphics.WallpaperLibrary.ResolveDefaultDirectory())));
                },
                ["generation-failed"] = () => new GenerationProgressScene(true),
                // The reveal, held at each stage. A timed sequence is the one
                // thing a single capture cannot catch on its own.
                ["reveal-stage1"] = () => new DspRevealScene(0),
                ["reveal-stage2"] = () => new DspRevealScene(1),
                ["reveal-stage3"] = () => new DspRevealScene(2),
                ["reveal-stage4"] = () => new DspRevealScene(3),
                ["reveal-stage5"] = () => new DspRevealScene(4),
                ["reveal-stage6"] = () => new DspRevealScene(5),
                ["reveal-stage7"] = () => new DspRevealScene(6),
                ["reveal-stage8"] = () => new DspRevealScene(7),
                ["reveal-stage9"] = () => new DspRevealScene(8),
                ["reveal-stage10"] = () => new DspRevealScene(9),
                ["reveal-stage11"] = () => new DspRevealScene(10),
                // The same stages on the newest generated map: five tiers,
                // where the fixture has two.
                ["reveal-latest-stage8"] = () => new DspRevealScene(7, latestTracedMap()),
                ["reveal-latest-stage9"] = () => new DspRevealScene(8, latestTracedMap()),
                // The analyser, against the committed fixture folder.
                ["inspector"] = () => new Game.Screens.Analysis.DspInspectorScreen(
                    System.IO.Path.Combine(
                        Game.Beatmaps.BeatmapLibrary.FindRepositoryRoot(AppContext.BaseDirectory)
                            ?? string.Empty,
                        "frontend", "OsuClient.Tests", "Fixtures")),
                // The same screen for a map that predates the trace — an
                // actual one out of data/output, not a contrived folder. This
                // is what every set generated before the feature looks like,
                // and the degradation has to be looked at, not assumed.
                ["inspector-v1-map"] = () => new Game.Screens.Analysis.DspInspectorScreen(
                    System.IO.Path.Combine(
                        Game.Beatmaps.BeatmapLibrary.FindRepositoryRoot(AppContext.BaseDirectory)
                            ?? string.Empty,
                        "data", "output", "The Chainsmokers - Closer")),
                // The newest generated map that has a trace, which is the
                // realistic case the fixture's two tiers can't show: five
                // tiers in a bottom row that only fits two.
                ["inspector-latest"] = () => new Game.Screens.Analysis.DspInspectorScreen(latestTracedMap()),
                // The playhead's pin as it looks under the cursor or held:
                // lit, with its time readout. A still frame cannot hover.
                ["inspector-pin-lit"] = () =>
                {
                    var screen = new Game.Screens.Analysis.DspInspectorScreen(latestTracedMap());
                    screen.OnLoadComplete += _ =>
                    {
                        screen.SeekToFraction(0.3);
                        screen.PlayheadPin.Grabbed = true;
                    };
                    return screen;
                },
                // The same, with the difficulty panel scrolled to its end.
                ["inspector-latest-scrolled"] = () =>
                {
                    var screen = new Game.Screens.Analysis.DspInspectorScreen(latestTracedMap());
                    // Held at the end every frame: at load the lanes have not
                    // been measured yet, so there is no end to scroll to.
                    screen.OnLoadComplete += _ =>
                    {
                        var scroll = screen.ChildrenOfType<Game.Graphics.Rack.RackScrollContainer>().Single();
                        scroll.OnUpdate += _ => scroll.ScrollToEnd(false);
                    };
                    return screen;
                },
                // A menu screen change, pressed after RippleScene.SettleTime:
                // capture at SettleTime + t to see t ms into the ripple.
                ["ripple-play"] = () => new RippleScene(play: true),
                ["ripple-create"] = () => new RippleScene(play: false),
                // Frozen 120 ms into the new screen's slide-in, however long
                // it took to load. Capture any time after that.
                ["ripple-play-arriving"] = () => new RippleScene(play: true, freezeAfterArrival: 120),
                ["ripple-create-arriving"] = () => new RippleScene(play: false, freezeAfterArrival: 120),
                ["ripple-play-sweep"] = () => new RippleScene(play: true, freezeAfterArrival: 280),
                ["ripple-create-sweep"] = () => new RippleScene(play: false, freezeAfterArrival: 280),
                // A results screen for a run that set a new best.
                ["results-new-best"] = () => new Game.Screens.Results.ResultsScreen(new Game.Screens.Results.ResultsScreen.Result
                {
                    Title = "abo - shaw",
                    Difficulty = "Hard",
                    Grade = Game.Screens.Gameplay.Grade.S,
                    Score = 1_284_630,
                    Accuracy = 97.84,
                    MaxCombo = 412,
                    CountGreat = 420,
                    CountOk = 12,
                    CountMeh = 3,
                    CountMiss = 1,
                    Failed = false,
                    NewHighScore = true,
                    PreviousBest = 1_102_455,
                }),
                // Choosing a song: the record lifting off the wheel and
                // gliding in, frozen at a moment of it, then landed, then
                // played into the ripple.
                ["play-cue-lift"] = () => new PlayCueScene(150),
                ["play-cue-glide"] = () => new PlayCueScene(500),
                ["play-cue-landed"] = () => new PlayCueScene(1300),
                ["play-cue-launch"] = () => new PlayCueScene(300, PlayCueScene.Then.Launch),
                ["play-cue-escape"] = () => new PlayCueScene(550, PlayCueScene.Then.Escape),
                ["retro-song-select"] = () => new Game.Screens.SongSelect.RetroSongSelectScreen(),
                // With a best on the selected difficulty, to see the panel's
                // readout filled in rather than waiting for a first clear.
                ["retro-song-select-highscore"] = () =>
                {
                    var screen = new Game.Screens.SongSelect.RetroSongSelectScreen();
                    screen.OnLoadComplete += _ => screen.ChildrenOfType<Game.Screens.SongSelect.SongInfoPanel>().Single()
                        .SetHighScore(new Game.Scores.HighScore
                        {
                            Score = 1_284_630,
                            Accuracy = 97.84,
                            MaxCombo = 412,
                            Grade = Game.Screens.Gameplay.Grade.S,
                        });
                    return screen;
                },
                // Same screen, but stepped three songs on before the capture,
                // so a still frame can show that turning the record actually
                // moves the selection through every panel.
                ["retro-song-select-turned"] = () =>
                {
                    var screen = new Game.Screens.SongSelect.RetroSongSelectScreen();
                    screen.OnLoadComplete += _ => screen.SelectRelative(3);
                    return screen;
                },
                // Search focused and narrowed to a single match — the one
                // state that exercises the focus glow, the caret, and a wheel
                // holding one song, which used to scale that song's wedge
                // over the platter because the whole disc *was* the wedge.
                ["retro-song-select-searching"] = () =>
                {
                    var screen = new Game.Screens.SongSelect.RetroSongSelectScreen();
                    screen.OnLoadComplete += _ => screen.Search("eden");
                    return screen;
                },
            };

        private static Game.Screens.MainMenu.MenuLogo logoOf(Drawable screen) =>
            screen.ChildrenOfType<Game.Screens.MainMenu.MenuLogo>().Single();

        private readonly Func<Drawable> createContent;
        private readonly string outputPath;
        private readonly double delayMs;
        private readonly System.Drawing.Size windowSize;

        private bool captured;

        public ScreenshotHarness(Func<Drawable> createContent, string outputPath, double delayMs, System.Drawing.Size windowSize)
        {
            this.createContent = createContent;
            this.outputPath = outputPath;
            this.delayMs = delayMs;
            this.windowSize = windowSize;
        }

        [BackgroundDependencyLoader]
        private void load(FrameworkConfigManager config)
        {
            config.SetValue(FrameworkSetting.WindowMode, WindowMode.Windowed);
            config.SetValue(FrameworkSetting.WindowedSize, windowSize);

            var content = createContent();

            if (content is Screen screen)
            {
                var stack = new ScreenStack { RelativeSizeAxes = Axes.Both };
                Add(stack);
                stack.Push(screen);
            }
            else
            {
                Add(content);
            }
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // Textures and the rasterized retro fonts both load asynchronously,
            // so an immediate capture reliably catches a half-built frame.
            Scheduler.AddDelayed(capture, delayMs);
        }

        private void capture()
        {
            if (captured)
                return;

            captured = true;

            Task.Run(async () =>
            {
                try
                {
                    using var image = await Host.TakeScreenshotAsync().ConfigureAwait(false);

                    string? directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));

                    if (directory != null)
                        Directory.CreateDirectory(directory);

                    await image.SaveAsPngAsync(outputPath).ConfigureAwait(false);

                    Console.WriteLine($"screenshot written: {Path.GetFullPath(outputPath)}");
                }
                catch (Exception e)
                {
                    Console.WriteLine($"screenshot failed: {e}");
                }
                finally
                {
                    Schedule(() => Host.Exit());
                }
            });
        }

        /// <summary>
        /// Command-line entry, called from <see cref="VisualTestRunner"/> when
        /// the first argument is <c>--screenshot</c>. Returns a process exit
        /// code.
        /// </summary>
        /// <summary>The newest folder in data/output with a dsp.json, or an empty path when there is none.</summary>
        private static string latestTracedMap()
        {
            string? root = Game.Beatmaps.BeatmapLibrary.FindRepositoryRoot(AppContext.BaseDirectory);
            string output = System.IO.Path.Combine(root ?? string.Empty, "data", "output");

            if (!System.IO.Directory.Exists(output))
                return string.Empty;

            return new System.IO.DirectoryInfo(output).EnumerateDirectories()
                                                     .Where(d => System.IO.File.Exists(System.IO.Path.Combine(d.FullName, "dsp.json")))
                                                     .OrderByDescending(d => d.LastWriteTimeUtc)
                                                     .Select(d => d.FullName)
                                                     .FirstOrDefault() ?? string.Empty;
        }

        public static int Run(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("usage: --screenshot <scene> <out.png> [delayMs] [width] [height]");
                Console.WriteLine($"scenes: {string.Join(", ", Scenes.Keys)}");
                return 1;
            }

            string scene = args[1];
            string output = args[2];

            if (!Scenes.TryGetValue(scene, out var factory))
            {
                Console.WriteLine($"unknown scene \"{scene}\"; known: {string.Join(", ", Scenes.Keys)}");
                return 1;
            }

            double delay = args.Length > 3 && double.TryParse(args[3], out double parsed) ? parsed : 2500;
            int width = args.Length > 4 && int.TryParse(args[4], out int w) ? w : 1920;
            int height = args.Length > 5 && int.TryParse(args[5], out int h) ? h : 1080;

            // Both names are fully qualified: unqualified "Host" binds to
            // Game.Host (this class's own base-class property), and "Game"
            // would be ambiguous between osu.Framework.Game and the
            // OsuClient.Game namespace the scene factories reach through.
            using DesktopGameHost host = osu.Framework.Host.GetSuitableDesktopHost(
                @"osu-client-screenshot",
                new osu.Framework.HostOptions { PortableInstallation = true });

            host.Run(new ScreenshotHarness(factory, output, delay, new System.Drawing.Size(width, height)));

            return 0;
        }
    }
}
