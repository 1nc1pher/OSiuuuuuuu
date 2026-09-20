using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OsuClient.Game.Beatmaps
{
    /// <summary>
    /// One discovered item in the songs directory: either a loaded beatmap set,
    /// or a record of why that file couldn't be loaded.
    ///
    /// A failed entry is deliberately kept rather than dropped — song select
    /// shows it as a broken panel so a corrupt .osz is visible instead of
    /// silently missing.
    /// </summary>
    public class BeatmapLibraryEntry
    {
        /// <summary>Absolute path to the .osz file or beatmap folder.</summary>
        public string Path { get; init; } = string.Empty;

        /// <summary>The loaded set, or null if loading failed.</summary>
        public BeatmapSet? Set { get; init; }

        /// <summary>Why loading failed, or null on success.</summary>
        public string? Error { get; init; }

        public bool IsValid => Set != null && Error == null;

        /// <summary>
        /// "Artist - Title" from the first difficulty's metadata, falling back
        /// to the file name when the set is broken or its metadata is empty.
        /// </summary>
        public string DisplayName
        {
            get
            {
                var first = Set?.Beatmaps.FirstOrDefault();

                if (first != null)
                {
                    string artist = first.Metadata.Artist;
                    string title = first.Metadata.Title;

                    if (!string.IsNullOrWhiteSpace(artist) || !string.IsNullOrWhiteSpace(title))
                        return $"{artist} - {title}".Trim(' ', '-');
                }

                return System.IO.Path.GetFileNameWithoutExtension(Path);
            }
        }

        /// <summary>Difficulties in the set, empty for a failed entry.</summary>
        public IReadOnlyList<Beatmap> Difficulties => Set?.Beatmaps ?? Array.Empty<Beatmap>();

        /// <summary>
        /// Absolute path to the set's cover image, or null when it has none.
        ///
        /// Only sets loaded from an unpacked folder can have one — a set read
        /// straight out of a <c>.osz</c> never extracts to disk, so there's no
        /// path to hand out. Song select treats null as "draw the procedural
        /// fallback", not as an error.
        /// </summary>
        public string? BackgroundPath => Set?.BackgroundPath;

        public override string ToString() =>
            IsValid ? $"{DisplayName} ({Difficulties.Count} difficulties)" : $"{DisplayName} (failed: {Error})";
    }

    /// <summary>
    /// Finds beatmaps on disk.
    ///
    /// Per FRONTEND_PLAN.md §4, the frontend and backend stay decoupled at the
    /// file level: the backend writes <c>data/output/&lt;Artist&gt; - &lt;Song&gt;.osz</c>
    /// and the frontend just reads a directory. By default that directory IS
    /// the backend's <c>data/output/</c>, so freshly generated maps appear in
    /// song select with no copy step.
    /// </summary>
    public static class BeatmapLibrary
    {
        /// <summary>Overrides the songs directory when set.</summary>
        public const string SongsDirectoryEnvironmentVariable = "OSUCLIENT_SONGS_DIR";

        /// <summary>
        /// The set name a path refers to: the folder's own name, or the
        /// <c>.osz</c>'s name without the extension.
        ///
        /// Exists because the two sides name the same set differently. The
        /// backend reports the folder it wrote (<c>…/Artist - Title</c>),
        /// while <see cref="Load"/> lists that set by its <c>.osz</c>
        /// (<c>…/Artist - Title.osz</c>) and skips the folder as a duplicate.
        /// Comparing full paths therefore silently never matches — the only
        /// symptom being song select sitting on the wrong song — so anything
        /// resolving "which set is this path" compares these instead.
        /// </summary>
        public static string SetNameOf(string path)
        {
            string trimmed = path.TrimEnd(System.IO.Path.DirectorySeparatorChar,
                                          System.IO.Path.AltDirectorySeparatorChar);
            string name = System.IO.Path.GetFileName(trimmed);

            // Only the packaging extension is stripped: a set titled
            // "Song v1.5" is a folder name with a dot in it, not an extension.
            return name.EndsWith(".osz", StringComparison.OrdinalIgnoreCase)
                ? name[..^4]
                : name;
        }

        /// <summary>Files that mark the repository root when walking upwards.</summary>
        private static readonly string[] repository_markers = { "FRONTEND_PLAN.md", "BACKEND.md" };

        /// <summary>
        /// Resolves the songs directory to use when none was given explicitly:
        /// the <c>OSUCLIENT_SONGS_DIR</c> environment variable if set, otherwise
        /// the repository's <c>data/output</c> if this build is running from
        /// inside the repo. Null when neither applies.
        ///
        /// The returned path is NOT guaranteed to exist — song select reports a
        /// missing directory, which is more useful than showing nothing.
        /// </summary>
        public static string? ResolveDefaultSongsDirectory()
        {
            string? fromEnvironment = Environment.GetEnvironmentVariable(SongsDirectoryEnvironmentVariable);

            if (!string.IsNullOrWhiteSpace(fromEnvironment))
                return fromEnvironment;

            string? repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);

            return repositoryRoot == null
                ? null
                : System.IO.Path.Combine(repositoryRoot, "data", "output");
        }

        /// <summary>
        /// Walks up from <paramref name="startDirectory"/> looking for the
        /// directory holding the project's marker files. Returns null if the
        /// filesystem root is reached without finding them — which is the
        /// normal case for an installed copy living outside the repo.
        /// </summary>
        public static string? FindRepositoryRoot(string startDirectory)
        {
            var directory = new DirectoryInfo(startDirectory);

            while (directory != null)
            {
                if (repository_markers.All(marker => File.Exists(System.IO.Path.Combine(directory.FullName, marker))))
                    return directory.FullName;

                directory = directory.Parent;
            }

            return null;
        }

        /// <summary>
        /// Loads every beatmap set in <paramref name="directory"/>.
        ///
        /// Picks up both shapes the backend can leave behind: the packaged
        /// <c>.osz</c> files, and the unpacked beatmap folders it writes
        /// alongside them (or on its own, with <c>--no-osz</c>). A folder whose
        /// name matches an <c>.osz</c> is skipped so the same set doesn't appear
        /// twice.
        ///
        /// Never throws for bad content: a file that fails to load becomes an
        /// entry carrying its error message. An empty or missing directory
        /// yields an empty list.
        /// </summary>
        public static IReadOnlyList<BeatmapLibraryEntry> Load(string? directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return Array.Empty<BeatmapLibraryEntry>();

            var entries = new List<BeatmapLibraryEntry>();
            var packagedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string oszPath in Directory.EnumerateFiles(directory, "*.osz", SearchOption.TopDirectoryOnly))
            {
                packagedNames.Add(System.IO.Path.GetFileNameWithoutExtension(oszPath));
                entries.Add(loadEntry(oszPath, () => OszImporter.ReadSet(oszPath)));
            }

            foreach (string folder in Directory.EnumerateDirectories(directory))
            {
                string name = new DirectoryInfo(folder).Name;

                if (packagedNames.Contains(name))
                    continue;

                // Only treat a folder as a beatmap if it actually holds a .osu,
                // so unrelated subdirectories don't turn into error panels.
                bool hasBeatmap = Directory
                                  .EnumerateFiles(folder, "*.osu", SearchOption.AllDirectories)
                                  .Any();

                if (!hasBeatmap)
                    continue;

                entries.Add(loadEntry(folder, () => OszImporter.LoadFromDirectory(folder)));
            }

            // Ordinal sort keeps song select stable between runs.
            return entries
                   .OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
                   .ThenBy(e => e.Path, StringComparer.Ordinal)
                   .ToList();
        }

        private static BeatmapLibraryEntry loadEntry(string path, Func<BeatmapSet> load)
        {
            try
            {
                return new BeatmapLibraryEntry { Path = path, Set = load() };
            }
            catch (Exception e) when (e is OszImportException or BeatmapDecodeException
                                          or IOException or UnauthorizedAccessException)
            {
                return new BeatmapLibraryEntry { Path = path, Error = e.Message };
            }
        }
    }
}
