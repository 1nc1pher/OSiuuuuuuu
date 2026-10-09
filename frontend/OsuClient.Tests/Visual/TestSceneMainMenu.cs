using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osu.Framework.Testing.Input;
using OsuClient.Game.Graphics;
using OsuClient.Game.Screens.MainMenu;
using osuTK;
using osuTK.Graphics;
using osuTK.Input;

namespace OsuClient.Tests.Visual
{
    /// <summary>
    /// The main menu, driven interactively.
    ///
    /// The parts worth poking at by hand are the ones a captured frame can't
    /// show: the strip opening and closing repeatedly — including reversals
    /// mid-animation, which is how a half-open strip would show up — and the
    /// spectrum ring fed a moving signal rather than whatever the song
    /// happens to be playing at the moment of capture.
    /// </summary>
    [TestFixture]
    public partial class TestSceneMainMenu : osu.Framework.Testing.TestScene
    {
        [Test]
        public void TestMenu()
        {
            MainMenuScreen menu = null!;

            AddStep("load menu", () =>
            {
                var stack = new ScreenStack { RelativeSizeAxes = Axes.Both };
                Child = stack;
                stack.Push(menu = new MainMenuScreen());
            });

            AddUntilStep("menu loaded", () => menu.IsLoaded);

            AddStep("open", () => menu.Expand());
            AddStep("close", () => menu.Collapse());

            // Reversals part-way through the transition: each one finishes
            // whatever is running before starting the next, so hammering it
            // has to settle in one of the two states, never between them.
            AddRepeatStep("hammer open", () => menu.Expand(), 5);
            AddRepeatStep("hammer closed", () => menu.Collapse(), 5);

            AddStep("settle open", () => menu.Expand());
        }

        /// <summary>
        /// Clicking the logo has to open the strip.
        ///
        /// The screenshot scenes call <see cref="MainMenuScreen.Expand"/>
        /// directly, so they prove the open state renders and prove nothing
        /// at all about the click that is supposed to reach it. That gap is
        /// exactly where this broke in the real game.
        /// </summary>
        [Test]
        public void TestClickingTheLogoOpensTheStrip()
        {
            ManualInputManager input = null!;
            MainMenuScreen menu = null!;

            AddStep("load menu", () =>
            {
                Child = input = new ManualInputManager
                {
                    RelativeSizeAxes = Axes.Both,
                    Child = new ScreenStack { RelativeSizeAxes = Axes.Both }
                        .With(stack => stack.Push(menu = new MainMenuScreen())),
                };
            });

            AddUntilStep("menu loaded", () => menu.IsLoaded);

            // Let the closed strip actually run a few frames before clicking.
            // Without this the click lands before the strip's first Update,
            // and the test passes over the exact bug that broke the real
            // game: Update sets Alpha to 0 while shut, a drawable that hides
            // itself in its own Update stops updating, and the strip could
            // then never animate open again.
            AddWaitStep("let the strip settle shut", 10);
            AddAssert("strip is shut", () => strip(menu).Expansion < 0.001f);

            AddStep("point at the logo", () => input.MoveMouseTo(logo(menu)));
            AddStep("click it", () => input.Click(MouseButton.Left));

            AddUntilStep("strip is open", () => strip(menu).Expansion > 0.9f);
            AddAssert("buttons are on screen", () => buttonsVisible(menu));

            AddStep("point at the logo again", () => input.MoveMouseTo(logo(menu)));
            AddStep("click it again", () => input.Click(MouseButton.Left));

            AddUntilStep("strip is closed", () => strip(menu).Expansion < 0.05f);
        }

        /// <summary>
        /// The info button opens the credits, anything closes them, and
        /// neither the click that opens nor the click that closes reaches the
        /// logo underneath.
        /// </summary>
        [Test]
        public void TestInfoButtonOpensTheCredits()
        {
            ManualInputManager input = null!;
            MainMenuScreen menu = null!;

            AddStep("load menu", () =>
            {
                Child = input = new ManualInputManager
                {
                    RelativeSizeAxes = Axes.Both,
                    Child = new ScreenStack { RelativeSizeAxes = Axes.Both }
                        .With(stack => stack.Push(menu = new MainMenuScreen())),
                };
            });

            AddUntilStep("menu loaded", () => menu.IsLoaded);
            AddWaitStep("let it settle", 10);

            AddAssert("credits start shut", () => credits(menu).State.Value == Visibility.Hidden);

            AddStep("point at the info button", () => input.MoveMouseTo(menu.ChildrenOfType<InfoButton>().Single()));
            AddStep("click it", () => input.Click(MouseButton.Left));

            AddUntilStep("credits are open", () => credits(menu).Alpha > 0.95f);

            AddAssert("everyone is named", () =>
            {
                var lines = credits(menu).ChildrenOfType<RetroText>().Select(t => t.Text).ToArray();

                return new[]
                {
                    "DEVELOPED BY", "Arafat Bin A Sattar Inan", "Adiba Noor Morshed",
                    "SUPERVISED BY", "Rokonuzzaman Sojib", "Lecturer, CSE, BUET",
                }.All(lines.Contains) && lines.Count(l => l == "CSE, BUET") == 2;
            });

            // Aimed at the logo itself: if the credits let the click through,
            // the strip would open behind them.
            AddStep("click on the logo", () =>
            {
                input.MoveMouseTo(logo(menu));
                input.Click(MouseButton.Left);
            });

            AddUntilStep("credits are shut", () => credits(menu).Alpha < 0.01f);
            AddAssert("the logo never saw the click", () => strip(menu).Expansion < 0.001f);

            AddStep("open again", () => menu.OpenCredits());
            AddUntilStep("open", () => credits(menu).Alpha > 0.95f);
            AddStep("press escape", () =>
            {
                input.PressKey(Key.Escape);
                input.ReleaseKey(Key.Escape);
            });
            AddUntilStep("escape shuts them", () => credits(menu).Alpha < 0.01f);
        }

        /// <summary>
        /// Clicking the now-playing credit drops the song list, a click
        /// elsewhere puts it away, and picking a row changes the song.
        ///
        /// Like the music test, only meaningful with a playable library: with
        /// no song there is no credit to click, and the steps pass over that.
        /// </summary>
        [Test]
        public void TestNowPlayingOpensTheSongList()
        {
            ManualInputManager input = null!;
            MainMenuScreen menu = null!;

            AddStep("load menu", () =>
            {
                Child = input = new ManualInputManager
                {
                    RelativeSizeAxes = Axes.Both,
                    Child = new ScreenStack { RelativeSizeAxes = Axes.Both }
                        .With(stack => stack.Push(menu = new MainMenuScreen())),
                };
            });

            AddUntilStep("menu loaded", () => menu.IsLoaded);

            // The credit is held back for a moment on entry, and the rows load
            // in the background.
            AddUntilStep("credit has appeared", () => !hasSongs(menu) || nowPlaying(menu).Alpha > 0.95f);
            AddUntilStep("list has loaded", () => !hasSongs(menu) || songPicker(menu).Rows.Count > 0);

            AddStep("point at the credit", () => input.MoveMouseTo(nowPlaying(menu)));
            AddStep("click it", () => input.Click(MouseButton.Left));

            AddUntilStep("list is open", () => !hasSongs(menu) || songPicker(menu).Alpha > 0.95f);

            AddAssert("it lists every playable song", () =>
                !hasSongs(menu) || songPicker(menu).Rows.Count == music(menu).PlayableSongs.Count);

            AddAssert("the song playing is marked", () =>
                !hasSongs(menu) || songPicker(menu).Rows.Count(r => r.IsCurrent) == 1);

            AddStep("click the credit again", () => input.Click(MouseButton.Left));
            AddUntilStep("list is away", () => !hasSongs(menu) || songPicker(menu).Alpha < 0.01f);

            // And the chooser itself: a different row starts that song.
            string? before = null;

            AddStep("open it", () =>
            {
                before = music(menu).Entry?.Path;
                menu.OpenSongList();
            });

            AddUntilStep("open", () => !hasSongs(menu) || songPicker(menu).Alpha > 0.95f);

            AddStep("click a row that is not playing", () =>
            {
                if (!hasSongs(menu) || songPicker(menu).Rows.Count < 2)
                    return;

                // The nearest row that is not the current one, so it is on screen.
                var rows = songPicker(menu).Rows;
                var target = rows.Where(r => !r.IsCurrent)
                                 .OrderBy(r => Math.Abs(r.ScreenSpaceDrawQuad.Centre.Y - songPicker(menu).ScreenSpaceDrawQuad.Centre.Y))
                                 .First();

                input.MoveMouseTo(target);
                input.Click(MouseButton.Left);
            });

            AddUntilStep("the song changed", () =>
                !hasSongs(menu) || songPicker(menu).Rows.Count < 2 || music(menu).Entry?.Path != before);

            AddUntilStep("list put itself away", () => !hasSongs(menu) || songPicker(menu).Alpha < 0.01f);
        }

        private static bool hasSongs(MainMenuScreen menu) => music(menu).PlayableSongs.Count > 0;

        private static MenuTrack music(MainMenuScreen menu) => menu.ChildrenOfType<MenuTrack>().Single();

        private static SongPicker songPicker(MainMenuScreen menu) => menu.ChildrenOfType<SongPicker>().Single();

        private static CreditsOverlay credits(MainMenuScreen menu) => menu.ChildrenOfType<CreditsOverlay>().Single();

        private static NowPlayingDisplay nowPlaying(MainMenuScreen menu) => menu.ChildrenOfType<NowPlayingDisplay>().Single();

        /// <summary>
        /// The music starts quiet and swells when the menu opens.
        ///
        /// Only meaningful on a machine with a playable library — with no
        /// song there is no track to ride, and the steps below assert the
        /// silent path instead, which is the one that would throw.
        /// </summary>
        [Test]
        public void TestMusicSwellsOnOpening()
        {
            MainMenuScreen menu = null!;

            AddStep("load menu", () =>
            {
                var stack = new ScreenStack { RelativeSizeAxes = Axes.Both };
                Child = stack;
                stack.Push(menu = new MainMenuScreen());
            });

            AddUntilStep("menu loaded", () => menu.IsLoaded);

            AddAssert("starts quiet", () => volume(menu) is not { } v || v <= MenuTrack.IdleVolume + 0.001);

            AddStep("open", () => menu.Expand());

            AddUntilStep("music swells", () => volume(menu) is not { } v || v > MenuTrack.IdleVolume + 0.05);

            AddStep("close", () => menu.Collapse());

            AddUntilStep("music settles back", () => volume(menu) is not { } v || v < MenuTrack.ActiveVolume - 0.05);
        }

        /// <summary>
        /// The edge flash is white while the screen is still black and
        /// white, and only takes the logo's colour once the colour has
        /// spread.
        /// </summary>
        [Test]
        public void TestEdgeFlashStartsWhite()
        {
            MainMenuScreen menu = null!;

            AddStep("load menu", () =>
            {
                var stack = new ScreenStack { RelativeSizeAxes = Axes.Both };
                Child = stack;
                stack.Push(menu = new MainMenuScreen());
            });

            AddUntilStep("menu loaded", () => menu.IsLoaded);
            AddWaitStep("let it settle", 5);

            AddAssert("flash is white", () => isWhite(flashColour(menu)));

            AddStep("open", () => menu.Expand());

            AddUntilStep("flash takes the hue", () => !isWhite(flashColour(menu)));

            AddStep("close", () => menu.Collapse());

            AddUntilStep("flash returns to white", () => isWhite(flashColour(menu)));
        }

        private static Color4 flashColour(MainMenuScreen menu) =>
            menu.ChildrenOfType<EdgeGlow>().Single().GlowColour;

        /// <summary>
        /// Whether a colour is white to the eye. Loose, because the hue is
        /// drifting continuously and the transition eases rather than
        /// snapping — what matters is that it reads as white, not that every
        /// channel is exactly 1.
        /// </summary>
        private static bool isWhite(Color4 colour) =>
            colour.R > 0.97f && colour.G > 0.97f && colour.B > 0.97f;

        /// <summary>The menu track's current volume, or null when nothing is playing.</summary>
        private static double? volume(MainMenuScreen menu) =>
            menu.ChildrenOfType<MenuTrack>().Single().Track?.Volume.Value;

        private static MenuLogo logo(MainMenuScreen menu) =>
            menu.ChildrenOfType<MenuLogo>().Single();

        private static MenuStrip strip(MainMenuScreen menu) =>
            menu.ChildrenOfType<MenuStrip>().Single();

        /// <summary>
        /// Whether both strip buttons actually have a presence on screen —
        /// non-zero size, visible, and inside the window. A button that is
        /// merely "there" in the tree but clipped to nothing passes an
        /// existence check and fails this one.
        /// </summary>
        private static bool buttonsVisible(MainMenuScreen menu)
        {
            var buttons = menu.ChildrenOfType<MenuStripButton>().ToArray();

            if (buttons.Length != 2)
                return false;

            return buttons.All(b => b.DrawWidth > 50
                                    && b.DrawHeight > 20
                                    && b.Alpha > 0.5f
                                    && b.IsPresent
                                    && b.ScreenSpaceDrawQuad.Width > 50);
        }

        [Test]
        public void TestSpectrumRingWithSyntheticSignal()
        {
            SpectrumRing ring = null!;
            double start = 0;

            AddStep("add ring", () =>
            {
                start = Time.Current;

                Child = new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(520),
                    Child = ring = new SpectrumRing
                    {
                        RelativeSizeAxes = Axes.Both,
                    },
                };
            });

            AddStep("drive it", () =>
            {
                // A peak sweeping up and down the spectrum, so every column
                // is seen to rise and fall in turn. A column with an empty
                // bin group, or one mapped to the wrong angle, shows up at
                // once as a gap travelling round the ring.
                ring.AmplitudeSource = () =>
                {
                    var bins = new float[256];

                    double t = (Time.Current - start) / 3000;
                    int centre = (int)(1 + (Math.Sin(t * Math.PI * 2) * 0.5 + 0.5) * 120);

                    for (int bin = centre - 4; bin <= centre + 4; bin++)
                    {
                        if (bin >= 0 && bin < bins.Length)
                            bins[bin] = 0.35f;
                    }

                    return bins;
                };
            });

            AddStep("silence", () => ring.AmplitudeSource = () => new float[256]);
        }
    }
}
