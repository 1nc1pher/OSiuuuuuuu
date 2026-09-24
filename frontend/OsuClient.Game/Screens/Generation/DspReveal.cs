using System;
using System.Collections.Generic;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Threading;
using OsuClient.Game.Backend;
using OsuClient.Game.Graphics;
using OsuClient.Game.Graphics.Rack;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.Generation
{
    /// <summary>
    /// The analyser chassis and the whole staged reveal that plays across it
    /// (GENERATION_REDESIGN_PLAN.md part 5).
    ///
    /// <para>
    /// A component rather than part of <see cref="DspVisualizationScreen"/>,
    /// for two reasons. It can be pointed at any beatmap folder, which is what
    /// the analyser entry from song select will need; and it can be rendered
    /// from a screenshot scene without a Python process running, which is the
    /// only way the stages get looked at as they are built.
    /// </para>
    ///
    /// <para>
    /// <b>The shared time axis is the thing that makes it readable.</b> The
    /// spectrogram strip owns it, through
    /// <see cref="SpectrogramReveal.TimeToX"/>, and every trace, spark and
    /// beat line places itself through that — so a flux peak, the onset it
    /// produced and the object it became all sit over the same moment of the
    /// song. No stage may invent its own.
    /// </para>
    ///
    /// <para>
    /// <b>How much it can show depends on what the backend wrote.</b> With
    /// only an <c>analysis.json</c> — every map generated before this feature
    /// existed — it plays the four original stages. With a <c>dsp.json</c>
    /// beside it, the detection, tempo and difficulty stages have data to draw
    /// and are inserted in the right places. Missing data removes a stage; it
    /// never breaks one.
    /// </para>
    /// </summary>
    public partial class DspReveal : CompositeDrawable
    {
        private readonly AnalysisData analysis;
        private readonly DspDetail? detail;
        private readonly string? spectrogramPath;

        private readonly List<Action> stages = new List<Action>();

        private RackPanel tapeStrip = null!;
        private RackPanel traceRack = null!;
        private Container overlayArea = null!;
        private Container actionArea = null!;
        private RetroText stageTitle = null!;
        private RetroText stageDetail = null!;
        private RetroText skipHint = null!;

        private SpectrogramReveal spectrogram = null!;
        private OnsetSparkLayer sparks = null!;
        private BeatGridReveal beatGrid = null!;
        private HitObjectAssemblyPreview? assembly;
        private FluxStage? fluxStage;
        private TempoStage? tempoStage;
        private SnapStage? snapStage;
        private TierFunnelStage? funnelStage;

        private int stageIndex = -1;
        private bool finished;
        private bool atFinale;
        private ScheduledDelegate? pendingAdvance;

        /// <summary>Raised as each stage starts, finale included.</summary>
        public Action? StageStarted;

        /// <summary>Raised as the onset sweep reveals each onset.</summary>
        public Action<AnalysisOnset>? OnsetRevealed;

        /// <summary>Raised as the beat grid draws each line.</summary>
        public Action? BeatRevealed;

        /// <summary>Raised with each object's kind as placement puts it down.</summary>
        public Action<string>? ObjectRevealed;

        /// <summary>Raised when the finale arrives.</summary>
        public Action? FinaleReached;

        /// <summary>Raised when a skip jumps the sequence ahead.</summary>
        public Action? Skipped;

        /// <summary>Raised when either finale button is pressed, before it acts.</summary>
        public Action? ButtonPressed;

        /// <summary>Raised when the finale's PLAY button is pressed.</summary>
        public Action? Completed;

        /// <summary>
        /// Raised when the finale's OPEN ANALYSER button is pressed.
        ///
        /// Null means the host has nowhere to send it, and the button is not
        /// offered at all — better than a button that does nothing.
        /// </summary>
        public Action? AnalyserRequested;

        /// <summary>
        /// Stage to open on, rather than the first.
        ///
        /// For screenshot scenes: a timed sequence is the one thing a single
        /// capture cannot catch, because by the time the harness takes its
        /// frame the stage worth looking at has usually been and gone. Applied
        /// inside <see cref="Play"/> so it cannot race the scheduled start.
        /// </summary>
        public int StartStage { get; init; }

        /// <summary>
        /// Stops the sequence handing on to the next stage, so it holds on
        /// <see cref="StartStage"/> indefinitely.
        ///
        /// For screenshot scenes again: without it a capture has to be timed
        /// to land between one stage's start and the next, which makes the
        /// frame depend on the pacing constants rather than on the stage
        /// being looked at.
        /// </summary>
        public bool HoldAtStartStage { get; init; }

        public DspReveal(AnalysisData analysis, DspDetail? detail, string? spectrogramPath)
        {
            this.analysis = analysis;
            this.detail = detail;
            this.spectrogramPath = spectrogramPath;

            RelativeSizeAxes = Axes.Both;
        }

        /// <summary>Which stage is on screen, counting from zero. Exposed for tests.</summary>
        public int StageIndex => stageIndex;

        /// <summary>How many stages this map's data supports. Exposed for tests.</summary>
        public int StageCount => stages.Count;

        /// <summary>The heading currently shown. Exposed for tests.</summary>
        public string StageTitle => stageTitle.Text;

        /// <summary>Whether the finale is on screen, waiting for a choice. Exposed for tests.</summary>
        public bool AtFinale => atFinale;

        /// <summary>Whether the sequence has ended. Exposed for tests.</summary>
        public bool Finished => finished;

        /// <summary>The spectrogram strip, which owns the shared time axis.</summary>
        public SpectrogramReveal Spectrogram => spectrogram;

        protected override void LoadComplete()
        {
            base.LoadComplete();

            build();
            buildStages();

            // Scheduled, not called: the children were constructed a moment
            // ago and have not finished loading, and the first stage's
            // FadeInFromZero on a drawable that has not entered the tree
            // silently does nothing — the heading simply never appears. Same
            // trap CarouselBackground carries a comment about.
            Schedule(Play);
        }

        private void build()
        {
            InternalChild = new GridContainer
            {
                RelativeSizeAxes = Axes.Both,
                RowDimensions = new[]
                {
                    new Dimension(GridSizeMode.Absolute, 54),
                    new Dimension(),
                    new Dimension(GridSizeMode.Absolute, 26),
                },
                Content = new[]
                {
                    new Drawable[]
                    {
                        new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Children = new Drawable[]
                            {
                                new FillFlowContainer
                                {
                                    RelativeSizeAxes = Axes.X,
                                    AutoSizeAxes = Axes.Y,
                                    Direction = FillDirection.Vertical,
                                    Spacing = new Vector2(0, 7),
                                    Children = new Drawable[]
                                    {
                                        stageTitle = new RetroText
                                        {
                                            Font = RetroFontFamily.Display,
                                            TextSize = 15,
                                            Colour = RetroPalette.Text,
                                        },
                                        stageDetail = new RetroText
                                        {
                                            Font = RetroFontFamily.Body,
                                            TextSize = 12,
                                            Colour = RetroPalette.TextDim,
                                        },
                                    },
                                },
                                // The finale's choices, level with its heading.
                                // Not over the chassis: that is where the
                                // finished map is, and it is what they are
                                // choosing about.
                                actionArea = new Container
                                {
                                    Anchor = Anchor.TopRight,
                                    Origin = Anchor.TopRight,
                                    AutoSizeAxes = Axes.Both,
                                },
                            },
                        },
                    },
                    new Drawable[]
                    {
                        new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Children = new Drawable[]
                            {
                                new GridContainer
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    RowDimensions = new[]
                                    {
                                        new Dimension(GridSizeMode.Relative, 0.52f),
                                        new Dimension(),
                                    },
                                    Content = new[]
                                    {
                                        new Drawable[]
                                        {
                                            tapeStrip = new RackPanel
                                            {
                                                RelativeSizeAxes = Axes.Both,
                                                Margin = new MarginPadding { Bottom = 12 },
                                                Legend = "MEL SPECTROGRAM",
                                            },
                                        },
                                        new Drawable[]
                                        {
                                            traceRack = new RackPanel
                                            {
                                                RelativeSizeAxes = Axes.Both,
                                                Legend = "SIGNALS",
                                            },
                                        },
                                    },
                                },
                                // Over both panels, so a later stage can take
                                // the whole chassis without the strip and the
                                // rack being torn down first.
                                overlayArea = new Container { RelativeSizeAxes = Axes.Both },
                            },
                        },
                    },
                    new Drawable[]
                    {
                        skipHint = new RetroText
                        {
                            Anchor = Anchor.BottomRight,
                            Origin = Anchor.BottomRight,
                            Font = RetroFontFamily.Body,
                            TextSize = 10,
                            Colour = RetroPalette.TextDim.Opacity(0.75f),
                            Text = "ESCAPE OR CLICK TO SKIP",
                        },
                    },
                },
            };

            spectrogram = new SpectrogramReveal(spectrogramPath, analysis.Track.Duration)
            {
                RelativeSizeAxes = Axes.Both,
            };

            sparks = new OnsetSparkLayer(analysis.Onsets, spectrogram);
            beatGrid = new BeatGridReveal(analysis.BeatGrid, spectrogram);

            sparks.SparkPlaced = onset => OnsetRevealed?.Invoke(onset);
            beatGrid.LinePlaced = () => BeatRevealed?.Invoke();

            spectrogram.AddOverlay(sparks);
            spectrogram.AddOverlay(beatGrid);

            tapeStrip.Add(spectrogram);

            // The trace rack shares the strip's width and padding, so a peak
            // here sits under the streak that produced it without either
            // panel knowing about the other.
            if (detail != null && detail.HasContent)
            {
                fluxStage = new FluxStage(detail, analysis.OnsetSource);
                traceRack.Add(fluxStage);
            }
        }

        /// <summary>
        /// Assembles the stage list for the data this map actually has.
        ///
        /// Built as a list rather than a chain of methods handing to each
        /// other, because the list changes with the file: a v1 map has four
        /// stages and one with a trace has nine, and a chain would need a
        /// branch at every link.
        /// </summary>
        private void buildStages()
        {
            stages.Add(stageSpectrogram);

            // Detection splits into method and result when the trace is
            // there: the flux and its threshold first, then the onsets that
            // are its crossings. Without a trace there is only the result to
            // show, which is what every map generated before this had.
            if (fluxStage != null)
            {
                stages.Add(stageFlux);
                stages.Add(stageThreshold);
            }

            stages.Add(stageOnsets);

            // The tempo search goes before the grid it produces, so the grid
            // arrives as the result of something watched rather than out of
            // nowhere.
            if (detail?.Tempo != null)
            {
                stages.Add(stageTempoSearch);
                stages.Add(stagePhaseFit);
            }

            stages.Add(stageBeatGrid);

            // Steps 4 and 5, between the grid they snap to and the objects
            // they produce.
            if (detail?.Tier(analysis.OnsetSource) != null)
                stages.Add(stageSnap);

            if (detail?.Tiers is { Count: > 0 })
                stages.Add(stageFunnel);

            stages.Add(stageHitObjects);
            stages.Add(stageFinale);
        }

        /// <summary>Starts, or restarts, the sequence from its first stage.</summary>
        public void Play()
        {
            stageIndex = -1;
            finished = false;
            atFinale = false;

            for (int i = 0; i <= StartStage && !finished; i++)
                Advance();
        }

        /// <summary>Moves to the next stage. Ends the sequence past the last one.</summary>
        public void Advance()
        {
            if (finished)
                return;

            stageIndex++;

            if (stageIndex >= stages.Count)
            {
                finish();
                return;
            }

            stages[stageIndex]();
        }

        /// <summary>
        /// Jumps every component to its finished state and lands on the
        /// finale, where the player picks what happens next.
        ///
        /// Everything already on screen lands on its final picture rather than
        /// cutting away mid-animation, which is what makes a skip feel like an
        /// end rather than an interruption.
        /// </summary>
        public void SkipToEnd()
        {
            if (finished)
                return;

            if (!atFinale)
                Skipped?.Invoke();

            spectrogram.RevealImmediately();
            fluxStage?.RevealImmediately();
            tempoStage?.RevealImmediately();
            snapStage?.RevealImmediately();
            funnelStage?.RevealImmediately();
            sparks.SweepImmediately();
            beatGrid.SweepImmediately();
            assembly?.AssembleImmediately();

            if (atFinale)
                return;

            // Stops the stage that was queued next from arriving on top of
            // the finale a moment later.
            pendingAdvance?.Cancel();

            // Placement may not have started yet; its objects are the picture
            // the finale sits on, so a skip builds them first.
            if (assembly == null)
            {
                tempoStage?.FadeOut(300, Easing.OutQuint);
                stageHitObjects();
                pendingAdvance?.Cancel();
                assembly?.AssembleImmediately();
            }

            stageIndex = stages.Count - 1;
            stageFinale();
        }

        private void finish()
        {
            if (finished)
                return;

            finished = true;
            Completed?.Invoke();
        }

        private Drawable finaleButton(string label, Color4 accent, Action action)
        {
            var button = new ClickableContainer
            {
                Size = new Vector2(196, 40),
                Masking = true,
                CornerRadius = 5,
                BorderThickness = 1.5f,
                BorderColour = accent.Opacity(0.7f),
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = RetroPalette.PanelInset,
                    },
                    new RetroText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Font = RetroFontFamily.Body,
                        TextSize = 11,
                        Colour = accent,
                        Text = label,
                    },
                },
            };

            button.Action = () =>
            {
                ButtonPressed?.Invoke();
                action();
            };

            return button;
        }

        private void queueNext(double delay)
        {
            if (HoldAtStartStage)
                return;

            pendingAdvance = Scheduler.AddDelayed(Advance, delay);
        }

        private void setStage(string title, string detail)
        {
            StageStarted?.Invoke();

            stageTitle.Text = title;
            stageDetail.Text = detail;

            stageTitle.FadeInFromZero(260, Easing.OutQuint);
            stageDetail.FadeInFromZero(340, Easing.OutQuint);
        }

        // ------------------------------------------------------------------
        // Stages
        // ------------------------------------------------------------------

        private void stageSpectrogram()
        {
            setStage("STEP 1 — SPECTROGRAM",
                $"{analysis.Track.Name} · {analysis.Track.Duration:0}s · 128 mel bands at {analysis.Track.SampleRate} Hz");

            spectrogram.Reveal(RevealPacing.Spectrogram);

            // Deliberately short of the full duration: the next stage starts
            // while the wipe is still finishing, which reads as one continuous
            // pass over the song rather than as separate animations.
            queueNext(RevealPacing.Spectrogram * 0.85);
        }

        private void stageFlux()
        {
            var bands = detail!.Bands;

            setStage("STEP 2A — SPECTRAL FLUX",
                "half-wave rectified frame-to-frame difference, L2 across "
                + $"{detail.Frames.NMels} mel bands · split into "
                + $"{bandLabel(bands, "low")}, {bandLabel(bands, "mid")}, {bandLabel(bands, "high")}");

            fluxStage!.RevealFlux(RevealPacing.Flux);

            queueNext(RevealPacing.Flux);
        }

        private void stageThreshold()
        {
            var sensitivity = fluxStage!.Sensitivity;

            setStage("STEP 2B — ADAPTIVE THRESHOLD",
                $"local median + margin — Bello et al. (2005) · δ {sensitivity.Delta:0.00} · "
                + $"margin {sensitivity.Margin:0.0} · {analysis.OnsetSource} sensitivity");

            fluxStage.RevealThreshold(RevealPacing.Threshold);

            queueNext(RevealPacing.Threshold);
        }

        /// <summary>A band's range as the file states it, in kHz or Hz as reads better.</summary>
        private static string bandLabel(IReadOnlyDictionary<string, IReadOnlyList<double>> bands, string name)
        {
            if (!bands.TryGetValue(name, out var edges) || edges.Count < 2)
                return name;

            string format(double hz) => hz >= 1000 ? $"{hz / 1000:0.#}k" : $"{hz:0}";

            return $"{name} {format(edges[0])}–{format(edges[1])}Hz";
        }

        private void stageOnsets()
        {
            setStage("STEP 2 — ONSET DETECTION",
                $"{analysis.Onsets.Count} peaks where the flux crosses its threshold · colour is the dominant band"
                + (string.IsNullOrEmpty(analysis.OnsetSource)
                    ? string.Empty
                    : $" · {analysis.OnsetSource} sensitivity"));

            sparks.Sweep(RevealPacing.Onsets);

            queueNext(RevealPacing.Onsets);
        }

        private void stageTempoSearch()
        {
            var search = detail!.Tempo!;

            tempoStage = new TempoStage(detail) { Alpha = 0 };
            overlayArea.Add(tempoStage);

            setStage("STEP 3A — TEMPO SEARCH",
                $"autocorrelation weighted by a log-normal prior · searched "
                + $"{search.SearchMinBpm:0}–{search.SearchMaxBpm:0} BPM · "
                + $"raw estimate {search.RawBpm:0.0} BPM");

            // The signals rack steps back while the search is over it, or
            // its flux trace reads through the panel as part of the plot. The
            // spectrogram strip stays: the search is *about* what is up there.
            traceRack.FadeTo(0.12f, 400, Easing.OutQuint);

            tempoStage.FadeIn(400, Easing.OutQuint);
            tempoStage.RevealSearch(RevealPacing.TempoSearch);

            queueNext(RevealPacing.TempoSearch);
        }

        private void stagePhaseFit()
        {
            var search = detail!.Tempo!;

            setStage("STEP 3B — OCTAVE AND PHASE",
                search.OctaveCorrected
                    ? $"octave correction moved {search.RawBpm:0.0} to {search.ChosenBpm:0.0} BPM · "
                      + $"phase strength {search.PhaseStrength:0.00}"
                    : $"{search.ChosenBpm:0.0} BPM held against its octaves · "
                      + $"phase strength {search.PhaseStrength:0.00}");

            tempoStage!.RevealPhase(RevealPacing.PhaseFit);

            queueNext(RevealPacing.PhaseFit);
        }

        private void stageBeatGrid()
        {
            setStage("STEP 3 — BEAT TRACKING",
                $"autocorrelation over the onset envelope · {analysis.BeatGrid.BeatTimes.Count} beats");

            // The search panel steps aside so the grid it produced can land
            // on the spectrogram underneath it, and the signals come back.
            tempoStage?.FadeOut(500, Easing.OutQuint);
            traceRack.FadeTo(1, 500, Easing.OutQuint);

            beatGrid.Sweep(RevealPacing.BeatGrid);

            queueNext(RevealPacing.BeatGrid);
        }

        private void stageSnap()
        {
            var tier = detail!.Tier(analysis.OnsetSource)!;

            snapStage = new SnapStage(tier) { Alpha = 0 };
            overlayArea.Add(snapStage);

            setStage("STEP 4 — SNAP AND CLASSIFY",
                $"every onset quantised to the nearest 1/{tier.Preset.SnapDivision}"
                + $" · {tier.Merged} merged into a stronger neighbour"
                + $" · gaps typed by sustained energy");

            traceRack.FadeTo(0.12f, 400, Easing.OutQuint);
            snapStage.FadeIn(400, Easing.OutQuint);
            snapStage.Reveal(RevealPacing.Snap);

            queueNext(RevealPacing.Snap);
        }

        private void stageFunnel()
        {
            funnelStage = new TierFunnelStage(detail!, TierFunnelStage.Size.Full) { Alpha = 0 };
            overlayArea.Add(funnelStage);

            setStage("STEP 5 — DIFFICULTY SCALING",
                $"{detail!.Tiers!.Count} difficult{(detail.Tiers.Count == 1 ? "y" : "ies")} "
                + "from one song · the same onsets, thinned by different knobs");

            snapStage?.FadeOut(400, Easing.OutQuint);
            funnelStage.FadeIn(400, Easing.OutQuint);
            funnelStage.Reveal(RevealPacing.Funnel);

            queueNext(RevealPacing.Funnel);
        }

        private void stageHitObjects()
        {
            string tier = analysis.PreferredTier;
            var objects = analysis.ObjectsFor(tier);

            // Numbered in the order the sequence shows it, after snapping (4)
            // and difficulty scaling (5), not by the backend module that
            // does it — the viewer only ever sees the animation's order.
            setStage("STEP 6 — PLACEMENT",
                $"{objects.Count} objects on the {tier} difficulty · distance-snapped at "
                + $"{placementSpacing(tier):0.##} diameters per beat, turning at most "
                + $"{placementTurn(tier):0}°");

            assembly = new HitObjectAssemblyPreview(objects) { Alpha = 0 };
            assembly.ObjectPlaced = kind => ObjectRevealed?.Invoke(kind);

            snapStage?.FadeOut(400, Easing.OutQuint);
            funnelStage?.FadeOut(400, Easing.OutQuint);
            overlayArea.Add(assembly);

            // The chassis steps aside rather than vanishing: the objects came
            // from what is on it, and a cut would break that thread.
            tapeStrip.FadeTo(0.15f, 600, Easing.OutQuint);
            traceRack.FadeTo(0.15f, 600, Easing.OutQuint);
            assembly.FadeIn(600, Easing.OutQuint);
            assembly.Assemble(RevealPacing.Assembly);

            queueNext(RevealPacing.Assembly);
        }

        /// <summary>The tier's distance-snap setting, or the classifier's default when untraced.</summary>
        private double placementSpacing(string tier) =>
            detail?.Tier(tier)?.Preset.DistanceSpacing ?? 1.3;

        private double placementTurn(string tier) =>
            detail?.Tier(tier)?.Preset.MaxTurnDegrees ?? 80;

        private void stageFinale()
        {
            int written = analysis.HitObjects.Count;

            atFinale = true;
            FinaleReached?.Invoke();

            setStage("MAP READY",
                $"{written} difficult{(written == 1 ? "y" : "ies")} written · "
                + (AnalyserRequested != null ? "play it, or open the analyser" : "ready to play"));

            skipHint.FadeOut(300, Easing.OutQuint);

            var buttons = new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(14, 0),
            };

            if (AnalyserRequested != null)
            {
                buttons.Add(finaleButton("OPEN ANALYSER", RetroPalette.Cyan, () => AnalyserRequested?.Invoke()));
            }

            buttons.Add(finaleButton("PLAY", RetroPalette.Mint, finish));

            actionArea.Add(buttons);
            buttons.FadeInFromZero(400, Easing.OutQuint);

            // Deliberately no timeout: this is where the player chooses, and
            // leaving on a timer took that choice away from anyone who
            // looked away for a second.
        }
    }

    /// <summary>
    /// How long each stage of the reveal lasts, in milliseconds. The finale
    /// has no duration: it holds until the player picks PLAY or the analyser.
    ///
    /// <para>
    /// Gathered in one place because pacing is the thing most likely to be
    /// retuned, and because it can only be tuned by watching a real generated
    /// map — every density constant in this project's first pass was wrong on
    /// first guess.
    /// </para>
    ///
    /// <para>
    /// <b>The full sequence is eleven stages, not the nine the plan
    /// sketched</b>, because detection and tempo each split into method and
    /// result. At these durations it runs about 33 seconds. That is over the
    /// plan's 30-second budget and deliberately not compressed further:
    /// below roughly two and a half seconds a stage stops being readable, and
    /// a sequence of eleven unreadable stages is worse than a longer one. The
    /// skip is one keypress, and the plan's own answer if it proves too long
    /// is to move stages 3a and 5 into the analyser rather than to speed
    /// everything up.
    /// </para>
    ///
    /// <para>
    /// A map with no <c>dsp.json</c> plays only the five original stages and
    /// comes in near 17 seconds, which is where this started.
    /// </para>
    /// </summary>
    public static class RevealPacing
    {
        public const double Spectrogram = 3000;
        public const double Flux = 2600;
        public const double Threshold = 2900;
        public const double Onsets = 3200;
        public const double TempoSearch = 3800;
        public const double PhaseFit = 2900;
        public const double BeatGrid = 2800;
        public const double Snap = 3400;
        public const double Funnel = 3600;
        public const double Assembly = 4600;

        /// <summary>
        /// How long the full sequence runs, in milliseconds — the stage
        /// durations with the spectrogram's deliberate overlap taken off.
        ///
        /// Exposed so a test can hold the total to a budget rather than
        /// leaving it to drift a few hundred milliseconds per edit until
        /// nobody notices it has doubled.
        /// </summary>
        public static double FullSequence =>
            Spectrogram * 0.85 + Flux + Threshold + Onsets + TempoSearch
            + PhaseFit + BeatGrid + Snap + Funnel + Assembly;

        /// <summary>The same for a map with only an <c>analysis.json</c>.</summary>
        public static double WithoutTrace =>
            Spectrogram * 0.85 + Onsets + BeatGrid + Assembly;
    }
}
