using osu.Framework.Graphics;

namespace OsuClient.Game.Screens.Gameplay.HUD
{
    /// <summary>
    /// How far through the beatmap's playable span the run is — from the
    /// first hit object to the last, not the length of the audio file, so it
    /// fills up exactly as the map's objects run out. Spans the full
    /// playfield width; positioning/padding is the caller's job.
    /// </summary>
    public partial class SongProgressBar : GlowBar
    {
        /// <summary>Exposed so the HUD can lay other elements out clear of the bar.</summary>
        public const float BarHeight = 26;

        public SongProgressBar()
            : base(BarHeight)
        {
            RelativeSizeAxes = Axes.X;
            Width = 0.92f;
        }

        /// <summary>Sets how far through the map the run is, 0 to 1.</summary>
        public void SetProgress(double progress) => SetFill(progress);
    }
}
