using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Framework.Platform;
using osu.Framework.Screens;
using OsuClient.Game.Backend;
using OsuClient.Game.Platform;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.Generation
{
    /// <summary>
    /// Picks an audio file and the metadata to generate a beatmap set from
    /// (FRONTEND_PLAN.md Phase 6) — the in-app replacement for copying a file
    /// into <c>data/raw/</c> and running the pipeline from a terminal.
    ///
    /// There are two ways in. <see cref="IWindow.DragDrop"/> handles dropping a
    /// file onto the window (the same gesture lazer uses to import), and the
    /// Browse button opens a real "Open file" window.
    ///
    /// <see cref="GameHost.CreateSystemFileSelector"/> is asked for that picker
    /// first, since the mobile hosts implement it, but every desktop host
    /// returns null from it — so on desktop Browse falls through to
    /// <see cref="NativeFileDialog"/>, which opens the system dialog itself.
    /// </summary>
    public partial class UploadScreen : Screen
    {
        private static readonly string[] all_tiers = { "Easy", "Normal", "Hard", "Insane", "Expert" };

        private readonly string? songsDirectory;

        [Resolved]
        private GameHost host { get; set; } = null!;

        private BackendPaths? backend;
        private string backendError = string.Empty;

        private ISystemFileSelector? fileSelector;
        private bool pickerOpen;

        private string? selectedAudioPath;

        private SpriteText dropLabel = null!;
        private SpriteText statusLabel = null!;
        private BasicTextBox artistBox = null!;
        private BasicTextBox titleBox = null!;
        private BasicButton browseButton = null!;
        private BasicButton generateButton = null!;
        private readonly List<TierToggle> tierToggles = new List<TierToggle>();

        public UploadScreen(string? songsDirectory = null)
        {
            this.songsDirectory = songsDirectory;
        }

        /// <summary>The audio file currently queued for generation. Exposed for tests.</summary>
        public string? SelectedAudioPath => selectedAudioPath;

        /// <summary>Tiers currently ticked, in the backend's own order. Exposed for tests.</summary>
        public IReadOnlyList<string> SelectedDifficulties =>
            tierToggles.Where(t => t.Selected).Select(t => t.Tier).ToList();

        /// <summary>Whether a backend was found to generate with. Exposed for tests.</summary>
        public bool BackendAvailable => backend != null;

        /// <summary>Whether Generate would currently do anything. Exposed for tests.</summary>
        public bool CanGenerate => generateButton.Enabled.Value;

        /// <summary>Whether Browse has a picker to open here. Exposed for tests.</summary>
        public bool FilePickerAvailable => fileSelector != null || NativeFileDialog.IsSupported;

        /// <summary>The message currently shown under the form, if any. Exposed for tests.</summary>
        public string StatusMessage => statusLabel.Text.ToString();

        /// <summary>Queues a file exactly as dropping it on the window would. Exposed for tests.</summary>
        public void ChooseFile(string path) => chooseFile(path);

        [BackgroundDependencyLoader]
        private void load()
        {
            backend = BackendPaths.Locate(BackendPaths.FindRepositoryRoot(), out backendError);

            // Null on every desktop host, which is the whole reason
            // NativeFileDialog exists; created up front so the Browse button
            // knows which of the two it will be opening.
            fileSelector = host.CreateSystemFileSelector(BackendRunner.SupportedAudioExtensions);

            if (fileSelector != null)
                fileSelector.Selected += file => Schedule(() => chooseFile(file.FullName));

            InternalChildren = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(0.08f, 0.08f, 0.12f, 1f),
                },
                new FillFlowContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Width = 0.7f,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 12),
                    Children = new Drawable[]
                    {
                        new SpriteText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Text = "Generate a beatmap",
                            Font = FontUsage.Default.With(size: 30),
                            Colour = Color4.White,
                        },
                        new SpriteText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Text = "Drop an audio file anywhere on this window, or browse for one.",
                            Font = FontUsage.Default.With(size: 14),
                            Colour = new Color4(0.6f, 0.6f, 0.7f, 1f),
                            Margin = new MarginPadding { Bottom = 6 },
                        },
                        createDropZone(),
                        browseButton = new BasicButton
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Size = new Vector2(200, 36),
                            Text = "Browse…",
                            BackgroundColour = new Color4(0.24f, 0.28f, 0.38f, 1f),
                            HoverColour = new Color4(0.32f, 0.38f, 0.5f, 1f),
                            Action = presentFileSelector,
                        },
                        artistBox = createTextBox("Artist (optional — defaults to \"unknown artist\")"),
                        titleBox = createTextBox("Title (optional — defaults to the file name)"),
                        createTierRow(),
                        generateButton = new BasicButton
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Size = new Vector2(280, 48),
                            Text = "Generate",
                            BackgroundColour = new Color4(0.2f, 0.5f, 0.3f, 1f),
                            HoverColour = new Color4(0.26f, 0.65f, 0.4f, 1f),
                            Action = startGeneration,
                            Margin = new MarginPadding { Top = 6 },
                        },
                        statusLabel = new SpriteText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Text = string.Empty,
                            Font = FontUsage.Default.With(size: 14),
                            Colour = new Color4(1f, 0.5f, 0.5f, 1f),
                        },
                        new SpriteText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Text = "Escape to go back",
                            Font = FontUsage.Default.With(size: 13),
                            Colour = new Color4(0.5f, 0.5f, 0.6f, 1f),
                            Margin = new MarginPadding { Top = 4 },
                        },
                    },
                },
            };

            if (backend == null)
                statusLabel.Text = backendError;

            updateGenerateButton();
        }

        private Drawable createDropZone() => new Container
        {
            Anchor = Anchor.TopCentre,
            Origin = Anchor.TopCentre,
            RelativeSizeAxes = Axes.X,
            Height = 86,
            Masking = true,
            CornerRadius = 8,
            BorderThickness = 2,
            BorderColour = new Color4(0.3f, 0.34f, 0.45f, 1f),
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(0.05f, 0.05f, 0.09f, 1f),
                },
                dropLabel = new SpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Text = "No file chosen",
                    Font = FontUsage.Default.With(size: 18),
                    Colour = new Color4(0.65f, 0.65f, 0.75f, 1f),
                },
            },
        };

        private static BasicTextBox createTextBox(string placeholder) => new BasicTextBox
        {
            Anchor = Anchor.TopCentre,
            Origin = Anchor.TopCentre,
            RelativeSizeAxes = Axes.X,
            Height = 34,
            PlaceholderText = placeholder,
        };

        private Drawable createTierRow()
        {
            var row = new FillFlowContainer
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(6, 0),
            };

            foreach (string tier in all_tiers)
            {
                var toggle = new TierToggle(tier);

                tierToggles.Add(toggle);
                row.Add(toggle);
            }

            return row;
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
        private void onFileDropped(string path) => Schedule(() => chooseFile(path));

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
                statusLabel.Text = "No file picker available here — drag an audio file onto the window instead.";
                return;
            }

            setPickerOpen(true);
            statusLabel.Text = backend == null ? backendError : string.Empty;

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
                statusLabel.Text = result.Error;
                return;
            }

            if (result.Cancelled)
                return;

            chooseFile(result.Path!);
        }

        private void setPickerOpen(bool open)
        {
            pickerOpen = open;

            browseButton.Enabled.Value = !open;
            browseButton.Alpha = open ? 0.5f : 1f;
            browseButton.Text = open ? "Choosing…" : "Browse…";
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
                statusLabel.Text = $"{Path.GetFileName(path)} isn't an audio file the generator reads "
                                   + $"({string.Join(", ", BackendRunner.SupportedAudioExtensions)}).";
                return;
            }

            if (!File.Exists(path))
            {
                statusLabel.Text = $"That file is gone: {path}";
                return;
            }

            selectedAudioPath = path;
            dropLabel.Text = Path.GetFileName(path);
            dropLabel.Colour = Color4.White;

            statusLabel.Text = backend == null ? backendError : string.Empty;

            updateGenerateButton();
        }

        private void updateGenerateButton()
        {
            bool ready = backend != null && selectedAudioPath != null;

            generateButton.Enabled.Value = ready;
            generateButton.Alpha = ready ? 1f : 0.5f;
        }

        private void startGeneration()
        {
            if (backend == null || selectedAudioPath == null || !this.IsCurrentScreen())
                return;

            var tiers = SelectedDifficulties;

            if (tiers.Count == 0)
            {
                statusLabel.Text = "Pick at least one difficulty.";
                return;
            }

            var request = new GenerationRequest
            {
                AudioPath = selectedAudioPath,
                Artist = artistBox.Text,
                Title = titleBox.Text,
                Difficulties = tiers,
            };

            this.Push(new DspVisualizationScreen(backend, request, songsDirectory));
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
        }

        public override bool OnExiting(ScreenExitEvent e)
        {
            this.FadeOut(200, Easing.OutQuint);

            return base.OnExiting(e);
        }

        public override void OnResuming(ScreenTransitionEvent e)
        {
            base.OnResuming(e);

            this.FadeIn(250, Easing.OutQuint);
        }

        protected override void Dispose(bool isDisposing)
        {
            // The window outlives this screen, so a live handler here would
            // keep firing into a disposed drawable after leaving.
            if (host?.Window != null)
                host.Window.DragDrop -= onFileDropped;

            base.Dispose(isDisposing);
        }

        /// <summary>One difficulty tier's on/off chip. Ticked by default — all five is the CLI's own default.</summary>
        private partial class TierToggle : BasicButton
        {
            public string Tier { get; }

            private bool selected = true;

            public bool Selected
            {
                get => selected;
                private set
                {
                    selected = value;
                    updateColour();
                }
            }

            public TierToggle(string tier)
            {
                Tier = tier;

                Anchor = Anchor.TopCentre;
                Origin = Anchor.TopCentre;
                Size = new Vector2(96, 34);
                Text = tier;

                Action = () => Selected = !Selected;
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                updateColour();
            }

            private void updateColour()
            {
                BackgroundColour = selected
                    ? new Color4(0.2f, 0.42f, 0.6f, 1f)
                    : new Color4(0.16f, 0.16f, 0.2f, 1f);

                HoverColour = selected
                    ? new Color4(0.28f, 0.55f, 0.78f, 1f)
                    : new Color4(0.24f, 0.24f, 0.3f, 1f);
            }
        }
    }
}
