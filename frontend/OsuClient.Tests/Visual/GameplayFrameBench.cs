using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Configuration;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Platform;

namespace OsuClient.Tests.Visual
{
    /// <summary>
    /// Frame pacing during autoplayed gameplay in a real window —
    /// <c>dotnet run --project frontend/OsuClient.Tests -c Release --
    /// --bench-gameplay &lt;set folder&gt; [seconds]</c>.
    ///
    /// Plays the folder's first difficulty through <see cref="AutoplayScene"/>
    /// and times every frame the draw thread renders, reporting how many
    /// missed a 60 Hz refresh (over 20 ms) and the worst ones. A regression
    /// check for anything that adds drawing to the game screen.
    /// </summary>
    public partial class GameplayFrameBench : osu.Framework.Game
    {
        private static readonly Stopwatch clock = Stopwatch.StartNew();

        private readonly string folder;
        private readonly double seconds;

        private Probe probe = null!;
        private AutoplayScene scene = null!;
        private double gameplayAtStart;

        public GameplayFrameBench(string folder, double seconds)
        {
            this.folder = folder;
            this.seconds = seconds;
        }

        [BackgroundDependencyLoader]
        private void load(FrameworkConfigManager config)
        {
            config.SetValue(FrameworkSetting.WindowMode, WindowMode.Windowed);
            config.SetValue(FrameworkSetting.WindowedSize, new System.Drawing.Size(1920, 1080));

            Add(scene = new AutoplayScene(folder));
            Add(probe = new Probe());

            // Past loading, so only gameplay is measured.
            Scheduler.AddDelayed(() =>
            {
                gameplayAtStart = scene.GameplayTime;
                probe.Recording = true;
            }, 1500);
            Scheduler.AddDelayed(report, 1500 + seconds * 1000);
        }

        private void report()
        {
            probe.Recording = false;
            var frames = probe.Frames.ToArray();

            // When the slow frames happened, from the start of recording.
            double at = 0;
            var slow = new System.Collections.Generic.List<string>();

            foreach (double f in frames)
            {
                at += f;
                if (f > 20) slow.Add($"{at:F0}:{f:F0}");
            }

            Console.WriteLine($"bench: recording began at gameplay time {gameplayAtStart:F0} ms");
            Console.WriteLine($"bench: slow frames (ms into recording:frame ms) {string.Join(", ", slow)}");
            var sorted = frames.OrderBy(f => f).ToArray();

            if (sorted.Length == 0)
            {
                Console.WriteLine("bench: no frames");
            }
            else
            {
                double pct(double p) => sorted[Math.Min(sorted.Length - 1, (int)(p * sorted.Length))];

                Console.WriteLine($"bench: draw frames={sorted.Length} p50={pct(0.5):F2} p99={pct(0.99):F2} max={sorted[^1]:F1} ms  "
                                  + $">20ms={frames.Count(f => f > 20)} >33ms={frames.Count(f => f > 33)}");
            }

            Host.Exit();
        }

        private partial class Probe : Box
        {
            public volatile bool Recording;
            public readonly ConcurrentQueue<double> Frames = new ConcurrentQueue<double>();
            private double last = -1;

            public Probe()
            {
                Size = new osuTK.Vector2(1);
                Colour = osuTK.Graphics.Color4.Black;
            }

            protected override DrawNode CreateDrawNode() => new ProbeNode(this);

            private void onDraw()
            {
                double now = clock.Elapsed.TotalMilliseconds;

                if (Recording && last >= 0)
                    Frames.Enqueue(now - last);

                last = now;
            }

            private class ProbeNode : SpriteDrawNode
            {
                private readonly Probe probe;

                public ProbeNode(Probe source)
                    : base(source)
                {
                    probe = source;
                }

                protected override void Draw(IRenderer renderer)
                {
                    base.Draw(renderer);
                    probe.onDraw();
                }
            }
        }

        public static int Run(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("usage: --bench-gameplay <set folder> [seconds]");
                return 1;
            }

            double seconds = args.Length > 2 && double.TryParse(args[2], out double s) ? s : 13;

            using DesktopGameHost host = osu.Framework.Host.GetSuitableDesktopHost(
                @"osu-client-gameplay-bench",
                new osu.Framework.HostOptions { PortableInstallation = true });

            host.Run(new GameplayFrameBench(args[1], seconds));
            return 0;
        }
    }
}
