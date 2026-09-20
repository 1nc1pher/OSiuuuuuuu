using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Framework.IO.Stores;
using osu.Framework.Platform;
using osu.Framework.Screens;
using OsuClient.Game.Backend;
using OsuClient.Game.Graphics;
using OsuClient.Game.Screens.SongSelect;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.Generation
{
    /// <summary>
    /// Runs one generation and then shows what the DSP actually did
    /// (FRONTEND_PLAN.md Phase 7), replacing Phase 6's plain stdout-tail
    /// progress screen.
    ///
    /// The screen has two halves. While Python is running it is a progress
    /// view — the pipeline's own output, and a clock, because a five-tier set
    /// is five real passes over the audio and silence reads as a hang. Once
    /// the run finishes it reads the <c>analysis.json</c> and
    /// <c>spectrogram.png</c> the backend just wrote and plays a fixed
    /// four-stage reveal through them, in the order BACKEND.md lists its
    /// steps: spectrogram, onsets, beat grid, placed objects.
    ///
    /// The reveal is a replay of written files, not a live view of the
    /// computation — decided in the plan, and the reason is pacing as much as
    /// decoupling: compute time swings from seconds to minutes with song
    /// length and hardware, so anything synced to it either flashes past or
    /// crawls. These timings are tuned for watching.
    ///
    /// Skipping is not optional. Escape or a click leaves at any point — on
    /// the tenth upload of a session nobody should have to sit through this
    /// again, and a spectacle you can't dismiss stops being one.
    /// </summary>
    public partial class DspVisualizationScreen : Screen
    {
        /// <summary>How many of the backend's most recent output lines stay on screen while it runs.</summary>
        private const int visible_log_lines = 10;

        // Stage durations, in milliseconds. ~18s of sequence in total, inside
        // the plan's 15-25s target: long enough to take each stage in, short
        // enough not to be the thing standing between a player and playing.
        private const double spectrogram_duration = 3500;
        private const double onset_duration = 4000;
        private const double beat_grid_duration = 3500;
        private const double assembly_duration = 6000;
        private const double finale_duration = 1400;

        /// <summary>Under the reveal, not over it — background, not a performance.</summary>
        private const double preview_volume = 0.4;

        private readonly BackendPaths paths;
        private readonly GenerationRequest request;
        private readonly string? songsDirectory;

        private readonly Queue<string> log = new Queue<string>();
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();

        [Resolved]
        private AudioManager audio { get; set; } = null!;

        [Resolved]
        private GameHost host { get; set; } = null!;

        private Container runningView = null!;
        private Container revealView = null!;

        private SpriteText statusLabel = null!;
        private SpriteText hintLabel = null!;
        private FillFlowContainer logFlow = null!;
        private BasicButton backButton = null!;

        private Container stageArea = null!;
        private RetroText stageTitle = null!;
        private RetroText stageDetail = null!;
        private SpriteText skipHint = null!;

        private SpectrogramReveal? spectrogram;
        private OnsetSparkLayer? sparks;
        private BeatGridReveal? beatGrid;
        private HitObjectAssemblyPreview? assembly;

        private Track? previewTrack;

        private bool finished;
        private bool cancelled;
        private bool sequenceStarted;
        private bool handedOff;
        private double startTime;
        private string? beatmapFolder;

        public DspVisualizationScreen(BackendPaths paths, GenerationRequest request, string? songsDirectory)
        {
            this.paths = paths;
            this.request = request;
            this.songsDirectory = songsDirectory;
        }

        /// <summary>Whether the run has ended, either way. Exposed for tests.</summary>
        public bool Finished => finished;

        /// <summary>How the run ended, or null while it's still going. Exposed for tests.</summary>
        public GenerationResult? Result { get; private set; }

        /// <summary>The analysis the reveal is playing, when there was one. Exposed for tests.</summary>
        public AnalysisData? Analysis { get; private set; }

        /// <summary>Whether the reveal is running. Exposed for tests.</summary>
        public bool SequenceRunning => sequenceStarted && !handedOff;

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChildren = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(0.06f, 0.06f, 0.10f, 1f),
                },
                runningView = createRunningView(),
                revealView = createRevealView(),
            };
        }

        private Container createRunningView() => new Container
        {
            RelativeSizeAxes = Axes.Both,
            Child = new FillFlowContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Width = 0.8f,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 14),
                Children = new Drawable[]
                {
                    new SpriteText
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Text = Path.GetFileName(request.AudioPath),
                        Font = FontUsage.Default.With(size: 26),
                        Colour = Color4.White,
                    },
                    statusLabel = new SpriteText
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Text = "Analysing…",
                        Font = FontUsage.Default.With(size: 17),
                        Colour = new Color4(0.55f, 0.8f, 1f, 1f),
                    },
                    new Container
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        RelativeSizeAxes = Axes.X,
                        Height = 200,
                        Masking = true,
                        CornerRadius = 6,
                        Children = new Drawable[]
                        {
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = new Color4(0.04f, 0.04f, 0.07f, 1f),
                            },
                            logFlow = new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Direction = FillDirection.Vertical,
                                Padding = new MarginPadding(12),
                                Spacing = new Vector2(0, 2),
                            },
                        },
                    },
                    hintLabel = new SpriteText
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Text = "Escape to cancel",
                        Font = FontUsage.Default.With(size: 13),
                        Colour = new Color4(0.55f, 0.55f, 0.65f, 1f),
                    },
                    backButton = new BasicButton
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Size = new Vector2(220, 40),
                        Text = "Back",
                        BackgroundColour = new Color4(0.28f, 0.28f, 0.36f, 1f),
                        HoverColour = new Color4(0.38f, 0.38f, 0.48f, 1f),
                        Alpha = 0,
                        Action = () => this.Exit(),
                    },
                },
            },
        };

        private Container createRevealView() => new Container
        {
            RelativeSizeAxes = Axes.Both,
            Alpha = 0,
            Padding = new MarginPadding { Horizontal = 40, Vertical = 32 },
            Children = new Drawable[]
            {
                new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 10),
                    Children = new Drawable[]
                    {
                        stageTitle = new RetroText
                        {
                            Font = RetroFontFamily.Display,
                            TextSize = 18,
                            Colour = new Color4(0.85f, 0.9f, 1f, 1f),
                            Text = string.Empty,
                        },
                        stageDetail = new RetroText
                        {
                            Font = RetroFontFamily.Body,
                            TextSize = 14,
                            Colour = new Color4(0.62f, 0.66f, 0.78f, 1f),
                            Text = string.Empty,
                        },
                        stageArea = new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            // Leaves room for the two headings above and the
                            // skip hint below, which sit outside this box.
                            Height = 0.82f,
                            Margin = new MarginPadding { Top = 6 },
                        },
                    },
                },
                skipHint = new SpriteText
                {
                    Anchor = Anchor.BottomRight,
                    Origin = Anchor.BottomRight,
                    Text = "Escape or click to skip",
                    Font = FontUsage.Default.With(size: 12),
                    Colour = new Color4(0.5f, 0.52f, 0.62f, 1f),
                },
            },
        };

        protected override void LoadComplete()
        {
            base.LoadComplete();

            startTime = Clock.CurrentTime;

            run();
        }

        protected override void Update()
        {
            base.Update();

            if (finished)
                return;

            double elapsed = (Clock.CurrentTime - startTime) / 1000;

            statusLabel.Text = $"Analysing…  {elapsed:0}s";
        }

        private void run()
        {
            var runner = new BackendRunner(paths);

            // The callback lands on a background thread, so everything it
            // touches on screen has to be scheduled back onto the update one.
            Task.Run(async () =>
            {
                try
                {
                    var result = await runner.RunAsync(request, line => Schedule(() => appendLine(line)),
                                                       cancellation.Token).ConfigureAwait(false);

                    Schedule(() => complete(result));
                }
                catch (OperationCanceledException)
                {
                    // Cancelling already left the screen; nothing to report.
                }
                catch (Exception e)
                {
                    Schedule(() => complete(new GenerationResult
                    {
                        Success = false,
                        ExitCode = -1,
                        Output = e.Message,
                    }));
                }
            });
        }

        private void appendLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            log.Enqueue(line);

            while (log.Count > visible_log_lines)
                log.Dequeue();

            logFlow.Clear();

            foreach (string entry in log)
            {
                logFlow.Add(new SpriteText
                {
                    Text = entry,
                    Font = FontUsage.Default.With(size: 13),
                    Colour = new Color4(0.72f, 0.75f, 0.82f, 1f),
                });
            }
        }

        private void complete(GenerationResult result)
        {
            finished = true;
            Result = result;

            if (!result.Success)
            {
                statusLabel.Text = $"Generation failed (exit code {result.ExitCode})";
                statusLabel.Colour = new Color4(1f, 0.45f, 0.45f, 1f);
                hintLabel.Text = "The output above is the generator's own error.";
                backButton.Alpha = 1;
                return;
            }

            beatmapFolder = result.BeatmapFolder;
            Analysis = AnalysisData.LoadFromFolder(beatmapFolder);

            // No analysis to play means an older map, a backend run with
            // --no-analysis, or a half-written file. The map itself is fine,
            // so the only sensible thing is to get out of the way.
            if (Analysis == null || !Analysis.HasContent)
            {
                statusLabel.Text = "Done — opening song select…";
                statusLabel.Colour = new Color4(0.4f, 0.9f, 0.5f, 1f);
                hintLabel.Text = string.Empty;

                Scheduler.AddDelayed(handOff, 700);
                return;
            }

            startSequence(Analysis);
        }

        private void startSequence(AnalysisData analysis)
        {
            sequenceStarted = true;

            runningView.FadeOut(300, Easing.OutQuint);
            revealView.FadeIn(400, Easing.OutQuint);

            spectrogram = new SpectrogramReveal(AnalysisData.FindSpectrogram(beatmapFolder),
                                                 analysis.Track.Duration)
            {
                RelativeSizeAxes = Axes.Both,
            };

            sparks = new OnsetSparkLayer(analysis.Onsets, spectrogram);
            beatGrid = new BeatGridReveal(analysis.BeatGrid, spectrogram);

            spectrogram.AddOverlay(sparks);
            spectrogram.AddOverlay(beatGrid);

            stageArea.Add(spectrogram);

            startPreviewAudio();

            // Each stage hands to the next on a delay rather than on a
            // completion callback: the stages deliberately overlap a little
            // (sparks start while the wipe is still finishing), which reads as
            // one continuous analysis instead of four separate animations.
            stage1();
        }

        private void stage1()
        {
            setStage("STEP 1 — SPECTROGRAM",
                $"{Analysis!.Track.Name} · {Analysis.Track.Duration:0}s · mel spectrogram at {Analysis.Track.SampleRate} Hz");

            spectrogram!.Reveal(spectrogram_duration);

            Scheduler.AddDelayed(stage2, spectrogram_duration * 0.85);
        }

        private void stage2()
        {
            if (handedOff)
                return;

            setStage("STEP 2 — ONSET DETECTION",
                $"{Analysis!.Onsets.Count} onsets from spectral flux · colour is the dominant band" +
                (string.IsNullOrEmpty(Analysis.OnsetSource) ? string.Empty : $" · {Analysis.OnsetSource} sensitivity"));

            sparks!.Sweep(onset_duration);

            Scheduler.AddDelayed(stage3, onset_duration);
        }

        private void stage3()
        {
            if (handedOff)
                return;

            setStage("STEP 3 — BEAT TRACKING",
                $"autocorrelation over the onset envelope · {Analysis!.BeatGrid.BeatTimes.Count} beats");

            beatGrid!.Sweep(beat_grid_duration);

            Scheduler.AddDelayed(stage4, beat_grid_duration);
        }

        private void stage4()
        {
            if (handedOff)
                return;

            string tier = Analysis!.PreferredTier;
            var objects = Analysis.ObjectsFor(tier);

            setStage("STEP 4 — HIT OBJECTS",
                $"{objects.Count} objects placed on the {tier} difficulty · snapped to the beat grid");

            assembly = new HitObjectAssemblyPreview(objects)
            {
                Alpha = 0,
            };

            stageArea.Add(assembly);

            // The spectrogram steps aside rather than vanishing: the objects
            // came from what is on it, and a cut would break that thread.
            spectrogram!.FadeOut(600, Easing.OutQuint);
            assembly.FadeIn(600, Easing.OutQuint);
            assembly.Assemble(assembly_duration);

            Scheduler.AddDelayed(finale, assembly_duration);
        }

        private void finale()
        {
            if (handedOff)
                return;

            int written = Analysis!.HitObjects.Count;

            setStage("MAP READY",
                $"{written} difficult{(written == 1 ? "y" : "ies")} written · opening song select");

            skipHint.FadeOut(300, Easing.OutQuint);

            Scheduler.AddDelayed(handOff, finale_duration);
        }

        private void setStage(string title, string detail)
        {
            stageTitle.Text = title;
            stageDetail.Text = detail;

            stageTitle.FadeInFromZero(260, Easing.OutQuint);
            stageDetail.FadeInFromZero(340, Easing.OutQuint);
        }

        /// <summary>
        /// Plays the generated map's own audio under the reveal, quietly. The
        /// backend copies the source file into the beatmap folder, so this is
        /// the same track the spectrogram on screen was computed from — which
        /// is the entire reason it is worth hearing.
        /// </summary>
        private void startPreviewAudio()
        {
            if (beatmapFolder == null || !Directory.Exists(beatmapFolder))
                return;

            try
            {
                string? audioFile = Directory.EnumerateFiles(beatmapFolder)
                                             .FirstOrDefault(BackendRunner.IsSupportedAudioFile);

                if (audioFile == null)
                    return;

                // The track lives in the beatmap's folder rather than the
                // game's resources, so it needs a store rooted there — the
                // same route Gameplay/PlayerScreen.cs loads a song through.
                var storage = host.GetStorage(beatmapFolder);
                var store = new StorageBackedResourceStore(storage);

                previewTrack = audio.GetTrackStore(store).Get(Path.GetFileName(audioFile));

                if (previewTrack == null)
                    return;

                // Under the sequence, not over it: this is the track the
                // spectrogram on screen was computed from, playing quietly
                // enough to stay background.
                previewTrack.Volume.Value = preview_volume;
                previewTrack.Start();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // The reveal is a visual sequence first; losing the audio
                // costs it atmosphere, not meaning.
            }
        }

        private void stopPreviewAudio()
        {
            previewTrack?.Stop();
            previewTrack = null;
        }

        /// <summary>
        /// Ends the sequence early and moves on. Everything already on screen
        /// jumps to its finished state first, so a skip lands on the finished
        /// picture rather than cutting away mid-animation.
        /// </summary>
        private void skip()
        {
            if (handedOff)
                return;

            spectrogram?.RevealImmediately();
            sparks?.SweepImmediately();
            beatGrid?.SweepImmediately();
            assembly?.AssembleImmediately();

            handOff();
        }

        private void handOff()
        {
            if (handedOff || !this.IsCurrentScreen())
                return;

            handedOff = true;

            stopPreviewAudio();

            this.Push(new RetroSongSelectScreen(songsDirectory, beatmapFolder));
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (e.Key == osuTK.Input.Key.Escape)
            {
                if (sequenceStarted)
                    skip();
                else
                    this.Exit();

                return true;
            }

            return base.OnKeyDown(e);
        }

        protected override bool OnClick(ClickEvent e)
        {
            if (!sequenceStarted)
                return base.OnClick(e);

            skip();
            return true;
        }

        public override void OnEntering(ScreenTransitionEvent e)
        {
            base.OnEntering(e);

            this.FadeInFromZero(250, Easing.OutQuint);
        }

        public override bool OnExiting(ScreenExitEvent e)
        {
            // Leaving mid-run kills the process — otherwise a cancelled
            // generation keeps churning in the background and still writes its
            // output when it finishes.
            cancelRun();
            stopPreviewAudio();

            this.FadeOut(200, Easing.OutQuint);

            return base.OnExiting(e);
        }

        public override void OnResuming(ScreenTransitionEvent e)
        {
            base.OnResuming(e);

            // Coming back from song select means this run is done with; step
            // aside rather than showing a finished sequence again.
            Schedule(() =>
            {
                if (this.IsCurrentScreen())
                    this.Exit();
            });
        }

        /// <summary>
        /// Cancels the run at most once.
        ///
        /// Both leaving the screen and disposing it want the process stopped,
        /// and the framework can dispose a drawable more than once — calling
        /// <see cref="CancellationTokenSource.Cancel()"/> after the source is
        /// disposed throws, which took the whole game down on exit in Phase 6
        /// until it was funnelled through one guarded path.
        /// </summary>
        private void cancelRun()
        {
            if (cancelled)
                return;

            cancelled = true;
            cancellation.Cancel();
        }

        protected override void Dispose(bool isDisposing)
        {
            cancelRun();
            cancellation.Dispose();
            stopPreviewAudio();

            base.Dispose(isDisposing);
        }
    }
}
