using System;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Logging;
using OsuClient.Game.Beatmaps;
using OsuClient.Game.Graphics;

namespace OsuClient.Game.Screens.SongSelect
{
    /// <summary>
    /// Does the first-visit work of the screens the menu leads to, in the
    /// background, while the menu is simply sitting there.
    ///
    /// <para>
    /// Song select's first load decodes every cover in the library and pays
    /// for the text rasterizer's first run (font parsing, glyph caching,
    /// JIT); after that both are cached for the session. Left to the first
    /// PLAY, that made it take several times longer than every later one —
    /// the ripple covered it, but visibly held for longer the first time.
    /// Doing it here makes the first PLAY and CREATE the same as the rest.
    /// </para>
    ///
    /// <para>
    /// Nothing depends on it finishing: every cache it fills, the screens
    /// fill themselves on a miss. It only moves the cost somewhere nobody is
    /// waiting on it.
    /// </para>
    /// </summary>
    public static class SongSelectWarmup
    {
        private static int started;

        /// <summary>
        /// Starts the warm-up the first time it is asked for, and does nothing
        /// after: the menu comes back after every song, and the caches last
        /// the whole session.
        /// </summary>
        public static void RunOnce(IRenderer renderer, string? songsDirectory)
        {
            if (Interlocked.Exchange(ref started, 1) == 0)
                Run(renderer, songsDirectory);
        }

        public static Task Run(IRenderer renderer, string? songsDirectory) => Task.Run(() =>
        {
            try
            {
                RetroText.Warm();

                var entries = BeatmapLibrary.Load(songsDirectory ?? BeatmapLibrary.ResolveDefaultSongsDirectory());

                foreach (var entry in entries)
                    CoverArt.Load(renderer, entry.BackgroundPath, VinylWedge.ArtResolution);
            }
            catch (Exception e)
            {
                // A warm-up that fails costs the first visit its head start,
                // nothing more.
                Logger.Error(e, "song select warm-up failed");
            }
        });
    }
}
