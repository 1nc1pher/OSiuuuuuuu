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
                ["retro-song-select"] = () => new Game.Screens.SongSelect.RetroSongSelectScreen(),
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
