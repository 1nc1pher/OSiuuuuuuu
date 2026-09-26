using System;
using osu.Framework.Graphics.Lines;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.Gameplay
{
    /// <summary>
    /// A slider's body as a length of magnetic tape: an outline in the combo
    /// colour, then a thin dark edge, then translucent oxide-brown ribbon with
    /// faint lighter tracks running along it, the way a tape's recorded tracks
    /// show.
    ///
    /// All of it is one path, coloured across its width (<see cref="ColourAt"/>
    /// goes from the outer edge at 0 to the centre line at 1), so the tape
    /// costs no more to draw than a plain body. Only the outline is solid and
    /// deep; the ribbon is see-through, so the background shows through the
    /// slider as it does through the circles.
    /// </summary>
    public partial class SliderTapeBody : SmoothPath
    {
        /// <summary>How much of the radius the solid outline takes.</summary>
        private const float outline = 0.16f;

        /// <summary>The dark edge of the tape, just inside the outline.</summary>
        private const float edge = 0.22f;

        private const float ribbon_alpha = 0.42f;

        private static readonly Color4 oxide = new Color4(0.30f, 0.19f, 0.14f, ribbon_alpha);
        private static readonly Color4 track = new Color4(0.52f, 0.37f, 0.28f, ribbon_alpha + 0.12f);
        private static readonly Color4 dark_edge = new Color4(0.04f, 0.03f, 0.05f, 0.6f);

        /// <summary>Centres of the recorded tracks, across the ribbon (0 = its edge, 1 = the centre line).</summary>
        private static readonly float[] tracks = { 0.40f, 0.62f, 0.86f };

        private const float track_half_width = 0.035f;

        private readonly Color4 outlineColour;

        public SliderTapeBody(Color4 comboColour)
        {
            outlineColour = comboColour;
        }

        protected override Color4 ColourAt(float position)
        {
            if (position < outline)
                return outlineColour;

            if (position < edge)
                return dark_edge;

            foreach (float centre in tracks)
            {
                if (Math.Abs(position - centre) < track_half_width)
                    return track;
            }

            return oxide;
        }
    }
}
