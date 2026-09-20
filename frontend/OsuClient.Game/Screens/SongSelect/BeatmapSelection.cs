using OsuClient.Game.Beatmaps;

namespace OsuClient.Game.Screens.SongSelect
{
    /// <summary>
    /// A chosen difficulty, together with the set it came from — what song
    /// select hands to gameplay.
    /// </summary>
    public class BeatmapSelection
    {
        public BeatmapLibraryEntry Entry { get; }

        public Beatmap Beatmap { get; }

        public BeatmapSelection(BeatmapLibraryEntry entry, Beatmap beatmap)
        {
            Entry = entry;
            Beatmap = beatmap;
        }
    }
}
