using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using OsuClient.Game.Graphics;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.Gameplay.HUD
{
    /// <summary>Bottom-left combo counter: bumps on every hit, flashes red on a combo break.</summary>
    public partial class ComboCounter : CompositeDrawable
    {
        private const float pop_scale = 1.6f;
        private const double pop_duration = 400;

        private readonly RetroText text;

        private int displayedCombo;

        /// <summary>Current display scale of the combo number. Exposed for tests.</summary>
        public float CurrentScale => text.Scale.X;

        /// <summary>Current display colour of the combo number. Exposed for tests.</summary>
        public Color4 CurrentColour => text.Colour;

        public ComboCounter()
        {
            AutoSizeAxes = Axes.Both;
            Anchor = Anchor.BottomLeft;
            Origin = Anchor.BottomLeft;

            InternalChild = text = new RetroText
            {
                Font = RetroFontFamily.Display,
                TextSize = 28,
                Colour = Color4.White,
                Text = "0x",
            };
        }

        public void SetCombo(int combo)
        {
            bool broke = combo == 0 && displayedCombo > 0;
            displayedCombo = combo;

            text.Text = $"{combo}x";

            if (broke)
            {
                text.FadeColour(new Color4(1f, 0.35f, 0.35f, 1f), 0)
                    .Then().FadeColour(Color4.White, 500, Easing.OutQuint);
            }
            else if (combo > 0)
            {
                // Classic beatmania pop: jump up in size immediately, then
                // ease smoothly back down to normal — a settle, not a
                // bounce, so no overshoot/elastic wobble here.
                text.ScaleTo(pop_scale, 0).Then().ScaleTo(1f, pop_duration, Easing.OutQuint);
            }
        }
    }
}
