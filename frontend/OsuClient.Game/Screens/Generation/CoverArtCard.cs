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

namespace OsuClient.Game.Screens.Generation
{
    /// <summary>
    /// The cover art slot beside the tape: a cassette case with its paper
    /// insert — the J-card — which is where a tape's picture has always gone.
    ///
    /// <para>
    /// Empty, the insert is blank cream stock with a printed frame where the
    /// art would go and a line saying how to fill it. With an image it is the
    /// image, cropped to fill, behind the case's plastic. The spine down the
    /// left edge always reads COVER ART, printed on the same paper and with
    /// the same magenta band as the tape's own label, so the two read as one
    /// kit rather than a cassette and an unrelated panel.
    /// </para>
    ///
    /// <para>
    /// Like <see cref="CassetteBay"/>, this owns no file logic: the screen
    /// decides what an acceptable image is and tells this what to show.
    /// </para>
    /// </summary>
    public partial class CoverArtCard : ClickableContainer
    {
        public static readonly Vector2 CaseSize = new Vector2(300, 292);

        private const float spine_width = 30;
        private const float insert_margin = 10;

        private static readonly Color4 case_edge = RetroPalette.Chrome.Opacity(0.45f);

        private readonly Container artArea;
        private readonly Container emptyInsert;
        private readonly ClearTab clearTab;

        private Drawable? art;
        private string? imagePath;

        /// <summary>Raised when the clear tab is pressed.</summary>
        public Action? ClearRequested;

        public CoverArtCard()
        {
            Size = CaseSize;
            Masking = true;
            CornerRadius = 6;
            BorderThickness = 2;
            BorderColour = case_edge;
            EdgeEffect = new EdgeEffectParameters
            {
                Type = EdgeEffectType.Shadow,
                Colour = Color4.Black.Opacity(0.55f),
                Radius = 14,
                Offset = new Vector2(0, 5),
            };

            Children = new Drawable[]
            {
                // The case: a dark recess, like the bay the tape sits in.
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = ColourInfo.GradientVertical(
                        new Color4(0.08f, 0.07f, 0.11f, 0.92f),
                        new Color4(0.05f, 0.04f, 0.08f, 0.92f)),
                },
                new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding(insert_margin),
                    Children = new Drawable[]
                    {
                        createSpine(),
                        new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Padding = new MarginPadding { Left = spine_width },
                            Child = artArea = new Container
                            {
                                RelativeSizeAxes = Axes.Both,
                                Masking = true,
                                CornerRadius = 2,
                                Child = emptyInsert = createEmptyInsert(),
                            },
                        },
                    },
                },
                // The case's plastic: a faint diagonal glare over everything
                // inside, which is what makes the art read as behind a case
                // rather than printed on the panel.
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = ColourInfo.GradientHorizontal(Color4.White.Opacity(0.07f), Color4.White.Opacity(0f)),
                    Shear = new Vector2(-0.4f, 0),
                    Width = 0.45f,
                    X = 0.1f,
                    RelativePositionAxes = Axes.X,
                },
                clearTab = new ClearTab(() => ClearRequested?.Invoke())
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    Margin = new MarginPadding { Top = insert_margin + 6, Right = insert_margin + 6 },
                    Alpha = 0,
                },
            };
        }

        /// <summary>Whether an image is in the case. Exposed for tests.</summary>
        public bool HasImage => imagePath != null;

        /// <summary>
        /// Shows <paramref name="path"/> in the case, or empties it when given
        /// null. The image decodes in the background, and fades in once it has.
        /// </summary>
        public void SetImage(string? path)
        {
            if (path == imagePath)
                return;

            imagePath = path;

            var previous = art;
            art = null;
            previous?.FadeOut(200, Easing.OutQuint).Expire();

            if (path == null)
            {
                emptyInsert.FadeIn(200, Easing.OutQuint);
                clearTab.FadeOut(150, Easing.OutQuint);
                return;
            }

            var incoming = new BeatmapBackground(path) { Alpha = 0 };
            art = incoming;

            LoadComponentAsync(incoming, loaded =>
            {
                // A second pick can land while the first is still decoding;
                // only the newest belongs in the case.
                if (art != loaded)
                    return;

                artArea.Add(loaded);
                loaded.FadeIn(300, Easing.OutQuint);
                emptyInsert.FadeOut(300, Easing.OutQuint);
                clearTab.FadeIn(200, Easing.OutQuint);
            });
        }

        private static Drawable createSpine() => new Container
        {
            RelativeSizeAxes = Axes.Y,
            Width = spine_width - 4,
            Masking = true,
            CornerRadius = 2,
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = PaperTextBox.Paper,
                },
                // The label's magenta band, carried over from the tape.
                new Box
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 26,
                    Colour = RetroPalette.Magenta,
                },
                new RetroText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Rotation = -90,
                    Font = RetroFontFamily.Display,
                    TextSize = 10,
                    Colour = PaperTextBox.Ink,
                    Text = "COVER ART",
                },
            },
        };

        /// <summary>A blank insert: cream stock, a printed frame where the art goes, and how to fill it.</summary>
        private static Container createEmptyInsert() => new Container
        {
            RelativeSizeAxes = Axes.Both,
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = PaperTextBox.Paper,
                },
                new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding(12),
                    Child = new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Masking = true,
                        BorderThickness = 1.5f,
                        BorderColour = PaperTextBox.Ink.Opacity(0.25f),
                        Child = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Alpha = 0,
                            AlwaysPresent = true,
                        },
                    },
                },
                new FillFlowContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 10),
                    Children = new Drawable[]
                    {
                        new RetroText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Font = RetroFontFamily.Display,
                            TextSize = 26,
                            Colour = RetroPalette.Magenta.Opacity(0.7f),
                            Text = "+",
                        },
                        new RetroText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Font = RetroFontFamily.Display,
                            TextSize = 11,
                            Colour = PaperTextBox.Ink.Opacity(0.75f),
                            Text = "COVER ART",
                        },
                        new RetroText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Font = RetroFontFamily.Body,
                            TextSize = 9,
                            Colour = PaperTextBox.Ink.Opacity(0.5f),
                            Text = "DROP AN IMAGE OR CLICK",
                        },
                    },
                },
            },
        };

        protected override bool OnHover(HoverEvent e)
        {
            this.TransformTo(nameof(BorderColour), (ColourInfo)RetroPalette.Magenta.Opacity(0.8f), 120, Easing.OutQuint);
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            this.TransformTo(nameof(BorderColour), (ColourInfo)case_edge, 200, Easing.OutQuint);
            base.OnHoverLost(e);
        }

        /// <summary>The small tab that takes the art back out of the case.</summary>
        private partial class ClearTab : ClickableContainer
        {
            private readonly Box face;

            public ClearTab(Action action)
            {
                AutoSizeAxes = Axes.Both;
                Masking = true;
                CornerRadius = 3;
                BorderThickness = 1;
                BorderColour = RetroPalette.Chrome.Opacity(0.6f);
                Action = action;

                Children = new Drawable[]
                {
                    face = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = RetroPalette.PanelInset,
                    },
                    new RetroText
                    {
                        Margin = new MarginPadding { Horizontal = 8, Vertical = 5 },
                        Font = RetroFontFamily.Body,
                        TextSize = 9,
                        Colour = RetroPalette.Text,
                        Text = "CLEAR",
                    },
                };
            }

            protected override bool OnHover(HoverEvent e)
            {
                face.FadeColour(RetroPalette.Magenta.Opacity(0.5f), 100, Easing.OutQuint);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                face.FadeColour(RetroPalette.PanelInset, 150, Easing.OutQuint);
                base.OnHoverLost(e);
            }
        }
    }
}
