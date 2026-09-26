using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osu.Framework.Platform;
using osu.Framework.Screens;
using OsuClient.Game.Audio;
using OsuClient.Game.Backend;
using OsuClient.Game.Graphics;
using OsuClient.Game.Graphics.Rack;
using OsuClient.Game.Platform;
using OsuClient.Game.Screens.MainMenu;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.Generation
{
    /// <summary>
    /// Picks an audio file and the metadata to generate a beatmap set from
    /// (FRONTEND_PLAN.md Phase 6), dressed as a tape deck
    /// (GENERATION_REDESIGN_PLAN.md step 8).
    ///
    /// There are two ways in. <see cref="IWindow.DragDrop"/> handles dropping a
    /// file onto the window (the same gesture lazer uses to import), and the
    /// LOAD TAPE button opens a real "Open file" window.
    ///
    /// <see cref="GameHost.CreateSystemFileSelector"/> is asked for that picker
    /// first, since the mobile hosts implement it, but every desktop host
    /// returns null from it — so on desktop LOAD TAPE falls through to
    /// <see cref="NativeFileDialog"/>, which opens the system dialog itself.
    ///
    /// <para>
    /// The deck metaphor is not decoration on a form: the file and the
    /// metadata are one object, a tape with writing on its label, because
    /// that is what they are. Difficulties are cassettes in a rack in the same
    /// five colours song select gives the same five tiers. What has <b>not</b>
    /// changed is any of the file handling below — the reskin deliberately
    /// stops at the surface, and <c>TestSceneUploadScreen</c> passes unedited.
    /// </para>
    /// </summary>
    public partial class UploadScreen : Screen, IRevealable
    {
        private ScreenReveal reveal = null!;

        /// <summary>Everything this screen draws, for the menu to open it through one curve.</summary>
        public ScreenReveal Reveal => reveal;

        /// <summary>
        /// The tiers offered, with the star target each is labelled by.
        ///
        /// Copied from <c>configs/difficulty_presets.yaml</c>, where the
        /// comment is clear that <c>stars</c> is a rough label and not a
        /// computed rating — so these are captions, and nothing downstream
        /// reads them back.
        /// </summary>
        private static readonly (string Name, double Stars)[] all_tiers =
        {
            ("Easy", 1.5),
            ("Normal", 2.5),
            ("Hard", 3.7),
            ("Insane", 4.8),
            ("Expert", 6.0),
        };

        private readonly string? songsDirectory;

        /// <summary>The menu's song, muffled and slowed, under the deck.</summary>
        private readonly TapeDeckMusic music;

        [Resolved]
        private GameHost host { get; set; } = null!;

        private BackendPaths? backend;
        private string backendError = string.Empty;

        private ISystemFileSelector? fileSelector;
        private bool pickerOpen;

        private string? selectedAudioPath;

        private MenuBackground background = null!;

        /// <summary>The wallpaper this deck picked, handed on so the generation screen sits on the same picture.</summary>
        private string? wallpaper;
        private CassetteBay bay = null!;
        private RetroText statusLabel = null!;
        private IndicatorLamp statusLamp = null!;
        private TransportButton browseButton = null!;
        private TransportButton coverButton = null!;
        private CoverArtCard coverCard = null!;

        private readonly TapeDeckSoundPlayer sounds = new TapeDeckSoundPlayer();

        /// <summary>The image chosen as the set's cover art, or null for none.</summary>
        private string? selectedCoverPath;

        /// <summary>Space between the tape and the cover art case.</summary>
        private const float bay_gap = 36;

        /// <summary>The bay's width: the cassette plus its recess (see <see cref="CassetteBay"/>).</summary>
        private const float bay_width = 476;
        private RecordButton generateButton = null!;
        private readonly List<TierSpine> tierSpines = new List<TierSpine>();

        /// <param name="songsDirectory">Where finished maps are listed from.</param>
        /// <param name="menuAudioPath">The song the menu was playing, to carry on under the deck; null for silence.</param>
        /// <param name="menuTime">How far into it the menu had got, in milliseconds.</param>
        public UploadScreen(string? songsDirectory = null, string? menuAudioPath = null, double menuTime = 0)
        {
            this.songsDirectory = songsDirectory;
            music = new TapeDeckMusic(menuAudioPath, menuTime);
        }

        /// <summary>The audio file currently queued for generation. Exposed for tests.</summary>
        public string? SelectedAudioPath => selectedAudioPath;

        /// <summary>The cover art queued, or null. Exposed for tests.</summary>
        public string? SelectedCoverPath => selectedCoverPath;

        /// <summary>Tiers currently ticked, in the backend's own order. Exposed for tests.</summary>
        public IReadOnlyList<string> SelectedDifficulties =>
            tierSpines.Where(t => t.Selected).Select(t => t.Tier).ToList();

        /// <summary>Whether a backend was found to generate with. Exposed for tests.</summary>
        public bool BackendAvailable => backend != null;

        /// <summary>Whether Generate would currently do anything. Exposed for tests.</summary>
        public bool CanGenerate => generateButton.Enabled.Value;

        /// <summary>Whether Browse has a picker to open here. Exposed for tests.</summary>
        public bool FilePickerAvailable => fileSelector != null || NativeFileDialog.IsSupported;

        /// <summary>The message currently shown on the front panel, if any. Exposed for tests.</summary>
        public string StatusMessage => statusLabel.Text;

        /// <summary>Queues a file exactly as dropping it on the window would. Exposed for tests.</summary>
        public void ChooseFile(string path) => chooseFile(path);

        /// <summary>Queues cover art exactly as dropping an image on the window would. Exposed for tests.</summary>
        public void ChooseCover(string path) => chooseCover(path);

        [BackgroundDependencyLoader]
        private void load()
        {
            backend = BackendPaths.Locate(BackendPaths.FindRepositoryRoot(), out backendError);

            // Null on every desktop host, which is the whole reason
            // NativeFileDialog exists; created up front so the LOAD TAPE
            // button knows which of the two it will be opening.
            fileSelector = host.CreateSystemFileSelector(BackendRunner.SupportedAudioExtensions);

            if (fileSelector != null)
                fileSelector.Selected += file => Schedule(() => chooseFile(file.FullName));

            InternalChild = reveal = new ScreenReveal
            {
                Children = new Drawable[]
                {
                        // A workbench holds still, so the art does not drift here the
                        // way it does behind the menu's moving logo.
                        //
                        // It also stays grey. MenuBackground only spreads colour when
                        // asked (the menu pushes it out of the logo), and not asking
                        // is the right answer here: leaving the art monochrome means
                        // every coloured thing on this screen is carrying meaning —
                        // the difficulty ramp, the record lamp, the label's spine —
                        // rather than competing with a photograph.
                        background = new MenuBackground { Drifting = false },
                        // Knocked back further than the menu's own 22%. The menu is a
                        // title screen and the art is the subject; this is a form, and
                        // the art is a backdrop for text.
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = new Color4(0f, 0f, 0f, 0.42f),
                        },
                        music,
                        sounds,
                        new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Padding = new MarginPadding { Horizontal = 46, Vertical = 34 },
                            Child = new GridContainer
                            {
                                RelativeSizeAxes = Axes.Both,
                                RowDimensions = new[]
                                {
                                    new Dimension(GridSizeMode.Absolute, 34),
                                    new Dimension(),
                                    new Dimension(GridSizeMode.Absolute, 232),
                                },
                                Content = new[]
                                {
                                    new Drawable[] { createHeader() },
                                    new Drawable[] { createBayRow() },
                                    new Drawable[] { createDeckPanel() },
                                },
                            },
                        },
                },
            };

            // The same wallpaper source the menu uses. An empty folder is the
            // normal case on a clean clone, and falls through to the bare
            // gradient rather than to a missing-texture flash.
            wallpaper = WallpaperLibrary.PickRandom(
                WallpaperLibrary.Load(WallpaperLibrary.ResolveDefaultDirectory()));
            background.SetBackground(wallpaper);

            if (backend == null)
                setStatus(backendError, true);
            else
                setStatus(string.Empty, false);

            updateGenerateButton();
        }

        private Drawable createHeader() => new Container
        {
            RelativeSizeAxes = Axes.Both,
            Children = new Drawable[]
            {
                new RetroText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Font = RetroFontFamily.Display,
                    TextSize = 15,
                    Colour = RetroPalette.Text,
                    Text = "TAPE DECK",
                },
                new RetroText
                {
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.CentreRight,
                    Font = RetroFontFamily.Body,
                    TextSize = 11,
                    Colour = RetroPalette.TextDim.Opacity(0.8f),
                    Text = "ESC — BACK",
                },
            },
        };

        /// <summary>
        /// The tape and its case, side by side: the cassette on the left, the
        /// cover art case on the right, the pair centred as one unit — a
        /// tape and its sleeve laid out on the deck.
        /// </summary>
        private Drawable createBayRow() => new Container
        {
            RelativeSizeAxes = Axes.Both,
            Children = new Drawable[]
            {
                new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    X = -(CoverArtCard.CaseSize.X + bay_gap) / 2,
                    Child = bay = new CassetteBay
                    {
                        // The empty bay opens the same browser LOAD TAPE does.
                        BrowseRequested = presentFileSelector,
                    },
                },
                coverCard = new CoverArtCard
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    X = (bay_width + bay_gap) / 2,
                    Action = presentCoverSelector,
                    ClearRequested = clearCover,
                },
            },
        };

        /// <summary>The deck's front panel: the rack of difficulties, the transport buttons, and the status lamp.</summary>
        private Drawable createDeckPanel()
        {
            var rack = new FillFlowContainer
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(10, 0),
            };

            for (int i = 0; i < all_tiers.Length; i++)
            {
                var (name, stars) = all_tiers[i];
                var spine = new TierSpine(name, stars, i, all_tiers.Length)
                {
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                };

                spine.Toggled += sounds.PlaySpine;

                tierSpines.Add(spine);
                rack.Add(spine);
            }

            return new RackPanel
            {
                RelativeSizeAxes = Axes.Both,
                Legend = "DIFFICULTIES — PUSHED IN IS ARMED",
                Children = new Drawable[]
                {
                    rack,
                    new FillFlowContainer
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 14),
                        Margin = new MarginPadding { Right = 8 },
                        Children = new Drawable[]
                        {
                            // Side by side, the pair as wide as RECORD below:
                            // the tape and its cover are loaded together, and
                            // a third full-width key would not fit the panel.
                            new FillFlowContainer
                            {
                                Anchor = Anchor.TopRight,
                                Origin = Anchor.TopRight,
                                AutoSizeAxes = Axes.Both,
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(6, 0),
                                Children = new Drawable[]
                                {
                                    browseButton = new TransportButton("LOAD TAPE", 112)
                                    {
                                        Action = () =>
                                        {
                                            sounds.PlayKey();
                                            presentFileSelector();
                                        },
                                    },
                                    coverButton = new TransportButton("LOAD COVER", 112)
                                    {
                                        Action = () =>
                                        {
                                            sounds.PlayKey();
                                            presentCoverSelector();
                                        },
                                    },
                                },
                            },
                            generateButton = new RecordButton
                            {
                                Anchor = Anchor.TopRight,
                                Origin = Anchor.TopRight,
                                Action = startGeneration,
                            },
                            new FillFlowContainer
                            {
                                Anchor = Anchor.TopRight,
                                Origin = Anchor.TopRight,
                                AutoSizeAxes = Axes.Y,
                                Width = 320,
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(8, 0),
                                Children = new Drawable[]
                                {
                                    statusLamp = new IndicatorLamp
                                    {
                                        Anchor = Anchor.TopLeft,
                                        Origin = Anchor.TopLeft,
                                        Margin = new MarginPadding { Top = 2 },
                                        LampColour = new Color4(1f, 0.32f, 0.34f, 1f),
                                        State = LampState.Off,
                                    },
                                    statusLabel = new RetroText
                                    {
                                        Font = RetroFontFamily.Body,
                                        TextSize = 10,
                                        Colour = RetroPalette.TextDim,
                                        Text = string.Empty,
                                    },
                                },
                            },
                        },
                    },
                },
            };
        }

        /// <summary>
        /// A complaint about something the player just did — a file it can't
        /// use, a picker that failed — with the deck's refusal buzz. Not for
        /// standing conditions like a missing backend, which would buzz on
        /// every visit.
        /// </summary>
        private void refuse(string message)
        {
            setStatus(message, true);
            sounds.PlayRefuse();
        }

        /// <summary>
        /// Puts a message on the front panel, lighting the fault lamp for a
        /// complaint.
        ///
        /// The backing text is what <see cref="StatusMessage"/> returns, so
        /// the wording stays exactly what the tests assert on.
        /// </summary>
        private void setStatus(string message, bool fault)
        {
            statusLabel.Text = message;
            statusLabel.Colour = fault ? new Color4(1f, 0.55f, 0.55f, 1f) : RetroPalette.TextDim;
            statusLamp.State = fault && message.Length > 0 ? LampState.Fault : LampState.Off;

            // An unlit lamp is a dark red dot, which on an otherwise empty
            // panel reads as a stray artefact rather than as a lamp that has
            // nothing to say.
            statusLamp.Alpha = message.Length > 0 ? 1 : 0;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            if (host.Window != null)
                host.Window.DragDrop += onFileDropped;
        }

        /// <summary>
        /// Fires from the windowing layer rather than the update thread, so
        /// everything it touches gets scheduled back.
        /// </summary>
        private void onFileDropped(string path) => Schedule(() =>
        {
            // An image is cover art; anything else is offered as the tape,
            // which says so if it is not audio either.
            if (BackendRunner.IsSupportedCoverFile(path))
                chooseCover(path);
            else
                chooseFile(path);
        });

        private void presentFileSelector()
        {
            // A second dialog while one is already up would be two windows
            // racing to set the same file.
            if (pickerOpen)
                return;

            if (fileSelector != null)
            {
                fileSelector.Present();
                return;
            }

            if (!NativeFileDialog.IsSupported)
            {
                // Nothing to open on this platform; drag and drop still works,
                // so say that rather than nothing.
                refuse("No file picker available here — drag an audio file onto the window instead.");
                return;
            }

            setPickerOpen(true);

            if (backend == null)
                setStatus(backendError, true);
            else
                setStatus(string.Empty, false);

            NativeFileDialog.OpenFile(
                "Choose an audio file to generate from",
                BackendRunner.SupportedAudioExtensions,
                initialBrowseDirectory(),
                // Comes back on the dialog's own thread, like the drop handler.
                result => Schedule(() => onFilePicked(result)));
        }

        private void onFilePicked(FileDialogResult result)
        {
            setPickerOpen(false);

            if (result.Error != null)
            {
                refuse(result.Error);
                return;
            }

            if (result.Cancelled)
                return;

            chooseFile(result.Path!);
        }

        private void setPickerOpen(bool open, bool forCover = false)
        {
            pickerOpen = open;

            // One dialog at a time: both keys go dead while either is up, and
            // the one that opened it shows as held down.
            browseButton.Enabled.Value = !open;
            coverButton.Enabled.Value = !open;

            browseButton.Held = open && !forCover;
            coverButton.Held = open && forCover;

            browseButton.Label = open && !forCover ? "CHOOSING…" : "LOAD TAPE";
            coverButton.Label = open && forCover ? "CHOOSING…" : "LOAD COVER";
        }

        private void presentCoverSelector()
        {
            if (pickerOpen)
                return;

            if (!NativeFileDialog.IsSupported)
            {
                refuse("No file picker available here — drag an image onto the window instead.");
                return;
            }

            setPickerOpen(true, forCover: true);

            string? previous = selectedCoverPath == null ? null : Path.GetDirectoryName(selectedCoverPath);

            NativeFileDialog.OpenFile(
                "Choose cover art",
                BackendRunner.SupportedCoverExtensions,
                previous != null && Directory.Exists(previous) ? previous : null,
                result => Schedule(() =>
                {
                    setPickerOpen(false);

                    if (result.Error != null)
                        refuse(result.Error);
                    else if (!result.Cancelled)
                        chooseCover(result.Path!);
                }));
        }

        private void chooseCover(string path)
        {
            if (!BackendRunner.IsSupportedCoverFile(path))
            {
                refuse($"{Path.GetFileName(path)} can't be cover art "
                          + $"({string.Join(", ", BackendRunner.SupportedCoverExtensions)}).");
                return;
            }

            if (!File.Exists(path))
            {
                refuse($"That file is gone: {path}");
                return;
            }

            selectedCoverPath = path;
            coverCard.SetImage(path);
            sounds.PlayCaseClose();

            if (backend == null)
                setStatus(backendError, true);
            else
                setStatus(string.Empty, false);
        }

        private void clearCover()
        {
            if (selectedCoverPath != null)
                sounds.PlayCaseOpen();

            selectedCoverPath = null;
            coverCard.SetImage(null);
        }

        /// <summary>
        /// Opens next to the last file chosen, then wherever the backend keeps
        /// its input audio, and otherwise lets the shell decide.
        /// </summary>
        private string? initialBrowseDirectory()
        {
            string? previous = selectedAudioPath == null ? null : Path.GetDirectoryName(selectedAudioPath);

            if (previous != null && Directory.Exists(previous))
                return previous;

            string? raw = backend == null ? null : Path.Combine(backend.RepositoryRoot, "data", "raw");

            return raw != null && Directory.Exists(raw) ? raw : null;
        }

        private void chooseFile(string path)
        {
            if (!BackendRunner.IsSupportedAudioFile(path))
            {
                refuse($"{Path.GetFileName(path)} isn't an audio file the generator reads "
                          + $"({string.Join(", ", BackendRunner.SupportedAudioExtensions)}).");
                return;
            }

            if (!File.Exists(path))
            {
                refuse($"That file is gone: {path}");
                return;
            }

            selectedAudioPath = path;
            bay.SetFile(Path.GetFileName(path));
            sounds.PlayTapeInsert();

            if (backend == null)
                setStatus(backendError, true);
            else
                setStatus(string.Empty, false);

            updateGenerateButton();
        }

        private void updateGenerateButton()
        {
            bool ready = backend != null && selectedAudioPath != null;

            generateButton.Enabled.Value = ready;
            generateButton.Armed = ready;
        }

        private void startGeneration()
        {
            if (backend == null || selectedAudioPath == null || !this.IsCurrentScreen())
                return;

            var tiers = SelectedDifficulties;

            if (tiers.Count == 0)
            {
                refuse("Pick at least one difficulty.");
                return;
            }

            var request = new GenerationRequest
            {
                AudioPath = selectedAudioPath,
                Artist = bay.ArtistBox.Text,
                Title = bay.TitleBox.Text,
                Difficulties = tiers,
                CoverPath = selectedCoverPath,
            };

            generateButton.Engage();
            sounds.PlayRecord();

            this.Push(new DspVisualizationScreen(backend, request, songsDirectory, wallpaper));
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (e.Key == osuTK.Input.Key.Escape)
            {
                this.Exit();
                return true;
            }

            return base.OnKeyDown(e);
        }

        public override void OnEntering(ScreenTransitionEvent e)
        {
            base.OnEntering(e);

            this.FadeInFromZero(250, Easing.OutQuint);
            music.Start();
        }

        public override bool OnExiting(ScreenExitEvent e)
        {
            music.Stop();
            this.FadeOut(200, Easing.OutQuint);

            return base.OnExiting(e);
        }

        public override void OnSuspending(ScreenTransitionEvent e)
        {
            base.OnSuspending(e);

            // The generation screen plays the new map's own audio; two songs
            // at once is the regression to avoid. Faded rather than cut, and
            // the deck fades too — under the arriving screen, unseen — so it
            // keeps updating long enough for the music's fade to finish.
            music.Stop();
            this.FadeOut(250, Easing.OutQuint);
        }

        public override void OnResuming(ScreenTransitionEvent e)
        {
            base.OnResuming(e);

            this.FadeIn(250, Easing.OutQuint);
            music.Start();
        }

        protected override void Dispose(bool isDisposing)
        {
            // The window outlives this screen, so a live handler here would
            // keep firing into a disposed drawable after leaving.
            if (host?.Window != null)
                host.Window.DragDrop -= onFileDropped;

            base.Dispose(isDisposing);
        }

        /// <summary>A chunky chrome transport key, the deck's equivalent of a plain button.</summary>
        private partial class TransportButton : ClickableContainer
        {
            private readonly Box face;
            private readonly RetroText label;

            public TransportButton(string text, float width = 230)
            {
                Size = new Vector2(width, 44);
                Masking = true;
                CornerRadius = 5;
                BorderThickness = 1.5f;
                BorderColour = RetroPalette.ChromeDark;

                Children = new Drawable[]
                {
                    face = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = ColourInfo.GradientVertical(
                            RetroPalette.Chrome.Darken(0.15f),
                            RetroPalette.ChromeDark.Darken(0.35f)),
                    },
                    label = new RetroText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Font = RetroFontFamily.Body,
                        TextSize = 12,
                        Colour = RetroPalette.Text,
                        Text = text,
                    },
                };
            }

            public string Label
            {
                get => label.Text;
                set => label.Text = value;
            }

            /// <summary>Held down — what a key looks like while its dialog is open.</summary>
            public bool Held
            {
                set => face.FadeColour(value
                    ? ColourInfo.GradientVertical(RetroPalette.ChromeDark.Darken(0.5f),
                                                  RetroPalette.ChromeDark.Darken(0.2f))
                    : ColourInfo.GradientVertical(RetroPalette.Chrome.Darken(0.15f),
                                                  RetroPalette.ChromeDark.Darken(0.35f)),
                    100);
            }

            protected override bool OnHover(HoverEvent e)
            {
                label.FadeColour(Color4.White, 80);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                label.FadeColour(RetroPalette.Text, 120);
                base.OnHoverLost(e);
            }
        }

        /// <summary>
        /// The record key: a red button that lights when there is something to
        /// record, and flares when it is pressed.
        /// </summary>
        private partial class RecordButton : ClickableContainer
        {
            private static readonly Color4 armed = new Color4(0.93f, 0.20f, 0.26f, 1f);

            private readonly Container dot;
            private readonly Box dotFill;
            private readonly RetroText label;

            public RecordButton()
            {
                Size = new Vector2(230, 58);
                Masking = true;
                CornerRadius = 5;
                BorderThickness = 1.5f;
                BorderColour = RetroPalette.ChromeDark;

                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = ColourInfo.GradientVertical(
                            RetroPalette.ChromeDark.Darken(0.1f),
                            RetroPalette.ChromeDark.Darken(0.45f)),
                    },
                    dot = new Container
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        X = 24,
                        Size = new Vector2(22),
                        Masking = true,
                        CornerRadius = 11,
                        Child = dotFill = new Box { RelativeSizeAxes = Axes.Both },
                    },
                    label = new RetroText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        X = 58,
                        Font = RetroFontFamily.Display,
                        TextSize = 13,
                        Text = "RECORD",
                    },
                };
            }

            /// <summary>Whether there is a tape and a backend — that is, whether pressing it would do anything.</summary>
            public bool Armed
            {
                set
                {
                    dotFill.FadeColour(value ? armed : armed.Darken(0.75f).Opacity(0.7f), 140);
                    label.FadeColour(value ? RetroPalette.Text : RetroPalette.TextDim.Opacity(0.5f), 140);

                    dot.EdgeEffect = new EdgeEffectParameters
                    {
                        Type = EdgeEffectType.Glow,
                        Colour = value ? armed.Opacity(0.7f) : Color4.Transparent,
                        Radius = 14,
                    };
                }
            }

            /// <summary>Flares once, as the run starts.</summary>
            public void Engage()
            {
                dot.ScaleTo(1.25f, 90, Easing.OutQuint).Then().ScaleTo(1f, 260, Easing.OutQuint);
            }
        }
    }
}
