using System;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Input.Events;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Framework.Extensions.Color4Extensions;
using OsuClient.Game.Audio;
using OsuClient.Game.Graphics;
using OsuClient.Game.Screens.Gameplay;
using OsuClient.Game.Screens.Generation;
using OsuClient.Game.Screens.SongSelect;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.MainMenu
{
    /// <summary>
    /// The client's entry screen: a random song from the library playing over
    /// a random wallpaper, with the RIMO logo pulsing on its beat
    /// (MENU_REDESIGN_PLAN.md).
    ///
    /// Clicking the logo opens the navigation strip — see
    /// <see cref="MenuStrip"/> and <see cref="setExpanded"/>.
    /// </summary>
    public partial class MainMenuScreen : Screen
    {
        /// <summary>
        /// The logo's square, as a fraction of the window's smaller dimension.
        /// Everything on this screen is sized from that one number, so the
        /// composition holds from a small window to a maximised one.
        /// </summary>
        private const float logo_area_fraction = 0.61f;

        /// <summary>The circle itself, within that square — the rest is the spectrum ring's.</summary>
        private const float logo_fraction = 0.72f;

        /// <summary>Gutter around the screen's corners, matching song select's.</summary>
        private const float margin = 52;

        /// <summary>
        /// How sharply the logo's pulse narrows around each beat. Gameplay's
        /// ambient effects use the same range; lower would have the logo
        /// breathing continuously rather than reacting on the beat.
        /// </summary>
        private const double beat_sharpness = 6;

        /// <summary>
        /// How much of the pulse's strength is handed to the song's current
        /// loudness rather than being fixed.
        ///
        /// It modulates the beat rather than adding to it: taking the louder
        /// of the two instead would leave the logo permanently inflated
        /// through any sustained bass line, since that never drops between
        /// beats the way the timing-point pulse does.
        /// </summary>
        private const double loudness_weight = 0.3;

        /// <summary>
        /// The background wash's saturation and brightness. Darker than the
        /// circle's 0.96 by a wide margin — same colour, well behind it.
        /// </summary>
        private const float background_saturation = 0.72f;

        private const float background_value = 0.42f;

        /// <summary>How far in from each edge the beat flash reaches, as a fraction of the window.</summary>
        private const float border_flash_reach = 0.16f;

        /// <summary>Brightness of that flash at its peak. Ambient, not a light show.</summary>
        private const double border_flash_peak = 0.4;

        /// <summary>
        /// How far the logo shrinks to make room for the strip.
        ///
        /// Chosen with <see cref="logo_area_fraction"/> so that the open
        /// state lands on the size the closed state used to be: the circle
        /// is now bigger at rest and shrinks to roughly its old resting size
        /// rather than to something smaller still.
        /// </summary>
        private const float expanded_logo_scale = 0.75f;

        /// <summary>
        /// The open strip's width, as a multiple of the logo square. Cut
        /// from 2.45 when the square grew, so the strip stays the same width
        /// on screen as it was before the circle was enlarged.
        /// </summary>
        private const float strip_width_factor = 2.05f;

        /// <summary>The strip's height, in the same units.</summary>
        private const float strip_height_factor = 0.174f;

        /// <summary>
        /// Clear space kept between the two buttons, as a multiple of the
        /// logo square.
        ///
        /// Sized against the spectrum ring rather than the circle: the ring
        /// reaches about 0.53 of the square from the centre when the music is
        /// loud, so a gap merely wide enough for the circle lets a loud
        /// passage throw blocks across the buttons.
        /// </summary>
        private const float strip_gap_factor = 1.14f;

        private const double expand_duration = 460;

        /// <summary>
        /// How long the colour takes to reach the corners. Longer than the
        /// strip's own animation on purpose — the spread should still be
        /// travelling once the buttons have arrived, which is what makes it
        /// read as the click causing it rather than accompanying it.
        /// </summary>
        private const double spread_duration = 1150;

        /// <summary>How long the music takes to swell once the menu opens.</summary>
        private const double volume_ramp = 900;

        private readonly string? songsDirectory;

        [Resolved]
        private GameHost host { get; set; } = null!;

        [Resolved]
        private IRenderer renderer { get; set; } = null!;

        /// <summary>The game's shared music. Null outside the game, where the menu makes its own.</summary>
        [Resolved(CanBeNull = true)]
        private MenuTrack? sharedMusic { get; set; }

        /// <summary>The song the credit and background were last set for, to catch up after song select.</summary>
        private string? shownSongPath;

        private MenuTrack menuTrack = null!;
        private MenuBackground background = null!;
        private Container logoArea = null!;
        private Container logoEntry = null!;
        private MenuLogo logo = null!;
        private SpectrumRing spectrum = null!;
        private NowPlayingDisplay nowPlaying = null!;
        private MenuStrip strip = null!;
        private RetroText closedHint = null!;
        private RetroText openHint = null!;
        private EdgeGlow borderFlash = null!;
        private MenuSoundPlayer sounds = null!;

        /// <summary>The game's screen-change ripple. Null when the menu runs outside the game, as in tests.</summary>
        [Resolved(CanBeNull = true)]
        private ScreenRipple? ripple { get; set; }

        /// <summary>
        /// How long the rings get on screen before the next screen may arrive,
        /// and how long the menu takes to reach its half-faded hold.
        /// </summary>
        private const double transition_lead = 380;

        /// <summary>How faded the menu is while the next screen is still loading.</summary>
        private const float leaving_hold_alpha = 0.4f;

        /// <summary>How far off its edge the arriving screen starts, as a fraction of the width.</summary>
        private const float arriving_offset = 0.22f;

        private const double arrive_duration = 650;

        /// <summary>
        /// How long, once suspended, the menu holds half-faded before fading
        /// out on its own. The arriving screen normally starts the fade sooner
        /// itself; this is what keeps the menu on screen until it does.
        /// </summary>
        private const double suspended_hold = 400;

        /// <summary>Whether the menu is leaving through a ripple, from the press until it is back.</summary>
        private bool leavingThroughRipple;

        /// <summary>
        /// Frames the arriving screen draws before its slide starts: the
        /// first is where it does its heavy setup, so the slide begins on the
        /// one after.
        /// </summary>
        private const int settle_frames = 2;

        /// <summary>
        /// How far the screen has turned from monochrome to colour, 0 to 1.
        ///
        /// Animated alongside the background's colour spread and used for
        /// everything outside that spread's own mask — currently the edge
        /// flash, which is white while the screen is still black and white
        /// and only takes the logo's hue once the colour has arrived.
        ///
        /// A bindable rather than a plain property because the transform
        /// overload that resolves a property by name fails silently; see
        /// <see cref="MenuStrip"/>.
        /// </summary>
        private readonly BindableFloat colourAmount = new BindableFloat();

        private string? wallpaper;

        /// <summary>
        /// Whether the navigation strip is open. The menu has exactly these
        /// two states, and every transition goes through
        /// <see cref="setExpanded"/> — transforms started from event handlers
        /// dotted around the class is how these get stuck half-open.
        /// </summary>
        private bool expanded;

        public MainMenuScreen(string? songsDirectory = null)
        {
            this.songsDirectory = songsDirectory;
        }

        [BackgroundDependencyLoader]
        private void load(AudioManager audio)
        {
            sounds = new MenuSoundPlayer(audio);

            // Both of these touch the filesystem, and both run here on the
            // screen's background load thread rather than on first frame.
            wallpaper = WallpaperLibrary.PickRandom(WallpaperLibrary.Load(WallpaperLibrary.ResolveDefaultDirectory()));

            menuTrack = sharedMusic ?? new MenuTrack(songsDirectory);

            InternalChildren = new Drawable[]
            {
                background = new MenuBackground(),
                // Between the background and the logo: the strip has to be
                // covered by the circle it grows out from, and an equal-depth
                // child added earlier draws underneath. (Pushing it back with
                // Depth instead puts it behind the background, where it is
                // invisible — which is exactly what happened first.)
                strip = new MenuStrip(pushUpload, pushSongSelect),
                logoArea = new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    // A second container purely so the entry animation and
                    // the open/close transition have a Scale each. Sharing
                    // one meant whichever ran second won: entering the screen
                    // already open left the logo at full size, because
                    // OnEntering's scale-up landed after Expand()'s
                    // scale-down and simply replaced it.
                    Child = logoEntry = new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Children = new Drawable[]
                        {
                            spectrum = new SpectrumRing
                            {
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                RelativeSizeAxes = Axes.Both,
                            },
                            logo = new MenuLogo
                            {
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                RelativeSizeAxes = Axes.Both,
                                Size = new Vector2(logo_fraction),
                                Clicked = toggleExpanded,
                            },
                        },
                    },
                },
                // The same edge wash gameplay pulses on the beat, so the two
                // screens feel like the same game breathing.
                borderFlash = new EdgeGlow(0),
                nowPlaying = new NowPlayingDisplay
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    Margin = new MarginPadding(margin),
                },
                // Two prepared lines swapped by alpha rather than one whose
                // text changes: RetroText rasterizes a new texture on every
                // assignment, and this changes on every click.
                new Container
                {
                    Anchor = Anchor.BottomCentre,
                    Origin = Anchor.BottomCentre,
                    AutoSizeAxes = Axes.Both,
                    Margin = new MarginPadding(margin),
                    Children = new Drawable[]
                    {
                        closedHint = new RetroText
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Font = RetroFontFamily.Display,
                            TextSize = 9,
                            Text = "CLICK THE RECORD   ·   ESC TO QUIT",
                            Colour = RetroPalette.TextDim,
                        },
                        openHint = new RetroText
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Font = RetroFontFamily.Display,
                            TextSize = 9,
                            Text = "ENTER TO PLAY   ·   ESC TO GO BACK",
                            Colour = RetroPalette.TextDim,
                            Alpha = 0,
                        },
                    },
                },
            };

            // Outside the game there is no shared music, and this one is the
            // menu's own; it has to be in the tree to load and play.
            if (sharedMusic == null)
                AddInternal(menuTrack);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // Wallpaper first, the playing song's own cover art second, and
            // the bare gradient when there's neither — data/ is gitignored, so
            // a fresh clone has no wallpapers at all and still has to look
            // deliberate.
            background.SetBackground(wallpaper ?? menuTrack.BackgroundPath);

            nowPlaying.SetSong(menuTrack.Title, menuTrack.Artist);
            shownSongPath = menuTrack.Entry?.Path;

            menuTrack.SongChanged += onSongChanged;

            spectrum.SetTrack(menuTrack);

            // Whatever state was asked for before the screen finished
            // loading, without animating into it.
            applyExpansion(0);

            // Song select's first-visit work — covers, the text rasterizer's
            // first run — done now, in the background, so the first PLAY is
            // as quick as every later one. A second in, to leave the menu's
            // own opening alone.
            Scheduler.AddDelayed(() => SongSelectWarmup.RunOnce(renderer, songsDirectory), 1000);
        }

        protected override void Update()
        {
            base.Update();

            float side = MathF.Min(DrawWidth, DrawHeight) * logo_area_fraction;

            logoArea.Size = new Vector2(side);

            strip.Height = side * strip_height_factor;

            // Clamped to the window: the strip is sized from the *smaller*
            // dimension, so on a window narrower than it is tall it would
            // otherwise run off both edges and take its buttons with it.
            strip.TargetWidth = MathF.Min(side * strip_width_factor, DrawWidth - margin * 2);
            strip.CentreGap = side * strip_gap_factor;

            double beat = currentBeatIntensity();

            logo.SetBeat(beat);
            updateColour();
            // Reach scales with the window rather than being a fixed pixel
            // count: gameplay matches its flash to the key overlay's bars,
            // but the menu has no furniture at the edges to line up with.
            borderFlash.Reach = DrawWidth * border_flash_reach;
            borderFlash.Intensity = (float)(beat * border_flash_peak);
        }

        /// <summary>
        /// Dresses the background and the border flash in the colour the
        /// logo is currently wearing, so the whole screen drifts through the
        /// spectrum as one thing rather than a coloured circle on a fixed
        /// backdrop.
        ///
        /// The background takes the same two hues at a much lower value: a
        /// wash as bright as the circle competes with it, and the circle is
        /// meant to be the brightest thing on screen.
        /// </summary>
        private void updateColour()
        {
            float hue = logo.Hue;
            float second = (hue + MenuLogo.HueSpread) % 360;

            background.SetTint(
                Color4Extensions.FromHSV(hue, background_saturation, background_value),
                Color4Extensions.FromHSV(second, background_saturation, background_value));

            // The edge flash is white until the colour spreads, so the
            // opening screen stays monochrome apart from the logo itself,
            // then takes the hue on its way out with everything else.
            borderFlash.GlowColour = towardsWhite(
                Color4Extensions.FromHSV(hue, 0.55f, 1f),
                colourAmount.Value);
        }

        /// <summary>
        /// <paramref name="colour"/> at <paramref name="amount"/> 1, white at
        /// 0, mixed in between.
        /// </summary>
        private static Color4 towardsWhite(Color4 colour, float amount) =>
            new Color4(
                1 + (colour.R - 1) * amount,
                1 + (colour.G - 1) * amount,
                1 + (colour.B - 1) * amount,
                1);

        /// <summary>
        /// How strongly the logo should be pulsing right now: 1 on a beat of
        /// the playing song, falling to 0 between beats.
        ///
        /// 0 whenever there's nothing to pulse to — no song, or a song whose
        /// beatmap carries no timing point, in which case
        /// <see cref="BeatPulse.PhaseAt"/> hands back NaN and
        /// <see cref="BeatPulse.IntensityAt"/> turns that into stillness.
        /// </summary>
        private double currentBeatIntensity()
        {
            if (menuTrack.Beatmap == null || !menuTrack.IsPlaying)
                return 0;

            double phase = BeatPulse.PhaseAt(menuTrack.Beatmap, menuTrack.CurrentTime);
            double beat = BeatPulse.IntensityAt(phase, beat_sharpness);

            // The beatmap's timing points say *when*; the song's own low end
            // says *how hard*. A quiet intro breathes, a chorus hits.
            return beat * (1 - loudness_weight + loudness_weight * spectrum.LowEnergy);
        }

        /// <summary>
        /// Opens the navigation strip from outside the screen. Exists for
        /// the test scenes, which have no way to click: a still frame of the
        /// open state is the only way to check the strip's geometry.
        /// </summary>
        public void Expand() => setExpanded(true);

        /// <summary>Closes the navigation strip. Counterpart to <see cref="Expand"/>.</summary>
        public void Collapse() => setExpanded(false);

        /// <summary>Does what the PLAY button does. For test scenes, like <see cref="Expand"/>.</summary>
        public void Play() => pushSongSelect();

        /// <summary>Does what the CREATE button does. For test scenes, like <see cref="Expand"/>.</summary>
        public void Create() => pushUpload();

        private void toggleExpanded()
        {
            // Only on the way open. Closing is a retreat, and a whoosh on the
            // way back makes it sound like something else is arriving.
            if (!expanded)
                sounds.PlayWhoosh();

            setExpanded(!expanded);
        }

        /// <summary>
        /// The logo circle's current radius on screen, transitions included —
        /// where the colour spread starts from.
        /// </summary>
        private float logoRadius() =>
            MathF.Min(DrawWidth, DrawHeight) * logo_area_fraction * logo_fraction / 2
            * (expanded ? expanded_logo_scale : 1f);

        /// <summary>
        /// Opens or closes the navigation strip: the logo shrinks out of the
        /// way and the strip grows from behind it, or the reverse.
        ///
        /// Transforms are finished before new ones start, so clicking the
        /// logo repeatedly mid-animation can't leave the two halves of the
        /// transition disagreeing about where they are.
        /// </summary>
        private void setExpanded(bool value)
        {
            expanded = value;

            // Callable before the screen has loaded — a test scene pushes
            // this screen and opens it on the next step, which can land
            // before the background load thread has built any of these. The
            // state is recorded either way and applied in LoadComplete.
            if (!IsLoaded)
                return;

            applyExpansion(expand_duration);
        }

        private void applyExpansion(double duration)
        {
            logoArea.FinishTransforms();
            strip.FinishTransforms();

            // Colour spreads out from the circle's own edge, so it looks like
            // it came out of the logo rather than from a point behind it.
            background.SetColourSpread(expanded, logoRadius(), duration > 0 ? spread_duration : 0);

            menuTrack.FadeVolumeTo(
                expanded ? MenuTrack.ActiveVolume : MenuTrack.IdleVolume,
                duration > 0 ? volume_ramp : 0);

            // Tracks the spread rather than the strip, so the flash finishes
            // turning colour at the same moment the picture does.
            this.TransformBindableTo(
                colourAmount,
                expanded ? 1f : 0f,
                duration > 0 ? spread_duration : 0,
                Easing.OutQuint);

            logoArea.ScaleTo(expanded ? expanded_logo_scale : 1f, duration, Easing.OutQuint);
            strip.AnimateTo(expanded, duration);

            closedHint.FadeTo(expanded ? 0 : 1, duration / 2, Easing.OutQuint);
            openHint.FadeTo(expanded ? 1 : 0, duration / 2, Easing.OutQuint);
        }

        private void pushSongSelect()
        {
            if (!this.IsCurrentScreen())
                return;

            // Song select opens on whatever the menu was playing, so the
            // music carries across instead of cutting to an unrelated track
            // the moment PLAY is pressed. Null (an empty library) leaves song
            // select to pick for itself, as it did before.
            string? current = menuTrack.Entry?.Path;

            // PLAY sits on the right of the strip and the songs are that way:
            // the ripple comes in from the right.
            leaveThrough(RippleEdge.Right, () => new RetroSongSelectScreen(songsDirectory, current));
        }

        private void pushUpload()
        {
            if (!this.IsCurrentScreen())
                return;

            // CREATE is PLAY's mirror, so its ripple comes from the left.
            // The menu's song goes with it, to play on under the deck.
            string? audioPath = menuTrack.Entry?.Set?.AudioPath;
            double time = menuTrack.CurrentTime;

            leaveThrough(RippleEdge.Left, () => new UploadScreen(songsDirectory, audioPath, time));
        }

        /// <summary>
        /// Leaves for another screen through a ripple from
        /// <paramref name="edge"/>, with the transition whoosh travelling the
        /// same way.
        ///
        /// There is no curtain. The menu starts fading the moment the button
        /// is pressed — in place: drifting it sideways uncovered a hard black
        /// strip along the window edge it moved away from. The next screen loads
        /// meanwhile, and once it has — and the rings have had a moment on
        /// screen — it is pushed, sliding in from that edge as it fades up
        /// while the menu finishes fading out beneath it. Loading starts at
        /// the press rather than after, because song select reads the whole
        /// library before it can show and that is time nobody should wait on.
        /// </summary>
        private void leaveThrough(RippleEdge edge, Func<Screen> next)
        {
            if (ripple == null)
            {
                this.Push(next());
                return;
            }

            // Checked before building anything, so a second press during a
            // ripple does not load a screen that will never be shown.
            if (!ripple.Begin(edge))
                return;

            sounds.PlayTransition(fromRight: edge == RippleEdge.Right);

            leavingThroughRipple = true;
            double pressedAt = Time.Current;

            // Partway out while the next screen loads: still there, clearly
            // on its way. The rest happens in OnSuspending, once there is
            // something arriving to replace it.
            this.FadeTo(leaving_hold_alpha, transition_lead, Easing.OutQuad);

            var screen = next();

            LoadComponentAsync(screen, _ =>
            {
                double wait = Math.Max(0, transition_lead - (Time.Current - pressedAt));

                Scheduler.AddDelayed(() => arrive(screen, edge), wait);
            });
        }

        private void arrive(Screen screen, RippleEdge edge)
        {
            if (!this.IsCurrentScreen())
            {
                ripple?.End();
                return;
            }

            float start = -awayFrom(edge) * DrawWidth * arriving_offset;

            screen.X = start;
            this.Push(screen);

            // Held at its starting point — off the ripple's edge, all but
            // invisible — until it has drawn a couple of ordinary frames.
            // Song select does heavy work on its first frame (the wheel, the
            // preview track); a slide started at the push was mostly gone by
            // the time that frame ended, and it simply appeared in place.
            int frames = 0;

            void beginWhenSettled(Drawable arriving)
            {
                frames++;

                if (frames == 1)
                {
                    // Here rather than straight after the push: the push
                    // completes, and the screen's own OnEntering starts its
                    // fade, a moment later — which then ran over the hold.
                    // Not quite zero: a drawable at Alpha 0 is not present,
                    // stops updating, and would never get to its slide.
                    arriving.ClearTransforms();
                    arriving.X = start;
                    arriving.Alpha = 0.01f;
                    return;
                }

                // Waiting only for the one long frame, not for the frame
                // rate to recover: song select runs a little slow for a while
                // after it arrives, and holding for a fast frame left the
                // menu fading out over nothing.
                if (frames < settle_frames)
                    return;

                arriving.OnUpdate -= beginWhenSettled;

                // In from the ripple's edge — the right for PLAY, the left
                // for CREATE — while the menu finishes fading beneath it.
                arriving.MoveToX(0, arrive_duration, Easing.OutQuint);
                arriving.FadeIn(arrive_duration * 0.8, Easing.OutQuad);

                // Replaces the fallback fade queued in OnSuspending, so the
                // menu goes as the new screen comes rather than on a timer.
                this.ClearTransforms(false, nameof(Alpha));
                this.FadeOut(arrive_duration * 0.7, Easing.OutQuad);

                ripple?.End();
            }

            screen.OnUpdate += beginWhenSettled;
        }

        /// <summary>-1 to move left, away from a ripple starting on the right; +1 the other way.</summary>
        private static float awayFrom(RippleEdge edge) => edge == RippleEdge.Right ? -1 : 1;

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            switch (e.Key)
            {
                case osuTK.Input.Key.Enter:
                case osuTK.Input.Key.KeypadEnter:
                    // Enter has always started a game from this screen; from
                    // the closed state it opens the strip first, so the
                    // keyboard and the mouse arrive at the same place.
                    if (expanded)
                        pushSongSelect();
                    else
                        setExpanded(true);

                    return true;

                case osuTK.Input.Key.Space:
                    setExpanded(!expanded);
                    return true;

                case osuTK.Input.Key.Escape:
                    // Closed, there is nothing left to back out of — the old
                    // menu had an Exit button and this design has no third
                    // slot for one.
                    if (expanded)
                        setExpanded(false);
                    else
                        host.Exit();

                    return true;
            }

            return base.OnKeyDown(e);
        }

        public override void OnEntering(ScreenTransitionEvent e)
        {
            base.OnEntering(e);

            this.FadeInFromZero(300, Easing.OutQuint);

            // The logo arrives a moment after the artwork does, and the
            // credit (delayed inside NowPlayingDisplay) after that. All three
            // at once reads as a flash rather than as the screen opening.
            logoEntry.ScaleTo(0.88f).Delay(120).ScaleTo(1f, 760, Easing.OutQuint);

            menuTrack.AutoAdvance = true;
            menuTrack.Start();
        }

        /// <summary>
        /// A new song: the credit, and the background when there is no
        /// wallpaper, follow it. The beat pulse and the spectrum read the
        /// track live, so they follow on their own.
        /// </summary>
        private void onSongChanged()
        {
            if (menuTrack.Entry?.Path == shownSongPath)
                return;

            shownSongPath = menuTrack.Entry?.Path;

            nowPlaying.ChangeSong(menuTrack.Title, menuTrack.Artist);

            if (wallpaper == null)
                background.SetBackground(menuTrack.BackgroundPath);
        }

        public override void OnResuming(ScreenTransitionEvent e)
        {
            base.OnResuming(e);

            this.FadeIn(250, Easing.OutQuint);
            leavingThroughRipple = false;


            // Coming back should look like arriving at the menu, not like
            // resuming a menu left half-open behind another screen.
            setExpanded(false);

            // Back from song select, whatever it was playing carries on, and
            // the credit catches up with it. Back from the tape deck, the
            // song it paused picks up where it stopped. Either way, the menu
            // plays through the library again from here.
            menuTrack.AutoAdvance = true;
            menuTrack.Start();
            onSongChanged();

            // Maps generated since the game started join the shuffle.
            menuTrack.RefreshLibrary();
        }

        public override void OnSuspending(ScreenTransitionEvent e)
        {
            base.OnSuspending(e);

            // The stack keeps a suspended screen drawn only until the
            // transforms queued right now have finished, so the fade-out has
            // to be queued here. Holding first leaves room for the arriving
            // screen to start it sooner, in step with its slide; this chain
            // is the fallback, and the menu's lifetime on screen.
            if (leavingThroughRipple)
            {
                this.FadeTo(leaving_hold_alpha, suspended_hold)
                    .Then().FadeOut(arrive_duration * 0.7, Easing.OutQuad);
            }

            // Song select carries on with the same song, so it keeps playing
            // through the change. The tape deck plays its own remixed copy,
            // so there the original stops.
            if (e.Next is not RetroSongSelectScreen || sharedMusic == null)
                menuTrack.Stop();
        }

        protected override void Dispose(bool isDisposing)
        {
            // The shared music outlives this screen; a handler left on it
            // would call into a disposed menu.
            if (menuTrack != null)
                menuTrack.SongChanged -= onSongChanged;

            base.Dispose(isDisposing);
        }

        public override bool OnExiting(ScreenExitEvent e)
        {
            menuTrack.Stop();

            return base.OnExiting(e);
        }
    }
}
