using System;
using System.Linq;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using OsuClient.Game.Beatmaps;
using OsuClient.Game.Graphics;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.SongSelect
{
    /// <summary>
    /// One difficulty, shaped like a cassette tape
    /// (CAROUSEL_REDESIGN_PLAN.md step 5): coloured shell, two reels, a paper
    /// label plate across the middle, screws in the corners.
    ///
    /// Exposes <see cref="Beatmap"/>, <see cref="Selected"/>, and
    /// <c>Action</c> from <see cref="ClickableContainer"/>.
    ///
    /// Every part is drawn from primitives rather than a sprite, matching how
    /// the rest of this game's retro hardware (the spinner, health bar, glow
    /// bars) is built.
    /// </summary>
    public partial class CassetteButton : ClickableContainer
    {
        private const float shell_height = 62;
        private const float reel_size = 30;

        /// <summary>One reel revolution while the tape is "playing".</summary>
        private const double reel_period = 2600;

        public Beatmap Beatmap { get; }

        /// <summary>
        /// This difficulty's colour. The wheel tints the set's wedge with the
        /// same ramp so the two panels agree (plan step 5).
        /// </summary>
        public Color4 Accent { get; }

        private readonly Color4 accent;

        private readonly Container shell;
        private readonly Box shellFill;
        private readonly Container leftReel;
        private readonly Container rightReel;
        private readonly Box labelPlate;
        private readonly RetroText nameText;

        private bool selected;

        /// <summary>Whether this is the highlighted difficulty.</summary>
        public bool Selected
        {
            get => selected;
            set
            {
                if (selected == value)
                    return;

                selected = value;
                updateVisualState();
            }
        }

        /// <summary>
        /// <paramref name="rank"/> and <paramref name="rankCount"/> place this
        /// difficulty in its set, easiest first — that's what picks the shell
        /// colour, so a set's tiers always come out distinguishable.
        /// </summary>
        public CassetteButton(Beatmap beatmap, int rank, int rankCount)
        {
            Beatmap = beatmap ?? throw new ArgumentNullException(nameof(beatmap));

            accent = RetroPalette.ForDifficultyRank(rank, rankCount);
            Accent = accent;

            RelativeSizeAxes = Axes.X;
            Height = shell_height;

            Child = shell = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 7,
                BorderThickness = 1.5f,
                BorderColour = accent.Opacity(0.85f),
                Children = new Drawable[]
                {
                    shellFill = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        // Top-lit: the shell reads as moulded plastic rather
                        // than a flat rectangle.
                        Colour = ColourInfo.GradientVertical(
                            accent.Darken(0.55f),
                            accent.Darken(0.85f)),
                    },
                    leftReel = reel(Anchor.CentreLeft, 34),
                    rightReel = reel(Anchor.CentreRight, -34),
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Padding = new MarginPadding { Horizontal = 58, Vertical = 11 },
                        Child = new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Masking = true,
                            CornerRadius = 3,
                            Children = new Drawable[]
                            {
                                labelPlate = new Box
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Colour = new Color4(0.93f, 0.90f, 0.82f, 0.90f),
                                },
                                new FillFlowContainer
                                {
                                    AutoSizeAxes = Axes.Both,
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.Centre,
                                    Direction = FillDirection.Vertical,
                                    Spacing = new Vector2(0, 3),
                                    Children = new Drawable[]
                                    {
                                        nameText = new RetroText
                                        {
                                            Anchor = Anchor.TopCentre,
                                            Origin = Anchor.TopCentre,
                                            Font = RetroFontFamily.Body,
                                            TextSize = 15,
                                            Text = versionOf(beatmap),
                                            Colour = new Color4(0.12f, 0.09f, 0.15f, 1f),
                                        },
                                        new RetroText
                                        {
                                            Anchor = Anchor.TopCentre,
                                            Origin = Anchor.TopCentre,
                                            Font = RetroFontFamily.Display,
                                            TextSize = 7,
                                            Text = $"{beatmap.HitObjects.Count} OBJECTS",
                                            Colour = new Color4(0.35f, 0.30f, 0.38f, 1f),
                                        },
                                    },
                                },
                            },
                        },
                    },
                    screw(Anchor.TopLeft, new Vector2(7, 7)),
                    screw(Anchor.TopRight, new Vector2(-7, 7)),
                    screw(Anchor.BottomLeft, new Vector2(7, -7)),
                    screw(Anchor.BottomRight, new Vector2(-7, -7)),
                },
            };
        }

        private static string versionOf(Beatmap beatmap) =>
            string.IsNullOrWhiteSpace(beatmap.Metadata.Version) ? "(unnamed)" : beatmap.Metadata.Version;

        /// <summary>
        /// A tape reel: a dark well, a chrome hub, and three spokes. The
        /// spokes live in their own container because that's what spins —
        /// rotating the whole reel would turn the well too, which reads as
        /// the hole moving rather than the tape running.
        /// </summary>
        private static Container reel(Anchor anchor, float x)
        {
            return new Container
            {
                Anchor = anchor,
                Origin = Anchor.Centre,
                X = x,
                Size = new Vector2(reel_size),
                Children = new Drawable[]
                {
                    new Circle
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = new Color4(0.09f, 0.07f, 0.12f, 1f),
                    },
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Children = new Drawable[]
                        {
                            spoke(0),
                            spoke(60),
                            spoke(120),
                        },
                    },
                    new Circle
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Size = new Vector2(reel_size * 0.34f),
                        Colour = RetroPalette.Chrome,
                    },
                },
            };
        }

        private static Drawable spoke(float rotation) => new Box
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Size = new Vector2(reel_size * 0.78f, 2.5f),
            Rotation = rotation,
            Colour = RetroPalette.Chrome.Opacity(0.55f),
        };

        private static Drawable screw(Anchor anchor, Vector2 position) => new Circle
        {
            Anchor = anchor,
            Origin = Anchor.Centre,
            Position = position,
            Size = new Vector2(4),
            Colour = new Color4(0f, 0f, 0f, 0.45f),
        };

        protected override void LoadComplete()
        {
            base.LoadComplete();

            updateVisualState();
        }

        protected override bool OnHover(osu.Framework.Input.Events.HoverEvent e)
        {
            updateVisualState();
            return base.OnHover(e);
        }

        protected override void OnHoverLost(osu.Framework.Input.Events.HoverLostEvent e)
        {
            updateVisualState();
            base.OnHoverLost(e);
        }

        private void updateVisualState()
        {
            if (!IsLoaded)
                return;

            if (selected)
            {
                shell.ScaleTo(1.04f, 240, Easing.OutBack);
                shell.TransformTo(nameof(shell.BorderColour), (ColourInfo)accent, 160, Easing.OutQuint);
                shellFill.FadeColour(ColourInfo.GradientVertical(accent.Darken(0.2f), accent.Darken(0.6f)),
                    180, Easing.OutQuint);
                labelPlate.FadeColour(new Color4(0.98f, 0.96f, 0.90f, 1f), 180, Easing.OutQuint);
                nameText.FadeColour(new Color4(0.08f, 0.06f, 0.10f, 1f), 180, Easing.OutQuint);

                shell.EdgeEffect = new EdgeEffectParameters
                {
                    Type = EdgeEffectType.Glow,
                    Colour = accent.Opacity(0.55f),
                    Radius = 22,
                };

                // The tape only runs on the selected difficulty — a column of
                // permanently spinning reels is noise, one is a focal point.
                setReelSpeed(1);
            }
            else
            {
                shell.ScaleTo(IsHovered ? 1.015f : 1f, 200, Easing.OutQuint);
                shell.TransformTo(nameof(shell.BorderColour),
                    (ColourInfo)accent.Opacity(IsHovered ? 0.85f : 0.5f), 160, Easing.OutQuint);
                shellFill.FadeColour(ColourInfo.GradientVertical(
                        accent.Darken(IsHovered ? 0.45f : 0.62f),
                        accent.Darken(IsHovered ? 0.78f : 0.88f)),
                    180, Easing.OutQuint);
                labelPlate.FadeColour(new Color4(0.88f, 0.85f, 0.78f, 0.82f), 180, Easing.OutQuint);
                nameText.FadeColour(new Color4(0.20f, 0.17f, 0.24f, 1f), 180, Easing.OutQuint);

                shell.EdgeEffect = new EdgeEffectParameters
                {
                    Type = EdgeEffectType.Shadow,
                    Colour = new Color4(0f, 0f, 0f, 0.4f),
                    Radius = 10,
                    Offset = new Vector2(0, 2),
                };

                setReelSpeed(IsHovered ? 1 : 0);
            }
        }

        /// <summary>
        /// Starts or stops the reels. "Stopped" is the absence of the looping
        /// transform rather than a rate of zero — a loop can't be re-rated
        /// once it's running, and leaving it spinning invisibly would keep
        /// every unselected button doing per-frame work.
        /// </summary>
        private void setReelSpeed(double rate)
        {
            foreach (var reel in new[] { leftReel, rightReel })
            {
                var spokes = reel.Children[1];

                if (rate > 0)
                {
                    if (!spokes.Transforms.Any())
                    {
                        spokes.RotateTo(spokes.Rotation)
                              .RotateTo(spokes.Rotation + 360, reel_period, Easing.None)
                              .Loop();
                    }
                }
                else
                {
                    spokes.ClearTransforms();
                }
            }
        }

        public string DifficultyName => versionOf(Beatmap);
    }
}
