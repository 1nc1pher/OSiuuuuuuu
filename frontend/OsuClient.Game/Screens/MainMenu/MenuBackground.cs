using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using OsuClient.Game.Graphics;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.MainMenu
{
    /// <summary>
    /// What sits behind the main menu: a wallpaper under a colour wash and
    /// CRT scanlines (MENU_REDESIGN_PLAN.md step 2).
    ///
    /// The wallpaper is shown twice — a grey copy, and a colour copy inside a
    /// circular mask centred where the logo is. At rest only the grey copy
    /// shows; clicking the logo grows the mask out past the screen corners,
    /// so colour appears to spread from the circle into the picture. Two
    /// decoded copies and a mask, rather than a desaturation shader, keeps
    /// this to stock drawables.
    ///
    /// A sibling of <see cref="Screens.SongSelect.CarouselBackground"/>, and
    /// deliberately not the same class. Song select's version blurs hard and
    /// darkens its left third, because its job is to be a backdrop for three
    /// panels of text; here the artwork is the thing being looked at, so it
    /// stays sharp and evenly lit.
    ///
    /// The picture drifts slowly rather than sitting still — a static
    /// photograph behind a pulsing logo reads as a frozen frame.
    /// </summary>
    public partial class MenuBackground : CompositeDrawable
    {
        /// <summary>
        /// How far the art zooms over one drift cycle. Small on purpose: this
        /// should be noticed only if you look for it.
        /// </summary>
        private const float drift_scale = 1.07f;

        private const double drift_duration = 26000;

        /// <summary>
        /// Art is drawn past the screen edge so the drift never exposes a
        /// border, and so <see cref="FillMode.Fill"/> has something to crop.
        /// </summary>
        private const float overscan = 1.04f;

        /// <summary>
        /// How strongly the colour wash sits over the artwork. Enough to
        /// carry the hue across the whole screen, not so much that the
        /// wallpaper stops being a picture.
        /// </summary>
        private const float tint_strength = 0.44f;

        /// <summary>How far the art is knocked back before anything is laid over it.</summary>
        private const float knock_back = 0.22f;

        /// <summary>Rings sent out from the circle when the colour spreads.</summary>
        private const int ripple_count = 3;

        private const double ripple_stagger = 150;

        private readonly Container greyDrift;
        private readonly Container colourDrift;
        private readonly Container revealContent;
        private readonly CircularContainer revealMask;
        private readonly Container ripples;
        private readonly Box tint;

        private string? currentPath;

        /// <summary>
        /// The newest requested layers, which may still be loading and may
        /// never be shown at all if another request supersedes them.
        /// </summary>
        private Drawable? pendingGrey;
        private Drawable? pendingColour;

        /// <summary>
        /// The layers actually on screen. Kept separate from the pending ones
        /// because only a drawable that made it into the tree can safely be
        /// animated — see the note in <see cref="SetBackground"/>.
        /// </summary>
        private Drawable? displayedGrey;
        private Drawable? displayedColour;

        private Color4 rippleColour = Color4.White;

        public MenuBackground()
        {
            RelativeSizeAxes = Axes.Both;
            Masking = true;

            InternalChildren = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = RetroPalette.Void,
                },
                greyDrift = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                },
                // Knocks the grey art back so the logo's white outline and
                // the credit have somewhere dark to land. The colour copy
                // carries its own identical layer inside the mask.
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(0f, 0f, 0f, knock_back),
                },
                revealMask = new CircularContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Masking = true,
                    Size = Vector2.Zero,
                    // The content inside is sized to the window and stays put
                    // while the mask grows around it. That is the whole
                    // trick: sizing it to the mask instead would make the
                    // picture appear to zoom out of the circle.
                    Child = revealContent = new Container
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Children = new Drawable[]
                        {
                            colourDrift = new Container
                            {
                                RelativeSizeAxes = Axes.Both,
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                            },
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = new Color4(0f, 0f, 0f, knock_back),
                            },
                            // The hue wash belongs to the colour half alone:
                            // tinting the grey copy would defeat the point of
                            // its being grey.
                            tint = new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Alpha = tint_strength,
                                Colour = ColourInfo.GradientVertical(
                                    RetroPalette.GradeTop,
                                    RetroPalette.GradeBottom),
                            },
                        },
                    },
                },
                ripples = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                },
                // A vignette in two halves: the corners fall away and the
                // centre stays open, which is where the logo sits.
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = ColourInfo.GradientHorizontal(
                        new Color4(0f, 0f, 0f, 0.38f),
                        new Color4(0f, 0f, 0f, 0f)),
                },
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = ColourInfo.GradientHorizontal(
                        new Color4(0f, 0f, 0f, 0f),
                        new Color4(0f, 0f, 0f, 0.38f)),
                },
                new ScanlineOverlay(alpha: 0.13f),
            };
        }

        /// <summary>
        /// Recolours the wash and the ripples. The menu feeds this the logo's
        /// current gradient, darkened — the background has to read as the
        /// same colour as the circle without competing with it, and the
        /// circle is the brightest thing on screen by design.
        /// </summary>
        public void SetTint(Color4 top, Color4 bottom)
        {
            tint.Colour = ColourInfo.GradientVertical(top, bottom);
            rippleColour = bottom;
        }

        /// <summary>
        /// Spreads colour out from the centre, or drains it back to grey.
        /// </summary>
        /// <param name="coloured">Where to end up.</param>
        /// <param name="fromRadius">
        /// Where the spread starts — the logo's own edge, so the colour looks
        /// like it comes out of the circle rather than out of a point behind
        /// it.
        /// </param>
        /// <param name="duration">How long the spread takes.</param>
        public void SetColourSpread(bool coloured, float fromRadius, double duration)
        {
            revealMask.FinishTransforms();

            float target = coloured ? coverRadius() : fromRadius;

            // OutQuint rushes out and settles, which is as close to liquid as
            // a plain size transform gets; a linear spread reads as a wipe.
            revealMask.ResizeTo(new Vector2(target * 2), duration, Easing.OutQuint);

            if (coloured)
                sendRipples(fromRadius, duration);
        }

        /// <summary>
        /// Rings chasing the colour outwards, staggered so they read as one
        /// disturbance spreading rather than three separate circles.
        /// </summary>
        private void sendRipples(float fromRadius, double duration)
        {
            for (int i = 0; i < ripple_count; i++)
            {
                var ring = new CircularContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(fromRadius * 2),
                    Masking = true,
                    BorderThickness = 3,
                    BorderColour = rippleColour,
                    Alpha = 0,
                    Child = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Alpha = 0,
                        AlwaysPresent = true,
                    },
                };

                ripples.Add(ring);

                double delay = i * ripple_stagger;

                ring.Delay(delay).FadeTo(0.5f - i * 0.12f, 120, Easing.OutQuint);
                ring.Delay(delay)
                    .ResizeTo(new Vector2(coverRadius() * 2.1f), duration * 1.3, Easing.OutQuint)
                    .FadeOut(duration * 1.3, Easing.OutQuint)
                    .Expire();
            }
        }

        /// <summary>Distance from the centre to a corner — where a reveal has covered everything.</summary>
        private float coverRadius() => new Vector2(DrawWidth, DrawHeight).Length / 2;

        protected override void Update()
        {
            base.Update();

            // Pinned to the window's size rather than the mask's, so the mask
            // can grow around a picture that doesn't move.
            revealContent.Size = DrawSize;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // Both copies drift identically, started in the same frame. Any
            // difference between them would show as the colour copy sliding
            // against the grey one along the reveal's edge.
            drift(greyDrift);
            drift(colourDrift);
        }

        private static void drift(Drawable target)
        {
            target.ScaleTo(1f)
                  .ScaleTo(drift_scale, drift_duration, Easing.InOutSine)
                  .Then()
                  .ScaleTo(1f, drift_duration, Easing.InOutSine)
                  .Loop();
        }

        /// <summary>
        /// Shows a new background, crossfading from whatever is showing.
        /// A null or missing path fades to the bare gradient.
        /// </summary>
        public void SetBackground(string? path)
        {
            if (path == currentPath)
                return;

            currentPath = path;

            var grey = createLayer(path, greyscale: true);
            var colour = createLayer(path, greyscale: false);

            pendingGrey = grey;
            pendingColour = colour;

            LoadComponentAsync(grey, loaded =>
            {
                if (pendingGrey != loaded)
                {
                    loaded.Dispose();
                    return;
                }

                greyDrift.Add(loaded);
                loaded.FadeIn(480, Easing.OutQuint);

                // Only a layer that actually reached the screen may be faded
                // out: a drawable still loading asynchronously can't have its
                // transforms mutated from the update thread. Song select hit
                // this as a crash; the same rule applies here.
                displayedGrey?.FadeOut(480, Easing.OutQuint).Expire();

                displayedGrey = loaded;
                pendingGrey = null;
            });

            LoadComponentAsync(colour, loaded =>
            {
                if (pendingColour != loaded)
                {
                    loaded.Dispose();
                    return;
                }

                colourDrift.Add(loaded);
                loaded.FadeIn(480, Easing.OutQuint);
                displayedColour?.FadeOut(480, Easing.OutQuint).Expire();

                displayedColour = loaded;
                pendingColour = null;
            });
        }

        private static BeatmapBackground createLayer(string? path, bool greyscale) =>
            new BeatmapBackground(path, greyscale)
            {
                RelativeSizeAxes = Axes.Both,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Scale = new Vector2(overscan),
                FillMode = FillMode.Fill,
                Alpha = 0,
            };
    }
}
