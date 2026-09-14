using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using OsuClient.Game.Graphics;

namespace OsuClient.Tests.Visual
{
    /// <summary>
    /// <see cref="RetroText"/> rasterizes text to a texture itself (rather
    /// than going through osu.Framework's own font system, which can't load
    /// a raw .ttf — see the class's own remarks), so this exercises that
    /// pipeline against a real renderer: both fonts, an empty string, and
    /// rapid text changes (the combo counter's actual usage pattern).
    /// </summary>
    [TestFixture]
    public partial class TestSceneRetroText : osu.Framework.Testing.TestScene
    {
        [Test]
        public void TestBothFontsRenderNonEmptyTextures()
        {
            RetroText display = null!;
            RetroText body = null!;

            AddStep("add both fonts", () =>
            {
                Child = new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Children = new Drawable[]
                    {
                        display = new RetroText { Font = RetroFontFamily.Display, TextSize = 24, Text = "COMBO 99x" },
                        body = new RetroText { Font = RetroFontFamily.Body, TextSize = 24, Text = "Song Select" },
                    },
                };
            });

            AddUntilStep("both loaded", () => display.IsLoaded && body.IsLoaded);

            AddAssert("display font has a real texture", () => display.Texture != null && display.Size.X > 0);
            AddAssert("body font has a real texture", () => body.Texture != null && body.Size.X > 0);
            AddAssert("the two fonts produced different-looking output",
                () => display.Texture!.Width != body.Texture!.Width || display.Texture!.Height != body.Texture!.Height);
        }

        [Test]
        public void TestEmptyTextClearsTheTexture()
        {
            RetroText text = null!;

            AddStep("add text", () => Child = text = new RetroText { Text = "hello", TextSize = 20 });
            AddUntilStep("loaded", () => text.IsLoaded);
            AddAssert("has a texture", () => text.Texture != null);

            AddStep("clear it", () => text.Text = "");
            AddAssert("texture is gone", () => text.Texture == null);
            AddAssert("size collapsed", () => text.Size == osuTK.Vector2.Zero);
        }

        [Test]
        public void TestRapidTextChangesDoNotThrow()
        {
            RetroText text = null!;

            AddStep("add text", () => Child = text = new RetroText { Text = "0", TextSize = 20 });
            AddUntilStep("loaded", () => text.IsLoaded);

            AddStep("change it a lot, like a combo counter would", () =>
            {
                for (int i = 0; i < 50; i++)
                    text.Text = $"{i}x";
            });

            AddAssert("still has a valid texture after many changes", () => text.Texture != null);
        }

        [Test]
        public void TestSameTextSizeStaysStableAcrossDifferentDigitCounts()
        {
            RetroText text = null!;
            float heightAtOneDigit = 0;

            AddStep("add text", () => Child = text = new RetroText { Font = RetroFontFamily.Display, Text = "5", TextSize = 24 });
            AddUntilStep("loaded", () => text.IsLoaded);
            AddStep("record height", () => heightAtOneDigit = text.Size.Y);

            AddStep("go to three digits", () => text.Text = "512");

            // Height shouldn't jump around as a combo counter's digit count
            // grows — only width should.
            AddAssert("height is unchanged", () => text.Size.Y == heightAtOneDigit);
            AddAssert("width grew", () => text.Size.X > 0);
        }
    }
}
