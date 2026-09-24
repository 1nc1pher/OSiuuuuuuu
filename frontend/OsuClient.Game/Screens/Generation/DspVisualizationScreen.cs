using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
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
using OsuClient.Game.Audio;
using OsuClient.Game.Backend;
using OsuClient.Game.Graphics;
using OsuClient.Game.Graphics.Rack;
using OsuClient.Game.Screens.MainMenu;
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
        /// <summary>Under the reveal, not over it — background, not a performance.</summary>
        private const double preview_volume = 0.4;

        private readonly BackendPaths paths;
        private readonly GenerationRequest request;
        private readonly string? songsDirectory;
        private readonly string? wallpaper;

        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();

        [Resolved]
        private AudioManager audio { get; set; } = null!;

        [Resolved]
        private GameHost host { get; set; } = null!;

        private Container runningView = null!;
        private GenerationProgressPanel progressPanel = null!;
        private Container revealView = null!;
        private DspReveal? reveal;

        private BasicButton backButton = null!;

        private readonly TapeDeckSoundPlayer sounds = new TapeDeckSoundPlayer();

        private Track? previewTrack;

        private bool finished;
        private bool cancelled;
        private bool sequenceStarted;
        private bool handedOff;
        private double startTime;
        private string? beatmapFolder;

        /// <param name="wallpaper">
        /// The tape deck's wallpaper, so the run and the reveal sit on the same
        /// picture the deck did rather than cutting to a flat panel. Null
        /// falls through to MenuBackground's own gradient.
        /// </param>
        public DspVisualizationScreen(BackendPaths paths, GenerationRequest request, string? songsDirectory,
                                      string? wallpaper = null)
        {
            this.paths = paths;
            this.request = request;
            this.songsDirectory = songsDirectory;
            this.wallpaper = wallpaper;
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
            MenuBackground background;

            InternalChildren = new Drawable[]
            {
                // The deck's own backdrop — same art, same stillness, same
                // dimming — so pressing RECORD reads as the deck getting to
                // work, not as a jump to another screen.
                background = new MenuBackground { Drifting = false },
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(0f, 0f, 0f, 0.42f),
                },
                sounds,
                runningView = createRunningView(),
                revealView = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Alpha = 0,
                    Padding = new MarginPadding { Horizontal = 40, Vertical = 30 },
                },
            };

            background.SetBackground(wallpaper);
        }

        /// <summary>
        /// The running half of the screen: a reel-to-reel transport, a row of
        /// stage lamps driven by the backend's own output, and that output on
        /// a printout strip (GENERATION_REDESIGN_PLAN.md step 9).
        ///
        /// The Back button stays outside the panel because it belongs to the
        /// screen, not to the machine — it is how you leave, and a failed run
        /// is the only time it appears.
        /// </summary>
        private Container createRunningView() => new Container
        {
            RelativeSizeAxes = Axes.Both,
            Padding = new MarginPadding { Horizontal = 40, Vertical = 32 },
            Children = new Drawable[]
            {
                new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    // Room at the bottom for the Back button, made here
                    // because CompositeDrawable.Padding is protected.
                    Padding = new MarginPadding { Bottom = 56 },
                    Child = progressPanel = new GenerationProgressPanel(Path.GetFileName(request.AudioPath))
                    {
                        RelativeSizeAxes = Axes.Both,
                    },
                },
                backButton = new BasicButton
                {
                    Anchor = Anchor.BottomCentre,
                    Origin = Anchor.BottomCentre,
                    Size = new Vector2(220, 40),
                    Text = "Back",
                    BackgroundColour = RetroPalette.ChromeDark,
                    HoverColour = RetroPalette.Chrome.Darken(0.2f),
                    Alpha = 0,
                    Action = () =>
                    {
                        sounds.PlayKey();
                        this.Exit();
                    },
                },
            },
        };

        protected override void LoadComplete()
        {
            base.LoadComplete();

            startTime = Clock.CurrentTime;

            // The tape is moving for as long as the generator is: RECORD on
            // the deck started the motor, and this is it running.
            sounds.StartTransport();
            progressPanel.StageAdvanced = sounds.PlayRelay;
            progressPanel.TierAdvanced = sounds.PlayCounter;

            run();
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
                    var result = await runner.RunAsync(request, line => Schedule(() => progressPanel.AppendLine(line)),
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

        private void complete(GenerationResult result)
        {
            finished = true;
            Result = result;

            // The auto-stop either way; a failed run adds the refusal a
            // moment after, once the motor has wound down.
            sounds.StopTransport();
            sounds.PlayAutoStop();

            if (!result.Success)
                Scheduler.AddDelayed(sounds.PlayRefuse, 380);

            if (!result.Success)
            {
                progressPanel.Finish(false, $"generation failed — exit code {result.ExitCode}");
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
                progressPanel.Finish(true, "done — opening song select…");

                Scheduler.AddDelayed(handOff, 700);
                return;
            }

            startSequence(Analysis);
        }

        private void startSequence(AnalysisData analysis)
        {
            sequenceStarted = true;

            progressPanel.Finish(true, "analysis written");

            reveal = new DspReveal(analysis, DspDetail.LoadFromFolder(beatmapFolder),
                                    AnalysisData.FindSpectrogram(beatmapFolder))
            {
                Alpha = 0,
            };

            reveal.Completed = handOff;

            reveal.StageStarted = sounds.PlayRelay;
            reveal.OnsetRevealed = onset => sounds.PlayOnset(onset.Band);
            reveal.BeatRevealed = sounds.PlayBeat;
            reveal.ObjectRevealed = sounds.PlayPlace;
            reveal.FinaleReached = sounds.PlayReady;
            reveal.Skipped = sounds.PlayFastForward;
            reveal.ButtonPressed = sounds.PlayKey;

            // Offered only when there is a folder to point it at.
            if (beatmapFolder != null)
            {
                reveal.AnalyserRequested = () =>
                {
                    if (handedOff || !this.IsCurrentScreen())
                        return;

                    handedOff = true;
                    stopPreviewAudio();

                    this.Push(new Analysis.DspInspectorScreen(beatmapFolder));
                };
            }

            revealView.Add(reveal);

            runningView.FadeOut(300, Easing.OutQuint);
            revealView.FadeIn(400, Easing.OutQuint);
            reveal.FadeIn(400, Easing.OutQuint);

            startPreviewAudio();
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

            // The reveal lands on its finished picture and then calls back
            // into handOff through Completed, so there is one path out.
            if (reveal != null)
                reveal.SkipToEnd();
            else
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
                // On the finished screen Escape is Back, to the deck: there
                // is nothing left to skip, and PLAY is one click away.
                if (sequenceStarted && reveal?.AtFinale != true)
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
            sounds.StopTransport();

            this.FadeOut(200, Easing.OutQuint);

            return base.OnExiting(e);
        }

        public override void OnResuming(ScreenTransitionEvent e)
        {
            base.OnResuming(e);

            // Back from the analyser: the finished screen is still the place
            // to choose from, so it stays, with PLAY and the analyser both
            // live again.
            if (e.Last is Analysis.DspInspectorScreen && reveal?.AtFinale == true)
            {
                handedOff = false;
                startPreviewAudio();
                return;
            }

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
