using System;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using OsuClient.Game.Graphics;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.MainMenu
{
    /// <summary>
    /// The menu's centrepiece: a record on the turntable, reading RIMO — the
    /// same vinyl the song wheel is made of, so the client opens on the
    /// object it is built around (MENU_REDESIGN_PLAN.md steps 3–4).
    ///
    /// <para>
    /// Black grooved vinyl, with the brighter gaps between tracks an LP has,
    /// spinning at a real 33 RPM. Concentric grooves look the same at any
    /// angle, so a few faint arcs of groove sheen ride on it: they are what
    /// is seen going round. The light across the grooves does not turn —
    /// light off a spinning record stays where the lamp is.
    ///
    /// The centre does not turn either. Its label carries the menu's colour —
    /// the spectrum it walks through, which the rest of the screen dresses in
    /// (see <see cref="Hue"/>) — with RIMO on it and nothing else: a logo
    /// that has to be read cannot go round. There is no beat pulse; the spin
    /// is the motion.
    /// </para>
    ///
    /// <para>
    /// Clicking it is how the menu opens; see <see cref="Clicked"/>.
    /// </para>
    ///
    /// <para>
    /// The open/close transition drives this drawable's own
    /// <see cref="Scale"/> from outside; nothing inside scales it.
    /// </para>
    /// </summary>
    public partial class MenuLogo : CompositeDrawable
    {
        /// <summary>
        /// Seconds for one full trip around the colour wheel. Slow enough that
        /// the colour is never seen to move, only to have moved.
        /// </summary>
        private const double hue_cycle_seconds = 26;

        /// <summary>
        /// How far apart the two ends of the label's gradient sit on the hue
        /// wheel. A flat colour reads as a sticker; a spread of about this much
        /// reads as a slice of a spectrum.
        /// </summary>
        private const float hue_spread = 46;

        private const float saturation = 0.72f;
        private const float value = 0.96f;

        /// <summary>The record's speed, in degrees a second: 33⅓ RPM, a real LP's.</summary>
        private const float spin_speed = 360 * (100f / 3) / 60;

        /// <summary>The label's diameter, as a fraction of the record's.</summary>
        private const float label_fraction = 0.46f;

        /// <summary>Rim weight, as a fraction of the diameter.</summary>
        private const float rim_fraction = 0.012f;

        /// <summary>Width of the RIMO lettering, as a fraction of the record's diameter — it sits on the label.</summary>
        private const float text_fraction = 0.34f;

        /// <summary>
        /// Size the lettering is rasterized at, once. <see cref="RetroText"/>
        /// re-rasterizes to a new texture on every size change, so the sprite
        /// is scaled to fit instead — otherwise dragging the window edge would
        /// rebuild the glyph texture on every frame of the drag.
        /// </summary>
        private const float text_raster_size = 110;


        /// <summary>
        /// The arcs of groove sheen that show the spin: where each sits (as a
        /// fraction of the diameter), where it starts, how much of the way
        /// round it runs, and how bright it is.
        /// </summary>
        private static readonly (float Size, float Start, float Length, float Alpha)[] sheen_arcs =
        {
            (0.93f, 0, 0.22f, 0.14f),
            (0.86f, 140, 0.14f, 0.10f),
            (0.79f, 250, 0.26f, 0.12f),
            (0.72f, 60, 0.12f, 0.16f),
            (0.64f, 200, 0.2f, 0.10f),
            (0.57f, 320, 0.16f, 0.13f),
        };

        /// <summary>
        /// Groove rings, from the rim in towards the label. Every ring is a
        /// faint line; the three marked ones are the brighter bands between
        /// tracks that make a record read as an LP rather than a black disc.
        /// </summary>
        private static readonly (float Size, float Alpha)[] grooves = buildGrooves();

        private readonly Container record;
        private readonly Circle label;
        private readonly CircularContainer rim;
        private readonly RetroText lettering;

        private double hue;

        /// <summary>
        /// The label's current position on the colour wheel, in degrees.
        ///
        /// Exposed so the rest of the screen can be dressed in the same
        /// colour the record happens to be wearing — the background tint and
        /// the beat flash both follow this.
        /// </summary>
        public float Hue => (float)hue;

        /// <summary>How far apart the label's two gradient stops sit, in degrees.</summary>
        public static float HueSpread => hue_spread;

        /// <summary>The record's current turn, in degrees. Exposed for tests.</summary>
        public float Spin => record.Rotation;

        /// <summary>Raised when the record itself is clicked.</summary>
        public Action? Clicked;

        /// <param name="labelArt">
        /// A picture for the label in place of RIMO — a song's cover art, for
        /// a record that is that song (the cue screen's). Cropped to the
        /// label's circle. Null keeps the menu's own label.
        /// </param>
        public MenuLogo(string? labelArt = null)
        {
            record = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Children = new Drawable[]
                {
                    // The vinyl: not flat black, a touch of depth top to bottom.
                    new Circle
                    {
                        RelativeSizeAxes = Axes.Both,
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Colour = ColourInfo.GradientVertical(
                            new Color4(0.09f, 0.08f, 0.11f, 1f),
                            new Color4(0.03f, 0.03f, 0.05f, 1f)),
                    },
                },
            };

            foreach (var (size, alpha) in grooves)
            {
                record.Add(new CircularContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(size),
                    Masking = true,
                    BorderThickness = 1.2f,
                    BorderColour = Color4.White.Opacity(alpha),
                    Child = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0, AlwaysPresent = true },
                });
            }

            // Arcs of groove sheen, turning with the record: the grooves are
            // circles and look the same at any angle, so these are what is
            // seen going round.
            foreach (var (size, start, length, alpha) in sheen_arcs)
            {
                record.Add(new CircularProgress
                {
                    RelativeSizeAxes = Axes.Both,
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(size),
                    Rotation = start,
                    Progress = length,
                    InnerRadius = 0.012f,
                    RoundedCaps = true,
                    Colour = Color4.White.Opacity(alpha),
                });
            }

            InternalChildren = new Drawable[]
            {
                record,
                createSheen(),
                // The label, and everything on it, holds still while the
                // record turns under it.
                label = new Circle
                {
                    RelativeSizeAxes = Axes.Both,
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(label_fraction),
                },
                // A dark wash over it: the spectrum at full value swallows
                // white lettering, and knocking it back keeps RIMO readable
                // at every hue without desaturating the colour itself.
                new Circle
                {
                    RelativeSizeAxes = Axes.Both,
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(label_fraction),
                    Colour = new Color4(0f, 0f, 0f, 0.28f),
                },
                // The song's own cover on the label, when this record is a
                // song: a portion of it, cropped to the circle, under a light
                // wash so it sits on the vinyl rather than glowing off it.
                labelArt == null
                    ? Empty()
                    : new CircularContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Size = new Vector2(label_fraction),
                        Masking = true,
                        Children = new Drawable[]
                        {
                            new CoverSprite(labelArt),
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = new Color4(0f, 0f, 0f, 0.12f),
                            },
                        },
                    },
                // The printed ring round the label's edge.
                new CircularContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(label_fraction * 0.9f),
                    Masking = true,
                    BorderThickness = 1.5f,
                    BorderColour = Color4.White.Opacity(0.35f),
                    Child = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0, AlwaysPresent = true },
                },
                rim = new CircularContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Masking = true,
                    BorderColour = Color4.White.Opacity(0.9f),
                    EdgeEffect = new EdgeEffectParameters
                    {
                        Type = EdgeEffectType.Glow,
                        Colour = Color4.White.Opacity(0.18f),
                        Radius = 14,
                        Hollow = true,
                    },
                    Child = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Alpha = 0,
                        AlwaysPresent = true,
                    },
                },
                lettering = new RetroText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Font = RetroFontFamily.Display,
                    TextSize = text_raster_size,
                    Text = "RIMO",
                    Colour = Color4.White,
                    // A record with a cover on its label is that song's: no RIMO.
                    Alpha = labelArt == null ? 1 : 0,
                },
            };
        }

        /// <summary>
        /// The light off the grooves: two soft bands crossing the record on
        /// opposite sides of the spindle, held still over the turning vinyl.
        /// Stops short of the label, which is paper and does not shine.
        /// </summary>
        private static Drawable createSheen()
        {
            var sheen = new CircularContainer
            {
                RelativeSizeAxes = Axes.Both,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Masking = true,
                Rotation = -32,
            };

            // One band either side of the label, each starting at the label's
            // edge and running out to the rim.
            foreach (bool left in new[] { true, false })
            {
                sheen.Add(new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    RelativePositionAxes = Axes.X,
                    Anchor = Anchor.Centre,
                    Origin = left ? Anchor.CentreRight : Anchor.CentreLeft,
                    X = (left ? -1 : 1) * label_fraction / 2,
                    Width = 0.5f - label_fraction / 2,
                    Height = 0.16f,
                    Colour = ColourInfo.GradientVertical(Color4.White.Opacity(0f), Color4.White.Opacity(0.09f)),
                });
            }

            return sheen;
        }

        private static (float, float)[] buildGrooves()
        {
            var rings = new System.Collections.Generic.List<(float, float)>();

            int index = 0;

            for (float size = 0.97f; size > label_fraction + 0.02f; size -= 0.018f, index++)
            {
                // A track gap every so often: a brighter, cleaner band.
                bool gap = index is 6 or 13 or 20;

                rings.Add((size, gap ? 0.16f : index % 2 == 0 ? 0.06f : 0.035f));
            }

            return rings.ToArray();
        }

        /// <summary>
        /// The beat, 0 (between beats) to 1 (on the beat), as the menu screen
        /// reads it. The record no longer pulses to it — the spin is the
        /// motion — but the screen still offers it, and the edge flash and
        /// the spectrum ring still use it.
        /// </summary>
        public void SetBeat(double intensity)
        {
        }

        protected override void Update()
        {
            base.Update();

            record.Rotation = (record.Rotation + (float)Time.Elapsed / 1000f * spin_speed) % 360;

            advanceHue();
            updateGlow();
            layout();
        }

        /// <summary>
        /// The rim's glow: steady, lifting under the cursor. It no longer
        /// swells with the beat — that read as the same heartbeat as the pulse
        /// it went with.
        /// </summary>
        private void updateGlow()
        {
            float hoverLift = IsHovered ? 1 : 0;

            var edge = rim.EdgeEffect;
            edge.Radius = 14 + 10 * hoverLift;
            edge.Colour = Color4.White.Opacity(0.18f + 0.18f * hoverLift);
            rim.EdgeEffect = edge;
        }

        /// <summary>
        /// Walks the label's hue forward by wall-clock time.
        ///
        /// Not a <c>FadeColour</c> loop: colour transforms interpolate in RGB,
        /// so a tween from one hue to its opposite passes through grey rather
        /// than through the colours between them, and a convincing spectrum
        /// would need the whole wheel chained up in six-segment pieces.
        /// Stepping the hue directly is both shorter and actually a spectrum.
        /// </summary>
        private void advanceHue()
        {
            hue = (hue + Time.Elapsed / (hue_cycle_seconds * 1000) * 360) % 360;

            label.Colour = ColourInfo.GradientVertical(
                Color4Extensions.FromHSV((float)hue, saturation, value),
                Color4Extensions.FromHSV((float)((hue + hue_spread) % 360), saturation, value));
        }

        private void layout()
        {
            float diameter = Math.Min(DrawWidth, DrawHeight);

            if (diameter <= 0)
                return;

            rim.BorderThickness = MathF.Max(2, diameter * rim_fraction);

            // Scaled rather than re-rasterized — see text_raster_size.
            if (lettering.Width > 0)
                lettering.Scale = new Vector2(diameter * text_fraction / lettering.Width);
        }

        /// <summary>
        /// Restricts clicks to the record, not its bounding box. Without this
        /// the corners of the square the record is drawn in would swallow
        /// clicks aimed at whatever sits behind the logo.
        /// </summary>
        public override bool ReceivePositionalInputAt(Vector2 screenSpacePos)
        {
            var local = ToLocalSpace(screenSpacePos);
            var centre = DrawSize / 2;
            float radius = Math.Min(DrawWidth, DrawHeight) / 2;

            return (local - centre).LengthSquared <= radius * radius;
        }

        protected override bool OnClick(ClickEvent e)
        {
            Clicked?.Invoke();
            return true;
        }

        protected override bool OnHover(HoverEvent e) => false;
    }
}
