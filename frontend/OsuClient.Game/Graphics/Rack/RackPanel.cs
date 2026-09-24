using System;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Graphics.Rack
{
    /// <summary>
    /// A rack-mount module: brushed chrome bezel, four corner screws, an
    /// engraved legend, and a recessed well for whatever it houses.
    ///
    /// This is the frame every panel in the generation screens sits in — the
    /// studio-hardware counterpart to song select's turntable furniture, and
    /// built the same way: drawn from primitives, no art assets.
    ///
    /// <para>
    /// Content goes in <see cref="Content"/>, which is the well, not the
    /// bezel — so a trace or a readout is clipped by the recess rather than
    /// running out over the metal. That is also why this is a
    /// <see cref="Container"/> with an overridden content target rather than
    /// a <see cref="CompositeDrawable"/>: callers add children the ordinary
    /// way and land in the right place.
    /// </para>
    /// </summary>
    public partial class RackPanel : Container
    {
        /// <summary>Bezel width around the well, in pixels.</summary>
        private const float bezel = 10;

        /// <summary>Height reserved for the legend when there is one.</summary>
        private const float legend_height = 20;

        private const float screw_size = 7;
        private const float screw_inset = 7;

        private readonly Container well;
        private readonly RetroText legend;

        protected override Container<Drawable> Content => well;

        public RackPanel()
        {
            Masking = true;
            CornerRadius = 5;
            BorderThickness = 1.5f;
            BorderColour = RetroPalette.Chrome.Opacity(0.55f);

            InternalChildren = new Drawable[]
            {
                // The bezel face. Top-lit, so it reads as a metal panel
                // rather than a grey rectangle — the same vertical gradient
                // CassetteButton moulds its shell with.
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = ColourInfo.GradientVertical(
                        RetroPalette.ChromeDark.Lighten(0.12f),
                        RetroPalette.ChromeDark.Darken(0.45f)),
                },
                legend = new RetroText
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.TopLeft,
                    // Clear of the top-left screw, which sits at
                    // screw_inset..screw_inset + screw_size.
                    Position = new Vector2(screw_inset + screw_size + 7, 6),
                    Font = RetroFontFamily.Body,
                    TextSize = 10,
                    // Engraved, not printed: a dim warm grey on metal rather
                    // than the bright text used on the dark panels.
                    Colour = RetroPalette.Text.Opacity(0.45f),
                    Alpha = 0,
                },
                well = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Masking = true,
                    CornerRadius = 3,
                    Padding = new MarginPadding(0),
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = RetroPalette.PanelInset,
                        },
                    },
                },
                screw(Anchor.TopLeft),
                screw(Anchor.TopRight),
                screw(Anchor.BottomLeft),
                screw(Anchor.BottomRight),
            };

            updateWellPadding();
        }

        /// <summary>The engraved label on the bezel. Empty for an unlabelled module.</summary>
        public string Legend
        {
            get => legend.Text;
            set
            {
                legend.Text = value ?? string.Empty;
                legend.Alpha = legend.Text.Length > 0 ? 1 : 0;
                updateWellPadding();
            }
        }

        private void updateWellPadding()
        {
            float top = legend.Text.Length > 0 ? bezel + legend_height : bezel;

            well.Padding = new MarginPadding
            {
                Top = top,
                Bottom = bezel,
                Horizontal = bezel,
            };
        }

        /// <summary>
        /// A panel-mount screw. Four of them is what stops a rounded
        /// rectangle reading as a UI card.
        /// </summary>
        private static Drawable screw(Anchor corner) => new Container
        {
            Anchor = corner,
            Origin = corner,
            Size = new Vector2(screw_size),
            Margin = new MarginPadding(screw_inset),
            Masking = true,
            CornerRadius = screw_size / 2,
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = ColourInfo.GradientVertical(
                        RetroPalette.Chrome.Opacity(0.75f),
                        RetroPalette.ChromeDark.Darken(0.2f)),
                },
                // The slot, turned a little off true. Every screw at the same
                // angle looks machined; a few degrees apart looks fitted.
                new Box
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.X,
                    Height = 1.2f,
                    Width = 0.62f,
                    Rotation = slotAngle(corner),
                    Colour = new Color4(0.12f, 0.11f, 0.15f, 0.85f),
                },
            },
        };

        private static float slotAngle(Anchor corner) => corner switch
        {
            Anchor.TopLeft => 28,
            Anchor.TopRight => -14,
            Anchor.BottomLeft => -37,
            _ => 19,
        };
    }
}
