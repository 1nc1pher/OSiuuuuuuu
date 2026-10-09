using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Configuration;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Platform;
using osu.Framework.Timing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OsuClient.Tests.Visual
{
    /// <summary>
    /// Renders autoplayed gameplay to a video, frame by frame —
    /// <c>dotnet run --project frontend/OsuClient.Tests -c Release --
    /// --record-gameplay &lt;set folder&gt; &lt;ffmpeg.exe&gt; &lt;out.mp4&gt; [seconds] [fps]</c>.
    ///
    /// A real-time screen capture would drop frames on this machine, and a
    /// dropped frame is a late hit. So nothing here runs on wall time: the
    /// whole scene is given a clock that only moves when the recorder steps
    /// it by exactly one video frame, the game is left to draw that frame,
    /// and the frame is read back and piped to ffmpeg. The track is left out
    /// of the copy of the beatmap the game plays, so the gameplay clock
    /// follows the stepped clock rather than the audio device; the music is
    /// muxed in afterwards from the original file.
    /// </summary>
    public partial class GameplayRecorder : osu.Framework.Game
    {
        /// <summary>Real update frames the clock is held still after a step, so the draw thread has caught up before the read-back.</summary>
        private const int settle_frames = 4;

        /// <summary>Video frames' worth of clock to run, unrecorded, once the player exists.</summary>
        private const int warmup_frames = 30;

        private int warmupFrames;
        private int lastOk, lastMeh, lastMiss;

        private readonly string folder;
        private readonly string ffmpegPath;
        private readonly string outputPath;
        private readonly int totalFrames;
        private readonly int fps;

        private readonly ManualClock manual = new ManualClock();
        private AutoplayScene scene = null!;

        private Process? ffmpeg;
        private Stream? pipe;
        private byte[] buffer = Array.Empty<byte>();

        private int framesWritten;
        private int settle;
        private bool capturing;
        private bool finished;
        private double firstGameplayTime = double.NaN;

        public GameplayRecorder(string folder, string ffmpegPath, string outputPath, double seconds, int fps)
        {
            this.folder = folder;
            this.ffmpegPath = ffmpegPath;
            this.outputPath = outputPath;
            this.fps = fps;
            totalFrames = (int)Math.Round(seconds * fps);
        }

        [BackgroundDependencyLoader]
        private void load(FrameworkConfigManager config)
        {
            config.SetValue(FrameworkSetting.WindowMode, WindowMode.Windowed);
            config.SetValue(FrameworkSetting.WindowedSize, new System.Drawing.Size(1920, 1080));

            // The copy of the set the game plays has no audio file, so the
            // player never starts a track and its clock is ours alone.
            string silent = Path.Combine(Path.GetTempPath(), "osu-client-record-" + Path.GetFileName(folder.TrimEnd('/', '\\')));
            if (Directory.Exists(silent)) Directory.Delete(silent, true);
            Directory.CreateDirectory(silent);

            foreach (string file in Directory.GetFiles(folder))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();

                if (ext is ".mp3" or ".ogg" or ".wav")
                    continue;

                // One difficulty only (the autoplay plays the first): the
                // one named by OSU_RECORD_DIFFICULTY, e.g. "Hard".
                string? difficulty = Environment.GetEnvironmentVariable("OSU_RECORD_DIFFICULTY");

                if (ext == ".osu" && difficulty != null && !file.Contains($"[{difficulty}]"))
                    continue;

                File.Copy(file, Path.Combine(silent, Path.GetFileName(file)));
            }

            manual.IsRunning = true;

            Add(new Container
            {
                RelativeSizeAxes = Axes.Both,
                Clock = new FramedClock(manual, false),
                Child = scene = new AutoplayScene(silent),
            });
        }

        protected override void Update()
        {
            base.Update();

            if (finished || capturing)
                return;

            // Until the player exists, let the scene's loading run at a
            // nominal frame rate.
            if (double.IsNaN(scene.GameplayTime))
            {
                manual.CurrentTime += 1000.0 / fps;
                return;
            }

            if (settle > 0)
            {
                settle--;
                return;
            }

            // The screen fades in from white as it arrives; let that pass
            // (clock running, nothing recorded) before the first frame.
            if (warmupFrames < warmup_frames)
            {
                warmupFrames++;
                manual.CurrentTime += 1000.0 / fps;
                return;
            }

            if (double.IsNaN(firstGameplayTime))
            {
                // Hold a few frames on the first step too, then record.
                firstGameplayTime = scene.GameplayTime;
                settle = settle_frames;
                return;
            }

            capturing = true;
            Task.Run(captureFrame);
        }

        private async Task captureFrame()
        {
            try
            {
                using var image = await Host.TakeScreenshotAsync().ConfigureAwait(false);

                if (ffmpeg == null)
                    startEncoder(image.Width, image.Height);

                image.CopyPixelDataTo(buffer);
                await pipe!.WriteAsync(buffer).ConfigureAwait(false);

                if (framesWritten == 0 || framesWritten == totalFrames / 2)
                    await image.SaveAsPngAsync(Path.ChangeExtension(outputPath, $".frame{framesWritten}.png")).ConfigureAwait(false);

                framesWritten++;

                var state = scene.ScoreState;
                if (state.CountOk != lastOk || state.CountMeh != lastMeh || state.CountMiss != lastMiss)
                {
                    Console.WriteLine($"record: non-300 at gameplay {scene.GameplayTime:F0}: ok={state.CountOk} meh={state.CountMeh} miss={state.CountMiss}");
                    lastOk = state.CountOk;
                    lastMeh = state.CountMeh;
                    lastMiss = state.CountMiss;
                }

                if (framesWritten % 120 == 0)
                    Console.WriteLine($"record: {framesWritten}/{totalFrames} frames, gameplay time {scene.GameplayTime:F0} ms");

                if (framesWritten >= totalFrames)
                {
                    finish();
                    return;
                }

                Schedule(() =>
                {
                    manual.CurrentTime += 1000.0 / fps;
                    settle = settle_frames;
                    capturing = false;
                });
            }
            catch (Exception e)
            {
                Console.WriteLine($"record failed: {e}");
                finish();
            }
        }

        private void startEncoder(int width, int height)
        {
            buffer = new byte[width * height * 4];

            ffmpeg = Process.Start(new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = $"-y -loglevel error -f rawvideo -pix_fmt rgba -s {width}x{height} -r {fps} -i - "
                            + "-c:v libx264 -preset medium -crf 16 -pix_fmt yuv420p -movflags +faststart "
                            + $"\"{outputPath}\"",
                RedirectStandardInput = true,
                UseShellExecute = false,
            })!;

            pipe = ffmpeg.StandardInput.BaseStream;
        }

        private void finish()
        {
            finished = true;

            pipe?.Flush();
            pipe?.Dispose();
            ffmpeg?.WaitForExit();

            var score = scene.ScoreState;
            Console.WriteLine($"record: first frame at gameplay time {firstGameplayTime:F1} ms, last at {scene.GameplayTime:F1} ms");
            Console.WriteLine($"record: judged={score.TotalJudged} great={score.CountGreat} ok={score.CountOk} meh={score.CountMeh} miss={score.CountMiss} "
                              + $"combo={score.Combo} accuracy={score.Accuracy:F2}");
            Console.WriteLine($"record: wrote {Path.GetFullPath(outputPath)}");

            Schedule(() => Host.Exit());
        }

        public static int Run(string[] args)
        {
            if (args.Length < 4)
            {
                Console.WriteLine("usage: --record-gameplay <set folder> <ffmpeg.exe> <out.mp4> [seconds] [fps]");
                return 1;
            }

            double seconds = args.Length > 4 && double.TryParse(args[4], out double s) ? s : 30;
            int fps = args.Length > 5 && int.TryParse(args[5], out int f) ? f : 60;

            using DesktopGameHost host = osu.Framework.Host.GetSuitableDesktopHost(
                @"osu-client-gameplay-record",
                new osu.Framework.HostOptions { PortableInstallation = true });

            host.Run(new GameplayRecorder(args[1], args[2], args[3], seconds, fps));
            return 0;
        }
    }
}
