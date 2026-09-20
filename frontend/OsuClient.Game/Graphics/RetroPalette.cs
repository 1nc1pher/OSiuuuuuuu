using osuTK.Graphics;

namespace OsuClient.Game.Graphics
{
    /// <summary>
    /// The 80s vinyl/cassette palette song select is built from.
    ///
    /// The codebase had no central colour class for a long time and didn't
    /// need one — every screen picked its own literals. This exists because
    /// two separate features now have to agree on the same ramp: a
    /// difficulty's cassette button and that difficulty's accent on the
    /// vinyl wheel are the same colour on purpose (CAROUSEL_REDESIGN_PLAN.md
    /// step 5), and a ramp duplicated in two files drifts.
    /// </summary>
    public static class RetroPalette
    {
        // ------------------------------------------------------------------
        // Surfaces
        // ------------------------------------------------------------------

        /// <summary>Deep indigo the whole screen sits on when no art is showing.</summary>
        public static readonly Color4 Void = new Color4(0.055f, 0.035f, 0.11f, 1f);

        /// <summary>Panel fill — dark enough to keep text legible over any cover art.</summary>
        public static readonly Color4 Panel = new Color4(0.08f, 0.05f, 0.15f, 0.82f);

        /// <summary>Slightly lifted panel fill, for insets and label windows.</summary>
        public static readonly Color4 PanelInset = new Color4(0.04f, 0.025f, 0.08f, 0.9f);

        /// <summary>Brushed-metal tone for cassette shells and turntable trim.</summary>
        public static readonly Color4 Chrome = new Color4(0.72f, 0.73f, 0.80f, 1f);

        /// <summary>Shadowed side of a chrome surface, for the gradient.</summary>
        public static readonly Color4 ChromeDark = new Color4(0.30f, 0.30f, 0.38f, 1f);

        /// <summary>Vinyl body — near-black with a violet cast, not flat grey.</summary>
        public static readonly Color4 Vinyl = new Color4(0.055f, 0.045f, 0.08f, 1f);

        // ------------------------------------------------------------------
        // Neon accents
        // ------------------------------------------------------------------

        public static readonly Color4 Magenta = new Color4(1f, 0.18f, 0.53f, 1f);
        public static readonly Color4 Cyan = new Color4(0.16f, 0.89f, 1f, 1f);
        public static readonly Color4 Violet = new Color4(0.69f, 0.29f, 1f, 1f);
        public static readonly Color4 Amber = new Color4(1f, 0.71f, 0.25f, 1f);
        public static readonly Color4 Mint = new Color4(0.24f, 0.95f, 0.61f, 1f);

        // ------------------------------------------------------------------
        // Text
        // ------------------------------------------------------------------

        /// <summary>Warm off-white — pure white reads too clinical against neon.</summary>
        public static readonly Color4 Text = new Color4(0.97f, 0.94f, 0.90f, 1f);

        /// <summary>Secondary labels.</summary>
        public static readonly Color4 TextDim = new Color4(0.70f, 0.66f, 0.78f, 1f);

        // ------------------------------------------------------------------
        // Background grade
        // ------------------------------------------------------------------

        /// <summary>Top of the sunset wash laid over the blurred cover art.</summary>
        public static readonly Color4 GradeTop = new Color4(0.36f, 0.09f, 0.45f, 1f);

        /// <summary>Bottom of that wash.</summary>
        public static readonly Color4 GradeBottom = new Color4(0.95f, 0.30f, 0.35f, 1f);

        // ------------------------------------------------------------------
        // Difficulty ramp
        // ------------------------------------------------------------------

        /// <summary>The ramp, easiest to hardest.</summary>
        public static readonly Color4[] DifficultyRamp = { Cyan, Mint, Amber, Magenta, Violet };

        /// <summary>
        /// Maps object density (objects per second) onto the neon ramp.
        ///
        /// The thresholds come from the backend's real per-tier densities
        /// (Easy ~0.73, Normal ~1.56, Hard ~2.33, Insane ~3.62, Expert ~4.61
        /// obj/s), as recorded in BACKEND.md's <c>--all</c> table.
        /// </summary>
        public static Color4 ForDensity(double density)
        {
            if (density < 1.1) return Cyan;
            if (density < 1.95) return Mint;
            if (density < 2.95) return Amber;
            if (density < 4.1) return Magenta;
            return Violet;
        }

        /// <summary>
        /// The colour for the <paramref name="index"/>th of
        /// <paramref name="count"/> difficulties, easiest first.
        ///
        /// Preferred over <see cref="ForDensity"/> anywhere a whole set is
        /// shown at once. Density is an absolute scale, so a long, sparse song
        /// can land three of its five difficulties in the same bucket — fine
        /// when the colour is a dot beside a label, wrong when the colour *is*
        /// the button. Spreading by rank keeps every tier distinguishable, and
        /// still gives the same five colours in the same order for every
        /// standard five-difficulty set the backend generates.
        /// </summary>
        public static Color4 ForDifficultyRank(int index, int count)
        {
            if (count <= 1)
                return DifficultyRamp[0];

            int last = DifficultyRamp.Length - 1;
            int slot = (int)System.Math.Round(index / (double)(count - 1) * last);

            return DifficultyRamp[System.Math.Clamp(slot, 0, last)];
        }
    }
}
