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
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.IO.Stores;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Framework.Threading;
using OsuClient.Game.Beatmaps;
using OsuClient.Game.Graphics;
using OsuClient.Game.Screens.Gameplay;
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
    public partial class RetroSongSelectScreen : Screen
    {
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
        private FillFlowContainer difficultyColumn = null!;
        private VinylCarousel carousel = null!;
        private Turntable turntable = null!;
        private Container wheelArea = null!;

        /// <summary>The library filtered by the search box — what the wheel shows.</summary>
        private IReadOnlyList<BeatmapLibraryEntry> visibleEntries = new List<BeatmapLibraryEntry>();

        private readonly List<CassetteButton> cassettes = new List<CassetteButton>();

        /// <summary>The difficulty that would be played — what Enter confirms.</summary>
        private Beatmap? selectedBeatmap;

        private Track? previewTrack;

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

        [BackgroundDependencyLoader]
        private void load()
        {
            songsDirectory = requestedDirectory ?? BeatmapLibrary.ResolveDefaultSongsDirectory();
            entries = BeatmapLibrary.Load(songsDirectory);

            InternalChildren = new Drawable[]
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
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            carousel.SelectionChanged += onSelectionChanged;
            carousel.SelectionConfirmed += onSelectionConfirmed;

            searchBar.QueryChanged += applyFilter;
            applyFilter(string.Empty);
        }

        /// <summary>Turns the record by <paramref name="delta"/> songs.</summary>
        public void SelectRelative(int delta) => carousel.SelectRelative(delta);

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

        private void onSelectionConfirmed(BeatmapLibraryEntry entry)
        {
            if (!this.IsCurrentScreen() || selectedBeatmap == null)
                return;

            this.Push(new PlayerScreen(new BeatmapSelection(entry, selectedBeatmap)));
        }

        /// <summary>
        /// Points the background, info panel and cassette column at one set.
        /// The wheel calls this on every selection change (step 8); until then
        /// it runs once at load.
        /// </summary>
        public void ShowSet(BeatmapLibraryEntry? entry)
        {
            difficultyColumn.Clear();
            cassettes.Clear();

            background.SetBackground(entry?.BackgroundPath);

            if (entry == null || !entry.IsValid)
            {
                selectedBeatmap = null;
                infoPanel.SetBeatmap(null);
                stopPreview();
                return;
            }

            var ordered = entry.Difficulties.OrderBy(DifficultyTier.SortKey).ToList();

            for (int i = 0; i < ordered.Count; i++)
            {
                var button = new CassetteButton(ordered[i], i, ordered.Count);

                button.Action = () => selectDifficulty(button);

                cassettes.Add(button);
                difficultyColumn.Add(button);
            }

            selectDifficulty(cassettes[0]);
            updatePreviewTrack(entry, selectedBeatmap);
        }

        /// <summary>
        /// Plays the selected song's own audio, quietly, under the wheel — a
        /// taste of what the player is about to pick. Starts around the
        /// middle of what's actually mapped rather than the intro: that's
        /// usually the densest, most representative part of the song (often
        /// a chorus), where an intro or outro is more likely to undersell it.
        /// One call per song selected, not per difficulty click — difficulties
        /// in a set share the same audio, so switching between them has
        /// nothing new to preview.
        /// </summary>
        private void updatePreviewTrack(BeatmapLibraryEntry entry, Beatmap? beatmap)
        {
            stopPreview();

            string? path = entry.Set?.AudioPath;

            if (path == null || !File.Exists(path))
                return;

            string? directory = Path.GetDirectoryName(path);

            if (directory == null)
                return;

            // The track lives in the beatmap's own folder, not in the game's
            // resources, so it needs a store rooted there — same route
            // Gameplay/PlayerScreen.cs loads a song through.
            var storage = host.GetStorage(directory);
            var store = new StorageBackedResourceStore(storage);

            previewTrack = audio.GetTrackStore(store).Get(Path.GetFileName(path));

            if (previewTrack == null)
                return;

            previewTrack.Volume.Value = preview_volume;
            previewTrack.Looping = true;

            double start = beatmap != null && beatmap.LastHitObjectTime > beatmap.FirstHitObjectTime
                ? beatmap.FirstHitObjectTime + (beatmap.LastHitObjectTime - beatmap.FirstHitObjectTime) * 0.5
                : 0;

            previewTrack.Seek(start);
            previewTrack.Start();
        }

        private void stopPreview()
        {
            previewTrack?.Stop();
            previewTrack = null;
        }

        private void selectDifficulty(CassetteButton button)
        {
            foreach (var cassette in cassettes)
                cassette.Selected = ReferenceEquals(cassette, button);

            selectedBeatmap = button.Beatmap;
            infoPanel.SetBeatmap(button.Beatmap, button.Accent);
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

            // The three panels stack top to bottom with a fixed gap: info
            // panel, cassettes, search bar. The info panel's height is driven
            // by its content and the search bar is pinned to the bottom, so
            // the cassettes get whatever is left in between.
            float difficultyTop = infoArea.Y + infoArea.DrawHeight + column_gap;
            float difficultyBottom = height - margin - searchArea.DrawHeight - column_gap;

            difficultyArea.Position = new Vector2(margin, difficultyTop);
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

            // Only flashes once there's a real, playing track to read a beat
            // from — a beatmap alone (audio still loading, or missing) would
            // hold the glow at whatever phase time 0 happens to land on
            // rather than actually pulsing.
            if (selectedBeatmap != null && previewTrack != null)
                turntable.SetBeatFlash(selectedBeatmap, previewTrack.CurrentTime, carousel.SelectionAccent);
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
            stopPreview();

            this.FadeOut(200, Easing.OutQuint);

            return base.OnExiting(e);
        }

        public override void OnSuspending(ScreenTransitionEvent e)
        {
            base.OnSuspending(e);

            // Confirming a selection pushes PlayerScreen, which starts the
            // same song's real playback — the preview would otherwise keep
            // running underneath it.
            stopPreview();
        }

        public override void OnResuming(ScreenTransitionEvent e)
        {
            base.OnResuming(e);

            // Back from PlayerScreen to the same selection — pick the preview
            // back up rather than leaving the wheel silent.
            if (carousel.Selection is { } entry)
                updatePreviewTrack(entry, selectedBeatmap);
        }
    }
}
