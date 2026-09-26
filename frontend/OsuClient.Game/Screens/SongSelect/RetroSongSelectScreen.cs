using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.IO.Stores;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Framework.Threading;
using OsuClient.Game.Audio;
using OsuClient.Game.Beatmaps;
using OsuClient.Game.Graphics;
using OsuClient.Game.Scores;
using OsuClient.Game.Screens.Gameplay;
using OsuClient.Game.Screens.MainMenu;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.SongSelect
{
    /// <summary>
    /// Song select, 80s vinyl-and-cassette edition — see
    /// CAROUSEL_REDESIGN_PLAN.md.
    ///
    /// This class owns layout: where the five regions sit and how they react
    /// to the window size, plus wiring the wheel, search box and cassette
    /// column together.
    ///
    /// The left-hand panels stack top to bottom with a fixed gap and the
    /// search bar is pinned to the bottom, so the cassette column gets
    /// whatever height is left between them — and shrinks in place rather
    /// than moving when that isn't enough. See <see cref="Update"/>.
    /// </summary>
    public partial class RetroSongSelectScreen : Screen, IRevealable
    {
        private ScreenReveal reveal = null!;

        /// <summary>Everything this screen draws, for the menu to open it through one curve.</summary>
        public ScreenReveal Reveal => reveal;

        // ------------------------------------------------------------------
        // Wheel geometry.
        //
        // The record is deliberately larger than the window and its hub sits
        // just past the right edge, so only a half-disc bulges into view —
        // that silhouette is what makes it read as a record on a turntable
        // rather than a pie chart floating on screen.
        // ------------------------------------------------------------------

        /// <summary>Hub position, as a fraction of the window. Just off the right edge.</summary>
        private const float hub_x = 1.0f;

        private const float hub_y = 0.47f;

        /// <summary>Disc diameter as a multiple of window height.</summary>
        private const float disc_scale = 1.52f;

        /// <summary>Gutter around the left-hand panels.</summary>
        private const float margin = 52;

        /// <summary>Clear space between the three stacked left-hand panels.</summary>
        private const float column_gap = 32;

        /// <summary>
        /// How long the wheel has to sit still before the left-hand panels
        /// rebuild. Long enough to coalesce a flick of the scroll wheel into
        /// one rebuild, short enough that a single step still feels immediate.
        /// </summary>
        private const double settle_delay = 140;

        /// <summary>
        /// How far the cassette column may be shrunk to fit the space between
        /// the info panel and the search bar before its labels stop being
        /// worth reading. Below this it would be better to scroll, but no
        /// window this client runs in gets close.
        /// </summary>
        private const float min_cassette_scale = 0.6f;

        /// <summary>Gap between the info panel and the analyse plate under it — tight, so they read as a pair.</summary>
        private const float analyse_gap = 12;

        /// <summary>
        /// A preview under the wheel, not a performance in its own right — the
        /// left-hand panels and the wheel itself are still the loudest things
        /// on screen.
        /// </summary>
        private const double preview_volume = 0.45;

        private readonly string? requestedDirectory;
        private readonly string? preferredSetPath;

        [Resolved]
        private AudioManager audio { get; set; } = null!;

        [Resolved]
        private GameHost host { get; set; } = null!;

        private string? songsDirectory;
        private IReadOnlyList<BeatmapLibraryEntry> entries = new List<BeatmapLibraryEntry>();

        private CarouselBackground background = null!;
        private SongInfoPanel infoPanel = null!;
        private CassetteSearchBar searchBar = null!;
        private AnalyseDeckButton analyseButton = null!;
        private FillFlowContainer difficultyColumn = null!;
        private VinylCarousel carousel = null!;
        private Turntable turntable = null!;
        private Container wheelArea = null!;

        /// <summary>The library filtered by the search box — what the wheel shows.</summary>
        private IReadOnlyList<BeatmapLibraryEntry> visibleEntries = new List<BeatmapLibraryEntry>();

        private readonly List<CassetteButton> cassettes = new List<CassetteButton>();

        /// <summary>The difficulty that would be played — what Enter confirms.</summary>
        private Beatmap? selectedBeatmap;

        /// <summary>
        /// What plays the selected song: the game's shared music, so the
        /// song the menu was playing carries straight on here and whatever is
        /// playing when this screen is left carries on in the menu.
        /// </summary>
        private MenuTrack music = null!;

        [Resolved(CanBeNull = true)]
        private MenuTrack? sharedMusic { get; set; }

        /// <summary>Where the best runs are kept. Null outside the game, as in tests, where no scores show.</summary>
        [Resolved(CanBeNull = true)]
        private HighScoreStore? highScores { get; set; }

        private ScheduledDelegate? pendingShowSet;
        private bool hasShownSet;
        private Container infoArea = null!;
        private Container difficultyArea = null!;
        private Container searchArea = null!;

        public RetroSongSelectScreen(string? songsDirectory = null, string? preferredSetPath = null)
        {
            requestedDirectory = songsDirectory;
            this.preferredSetPath = preferredSetPath;
        }

        private SongSelectSoundPlayer sounds = null!;

        private IRenderer renderer = null!;

        [BackgroundDependencyLoader]
        private void load(AudioManager audio, IRenderer renderer)
        {
            this.renderer = renderer;

            sounds = new SongSelectSoundPlayer(audio);

            songsDirectory = requestedDirectory ?? BeatmapLibrary.ResolveDefaultSongsDirectory();
            entries = BeatmapLibrary.Load(songsDirectory);

            // Every cover the wheel will show, decoded here on the load
            // thread. The wedges are built in LoadComplete, on the update
            // thread, and each decodes its own cover there on a miss — on a
            // first visit that was most of a second of full-size JPEG
            // decoding in one frame, which is what made the first PLAY
            // stutter and every later one (cache warm) smooth. The menu loads
            // this screen while its ripple plays, so this happens under that.
            foreach (var entry in entries)
                CoverArt.Load(renderer, entry.BackgroundPath, VinylWedge.ArtResolution);

            InternalChild = reveal = new ScreenReveal
            {
                Children = new Drawable[]
                {
                        background = new CarouselBackground(),
                        wheelArea = new Container
                        {
                            Origin = Anchor.Centre,
                            Children = new Drawable[]
                            {
                                turntable = new Turntable(),
                                carousel = new VinylCarousel(),
                            },
                        },
                        infoArea = new Container
                        {
                            Anchor = Anchor.TopLeft,
                            Origin = Anchor.TopLeft,
                            AutoSizeAxes = Axes.Y,
                            Position = new Vector2(margin),
                            Child = infoPanel = new SongInfoPanel(),
                        },
                        analyseButton = new AnalyseDeckButton(),
                        difficultyArea = new Container
                        {
                            Anchor = Anchor.TopLeft,
                            Origin = Anchor.TopLeft,
                            AutoSizeAxes = Axes.Y,
                            Child = difficultyColumn = new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Direction = FillDirection.Vertical,
                                Spacing = new Vector2(0, 13),
                            },
                        },
                        searchArea = new Container
                        {
                            Anchor = Anchor.BottomLeft,
                            Origin = Anchor.BottomLeft,
                            AutoSizeAxes = Axes.Y,
                            Child = searchBar = new CassetteSearchBar(),
                        },
                },
            };

            // Outside the game there is nothing shared to play through, so a
            // stand-in of its own — told what to play, never picking.
            music = sharedMusic ?? new MenuTrack(songsDirectory, pickOnLoad: false);

            if (sharedMusic == null)
                AddInternal(music);

            // The first song's cassettes and info panel, built here on the
            // load thread for the same reason as the covers: built on the
            // update thread when the wheel first reports its selection, their
            // text rasterizing cost a second stall right after the first. The
            // song is the one the wheel will open on; ShowSet sees they are
            // already built for it and does only what needs a live screen.
            if (initialEntry() is { IsValid: true } first)
                buildSetPanels(first);
        }

        /// <summary>
        /// The song the wheel opens on, worked out the way it will: the
        /// preferred set if it is in the library, the first song otherwise.
        /// </summary>
        private BeatmapLibraryEntry? initialEntry()
        {
            var all = BeatmapFilter.Apply(entries, string.Empty);

            return all.FirstOrDefault(matchesPreferredSet) ?? all.FirstOrDefault();
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            carousel.SelectionChanged += onSelectionChanged;
            carousel.SelectionConfirmed += onSelectionConfirmed;
            carousel.Turned += forward => sounds.PlayTurn(forward, Time.Current);

            searchBar.Focused += sounds.PlaySearchClick;

            searchBar.QueryChanged += applyFilter;
            applyFilter(string.Empty);
        }

        /// <summary>Turns the record by <paramref name="delta"/> songs.</summary>
        public void SelectRelative(int delta) => carousel.SelectRelative(delta);

        /// <summary>Confirms the song under the needle, as clicking it or Enter does. For tests, like <see cref="SelectRelative"/>.</summary>
        public void ConfirmSelection() => carousel.ConfirmSelection();

        /// <summary>
        /// Focuses the search box and puts <paramref name="query"/> in it.
        /// Public for the same reason <see cref="SelectRelative"/> is: the
        /// screenshot harness drives the screen through these, and it has no
        /// way to click or type.
        /// </summary>
        public void Search(string query)
        {
            searchBar.Focus();
            searchBar.SetQuery(query);
        }

        /// <summary>
        /// Rebuilding the left-hand panels is expensive — a dozen
        /// <see cref="RetroText"/> values, each of which rasterizes to a
        /// texture, plus a fresh cassette button per difficulty. Doing that on
        /// every notch of the scroll wheel is what made turning the record
        /// feel like it was chugging.
        ///
        /// So the wheel turns freely and the panels catch up once it settles.
        /// The very first selection is applied immediately, or the screen
        /// would be visibly empty on entry.
        /// </summary>
        private void onSelectionChanged(BeatmapLibraryEntry? entry)
        {
            pendingShowSet?.Cancel();

            if (!hasShownSet)
            {
                hasShownSet = true;
                ShowSet(entry);
                return;
            }

            pendingShowSet = Scheduler.AddDelayed(() => ShowSet(entry), settle_delay);
        }

        /// <summary>
        /// Shows the ANALYSE button when this set still has the analysis the
        /// backend wrote beside it.
        ///
        /// Every set generated before the DSP trace existed will open the
        /// analyser in its reduced form — spectrogram, onsets and beat grid,
        /// and a line saying the rest needs regenerating. That is worth
        /// offering: it is what turns maps generated months ago into
        /// something that can be looked at without re-running anything.
        /// </summary>
        private void offerAnalyser(BeatmapLibraryEntry? entry)
        {
            string? folder = analysisFolderFor(entry);

            analyseButton.SetAction(folder == null
                ? null
                : () =>
                {
                    // Putting the song under the stylus to look at closely.
                    sounds.PlayNeedleDrop();
                    this.Push(new Screens.Analysis.DspInspectorScreen(folder));
                });
        }

        /// <summary>The set's own folder, when it holds an analysis to read.</summary>
        private static string? analysisFolderFor(BeatmapLibraryEntry? entry)
        {
            string? audio = entry?.Set?.AudioPath;

            if (string.IsNullOrEmpty(audio))
                return null;

            string? folder = System.IO.Path.GetDirectoryName(audio);

            if (folder == null || !System.IO.File.Exists(
                    System.IO.Path.Combine(folder, Backend.AnalysisData.FileName)))
            {
                return null;
            }

            return folder;
        }

        private void onSelectionConfirmed(BeatmapLibraryEntry entry)
        {
            if (!this.IsCurrentScreen() || selectedBeatmap == null)
                return;

            // Not straight into gameplay: the record lifts off the wheel,
            // the music steps back behind a muffle, and the cue screen sets
            // the record in the middle to be played.
            sounds.PlayWhoosh();
            music.SetMuffled(true);

            var accent = cassettes.Find(c => ReferenceEquals(c.Beatmap, selectedBeatmap))?.Accent ?? RetroPalette.Cyan;

            this.Push(new PlayCueScreen(new BeatmapSelection(entry, selectedBeatmap), accent, wheelArea.ScreenSpaceDrawQuad));
        }

        /// <summary>
        /// Points the background, info panel and cassette column at one set.
        /// The wheel calls this on every selection change (step 8); until then
        /// it runs once at load.
        /// </summary>
        public void ShowSet(BeatmapLibraryEntry? entry)
        {
            background.SetBackground(entry?.BackgroundPath);

            // The cover the cue screen will want if this song is chosen,
            // decoded now in the background while it is only selected — so
            // choosing it moves at once instead of waiting on a decode.
            if (entry?.BackgroundPath is string cover)
                System.Threading.Tasks.Task.Run(() => CoverTextures.Load(renderer, cover));

            if (entry == null || !entry.IsValid)
            {
                difficultyColumn.Clear();
                cassettes.Clear();
                panelsBuiltFor = null;

                selectedBeatmap = null;
                infoPanel.SetBeatmap(null);
                analyseButton.SetAction(null);
                stopPreview();
                return;
            }

            if (!ReferenceEquals(panelsBuiltFor, entry))
                buildSetPanels(entry);

            playSong(entry);
            offerAnalyser(entry);
        }

        /// <summary>The song the cassettes and info panel currently show.</summary>
        private BeatmapLibraryEntry? panelsBuiltFor;

        /// <summary>
        /// The cassettes and info panel for a song, with its first difficulty
        /// picked. Safe before the screen has loaded: nothing here touches
        /// audio or loads anything asynchronously.
        /// </summary>
        private void buildSetPanels(BeatmapLibraryEntry entry)
        {
            difficultyColumn.Clear();
            cassettes.Clear();

            var ordered = entry.Difficulties.OrderBy(DifficultyTier.SortKey).ToList();

            for (int i = 0; i < ordered.Count; i++)
            {
                var button = new CassetteButton(ordered[i], i, ordered.Count);

                button.Action = () =>
                {
                    // Only on a press: the first cassette being picked for a
                    // new song is the screen's doing, not the player's.
                    sounds.PlayDeckKey();
                    selectDifficulty(button);
                };

                cassettes.Add(button);
                difficultyColumn.Add(button);
            }

            selectDifficulty(cassettes[0]);
            panelsBuiltFor = entry;
        }

        /// <summary>
        /// Plays the selected song's own audio, quietly, under the wheel — a
        /// taste of what the player is about to pick. A song that is already
        /// playing just carries on; a new one starts around the
        /// middle of what's actually mapped rather than the intro: that's
        /// usually the densest, most representative part of the song (often
        /// a chorus), where an intro or outro is more likely to undersell it.
        /// One call per song selected, not per difficulty click — difficulties
        /// in a set share the same audio, so switching between them has
        /// nothing new to preview.
        /// </summary>
        private void playSong(BeatmapLibraryEntry entry)
        {
            // Looping while here: the record on screen is this song.
            music.AutoAdvance = false;
            music.FadeVolumeTo(preview_volume, 500);

            // The song already playing — the one the menu handed over, or the
            // one this screen was left on — carries on uninterrupted. Only a
            // different song starts, from its preview point.
            music.Play(entry);
        }

        private void stopPreview() => music.Stop();

        private void selectDifficulty(CassetteButton button)
        {
            foreach (var cassette in cassettes)
                cassette.Selected = ReferenceEquals(cassette, button);

            selectedBeatmap = button.Beatmap;
            infoPanel.SetBeatmap(button.Beatmap, button.Accent);
            infoPanel.SetHighScore(highScores?.Get(button.Beatmap.ContentHash));
        }

        /// <summary>
        /// Narrows the library to what matches the search box. The wheel
        /// (step 7) becomes the consumer of <see cref="visibleEntries"/>; for
        /// now this only keeps the list itself honest.
        /// </summary>
        private void applyFilter(string query)
        {
            visibleEntries = BeatmapFilter.Apply(entries, query);
            carousel.SetEntries(visibleEntries, matchesPreferredSet);
        }

        /// <summary>
        /// Matched by set name rather than by path, because the two sides name
        /// the same set differently — see <see cref="BeatmapLibrary.SetNameOf"/>.
        /// </summary>
        private bool matchesPreferredSet(BeatmapLibraryEntry entry) =>
            preferredSetPath != null
            && !string.IsNullOrEmpty(entry.Path)
            && string.Equals(BeatmapLibrary.SetNameOf(entry.Path), BeatmapLibrary.SetNameOf(preferredSetPath),
                StringComparison.OrdinalIgnoreCase);

        protected override void Update()
        {
            base.Update();

            float width = DrawWidth;
            float height = DrawHeight;

            // The disc is sized off height alone so its silhouette stays the
            // same shape on any aspect ratio; only how much of the window it
            // leaves for the left column changes.
            float diameter = height * disc_scale;

            wheelArea.Size = new Vector2(diameter);
            wheelArea.Position = new Vector2(width * hub_x, height * hub_y);

            // Left column stops short of where the deck bulges in, so panels
            // and wedges never overlap on a narrow window. Measured against
            // the deck rather than the disc: the turntable reaches further in
            // than the record it carries.
            float deckLeftEdge = width * hub_x - diameter / 2 * (1 + Turntable.DeckOverhang);
            float columnWidth = MathHelper.Clamp(deckLeftEdge - margin * 2, 280, width * 0.46f);

            infoArea.Width = columnWidth;

            searchArea.Width = columnWidth;
            searchArea.Position = new Vector2(margin, -margin);

            // Directly under the info panel, as wide as it: the way into the
            // analyser gets a plate of its own rather than a corner of the
            // panel.
            analyseButton.Position = new Vector2(margin, infoArea.Y + infoArea.DrawHeight + analyse_gap);
            analyseButton.Width = columnWidth;

            // Full height on a large window; on a short one it gives up some
            // height so the cassettes under it still fit and centre.
            analyseButton.Height = MathHelper.Clamp(height * 0.078f,
                AnalyseDeckButton.MinPlateHeight, AnalyseDeckButton.PlateHeight);

            // The panels stack top to bottom: info panel, the analyse plate,
            // cassettes, search bar. The info panel's height is driven by its
            // content and the search bar is pinned to the bottom, so the
            // cassettes get whatever is left in between.
            float difficultyTop = analyseButton.Y + analyseButton.DrawHeight + column_gap;
            float difficultyBottom = height - margin - searchArea.DrawHeight - column_gap;

            difficultyArea.Width = MathHelper.Clamp(columnWidth * 0.58f, 260, 420);

            // When a set has more difficulties than fit, the column shrinks in
            // place. It must never be moved up to make room: its only way out
            // is through the info panel above it, which is how the Easy
            // cassette ended up covering the stat row on a 720p window.
            float available = difficultyBottom - difficultyTop;
            float needed = difficultyColumn.DrawHeight;

            difficultyColumn.Scale = new Vector2(needed > available && needed > 0
                ? MathHelper.Clamp(available / needed, min_cassette_scale, 1f)
                : 1f);

            // Centred in the gap between the info panel and the search bar,
            // rather than hung from the top of it: a short set left a band of
            // empty space above the search bar and none below the panel. The
            // offset never goes negative, so a column that has to shrink to
            // fit still starts right under the panel.
            float shown = needed * difficultyColumn.Scale.Y;

            difficultyArea.Position = new Vector2(margin, difficultyTop + Math.Max(0, (available - shown) / 2));

            // Only flashes once there's a real, playing track to read a beat
            // from — a beatmap alone (audio still loading, or missing) would
            // hold the glow at whatever phase time 0 happens to land on
            // rather than actually pulsing.
            if (selectedBeatmap != null && music.IsPlaying)
                turntable.SetBeatFlash(selectedBeatmap, music.CurrentTime, carousel.SelectionAccent);
            else
                turntable.SetBeatFlash(null, 0, carousel.SelectionAccent);
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            switch (e.Key)
            {
                case osuTK.Input.Key.Down:
                    carousel.SelectRelative(1);
                    return true;

                case osuTK.Input.Key.Up:
                    carousel.SelectRelative(-1);
                    return true;

                case osuTK.Input.Key.Enter:
                case osuTK.Input.Key.KeypadEnter:
                    carousel.ConfirmSelection();
                    return true;

                case osuTK.Input.Key.Escape:
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
            // Back to the menu, the song carries on there. Anywhere else —
            // the tape deck after a generated map, say — it stops.
            if (e.Next is not MainMenuScreen || sharedMusic == null)
                stopPreview();

            this.FadeOut(200, Easing.OutQuint);

            return base.OnExiting(e);
        }

        public override void OnSuspending(ScreenTransitionEvent e)
        {
            base.OnSuspending(e);

            if (e.Next is PlayCueScreen)
            {
                // The cue screen carries the song on, muffled, and its record
                // is taking the wheel's place: this steps back under it.
                this.FadeOut(450, Easing.InOutSine);
                return;
            }

            // Anything else — the analyser — plays its own audio; the preview
            // would otherwise keep running underneath it.
            stopPreview();
        }

        public override void OnResuming(ScreenTransitionEvent e)
        {
            base.OnResuming(e);

            // Back from the cue screen, played or not: in view again, and the
            // music open again. Slow and even, to come up underneath the
            // record as it glides back onto the wheel.
            this.FadeIn(900, Easing.InOutSine);
            music.SetMuffled(false);

            // Back from PlayerScreen to the same selection — pick the preview
            // back up rather than leaving the wheel silent.
            if (carousel.Selection is { } entry)
                playSong(entry);

            // A run just finished may have set a new best on this difficulty.
            if (selectedBeatmap != null)
                infoPanel.SetHighScore(highScores?.Get(selectedBeatmap.ContentHash));
        }
    }
}
