using System;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osu.Framework.Screens;
using OsuClient.Game.Audio;
using OsuClient.Game.Beatmaps;
using OsuClient.Game.Graphics;
using OsuClient.Game.Scores;
using OsuClient.Game.Screens.Gameplay;
using OsuClient.Game.Screens.MainMenu;
using OsuClient.Game.Screens.Results;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.SongSelect
{
    /// <summary>
    /// The moment between choosing a song and playing it: the record lifted
    /// off the wheel and set on its own, waiting to be played.
    ///
    /// <para>
    /// It opens where song select's vinyl is — the same place, the same size —
    /// so the wheel seems to shrink out of its deck and glide into the middle
    /// of the screen, where it lands as the menu's record with the song's own
    /// cover on its label and the spectrum ring round it. The music, muffled
    /// when the song was chosen, carries on under it, and the song — its
    /// numbers and the best run on it — is set out above it on a card lit like
    /// the info panel. Escape sends the record back the way it came, onto the
    /// wheel, as song select comes back up around it.
    /// </para>
    ///
    /// <para>
    /// Clicking the record plays it the way the menu opens: one ring out of
    /// the record, and behind it the blurred cover coming into focus, on the
    /// menu's own curve. Gameplay then fades up over the picture now clear —
    /// which is the picture gameplay plays in front of.
    /// </para>
    /// </summary>
    public partial class PlayCueScreen : Screen
    {
        /// <summary>
        /// The ring-and-record square, as a fraction of the window's smaller
        /// side: a little under the menu's 0.61, to leave the card above it
        /// room on a short window.
        /// </summary>
        private const float deck_fraction = 0.56f;

        /// <summary>The record within that square — the menu's.</summary>
        private const float record_fraction = 0.72f;

        /// <summary>Where the record's centre lands, as a fraction of the height: low enough for the card above.</summary>
        private const float resting_y = 0.6f;

        /// <summary>
        /// Long enough to follow, eased in and out so it lifts off gently,
        /// travels, and settles — rather than jumping most of the way at once
        /// and creeping the rest. The way back takes the same curve.
        /// </summary>
        private const double glide_duration = 1100;

        /// <summary>The menu's own reveal, so playing a record feels like opening the menu.</summary>
        private const double reveal_duration = 1150;

        /// <summary>
        /// How far into the reveal gameplay may arrive. OutQuint has covered
        /// almost everything by here; the rest is the edge running off the
        /// corners, which gameplay's own fade-in overlaps.
        /// </summary>
        private const double launch_lead = 650;

        private readonly BeatmapSelection selection;
        private readonly Color4 accent;
        private readonly Quad startQuad;

        [Resolved(CanBeNull = true)]
        private MenuTrack? music { get; set; }

        [Resolved(CanBeNull = true)]
        private HighScoreStore? highScores { get; set; }

        private SongSelectSoundPlayer sounds = null!;

        private Container backdrop = null!;
        private CircularContainer revealMask = null!;
        private Container revealContent = null!;
        private Container deck = null!;
        private SpectrumRing spectrum = null!;
        private MenuLogo record = null!;
        private Container card = null!;
        private RetroText hint = null!;

        private bool gliding;
        private bool launching;
        private bool played;

        /// <param name="selection">The song and difficulty to play.</param>
        /// <param name="accent">The difficulty's cassette colour, for the card.</param>
        /// <param name="startQuad">
        /// Where song select's vinyl is on screen, so the record starts exactly
        /// over it — and goes back to exactly there on Escape.
        /// </param>
        public PlayCueScreen(BeatmapSelection selection, Color4 accent, Quad startQuad)
        {
            this.selection = selection;
            this.accent = accent;
            this.startQuad = startQuad;
        }

        /// <summary>Whether the record has landed. Exposed for tests.</summary>
        public bool Landed => IsLoaded && !gliding;

        /// <summary>The record, for tests to click.</summary>
        public MenuLogo Record => record;

        /// <summary>Whether the clear picture has started coming through. Exposed for tests.</summary>
        public bool Revealing => revealMask.Size.X > 0;

        [BackgroundDependencyLoader]
        private void load(AudioManager audio)
        {
            sounds = new SongSelectSoundPlayer(audio);

            string? cover = selection.Entry.BackgroundPath;

            InternalChildren = new Drawable[]
            {
                backdrop = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Alpha = 0,
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = RetroPalette.Void,
                        },
                        new BufferedContainer(cachedFrameBuffer: true)
                        {
                            RelativeSizeAxes = Axes.Both,
                            BlurSigma = new Vector2(14),
                            Child = new CoverSprite(cover),
                        },
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = new Color4(0f, 0f, 0f, 0.6f),
                        },
                    },
                },
                // The same picture, sharp, behind a circle that grows out of
                // the record when it is played. Held still while the circle
                // grows round it — the menu's trick — so the focus is seen
                // spreading, not the picture zooming out of a hole.
                revealMask = new CircularContainer
                {
                    Origin = Anchor.Centre,
                    Masking = true,
                    Child = revealContent = new Container
                    {
                        Children = new Drawable[]
                        {
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = RetroPalette.Void,
                            },
                            new CoverSprite(cover),
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = new Color4(0f, 0f, 0f, 0.35f),
                            },
                        },
                    },
                },
                deck = new Container
                {
                    Origin = Anchor.Centre,
                    Children = new Drawable[]
                    {
                        spectrum = new SpectrumRing
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            RelativeSizeAxes = Axes.Both,
                            Alpha = 0,
                        },
                        record = new MenuLogo(labelArt: cover)
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            RelativeSizeAxes = Axes.Both,
                            Size = new Vector2(record_fraction),
                            Clicked = Launch,
                        },
                    },
                },
                card = createCard(selection.Beatmap),
                hint = new RetroText
                {
                    Anchor = Anchor.BottomCentre,
                    Origin = Anchor.BottomCentre,
                    Y = -36,
                    Font = RetroFontFamily.Display,
                    TextSize = 11,
                    Colour = RetroPalette.TextDim,
                    Text = "CLICK THE RECORD TO PLAY   ·   ESC — BACK",
                    Alpha = 0,
                },
            };
        }

        /// <summary>
        /// The song, set out above the record on a card lit the way the info
        /// panel is: what it is, what the difficulty asks of you, and the best
        /// anyone has done on it here.
        /// </summary>
        private Container createCard(Beatmap beatmap)
        {
            var length = TimeSpan.FromMilliseconds(Math.Max(0, beatmap.LastHitObjectTime));
            var (circles, sliders, spinners) = BeatmapStatistics.CountObjects(beatmap);

            return new Container
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                RelativePositionAxes = Axes.Y,
                Y = 0.05f,
                AutoSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 12,
                BorderThickness = 2,
                BorderColour = RetroPalette.Cyan.Opacity(0.6f),
                EdgeEffect = new EdgeEffectParameters
                {
                    Type = EdgeEffectType.Glow,
                    Colour = RetroPalette.Cyan.Opacity(0.5f),
                    Radius = 20,
                    Hollow = true,
                },
                Alpha = 0,
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = RetroPalette.Panel,
                    },
                    new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 7),
                        Padding = new MarginPadding { Horizontal = 40, Vertical = 16 },
                        Children = new Drawable[]
                        {
                            centred(new RetroText
                            {
                                Font = RetroFontFamily.Body,
                                TextSize = 28,
                                Colour = RetroPalette.Text,
                                Text = string.IsNullOrWhiteSpace(beatmap.Metadata.Title) ? "(untitled)" : beatmap.Metadata.Title,
                            }),
                            centred(new RetroText
                            {
                                Font = RetroFontFamily.Body,
                                TextSize = 15,
                                Colour = RetroPalette.TextDim,
                                Text = beatmap.Metadata.Artist,
                            }),
                            centred(new RetroText
                            {
                                Font = RetroFontFamily.Display,
                                TextSize = 12,
                                Colour = accent,
                                Text = $"{beatmap.Metadata.Version.ToUpperInvariant()}   ·   {beatmap.BPM:0} BPM   ·   "
                                       + $"{(int)length.TotalMinutes}:{length.Seconds:00}",
                            }),
                            centred(new FillFlowContainer
                            {
                                AutoSizeAxes = Axes.Both,
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(22, 0),
                                Margin = new MarginPadding { Top = 6 },
                                Children = new Drawable[]
                                {
                                    stat("CIRCLES", circles.ToString(), RetroPalette.Cyan),
                                    stat("SLIDERS", sliders.ToString(), RetroPalette.Mint),
                                    stat("SPINNERS", spinners.ToString(), RetroPalette.Amber),
                                    stat("CS", $"{beatmap.Difficulty.CircleSize:0.#}", RetroPalette.Magenta),
                                    stat("AR", $"{beatmap.Difficulty.ApproachRate:0.#}", RetroPalette.Magenta),
                                    stat("OD", $"{beatmap.Difficulty.OverallDifficulty:0.#}", RetroPalette.Violet),
                                },
                            }),
                            centred(highScoreLine(beatmap)),
                        },
                    },
                },
            };
        }

        /// <summary>The best run on this difficulty, or that there has not been one — in amber, as on song select.</summary>
        private Drawable highScoreLine(Beatmap beatmap)
        {
            var best = highScores?.Get(beatmap.ContentHash);

            var line = new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(12, 0),
                Margin = new MarginPadding { Top = 4 },
                Children = new Drawable[]
                {
                    new RetroText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Font = RetroFontFamily.Body,
                        TextSize = 11,
                        Colour = RetroPalette.Amber.Opacity(0.8f),
                        Text = "HIGH SCORE",
                    },
                    new RetroText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Font = RetroFontFamily.Display,
                        TextSize = 15,
                        Colour = RetroPalette.Amber,
                        Text = best == null ? "--" : best.Score.ToString("N0"),
                    },
                    new RetroText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Font = RetroFontFamily.Body,
                        TextSize = 11,
                        Colour = best == null ? RetroPalette.TextDim : ResultsScreen.ColourForGrade(best.Grade),
                        Text = best == null ? "NOT CLEARED YET" : $"{best.Grade} · {best.Accuracy:0.00}% · {best.MaxCombo}x",
                    },
                },
            };

            return line;
        }

        private static Drawable stat(string label, string value, Color4 colour) => new FillFlowContainer
        {
            AutoSizeAxes = Axes.Both,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, 4),
            Children = new Drawable[]
            {
                new RetroText
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Font = RetroFontFamily.Body,
                    TextSize = 9,
                    Colour = colour.Opacity(0.85f),
                    Text = label,
                },
                new RetroText
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Font = RetroFontFamily.Display,
                    TextSize = 13,
                    Colour = RetroPalette.Text,
                    Text = value,
                },
            },
        };

        private static Drawable centred(Drawable drawable)
        {
            drawable.Anchor = Anchor.TopCentre;
            drawable.Origin = Anchor.TopCentre;
            return drawable;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            spectrum.SetTrack(music);

            // Before the first frame, so there is never a frame of the record
            // anywhere but over the wheel it is coming off.
            deck.Position = startCentre;
            deck.Size = new Vector2(startSide);

            gliding = true;

            Schedule(() =>
            {
                // The glide: one smooth ease in and out, position and size
                // together, so it reads as a single movement.
                deck.MoveTo(restingPosition, glide_duration, Easing.InOutCubic)
                    .ResizeTo(new Vector2(restingSide), glide_duration, Easing.InOutCubic)
                    .OnComplete(_ => gliding = false);

                backdrop.FadeIn(500, Easing.OutQuint);

                // The ring grows in as the record settles into it.
                spectrum.Delay(glide_duration * 0.55).FadeIn(500, Easing.OutQuint);

                card.MoveToOffset(new Vector2(0, -0.02f))
                    .Delay(300)
                    .MoveToOffset(new Vector2(0, 0.02f), 650, Easing.OutQuint)
                    .FadeIn(550, Easing.OutQuint);

                hint.Delay(glide_duration * 0.75).FadeIn(500, Easing.OutQuint);
            });
        }

        private Vector2 startCentre => ToLocalSpace(startQuad).AABBFloat.Centre;

        /// <summary>The deck square that puts the record exactly over the wheel's vinyl.</summary>
        private float startSide
        {
            get
            {
                var start = ToLocalSpace(startQuad).AABBFloat;
                return Math.Max(start.Width, start.Height) / record_fraction;
            }
        }

        private Vector2 restingPosition => new Vector2(DrawWidth / 2, DrawHeight * resting_y);

        private float restingSide => Math.Min(DrawWidth, DrawHeight) * deck_fraction;

        protected override void Update()
        {
            base.Update();

            // Landed, it keeps its place on a resize; gliding, the glide owns it.
            if (!gliding && !launching)
            {
                deck.Position = restingPosition;
                deck.Size = new Vector2(restingSide);
            }

            // The sharp picture stays pinned to the screen while its mask
            // grows round it.
            revealContent.Size = DrawSize;
            revealContent.Position = revealMask.Size / 2 - revealMask.Position;
        }

        /// <summary>
        /// Plays the record: the second whoosh, one ring out of it and the
        /// picture coming into focus behind it, and gameplay — loaded
        /// meanwhile — fading up over the clear picture.
        /// </summary>
        public void Launch()
        {
            if (launching || gliding || !this.IsCurrentScreen())
                return;

            launching = true;

            sounds.PlayLaunch();

            var centre = deck.Position;
            float recordDiameter = deck.Size.X * record_fraction;

            revealMask.Position = centre;
            revealMask.Size = new Vector2(recordDiameter);
            revealMask.ResizeTo(new Vector2(coverRadiusFrom(centre) * 2), reveal_duration, Easing.OutQuint);

            sendRing(centre, recordDiameter);

            card.FadeOut(300, Easing.OutQuint);
            hint.FadeOut(200, Easing.OutQuint);
            spectrum.FadeOut(400, Easing.OutQuint);

            // The record gives way as the picture clears around it.
            deck.Delay(200).ScaleTo(1.06f, 600, Easing.OutQuint).FadeOut(600, Easing.OutQuint);

            var player = new PlayerScreen(selection);
            double pressedAt = Time.Current;

            LoadComponentAsync(player, _ => Scheduler.AddDelayed(() =>
            {
                if (this.IsCurrentScreen())
                    this.Push(player);
            }, Math.Max(0, launch_lead - (Time.Current - pressedAt))));
        }

        /// <summary>
        /// The one ring, running just ahead of the clearing picture — the
        /// menu's ripple, alone, in the difficulty's colour.
        /// </summary>
        private void sendRing(Vector2 centre, float fromDiameter)
        {
            var ring = new CircularContainer
            {
                Origin = Anchor.Centre,
                Position = centre,
                Size = new Vector2(fromDiameter),
                Masking = true,
                BorderThickness = 3,
                BorderColour = accent,
                EdgeEffect = new EdgeEffectParameters
                {
                    Type = EdgeEffectType.Glow,
                    Colour = accent.Opacity(0.5f),
                    Radius = 16,
                    Hollow = true,
                },
                Alpha = 0,
                Child = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0, AlwaysPresent = true },
            };

            AddInternal(ring);

            ring.FadeTo(0.8f, 120, Easing.OutQuint);
            ring.ResizeTo(new Vector2(coverRadiusFrom(centre) * 2.1f), reveal_duration * 1.3, Easing.OutQuint)
                .FadeOut(reveal_duration * 1.3, Easing.OutQuint)
                .Expire();
        }

        /// <summary>The distance from a point to the farthest corner — a circle this big from there covers the window.</summary>
        private float coverRadiusFrom(Vector2 point) =>
            Math.Max(Math.Max(Vector2.Distance(point, Vector2.Zero), Vector2.Distance(point, new Vector2(DrawWidth, 0))),
                Math.Max(Vector2.Distance(point, new Vector2(0, DrawHeight)), Vector2.Distance(point, DrawSize)));

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            switch (e.Key)
            {
                case osuTK.Input.Key.Enter:
                case osuTK.Input.Key.KeypadEnter:
                    Launch();
                    return true;

                case osuTK.Input.Key.Escape:
                    if (!launching)
                        this.Exit();

                    return true;
            }

            return base.OnKeyDown(e);
        }

        public override void OnEntering(ScreenTransitionEvent e)
        {
            base.OnEntering(e);

            // No fade-in: the record is on screen from the first frame,
            // exactly over the wheel it is lifting off, so the change reads
            // as the wheel moving rather than as one screen replacing another.
            // Only the backdrop comes up behind it, as song select goes.
        }

        public override void OnSuspending(ScreenTransitionEvent e)
        {
            base.OnSuspending(e);

            played = true;

            // It fades out under gameplay, and a drawable at Alpha 0 stops
            // running its scheduler — which is where OnResuming's step aside
            // runs. Kept present, it can still leave when gameplay does.
            AlwaysPresent = true;

            // Gameplay plays the song itself; the muffled copy goes, and is
            // opened up again for whenever the music next plays.
            music?.Stop();
            music?.SetMuffled(false);

            this.FadeOut(400, Easing.OutQuint);
        }

        public override void OnResuming(ScreenTransitionEvent e)
        {
            base.OnResuming(e);

            // Back from a finished run: this record has been played. Step
            // aside to song select rather than cue it up again.
            if (played)
            {
                Schedule(() =>
                {
                    if (this.IsCurrentScreen())
                        this.Exit();
                });
            }
        }

        public override bool OnExiting(ScreenExitEvent e)
        {
            music?.SetMuffled(false);

            if (played)
            {
                this.FadeOut(200, Easing.OutQuint);
                return base.OnExiting(e);
            }

            // The way back is the way in, reversed: the record glides back
            // onto the wheel on the same curve, the ring and the card go, the
            // backdrop falls away as song select comes up underneath — and
            // the record, landed on the vinyl it came from, dissolves into it.
            gliding = true;

            deck.ClearTransforms();
            deck.MoveTo(startCentre, glide_duration, Easing.InOutCubic)
                .ResizeTo(new Vector2(startSide), glide_duration, Easing.InOutCubic);

            spectrum.FadeOut(300, Easing.OutQuint);
            card.FadeOut(300, Easing.OutQuint);
            hint.FadeOut(200, Easing.OutQuint);
            backdrop.FadeOut(glide_duration * 0.8, Easing.InOutSine);

            deck.Delay(glide_duration - 200).FadeOut(300, Easing.OutQuad);

            // Kept on screen for the whole glide: the stack keeps an exiting
            // screen only as long as the transforms queued on it right now.
            this.Delay(glide_duration + 100).FadeOut(0);

            return base.OnExiting(e);
        }
    }
}
