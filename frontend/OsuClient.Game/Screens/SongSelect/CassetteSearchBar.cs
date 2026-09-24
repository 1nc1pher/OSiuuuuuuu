using System;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using OsuClient.Game.Graphics;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.SongSelect
{
    /// <summary>
    /// The song search, dressed as the paper label off a cassette
    /// (CAROUSEL_REDESIGN_PLAN.md step 4): cream sticker, coloured spine, sat
    /// very slightly crooked as though applied by hand.
    ///
    /// The cream ground is deliberate contrast — it's the one warm, physical,
    /// non-neon object on a screen of glowing dark panels, which is what makes
    /// it read as a sticker rather than another UI card.
    /// </summary>
    public partial class CassetteSearchBar : CompositeDrawable
    {
        /// <summary>Hand-applied tilt. Small enough to feel accidental.</summary>
        private const float tilt = -0.45f;

        /// <summary>Corner rounding of the sticker, shared with its focus glow.</summary>
        private const float corner_radius = 3;

        private static readonly Color4 paper = PaperTextBox.Paper;
        private static readonly Color4 ink = PaperTextBox.Ink;

        /// <summary>Fired as the query changes, already trimmed.</summary>
        public Action<string>? QueryChanged;

        /// <summary>Raised when the box gains focus — clicked into, or focused by a key.</summary>
        public Action? Focused;

        private readonly PaperTextBox textBox;
        private readonly Container focusGlow;

        public CassetteSearchBar()
        {
            RelativeSizeAxes = Axes.X;
            Height = 74;
            Rotation = tilt;

            InternalChildren = new Drawable[]
            {
                // Focus glow, behind the sticker and shaped exactly like it.
                // A layer of its own rather than the sticker's own edge
                // effect, because that one is a drop shadow and a drawable
                // only carries one: this way the sticker keeps sitting on the
                // screen while the glow comes and goes independently.
                focusGlow = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Masking = true,
                    CornerRadius = corner_radius,
                    Alpha = 0,
                    EdgeEffect = new EdgeEffectParameters
                    {
                        Type = EdgeEffectType.Glow,
                        Colour = RetroPalette.Magenta.Opacity(0.85f),
                        Radius = 22,
                    },
                    Child = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0, AlwaysPresent = true },
                },
                new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Masking = true,
                    CornerRadius = corner_radius,
                    EdgeEffect = new EdgeEffectParameters
                    {
                        Type = EdgeEffectType.Shadow,
                        Colour = new Color4(0f, 0f, 0f, 0.55f),
                        Radius = 14,
                        Offset = new Vector2(0, 4),
                    },
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = paper,
                        },
                        // The label's coloured spine, like the band printed
                        // down the edge of a tape's insert.
                        new Box
                        {
                            RelativeSizeAxes = Axes.Y,
                            Width = 14,
                            Colour = RetroPalette.Magenta,
                        },
                        // Pins holding the sticker down, matching the screws
                        // on the cassettes and the info panel.
                        pin(Anchor.TopRight, new Vector2(-10, 10)),
                        pin(Anchor.BottomRight, new Vector2(-10, -10)),
                        new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Direction = FillDirection.Vertical,
                            Spacing = new Vector2(0, 3),
                            Padding = new MarginPadding { Left = 30, Right = 18 },
                            Children = new Drawable[]
                            {
                                new RetroText
                                {
                                    Font = RetroFontFamily.Display,
                                    TextSize = 8,
                                    Text = "SIDE A / SEARCH",
                                    Colour = ink.Opacity(0.55f),
                                },
                                textBox = new PaperTextBox
                                {
                                    RelativeSizeAxes = Axes.X,
                                    Height = 30,
                                    PlaceholderText = "search songs...",
                                },
                            },
                        },
                    },
                },
            };
        }

        private static Drawable pin(Anchor anchor, Vector2 position) => new Circle
        {
            Anchor = anchor,
            Origin = Anchor.Centre,
            Position = position,
            Size = new Vector2(5),
            Colour = ink.Opacity(0.22f),
        };

        protected override void LoadComplete()
        {
            base.LoadComplete();

            textBox.Current.ValueChanged += e => QueryChanged?.Invoke(e.NewValue.Trim());
            textBox.FocusChanged += setFocused;

            // A query set before this point has already been assigned, so
            // subscribing now would never hear about it, and the box would
            // sit there showing a search the wheel knows nothing about. This
            // is reachable from outside a test: a parent's LoadComplete runs
            // before its children's, so anything the screen does in its own
            // gets in ahead of this line.
            if (!string.IsNullOrEmpty(textBox.Current.Value))
                QueryChanged?.Invoke(textBox.Current.Value.Trim());
        }

        /// <summary>
        /// Breathes the glow while the box has focus. It pulses rather than
        /// sitting at a fixed brightness because a static glow on a screen
        /// that already glows everywhere reads as one more decorated panel,
        /// where a moving one reads as "this is where your typing is going".
        /// </summary>
        private void setFocused(bool focused)
        {
            if (focused)
                Focused?.Invoke();

            focusGlow.ClearTransforms();

            if (!focused)
            {
                focusGlow.FadeOut(220, Easing.OutQuint);
                return;
            }

            focusGlow.FadeTo(1f, 400, Easing.InOutSine)
                     .Then()
                     .FadeTo(0.45f, 700, Easing.InOutSine)
                     .Then()
                     .FadeTo(1f, 700, Easing.InOutSine)
                     .Loop();
        }

        /// <summary>
        /// Puts the caret in the box.
        ///
        /// Deferred a frame rather than focusing right away: there is no
        /// focus manager to reach until this is in the tree, and a caller
        /// that runs during load (a parent's LoadComplete happens before its
        /// children's) would otherwise silently do nothing.
        /// </summary>
        public void Focus() => Schedule(() => GetContainingFocusManager()?.ChangeFocus(textBox));

        /// <summary>
        /// Puts <paramref name="query"/> in the box, as though it had been
        /// typed. Through <see cref="osu.Framework.Graphics.UserInterface.TextBox.Current"/>
        /// rather than <c>Text</c>: the latter updates what's displayed
        /// without necessarily notifying the bindable, so the box would show
        /// a query the filter never heard about.
        /// </summary>
        public void SetQuery(string query) => textBox.Current.Value = query;

        /// <summary>
        /// A <see cref="BasicTextBox"/> stripped back to ink on paper: the
        /// sticker behind it already supplies the surface, so the box's own
        /// background would just draw a second rectangle on top of it.
        ///
        /// Typed characters stay in osu.Framework's default font rather than
        /// <see cref="RetroText"/> — the textbox lays out one drawable per
        /// character and needs each one's width at construction, which a
        /// texture rasterized asynchronously can't promise.
        /// </summary>
    }
}
