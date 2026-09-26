using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osu.Framework.IO.Stores;
using osu.Framework.Platform;
using osu.Framework.Screens;
using OsuClient.Game.Audio;
using OsuClient.Game.Beatmaps;
using OsuClient.Game.Beatmaps.HitObjects;
using OsuClient.Game.Graphics;
using OsuClient.Game.Input;
using OsuClient.Game.Screens.Gameplay.HUD;
using OsuClient.Game.Scores;
using OsuClient.Game.Screens.Results;
using OsuClient.Game.Screens.SongSelect;
using osuTK;
using osuTK.Graphics;
using osuTK.Input;

namespace OsuClient.Game.Screens.Gameplay
{
    /// <summary>
    /// The gameplay screen: an audio-synced beatmap clock, the hit object
    /// pool, input judging and the HUD.
    ///
    /// Everything inside the playfield runs on <see cref="GameplayClock"/>
    /// rather than wall time, so hit object animations, the hit windows and
    /// the music all agree with each other.
    /// </summary>
    public partial class PlayerScreen : Screen
    {
        /// <summary>How long the cleared/failed banner shows before the results screen takes over.</summary>
        private const double results_delay = 1200;

        /// <summary>Inset of the whole HUD from the window edges.</summary>
        private const float hud_padding = 20;

        /// <summary>Clear space between a HUD bar and whatever sits next to it.</summary>
        private const float hud_spacing = 10;

        /// <summary>
        /// The window size the game screen was designed and tuned in —
        /// osu.Framework's default window. The playfield and HUD are laid out
        /// at this size and scaled uniformly to the actual window, so a
        /// fullscreen window shows the same proportions, just larger.
        /// </summary>
        private static readonly Vector2 design_size = new Vector2(1366, 768);

        /// <summary>How far past the key overlay bars the side flash is allowed to reach.</summary>
        private const float beat_flash_overshoot = 20;

        /// <summary>
        /// Combo colours, from the palette the rest of the game shares — the
        /// same neon as song select's wheel and the difficulty cassettes.
        /// </summary>
        private static readonly Color4[] combo_colours =
        {
            RetroPalette.Cyan,
            RetroPalette.Magenta,
            RetroPalette.Amber,
            RetroPalette.Mint,
        };

        /// <summary>How long a failed run's tape takes to wind down.</summary>
        private const double tape_stop_duration = 1000;

        /// <summary>The slowest the tape is wound down to before it is stopped outright.</summary>
        private const double tape_stop_floor = 0.05;

        private readonly BeatmapSelection selection;
        private Beatmap Beatmap => selection.Beatmap;

        private readonly List<HitObjectData> hitObjects;
        private readonly int[] comboNumbers;
        private readonly int[] comboColourIndices;

        private readonly List<DrawableHitObject> activeObjects = new List<DrawableHitObject>();
        private readonly ScoreProcessor scoreProcessor = new ScoreProcessor();
        private readonly HealthProcessor healthProcessor;

        private readonly HashSet<Key> heldKeys = new HashSet<Key>();
        private readonly HashSet<MouseButton> heldButtons = new HashSet<MouseButton>();

        [Resolved]
        private AudioManager audio { get; set; } = null!;

        [Resolved]
        private GameHost host { get; set; } = null!;

        private readonly GameplayClock gameplayClock;

        private Track? track;
        private bool trackStarted;
        private HitSoundPlayer hitSounds = null!;

        private BeatBorderFlash beatFlash = null!;
        private BeatPulseBackground backgroundPulse = null!;
        private Container playfield = null!;
        private Container hitObjectContainer = null!;
        private ComboCounter comboCounter = null!;
        private HealthBar healthBar = null!;
        private SongProgressBar progressBar = null!;
        private ScorePanel scorePanel = null!;
        private CountIn countIn = null!;
        private RetroText stateText = null!;
        private Box failShade = null!;
        private GameplayDeckSounds deckSounds = null!;

        /// <summary>
        /// The playing difficulty's own colour — its cassette's in song
        /// select — for the label, the beat flash and the needle-drop cue.
        /// </summary>
        private Color4 accent;

        /// <summary>
        /// The track's speed, as a frequency adjustment: 1 while playing, run
        /// down towards 0 when the run fails, so the music winds down like a
        /// tape losing power rather than cutting off.
        /// </summary>
        private readonly BindableDouble tapeSpeed = new BindableDouble(1);

        private int lastCombo;
        private PauseOverlay pauseOverlay = null!;
        private KeyTimingBar firstKeyBar = null!;
        private KeyTimingBar secondKeyBar = null!;
        private SkipOverlay skipOverlay = null!;
        private PerformanceOverlay performanceOverlay = null!;

        /// <summary>Gaps in the map with nothing to hit — see <see cref="BreakPeriods"/>.</summary>
        private readonly List<BreakPeriod> breaks;

        private int spawnIndex;
        private bool completed;
        private bool failed;
        private bool paused;
        private bool resultsQueued;

        /// <summary>Where a finished run is kept if it is a best. Null outside the game, as in tests.</summary>
        [Resolved(CanBeNull = true)]
        private HighScoreStore? highScores { get; set; }
        private double? lastDrainTime;

        public PlayerScreen(BeatmapSelection selection)
        {
            this.selection = selection;

            // osu!mania holds and anything else the decoder doesn't model as a
            // circle/slider/spinner are dropped rather than guessed at.
            hitObjects = selection.Beatmap.HitObjects
                                  .Where(h => h is HitCircleData or SliderData or SpinnerData)
                                  .OrderBy(h => h.StartTime)
                                  .ToList();

            comboNumbers = new int[hitObjects.Count];
            comboColourIndices = new int[hitObjects.Count];

            int number = 0;
            int colourIndex = -1;

            for (int i = 0; i < hitObjects.Count; i++)
            {
                if (i == 0 || hitObjects[i].NewCombo || hitObjects[i] is SpinnerData)
                {
                    number = 1;
                    colourIndex = (colourIndex + 1) % combo_colours.Length;
                }
                else
                {
                    number++;
                }

                comboNumbers[i] = number;
                comboColourIndices[i] = colourIndex;
            }

            breaks = BreakPeriods.Compute(hitObjects, Beatmap.Difficulty.ApproachRate, Beatmap.Difficulty.OverallDifficulty);

            // Enough silence up front for the first object's approach circle to
            // play out in full before the audio reaches it.
            double leadIn = Math.Max(1000, JudgementProcessor.Preempt(Beatmap.Difficulty.ApproachRate) + 500);

            gameplayClock = new GameplayClock(-leadIn);
            healthProcessor = new HealthProcessor(Beatmap.Difficulty.HPDrainRate);
        }

        /// <summary>Whether the play is over — every object judged, or health ran out. Exposed for tests.</summary>
        public bool Completed => completed;

        /// <summary>Whether the play ended because health ran out. Exposed for tests.</summary>
        public bool Failed => failed;

        /// <summary>Whether gameplay is currently paused. Exposed for tests.</summary>
        public bool Paused => paused;

        /// <summary>Current combo/accuracy/score state. Exposed for tests.</summary>
        public ScoreProcessor ScoreState => scoreProcessor;

        /// <summary>Current health state. Exposed for tests.</summary>
        public HealthProcessor HealthState => healthProcessor;

        /// <summary>Milliseconds into the audio track. Negative during the lead-in.</summary>
        public double GameplayTime => gameplayClock.CurrentTime;

        /// <summary>Whether the skip prompt for a long gap is currently shown. Exposed for tests.</summary>
        public bool SkipOverlayVisible => skipOverlay.State.Value == Visibility.Visible;

        /// <summary>Whether the current-performance readout for a short gap is currently shown. Exposed for tests.</summary>
        public bool PerformanceOverlayVisible => performanceOverlay.State.Value == Visibility.Visible;

        [BackgroundDependencyLoader]
        private void load(IRenderer renderer)
        {
            hitSounds = new HitSoundPlayer(audio);

            // Hit circles share one generated vinyl texture. Made here, while
            // the screen loads in the background, rather than by the first
            // circle to appear: circles load on the update thread mid-play,
            // and generating it there was a hitch as the first note came in.
            VinylTexture.Get(renderer);
            deckSounds = new GameplayDeckSounds(audio);
            accent = difficultyAccent();

            InternalChildren = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(0.05f, 0.05f, 0.08f, 1f),
                },
                backgroundPulse = new BeatPulseBackground(Beatmap, selection.Entry.Set?.BackgroundPath),
                // Dimmed the way osu! dims gameplay backgrounds by default —
                // without it, a bright or busy image behind the circles would
                // fight with them for attention instead of just setting a mood.
                // The dim carries song select's sunset grade, purple at the
                // top to red at the bottom, at the same strength: the same
                // single layer as a plain black dim, so it costs nothing.
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = ColourInfo.GradientVertical(
                        new Color4(0.05f, 0.01f, 0.08f, 0.70f),
                        new Color4(0.14f, 0.03f, 0.06f, 0.66f)),
                },
                // Drains the picture when a run fails. Invisible, and so not
                // drawn at all, until then.
                failShade = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(0.04f, 0.04f, 0.05f, 1f),
                    Alpha = 0,
                },
                // Everything the player reads or aims at, laid out at the
                // window size the screen was designed in and scaled to the
                // real window as one piece. Sized in raw pixels instead, a
                // fullscreen window grew the playfield (and so the circles)
                // past its designed share of the screen while every HUD panel
                // stayed its small-window size. The layout still fills the
                // whole window — only sizes scale — so panels anchored to an
                // edge stay on that edge.
                new DrawSizePreservingFillContainer
                {
                    TargetDrawSize = design_size,
                    Strategy = DrawSizePreservationStrategy.Minimum,
                    Children = new Drawable[]
                    {
                        // Behind the playfield and HUD: an ambient effect, not
                        // something that should ever compete with them for attention.
                        // Reaches a little past the key overlay's own bars (see
                        // beat_flash_overshoot) rather than stopping exactly at them.
                        beatFlash = new BeatBorderFlash(
                            Beatmap, Beatmap.Difficulty, hud_padding + KeyTimingBar.BarWidth + beat_flash_overshoot, accent),
                        playfield = new Container
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Size = new Vector2(512, 384),
                            // Everything below here runs on gameplay time, not wall time.
                            Clock = gameplayClock.FrameClock,
                            Child = hitObjectContainer = new Container { RelativeSizeAxes = Axes.Both },
                        },
                        // Padding rather than per-child margins: a relatively-sized
                        // child measures against this container's padded area, so the
                        // full-width progress bar ends up inside the window instead of
                        // running off the right edge by the width of its own margin.
                        new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Padding = new MarginPadding(hud_padding),
                            Children = new Drawable[]
                            {
                                healthBar = new HealthBar
                                {
                                    Anchor = Anchor.TopLeft,
                                    Origin = Anchor.TopLeft,
                                },
                                progressBar = new SongProgressBar
                                {
                                    Anchor = Anchor.BottomCentre,
                                    Origin = Anchor.BottomCentre,
                                },
                                comboCounter = new ComboCounter
                                {
                                    // Clear of the progress bar along the bottom edge.
                                    Margin = new MarginPadding { Bottom = SongProgressBar.BarHeight + hud_spacing },
                                },
                                scorePanel = new ScorePanel
                                {
                                    Anchor = Anchor.TopRight,
                                    Origin = Anchor.TopRight,
                                },
                                // The song on its cassette label, below the meter
                                // rather than across it.
                                new CassetteLabel(Beatmap.Metadata.Artist, Beatmap.Metadata.Title, Beatmap.Metadata.Version, accent)
                                {
                                    Anchor = Anchor.TopLeft,
                                    Origin = Anchor.TopLeft,
                                    Margin = new MarginPadding { Top = HealthBar.BarHeight + hud_spacing },
                                },
                                // Top centre: clear of the corners' HUD and of the
                                // approach circles the first notes arrive through.
                                countIn = new CountIn(Beatmap)
                                {
                                    Anchor = Anchor.TopCentre,
                                    Origin = Anchor.TopCentre,
                                },
                                stateText = new RetroText
                                {
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.Centre,
                                    Font = RetroFontFamily.Display,
                                    TextSize = 24,
                                    Colour = RetroPalette.Text,
                                    Alpha = 0,
                                },
                                // One panel per hit key, down either side of the
                                // playfield and clear of it at any window size.
                                firstKeyBar = new KeyTimingBar(Beatmap.Difficulty.OverallDifficulty)
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                },
                                secondKeyBar = new KeyTimingBar(Beatmap.Difficulty.OverallDifficulty)
                                {
                                    Anchor = Anchor.CentreRight,
                                    Origin = Anchor.CentreRight,
                                },
                            },
                        },
                        // Above the HUD, below the pause menu: one gap in the map at a
                        // time is ever active, so only one of these two is ever shown.
                        skipOverlay = new SkipOverlay(performSkip),
                        performanceOverlay = new PerformanceOverlay(),
                        // Above the HUD: while paused it covers everything, and while
                        // hidden it takes no input at all.
                        pauseOverlay = new PauseOverlay(
                            selection.Entry.Set?.BackgroundPath,
                            $"{Beatmap.Metadata.Artist} - {Beatmap.Metadata.Title}",
                            Beatmap.Metadata.Version,
                            onContinue: resume,
                            onRestart: restart,
                            onQuit: exitToSongSelect),
                    },
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            countIn.Counted += count => deckSounds.PlayCountTick(last: count == 1);

            loadTrack();

            if (hitObjects.Count == 0)
                completeMap();
        }

        /// <summary>
        /// The playing difficulty's colour, worked out the way song select
        /// colours its cassettes: its place among the set's difficulties,
        /// easiest first, spread across the palette's difficulty ramp.
        /// </summary>
        private Color4 difficultyAccent()
        {
            var difficulties = selection.Entry.Set?.Beatmaps
                                        .OrderBy(DifficultyTier.SortKey)
                                        .ToList();

            if (difficulties == null || difficulties.Count == 0)
                return RetroPalette.Cyan;

            int index = difficulties.IndexOf(Beatmap);

            if (index < 0)
                index = difficulties.FindIndex(b => b.ContentHash == Beatmap.ContentHash);

            return RetroPalette.ForDifficultyRank(Math.Max(0, index), difficulties.Count);
        }

        private void loadTrack()
        {
            string? path = selection.Entry.Set?.AudioPath;

            if (path == null || !File.Exists(path))
                return;

            string? directory = Path.GetDirectoryName(path);

            if (directory == null)
                return;

            // The track lives in the beatmap's own folder, not in the game's
            // resources, so it needs a store rooted there.
            var storage = host.GetStorage(directory);
            var store = new StorageBackedResourceStore(storage);

            track = audio.GetTrackStore(store).Get(Path.GetFileName(path));
            track?.AddAdjustment(AdjustableProperty.Frequency, tapeSpeed);
        }

        protected override void Update()
        {
            base.Update();

            updatePlayfieldScale();

            // The tape only moves while the music actually plays: not during
            // the silent lead-in, not while paused, not once a run has failed.
            progressBar.Running = !paused && !failed && trackStarted && track?.IsRunning == true;

            // Paused: the gameplay clock stops being advanced, which freezes
            // everything downstream of it — hit object transforms, the hit
            // windows and health drain all read from it, so none of them can
            // run on while the menu is up.
            if (paused)
                return;

            // The track is the authority on time once it's playing; before
            // that the clock free-runs through the lead-in.
            double? audioTime = track != null && track.IsRunning ? track.CurrentTime : null;

            gameplayClock.Advance(Clock.ElapsedFrameTime, audioTime);

            double time = gameplayClock.CurrentTime;

            // A failed run is over: no more spawning, judging or draining —
            // only the banner and the pending move to the results screen.
            if (failed)
                return;

            if (!trackStarted && time >= 0)
            {
                track?.Start();
                trackStarted = true;

                // The needle meets the record as the music starts.
                if (track != null)
                    deckSounds.PlayNeedleDrop();
            }

            spawnDueObjects(time);

            Vector2 cursor = cursorPosition();
            bool anyKeyHeld = heldKeys.Count > 0 || heldButtons.Count > 0;

            foreach (var hitObject in activeObjects)
                hitObject.UpdateGameplay(time, cursor, anyKeyHeld);

            activeObjects.RemoveAll(o =>
            {
                if (!o.IsReadyForRemoval(time))
                    return false;

                hitObjectContainer.Remove(o, true);
                return true;
            });

            updateHealth(time);
            updateProgress(time);
            updateBreakState(time);
            beatFlash.SetTime(time);
            backgroundPulse.SetTime(time);
            countIn.SetTime(time);

            if (!completed && healthProcessor.HasFailed)
                failMap();
            else if (!completed && spawnIndex >= hitObjects.Count && activeObjects.Count == 0 && hitObjects.Count > 0)
                completeMap();
        }

        /// <summary>
        /// Drains health for the time elapsed since the last frame, but only
        /// across the map's playable span — no draining during the lead-in or
        /// after the last object, where the player has nothing to hit.
        /// </summary>
        private void updateHealth(double time)
        {
            double previous = lastDrainTime ?? time;
            lastDrainTime = time;

            // No draining during a break either — there's nothing on screen
            // to have missed, the same reason osu! itself holds HP steady
            // through one.
            if (completed || time < Beatmap.FirstHitObjectTime || time > Beatmap.LastHitObjectTime
                || FindBreak(time) != null)
            {
                healthBar.SetHealth(healthProcessor.Health);
                return;
            }

            // The gameplay clock can jump when it resyncs to the audio, which
            // would otherwise drain a chunk of health in a single frame.
            double elapsed = Math.Clamp(time - previous, 0, 100);

            healthProcessor.Drain(elapsed);
            healthBar.SetHealth(healthProcessor.Health);
        }

        private void updateProgress(double time)
        {
            double start = Beatmap.FirstHitObjectTime;
            double span = Beatmap.LastHitObjectTime - start;

            progressBar.SetProgress(span > 0 ? (time - start) / span : 0);
            progressBar.SetTime(Math.Clamp(time - start, 0, Math.Max(0, span)), Math.Max(0, span));
        }

        /// <summary>The break covering <paramref name="time"/>, if any — at most one ever can.</summary>
        private BreakPeriod? FindBreak(double time)
        {
            foreach (var candidate in breaks)
            {
                if (candidate.Contains(time))
                    return candidate;
            }

            return null;
        }

        /// <summary>Shows whichever overlay the current gap calls for, or neither once it's passed.</summary>
        private void updateBreakState(double time)
        {
            var active = FindBreak(time);

            if (active is not BreakPeriod current)
            {
                skipOverlay.Hide();
                performanceOverlay.Hide();
                return;
            }

            if (current.Kind == BreakKind.Skip)
            {
                performanceOverlay.Hide();
                skipOverlay.Show();
                skipOverlay.SetRemaining(current.RemainingFraction(time));
            }
            else
            {
                skipOverlay.Hide();
                performanceOverlay.Show();
                performanceOverlay.UpdateStats(scoreProcessor);
            }
        }

        /// <summary>
        /// Jumps straight to the end of the current skippable break, if
        /// there is one — a no-op otherwise, so binding it unconditionally to
        /// a key press is safe.
        /// </summary>
        private void performSkip()
        {
            if (paused || completed)
                return;

            var active = FindBreak(gameplayClock.CurrentTime);

            if (active is not { Kind: BreakKind.Skip } current)
                return;

            gameplayClock.Seek(current.End);
            track?.Seek(current.End);

            // Otherwise the next frame's health drain would see a jump of up
            // to 100ms of "elapsed" time across a gap nothing could be missed
            // in.
            lastDrainTime = current.End;

            skipOverlay.Hide();
        }

        private Vector2 cursorPosition() =>
            GetContainingInputManager()?.CurrentState.Mouse.Position ?? Vector2.Zero;

        private void updatePlayfieldScale()
        {
            const float vertical_margin = 160f;

            // Measured in the design-size layout the playfield sits in (see
            // design_size), not in window pixels, so the playfield keeps the
            // same share of the screen at any window size.
            Vector2 area = playfield.Parent?.DrawSize ?? DrawSize;

            float availableHeight = Math.Max(100f, area.Y - vertical_margin);
            float scale = Math.Min(area.X / 512f, availableHeight / 384f);

            playfield.Scale = new Vector2(scale);
        }

        private void spawnDueObjects(double time)
        {
            double preempt = JudgementProcessor.Preempt(Beatmap.Difficulty.ApproachRate);

            while (spawnIndex < hitObjects.Count && time >= hitObjects[spawnIndex].StartTime - preempt)
            {
                var drawable = createDrawable(spawnIndex);

                drawable.Judged += onObjectJudged;
                drawable.TickJudged += onTickJudged;
                drawable.BonusAwarded += onBonusAwarded;

                // Later objects render behind earlier ones, as in osu!.
                drawable.Depth = spawnIndex;

                hitObjectContainer.Add(drawable);
                activeObjects.Add(drawable);

                spawnIndex++;
            }
        }

        private DrawableHitObject createDrawable(int index)
        {
            var data = hitObjects[index];
            Color4 colour = combo_colours[comboColourIndices[index]];
            int number = comboNumbers[index];

            return data switch
            {
                HitCircleData circle => new DrawableHitCircle(circle, Beatmap.Difficulty, colour, number),
                SliderData slider => new DrawableSlider(slider, Beatmap.Difficulty, colour, number),
                SpinnerData spinner => new DrawableSpinner(spinner, Beatmap.Difficulty, colour),
                _ => throw new InvalidOperationException($"unsupported hit object type {data.GetType().Name}"),
            };
        }

        private void onObjectJudged(DrawableHitObject hitObject, HitResult result)
        {
            scoreProcessor.Apply(result);
            healthProcessor.Apply(result);
            updateHud();

            if (result != HitResult.Miss)
                hitSounds.PlayHit();

            showJudgementPopup(hitObject.Data.Position, result);

            if (result != HitResult.Miss && hitObject.Data is HitCircleData)
                showHitBurst(hitObject.Data.Position, judgementColour(result));
        }

        private void onTickJudged(DrawableHitObject hitObject, bool hit)
        {
            scoreProcessor.ApplyTick(hit);
            healthProcessor.ApplyTick(hit);
            updateHud();

            if (hit)
                hitSounds.PlayTick();
        }

        private void onBonusAwarded(DrawableHitObject hitObject)
        {
            // Purely a reward: unlike a tick, there's no way to earn one
            // without the spinner already being complete, so it never touches
            // health in either direction.
            scoreProcessor.ApplyBonus();
            updateHud();

            hitSounds.PlayTick();

            // Above the spinner's label, over the dark record, where an
            // amber number reads; on the label itself it vanished.
            showBonusPopup(hitObject.Data.Position + new Vector2(0, -80));
        }

        private void updateHud()
        {
            int combo = scoreProcessor.Combo;

            // The tape counter snapping back to zero is heard as well as seen.
            if (combo == 0 && lastCombo > 0)
                deckSounds.PlayCounterReset();

            lastCombo = combo;

            comboCounter.SetCombo(combo);
            scorePanel.SetScore(scoreProcessor.Score);
            scorePanel.SetAccuracy(scoreProcessor.Accuracy);
        }

        private void showJudgementPopup(Vector2 position, HitResult result)
        {
            string text = result switch
            {
                HitResult.Great => "300",
                HitResult.Ok => "100",
                HitResult.Meh => "50",
                _ => "X",
            };

            showPopup(position, text, judgementColour(result));
        }

        /// <summary>
        /// Each judgement's colour, from the palette: the same order of hues
        /// as the key bars' timing bands, and a miss in magenta rather than a
        /// warning red, so it reads as part of the same display.
        /// </summary>
        private static Color4 judgementColour(HitResult result) => result switch
        {
            HitResult.Great => RetroPalette.Cyan,
            HitResult.Ok => RetroPalette.Mint,
            HitResult.Meh => RetroPalette.Amber,
            _ => RetroPalette.Magenta,
        };

        /// <summary>
        /// A short neon ring off a hit circle: it opens out from the circle's
        /// edge and fades, in the judgement's colour.
        /// </summary>
        private void showHitBurst(Vector2 position, Color4 colour)
        {
            float diameter = (float)Beatmap.Difficulty.CircleRadius * 2;

            var ring = new CircularContainer
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.Centre,
                Position = position,
                Size = new Vector2(diameter),
                Masking = true,
                BorderThickness = 3,
                BorderColour = colour,
                Depth = float.MinValue,
                Child = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0, AlwaysPresent = true },
            };

            hitObjectContainer.Add(ring);

            ring.ScaleTo(1.55f, 320, Easing.OutQuint);
            ring.FadeOut(320, Easing.OutQuad);

            Scheduler.AddDelayed(() => hitObjectContainer.Remove(ring, true), 340);
        }

        /// <summary>The gold "+100" that flies off a spinner for each full extra rotation past its requirement.</summary>
        private void showBonusPopup(Vector2 position) =>
            showPopup(position, "+100", RetroPalette.Amber);

        private void showPopup(Vector2 position, string text, Color4 colour)
        {
            var popup = new RetroText
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.Centre,
                Position = position,
                Font = RetroFontFamily.Display,
                TextSize = 18,
                Colour = colour,
                Text = text,
                Depth = float.MinValue,
            };

            hitObjectContainer.Add(popup);

            popup.MoveTo(position + new Vector2(0, -30), 500, Easing.OutQuint);
            popup.FadeOut(500, Easing.OutQuint);

            Scheduler.AddDelayed(() => hitObjectContainer.Remove(popup, true), 520);
        }

        private void completeMap()
        {
            completed = true;

            // The needle lifts off the record at the end of the side.
            stateText.Text = "SIDE A COMPLETE";
            stateText.Colour = RetroPalette.Mint;
            stateText.FadeIn(300, Easing.OutQuint);

            if (track != null)
                deckSounds.PlayNeedleLift();

            queueResults();
        }

        private void failMap()
        {
            completed = true;
            failed = true;

            // The tape winds down in pitch and speed together, like a deck
            // losing power, and the auto-stop catches as it runs out. The
            // gameplay clock follows the track, so the notes slow with it.
            this.TransformBindableTo(tapeSpeed, tape_stop_floor, tape_stop_duration, Easing.InQuad);
            Scheduler.AddDelayed(() =>
            {
                track?.Stop();
                deckSounds.PlayAutoStop();
            }, tape_stop_duration);

            // The colour drains out of the picture as the tape runs down.
            failShade.FadeTo(0.55f, tape_stop_duration, Easing.OutQuad);
            playfield.FadeColour(new Color4(0.5f, 0.5f, 0.55f, 1f), tape_stop_duration, Easing.OutQuad);

            stateText.Text = "TAPE STOPPED";
            stateText.Colour = new Color4(1f, 0.3f, 0.32f, 1f);
            stateText.FadeIn(300, Easing.OutQuint);

            queueResults();
        }

        /// <summary>
        /// Moves to the results screen after a beat, so the last judgement and
        /// the cleared/failed banner are actually visible before the screen
        /// changes out from under the player.
        /// </summary>
        private void queueResults()
        {
            if (resultsQueued)
                return;

            resultsQueued = true;

            // Snapshot now rather than when the screen is pushed: the play is
            // decided at this moment, and nothing that happens during the
            // banner should be able to edit the result.
            var result = ResultsScreen.Result.From(Beatmap, scoreProcessor, failed, selection.Entry.Set?.BackgroundPath);

            // Kept now too, for the same reason: the run is decided here. What
            // it had to beat is read first, so the results can say.
            if (highScores != null)
            {
                result.PreviousBest = highScores.Get(Beatmap.ContentHash)?.Score;
                result.NewHighScore = highScores.Submit(Beatmap.ContentHash, result);
            }

            Scheduler.AddDelayed(() =>
            {
                if (!this.IsCurrentScreen())
                    return;

                track?.Stop();
                this.Push(new ResultsScreen(result));
            }, results_delay);
        }

        /// <summary>
        /// Offers a press to the earliest object still waiting for one. Only
        /// that object gets the chance, which is osu!'s note lock: you can't
        /// reach past a circle you haven't hit yet to hit a later one.
        /// </summary>
        /// <returns>
        /// The signed timing error of the object that took the press, or null
        /// if nothing did.
        /// </returns>
        private double? attemptPress(Vector2 cursorScreenSpace)
        {
            double time = gameplayClock.CurrentTime;

            foreach (var hitObject in activeObjects)
            {
                if (!hitObject.AcceptsPress)
                    continue;

                return hitObject.TryPress(time, cursorScreenSpace) ? hitObject.HitError : null;
            }

            return null;
        }

        private KeyTimingBar keyBar(HitKeyColumn column) =>
            column == HitKeyColumn.First ? firstKeyBar : secondKeyBar;

        /// <summary>Lights up the key's panel, and plots the hit on it if the press landed one.</summary>
        private void pressKey(HitKeyColumn column, Vector2 cursorScreenSpace)
        {
            var bar = keyBar(column);

            bar.Press();

            if (attemptPress(cursorScreenSpace) is double error)
                bar.RecordHit(error);
        }

        private void notifyRelease()
        {
            if (heldKeys.Count > 0 || heldButtons.Count > 0)
                return;

            double time = gameplayClock.CurrentTime;

            foreach (var hitObject in activeObjects)
                hitObject.OnRelease(time);
        }

        /// <summary>
        /// Stops the clock and the music and raises the pause menu. A run that
        /// has already ended can't be paused — there's nothing left to come
        /// back to, and the results screen is already on its way.
        /// </summary>
        private void pause()
        {
            if (paused || completed || failed)
                return;

            paused = true;
            track?.Stop();

            // Nothing can be held across a pause: a key still down when the
            // menu opened would otherwise keep tracking a slider through it.
            heldKeys.Clear();
            heldButtons.Clear();
            notifyRelease();

            firstKeyBar.Release();
            secondKeyBar.Release();

            pauseOverlay.Show();
        }

        private void resume()
        {
            if (!paused)
                return;

            paused = false;
            pauseOverlay.Hide();

            // Only if the lead-in had already handed over to the track —
            // otherwise starting it here would jump the music ahead of the
            // silence the first object is still approaching through.
            if (trackStarted)
                track?.Start();
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (e.Key == Key.Escape)
            {
                // Once the run is over, escape still leaves outright: there is
                // no gameplay left to pause.
                if (completed || failed)
                    exitToSongSelect();
                else if (paused)
                    resume();
                else
                    pause();

                return true;
            }

            if (paused)
                return true;

            // osu!'s own break-skip binding. A no-op outside a skippable
            // break, so it's safe to always claim the key rather than only
            // while the prompt happens to be showing.
            if (e.Key == Key.Space)
            {
                performSkip();
                return true;
            }

            if (!e.Repeat && GameplayKeyBindings.ColumnFor(e.Key) is HitKeyColumn column)
            {
                heldKeys.Add(e.Key);
                pressKey(column, e.ScreenSpaceMousePosition);
                return true;
            }

            return base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyUpEvent e)
        {
            if (heldKeys.Remove(e.Key))
            {
                if (GameplayKeyBindings.ColumnFor(e.Key) is HitKeyColumn column)
                    keyBar(column).Release();

                notifyRelease();
            }

            base.OnKeyUp(e);
        }

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            if (paused)
                return true;

            heldButtons.Add(e.Button);

            if (GameplayKeyBindings.ColumnFor(e.Button) is HitKeyColumn column)
                pressKey(column, e.ScreenSpaceMousePosition);
            else
                attemptPress(e.ScreenSpaceMousePosition);

            return true;
        }

        protected override void OnMouseUp(MouseUpEvent e)
        {
            if (heldButtons.Remove(e.Button))
            {
                if (GameplayKeyBindings.ColumnFor(e.Button) is HitKeyColumn column)
                    keyBar(column).Release();

                notifyRelease();
            }

            base.OnMouseUp(e);
        }

        /// <summary>
        /// Plays the same map again from the top, as a fresh screen.
        ///
        /// A new player is pushed over this one rather than this one being
        /// reset in place — every piece of run state (score, combo, health,
        /// judged objects, the break and skip overlays) starts clean without a
        /// reset path to keep in step with each of them. Marked not valid for
        /// resume, this one is then exited along with the new one when that
        /// leaves, so quitting still lands on song select, not back here.
        /// Pushed rather than going back to song select to push again, which
        /// would flash song select and its preview audio while the new player
        /// loads.
        /// </summary>
        private void restart()
        {
            if (!this.IsCurrentScreen())
                return;

            track?.Stop();
            ValidForResume = false;

            this.Push(new PlayerScreen(selection));
        }

        private void exitToSongSelect()
        {
            track?.Stop();
            this.Exit();
        }

        public override void OnEntering(ScreenTransitionEvent e)
        {
            base.OnEntering(e);

            this.FadeInFromZero(250, Easing.OutQuint);
        }

        public override void OnResuming(ScreenTransitionEvent e)
        {
            base.OnResuming(e);

            // The only thing that gets pushed above gameplay is the results
            // screen, so resuming means the player dismissed it — step aside
            // to song select instead of dropping them back into a map that
            // has already ended.
            //
            // Deferred by a frame: exiting from inside the resume callback
            // re-enters the screen stack while it is still unwinding.
            if (resultsQueued)
                Schedule(() =>
                {
                    if (this.IsCurrentScreen())
                        this.Exit();
                });
        }

        public override bool OnExiting(ScreenExitEvent e)
        {
            track?.Stop();
            track?.RemoveAdjustment(AdjustableProperty.Frequency, tapeSpeed);
            this.FadeOut(200, Easing.OutQuint);

            return base.OnExiting(e);
        }
    }
}
