using System;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osu.Framework.Utils;
using OsuClient.Game.Graphics;
using OsuClient.Game.Screens.MainMenu;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.SongSelect
{
    /// <summary>
    /// The way into the analyser, and the one thing on song select that says
    /// this project is about signal processing: a deck plate of its own under
    /// the info panel, rather than a small key in the panel's corner.
    ///
    /// <para>
    /// It is built from the screen's own parts. A small record turns at its
    /// left — slowly at rest, spinning up under the cursor. A spectrum meter
    /// at its right runs off the song actually playing, so the button is
    /// visibly listening. A glowing line sweeps across it every few seconds,
    /// a tape head passing over the tape, and constantly while hovered. Its
    /// cyan border breathes. Pressing it pushes the plate in with a flash, and
    /// the screen drops the needle.
    /// </para>
    ///
    /// <para>
    /// For a map with nothing recorded to analyse it stays where it is,
    /// dimmed, the record still and the meter flat, reading NO ANALYSIS
    /// RECORDED — so the column does not jump as the wheel turns between
    /// songs that have one and songs that do not.
    /// </para>
    /// </summary>
    public partial class AnalyseDeckButton : ClickableContainer
    {
        /// <summary>The plate's height on a large window; the screen shrinks it on a short one.</summary>
        public const float PlateHeight = 84;

        /// <summary>The shortest it goes, on a short window, before the cassettes under it run out of room.</summary>
        public const float MinPlateHeight = 60;

        private const int meter_bars = 14;
        private const float disc_size = 60;

        /// <summary>Record speeds, in degrees per second.</summary>
        private const float idle_spin = 90;
        private const float hover_spin = 600;

        private const double idle_scan = 3200;
        private const double hover_scan = 1100;

        [Resolved(CanBeNull = true)]
        private MenuTrack? music { get; set; }

        private readonly Container glow;
        private readonly Container plate;
        private readonly Container disc;
        private readonly Container scanLine;
        private readonly Box flash;
        private readonly RetroText title;
        private readonly RetroText subtitle;
        private readonly FillFlowContainer caption;
        private readonly Box[] bars = new Box[meter_bars];
        private readonly float[] levels = new float[meter_bars];
        private readonly float[] binBuffer = new float[256];

        private Action? openAnalyser;

        private float spin;
        private float spinTarget = idle_spin;

        public AnalyseDeckButton()
        {
            Width = 420;
            Height = PlateHeight;

            // The glow sits behind the plate as its own layer so it can
            // breathe: an edge effect cannot be faded, a drawable can.
            glow = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 8,
                EdgeEffect = new EdgeEffectParameters
                {
                    Type = EdgeEffectType.Glow,
                    Colour = RetroPalette.Cyan.Opacity(0.5f),
                    Radius = 20,
                },
                Child = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0, AlwaysPresent = true },
            };

            plate = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Masking = true,
                CornerRadius = 8,
                BorderThickness = 2,
                BorderColour = RetroPalette.Cyan.Opacity(0.6f),
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = ColourInfo.GradientHorizontal(
                            new Color4(0.06f, 0.09f, 0.14f, 0.95f),
                            new Color4(0.08f, 0.04f, 0.12f, 0.95f)),
                    },
                    // The sweeping head: a bright edge with a fading trail
                    // behind it, so it reads as light moving across the plate
                    // rather than as a line drawn on it.
                    scanLine = new Container
                    {
                        RelativeSizeAxes = Axes.Y,
                        RelativePositionAxes = Axes.X,
                        Width = 80,
                        Origin = Anchor.TopRight,
                        Alpha = 0,
                        Children = new Drawable[]
                        {
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = ColourInfo.GradientHorizontal(RetroPalette.Cyan.Opacity(0f), RetroPalette.Cyan.Opacity(0.22f)),
                            },
                            new Box
                            {
                                Anchor = Anchor.TopRight,
                                Origin = Anchor.TopRight,
                                RelativeSizeAxes = Axes.Y,
                                Width = 2,
                                Colour = RetroPalette.Cyan.Opacity(0.9f),
                            },
                        },
                    },
                    disc = createDisc(),
                    caption = new FillFlowContainer
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        X = disc_size + 34,
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 7),
                        Children = new Drawable[]
                        {
                            title = new RetroText
                            {
                                Font = RetroFontFamily.Display,
                                TextSize = 20,
                                Colour = RetroPalette.Cyan,
                                Text = "ANALYSE",
                            },
                            subtitle = new RetroText
                            {
                                Font = RetroFontFamily.Body,
                                TextSize = 10,
                                Colour = RetroPalette.TextDim,
                                Text = "OPEN THE DSP ANALYSER",
                            },
                        },
                    },
                    createMeter(),
                    flash = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = Color4.White,
                        Alpha = 0,
                    },
                },
            };

            Children = new Drawable[] { glow, plate };

            Action = () =>
            {
                if (openAnalyser == null)
                    return;

                flash.FadeTo(0.35f).FadeOut(400, Easing.OutQuint);
                openAnalyser();
            };
        }

        /// <summary>Whether there is an analysis to open. Exposed for tests.</summary>
        public bool Available => openAnalyser != null;

        /// <summary>
        /// What pressing it does, or null for a map with nothing recorded —
        /// which dims it rather than hiding it.
        /// </summary>
        public void SetAction(Action? action)
        {
            bool was = Available;
            openAnalyser = action;

            if (IsLoaded && was != Available)
                applyAvailability();
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            applyAvailability();
        }

        private void applyAvailability()
        {
            bool on = Available;

            title.FadeColour(on ? RetroPalette.Cyan : RetroPalette.TextDim.Opacity(0.5f), 200);
            subtitle.Text = on ? "OPEN THE DSP ANALYSER" : "NO ANALYSIS RECORDED";
            plate.TransformTo(nameof(BorderColour),
                (ColourInfo)(on ? RetroPalette.Cyan.Opacity(0.6f) : RetroPalette.ChromeDark), 200);
            plate.FadeTo(on ? 1 : 0.6f, 200);

            spinTarget = on ? (IsHovered ? hover_spin : idle_spin) : 0;

            // The glow breathes, slow and steady, so the plate reads as live;
            // with nothing to open it goes out.
            glow.ClearTransforms();

            if (on)
                glow.FadeTo(0.35f, 800, Easing.InOutSine).Then().FadeTo(1f, 1600, Easing.InOutSine).Then().FadeTo(0.35f, 1600, Easing.InOutSine).Loop();
            else
                glow.FadeTo(0, 200);

            restartScan(IsHovered ? hover_scan : idle_scan);
        }

        private void restartScan(double period)
        {
            scanLine.ClearTransforms();

            if (!Available)
            {
                scanLine.FadeOut(150);
                return;
            }

            // Across, then a pause, and again: a head passing over the tape.
            scanLine.MoveToX(0).FadeTo(0.9f)
                    .MoveToX(1, period * 0.55, Easing.InOutSine)
                    .Then().FadeTo(0, 0).Delay(period * 0.45).FadeTo(0.9f, 0)
                    .Loop();
        }

        protected override void Update()
        {
            base.Update();

            // The record's speed eases to its target, so it spins up and down
            // rather than jumping between speeds.
            float dt = (float)Time.Elapsed / 1000f;
            spin += (spinTarget - spin) * Math.Min(1, dt * 4);
            disc.Rotation += spin * dt;

            // The record and the lettering follow the plate's height, which
            // the screen shrinks on a short window.
            float discSize = Math.Max(24, DrawHeight - 24);
            disc.Size = new Vector2(discSize);
            disc.X = 18 + discSize / 2;
            caption.X = 18 + discSize + 16;

            updateMeter();
        }

        private void updateMeter()
        {
            var amplitudes = liveAmplitudes();
            float gain = IsHovered ? 1.4f : 1f;

            for (int i = 0; i < meter_bars; i++)
            {
                float target;

                if (!Available)
                    target = 0.04f;
                else if (amplitudes != null)
                {
                    // Low bins on the left, spread over the useful part of
                    // the spectrum; the highs barely move otherwise.
                    int from = (int)(Math.Pow(i / (float)meter_bars, 1.8) * 160);
                    int to = Math.Max(from + 1, (int)(Math.Pow((i + 1) / (float)meter_bars, 1.8) * 160));

                    float sum = 0;

                    for (int b = from; b < to; b++)
                        sum += amplitudes[b];

                    target = Math.Clamp(sum / (to - from) * (1.5f + i * 0.35f) * gain, 0.05f, 1f);
                }
                else
                {
                    // Nothing playing: a slow idle ripple, so it never looks dead.
                    target = 0.15f + 0.1f * MathF.Sin((float)Time.Current / 400f + i * 0.6f);
                }

                // Quick up, slow down: how a VU needle moves.
                levels[i] += (target - levels[i]) * (target > levels[i] ? 0.5f : 0.12f);
                bars[i].Height = levels[i];
            }
        }

        private float[]? liveAmplitudes()
        {
            if (music?.Track == null || !music.IsPlaying)
                return null;

            var live = music.Track.CurrentAmplitudes.FrequencyAmplitudes;
            live.Span[..Math.Min(binBuffer.Length, live.Length)].CopyTo(binBuffer);

            return binBuffer;
        }

        protected override bool OnHover(HoverEvent e)
        {
            if (Available)
            {
                spinTarget = hover_spin;
                plate.ScaleTo(1.02f, 200, Easing.OutQuint);
                plate.TransformTo(nameof(BorderColour), (ColourInfo)RetroPalette.Cyan, 150);
                restartScan(hover_scan);
            }

            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            if (Available)
            {
                spinTarget = idle_spin;
                plate.ScaleTo(1f, 250, Easing.OutQuint);
                plate.TransformTo(nameof(BorderColour), (ColourInfo)RetroPalette.Cyan.Opacity(0.6f), 250);
                restartScan(idle_scan);
            }

            base.OnHoverLost(e);
        }

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            if (Available)
                plate.ScaleTo(0.97f, 80, Easing.OutQuint);

            return base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseUpEvent e)
        {
            if (Available)
                plate.ScaleTo(IsHovered ? 1.02f : 1f, 250, Easing.OutBack);

            base.OnMouseUp(e);
        }

        /// <summary>
        /// The record: black, with grooves, a cyan label, and one pale streak
        /// across it — a reflection — so a turning record is seen to turn.
        /// </summary>
        private static Container createDisc()
        {
            var record = new Container
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.Centre,
                X = 18 + disc_size / 2,
                Size = new Vector2(disc_size),
                Children = new Drawable[]
                {
                    new Circle
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = RetroPalette.Vinyl,
                    },
                },
            };

            foreach (float groove in new[] { 0.92f, 0.8f, 0.68f, 0.56f })
            {
                record.Add(new CircularContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Size = new Vector2(groove),
                    Masking = true,
                    BorderThickness = 1,
                    BorderColour = Color4.White.Opacity(0.09f),
                    Child = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0, AlwaysPresent = true },
                });
            }

            record.AddRange(new Drawable[]
            {
                // Proportional throughout: the screen shrinks the plate, and
                // the record with it, on a short window.
                new Box
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.CentreLeft,
                    RelativeSizeAxes = Axes.X,
                    RelativePositionAxes = Axes.X,
                    Width = 0.42f,
                    Height = 2,
                    X = 0.08f,
                    Rotation = -35,
                    Colour = Color4.White.Opacity(0.25f),
                },
                new Circle
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Size = new Vector2(0.36f),
                    Colour = RetroPalette.Cyan,
                },
                new Circle
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Size = new Vector2(0.07f),
                    Colour = RetroPalette.Vinyl,
                },
            });

            return record;
        }

        /// <summary>The spectrum meter: bars rising from a shared baseline, cyan on the lows to magenta on the highs.</summary>
        private Drawable createMeter()
        {
            var meter = new FillFlowContainer
            {
                Anchor = Anchor.CentreRight,
                Origin = Anchor.CentreRight,
                Margin = new MarginPadding { Right = 22 },
                AutoSizeAxes = Axes.X,
                Height = PlateHeight * 0.52f,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(4, 0),
            };

            for (int i = 0; i < meter_bars; i++)
            {
                var colour = Interpolation.ValueAt(i, RetroPalette.Cyan, RetroPalette.Magenta, 0, meter_bars - 1);

                meter.Add(new Container
                {
                    RelativeSizeAxes = Axes.Y,
                    Width = 7,
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = Color4.White.Opacity(0.05f),
                        },
                        bars[i] = new Box
                        {
                            Anchor = Anchor.BottomLeft,
                            Origin = Anchor.BottomLeft,
                            RelativeSizeAxes = Axes.Both,
                            Height = 0.05f,
                            Colour = colour,
                        },
                    },
                });
            }

            return meter;
        }
    }
}
