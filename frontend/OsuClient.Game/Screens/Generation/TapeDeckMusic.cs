using System;
using System.IO;
using ManagedBass.Fx;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Mixing;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics;
using osu.Framework.IO.Stores;
using osu.Framework.Platform;

namespace OsuClient.Game.Screens.Generation
{
    /// <summary>
    /// The menu's song, carried on into the tape deck as a worn-down remix
    /// of itself: slowed a little, low-passed to a muffle, thinned at the
    /// bottom, drowned in a large reverb and kept quiet — the same song heard
    /// through the wall of the room the deck is in.
    ///
    /// <para>
    /// It plays through a mixer of its own, so the effects touch this track
    /// and nothing else: the deck's own sounds and the rest of the game stay
    /// dry. Nothing here streams from the menu: it opens the same audio file
    /// and seeks to where the menu had got to, so the song carries on rather
    /// than starting over.
    /// </para>
    /// </summary>
    public partial class TapeDeckMusic : Component
    {
        /// <summary>Well under the menu's 0.6: a backdrop to a form, not a song to listen to.</summary>
        public const double Volume = 0.22;

        /// <summary>
        /// Playback rate. Slowing the tape lowers its pitch with it — the
        /// sag of a slowed remix, and of a deck running slightly slow.
        /// </summary>
        public const double Rate = 0.92;

        private const double fade_in = 1500;

        /// <summary>
        /// Short enough to finish inside the deck's own fade when it leaves:
        /// a screen stops updating once that is done, and a volume ride
        /// frozen halfway would leave the song playing on underneath.
        /// </summary>
        private const double fade_out = 200;

        private readonly string? audioPath;
        private readonly double startTime;

        [Resolved]
        private AudioManager audio { get; set; } = null!;

        [Resolved]
        private GameHost host { get; set; } = null!;

        private AudioMixer? mixer;

        /// <summary>Whether it should be playing: between <see cref="Start"/> and <see cref="Stop"/>.</summary>
        private bool wanted;

        /// <param name="audioPath">The menu song's audio file, or null for silence.</param>
        /// <param name="startTime">Where the menu had got to in it, in milliseconds.</param>
        public TapeDeckMusic(string? audioPath, double startTime)
        {
            this.audioPath = audioPath;
            this.startTime = startTime;
        }

        /// <summary>The playing track, or null when there is none. Exposed for tests.</summary>
        public Track? Track { get; private set; }

        /// <summary>Whether it is playing. Exposed for tests.</summary>
        public bool IsPlaying => Track?.IsRunning == true;

        [BackgroundDependencyLoader]
        private void load()
        {
            if (string.IsNullOrEmpty(audioPath) || !File.Exists(audioPath))
                return;

            string? directory = Path.GetDirectoryName(audioPath);

            if (directory == null)
                return;

            mixer = audio.CreateAudioMixer("tape-deck");

            // The muffle: everything above about 1 kHz rolled away, as
            // through a wall or off a tired tape.
            mixer.AddEffect(new BQFParameters
            {
                lFilter = BQFType.LowPass,
                fCenter = 1000,
                fQ = 0.7f,
            }, 0);

            // And the bottom thinned out, as from a small speaker — without
            // it the muffle turns into a boom under the deck's own sounds.
            mixer.AddEffect(new BQFParameters
            {
                lFilter = BQFType.HighPass,
                fCenter = 120,
                fQ = 0.7f,
            }, 1);

            // A big, soft room: most of what is heard is the reflections.
            mixer.AddEffect(new ReverbParameters
            {
                fDryMix = 0.6f,
                fWetMix = 0.5f,
                fRoomSize = 0.85f,
                fDamp = 0.55f,
                fWidth = 1f,
            }, 2);

            // The same route the menu and song select load a song's audio by:
            // a store rooted at the beatmap's own folder.
            var store = new StorageBackedResourceStore(host.GetStorage(directory));

            Track = audio.GetTrackStore(store, mixer).Get(Path.GetFileName(audioPath));

            if (Track == null)
                return;

            Track.Looping = true;
            Track.Frequency.Value = Rate;
            Track.Volume.Value = 0;
        }

        /// <summary>Starts, or picks back up, and fades in. Safe to call with no track.</summary>
        public void Start()
        {
            if (Track == null)
                return;

            wanted = true;
            ensureRunning();

            this.TransformBindableTo(Track.Volume, Volume, fade_in, Easing.OutQuad);
        }

        protected override void Update()
        {
            base.Update();

            // A start can be lost: the mixer this plays through is created on
            // the audio thread, and a track started before that mixer is
            // ready never begins — which left the deck silent every so often.
            // Until it reports running, it is asked again each frame.
            if (wanted)
                ensureRunning();
        }

        private void ensureRunning()
        {
            if (Track == null || Track.IsRunning)
                return;

            // First start: carry on from where the menu was. After that,
            // from wherever this left off.
            if (Track.CurrentTime <= 0 && startTime > 0)
                Track.Seek(startTime);

            Track.Start();
        }

        /// <summary>Fades out and stops, keeping the position for <see cref="Start"/>.</summary>
        public void Stop()
        {
            if (Track == null)
                return;

            wanted = false;

            var track = Track;

            this.TransformBindableTo(track.Volume, 0, fade_out, Easing.OutQuad)
                .OnComplete(_ => track.Stop());
        }

        protected override void Dispose(bool isDisposing)
        {
            Track?.Stop();
            mixer?.Dispose();

            base.Dispose(isDisposing);
        }
    }
}
