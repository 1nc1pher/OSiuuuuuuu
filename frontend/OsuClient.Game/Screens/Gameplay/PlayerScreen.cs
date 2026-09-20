using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
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

        /// <summary>How far past the key overlay bars the side flash is allowed to reach.</summary>
        private const float beat_flash_overshoot = 20;

        private static readonly Color4[] combo_colours =
        {
            new Color4(1f, 0.75f, 0f, 1f),
            new Color4(0f, 0.79f, 0f, 1f),
            new Color4(0f, 0.6f, 1f, 1f),
            new Color4(1f, 0f, 0.47f, 1f),
        };

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
        private RetroText scoreText = null!;
        private RetroText accuracyText = null!;
        private RetroText stateText = null!;
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
        private void load()
        {
            hitSounds = new HitSoundPlayer(audio);

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
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(0f, 0f, 0f, 0.65f),
                },
                // Behind the playfield and HUD: an ambient effect, not
                // something that should ever compete with them for attention.
                // Reaches a little past the key overlay's own bars (see
                // beat_flash_overshoot) rather than stopping exactly at them.
                beatFlash = new BeatBorderFlash(
                    Beatmap, Beatmap.Difficulty, hud_padding + KeyTimingBar.BarWidth + beat_flash_overshoot),
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
                        new FillFlowContainer
                        {
                            Anchor = Anchor.TopRight,
                            Origin = Anchor.TopRight,
                            AutoSizeAxes = Axes.Both,
                            Direction = FillDirection.Vertical,
                            Spacing = new Vector2(0, 2),
                            Children = new Drawable[]
                            {
                                scoreText = new RetroText
                                {
                                    Anchor = Anchor.TopRight,
                                    Origin = Anchor.TopRight,
                                    Font = RetroFontFamily.Display,
                                    TextSize = 20,
                                    Colour = Color4.White,
                                    Text = "0",
                                },
                                accuracyText = new RetroText
                                {
                                    Anchor = Anchor.TopRight,
                                    Origin = Anchor.TopRight,
                                    Font = RetroFontFamily.Body,
                                    TextSize = 15,
                                    Colour = new Color4(0.8f, 0.8f, 0.88f, 1f),
                                    Text = "100.00%",
                                },
                            },
                        },
                        new RetroText
                        {
                            Anchor = Anchor.TopLeft,
                            Origin = Anchor.TopLeft,
                            // Sits below the health bar rather than across it.
                            Margin = new MarginPadding { Top = HealthBar.BarHeight + hud_spacing },
                            Font = RetroFontFamily.Body,
                            TextSize = 15,
                            Colour = new Color4(0.7f, 0.7f, 0.8f, 1f),
                            Text = $"{Beatmap.Metadata.Artist} - {Beatmap.Metadata.Title} [{Beatmap.Metadata.Version}]",
                        },
                        stateText = new RetroText
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Font = RetroFontFamily.Display,
                            TextSize = 24,
                            Colour = Color4.White,
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
                    onQuit: exitToSongSelect),
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            loadTrack();

            if (hitObjects.Count == 0)
                completeMap();
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
        }

        protected override void Update()
        {
            base.Update();

            updatePlayfieldScale();

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

            float availableHeight = Math.Max(100f, DrawSize.Y - vertical_margin);
            float scale = Math.Min(DrawSize.X / 512f, availableHeight / 384f);

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
            showBonusPopup(hitObject.Data.Position);
        }

        private void updateHud()
        {
            comboCounter.SetCombo(scoreProcessor.Combo);
            scoreText.Text = $"{scoreProcessor.Score:N0}";
            accuracyText.Text = $"{scoreProcessor.Accuracy:0.00}%";
        }

        private void showJudgementPopup(Vector2 position, HitResult result)
        {
            string text = result switch
            {
                HitResult.Great => "300",
                HitResult.Ok => "100",
                HitResult.Meh => "50",
                _ => "Miss",
            };

            Color4 colour = result switch
            {
                HitResult.Great => new Color4(0.4f, 0.8f, 1f, 1f),
                HitResult.Ok => new Color4(0.4f, 0.9f, 0.4f, 1f),
                HitResult.Meh => new Color4(0.95f, 0.8f, 0.3f, 1f),
                _ => new Color4(1f, 0.3f, 0.3f, 1f),
            };

            showPopup(position, text, colour);
        }

        /// <summary>The gold "+100" that flies off a spinner for each full extra rotation past its requirement.</summary>
        private void showBonusPopup(Vector2 position) =>
            showPopup(position, "+100", new Color4(1f, 0.85f, 0.3f, 1f));

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

            stateText.Text = "Cleared!";
            stateText.FadeIn(300, Easing.OutQuint);

            queueResults();
        }

        private void failMap()
        {
            completed = true;
            failed = true;

            track?.Stop();

            stateText.Text = "Failed";
            stateText.Colour = new Color4(1f, 0.4f, 0.4f, 1f);
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
            this.FadeOut(200, Easing.OutQuint);

            return base.OnExiting(e);
        }
    }
}
