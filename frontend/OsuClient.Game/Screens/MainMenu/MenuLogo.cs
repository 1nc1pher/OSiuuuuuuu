using System;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using OsuClient.Game.Graphics;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.MainMenu
{
    /// <summary>
    /// The menu's centrepiece: a bold white-outlined circle reading OTO, its
    /// interior cycling slowly through the colour spectrum, pulsing on the
    /// beat of whatever is playing (MENU_REDESIGN_PLAN.md steps 3–4).
    ///
    /// Clicking it is how the menu opens; see <see cref="Clicked"/>.
    ///
    /// Two separate things want to scale this drawable — the beat pulse, many
    /// times a second, and the open/close transition, once per click. They are
    /// kept on different containers on purpose: a single <see cref="Scale"/>
    /// written by both ends up with one overwriting the other mid-transform,
    /// which reads as the logo stuttering every beat while it opens. The beat
    /// drives <see cref="pulse"/> (internal); the transition is free to drive
    /// this drawable's own <see cref="Scale"/> from outside.
    /// </summary>
    public partial class MenuLogo : CompositeDrawable
    {
        /// <summary>
        /// Seconds for one full trip around the colour wheel. Slow enough that
        /// the colour is never seen to move, only to have moved.
        /// </summary>
        private const double hue_cycle_seconds = 26;

        /// <summary>
        /// How far apart the two ends of the fill's gradient sit on the hue
        /// wheel. A flat fill reads as "a coloured circle"; a spread of about
        /// this much reads as a slice of a spectrum.
        /// </summary>
        private const float hue_spread = 46;

        private const float saturation = 0.72f;
        private const float value = 0.96f;

        /// <summary>Outline weight, as a fraction of the circle's diameter.</summary>
        private const float border_fraction = 0.026f;

        /// <summary>Width of the OTO lettering, as a fraction of the diameter.</summary>
        private const float text_fraction = 0.46f;

        /// <summary>
        /// Size the lettering is rasterized at, once. <see cref="RetroText"/>
        /// re-rasterizes to a new texture on every size change, so the sprite
        /// is scaled to fit instead — otherwise dragging the window edge would
        /// rebuild the glyph texture on every frame of the drag.
        /// </summary>
        private const float text_raster_size = 110;

        /// <summary>How far the logo grows on a beat, at full intensity.</summary>
        private const float beat_scale = 0.055f;

        private readonly Container pulse;
        private readonly Circle fill;
        private readonly CircularContainer outline;
        private readonly RetroText lettering;

        private double hue;

        /// <summary>
        /// The fill's current position on the colour wheel, in degrees.
        ///
        /// Exposed so the rest of the screen can be dressed in the same
        /// colour the logo happens to be wearing — the background tint and
        /// the beat flash both follow this.
        /// </summary>
        public float Hue => (float)hue;

        /// <summary>How far apart the fill's two gradient stops sit, in degrees.</summary>
        public static float HueSpread => hue_spread;

        /// <summary>
        /// Latest beat strength, 0-1. Stored rather than applied directly:
        /// the glow is a function of this *and* hover, and two writers on one
        /// EdgeEffect is the same trap as two writers on one Scale.
        /// </summary>
        private float beat;

        /// <summary>Raised when the circle itself is clicked.</summary>
        public Action? Clicked;

        public MenuLogo()
        {
            InternalChild = pulse = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Children = new Drawable[]
                {
                    fill = new Circle
                    {
                        RelativeSizeAxes = Axes.Both,
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                    },
                    // A dark wash over the fill: the spectrum at full value is
                    // bright enough to swallow white lettering, and knocking
                    // it back here keeps OTO readable at every hue without
                    // desaturating the colour itself.
                    new Circle
                    {
                        RelativeSizeAxes = Axes.Both,
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Colour = new Color4(0f, 0f, 0f, 0.30f),
                    },
                    outline = new CircularContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Masking = true,
                        BorderColour = Color4.White,
                        EdgeEffect = new EdgeEffectParameters
                        {
                            Type = EdgeEffectType.Glow,
                            Colour = Color4.White.Opacity(0.25f),
                            Radius = 18,
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
                        Text = "OTO",
                        Colour = Color4.White,
                    },
                },
            };
        }

        /// <summary>
        /// Drives the beat pulse, 0 (between beats) to 1 (on the beat). The
        /// menu screen feeds this from <see cref="Gameplay.BeatPulse"/>.
        /// </summary>
        public void SetBeat(double intensity)
        {
            beat = (float)Math.Clamp(intensity, 0, 1);
        }

        protected override void Update()
        {
            base.Update();

            advanceHue();
            updateGlow();
            layout();
        }

        /// <summary>
        /// The outline's glow, swelling with the beat and lifting on hover.
        ///
        /// Recomputed here every frame rather than transformed, because the
        /// beat rewrites it many times a second — a hover tween would be
        /// overwritten the moment it started.
        /// </summary>
        private void updateGlow()
        {
            pulse.Scale = new Vector2(1 + beat_scale * beat);

            float hoverLift = IsHovered ? 1 : 0;

            var edge = outline.EdgeEffect;
            edge.Radius = 14 + 26 * beat + 10 * hoverLift;
            edge.Colour = Color4.White.Opacity(0.18f + 0.30f * beat + 0.18f * hoverLift);
            outline.EdgeEffect = edge;
        }

        /// <summary>
        /// Walks the fill's hue forward by wall-clock time.
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

            fill.Colour = ColourInfo.GradientVertical(
                Color4Extensions.FromHSV((float)hue, saturation, value),
                Color4Extensions.FromHSV((float)((hue + hue_spread) % 360), saturation, value));
        }

        private void layout()
        {
            float diameter = Math.Min(DrawWidth, DrawHeight);

            if (diameter <= 0)
                return;

            outline.BorderThickness = MathF.Max(2, diameter * border_fraction);

            // Scaled rather than re-rasterized — see text_raster_size.
            if (lettering.Width > 0)
            {
                float target = diameter * text_fraction / lettering.Width;
                lettering.Scale = new Vector2(target);
            }
        }

        /// <summary>
        /// Restricts clicks to the circle, not its bounding box. Without this
        /// the corners of the square the circle is drawn in would swallow
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
