using System;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Graphics
{
    /// <summary>Which edge a <see cref="ScreenRipple"/> starts from.</summary>
    public enum RippleEdge
    {
        Left,
        Right,

        /// <summary>From the middle of the screen, going out both ways at once.</summary>
        Centre,
    }

    /// <summary>The VCR on-screen label a screen change shows in its corner.</summary>
    public enum VcrCue
    {
        None,

        /// <summary>"▶ PLAY" — going to play something.</summary>
        Play,

        /// <summary>"● REC" — going to make something.</summary>
        Record,
    }

    /// <summary>
    /// The edge of a screen change, dressed as a VCR losing tracking: a band
    /// of streaky static with colour fringes and a flickering head-switch
    /// line, riding the edge of the next screen's <see cref="ScreenReveal"/>
    /// as it wipes across the one being left. Faint noise bars roll up
    /// through the old screen as it fades, and the deck's on-screen label
    /// ("▶ PLAY", "● REC") comes up in the corner the new screen arrives from.
    ///
    /// <para>
    /// It lives above the screen stack, in the game itself, rather than in
    /// either screen: it has to outlast the screen that started it and still
    /// be on top when the next one arrives underneath.
    /// </para>
    ///
    /// <para>
    /// From <see cref="Begin"/> until the next screen has settled it also
    /// owns input, so nothing lands on a screen on its way out. The band is
    /// sent by <see cref="Sweep"/>, when the next screen actually starts to
    /// show — not at the press, which would leave it running ahead of a
    /// screen still loading.
    /// </para>
    ///
    /// <para>
    /// Everything here is a handful of quads: the static is pre-drawn
    /// (<see cref="VhsNoise"/>) and the bars are clipped to the part of the
    /// old screen still showing, so the effect costs next to nothing on top
    /// of the two screens it sits over.
    /// </para>
    /// </summary>
    public partial class ScreenRipple : CompositeDrawable
    {
        /// <summary>How long input stays blocked after <see cref="End"/>, while the next screen settles.</summary>
        private const double settle_time = 400;

        /// <summary>
        /// Ends a ripple nobody ended. A screen that never arrives should cost
        /// a few seconds, not a game that ignores every click.
        /// </summary>
        private const double max_duration = 5000;

        /// <summary>Band width as a fraction of the window height.</summary>
        private const float band_fraction = 0.09f;

        /// <summary>How far the colour fringes sit either side of the edge, as a fraction of the band.</summary>
        private const float fringe_offset = 0.07f;

        /// <summary>How long a noise frame stays up — about the rate a VCR's static crawls.</summary>
        private const double noise_frame_duration = 34;

        /// <summary>How long the on-screen label stays up once the sweep starts.</summary>
        private const double cue_hold = 1500;

        private bool running;
        private bool waiting;
        private bool sweeping;

        private double startedAt;
        private double sweepStartedAt;

        private Vector2 origin;

        private readonly Container band;
        private readonly Sprite noise;
        private readonly Box leadingFringe;
        private readonly Box trailingFringe;
        private readonly Box headLine;

        private readonly Container fadingSide;
        private readonly Container fadingSideContent;
        private readonly Sprite[] bars;

        // One of each, built with the ripple, so their text is rasterized at
        // game start rather than in the first frame of a screen change.
        private readonly VcrLabel playLabel;
        private readonly VcrLabel recordLabel;

        private Texture[] noiseFrames = Array.Empty<Texture>();
        private int lastNoiseFrame = -1;
        private readonly Random jitter = new Random(1981);

        [Resolved]
        private IRenderer renderer { get; set; } = null!;

        /// <summary>How far the band has crossed the window, 0 to 1, on the same curve as the reveal. Transformable.</summary>
        public float Progress { get; set; }

        public ScreenRipple()
        {
            RelativeSizeAxes = Axes.Both;

            InternalChildren = new Drawable[]
            {
                fadingSide = new Container
                {
                    Masking = true,
                    Alpha = 0,
                    Child = fadingSideContent = new Container(),
                },
                band = new Container
                {
                    Origin = Anchor.TopCentre,
                    RelativeSizeAxes = Axes.Y,
                    Alpha = 0,
                    Children = new Drawable[]
                    {
                        noise = new Sprite
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            RelativeSizeAxes = Axes.Both,
                            Blending = BlendingParameters.Additive,
                            Alpha = 0.7f,
                        },
                        leadingFringe = fringe(RetroPalette.Cyan),
                        trailingFringe = fringe(RetroPalette.Magenta),
                        headLine = new Box
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            RelativeSizeAxes = Axes.Y,
                            Width = 2,
                            Colour = Color4.White,
                            Blending = BlendingParameters.Additive,
                        },
                    },
                },
                playLabel = new VcrLabel(VcrCue.Play) { Alpha = 0 },
                recordLabel = new VcrLabel(VcrCue.Record) { Alpha = 0 },
            };

            bars = new Sprite[3];

            for (int i = 0; i < bars.Length; i++)
            {
                fadingSideContent.Add(bars[i] = new Sprite
                {
                    RelativeSizeAxes = Axes.X,
                    Blending = BlendingParameters.Additive,
                });
            }
        }

        private static Box fringe(Color4 colour) => new Box
        {
            Anchor = Anchor.TopCentre,
            Origin = Anchor.TopCentre,
            RelativeSizeAxes = Axes.Y,
            Width = 3,
            Colour = colour,
            Alpha = 0.75f,
            Blending = BlendingParameters.Additive,
        };

        [BackgroundDependencyLoader]
        private void load()
        {
            noiseFrames = VhsNoise.Get(renderer);

            noise.Texture = noiseFrames[0];

            foreach (var bar in bars)
                bar.Texture = noiseFrames[0];
        }

        /// <summary>Whether a ripple is under way, input blocked. Exposed for tests.</summary>
        public bool Running => running;

        /// <summary>Where the band will start, in this drawable's space — the window's.</summary>
        public Vector2 RingOrigin => origin;

        /// <summary>
        /// Takes input and sets where the band will start, until
        /// <see cref="End"/>. Ignored while one is already running, so a
        /// double click cannot start two screen changes.
        /// </summary>
        /// <returns>False when a ripple was already running and this one was ignored.</returns>
        public bool Begin(RippleEdge edge) => start(edge switch
        {
            RippleEdge.Right => new Vector2(DrawWidth, DrawHeight / 2),
            RippleEdge.Left => new Vector2(0, DrawHeight / 2),
            _ => DrawSize / 2,
        });

        /// <summary>As <see cref="Begin"/>, from a point on screen — the thing that was pressed.</summary>
        public bool BeginAt(Vector2 screenSpacePoint) => start(ToLocalSpace(screenSpacePoint));

        private bool start(Vector2 from)
        {
            if (running)
                return false;

            running = true;
            waiting = true;
            startedAt = Time.Current;
            origin = from;

            return true;
        }

        /// <summary>
        /// Sends the band across the window from the origin, on the same
        /// curve as the <see cref="ScreenReveal"/> it rides, and puts up the
        /// deck's label for <paramref name="cue"/>.
        /// </summary>
        public void Sweep(double duration, Easing easing, VcrCue cue = VcrCue.None)
        {
            ClearTransforms(false, nameof(Progress));
            Progress = 0;

            sweeping = true;
            sweepStartedAt = Time.Current;

            this.TransformTo(nameof(Progress), 1f, duration, easing)
                .OnComplete(_ =>
                {
                    sweeping = false;
                    band.Alpha = 0;
                    fadingSide.Alpha = 0;
                });

            if (cue != VcrCue.None)
            {
                bool fromRight = origin.X >= DrawWidth / 2;

                var label = cue == VcrCue.Record ? recordLabel : playLabel;
                label.Anchor = label.Origin = fromRight ? Anchor.TopRight : Anchor.TopLeft;

                label.ClearTransforms();
                label.FadeIn()
                     .Delay(cue_hold)
                     .FadeOut(250, Easing.OutQuint);
            }
        }

        /// <summary>
        /// The next screen has arrived: input returns once it has settled.
        /// </summary>
        public void End()
        {
            if (!waiting)
                return;

            waiting = false;

            Scheduler.AddDelayed(() => running = false, settle_time);
        }

        protected override void Update()
        {
            base.Update();

            if (waiting && Time.Current - startedAt > max_duration)
                End();

            // In from the side as far as the screens' own corner text, and
            // below it: both arriving screens keep a small title or hint in
            // their top corners, which the label would otherwise sit on.
            playLabel.Margin = recordLabel.Margin = new MarginPadding
            {
                Horizontal = DrawHeight * 0.045f,
                Top = DrawHeight * 0.095f,
            };

            if (sweeping)
                updateSweep();
        }

        private void updateSweep()
        {
            var (left, right) = ScreenReveal.CoveredSpan(origin, DrawWidth, Progress);
            bool fromRight = origin.X >= DrawWidth / 2;

            float edge = fromRight ? left : right;
            float bandWidth = DrawHeight * band_fraction;

            // In over the first few percent, out as it runs off the far side,
            // where half of it would otherwise sit on the edge of the window.
            float presence = Math.Clamp(Progress / 0.04f, 0, 1) * Math.Clamp((1 - Progress) / 0.12f, 0, 1);

            band.X = edge;
            band.Width = bandWidth;
            band.Alpha = presence;

            // Cyan leads into the old screen, magenta trails over the new —
            // the colour slipping behind the luminance, as it does on tape.
            float fringeDistance = bandWidth * fringe_offset;
            leadingFringe.X = fromRight ? -fringeDistance : fringeDistance;
            trailingFringe.X = -leadingFringe.X;

            // The static crawls at tape speed rather than at the frame rate.
            int frame = (int)((Time.Current - sweepStartedAt) / noise_frame_duration);

            if (frame != lastNoiseFrame)
            {
                lastNoiseFrame = frame;

                noise.Texture = noiseFrames[jitter.Next(noiseFrames.Length)];
                noise.X = (jitter.NextSingle() - 0.5f) * bandWidth * 0.15f;
                // Flipped at random too, so six frames never read as a loop.
                noise.Scale = new Vector2(jitter.Next(2) == 0 ? 1 : -1, 1);
                headLine.Alpha = 0.5f + 0.5f * jitter.NextSingle();

                foreach (var bar in bars)
                    bar.Texture = noiseFrames[jitter.Next(noiseFrames.Length)];
            }

            updateFadingSide(left, right, presence);
        }

        /// <summary>
        /// Noise bars rolling up through the old screen as it fades, clipped
        /// to the part of it still showing.
        /// </summary>
        private void updateFadingSide(float coveredLeft, float coveredRight, float presence)
        {
            float showingLeft = coveredLeft > 0 ? 0 : coveredRight;
            float showingRight = coveredRight < DrawWidth ? DrawWidth : coveredLeft;

            if (showingRight - showingLeft <= 0)
            {
                fadingSide.Alpha = 0;
                return;
            }

            fadingSide.Alpha = presence * Math.Clamp(Progress * 3, 0, 1);
            fadingSide.Position = new Vector2(showingLeft, 0);
            fadingSide.Size = new Vector2(showingRight - showingLeft, DrawHeight);

            fadingSideContent.Size = DrawSize;
            fadingSideContent.Position = -fadingSide.Position;

            double elapsed = Time.Current - sweepStartedAt;

            for (int i = 0; i < bars.Length; i++)
            {
                // Each bar its own height, speed and starting point; rolling
                // upwards and wrapping, like a picture that won't hold.
                float height = DrawHeight * (0.008f + 0.009f * i);
                float speed = DrawHeight * (0.55f + 0.35f * i) / 1000;
                float start = DrawHeight * (0.25f + 0.31f * i);

                float y = (float)((start - elapsed * speed) % DrawHeight);

                if (y < -height)
                    y += DrawHeight + height;

                var bar = bars[i];
                bar.Height = height;
                bar.Y = y;
                bar.Alpha = 0.35f - 0.07f * i;
            }
        }

        // While running, the ripple owns input: a click or key landing on the
        // screen being left would act on a screen that is on its way out.
        public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) => running;

        protected override bool OnMouseDown(MouseDownEvent e) => running;

        protected override bool OnClick(ClickEvent e) => running;

        protected override bool OnScroll(ScrollEvent e) => running;

        protected override bool OnKeyDown(KeyDownEvent e) => running;

        /// <summary>
        /// The deck's on-screen label: a white symbol and word in the pixel
        /// face, with the colour fringing a tape picture puts on hard white.
        /// </summary>
        private partial class VcrLabel : CompositeDrawable
        {
            private const float text_size = 20;

            public VcrLabel(VcrCue cue)
            {
                AutoSizeAxes = Axes.Both;

                string text = cue == VcrCue.Record ? "REC" : "PLAY";

                // Behind: the fringes. In front: the white label itself.
                var words = new[]
                {
                    word(text, RetroPalette.Magenta.Opacity(0.7f), new Vector2(-2, 0)),
                    word(text, RetroPalette.Cyan.Opacity(0.7f), new Vector2(2, 0)),
                    word(text, Color4.White, Vector2.Zero),
                };

                InternalChild = new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(12, 0),
                    Children = new Drawable[]
                    {
                        new Container
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Size = new Vector2(text_size * 0.9f),
                            Child = symbolFor(cue),
                        },
                        new Container
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            AutoSizeAxes = Axes.Both,
                            Children = words,
                        },
                    },
                };
            }

            private static RetroText word(string text, Color4 colour, Vector2 offset) => new RetroText
            {
                Text = text,
                Font = RetroFontFamily.Display,
                TextSize = text_size,
                Colour = colour,
                Position = offset,
                Shared = true,
                Blending = colour == Color4.White ? BlendingParameters.Mixture : BlendingParameters.Additive,
            };

            private static Drawable symbolFor(VcrCue cue) =>
                cue == VcrCue.Record
                    ? new Circle
                    {
                        RelativeSizeAxes = Axes.Both,
                        Size = new Vector2(0.8f),
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Colour = new Color4(1f, 0.2f, 0.25f, 1f),
                    }
                    : new Triangle
                    {
                        RelativeSizeAxes = Axes.Both,
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        // Triangle points up; turned to point the way PLAY
                        // goes.
                        Rotation = 90,
                        Colour = Color4.White,
                    };
        }
    }
}
