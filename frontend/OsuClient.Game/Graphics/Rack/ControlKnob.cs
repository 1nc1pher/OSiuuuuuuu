using System;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Graphics.Rack
{
    /// <summary>
    /// A knurled panel knob with a pointer and a tick arc.
    ///
    /// Built for the analyser's two detector controls — <c>margin</c> and
    /// <c>delta</c> — where turning one re-runs the ported peak-picker over
    /// the exported flux and the onset count changes live. A knob rather than
    /// a slider because the value being changed is a sensitivity, the arc
    /// shows its whole range at a glance, and because it is the control this
    /// room would actually have.
    ///
    /// <para>
    /// Dragged vertically or scrolled; <see cref="Current"/> is a
    /// <see cref="BindableFloat"/> so a panel can watch it without a
    /// hand-wired callback, and so nothing here animates a plain property
    /// through <c>TransformTo(nameof(...))</c>, which fails silently.
    /// </para>
    ///
    /// <para>
    /// The sweep deliberately stops short of a full turn: a real panel knob
    /// has end stops, and a pointer that can pass its own minimum gives no
    /// clue which way round the range runs.
    /// </para>
    /// </summary>
    public partial class ControlKnob : CompositeDrawable
    {
        /// <summary>Degrees of travel between minimum and maximum, centred on straight up.</summary>
        private const float sweep = 280;

        /// <summary>Pixels of drag for the full range. Roughly a panel's height, so a full sweep is one comfortable gesture.</summary>
        private const float drag_range = 220;

        private const int knurl_count = 20;
        private const int tick_count = 11;

        private readonly Container knob;
        private readonly Container pointer;
        private readonly RetroText valueText;
        private readonly RetroText legend;

        private float minimum;
        private float maximum = 1;
        private float dragStartValue;

        /// <summary>The knob's position in its range.</summary>
        public BindableFloat Current { get; } = new BindableFloat();

        /// <summary>How the value is written under the knob. Defaults to two decimals.</summary>
        public Func<float, string> Format { get; set; } = value => value.ToString("0.00");

        public ControlKnob(string label, float min, float max, float initial)
        {
            minimum = min;
            maximum = Math.Max(min + 1e-6f, max);

            AutoSizeAxes = Axes.Both;

            InternalChild = new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 5),
                Children = new Drawable[]
                {
                    new Container
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Size = new Vector2(64),
                        Children = new Drawable[]
                        {
                            tickArc(),
                            knob = new Container
                            {
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                Size = new Vector2(44),
                                Masking = true,
                                CornerRadius = 22,
                                BorderThickness = 1.5f,
                                BorderColour = RetroPalette.ChromeDark,
                                EdgeEffect = new EdgeEffectParameters
                                {
                                    Type = EdgeEffectType.Shadow,
                                    Colour = Color4.Black.Opacity(0.5f),
                                    Radius = 5,
                                    Offset = new Vector2(0, 2),
                                },
                                Children = new Drawable[]
                                {
                                    new Box
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        Colour = ColourInfo.GradientVertical(
                                            RetroPalette.Chrome.Darken(0.1f),
                                            RetroPalette.ChromeDark.Darken(0.35f)),
                                    },
                                    pointer = new Container
                                    {
                                        // Centred on both axes, so Rotation
                                        // turns the pointer about the knob's
                                        // spindle. Left at the default
                                        // top-left origin it swings the
                                        // pointer out of the masked face
                                        // entirely and every knob looks
                                        // identical whatever its value.
                                        Anchor = Anchor.Centre,
                                        Origin = Anchor.Centre,
                                        RelativeSizeAxes = Axes.Both,
                                        Child = new Box
                                        {
                                            Anchor = Anchor.TopCentre,
                                            Origin = Anchor.TopCentre,
                                            Y = 3,
                                            Size = new Vector2(3f, 15),
                                            Colour = RetroPalette.Amber,
                                        },
                                    },
                                },
                            },
                            knurling(),
                        },
                    },
                    valueText = new RetroText
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Font = RetroFontFamily.Display,
                        TextSize = 10,
                        Colour = RetroPalette.Amber,
                    },
                    legend = new RetroText
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Font = RetroFontFamily.Body,
                        TextSize = 10,
                        Colour = RetroPalette.TextDim,
                        Text = label,
                    },
                },
            };

            Current.Value = Math.Clamp(initial, minimum, maximum);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            Current.BindValueChanged(_ => applyValue(), true);
        }

        /// <summary>How many detents the knob's travel is divided into, for <see cref="Detented"/>.</summary>
        public const int Detents = 24;

        /// <summary>
        /// Raised with the knob's position, 0 to 1, each time a hand on it
        /// turns it across a detent — never when something sets it, like a
        /// preset, which is a jump and not a turn.
        /// </summary>
        public event Action<float>? Detented;

        private int lastDetent = -1;

        private void turnedByHand()
        {
            int detent = (int)MathF.Round(Fraction * Detents);

            if (detent == lastDetent)
                return;

            lastDetent = detent;
            Detented?.Invoke(Fraction);
        }

        /// <summary>Where the knob sits in its range, 0 to 1.</summary>
        public float Fraction => (Current.Value - minimum) / (maximum - minimum);

        private void applyValue()
        {
            float clamped = Math.Clamp(Current.Value, minimum, maximum);

            if (clamped != Current.Value)
            {
                Current.Value = clamped;
                return;
            }

            pointer.Rotation = -sweep / 2 + Fraction * sweep;
            valueText.Text = Format(Current.Value);
        }

        private void nudge(float byFractionOfRange) =>
            Current.Value = Math.Clamp(Current.Value + byFractionOfRange * (maximum - minimum),
                                       minimum, maximum);

        protected override bool OnDragStart(DragStartEvent e)
        {
            dragStartValue = Current.Value;

            // Starting from wherever a preset may have left it.
            lastDetent = (int)MathF.Round(Fraction * Detents);
            return true;
        }

        protected override void OnDrag(DragEvent e)
        {
            // Up is more. Vertical drag rather than a circular gesture: on a
            // knob this small, following the mouse round the circle means the
            // value leaps as the pointer crosses the centre.
            float delta = -(e.MousePosition.Y - e.MouseDownPosition.Y) / drag_range;

            Current.Value = Math.Clamp(dragStartValue + delta * (maximum - minimum),
                                       minimum, maximum);

            turnedByHand();
        }

        protected override bool OnScroll(ScrollEvent e)
        {
            lastDetent = (int)MathF.Round(Fraction * Detents);

            nudge(e.ScrollDelta.Y * 0.02f);
            turnedByHand();
            return true;
        }

        /// <summary>The ring of marks that makes the rim read as knurled metal rather than a disc.</summary>
        private static Drawable knurling()
        {
            var container = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
            };

            for (int i = 0; i < knurl_count; i++)
            {
                float angle = i / (float)knurl_count * MathF.Tau;

                container.Add(new Box
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(2.5f, 7),
                    Position = new Vector2(MathF.Sin(angle), -MathF.Cos(angle)) * 21.5f,
                    Rotation = angle * 180 / MathF.PI,
                    Colour = RetroPalette.ChromeDark.Darken(0.55f).Opacity(0.9f),
                });
            }

            return container;
        }

        /// <summary>Tick marks around the knob showing the extent of its travel.</summary>
        private static Drawable tickArc()
        {
            var container = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
            };

            for (int i = 0; i < tick_count; i++)
            {
                float fraction = i / (float)(tick_count - 1);
                float degrees = -sweep / 2 + fraction * sweep;
                float angle = degrees * MathF.PI / 180;

                container.Add(new Box
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(2f, i == 0 || i == tick_count - 1 ? 8 : 5),
                    Position = new Vector2(MathF.Sin(angle), -MathF.Cos(angle)) * 28f,
                    Rotation = degrees,
                    Colour = RetroPalette.TextDim.Opacity(i == 0 || i == tick_count - 1 ? 0.95f : 0.5f),
                });
            }

            return container;
        }
    }
}
