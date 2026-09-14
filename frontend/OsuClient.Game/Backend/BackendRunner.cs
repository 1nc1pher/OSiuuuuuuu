using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OsuClient.Game.Backend
{
    /// <summary>What to generate: one audio file, and the metadata to stamp on it.</summary>
    public sealed class GenerationRequest
    {
        public required string AudioPath { get; init; }

        /// <summary>Blank falls back to the backend's own default ("unknown artist").</summary>
        public string Artist { get; init; } = string.Empty;

        /// <summary>Blank falls back to the backend's own default (the file name).</summary>
        public string Title { get; init; } = string.Empty;

        /// <summary>Empty runs every tier, matching the CLI's own default.</summary>
        public IReadOnlyList<string> Difficulties { get; init; } = Array.Empty<string>();
    }

    /// <summary>How a run ended, and where it left the map if it worked.</summary>
    public sealed class GenerationResult
    {
        public required bool Success { get; init; }
        public required int ExitCode { get; init; }

        /// <summary>The beatmap folder the run reported, when it reported one.</summary>
        public string? BeatmapFolder { get; init; }

        /// <summary>The packaged <c>.osz</c> the run reported, when it reported one.</summary>
        public string? OszPath { get; init; }

        /// <summary>Everything the process printed, for showing a failure to the player.</summary>
        public string Output { get; init; } = string.Empty;
    }

    /// <summary>
    /// Runs the backend's own pipeline as a child process (FRONTEND_PLAN.md
    /// Phase 6). Nothing about the DSP is reimplemented here — this is the
    /// in-app equivalent of typing <c>python src/main.py song.mp3</c>, which
    /// is the same entry point the manual workflow has always used.
    ///
    /// The interesting logic is deliberately kept out of the process call:
    /// <see cref="BuildArguments"/> and <see cref="ParseOutput"/> are pure and
    /// tested directly, so the parts that can silently mangle a path or miss a
    /// result don't need Python running to verify.
    /// </summary>
    public sealed class BackendRunner
    {
        private readonly BackendPaths paths;

        public BackendRunner(BackendPaths paths)
        {
            this.paths = paths;
        }

        /// <summary>
        /// The argument list for one request.
        ///
        /// Built as a list rather than a command string so
        /// <see cref="ProcessStartInfo.ArgumentList"/> can do the quoting:
        /// song files routinely have spaces, quotes and brackets in their
        /// names, and hand-joining them into one string is exactly where that
        /// goes wrong.
        /// </summary>
        public static IReadOnlyList<string> BuildArguments(BackendPaths paths, GenerationRequest request)
        {
            var arguments = new List<string> { paths.MainScript, request.AudioPath };

            if (!string.IsNullOrWhiteSpace(request.Artist))
            {
                arguments.Add("--artist");
                arguments.Add(request.Artist.Trim());
            }

            if (!string.IsNullOrWhiteSpace(request.Title))
            {
                arguments.Add("--title");
                arguments.Add(request.Title.Trim());
            }

            // Left off entirely when empty: main.py's own default is every
            // tier, so passing an empty --difficulties would mean "none".
            if (request.Difficulties.Count > 0)
            {
                arguments.Add("--difficulties");
                arguments.Add(string.Join(",", request.Difficulties));
            }

            return arguments;
        }

        /// <summary>
        /// Picks the output paths out of what the run printed.
        ///
        /// <c>main.py</c> ends with "Beatmap folder: ..." and, when it packaged
        /// one, "Importable set: ... (NN KB)". Both are best-effort: a run is
        /// judged by its exit code, and the library rescan afterwards doesn't
        /// depend on either path being found.
        /// </summary>
        public static (string? BeatmapFolder, string? OszPath) ParseOutput(string output)
        {
            string? folder = null;
            string? osz = null;

            foreach (string line in output.Split('\n'))
            {
                string trimmed = line.Trim();

                if (trimmed.StartsWith("Beatmap folder:", StringComparison.Ordinal))
                    folder = trimmed["Beatmap folder:".Length..].Trim();

                if (trimmed.StartsWith("Importable set:", StringComparison.Ordinal))
                {
                    string rest = trimmed["Importable set:".Length..].Trim();

                    // The line carries a trailing "(123 KB)" size the path
                    // itself has no business keeping.
                    int size = rest.LastIndexOf(" (", StringComparison.Ordinal);
                    osz = size > 0 ? rest[..size].Trim() : rest;
                }
            }

            return (emptyToNull(folder), emptyToNull(osz));
        }

        private static string? emptyToNull(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value;

        /// <summary>
        /// Runs one generation to completion.
        ///
        /// <paramref name="onOutputLine"/> is called for every line the process
        /// prints, <b>from a background thread</b> — a caller putting those on
        /// screen has to marshal them onto the update thread itself.
        /// </summary>
        public async Task<GenerationResult> RunAsync(GenerationRequest request,
                                                     Action<string>? onOutputLine = null,
                                                     CancellationToken cancellationToken = default)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = paths.PythonExecutable,
                WorkingDirectory = paths.RepositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (string argument in BuildArguments(paths, request))
                startInfo.ArgumentList.Add(argument);

            // Python buffers stdout when it isn't writing to a terminal, which
            // would hold every progress line back until the run finished — the
            // opposite of what a progress screen is for.
            startInfo.Environment["PYTHONUNBUFFERED"] = "1";

            var output = new StringBuilder();
            var outputLock = new object();

            void record(string? line)
            {
                if (line == null)
                    return;

                lock (outputLock)
                    output.AppendLine(line);

                onOutputLine?.Invoke(line);
            }

            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

            process.OutputDataReceived += (_, e) => record(e.Data);
            process.ErrorDataReceived += (_, e) => record(e.Data);

            try
            {
                process.Start();
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                return new GenerationResult
                {
                    Success = false,
                    ExitCode = -1,
                    Output = $"Couldn't start {paths.PythonExecutable}: {e.Message}",
                };
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                tryKill(process);
                throw;
            }

            string text;

            lock (outputLock)
                text = output.ToString();

            var (folder, osz) = ParseOutput(text);

            return new GenerationResult
            {
                Success = process.ExitCode == 0,
                ExitCode = process.ExitCode,
                BeatmapFolder = folder,
                OszPath = osz,
                Output = text,
            };
        }

        private static void tryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (Exception e) when (e is InvalidOperationException or NotSupportedException
                                          or System.ComponentModel.Win32Exception)
            {
                // Already gone, or the OS won't let us — either way there's
                // nothing left to do about it here.
            }
        }

        /// <summary>Audio extensions the backend's loader handles (librosa via audioread/soundfile).</summary>
        public static readonly string[] SupportedAudioExtensions = { ".mp3", ".wav", ".ogg", ".flac" };

        /// <summary>Whether a dropped or picked file is worth handing to the backend at all.</summary>
        public static bool IsSupportedAudioFile(string path) =>
            !string.IsNullOrWhiteSpace(path)
            && SupportedAudioExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    }
}
