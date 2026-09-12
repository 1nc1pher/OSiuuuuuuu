using System;
using System.IO;
using osu.Framework;
using osu.Framework.Platform;
using OsuClient.Game;
using OsuClient.Game.Beatmaps;

namespace OsuClient.Desktop
{
    /// <summary>
    /// Thin executable host. All game logic lives in OsuClient.Game so the
    /// test project can load the same code without a desktop window.
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            // Phase 1 verification hook, as called for by FRONTEND_PLAN.md:
            // "Load one of the backend's generated maps and print the parsed
            // object list to the console."
            if (args.Length >= 2 && args[0] == "--dump")
                return Dump(args[1]);

            // --songs <dir> overrides where song select looks for beatmaps.
            string? songsDirectory = null;

            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--songs")
                    songsDirectory = args[i + 1];
            }

            using (GameHost host = Host.GetSuitableDesktopHost(@"osu-client"))
            using (osu.Framework.Game game = new OsuClientGame(songsDirectory))
                host.Run(game);

            return 0;
        }

        /// <summary>
        /// Decodes an .osz, a beatmap folder or a single .osu and prints what
        /// came out. No window, no audio — just the import layer.
        /// </summary>
        private static int Dump(string path)
        {
            try
            {
                if (File.Exists(path) && path.EndsWith(".osu", StringComparison.OrdinalIgnoreCase))
                {
                    PrintBeatmap(BeatmapDecoder.DecodeFile(path));
                    return 0;
                }

                BeatmapSet set = Directory.Exists(path)
                    ? OszImporter.LoadFromDirectory(path)
                    : OszImporter.ReadSet(path);

                Console.WriteLine($"Set: {set.Name}");
                Console.WriteLine($"Audio: {set.AudioFilename} (present: {set.HasAudio})");
                Console.WriteLine($"Difficulties: {set.Beatmaps.Count}");
                Console.WriteLine();

                foreach (var beatmap in set.Beatmaps)
                    PrintBeatmap(beatmap);

                return 0;
            }
            catch (Exception e) when (e is BeatmapDecodeException or OszImportException
                                          or IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"error: {e.Message}");
                return 1;
            }
        }

        private static void PrintBeatmap(Beatmap beatmap)
        {
            Console.WriteLine($"=== {beatmap} ===");
            Console.WriteLine($"  AR {beatmap.Difficulty.ApproachRate}  " +
                              $"CS {beatmap.Difficulty.CircleSize}  " +
                              $"OD {beatmap.Difficulty.OverallDifficulty}  " +
                              $"HP {beatmap.Difficulty.HPDrainRate}  " +
                              $"SV {beatmap.Difficulty.SliderMultiplier}");

            foreach (var timingPoint in beatmap.TimingPoints)
                Console.WriteLine($"  {timingPoint}");

            foreach (var hitObject in beatmap.HitObjects)
                Console.WriteLine($"    {hitObject}");

            Console.WriteLine();
        }
    }
}
