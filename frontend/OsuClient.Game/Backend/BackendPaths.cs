using System;
using System.IO;
using OsuClient.Game.Beatmaps;

namespace OsuClient.Game.Backend
{
    /// <summary>
    /// Where the Python half of the project lives, as seen from this build.
    ///
    /// The frontend shells out to the backend rather than reimplementing any
    /// of it (FRONTEND_PLAN.md §4, Phase 6), so it needs three things: an
    /// interpreter with the backend's dependencies installed, the pipeline
    /// entry point, and the directory generated sets land in.
    ///
    /// Pure path logic on purpose — no process is started here, so the whole
    /// "did we find a usable backend" question is testable against a fake
    /// directory tree without Python being involved at all.
    /// </summary>
    public sealed class BackendPaths
    {
        /// <summary>The repository checkout holding both halves of the project.</summary>
        public string RepositoryRoot { get; }

        /// <summary>Interpreter to run <see cref="MainScript"/> with.</summary>
        public string PythonExecutable { get; }

        /// <summary>The pipeline entry point, <c>src/main.py</c>.</summary>
        public string MainScript { get; }

        /// <summary>Where a finished run leaves its beatmap folder and <c>.osz</c>.</summary>
        public string OutputDirectory { get; }

        private BackendPaths(string repositoryRoot, string pythonExecutable, string mainScript, string outputDirectory)
        {
            RepositoryRoot = repositoryRoot;
            PythonExecutable = pythonExecutable;
            MainScript = mainScript;
            OutputDirectory = outputDirectory;
        }

        /// <summary>
        /// Virtual environment directories to look in, in order.
        ///
        /// <c>.venv</c> comes first deliberately: this repository also carries
        /// an older <c>venv/</c> that is no longer the one dependencies are
        /// installed into, and silently picking it would fail deep inside an
        /// import rather than here. Only reached when <c>.venv</c> is absent
        /// entirely, which is the case for a checkout set up the other way
        /// round.
        /// </summary>
        private static readonly string[] virtual_environment_directories = { ".venv", "venv" };

        /// <summary>
        /// Finds the repository this build is running from inside of, or null
        /// for a copy installed elsewhere. Shares
        /// <see cref="BeatmapLibrary.FindRepositoryRoot"/>'s marker-file walk,
        /// so the songs directory and the backend can never disagree about
        /// which checkout is "the" one.
        /// </summary>
        public static string? FindRepositoryRoot() =>
            BeatmapLibrary.FindRepositoryRoot(AppContext.BaseDirectory);

        /// <summary>
        /// Resolves the backend inside <paramref name="repositoryRoot"/>.
        ///
        /// Returns null — with a reason written for a player to read, not a
        /// stack trace — when the checkout can't be found, has no virtual
        /// environment, or is missing the pipeline script. "Not set up" is an
        /// ordinary state here (an installed copy with no Python beside it),
        /// not an exception.
        /// </summary>
        public static BackendPaths? Locate(string? repositoryRoot, out string error)
        {
            if (string.IsNullOrWhiteSpace(repositoryRoot) || !Directory.Exists(repositoryRoot))
            {
                error = "Couldn't find the project checkout this build came from, "
                        + "so there's no beatmap generator to run.";
                return null;
            }

            string mainScript = Path.Combine(repositoryRoot, "src", "main.py");

            if (!File.Exists(mainScript))
            {
                error = $"The generator script is missing: {mainScript}";
                return null;
            }

            string? python = findInterpreter(repositoryRoot);

            if (python == null)
            {
                error = "No Python virtual environment found in the project "
                        + $"({string.Join(" or ", virtual_environment_directories)} in {repositoryRoot}). "
                        + "Set one up with the backend's requirements.txt first.";
                return null;
            }

            error = string.Empty;

            return new BackendPaths(
                repositoryRoot,
                python,
                mainScript,
                Path.Combine(repositoryRoot, "data", "output"));
        }

        /// <summary>
        /// The interpreter inside a checkout's virtual environment, or null if
        /// none of the expected layouts are present. Windows puts it in
        /// <c>Scripts/python.exe</c>, everything else in <c>bin/python</c>.
        /// </summary>
        private static string? findInterpreter(string repositoryRoot)
        {
            foreach (string environment in virtual_environment_directories)
            {
                string candidate = OperatingSystem.IsWindows()
                    ? Path.Combine(repositoryRoot, environment, "Scripts", "python.exe")
                    : Path.Combine(repositoryRoot, environment, "bin", "python");

                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }
    }
}
