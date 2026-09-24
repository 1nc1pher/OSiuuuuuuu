using System;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Graphics.Rack
{
    /// <summary>What a lamp is currently saying.</summary>
    public enum LampState
    {
        /// <summary>Dark, but still tinted — an unlit lamp is a coloured lens, not a hole.</summary>
        Off,

        /// <summary>Lit and steady: this stage is done.</summary>
        Lit,

        /// <summary>Breathing: this stage is running. The only honest "in progress" a screen with no percentage can show.</summary>
        Working,

        /// <summary>Lit red regardless of the lamp's own colour: something failed here.</summary>
        Fault,
    }

    /// <summary>
    /// A small domed panel lamp.
    ///
    /// Used for the generation screen's stage row (LOAD · DETECT · MAP ·
    /// WRITE · ANALYSE) and anywhere else a boolean needs to read as hardware.
    ///
    /// <para>
    /// The <see cref="LampState.Working"/> state matters more than it looks.
    /// While the backend runs, nothing streams but stdout lines — there is no
    /// honest percentage to show — so a breathing lamp on the current stage is
    /// the whole of the progress indication, and it is truthful precisely
    /// because it claims only "this is happening now".
    /// </para>
    /// </summary>
    public partial class IndicatorLamp : CompositeDrawable
    {
        /// <summary>One breath, in milliseconds. Slow enough to read as alive rather than as blinking.</summary>
        private const double breath_duration = 900;

        private static readonly Color4 fault_colour = new Color4(1f, 0.28f, 0.30f, 1f);

        private readonly Container glow;
        private readonly Box lens;
        private readonly Box highlight;

        private Color4 colour = RetroPalette.Mint;
        private LampState state = LampState.Off;

        public IndicatorLamp()
        {
            Size = new Vector2(12);

            InternalChild = glow = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 6,
                BorderThickness = 1,
                BorderColour = RetroPalette.ChromeDark,
                Children = new Drawable[]
                {
                    lens = new Box { RelativeSizeAxes = Axes.Both },
                    // A off-centre specular dot: what makes it read as a dome
                    // rather than a filled circle.
                    highlight = new Box
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Position = new Vector2(-1.6f, -1.6f),
                        Size = new Vector2(3.2f),
                        Colour = Color4.White.Opacity(0.55f),
                        Alpha = 0,
                    },
                },
            };
        }

        /// <summary>The lamp's lit colour. An unlit lamp shows a dark version of it.</summary>
        public Color4 LampColour
        {
            get => colour;
            set
            {
                colour = value;
                applyState();
            }
        }

        public LampState State
        {
            get => state;
            set
            {
                if (state == value)
                    return;

                state = value;
                applyState();
            }
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            applyState();
        }

        private void applyState()
        {
            if (!IsLoaded)
                return;

            glow.ClearTransforms();
            lens.ClearTransforms();

            Color4 lit = state == LampState.Fault ? fault_colour : colour;

            switch (state)
            {
                case LampState.Off:
                    lens.Colour = lit.Darken(0.93f).Opacity(0.85f);
                    highlight.Alpha = 0;
                    glow.EdgeEffect = new EdgeEffectParameters { Type = EdgeEffectType.None };
                    break;

                case LampState.Working:
                    lens.Colour = lit;
                    highlight.Alpha = 1;
                    glow.EdgeEffect = new EdgeEffectParameters
                    {
                        Type = EdgeEffectType.Glow,
                        Colour = lit.Opacity(0.7f),
                        Radius = 9,
                    };

                    // Breathing is driven on the lens's own alpha rather than
                    // the container's: a drawable that fades itself to zero in
                    // its own update stops being updated, and this one has to
                    // keep coming back.
                    lens.FadeTo(0.45f, breath_duration / 2, Easing.InOutSine)
                        .Then()
                        .FadeTo(1f, breath_duration / 2, Easing.InOutSine)
                        .Loop();
                    break;

                default:
                    lens.Colour = lit;
                    lens.Alpha = 1;
                    highlight.Alpha = 1;
                    glow.EdgeEffect = new EdgeEffectParameters
                    {
                        Type = EdgeEffectType.Glow,
                        Colour = lit.Opacity(0.65f),
                        Radius = 7,
                    };
                    break;
            }
        }
    }
}
