using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace OsuClient.Game.Beatmaps
{
    /// <summary>Thrown when an .osz archive can't be read or contains no usable beatmap.</summary>
    public class OszImportException : Exception
    {
        public OszImportException(string message)
            : base(message)
        {
        }

        public OszImportException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }

    /// <summary>
    /// A beatmap set: the difficulties that shipped together in one .osz,
    /// plus the audio track they share.
    /// </summary>
    public class BeatmapSet
    {
        /// <summary>Set name — the .osz filename or folder name, without extension.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Difficulties, ordered by filename for determinism.</summary>
        public IReadOnlyList<Beatmap> Beatmaps { get; set; } = Array.Empty<Beatmap>();

        /// <summary>Every file in the set, by relative name.</summary>
        public IReadOnlyList<string> Files { get; set; } = Array.Empty<string>();

        /// <summary>
        /// The audio filename the beatmaps declare in <c>[General]</c>.
        /// Null if the set contains no beatmap or none names a track.
        /// </summary>
        public string? AudioFilename { get; set; }

        /// <summary>
        /// Absolute path to the audio file on disk. Only set by the overloads
        /// that extract to a directory; null when a set is read from a stream.
        /// </summary>
        public string? AudioPath { get; set; }

        /// <summary>
        /// Absolute path to a background image, if the set's folder happens to
        /// contain one — there's no customization UI yet (see
        /// FRONTEND_PLAN.md's Phase 6), so for now this is just whatever image
        /// file someone dropped into the set's own folder by hand. Null if
        /// there isn't one, or the set wasn't loaded from a directory.
        /// </summary>
        public string? BackgroundPath { get; set; }

        /// <summary>Whether the declared audio track is actually present in the set.</summary>
        public bool HasAudio =>
            AudioFilename != null &&
            Files.Any(f => string.Equals(f, AudioFilename, StringComparison.OrdinalIgnoreCase));

        public override string ToString() =>
            $"{Name} ({Beatmaps.Count} difficulties, audio: {AudioFilename ?? "none"})";
    }

    /// <summary>
    /// Reads .osz beatmap archives — which are plain zips holding the audio
    /// track plus one .osu per difficulty, exactly what the backend's
    /// <c>build_beatmap_set()</c> writes to <c>data/output/</c>.
    /// </summary>
    public static class OszImporter
    {
        private const string osu_extension = ".osu";

        /// <summary>
        /// Reads a set straight out of a stream without touching the disk.
        /// <see cref="BeatmapSet.AudioPath"/> is left null; use
        /// <see cref="Import(string, string)"/> if the audio file is needed.
        /// </summary>
        public static BeatmapSet ReadSet(Stream oszStream, string name = "")
        {
            if (oszStream == null)
                throw new ArgumentNullException(nameof(oszStream));

            using var archive = openArchive(oszStream);

            var files = new List<string>();
            var decoded = new List<(string entry, Beatmap beatmap)>();

            foreach (var entry in archive.Entries)
            {
                // Directory entries have an empty name.
                if (entry.Name.Length == 0)
                    continue;

                files.Add(entry.FullName);

                if (!isOsuFile(entry.FullName))
                    continue;

                using var entryStream = entry.Open();

                try
                {
                    decoded.Add((entry.FullName, BeatmapDecoder.Decode(entryStream)));
                }
                catch (BeatmapDecodeException e)
                {
                    throw new OszImportException($"\"{entry.FullName}\" is not a valid .osu file: {e.Message}", e);
                }
            }

            if (decoded.Count == 0)
                throw new OszImportException($"archive contains no {osu_extension} files");

            return buildSet(name, files, decoded);
        }

        /// <summary>Reads a set from an .osz on disk, without extracting it.</summary>
        public static BeatmapSet ReadSet(string oszPath)
        {
            if (!File.Exists(oszPath))
                throw new FileNotFoundException("no such .osz file", oszPath);

            using var stream = File.OpenRead(oszPath);
            return ReadSet(stream, Path.GetFileNameWithoutExtension(oszPath));
        }

        /// <summary>
        /// Extracts an .osz into <paramref name="destinationDirectory"/> (in a
        /// subfolder named after the set) and returns the decoded set with
        /// <see cref="BeatmapSet.AudioPath"/> resolved.
        /// </summary>
        public static BeatmapSet Import(string oszPath, string destinationDirectory)
        {
            if (!File.Exists(oszPath))
                throw new FileNotFoundException("no such .osz file", oszPath);

            string name = Path.GetFileNameWithoutExtension(oszPath);
            string target = Path.Combine(destinationDirectory, name);

            Directory.CreateDirectory(target);

            string targetFull = Path.GetFullPath(target);

            using (var stream = File.OpenRead(oszPath))
            using (var archive = openArchive(stream))
            {
                foreach (var entry in archive.Entries)
                {
                    if (entry.Name.Length == 0)
                        continue;

                    string destination = Path.GetFullPath(Path.Combine(target, entry.FullName));

                    // Zip-slip guard: a crafted archive can carry "../" in an
                    // entry name and write outside the target directory.
                    if (!destination.StartsWith(targetFull + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                        !string.Equals(destination, targetFull, StringComparison.Ordinal))
                    {
                        throw new OszImportException(
                            $"archive entry \"{entry.FullName}\" would extract outside the target directory");
                    }

                    string? parent = Path.GetDirectoryName(destination);

                    if (parent != null)
                        Directory.CreateDirectory(parent);

                    entry.ExtractToFile(destination, true);
                }
            }

            var set = LoadFromDirectory(target);
            set.Name = name;
            return set;
        }

        /// <summary>
        /// Loads an already-extracted beatmap folder — the shape the backend
        /// leaves behind in <c>data/output/&lt;Artist&gt; - &lt;Song&gt;/</c>,
        /// which the plan's "Songs folder" convention points at directly.
        /// </summary>
        public static BeatmapSet LoadFromDirectory(string directory)
        {
            if (!Directory.Exists(directory))
                throw new DirectoryNotFoundException($"no such beatmap folder: {directory}");

            string root = Path.GetFullPath(directory);

            var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                                 .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
                                 .ToList();

            var decoded = new List<(string entry, Beatmap beatmap)>();

            foreach (string relative in files.Where(isOsuFile))
            {
                string full = Path.Combine(root, relative);

                try
                {
                    decoded.Add((relative, BeatmapDecoder.DecodeFile(full)));
                }
                catch (BeatmapDecodeException e)
                {
                    throw new OszImportException($"\"{relative}\" is not a valid .osu file: {e.Message}", e);
                }
            }

            if (decoded.Count == 0)
                throw new OszImportException($"folder contains no {osu_extension} files: {directory}");

            var set = buildSet(new DirectoryInfo(root).Name, files, decoded);

            if (set.AudioFilename != null)
            {
                string audio = Path.Combine(root, set.AudioFilename);

                if (File.Exists(audio))
                    set.AudioPath = audio;
            }

            // Ordinal sort for the same reason difficulty order is sorted
            // this way: deterministic across platforms, so which file wins
            // when a set somehow has more than one image doesn't depend on
            // filesystem enumeration order.
            string? background = files.Where(isImageFile).OrderBy(f => f, StringComparer.Ordinal).FirstOrDefault();

            if (background != null)
                set.BackgroundPath = Path.Combine(root, background);

            return set;
        }

        // ------------------------------------------------------------------

        private static readonly string[] image_extensions = { ".jpg", ".jpeg", ".png", ".bmp", ".webp" };

        private static bool isImageFile(string path) =>
            image_extensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

        private static ZipArchive openArchive(Stream stream)
        {
            try
            {
                return new ZipArchive(stream, ZipArchiveMode.Read, true);
            }
            catch (InvalidDataException e)
            {
                throw new OszImportException("file is not a valid zip archive", e);
            }
        }

        private static bool isOsuFile(string path) =>
            path.EndsWith(osu_extension, StringComparison.OrdinalIgnoreCase);

        private static BeatmapSet buildSet(string name, List<string> files,
                                           List<(string entry, Beatmap beatmap)> decoded)
        {
            // Ordinal sort on the filename keeps difficulty order stable across
            // platforms and zip implementations, which matters for tests and for
            // a song-select list that shouldn't reshuffle between runs.
            var ordered = decoded.OrderBy(d => d.entry, StringComparer.Ordinal)
                                 .Select(d => d.beatmap)
                                 .ToList();

            return new BeatmapSet
            {
                Name = name,
                Beatmaps = ordered,
                Files = files,
                AudioFilename = ordered
                                .Select(b => b.General.AudioFilename)
                                .FirstOrDefault(a => !string.IsNullOrWhiteSpace(a)),
            };
        }
    }
}
