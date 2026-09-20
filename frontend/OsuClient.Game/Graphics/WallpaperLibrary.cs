using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OsuClient.Game.Beatmaps;

namespace OsuClient.Game.Graphics
{
    /// <summary>
    /// Finds the menu's background artwork on disk — the shipped wallpapers in
    /// <c>data/default wallpapers</c>, which the main menu picks one of at
    /// random per session (MENU_REDESIGN_PLAN.md step 0).
    ///
    /// Deliberately shaped like <see cref="BeatmapLibrary"/>: an env var
    /// override, a repository-relative default, and a loader that never
    /// throws. The menu has to come up on a machine with no wallpapers at all
    /// — <c>data/</c> is gitignored, so a clean clone has an empty folder —
    /// and an empty list here is the normal case, not an error. Callers fall
    /// back to the playing song's own cover art, then to the flat palette.
    /// </summary>
    public static class WallpaperLibrary
    {
        /// <summary>Overrides the wallpaper directory when set.</summary>
        public const string DirectoryEnvironmentVariable = "OSUCLIENT_WALLPAPER_DIR";

        /// <summary>
        /// Extensions treated as wallpapers. Matches what SixLabors.ImageSharp
        /// decodes by default — <see cref="BeatmapBackground"/> is what
        /// eventually loads these, and it can only show what ImageSharp reads.
        /// </summary>
        private static readonly string[] image_extensions = { ".jpg", ".jpeg", ".png", ".bmp", ".webp", ".gif" };

        /// <summary>
        /// Where wallpapers live when nothing was passed explicitly: the
        /// <c>OSUCLIENT_WALLPAPER_DIR</c> environment variable if set,
        /// otherwise the repository's <c>data/default wallpapers</c> when this
        /// build runs from inside the repo. Null when neither applies.
        ///
        /// Like <see cref="BeatmapLibrary.ResolveDefaultSongsDirectory"/>, the
        /// returned path is not guaranteed to exist.
        /// </summary>
        public static string? ResolveDefaultDirectory()
        {
            string? fromEnvironment = Environment.GetEnvironmentVariable(DirectoryEnvironmentVariable);

            if (!string.IsNullOrWhiteSpace(fromEnvironment))
                return fromEnvironment;

            string? repositoryRoot = BeatmapLibrary.FindRepositoryRoot(AppContext.BaseDirectory);

            return repositoryRoot == null
                ? null
                : Path.Combine(repositoryRoot, "data", "default wallpapers");
        }

        /// <summary>
        /// Every usable image in <paramref name="directory"/>, sorted by name.
        ///
        /// Sorted because <see cref="Directory.EnumerateFiles(string)"/>'s
        /// order is filesystem-defined: a seeded <see cref="Random"/> would
        /// otherwise pick a different wallpaper on a different machine, which
        /// is exactly what the tests here pin down.
        ///
        /// A missing directory, an empty one, or one holding no images all
        /// yield an empty list.
        /// </summary>
        public static IReadOnlyList<string> Load(string? directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return Array.Empty<string>();

            try
            {
                return Directory
                       .EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                       .Where(path => image_extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                       .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                       .ToArray();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A wallpaper folder that can't be read is the same situation
                // as one that isn't there — the menu shows the song's own art.
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// One of <paramref name="wallpapers"/> at random, or null when there
        /// are none.
        ///
        /// <paramref name="random"/> is injectable so tests get a predictable
        /// choice; the menu passes null and gets <see cref="Random.Shared"/>.
        /// </summary>
        public static string? PickRandom(IReadOnlyList<string> wallpapers, Random? random = null)
        {
            if (wallpapers.Count == 0)
                return null;

            return wallpapers[(random ?? Random.Shared).Next(wallpapers.Count)];
        }
    }
}
