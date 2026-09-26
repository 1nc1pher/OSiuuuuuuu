using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using OsuClient.Game.Beatmaps;
using OsuClient.Game.Graphics;
using OsuClient.Game.Graphics.Rack;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.Gameplay.HUD
{
    /// <summary>
    /// A 3-2-1 count on the song's own beat, leading into the first note: a
    /// seven-segment digit and a lamp that flashes in time, in a small cue
    /// panel at the top of the screen, clear of both the HUD and the
    /// approach circles the first notes arrive through.
    ///
    /// The count runs over the three beats before the first hit object, not
    /// the start of the audio: a song with a long intro still gets its count
    /// just before there is anything to hit, which is when knowing the tempo
    /// helps.
    ///
    /// Driven purely by <see cref="SetTime"/>, the way the beat effects are:
    /// what shows is computed from gameplay time every frame, so pausing
    /// freezes it and a skip lands it in the right state. It never schedules
    /// anything or hides itself.
    /// </summary>
    public partial class CountIn : CompositeDrawable
    {
        private const int counts = 3;

        /// <summary>How long the panel takes to appear before the first count, and to go after the note.</summary>
        private const double fade = 250;

        /// <summary>Fraction of each beat the lamp stays lit for.</summary>
        private const double lamp_on = 0.4;

        /// <summary>A beat crossed further back than this (a skip jumping over it) doesn't tick.</summary>
        private const double tick_tolerance = 120;

        private readonly double firstObject;
        private readonly double beatLength;

        private readonly Container panel;
        private readonly SegmentReadout digit;
        private readonly IndicatorLamp lamp;

        private int lastCount = int.MaxValue;
        private double lastTime = double.NegativeInfinity;

        /// <summary>Fires as each count begins, with the number shown (3, 2, 1).</summary>
        public event Action<int>? Counted;

        public CountIn(Beatmap beatmap)
        {
            firstObject = beatmap.FirstHitObjectTime;

            double beat = beatmap.BeatLengthAt(firstObject);
            beatLength = double.IsFinite(beat) && beat > 0 ? beat : 500;

            AutoSizeAxes = Axes.Both;

            InternalChild = panel = new Container
            {
                AutoSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 3,
                BorderThickness = 1.5f,
                BorderColour = RetroPalette.ChromeDark,
                Alpha = 0,
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = RetroPalette.PanelInset,
                    },
                    new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(9, 0),
                        Padding = new MarginPadding { Horizontal = 9, Vertical = 6 },
                        Children = new Drawable[]
                        {
                            new RetroText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Font = RetroFontFamily.Display,
                                TextSize = 8,
                                Colour = RetroPalette.TextDim,
                                Text = "CUE",
                            },
                            digit = new SegmentReadout(22)
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Digits = 1,
                                DisplayColour = RetroPalette.Amber,
                                Glowing = true,
                                Text = counts.ToString(),
                            },
                            lamp = new IndicatorLamp
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                LampColour = RetroPalette.Amber,
                            },
                        },
                    },
                },
            };
        }

        private double countStart => firstObject - counts * beatLength;

        /// <summary>The count showing at <paramref name="time"/>: 3, 2 or 1, or 0 outside the count. Exposed for tests.</summary>
        public int CountAt(double time)
        {
            if (time < countStart || time >= firstObject)
                return 0;

            return counts - (int)Math.Floor((time - countStart) / beatLength);
        }

        /// <summary>How visible the panel is at <paramref name="time"/>, 0 to 1. Exposed for tests.</summary>
        public float VisibilityAt(double time)
        {
            double fadeIn = (time - (countStart - fade)) / fade;
            double fadeOut = (firstObject + fade - time) / fade;

            return (float)Math.Clamp(Math.Min(fadeIn, fadeOut), 0, 1);
        }

        public void SetTime(double time)
        {
            panel.Alpha = VisibilityAt(time);

            int count = CountAt(time);

            if (count > 0)
            {
                digit.Text = count.ToString();

                double intoBeat = ((time - countStart) % beatLength) / beatLength;
                lamp.State = intoBeat < lamp_on ? LampState.Lit : LampState.Off;

                // A new count, reached by playing through rather than by a
                // skip jumping over it.
                if (count < lastCount && time - lastTime < tick_tolerance)
                    Counted?.Invoke(count);
            }
            else
            {
                lamp.State = LampState.Off;
            }

            lastCount = count > 0 ? count : int.MaxValue;
            lastTime = time;
        }
    }
}
