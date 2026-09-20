using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Transforms;
using osu.Framework.IO.Stores;
using osu.Framework.Platform;
using OsuClient.Game.Beatmaps;

namespace OsuClient.Game.Screens.MainMenu
{
    /// <summary>
    /// The song playing behind the main menu: one picked at random from the
    /// library on entry, looping (MENU_REDESIGN_PLAN.md step 1).
    ///
    /// Everything else on the menu reads its beat, its spectrum and its
    /// metadata from here, so the whole screen goes quiet and still — rather
    /// than breaking — when the library is empty or has no playable audio.
    /// Callers must cope with <see cref="Beatmap"/> and <see cref="Track"/>
    /// both being null; that is the normal state on a fresh clone with no
    /// generated maps.
    ///
    /// A <see cref="Component"/> rather than a plain class so it gets the
    /// audio manager and host through DI and loads on the screen's background
    /// load thread — decoding a several-minute MP3 on the update thread would
    /// stall the menu's fade-in.
    /// </summary>
    public partial class MenuTrack : Component
    {
        /// <summary>
        /// Volume while the menu is just sitting there — deliberately low, so
        /// that opening the strip has somewhere to build up to.
        /// </summary>
        public const double IdleVolume = 0.18;

        /// <summary>Volume once the menu has been opened.</summary>
        public const double ActiveVolume = 0.6;

        private readonly string? requestedDirectory;
        private readonly Random random;

        [Resolved]
        private AudioManager audio { get; set; } = null!;

        [Resolved]
        private GameHost host { get; set; } = null!;

        /// <summary>The set being played, or null when nothing could be played.</summary>
        public BeatmapLibraryEntry? Entry { get; private set; }

        /// <summary>
        /// The difficulty whose timing points drive the beat pulse. Any
        /// difficulty of the set would do — they all share one audio file and
        /// one set of timing points — so this is just the first.
        /// </summary>
        public Beatmap? Beatmap { get; private set; }

        /// <summary>
        /// The playing track, or null when there's nothing to play. Exposed
        /// because the spectrum ring reads <c>CurrentAmplitudes</c> off it
        /// directly.
        /// </summary>
        public Track? Track { get; private set; }

        /// <summary>Title of the playing song, or null.</summary>
        public string? Title => nullIfBlank(Beatmap?.Metadata.Title);

        /// <summary>Artist of the playing song, or null.</summary>
        public string? Artist => nullIfBlank(Beatmap?.Metadata.Artist);

        /// <summary>The playing set's cover image, or null when it has none.</summary>
        public string? BackgroundPath => Entry?.BackgroundPath;

        /// <summary>Position in the track, in milliseconds. 0 when silent.</summary>
        public double CurrentTime => Track?.CurrentTime ?? 0;

        /// <summary>Whether a track was found and is currently running.</summary>
        public bool IsPlaying => Track?.IsRunning == true;

        /// <param name="songsDirectory">
        /// Where to look for songs. Null resolves the same default song select
        /// uses.
        /// </param>
        /// <param name="random">
        /// Injectable so a test gets a predictable song. Null uses
        /// <see cref="Random.Shared"/>.
        /// </param>
        public MenuTrack(string? songsDirectory = null, Random? random = null)
        {
            requestedDirectory = songsDirectory;
            this.random = random ?? Random.Shared;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            string? directory = requestedDirectory ?? BeatmapLibrary.ResolveDefaultSongsDirectory();

            Entry = ChooseSong(BeatmapLibrary.Load(directory), random);

            if (Entry == null)
                return;

            Beatmap = Entry.Difficulties.FirstOrDefault();

            loadTrack();
        }

        /// <summary>
        /// Whether an entry can actually be played: loaded cleanly, and its
        /// audio resolved to a file that exists.
        ///
        /// <see cref="BeatmapSet.AudioPath"/> is only filled in for sets read
        /// from an unpacked folder — a set still packed in its <c>.osz</c>
        /// has no audio path to hand out, so it can't be the menu's song.
        /// The backend writes both shapes side by side, so in practice this
        /// filters nothing out; it matters for a library someone assembled by
        /// hand from bare <c>.osz</c> files.
        /// </summary>
        private static bool hasPlayableAudio(BeatmapLibraryEntry entry) =>
            entry.IsValid
            && entry.Difficulties.Count > 0
            && entry.Set?.AudioPath is { } path
            && File.Exists(path);

        private void loadTrack()
        {
            string? path = Entry?.Set?.AudioPath;

            if (path == null)
                return;

            string? directory = Path.GetDirectoryName(path);

            if (directory == null)
                return;

            // The song lives in the beatmap's own folder rather than the
            // game's resources, so it needs a store rooted there — the same
            // route song select's preview and PlayerScreen both load through.
            var storage = host.GetStorage(directory);
            var store = new StorageBackedResourceStore(storage);

            Track = audio.GetTrackStore(store).Get(Path.GetFileName(path));

            if (Track == null)
                return;

            Track.Volume.Value = IdleVolume;
            Track.Looping = true;
        }

        /// <summary>
        /// Starts (or restarts) playback from the song's most interesting
        /// point. Safe to call when there's no track.
        /// </summary>
        public void Start()
        {
            if (Track == null || Track.IsRunning)
                return;

            // A generated map's first seconds are often near-silence, and a
            // menu whose logo doesn't move for ten seconds reads as broken.
            // Dropping into the middle of the mapped span is what song select
            // previews do, for the same reason.
            if (Track.CurrentTime <= 0)
                Track.Seek(previewStart());

            Track.Start();
        }

        /// <summary>
        /// Stops playback, keeping the position so <see cref="Start"/> picks
        /// the same song back up. Safe to call repeatedly.
        /// </summary>
        public void Stop() => Track?.Stop();

        /// <summary>
        /// Rides the volume to <paramref name="target"/> over
        /// <paramref name="duration"/> milliseconds.
        ///
        /// The menu opens quietly and swells when the strip does, so arriving
        /// at the client feels like something starting rather than like a
        /// song already in progress. Transformed on this component rather
        /// than set outright, so the change is heard as a rise.
        /// </summary>
        public void FadeVolumeTo(double target, double duration)
        {
            if (Track == null)
                return;

            this.TransformBindableTo(Track.Volume, target, duration, Easing.OutQuint);
        }

        private double previewStart()
        {
            if (Beatmap == null || Beatmap.LastHitObjectTime <= Beatmap.FirstHitObjectTime)
                return 0;

            return Beatmap.FirstHitObjectTime
                   + (Beatmap.LastHitObjectTime - Beatmap.FirstHitObjectTime) * 0.5;
        }

        private static string? nullIfBlank(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value;

        protected override void Dispose(bool isDisposing)
        {
            // Stopped but not disposed, matching how song select drops a
            // preview track. One track exists per session here, and the audio
            // manager owns the store it came from.
            Track?.Stop();

            base.Dispose(isDisposing);
        }

        /// <summary>
        /// Picks the song the menu would play from an already-loaded library,
        /// without touching audio or the filesystem beyond an existence check.
        /// Split out so the selection rule is testable on its own.
        /// </summary>
        public static BeatmapLibraryEntry? ChooseSong(IReadOnlyList<BeatmapLibraryEntry> library, Random random)
        {
            var playable = library.Where(hasPlayableAudio).ToArray();

            return playable.Length == 0 ? null : playable[random.Next(playable.Length)];
        }
    }
}
