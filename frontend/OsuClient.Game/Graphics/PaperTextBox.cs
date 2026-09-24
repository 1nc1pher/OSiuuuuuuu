using System;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Graphics
{
    /// <summary>
    /// A text box that looks like handwriting on a cassette's paper label:
    /// dark ink, a transparent ground so the paper shows through, an italic
    /// placeholder, and a caret wide enough to find against cream.
    ///
    /// <para>
    /// Extracted from <c>CassetteSearchBar</c>, which was the only user until
    /// the upload screen's cassette grew a label of its own. Two copies of a
    /// caret that has to be written out by hand (see
    /// <see cref="PaperCaret"/>) is exactly the kind of duplication that
    /// drifts, so it lives here and both screens use it.
    /// </para>
    ///
    /// <para>
    /// Styling is fixed rather than parameterised on purpose: osu.Framework's
    /// <see cref="TextBox"/> calls <see cref="CreatePlaceholder"/> from its own
    /// constructor, which runs before any derived field or property is
    /// assigned — so a configurable font size or accent would silently be the
    /// default on the placeholder and correct everywhere else. Both call sites
    /// want the same look anyway.
    /// </para>
    /// </summary>
    public partial class PaperTextBox : BasicTextBox
    {
        /// <summary>The label stock these boxes are written on.</summary>
        public static readonly Color4 Paper = new Color4(0.93f, 0.90f, 0.82f, 0.97f);

        /// <summary>What they are written in.</summary>
        public static readonly Color4 Ink = new Color4(0.13f, 0.10f, 0.16f, 1f);

        private const float font_size = 21;

        /// <summary>Fired when the box gains or loses focus.</summary>
        public Action<bool>? FocusChanged;

        public PaperTextBox()
        {
            BackgroundUnfocused = Color4.Transparent;
            BackgroundFocused = Color4.Transparent;
            BackgroundCommit = Color4.Transparent;
        }

        protected override void OnFocus(FocusEvent e)
        {
            base.OnFocus(e);
            FocusChanged?.Invoke(true);
        }

        protected override void OnFocusLost(FocusLostEvent e)
        {
            base.OnFocusLost(e);
            FocusChanged?.Invoke(false);
        }

        protected override float LeftRightPadding => 0;

        protected override Color4 SelectionColour => RetroPalette.Magenta.Opacity(0.35f);

        protected override SpriteText CreatePlaceholder() => new SpriteText
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            Font = FontUsage.Default.With(size: font_size, italics: true),
            Colour = Ink.Opacity(0.38f),
        };

        protected override Drawable GetDrawableCharacter(char c) => new SpriteText
        {
            Text = c.ToString(),
            Font = FontUsage.Default.With(size: font_size),
            Colour = Ink,
        };

        protected override Caret CreateCaret() => new PaperCaret(SelectionColour);

        /// <summary>
        /// The typing caret.
        ///
        /// Written out rather than configured from <see cref="BasicCaret"/>,
        /// which cannot be made visible here: it repaints itself
        /// <see cref="Color4.White"/> every time it moves, so a colour set on
        /// it survives exactly until the next keystroke — and white on cream
        /// paper is invisible either way. It's also wider than the
        /// framework's and carries a glow, because the thing it has to stand
        /// out against is paper rather than the dark panel a caret normally
        /// sits on.
        /// </summary>
        private partial class PaperCaret : Caret
        {
            private const float caret_width = 3;

            private readonly Color4 selectionColour;

            public PaperCaret(Color4 selectionColour)
            {
                this.selectionColour = selectionColour;

                RelativeSizeAxes = Axes.Y;
                Size = new Vector2(caret_width, 0.8f);
                Anchor = Anchor.CentreLeft;
                Origin = Anchor.CentreLeft;
                Masking = true;
                CornerRadius = caret_width / 2;
                Colour = RetroPalette.Magenta;
                EdgeEffect = new EdgeEffectParameters
                {
                    Type = EdgeEffectType.Glow,
                    Colour = RetroPalette.Magenta.Opacity(0.5f),
                    Radius = 7,
                };

                InternalChild = new Box { RelativeSizeAxes = Axes.Both };
            }

            public override void Hide() => this.FadeOut(120);

            public override void DisplayAt(Vector2 position, float? selectionWidth)
            {
                if (selectionWidth != null)
                {
                    this.MoveTo(position, 60, Easing.Out);
                    this.ResizeWidthTo(selectionWidth.Value + caret_width / 2, 60, Easing.Out);
                    this.FadeColour(selectionColour, 200, Easing.Out);
                    this.FadeTo(1, 200, Easing.Out);
                    return;
                }

                this.MoveTo(new Vector2(position.X - caret_width / 2, position.Y), 60, Easing.Out);
                this.ResizeWidthTo(caret_width, 60, Easing.Out);
                this.FadeColour(RetroPalette.Magenta, 200, Easing.Out);

                // Blinks between full and dim rather than fully out: a caret
                // that vanishes entirely is harder to find again on a busy
                // screen than one that only dips.
                this.FadeTo(1f).Then().FadeTo(0.35f, 480, Easing.InOutSine)
                    .Then().FadeTo(1f, 480, Easing.InOutSine).Loop();
            }
        }
    }
}
