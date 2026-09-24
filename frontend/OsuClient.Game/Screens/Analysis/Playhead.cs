using System;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using OsuClient.Game.Graphics;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.Analysis
{
    /// <summary>
    /// The analyser's playhead: a line down the spectrogram with a pin on top
    /// — a block like a tape head, pointing down at the moment it marks.
    ///
    /// <para>
    /// The pin is the handle. With the cursor over it, or while it is being
    /// dragged, it lights: the block glows, the line thickens, and the time
    /// it marks appears beside it. Dragging itself is the screen's, like any
    /// drag on the strip — this only draws, and says when it is hovered.
    /// </para>
    /// </summary>
    public partial class Playhead : CompositeDrawable
    {
        private const float pin_width = 26;
        private const float pin_height = 18;
        private const float pointer_size = 7;

        private readonly Box line;
        private readonly Container pin;
        private readonly Container pointer;
        private readonly RetroText time;

        private bool hovered;
        private bool grabbed;

        public Playhead()
        {
            RelativeSizeAxes = Axes.Y;
            RelativePositionAxes = Axes.X;
            AutoSizeAxes = Axes.X;
            Origin = Anchor.TopCentre;

            InternalChildren = new Drawable[]
            {
                line = new Box
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    RelativeSizeAxes = Axes.Y,
                    Width = 2,
                    Colour = RetroPalette.Cyan,
                    Alpha = 0.9f,
                },
                // Under the pin, a rotated square half-hidden behind it: the
                // point that says which moment the block is marking.
                pointer = new Container
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.Centre,
                    Y = pin_height - 1,
                    Size = new Vector2(pointer_size * 1.4f),
                    Rotation = 45,
                    Masking = true,
                    Child = new Box { RelativeSizeAxes = Axes.Both },
                    Colour = RetroPalette.Cyan.Darken(0.35f),
                },
                pin = new PinBlock(this)
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Size = new Vector2(pin_width, pin_height),
                    Masking = true,
                    CornerRadius = 3,
                    BorderThickness = 1.5f,
                    BorderColour = RetroPalette.Chrome.Opacity(0.7f),
                    Colour = RetroPalette.Cyan.Darken(0.35f),
                    Children = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both },
                        // Three grip lines, like the ridges on a slider cap.
                        grip(-5),
                        grip(0),
                        grip(5),
                    },
                },
                time = new RetroText
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.CentreLeft,
                    X = pin_width / 2 + 8,
                    Y = pin_height / 2,
                    Font = RetroFontFamily.Display,
                    TextSize = 10,
                    Colour = RetroPalette.Cyan,
                    Alpha = 0,
                },
            };
        }

        /// <summary>Whether the pin is being dragged. Set by the screen, which owns the drag.</summary>
        public bool Grabbed
        {
            get => grabbed;
            set
            {
                if (grabbed == value)
                    return;

                grabbed = value;
                updateLit();
            }
        }

        /// <summary>Whether the pin is lit — hovered or held. Exposed for tests.</summary>
        public bool Lit => hovered || grabbed;

        /// <summary>The moment marked, for the readout beside the pin.</summary>
        public void SetTime(double seconds)
        {
            var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
            time.Text = $"{(int)span.TotalMinutes}:{span.Seconds:00}.{span.Milliseconds / 100}";
        }

        private void setHovered(bool value)
        {
            hovered = value;
            updateLit();
        }

        private void updateLit()
        {
            bool lit = Lit;

            var colour = lit ? RetroPalette.Cyan : RetroPalette.Cyan.Darken(0.35f);

            pin.FadeColour(colour, 120, Easing.OutQuint);
            pointer.FadeColour(colour, 120, Easing.OutQuint);

            pin.EdgeEffect = new EdgeEffectParameters
            {
                Type = EdgeEffectType.Glow,
                Colour = lit ? RetroPalette.Cyan.Opacity(0.7f) : Color4.Transparent,
                Radius = 16,
            };

            line.ResizeWidthTo(lit ? 3 : 2, 120, Easing.OutQuint);
            line.FadeTo(lit ? 1 : 0.9f, 120);

            time.FadeTo(lit ? 1 : 0, lit ? 120 : 250, Easing.OutQuint);
        }

        private static Drawable grip(float x) => new Box
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            X = x,
            Size = new Vector2(1.5f, pin_height * 0.5f),
            Colour = RetroPalette.Void.Opacity(0.6f),
        };

        /// <summary>The block itself, which is what the cursor finds.</summary>
        private partial class PinBlock : Container
        {
            private readonly Playhead playhead;

            public PinBlock(Playhead playhead)
            {
                this.playhead = playhead;
            }

            protected override bool OnHover(HoverEvent e)
            {
                playhead.setHovered(true);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                playhead.setHovered(false);
                base.OnHoverLost(e);
            }
        }
    }
}
