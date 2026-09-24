using System;
using System.Text;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Graphics.Rack
{
    /// <summary>
    /// A numeric readout on a dark inset, with the unlit segments ghosted
    /// behind the value.
    ///
    /// The ghost is the whole trick. A seven-segment display shows its dead
    /// segments faintly whether or not they are driven, which is why a real
    /// one reads as hardware and a number on a dark rectangle reads as a
    /// label. It also stops the panel twitching as digits change width — the
    /// ghost reserves the room.
    ///
    /// Used for the BPM counter, the elapsed clock, the tier counter and the
    /// analyser's live onset count.
    /// </summary>
    public partial class SegmentReadout : CompositeDrawable
    {
        /// <summary>The character the ghost is drawn from — the one that lights every segment.</summary>
        private const char ghost_character = '8';

        private readonly RetroText ghost;
        private readonly RetroText value;
        private readonly Container glow;

        private string text = string.Empty;
        private int digits = 3;

        // Kept alongside the drawable's own Colour, which reads back as a
        // ColourInfo and cannot be handed to the Color4 helpers.
        private Color4 displayColour = RetroPalette.Amber;

        public SegmentReadout(float textSize = 16)
        {
            AutoSizeAxes = Axes.Both;

            InternalChild = new Container
            {
                AutoSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 3,
                BorderThickness = 1,
                BorderColour = RetroPalette.ChromeDark.Opacity(0.8f),
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = new Color4(0.05f, 0.045f, 0.08f, 1f),
                    },
                    glow = new Container
                    {
                        AutoSizeAxes = Axes.Both,
                        // Masking has to be on for an edge effect to be legal
                        // at all, even though nothing here needs clipping --
                        // the glow itself draws outside these bounds.
                        Masking = true,
                        Padding = new MarginPadding { Horizontal = 7, Vertical = 4 },
                        Children = new Drawable[]
                        {
                            ghost = new RetroText
                            {
                                Font = RetroFontFamily.Display,
                                TextSize = textSize,
                                Colour = RetroPalette.Amber.Opacity(0.12f),
                            },
                            value = new RetroText
                            {
                                Font = RetroFontFamily.Display,
                                TextSize = textSize,
                                Colour = RetroPalette.Amber,
                            },
                        },
                    },
                },
            };
        }

        /// <summary>
        /// How many characters of room to reserve. The ghost is this many
        /// <c>8</c>s, so a counter climbing from 9 to 1,122 does not shove
        /// the panel around as it goes.
        /// </summary>
        public int Digits
        {
            get => digits;
            set
            {
                digits = Math.Max(1, value);
                rebuildGhost();
            }
        }

        /// <summary>The value on the display.</summary>
        public string Text
        {
            get => text;
            set
            {
                text = value ?? string.Empty;
                this.value.Text = text;

                if (text.Length > digits)
                {
                    digits = text.Length;
                    rebuildGhost();
                }
            }
        }

        /// <summary>The lit colour. The ghost follows it at low opacity.</summary>
        public Color4 DisplayColour
        {
            get => displayColour;
            set
            {
                displayColour = value;
                this.value.Colour = value;
                ghost.Colour = value.Opacity(0.12f);
            }
        }

        /// <summary>Whether the readout glows. Off by default — a whole rack of glowing numbers is a light show, not a panel.</summary>
        public bool Glowing
        {
            set => glow.EdgeEffect = value
                ? new EdgeEffectParameters
                {
                    Type = EdgeEffectType.Glow,
                    Colour = displayColour.Opacity(0.35f),
                    Radius = 10,
                }
                : new EdgeEffectParameters { Type = EdgeEffectType.None };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            rebuildGhost();
        }

        private void rebuildGhost()
        {
            var builder = new StringBuilder(digits);

            for (int i = 0; i < digits; i++)
                builder.Append(ghost_character);

            ghost.Text = builder.ToString();
        }
    }
}
