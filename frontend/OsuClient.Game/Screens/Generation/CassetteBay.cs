using System;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using OsuClient.Game.Graphics;
using OsuClient.Game.Graphics.Rack;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.Generation
{
    /// <summary>
    /// The deck's loading bay and the cassette in it — the whole of the
    /// upload form, as one object.
    ///
    /// <para>
    /// The unification is the point. A file to generate from and the metadata
    /// to label it with are not two unrelated fields on a form; they are a
    /// tape and what is written on the tape. Dropping a file slides the
    /// cassette into the bay and its reels begin to turn; the artist and title
    /// are written on its paper label, in the same ink-on-cream the song
    /// select search bar uses.
    /// </para>
    ///
    /// <para>
    /// This draws no file dialog and owns no file logic: the screen still
    /// decides what a valid file is and tells this what to show. All it knows
    /// is whether a tape is loaded and what is written on it.
    /// </para>
    /// </summary>
    public partial class CassetteBay : CompositeDrawable
    {
        /// <summary>The cassette's size. A real compact cassette is about 10:6.</summary>
        private static readonly Vector2 shell_size = new Vector2(430, 272);

        // The shell is laid out top to bottom in three bands that never
        // overlap, the way a real cassette is: the paper label, then the
        // reels and the tape window between them, then the head opening.
        // Everything is measured from the shell's top edge so the bands can
        // be checked against each other by reading the numbers.

        private const float label_top = 14;
        private const float label_height = 104;

        private const float reel_size = 84;

        /// <summary>Reel centres sit this far down the shell: clear of the label, clear of the head opening.</summary>
        private const float reel_centre = label_top + label_height + 12 + reel_size / 2;

        private const float head_opening_height = 30;

        /// <summary>How far below the bay the cassette waits before a file is chosen.</summary>
        private const float ejected_offset = 40;

        private const double insert_duration = 420;

        private readonly Container shell;
        private readonly Drawable emptyLegend;
        private readonly RetroText legendTitle;
        private readonly Container interiorLamp;

        // Assigned inside createShell/createLabel, which C# does not treat as
        // the constructor body, so these cannot be readonly.
        private TapeReel leftReel = null!;
        private TapeReel rightReel = null!;
        private RetroText fileName = null!;

        private bool loaded;

        private BayRecess recess = null!;

        public CassetteBay()
        {
            RelativeSizeAxes = Axes.Both;

            InternalChildren = new Drawable[]
            {
                // The bay: a recess in the deck, lit from inside. Clickable
                // while it is empty, to browse for a tape.
                new BayRecess(this)
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = shell_size + new Vector2(46, 40),
                    Masking = true,
                    CornerRadius = 8,
                    BorderThickness = 2,
                    BorderColour = RetroPalette.ChromeDark,
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = ColourInfo.GradientVertical(
                                new Color4(0.05f, 0.04f, 0.08f, 1f),
                                new Color4(0.09f, 0.08f, 0.13f, 1f)),
                        },
                        interiorLamp = new Container
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            RelativeSizeAxes = Axes.X,
                            Height = 3,
                            Y = 6,
                            Masking = true,
                            CornerRadius = 1.5f,
                            EdgeEffect = new EdgeEffectParameters
                            {
                                Type = EdgeEffectType.Glow,
                                Colour = RetroPalette.Amber.Opacity(0.35f),
                                Radius = 24,
                            },
                            Child = new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = RetroPalette.Amber.Opacity(0.5f),
                            },
                        },
                        emptyLegend = new FillFlowContainer
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            AutoSizeAxes = Axes.Both,
                            Direction = FillDirection.Vertical,
                            Spacing = new Vector2(0, 10),
                            Children = new Drawable[]
                            {
                                legendTitle = new RetroText
                                {
                                    Anchor = Anchor.TopCentre,
                                    Origin = Anchor.TopCentre,
                                    Font = RetroFontFamily.Display,
                                    TextSize = 15,
                                    Colour = RetroPalette.TextDim.Opacity(0.75f),
                                    Text = "DROP A TAPE",
                                },
                                new RetroText
                                {
                                    Anchor = Anchor.TopCentre,
                                    Origin = Anchor.TopCentre,
                                    Font = RetroFontFamily.Body,
                                    TextSize = 9,
                                    Colour = RetroPalette.TextDim.Opacity(0.55f),
                                    Text = "OR CLICK TO BROWSE",
                                },
                            },
                        },
                    },
                },
                shell = createShell(),
            };

            shell.Y = ejected_offset;
            shell.Alpha = 0;
        }

        /// <summary>What is written in the artist field.</summary>
        public PaperTextBox ArtistBox { get; private set; } = null!;

        /// <summary>What is written in the title field.</summary>
        public PaperTextBox TitleBox { get; private set; } = null!;

        /// <summary>Whether a tape is in the bay. Exposed for tests.</summary>
        public bool HasTape => loaded;

        /// <summary>
        /// Raised when the empty bay is clicked. Only while it is empty: with
        /// a tape in, clicks on it belong to the label's fields, and a stray
        /// one beside a field should not throw up a dialog. LOAD TAPE still
        /// swaps a loaded tape.
        /// </summary>
        public Action? BrowseRequested;

        /// <summary>
        /// Shows a tape with this file written on it, or empties the bay when
        /// given null.
        /// </summary>
        public void SetFile(string? name)
        {
            bool nowLoaded = !string.IsNullOrEmpty(name);

            if (nowLoaded)
                fileName.Text = name!;

            if (nowLoaded == loaded)
                return;

            loaded = nowLoaded;

            // A hover lit while empty would otherwise stay lit under the tape.
            recess.SetLit(false);

            if (loaded)
            {
                shell.FadeIn(insert_duration / 2, Easing.OutQuint);
                shell.MoveToY(0, insert_duration, Easing.OutQuint);
                emptyLegend.FadeOut(insert_duration / 3, Easing.OutQuint);
                interiorLamp.FadeTo(0.35f, insert_duration, Easing.OutQuint);
            }
            else
            {
                shell.FadeOut(insert_duration / 2, Easing.OutQuint);
                shell.MoveToY(ejected_offset, insert_duration, Easing.OutQuint);
                emptyLegend.FadeIn(insert_duration / 2, Easing.OutQuint);
                interiorLamp.FadeTo(1f, insert_duration, Easing.OutQuint);
            }

            leftReel.Running = loaded;
            rightReel.Running = loaded;
        }

        private Container createShell()
        {
            return new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = shell_size,
                Masking = true,
                CornerRadius = 7,
                BorderThickness = 1.5f,
                BorderColour = RetroPalette.Chrome.Opacity(0.5f),
                EdgeEffect = new EdgeEffectParameters
                {
                    Type = EdgeEffectType.Shadow,
                    Colour = Color4.Black.Opacity(0.6f),
                    Radius = 14,
                    Offset = new Vector2(0, 5),
                },
                Children = new Drawable[]
                {
                    // Moulded plastic: top-lit, like the difficulty cassettes
                    // in song select.
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = ColourInfo.GradientVertical(
                            new Color4(0.20f, 0.17f, 0.26f, 1f),
                            new Color4(0.10f, 0.08f, 0.15f, 1f)),
                    },
                    leftReel = new TapeReel
                    {
                        Anchor = Anchor.TopLeft,
                        Origin = Anchor.Centre,
                        X = 84,
                        Y = reel_centre,
                        Size = new Vector2(reel_size),
                        TapeFill = 0.85f,
                    },
                    rightReel = new TapeReel
                    {
                        Anchor = Anchor.TopRight,
                        Origin = Anchor.Centre,
                        X = -84,
                        Y = reel_centre,
                        Size = new Vector2(reel_size),
                        TapeFill = 0.3f,
                        Reverse = true,
                    },
                    // The window the tape shows through, between the reels.
                    new Container
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.Centre,
                        Y = reel_centre,
                        Size = new Vector2(86, 34),
                        Masking = true,
                        CornerRadius = 3,
                        Children = new Drawable[]
                        {
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = new Color4(0.06f, 0.05f, 0.09f, 0.9f),
                            },
                            new Box
                            {
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                RelativeSizeAxes = Axes.X,
                                Height = 12,
                                Colour = new Color4(0.38f, 0.24f, 0.17f, 1f),
                            },
                        },
                    },
                    createHeadOpening(),
                    createLabel(),
                    screw(Anchor.TopLeft),
                    screw(Anchor.TopRight),
                    screw(Anchor.BottomLeft),
                    screw(Anchor.BottomRight),
                },
            };
        }

        /// <summary>
        /// The paper insert, and the form.
        ///
        /// Cream stock, very slightly crooked, exactly as the song select
        /// search bar is — it is the one warm physical object on a screen of
        /// dark panels, which is what stops it reading as another UI card.
        /// The fields are the ruled lines a tape label is printed with, each
        /// with its small printed caption, so they read as somewhere to write
        /// rather than as text floating on paper.
        /// </summary>
        private Container createLabel() => new Container
        {
            Anchor = Anchor.TopCentre,
            Origin = Anchor.TopCentre,
            Y = label_top,
            Width = shell_size.X - 40,
            Height = label_height,
            Rotation = -0.4f,
            Masking = true,
            CornerRadius = 3,
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = PaperTextBox.Paper,
                },
                // The coloured band printed down the edge of a tape insert.
                new Box
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    RelativeSizeAxes = Axes.Y,
                    Width = 7,
                    Colour = RetroPalette.Magenta,
                },
                // The side letter, printed large in the corner as on a real label.
                new RetroText
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    X = -12,
                    Y = 6,
                    Font = RetroFontFamily.Display,
                    TextSize = 18,
                    Colour = RetroPalette.Magenta.Opacity(0.8f),
                    Text = "A",
                },
                new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 4),
                    Padding = new MarginPadding { Left = 20, Right = 38, Top = 8, Bottom = 6 },
                    Children = new Drawable[]
                    {
                        fileName = new RetroText
                        {
                            Font = RetroFontFamily.Body,
                            TextSize = 10,
                            Colour = PaperTextBox.Ink.Opacity(0.55f),
                            Text = string.Empty,
                        },
                        new LabelLine("ARTIST", ArtistBox = new PaperTextBox()),
                        new LabelLine("TITLE", TitleBox = new PaperTextBox()),
                    },
                },
            },
        };

        /// <summary>
        /// The recess at the bottom of the shell where the tape runs past the
        /// deck's heads: a dark slot with the capstan and guide holes in it.
        /// It fills the space under the reels as it does on a real cassette,
        /// so that space reads as part of the object rather than as slack.
        /// </summary>
        private static Drawable createHeadOpening() => new Container
        {
            Anchor = Anchor.BottomCentre,
            Origin = Anchor.BottomCentre,
            Size = new Vector2(220, head_opening_height),
            Masking = true,
            CornerRadius = 4,
            BorderThickness = 1,
            BorderColour = RetroPalette.Chrome.Opacity(0.25f),
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(0.06f, 0.05f, 0.09f, 0.9f),
                },
                new FillFlowContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(26, 0),
                    Children = new[] { guideHole(8), guideHole(11), guideHole(14), guideHole(11), guideHole(8) },
                },
            },
        };

        private static Drawable guideHole(float size) => new Circle
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Size = new Vector2(size),
            Colour = RetroPalette.ChromeDark.Opacity(0.9f),
        };

        /// <summary>
        /// One ruled line on the label: a caption printed in the label's
        /// magenta spine colour, and a faint band of the same ink to write on,
        /// like the coloured stripes 80s tape labels were printed with. The
        /// rule under it inks in fully while its field has focus — the same
        /// accent as the caret — so it is always clear which line is being
        /// written on. No placeholder: the caption already says what goes
        /// there.
        /// </summary>
        private partial class LabelLine : CompositeDrawable
        {
            private const float caption_width = 70;

            private static readonly Color4 band_idle = RetroPalette.Magenta.Opacity(0.06f);
            private static readonly Color4 band_focused = RetroPalette.Magenta.Opacity(0.12f);
            private static readonly Color4 rule_idle = RetroPalette.Magenta.Opacity(0.45f);

            public LabelLine(string caption, PaperTextBox box)
            {
                Box band, rule;

                RelativeSizeAxes = Axes.X;
                Height = 30;

                box.RelativeSizeAxes = Axes.Both;

                InternalChildren = new Drawable[]
                {
                    new RetroText
                    {
                        Anchor = Anchor.BottomLeft,
                        Origin = Anchor.BottomLeft,
                        Y = -8,
                        Font = RetroFontFamily.Display,
                        TextSize = 9,
                        Colour = RetroPalette.Magenta.Darken(0.35f),
                        Text = caption,
                    },
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Padding = new MarginPadding { Left = caption_width - 6 },
                        Child = band = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = band_idle,
                        },
                    },
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Padding = new MarginPadding { Left = caption_width, Bottom = 2 },
                        Child = box,
                    },
                    rule = new Box
                    {
                        Anchor = Anchor.BottomLeft,
                        Origin = Anchor.BottomLeft,
                        RelativeSizeAxes = Axes.X,
                        Height = 2,
                        Colour = rule_idle,
                    },
                };

                box.FocusChanged += focused =>
                {
                    band.FadeColour(focused ? band_focused : band_idle, 150, Easing.OutQuint);
                    rule.FadeColour(focused ? RetroPalette.Magenta : rule_idle, 150, Easing.OutQuint);
                };
            }
        }

        /// <summary>
        /// The recess the tape sits in, and — while there is no tape — the
        /// thing to click to find one. Lit on hover the way the cover art
        /// case is: its edge and the lamp inside warm up, and the legend
        /// brightens, so it reads as a control and not just an empty slot.
        /// </summary>
        private partial class BayRecess : Container
        {
            private readonly CassetteBay bay;

            public BayRecess(CassetteBay bay)
            {
                this.bay = bay;
                bay.recess = this;
            }

            private bool active => !bay.loaded;

            public void SetLit(bool lit)
            {
                this.TransformTo(nameof(BorderColour),
                    (ColourInfo)(lit ? RetroPalette.Amber.Opacity(0.8f) : RetroPalette.ChromeDark), 150, Easing.OutQuint);

                bay.interiorLamp.FadeTo(lit ? 1f : bay.loaded ? 0.35f : 1f, 150);
                bay.interiorLamp.ScaleTo(new Vector2(1, lit ? 1.6f : 1f), 150, Easing.OutQuint);
                bay.legendTitle.FadeColour(lit ? RetroPalette.Text : RetroPalette.TextDim.Opacity(0.75f), 150, Easing.OutQuint);
            }

            protected override bool OnHover(osu.Framework.Input.Events.HoverEvent e)
            {
                if (active)
                    SetLit(true);

                return base.OnHover(e);
            }

            protected override void OnHoverLost(osu.Framework.Input.Events.HoverLostEvent e)
            {
                SetLit(false);
                base.OnHoverLost(e);
            }

            protected override bool OnClick(osu.Framework.Input.Events.ClickEvent e)
            {
                if (!active)
                    return false;

                bay.BrowseRequested?.Invoke();
                return true;
            }
        }

        private static Drawable screw(Anchor corner) => new Circle
        {
            Anchor = corner,
            Origin = corner,
            Size = new Vector2(7),
            Margin = new MarginPadding(9),
            Colour = RetroPalette.ChromeDark.Lighten(0.15f).Opacity(0.8f),
        };
    }
}
