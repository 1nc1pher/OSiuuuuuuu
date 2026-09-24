using System;
using osu.Framework.Bindables;
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
    /// A chrome paddle switch with a lit legend beside it — one trace on or
    /// off in the analyser's rack.
    ///
    /// The paddle throws between two positions rather than sliding, and the
    /// legend lights rather than changing text: both are what a panel switch
    /// does, and both make the state readable in a still frame, which a
    /// colour-only toggle is not.
    ///
    /// <para>
    /// <see cref="Active"/> is a <see cref="BindableBool"/> so the panel
    /// holding a row of these can watch them without each one needing a
    /// callback wired by hand — and so the animation is driven by a bindable
    /// rather than by <c>TransformTo(nameof(...))</c>, which resolves by name
    /// at runtime and fails silently when it cannot.
    /// </para>
    /// </summary>
    public partial class ToggleSwitch : ClickableContainer
    {
        private const float body_width = 22;
        private const float body_height = 34;
        private const float paddle_inset = 3;
        private const double throw_duration = 90;

        /// <summary>Whether the switch is thrown. Bind to it rather than polling.</summary>
        public BindableBool Active { get; } = new BindableBool();

        private readonly Container paddle;
        private readonly Box paddleFace;
        private readonly RetroText legend;
        private readonly IndicatorLamp lamp;

        private Color4 accent = RetroPalette.Cyan;

        public ToggleSwitch(string label)
        {
            AutoSizeAxes = Axes.Both;

            InternalChild = new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(9, 0),
                Children = new Drawable[]
                {
                    new Container
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Size = new Vector2(body_width, body_height),
                        Masking = true,
                        CornerRadius = 4,
                        BorderThickness = 1,
                        BorderColour = RetroPalette.ChromeDark,
                        Children = new Drawable[]
                        {
                            // The recess the paddle sits in.
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = new Color4(0.09f, 0.08f, 0.12f, 1f),
                            },
                            paddle = new Container
                            {
                                RelativeSizeAxes = Axes.X,
                                Height = body_height / 2 - paddle_inset,
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                Y = paddle_inset,
                                Padding = new MarginPadding { Horizontal = paddle_inset },
                                Child = new Container
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Masking = true,
                                    CornerRadius = 2,
                                    Child = paddleFace = new Box
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        Colour = ColourInfo.GradientVertical(
                                            RetroPalette.Chrome,
                                            RetroPalette.ChromeDark),
                                    },
                                },
                            },
                        },
                    },
                    lamp = new IndicatorLamp
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Size = new Vector2(8),
                    },
                    legend = new RetroText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Font = RetroFontFamily.Body,
                        TextSize = 11,
                        Text = label,
                    },
                },
            };

            Action = () =>
            {
                Active.Toggle();
                Flicked?.Invoke(Active.Value);
            };
        }

        /// <summary>Raised when a hand throws the switch, with where it went.</summary>
        public event Action<bool>? Flicked;

        /// <summary>The colour this switch lights in when thrown — usually the trace's own.</summary>
        public Color4 Accent
        {
            get => accent;
            set
            {
                accent = value;
                lamp.LampColour = value;
                applyState(false);
            }
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            Active.BindValueChanged(_ => applyState(true), true);
        }

        private void applyState(bool animate)
        {
            double duration = animate ? throw_duration : 0;

            // Up is on. The paddle occupies the top or bottom half of the
            // recess, so the two states differ in silhouette and not only in
            // brightness.
            paddle.MoveToY(Active.Value ? paddle_inset : body_height / 2, duration, Easing.OutQuint);

            // Thrown up catches the light along its top edge; thrown down is
            // in shadow, and the gradient flips as well as darkening. Two
            // positions that differ only in brightness are hard to read in a
            // still frame, which is most of how this screen gets checked.
            paddleFace.FadeColour(Active.Value
                ? ColourInfo.GradientVertical(RetroPalette.Chrome.Lighten(0.25f), RetroPalette.ChromeDark)
                : ColourInfo.GradientVertical(RetroPalette.ChromeDark.Darken(0.55f),
                                              RetroPalette.ChromeDark.Darken(0.15f)),
                duration);

            legend.FadeColour(Active.Value ? accent : RetroPalette.TextDim.Opacity(0.6f), duration);

            lamp.State = Active.Value ? LampState.Lit : LampState.Off;
        }
    }
}
