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
