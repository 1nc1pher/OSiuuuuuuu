using System.Linq;
using OsuClient.Game.Beatmaps.HitObjects;

namespace OsuClient.Game.Beatmaps
{
    /// <summary>
    /// Measurements song select reads off a decoded beatmap.
    ///
    /// The backend doesn't compute star ratings, so "how hard is this" has to
    /// be derived from the map itself — object density is the stand-in.
    /// </summary>
    public static class BeatmapStatistics
    {
        /// <summary>
        /// Objects per second across the playable span of the map. Returns 0
        /// for a map with fewer than two objects, where density is meaningless.
        /// </summary>
        public static double Density(Beatmap beatmap)
        {
            if (beatmap.HitObjects.Count < 2)
                return 0;

            double span = (beatmap.LastHitObjectTime - beatmap.FirstHitObjectTime) / 1000.0;

            return span <= 0 ? 0 : beatmap.HitObjects.Count / span;
        }

        /// <summary>Counts of each hit object kind, for the details panel.</summary>
        public static (int circles, int sliders, int spinners) CountObjects(Beatmap beatmap)
        {
            int circles = beatmap.HitObjects.OfType<HitCircleData>().Count();
            int sliders = beatmap.HitObjects.OfType<SliderData>().Count();
            int spinners = beatmap.HitObjects.OfType<SpinnerData>().Count();

            return (circles, sliders, spinners);
        }
    }
}
