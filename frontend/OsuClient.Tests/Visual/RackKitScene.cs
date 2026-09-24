using System;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using OsuClient.Game.Graphics;
using OsuClient.Game.Graphics.Rack;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Tests.Visual
{
    /// <summary>
    /// The <c>rack-kit</c> screenshot scene: every part of the studio-rack
    /// kit, in every state, on one frame.
    ///
    /// Four screens are built out of these, so five ad-hoc chrome gradients
    /// would not match each other — and a kit is only a kit if the parts can
    /// be compared side by side. Every lamp state, both switch positions and
    /// the knob at three points in its travel are on screen at once for
    /// exactly that reason.
    /// </summary>
    public partial class RackKitScene : CompositeDrawable
    {
        public RackKitScene()
        {
            RelativeSizeAxes = Axes.Both;

            InternalChildren = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = RetroPalette.Void },
                new FillFlowContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Width = 0.88f,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 16),
                    Children = new Drawable[]
                    {
                        lampsPanel(),
                        switchesPanel(),
                        knobsAndReadouts(),
                    },
                },
            };
        }

        private static Drawable lampsPanel() => new RackPanel
        {
            RelativeSizeAxes = Axes.X,
            Height = 92,
            Legend = "STAGE LAMPS — OFF · LIT · WORKING · FAULT",
            Child = new FillFlowContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(34, 0),
                Children = new Drawable[]
                {
                    lamp("LOAD", LampState.Lit, RetroPalette.Mint),
                    lamp("DETECT", LampState.Lit, RetroPalette.Mint),
                    lamp("MAP", LampState.Working, RetroPalette.Amber),
                    lamp("WRITE", LampState.Off, RetroPalette.Mint),
                    lamp("ANALYSE", LampState.Off, RetroPalette.Mint),
                    lamp("FAULT", LampState.Fault, RetroPalette.Mint),
                },
            },
        };

        private static Drawable lamp(string label, LampState state, Color4 colour) => new FillFlowContainer
        {
            AutoSizeAxes = Axes.Both,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, 6),
            Children = new Drawable[]
            {
                new IndicatorLamp
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    State = state,
                    LampColour = colour,
                },
                new RetroText
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Font = RetroFontFamily.Body,
                    TextSize = 9,
                    Colour = state == LampState.Off ? RetroPalette.TextDim.Opacity(0.5f) : RetroPalette.Text,
                    Text = label,
                },
            },
        };

        private static Drawable switchesPanel()
        {
            var flow = new FillFlowContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(26, 0),
            };

            (string Label, bool On, Color4 Accent)[] rows =
            {
                ("FLUX", true, RetroPalette.Cyan),
                ("THRESHOLD", true, RetroPalette.Amber),
                ("BANDS", false, RetroPalette.Mint),
                ("ENVELOPE", false, RetroPalette.Violet),
                ("RMS", true, RetroPalette.Magenta),
            };

            foreach (var (label, on, accent) in rows)
            {
                var toggle = new ToggleSwitch(label)
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Accent = accent,
                };

                toggle.Active.Value = on;
                flow.Add(toggle);
            }

            return new RackPanel
            {
                RelativeSizeAxes = Axes.X,
                Height = 92,
                Legend = "TRACE SWITCHES — thrown up is on",
                Child = flow,
            };
        }

        private static Drawable knobsAndReadouts() => new RackPanel
        {
            RelativeSizeAxes = Axes.X,
            Height = 168,
            Legend = "DETECTOR CONTROLS AND READOUTS",
            Child = new FillFlowContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(44, 0),
                Children = new Drawable[]
                {
                    knob("MARGIN", 1.0f, 3.0f, 1.1f),
                    knob("MARGIN", 1.0f, 3.0f, 1.5f),
                    knob("MARGIN", 1.0f, 3.0f, 2.9f),
                    knob("DELTA", 0f, 0.15f, 0.05f, value => value.ToString("0.000")),
                    readout("ONSETS", "1122", 4, RetroPalette.Amber, true),
                    readout("BPM", "137.85", 6, RetroPalette.Cyan, false),
                    readout("TIER", "3/5", 3, RetroPalette.Mint, false),
                },
            },
        };

        private static Drawable knob(string label, float min, float max, float initial,
                                     Func<float, string>? format = null)
        {
            var control = new ControlKnob(label, min, max, initial)
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
            };

            if (format != null)
                control.Format = format;

            return control;
        }

        private static Drawable readout(string label, string text, int digits, Color4 colour, bool glowing)
        {
            var display = new SegmentReadout(16)
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                Digits = digits,
                DisplayColour = colour,
                Text = text,
                Glowing = glowing,
            };

            return new FillFlowContainer
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 7),
                Children = new Drawable[]
                {
                    display,
                    new RetroText
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
        }
    }
}
