using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Screens;
using osu.Framework.Testing;
using OsuClient.Game.Graphics;
using OsuClient.Game.Screens.Gameplay;
using OsuClient.Game.Screens.MainMenu;
using OsuClient.Game.Screens.SongSelect;

namespace OsuClient.Tests.Visual
{
    /// <summary>
    /// Between choosing a song and playing it: the record lifts off the
    /// wheel into the cue screen with the music muffled, lands in the middle,
    /// and plays into gameplay when clicked — or goes back on Escape.
    /// </summary>
    [TestFixture]
    public partial class TestScenePlayCue : TestScene
    {
        private CueHost host = null!;
        private RetroSongSelectScreen songSelect = null!;

        [SetUpSteps]
        public void SetUpSteps()
        {
            AddStep("open song select on a small library", () =>
            {
                string songs = Path.Combine(Path.GetTempPath(), "osuclient-cue-" + Path.GetRandomFileName());

                foreach (string name in new[] { "Alpha", "Bravo" })
                    TestSceneSharedMusic.WriteSong(songs, name);

                Child = host = new CueHost(songs);
                host.Stack.Push(songSelect = new RetroSongSelectScreen(songs));
            });

            AddUntilStep("song select ready", () => songSelect.IsCurrentScreen() && host.Music.IsPlaying);
        }

        private PlayCueScreen cue => (PlayCueScreen)host.Stack.CurrentScreen;

        private void confirm()
        {
            AddStep("confirm the song", () => songSelect.ConfirmSelection());
            AddUntilStep("cue screen up", () => host.Stack.CurrentScreen is PlayCueScreen);
        }

        [Test]
        public void TestChoosingASongCuesItUpWithTheMusicMuffled()
        {
            confirm();

            AddAssert("music muffled", () => host.Music.Muffled);
            AddAssert("still playing under it", () => host.Music.IsPlaying);

            AddUntilStep("record lands", () => cue.Landed);
            AddAssert("in the middle", () =>
                Math.Abs(cue.Record.ScreenSpaceDrawQuad.Centre.X - cue.ScreenSpaceDrawQuad.Centre.X) < 2);
        }

        [Test]
        public void TestEscapePutsItBack()
        {
            PlayCueScreen leaving = null!;
            osuTK.Vector2 wheelCentre = default;

            AddStep("note where the wheel is", () =>
                wheelCentre = songSelect.ChildrenOfType<VinylCarousel>().Single().ScreenSpaceDrawQuad.Centre);

            confirm();
            AddUntilStep("record lands", () => cue.Landed);

            AddStep("escape", () =>
            {
                leaving = cue;
                leaving.Exit();
            });

            AddUntilStep("back on song select", () => songSelect.IsCurrentScreen());

            // The way back is the way in: the record glides back onto the
            // wheel it came off, rather than vanishing where it was.
            AddUntilStep("the record goes back onto the wheel", () =>
                osuTK.Vector2.Distance(leaving.Record.ScreenSpaceDrawQuad.Centre, wheelCentre) < 3);
            AddAssert("music open again", () => !host.Music.Muffled);
            AddAssert("and playing", () => host.Music.IsPlaying);
        }

        [Test]
        public void TestPlayingTheRecordStartsGameplayAndComesBackToSongSelect()
        {
            confirm();
            AddUntilStep("record lands", () => cue.Landed);

            AddStep("play the record", () => cue.Launch());
            AddAssert("the picture starts to clear", () => cue.Revealing);
            AddUntilStep("gameplay arrives", () => host.Stack.CurrentScreen is PlayerScreen);
            AddAssert("menu music out of the way", () => !host.Music.IsPlaying);

            // As results do on the way out.
            AddStep("leave gameplay", () => host.Stack.CurrentScreen.Exit());
            AddUntilStep("the cue steps aside to song select", () => songSelect.IsCurrentScreen());
        }

        /// <summary>A screen stack with the game's shared music and ripple cached above it.</summary>
        private partial class CueHost : CompositeDrawable
        {
            [Cached]
            public readonly MenuTrack Music;

            [Cached]
            public readonly ScreenRipple Ripple = new ScreenRipple();

            public readonly ScreenStack Stack = new ScreenStack { RelativeSizeAxes = Axes.Both };

            public CueHost(string songs)
            {
                RelativeSizeAxes = Axes.Both;
                Music = new MenuTrack(songs, new Random(1), pickOnLoad: false);

                InternalChildren = new Drawable[] { Music, Stack, Ripple };
            }
        }
    }
}
