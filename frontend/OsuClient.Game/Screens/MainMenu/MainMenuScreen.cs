using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Framework.Platform;
using osu.Framework.Screens;
using OsuClient.Game.Graphics;
using OsuClient.Game.Screens.Generation;
using OsuClient.Game.Screens.SongSelect;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.MainMenu
{
    /// <summary>
    /// Entry screen: the animated logo plus navigation into song select.
    /// </summary>
    public partial class MainMenuScreen : Screen
    {
        private readonly string? songsDirectory;

        [Resolved]
        private GameHost host { get; set; } = null!;

        public MainMenuScreen(string? songsDirectory = null)
        {
            this.songsDirectory = songsDirectory;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChild = new FillFlowContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 24),
                Children = new Drawable[]
                {
                    new AnimatedLogo
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                    },
                    new SpriteText
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Text = "OsuClient",
                        Font = FontUsage.Default.With(size: 32),
                        Colour = Color4.White,
                        Margin = new MarginPadding { Top = 10 },
                    },
                    new BasicButton
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Size = new Vector2(240, 46),
                        Text = "Play",
                        BackgroundColour = new Color4(0.2f, 0.45f, 0.75f, 1f),
                        HoverColour = new Color4(0.28f, 0.58f, 0.9f, 1f),
                        Action = pushSongSelect,
                    },
                    new BasicButton
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Size = new Vector2(240, 46),
                        Text = "Generate",
                        BackgroundColour = new Color4(0.2f, 0.5f, 0.3f, 1f),
                        HoverColour = new Color4(0.26f, 0.65f, 0.4f, 1f),
                        Action = pushUpload,
                    },
                    new BasicButton
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Size = new Vector2(240, 46),
                        Text = "Exit",
                        BackgroundColour = new Color4(0.35f, 0.2f, 0.2f, 1f),
                        HoverColour = new Color4(0.5f, 0.26f, 0.26f, 1f),
                        Action = () => host.Exit(),
                    },
                    new SpriteText
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Text = "Press Enter to play",
                        Font = FontUsage.Default.With(size: 13),
                        Colour = new Color4(0.55f, 0.55f, 0.65f, 1f),
                        Margin = new MarginPadding { Top = 6 },
                    },
                },
            };
        }

        private void pushSongSelect()
        {
            if (this.IsCurrentScreen())
                this.Push(new SongSelectScreen(songsDirectory));
        }

        private void pushUpload()
        {
            if (this.IsCurrentScreen())
                this.Push(new UploadScreen(songsDirectory));
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            switch (e.Key)
            {
                case osuTK.Input.Key.Enter:
                case osuTK.Input.Key.KeypadEnter:
                    pushSongSelect();
                    return true;
            }

            return base.OnKeyDown(e);
        }

        public override void OnEntering(ScreenTransitionEvent e)
        {
            base.OnEntering(e);

            this.FadeInFromZero(300, Easing.OutQuint);
        }

        public override void OnResuming(ScreenTransitionEvent e)
        {
            base.OnResuming(e);

            this.FadeIn(250, Easing.OutQuint);
        }
    }
}
