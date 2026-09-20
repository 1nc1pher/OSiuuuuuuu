using System;
using System.Collections.Generic;
using OsuClient.Game.Beatmaps;

namespace OsuClient.Game.Screens.SongSelect
{
    /// <summary>
    /// Puts a set's difficulties in the order a player expects to read them.
    ///
    /// Neither obvious option works on its own. Filename order — what the
    /// decoder returns — is alphabetical, so Expert lands second. Object
    /// density is usually right but not always: the backend can emit a Hard
    /// with fewer objects than its own Normal, and sorting by density then
    /// prints "Easy, Hard, Normal", which reads as a bug whatever the
    /// numbers say. The tier *name* is what the player is actually reading,
    /// so that wins, with density breaking ties and ordering anything
    /// unrecognised.
    /// </summary>
    public static class DifficultyTier
    {
        private static readonly Dictionary<string, int> known_tiers =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["easy"] = 0,
                ["normal"] = 1,
                ["hard"] = 2,
                ["insane"] = 3,
                ["expert"] = 4,
            };

        /// <summary>
        /// The tier a difficulty name denotes, or null when it isn't one of
        /// the names the backend generates.
        /// </summary>
        public static int? RankOf(string? version)
        {
            if (string.IsNullOrWhiteSpace(version))
                return null;

            return known_tiers.TryGetValue(version.Trim(), out int rank) ? rank : null;
        }

        /// <summary>
        /// Sort key: recognised tiers first in tier order, then anything else
        /// by density. The large offset keeps the two groups from interleaving
        /// — an unnamed difficulty sorts after Expert rather than into the
        /// middle of the list.
        /// </summary>
        public static double SortKey(Beatmap beatmap)
        {
            int? rank = RankOf(beatmap.Metadata.Version);

            return rank ?? 1000 + BeatmapStatistics.Density(beatmap);
        }
    }
}
