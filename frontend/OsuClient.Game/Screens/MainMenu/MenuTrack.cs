using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ManagedBass.Fx;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Mixing;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Transforms;
using osu.Framework.IO.Stores;
using osu.Framework.Platform;
using OsuClient.Game.Beatmaps;

namespace OsuClient.Game.Screens.MainMenu
{
    /// <summary>
    /// The music playing outside of gameplay: one song at a time, shared by
    /// the main menu and song select so that moving between them never
    /// restarts or reloads what is playing (MENU_REDESIGN_PLAN.md step 1).
    ///
    /// <para>
    /// The game owns one and caches it. On the menu it plays through the
    /// library — when a song ends, another starts and <see cref="SongChanged"/>
    /// fires so the credit, beat and spectrum follow. Song select asks for the
    /// selected song with <see cref="Play"/>: the song already playing just
    /// carries on, and a different one starts from its preview point and
    /// loops. Whatever song select was on is still playing when the menu comes
    /// back, and the menu carries on with it.
    /// </para>
    ///
    /// <para>
    /// Everything else on the menu reads its beat, its spectrum and its
    /// metadata from here, so the whole screen goes quiet and still — rather
    /// than breaking — when the library is empty or has no playable audio.
    /// Callers must cope with <see cref="Beatmap"/> and <see cref="Track"/>
    /// both being null; that is the normal state on a fresh clone with no
    /// generated maps.
    /// </para>
    ///
    /// <para>
    /// A screen that runs outside the game — a test, a screenshot scene —
    /// finds none cached and makes its own, which behaves the same apart from
    /// not being shared.
    /// </para>
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

        /// <summary>How long a new song takes to fade up, so a change is heard as a change and not a cut.</summary>
        private const double song_fade_in = 600;

        private readonly string? requestedDirectory;
        private readonly bool pickOnLoad;
        private readonly Random random;

        /// <summary>
        /// The music's own mixer, so it can be muffled without muffling
        /// anything else — gameplay's track and every sound effect play
        /// through the global one.
        /// </summary>
        private AudioMixer? mixer;

        /// <summary>The muffle: a low-pass whose cutoff rides between open and closed.</summary>
        private readonly BQFParameters muffleFilter = new BQFParameters
        {
            lFilter = BQFType.LowPass,
            fCenter = open_cutoff,
            fQ = 0.7f,
        };

        private const float open_cutoff = 20000;
        private const float muffled_cutoff = 600;

        private bool muffleApplied;
        private float muffleTarget = open_cutoff;

        /// <summary>
        /// Whether it should be playing: between a start and a stop. A start
        /// on a mixer that has only just been created can be lost, so until
        /// the track reports running it is asked again each frame.
        /// </summary>
        private bool wanted;

        [Resolved]
        private AudioManager audio { get; set; } = null!;

        [Resolved]
        private GameHost host { get; set; } = null!;

        private IReadOnlyList<BeatmapLibraryEntry> library = Array.Empty<BeatmapLibraryEntry>();

        private bool autoAdvance = true;

        /// <summary>The volume songs are brought up to: whatever the screen last asked for.</summary>
        private double targetVolume = IdleVolume;

        /// <summary>Raised on the update thread whenever a different song starts.</summary>
        public event Action? SongChanged;

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

        /// <summary>
        /// Whether a song that finishes is followed by another (the menu), or
        /// loops (song select, where the record on screen is that song).
        /// </summary>
        public bool AutoAdvance
        {
            get => autoAdvance;
            set
            {
                autoAdvance = value;

                if (Track != null)
                    Track.Looping = !value;
            }
        }

        /// <param name="songsDirectory">
        /// Where to look for songs. Null resolves the same default song select
        /// uses.
        /// </param>
        /// <param name="random">
        /// Injectable so a test gets a predictable song. Null uses
        /// <see cref="Random.Shared"/>.
        /// </param>
        /// <param name="pickOnLoad">
        /// Whether to choose a random song while loading. Song select's own
        /// stand-in passes false: it is told what to play.
        /// </param>
        public MenuTrack(string? songsDirectory = null, Random? random = null, bool pickOnLoad = true)
        {
            requestedDirectory = songsDirectory;
            this.random = random ?? Random.Shared;
            this.pickOnLoad = pickOnLoad;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            mixer = audio.CreateAudioMixer("menu-music");

            library = BeatmapLibrary.Load(songsDirectory);

            if (!pickOnLoad)
                return;

            // Straight into the fields rather than through switchTo: this is
            // the first song, there is nothing to announce a change from, and
            // this runs on the load thread.
            Entry = ChooseSong(library, random);

            if (Entry == null)
                return;

            Beatmap = Entry.Difficulties.FirstOrDefault();
            Track = loadTrack(Entry);

            if (Track != null)
            {
                Track.Volume.Value = IdleVolume;
                Track.Looping = !autoAdvance;
            }
        }

        private string? songsDirectory => requestedDirectory ?? BeatmapLibrary.ResolveDefaultSongsDirectory();

        /// <summary>
        /// Re-reads the library in the background, so the menu's shuffle
        /// picks up maps generated since the game started. The song playing
        /// is not touched.
        /// </summary>
        public void RefreshLibrary()
        {
            Task.Run(() => BeatmapLibrary.Load(songsDirectory))
                .ContinueWith(t =>
                {
                    if (t.IsCompletedSuccessfully)
                        Schedule(() => library = t.Result);
                });
        }

        /// <summary>
        /// Plays <paramref name="entry"/>. If it is the song already playing
        /// — the same set, however it was looked up — it simply carries on,
        /// uninterrupted; otherwise it starts from its preview point.
        /// </summary>
        public void Play(BeatmapLibraryEntry entry)
        {
            wanted = true;

            if (isSameSet(entry, Entry) && Track != null)
            {
                if (!Track.IsRunning)
                    Track.Start();

                return;
            }

            switchTo(entry, previewStart(entry.Difficulties.FirstOrDefault()));
        }

        /// <summary>
        /// Starts (or restarts) playback. Picks up where it stopped; a song
        /// never started begins from its most interesting point. Safe to call
        /// when there's no track.
        /// </summary>
        public void Start()
        {
            wanted = true;

            if (Track == null || Track.IsRunning)
                return;

            // A generated map's first seconds are often near-silence, and a
            // menu whose logo doesn't move for ten seconds reads as broken.
            // Dropping into the middle of the mapped span is what song select
            // previews do, for the same reason.
            if (Track.CurrentTime <= 0)
                Track.Seek(previewStart(Beatmap));

            Track.Start();
        }

        /// <summary>
        /// Stops playback, keeping the position so <see cref="Start"/> picks
        /// the same song back up. Safe to call repeatedly.
        /// </summary>
        public void Stop()
        {
            wanted = false;
            Track?.Stop();
        }

        /// <summary>Whether the music is muffled, or on its way there. Exposed for tests.</summary>
        public bool Muffled => muffleTarget < open_cutoff;

        /// <summary>
        /// Muffles the music, as if heard from the next room — for the moment
        /// before a song is played, when it steps back but carries on — or
        /// opens it up again. The cutoff sweeps rather than switching, so it
        /// is heard closing in and opening out.
        /// </summary>
        public void SetMuffled(bool muffled) => muffleTarget = muffled ? muffled_cutoff : open_cutoff;

        /// <summary>
        /// Rides the volume to <paramref name="target"/> over
        /// <paramref name="duration"/> milliseconds, and keeps it as the level
        /// later songs come up to.
        ///
        /// The menu opens quietly and swells when the strip does, so arriving
        /// at the client feels like something starting rather than like a
        /// song already in progress. Transformed on this component rather
        /// than set outright, so the change is heard as a rise.
        /// </summary>
        public void FadeVolumeTo(double target, double duration)
        {
            targetVolume = target;

            if (Track == null)
                return;

            this.TransformBindableTo(Track.Volume, target, duration, Easing.OutQuint);
        }

        protected override void Update()
        {
            base.Update();

            // The menu plays through the library: a song that has run out is
            // followed by another, never the same one twice running.
            if (autoAdvance && Track is { HasCompleted: true })
                playNext();
            else if (wanted && Track is { IsRunning: false, HasCompleted: false })
                Track.Start();

            updateMuffle();
        }

        private void updateMuffle()
        {
            if (mixer == null || muffleFilter.fCenter == muffleTarget && muffleApplied == muffleTarget < open_cutoff)
                return;

            if (!muffleApplied && muffleTarget < open_cutoff)
            {
                mixer.AddEffect(muffleFilter);
                muffleApplied = true;
            }

            // Eased in log-frequency, which is how a filter sweep is heard:
            // a linear slide spends almost all its time in the top octave.
            float current = MathF.Log(muffleFilter.fCenter);
            float target = MathF.Log(muffleTarget);
            float step = (float)Math.Min(1, Time.Elapsed / 1000 * 6);

            float next = current + (target - current) * step;

            if (MathF.Abs(next - target) < 0.02f)
                next = target;

            muffleFilter.fCenter = MathF.Exp(next);

            if (muffleApplied && muffleFilter.fCenter >= open_cutoff)
            {
                // Fully open: take the filter out entirely rather than leave a
                // low-pass at 20 kHz in the chain.
                mixer.RemoveEffect(muffleFilter);
                muffleApplied = false;
                return;
            }

            if (muffleApplied)
                mixer.UpdateEffect(muffleFilter);
        }

        private void playNext()
        {
            var next = ChooseSong(library.Where(e => !isSameSet(e, Entry)).ToList(), random)
                       ?? ChooseSong(library, random);

            if (next == null)
            {
                Track?.Stop();
                return;
            }

            // From the top: this is a song following another, not a preview.
            switchTo(next, 0);
        }

        private void switchTo(BeatmapLibraryEntry entry, double startTime)
        {
            wanted = true;

            var previous = Track;

            var track = loadTrack(entry);

            if (track == null)
                return;

            // The old song stops and lets go of its volume ride before the new
            // one takes over, so a transform left over from it cannot reach
            // across into the new track.
            if (previous != null)
            {
                ClearTransforms();
                previous.Stop();
            }

            Entry = entry;
            Beatmap = entry.Difficulties.FirstOrDefault();
            Track = track;

            track.Looping = !autoAdvance;
            track.Volume.Value = 0;
            track.Seek(startTime);
            track.Start();

            this.TransformBindableTo(track.Volume, targetVolume, song_fade_in, Easing.OutQuad);

            SongChanged?.Invoke();
        }

        /// <summary>
        /// Two lookups of the same set. The menu and song select each load
        /// the library themselves, so the same song arrives as two different
        /// objects — compared by set name, the way the rest of the client
        /// matches sets (see <see cref="BeatmapLibrary.SetNameOf"/>).
        /// </summary>
        private static bool isSameSet(BeatmapLibraryEntry? a, BeatmapLibraryEntry? b) =>
            a != null && b != null
            && (ReferenceEquals(a, b)
                || string.Equals(BeatmapLibrary.SetNameOf(a.Path), BeatmapLibrary.SetNameOf(b.Path),
                    StringComparison.OrdinalIgnoreCase));

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

        private Track? loadTrack(BeatmapLibraryEntry entry)
        {
            string? path = entry.Set?.AudioPath;

            if (path == null || !File.Exists(path))
                return null;

            string? directory = Path.GetDirectoryName(path);

            if (directory == null)
                return null;

            // The song lives in the beatmap's own folder rather than the
            // game's resources, so it needs a store rooted there — the same
            // route PlayerScreen loads a song through.
            var storage = host.GetStorage(directory);
            var store = new StorageBackedResourceStore(storage);

            return audio.GetTrackStore(store, mixer).Get(Path.GetFileName(path));
        }

        private static double previewStart(Beatmap? beatmap)
        {
            if (beatmap == null || beatmap.LastHitObjectTime <= beatmap.FirstHitObjectTime)
                return 0;

            return beatmap.FirstHitObjectTime
                   + (beatmap.LastHitObjectTime - beatmap.FirstHitObjectTime) * 0.5;
        }

        private static string? nullIfBlank(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value;

        protected override void Dispose(bool isDisposing)
        {
            // Stopped but not disposed, matching how song select drops a
            // preview track. The audio manager owns the store it came from.
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
