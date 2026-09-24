using System;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using OsuClient.Game.Graphics;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.Generation
{
    /// <summary>
    /// One difficulty, as a cassette standing spine-out in a rack: pushed in
    /// and lit when it will be generated, pulled out and grey when it won't.
    ///
    /// <para>
    /// The colours come from <see cref="RetroPalette.ForDifficultyRank"/>,
    /// which is the same ramp song select gives the same five tiers — so the
    /// tape you ask for here is the colour of the cassette you press play on
    /// later. A tier keeping its colour across the whole app is the point of
    /// that ramp existing.
    /// </para>
    ///
    /// <para>
    /// Selected state differs in position as well as brightness, because a
    /// still frame is how this screen gets checked and a colour-only toggle
    /// cannot be read in one.
    /// </para>
    /// </summary>
    public partial class TierSpine : ClickableContainer
    {
        private const float spine_width = 52;
        private const float spine_height = 176;

        /// <summary>How far a deselected spine stands proud of the rack.</summary>
        private const float out_offset = 16;

        private const double push_duration = 140;

        private readonly Container body;
        private readonly Box shell;
        private readonly Box band;
        private readonly RetroText nameText;
        private readonly RetroText starsText;
        private readonly Container glow;

        private readonly Color4 accent;

        private bool selected = true;

        /// <summary>Raised when the player pushes this in (true) or pulls it out (false).</summary>
        public event Action<bool>? Toggled;

        /// <summary>The difficulty this stands for, in the backend's own naming.</summary>
        public string Tier { get; }

        public TierSpine(string tier, double stars, int rank, int rankCount)
        {
            Tier = tier;
            accent = RetroPalette.ForDifficultyRank(rank, rankCount);

            Size = new Vector2(spine_width, spine_height + out_offset);

            InternalChild = glow = new Container
            {
                Anchor = Anchor.BottomCentre,
                Origin = Anchor.BottomCentre,
                Size = new Vector2(spine_width, spine_height),
                Masking = true,
                CornerRadius = 4,
                BorderThickness = 1.5f,
                BorderColour = accent.Opacity(0.8f),
                Child = body = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Children = new Drawable[]
                    {
                        shell = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = ColourInfo.GradientVertical(
                                accent.Darken(0.55f), accent.Darken(0.85f)),
                        },
                        // The printed band across the spine, where a real
                        // tape carries its title.
                        band = new Box
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            RelativeSizeAxes = Axes.Y,
                            Width = spine_width - 12,
                            Height = 0.62f,
                            Colour = PaperTextBox.Paper.Opacity(0.92f),
                        },
                        nameText = new RetroText
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            // Read bottom-to-top, the way a spine on a shelf is.
                            Rotation = -90,
                            Font = RetroFontFamily.Body,
                            TextSize = 13,
                            Colour = PaperTextBox.Ink,
                            Text = tier.ToUpperInvariant(),
                        },
                        starsText = new RetroText
                        {
                            Anchor = Anchor.BottomCentre,
                            Origin = Anchor.BottomCentre,
                            Y = -7,
                            Font = RetroFontFamily.Display,
                            TextSize = 8,
                            Colour = accent,
                            Text = stars.ToString("0.0"),
                        },
                    },
                },
            };

            Action = () =>
            {
                Selected = !selected;
                Toggled?.Invoke(selected);
            };
        }

        /// <summary>Whether this difficulty will be generated.</summary>
        public bool Selected
        {
            get => selected;
            set
            {
                if (selected == value)
                    return;

                selected = value;
                applyState(true);
            }
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            applyState(false);
        }

        private void applyState(bool animate)
        {
            double duration = animate ? push_duration : 0;

            // Pushed home when selected; standing proud of the rack when not.
            glow.MoveToY(selected ? 0 : -out_offset, duration, Easing.OutQuint);

            shell.FadeColour(selected
                ? ColourInfo.GradientVertical(accent.Darken(0.55f), accent.Darken(0.85f))
                : ColourInfo.GradientVertical(RetroPalette.ChromeDark.Darken(0.45f),
                                              RetroPalette.ChromeDark.Darken(0.7f)),
                duration);

            band.FadeColour(selected
                ? PaperTextBox.Paper.Opacity(0.92f)
                : PaperTextBox.Paper.Opacity(0.35f), duration);

            nameText.FadeColour(selected
                ? PaperTextBox.Ink
                : PaperTextBox.Ink.Opacity(0.55f), duration);

            starsText.FadeColour(selected ? accent : RetroPalette.TextDim.Opacity(0.5f), duration);

            glow.BorderColour = selected ? accent.Opacity(0.85f) : RetroPalette.ChromeDark;

            glow.FadeEdgeEffectTo(selected ? accent.Opacity(0.35f) : Color4.Transparent, duration);
            glow.EdgeEffect = new EdgeEffectParameters
            {
                Type = EdgeEffectType.Glow,
                Colour = selected ? accent.Opacity(0.35f) : Color4.Transparent,
                Radius = 12,
            };
        }
    }
}
