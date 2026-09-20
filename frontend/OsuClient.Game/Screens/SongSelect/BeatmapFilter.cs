using System;
using System.Collections.Generic;
using System.Linq;
using OsuClient.Game.Beatmaps;

namespace OsuClient.Game.Screens.SongSelect
{
    /// <summary>
    /// Text matching for the song search (CAROUSEL_REDESIGN_PLAN.md step 4).
    ///
    /// Separate from the search bar drawable so the matching rules can be
    /// tested without standing up a game host — the wheel downstream of it
    /// has to cope with "everything", "some", and "nothing" matching, and
    /// those are much cheaper to pin down here than through the UI.
    /// </summary>
    public static class BeatmapFilter
    {
        /// <summary>
        /// The subset of <paramref name="entries"/> matching
        /// <paramref name="query"/>. An empty or whitespace query matches
        /// everything, so clearing the box restores the full library.
        /// </summary>
        public static IReadOnlyList<BeatmapLibraryEntry> Apply(
            IReadOnlyList<BeatmapLibraryEntry> entries, string? query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return entries;

            return entries.Where(e => Matches(e, query)).ToList();
        }

        /// <summary>
        /// Whether one entry matches. Every whitespace-separated term has to
        /// match somewhere — typing "ado horizon" should narrow, not widen,
        /// which a single-substring match over the whole query wouldn't do
        /// once the terms come from different fields.
        /// </summary>
        public static bool Matches(BeatmapLibraryEntry entry, string? query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;

            string haystack = searchableTextFor(entry);

            foreach (string term in query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (haystack.IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Artist, title, difficulty names and mapper, joined. A broken entry
        /// still contributes its display name so it stays findable rather
        /// than disappearing from search only.
        /// </summary>
        private static string searchableTextFor(BeatmapLibraryEntry entry)
        {
            var parts = new List<string> { entry.DisplayName };

            foreach (var beatmap in entry.Difficulties)
            {
                parts.Add(beatmap.Metadata.Artist);
                parts.Add(beatmap.Metadata.Title);
                parts.Add(beatmap.Metadata.Version);
                parts.Add(beatmap.Metadata.Creator);
                parts.Add(beatmap.Metadata.Tags);
            }

            return string.Join(' ', parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }
    }
}
