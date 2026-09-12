using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Screens;
using OsuClient.Game.Screens.SongSelect;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.Gameplay
{
    /// <summary>
    /// Stand-in for the real gameplay screen. Song select pushes this when a
    /// difficulty is confirmed, so the Menu -> Song Select -> Player navigation
    /// path is wired end to end.
    ///
    /// It plays nothing. It just proves the right beatmap arrived, by showing
    /// what was decoded from it. Phase 2 replaces this with PlayerScreen — the
    /// beatmap clock, the hit object pool and input handling.
    /// </summary>
    public partial class PlayerScreenPlaceholder : Screen
    {
        private readonly BeatmapSelection selection;

        public PlayerScreenPlaceholder(BeatmapSelection selection)
        {
            this.selection = selection;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            var beatmap = selection.Beatmap;
            var (circles, sliders, spinners) = DifficultyIcon.CountObjects(beatmap);

            double lengthSeconds = (beatmap.LastHitObjectTime - beatmap.FirstHitObjectTime) / 1000.0;

            InternalChildren = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(0.05f, 0.05f, 0.08f, 1f),
                },
                new FillFlowContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 10),
                    Children = new Drawable[]
                    {
                        new SpriteText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Text = $"{beatmap.Metadata.Artist} - {beatmap.Metadata.Title}",
                            Font = FontUsage.Default.With(size: 30),
                            Colour = Color4.White,
                        },
                        new SpriteText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Text = $"[{beatmap.Metadata.Version}]   mapped by {beatmap.Metadata.Creator}",
                            Font = FontUsage.Default.With(size: 17),
                            Colour = new Color4(0.7f, 0.7f, 0.8f, 1f),
                        },
                        new SpriteText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Text = $"{beatmap.BPM:0.#} BPM   •   {lengthSeconds:0.#}s   •   "
                                   + $"{beatmap.HitObjects.Count} objects "
                                   + $"({circles} circles, {sliders} sliders, {spinners} spinners)",
                            Font = FontUsage.Default.With(size: 15),
                            Colour = new Color4(0.65f, 0.65f, 0.75f, 1f),
                            Margin = new MarginPadding { Top = 10 },
                        },
                        new SpriteText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Text = $"AR {beatmap.Difficulty.ApproachRate}   CS {beatmap.Difficulty.CircleSize}   "
                                   + $"OD {beatmap.Difficulty.OverallDifficulty}   HP {beatmap.Difficulty.HPDrainRate}   "
                                   + $"SV {beatmap.Difficulty.SliderMultiplier}",
                            Font = FontUsage.Default.With(size: 15),
                            Colour = new Color4(0.65f, 0.65f, 0.75f, 1f),
                        },
                        new SpriteText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Text = $"audio: {selection.Entry.Set?.AudioFilename ?? "unknown"}"
                                   + $"   (present: {selection.Entry.Set?.HasAudio == true})",
                            Font = FontUsage.Default.With(size: 13),
                            Colour = new Color4(0.5f, 0.5f, 0.6f, 1f),
                        },
                        new SpriteText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Text = "Gameplay arrives in Phase 2 — press Escape to go back",
                            Font = FontUsage.Default.With(size: 16),
                            Colour = new Color4(0.96f, 0.82f, 0.25f, 1f),
                            Margin = new MarginPadding { Top = 24 },
                        },
                    },
                },
            };
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (e.Key == osuTK.Input.Key.Escape)
            {
                this.Exit();
                return true;
            }

            return base.OnKeyDown(e);
        }

        public override void OnEntering(ScreenTransitionEvent e)
        {
            base.OnEntering(e);

            this.FadeInFromZero(250, Easing.OutQuint);
        }

        public override bool OnExiting(ScreenExitEvent e)
        {
            this.FadeOut(200, Easing.OutQuint);

            return base.OnExiting(e);
        }
    }
}
