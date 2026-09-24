using System;
using System.Collections.Generic;
using System.Globalization;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using OsuClient.Game.Graphics;
using OsuClient.Game.Graphics.Rack;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.Generation
{
    /// <summary>
    /// What the screen shows while the backend runs: a reel-to-reel transport
    /// with a row of stage lamps, a tier counter, an elapsed clock, and the
    /// process's own output on a printout strip.
    ///
    /// <para>
    /// <b>Nothing here invents a number.</b> The lamps come from
    /// <see cref="PipelineProgress"/> parsing real stdout; the tier counter is
    /// the real <c>[i/n]</c> out of the slow stage; the clock is the clock.
    /// There is no percentage bar because there is no percentage — the reels
    /// turn, which is a liveness indicator and claims only that, and there is
    /// no VU meter because during a run there is no signal to meter.
    /// </para>
    ///
    /// <para>
    /// <b>The printout strip is not decoration.</b> It is the only surface a
    /// failed run has, so on failure it takes the whole panel rather than
    /// shrinking: the moment a user actually needs the raw text is the moment
    /// something went wrong.
    /// </para>
    /// </summary>
    public partial class GenerationProgressPanel : CompositeDrawable
    {
        /// <summary>How many of the backend's most recent lines stay on the strip.</summary>
        public const int VisibleLogLines = 9;

        private readonly PipelineProgress progress = new PipelineProgress();
        private readonly Queue<string> log = new Queue<string>();
        private readonly Dictionary<PipelineStage, IndicatorLamp> lamps =
            new Dictionary<PipelineStage, IndicatorLamp>();

        private readonly TapeReel supplyReel;
        private readonly TapeReel takeUpReel;
        private readonly SegmentReadout clock;
        private readonly SegmentReadout tierCounter;
        private readonly RetroText tierLabel;
        private readonly RetroText title;
        private readonly RetroText status;
        private readonly IndicatorLamp faultLamp;
        private readonly RetroText faultLabel;
        private readonly FillFlowContainer logFlow;
        private readonly RackPanel logPanel;
        private readonly RackPanel transportPanel;
        private readonly GridContainer layout;

        private double startTime;
        private bool running = true;
        private bool failed;

        public GenerationProgressPanel(string sourceFileName)
        {
            RelativeSizeAxes = Axes.Both;

            // A grid, not a vertical FillFlowContainer. A flow child with
            // RelativeSizeAxes.Both takes the *whole* parent height rather
            // than what is left over, so the printout panel hung off the
            // bottom of the screen with its contents out of sight. Rows sized
            // absolute/absolute/distributed is how "fill the rest" is
            // expressed here.
            InternalChild = layout = new GridContainer
            {
                RelativeSizeAxes = Axes.Both,
                RowDimensions = runningRows,
                Content = new[]
                {
                    // Open space for the wallpaper, as above the tape deck's
                    // own rack: the machine sits at the bottom of the screen
                    // rather than being the screen.
                    new[] { Empty() },
                    new Drawable[]
                    {
                    transportPanel = new RackPanel
                    {
                        RelativeSizeAxes = Axes.Both,
                        Margin = new MarginPadding { Bottom = 14 },
                        Legend = "TRANSPORT",
                        Children = new Drawable[]
                        {
                            new FillFlowContainer
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                AutoSizeAxes = Axes.Both,
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(12, 0),
                                Margin = new MarginPadding { Left = 16 },
                                Children = new Drawable[]
                                {
                                    supplyReel = new TapeReel
                                    {
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        Running = true,
                                        TapeFill = 0.85f,
                                    },
                                    tapePath(),
                                    takeUpReel = new TapeReel
                                    {
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        Running = true,
                                        Reverse = true,
                                        TapeFill = 0.25f,
                                    },
                                },
                            },
                            new FillFlowContainer
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                AutoSizeAxes = Axes.Both,
                                Direction = FillDirection.Vertical,
                                Spacing = new Vector2(0, 7),
                                Margin = new MarginPadding { Left = 206 },
                                Children = new Drawable[]
                                {
                                    title = new RetroText
                                    {
                                        Font = RetroFontFamily.Body,
                                        TextSize = 15,
                                        Colour = RetroPalette.Text,
                                        Text = sourceFileName,
                                    },
                                    status = new RetroText
                                    {
                                        Font = RetroFontFamily.Body,
                                        TextSize = 11,
                                        Colour = RetroPalette.TextDim,
                                        Text = "starting the generator…",
                                    },
                                },
                            },
                            new FillFlowContainer
                            {
                                Anchor = Anchor.CentreRight,
                                Origin = Anchor.CentreRight,
                                AutoSizeAxes = Axes.Both,
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(16, 0),
                                Margin = new MarginPadding { Right = 16 },
                                Children = new Drawable[]
                                {
                                    readoutBlock("ELAPSED", clock = new SegmentReadout(15)
                                    {
                                        Anchor = Anchor.TopCentre,
                                        Origin = Anchor.TopCentre,
                                        Digits = 5,
                                        DisplayColour = RetroPalette.Amber,
                                        Text = "0:00",
                                    }),
                                    readoutBlock("DIFFICULTY", tierCounter = new SegmentReadout(15)
                                    {
                                        Anchor = Anchor.TopCentre,
                                        Origin = Anchor.TopCentre,
                                        Digits = 3,
                                        DisplayColour = RetroPalette.Cyan,
                                        Text = "–",
                                    }, out tierLabel),
                                },
                            },
                        },
                    },
                    },
                    new Drawable[]
                    {
                    new RackPanel
                    {
                        RelativeSizeAxes = Axes.Both,
                        Margin = new MarginPadding { Bottom = 14 },
                        Legend = "PIPELINE",
                        Children = new Drawable[]
                        {
                            lampRow(),
                            new FillFlowContainer
                            {
                                Anchor = Anchor.CentreRight,
                                Origin = Anchor.CentreRight,
                                AutoSizeAxes = Axes.Both,
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(8, 0),
                                Margin = new MarginPadding { Right = 16 },
                                Children = new Drawable[]
                                {
                                    faultLamp = new IndicatorLamp
                                    {
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        State = LampState.Off,
                                        LampColour = new Color4(1f, 0.28f, 0.30f, 1f),
                                    },
                                    faultLabel = new RetroText
                                    {
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        Font = RetroFontFamily.Body,
                                        TextSize = 10,
                                        Colour = RetroPalette.TextDim.Opacity(0.5f),
                                        Text = "FAULT",
                                    },
                                },
                            },
                        },
                    },
                    },
                    new Drawable[]
                    {
                    logPanel = new RackPanel
                    {
                        RelativeSizeAxes = Axes.Both,
                        Legend = "GENERATOR OUTPUT",
                        // Anchored to the bottom so lines stack upward as
                        // they arrive, the way a printout feeds out of a
                        // machine. Top-anchored, a short run sits marooned at
                        // the top of a tall empty panel.
                        Child = logFlow = new FillFlowContainer
                        {
                            Anchor = Anchor.BottomLeft,
                            Origin = Anchor.BottomLeft,
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Vertical,
                            Spacing = new Vector2(0, 3),
                            Padding = new MarginPadding { Horizontal = 6, Vertical = 4 },
                        },
                    },
                    },
                },
            };
        }

        /// <summary>Height of the printout while running: its visible lines, the legend, and a little air.</summary>
        private const float running_log_height = 200;

        /// <summary>
        /// Transport, pipeline and a printout strip stacked at the bottom, with
        /// the space above left open. A printout that took whatever was left
        /// covered the screen in grey panel, where the tape deck before it
        /// shows its wallpaper.
        /// </summary>
        private static Dimension[] runningRows => new[]
        {
            new Dimension(),
            new Dimension(GridSizeMode.Absolute, 162),
            new Dimension(GridSizeMode.Absolute, 98),
            new Dimension(GridSizeMode.Absolute, running_log_height),
        };

        /// <summary>
        /// A failed run gives the printout the transport's space too: the
        /// output above the error is usually where the cause is, and that is
        /// the one moment the raw text is what the user needs.
        /// </summary>
        private static Dimension[] failedRows => new[]
        {
            new Dimension(GridSizeMode.Absolute, 0),
            new Dimension(GridSizeMode.Absolute, 0),
            new Dimension(GridSizeMode.Absolute, 98),
            new Dimension(),
        };

        /// <summary>Raised when a pipeline lamp moves on to the next stage.</summary>
        public Action? StageAdvanced;

        /// <summary>Raised when the difficulty counter steps on within a stage.</summary>
        public Action? TierAdvanced;

        /// <summary>The parsed run state. Exposed for tests.</summary>
        public PipelineProgress Progress => progress;

        /// <summary>Whether the panel is showing a failed run. Exposed for tests.</summary>
        public bool Failed => failed;

        protected override void LoadComplete()
        {
            base.LoadComplete();

            startTime = Clock.CurrentTime;
            updateLamps();
        }

        /// <summary>
        /// Feeds one line of backend output in. Call from the update thread —
        /// <c>BackendRunner</c> hands lines over on a background one.
        /// </summary>
        public void AppendLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            log.Enqueue(line.TrimEnd());

            while (log.Count > VisibleLogLines && !failed)
                log.Dequeue();

            // A failed run keeps everything: the output above the error is
            // usually where the cause is.
            while (log.Count > 200)
                log.Dequeue();

            rebuildLog();

            var stageBefore = progress.Stage;
            int tierBefore = progress.TierIndex;

            if (progress.Apply(line))
            {
                updateLamps();

                if (progress.Stage != stageBefore)
                    StageAdvanced?.Invoke();
                else if (progress.TierIndex != tierBefore)
                    TierAdvanced?.Invoke();
            }
        }

        /// <summary>Marks the run finished. <paramref name="success"/> false switches the panel to its fault state.</summary>
        public void Finish(bool success, string message)
        {
            running = false;
            failed = !success;

            supplyReel.Running = false;
            takeUpReel.Running = false;

            if (success)
                progress.Finish();

            status.Text = message;
            status.Colour = success ? RetroPalette.Mint : new Color4(1f, 0.45f, 0.45f, 1f);

            faultLamp.State = success ? LampState.Off : LampState.Fault;
            faultLabel.Colour = success ? RetroPalette.TextDim.Opacity(0.5f) : RetroPalette.Text;

            if (!success)
            {
                // The printout is the only place a failure explains itself, so
                // it takes the transport's space rather than staying a strip.
                transportPanel.FadeOut(200, Easing.OutQuint);
                layout.RowDimensions = failedRows;

                // The status line lived on the transport panel, which has just
                // gone: without this the screen shows a traceback and a red
                // lamp but never says the run failed or with what code.
                logPanel.Legend = $"GENERATOR OUTPUT — {message}";
            }

            updateLamps();
            rebuildLog();
        }

        protected override void Update()
        {
            base.Update();

            if (!running)
                return;

            var elapsed = TimeSpan.FromMilliseconds(Clock.CurrentTime - startTime);

            clock.Text = elapsed.TotalHours >= 1
                ? elapsed.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
                : elapsed.ToString(@"m\:ss", CultureInfo.InvariantCulture);
        }

        private void updateLamps()
        {
            foreach (var (stage, lamp) in lamps)
            {
                var reading = progress.ReadingFor(stage);

                // Colour carries the meaning, not only the animation: amber
                // is happening, mint is done. Breathing alone is invisible in
                // a still frame, and a still frame is how this screen gets
                // checked.
                lamp.LampColour = reading == LampReading.Running && running
                    ? RetroPalette.Amber
                    : RetroPalette.Mint;

                lamp.State = reading switch
                {
                    LampReading.Passed => LampState.Lit,
                    LampReading.Running => running ? LampState.Working : LampState.Lit,
                    _ => LampState.Off,
                };

                // A run that stopped mid-stage marks that lamp rather than
                // leaving it breathing — nothing is happening any more.
                if (failed && reading == LampReading.Running)
                    lamp.State = LampState.Fault;
            }

            if (progress.HasTierProgress)
            {
                tierCounter.Text = $"{progress.TierIndex}/{progress.TierCount}";
                tierLabel.Text = progress.TierName.ToUpperInvariant();
            }

            if (running)
            {
                status.Text = progress.Stage switch
                {
                    PipelineStage.Loading => $"decoding {progress.SourceFile}",
                    PipelineStage.Mapping => $"detecting onsets and placing objects — {progress.TierName}",
                    PipelineStage.Writing => "writing the beatmap files",
                    PipelineStage.Analysing => "writing the analysis and DSP trace",
                    _ => "starting the generator…",
                };
            }
        }

        private void rebuildLog()
        {
            logFlow.Clear();

            foreach (string entry in log)
            {
                logFlow.Add(new RetroText
                {
                    Font = RetroFontFamily.Body,
                    TextSize = 10,
                    Colour = RetroPalette.TextDim.Opacity(failed ? 0.95f : 0.75f),
                    Text = entry,
                });
            }
        }

        private Drawable lampRow()
        {
            var flow = new FillFlowContainer
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(30, 0),
                Margin = new MarginPadding { Left = 16 },
            };

            foreach (var stage in PipelineProgress.LampStages)
            {
                var lamp = new IndicatorLamp
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    LampColour = RetroPalette.Mint,
                };

                lamps[stage] = lamp;

                flow.Add(new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 5),
                    Children = new Drawable[]
                    {
                        lamp,
                        new RetroText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Font = RetroFontFamily.Body,
                            TextSize = 9,
                            Colour = RetroPalette.TextDim,
                            Text = PipelineProgress.LegendFor(stage),
                        },
                    },
                });
            }

            return flow;
        }

        private static Drawable readoutBlock(string label, SegmentReadout readout) =>
            readoutBlock(label, readout, out _);

        private static Drawable readoutBlock(string label, SegmentReadout readout, out RetroText caption)
        {
            caption = new RetroText
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                Font = RetroFontFamily.Body,
                TextSize = 9,
                Colour = RetroPalette.TextDim,
                Text = label,
            };

            return new FillFlowContainer
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 6),
                Children = new Drawable[] { readout, caption },
            };
        }

        /// <summary>The span of tape between the two reels, over the heads.</summary>
        private static Drawable tapePath() => new Container
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            Size = new Vector2(46, 58),
            Children = new Drawable[]
            {
                new Box
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.X,
                    Height = 2.5f,
                    Y = -6,
                    Colour = new Color4(0.38f, 0.24f, 0.17f, 1f),
                },
                // The head block the tape runs across.
                new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Y = 6,
                    Size = new Vector2(16, 18),
                    Masking = true,
                    CornerRadius = 2,
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = RetroPalette.ChromeDark.Lighten(0.1f),
                        },
                        new Box
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            RelativeSizeAxes = Axes.X,
                            Height = 3,
                            Colour = RetroPalette.Chrome,
                        },
                    },
                },
            },
        };
    }
}
