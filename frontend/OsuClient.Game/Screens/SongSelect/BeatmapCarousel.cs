using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using OsuClient.Game.Beatmaps;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.SongSelect
{
    /// <summary>A chosen difficulty, together with the set it came from.</summary>
    public class BeatmapSelection
    {
        public BeatmapLibraryEntry Entry { get; }

        public Beatmap Beatmap { get; }

        public BeatmapSelection(BeatmapLibraryEntry entry, Beatmap beatmap)
        {
            Entry = entry;
            Beatmap = beatmap;
        }
    }

    /// <summary>
    /// The scrollable list of beatmap sets, each showing its difficulties.
    ///
    /// The carousel does no file IO of its own — it renders whatever list of
    /// <see cref="BeatmapLibraryEntry"/> it is given, so it can be exercised in
    /// a visual test with in-memory data.
    /// </summary>
    public partial class BeatmapCarousel : CompositeDrawable
    {
        /// <summary>Fired when the highlighted difficulty changes.</summary>
        public Action<BeatmapSelection>? SelectionChanged;

        /// <summary>
        /// Fired when a difficulty is confirmed — clicking the already-selected
        /// one. Song select turns this into a push to gameplay.
        /// </summary>
        public Action<BeatmapSelection>? SelectionConfirmed;

        private readonly FillFlowContainer panels;
        private readonly BasicScrollContainer scroll;
        private readonly SpriteText emptyMessage;

        private readonly List<DifficultyIcon> icons = new List<DifficultyIcon>();

        /// <summary>The currently highlighted difficulty, or null if nothing is selected.</summary>
        public BeatmapSelection? Selection { get; private set; }

        public BeatmapCarousel()
        {
            RelativeSizeAxes = Axes.Both;

            InternalChildren = new Drawable[]
            {
                scroll = new BasicScrollContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    ScrollbarVisible = true,
                    Child = panels = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 10),
                        Padding = new MarginPadding(14),
                    },
                },
                emptyMessage = new SpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Font = FontUsage.Default.With(size: 18),
                    Colour = new Color4(0.7f, 0.7f, 0.78f, 1f),
                    Alpha = 0,
                },
            };
        }

        /// <summary>
        /// Replaces the carousel contents. <paramref name="emptyText"/> is shown
        /// when there is nothing to list.
        /// </summary>
        public void SetEntries(IReadOnlyList<BeatmapLibraryEntry> entries, string emptyText)
        {
            panels.Clear();
            icons.Clear();
            entriesByIcon.Clear();
            Selection = null;

            emptyMessage.Text = emptyText;
            emptyMessage.Alpha = entries.Count == 0 ? 1 : 0;

            foreach (var entry in entries)
                panels.Add(createPanel(entry));

            scroll.ScrollToStart(false);

            // Start on the first playable difficulty so the details panel and
            // the Enter shortcut have something to act on immediately.
            var firstIcon = icons.FirstOrDefault();

            if (firstIcon != null)
                Select(firstIcon, false);
        }

        private Drawable createPanel(BeatmapLibraryEntry entry)
        {
            var content = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 8),
                Padding = new MarginPadding { Horizontal = 16, Vertical = 14 },
            };

            content.Add(new SpriteText
            {
                Text = entry.DisplayName,
                Font = FontUsage.Default.With(size: 20),
                Colour = entry.IsValid ? Color4.White : new Color4(1f, 0.55f, 0.55f, 1f),
            });

            if (entry.IsValid)
            {
                var first = entry.Difficulties[0];

                content.Add(new SpriteText
                {
                    Text = $"mapped by {first.Metadata.Creator}   •   {first.BPM:0.#} BPM   •   " +
                           $"{entry.Difficulties.Count} difficult{(entry.Difficulties.Count == 1 ? "y" : "ies")}",
                    Font = FontUsage.Default.With(size: 13),
                    Colour = new Color4(0.65f, 0.65f, 0.74f, 1f),
                });

                var row = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Full,
                    Spacing = new Vector2(8, 8),
                };

                foreach (var beatmap in entry.Difficulties)
                {
                    var icon = new DifficultyIcon(beatmap);

                    icon.Action = () =>
                    {
                        // Clicking the already-selected difficulty confirms it.
                        if (icon.Selected)
                            SelectionConfirmed?.Invoke(new BeatmapSelection(entry, beatmap));
                        else
                            Select(icon, true);
                    };

                    icons.Add(icon);
                    entriesByIcon[icon] = entry;
                    row.Add(icon);
                }

                content.Add(row);
            }
            else
            {
                // A broken file stays visible with the reason attached, rather
                // than silently vanishing from the list.
                content.Add(new SpriteText
                {
                    Text = "failed to load",
                    Font = FontUsage.Default.With(size: 14),
                    Colour = new Color4(1f, 0.45f, 0.45f, 1f),
                });

                content.Add(new TextFlowContainer(t =>
                    {
                        t.Font = FontUsage.Default.With(size: 12);
                        t.Colour = new Color4(0.8f, 0.6f, 0.6f, 1f);
                    })
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Text = entry.Error ?? "unknown error",
                    });
            }

            return new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Masking = true,
                CornerRadius = 8,
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = entry.IsValid
                            ? new Color4(1f, 1f, 1f, 0.05f)
                            : new Color4(0.6f, 0.15f, 0.15f, 0.25f),
                    },
                    content,
                },
            };
        }

        private readonly Dictionary<DifficultyIcon, BeatmapLibraryEntry> entriesByIcon =
            new Dictionary<DifficultyIcon, BeatmapLibraryEntry>();

        /// <summary>Highlights a difficulty and notifies listeners.</summary>
        private void Select(DifficultyIcon icon, bool scrollIntoView)
        {
            if (!entriesByIcon.TryGetValue(icon, out var entry))
                return;

            foreach (var other in icons)
                other.Selected = ReferenceEquals(other, icon);

            Selection = new BeatmapSelection(entry, icon.Beatmap);

            if (scrollIntoView)
                scroll.ScrollIntoView(icon);

            SelectionChanged?.Invoke(Selection);
        }

        /// <summary>Moves the highlight by <paramref name="delta"/> places.</summary>
        public void SelectRelative(int delta)
        {
            if (icons.Count == 0)
                return;

            int current = Selection == null
                ? -1
                : icons.FindIndex(i => ReferenceEquals(i.Beatmap, Selection.Beatmap));

            int next = Math.Clamp(current + delta, 0, icons.Count - 1);

            Select(icons[next], true);
        }

        /// <summary>Confirms the current selection, if any.</summary>
        public void ConfirmSelection()
        {
            if (Selection != null)
                SelectionConfirmed?.Invoke(Selection);
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            switch (e.Key)
            {
                case osuTK.Input.Key.Down:
                    SelectRelative(1);
                    return true;

                case osuTK.Input.Key.Up:
                    SelectRelative(-1);
                    return true;
            }

            return base.OnKeyDown(e);
        }
    }
}
