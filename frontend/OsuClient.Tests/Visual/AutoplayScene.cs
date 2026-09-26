using System;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osu.Framework.Testing.Input;
using OsuClient.Game.Beatmaps;
using OsuClient.Game.Beatmaps.HitObjects;
using OsuClient.Game.Graphics.Cursor;
using OsuClient.Game.Screens.Gameplay;
using OsuClient.Game.Screens.SongSelect;
using osuTK;
using osuTK.Input;

namespace OsuClient.Tests.Visual
{
    /// <summary>
    /// The <c>gameplay-autoplay</c> screenshot scene: a beatmap folder on disk
    /// (the first difficulty in <c>OSU_SCREENSHOT_SET</c>) played by a simple
    /// autoplay through the real input system, so a capture shows what an
    /// idle run never can: judgements, hit bursts, the combo counter
    /// rolling, the score moving and a spinner being spun.
    ///
    /// It presses Z on each circle's time with the cursor on it, holds
    /// through sliders following the ball, and circles the cursor round a
    /// spinner's centre at <see cref="spin_rpm"/>. Good enough to exercise the
    /// screen; not a replay of anything.
    /// </summary>
    public partial class AutoplayScene : CompositeDrawable
    {
        private const double spin_rpm = 300;
        private const double press_length = 45;
        private const float spin_radius = 60;

        private readonly ManualInputManager input;
        private readonly PlayerScreen player;
        private readonly Beatmap beatmap;

        /// <summary>The run's gameplay time, in milliseconds. For the frame benchmark.</summary>
        public double GameplayTime => player.IsLoaded ? player.GameplayTime : double.NaN;

        private int next;
        private HitObjectData? holding;
        private double releaseAt = double.NaN;

        public AutoplayScene(string folder)
        {
            RelativeSizeAxes = Axes.Both;

            var set = OszImporter.LoadFromDirectory(folder);
            beatmap = set.Beatmaps[0];

            var entry = new BeatmapLibraryEntry { Path = folder, Set = set };
            player = new PlayerScreen(new BeatmapSelection(entry, beatmap));

            var stack = new ScreenStack { RelativeSizeAxes = Axes.Both };

            InternalChild = input = new ManualInputManager
            {
                RelativeSizeAxes = Axes.Both,
                Children = new Drawable[] { stack, new GameCursor() },
            };

            stack.Push(player);
        }

        protected override void Update()
        {
            base.Update();

            if (!player.IsLoaded || player.Completed)
                return;

            double time = player.GameplayTime;
            var objects = beatmap.HitObjects;

            if (!double.IsNaN(releaseAt) && time >= releaseAt)
            {
                input.ReleaseKey(Key.Z);
                releaseAt = double.NaN;
            }

            if (holding is SliderData slider)
            {
                if (time >= slider.EndTime)
                {
                    input.ReleaseKey(Key.Z);
                    holding = null;
                }
                else
                    moveTo(slider, ballPosition(slider, time));
            }

            if (holding is SpinnerData spinner)
            {
                if (time >= spinner.EndTime)
                    holding = null;
                else
                {
                    double angle = (time - spinner.StartTime) / 60000 * spin_rpm * 2 * Math.PI;
                    moveTo(spinner, spinner.Position + new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * spin_radius);
                }
            }

            if (holding != null || next >= objects.Count)
                return;

            var target = objects[next];

            // Glide towards the next object while waiting for it, so the
            // cursor is on it when its time comes.
            if (drawableFor(target) != null)
                moveTo(target, target.Position);

            if (time < target.StartTime)
                return;

            next++;

            switch (target)
            {
                case SliderData s:
                    input.PressKey(Key.Z);
                    holding = s;
                    break;

                case SpinnerData sp:
                    holding = sp;
                    break;

                default:
                    input.PressKey(Key.Z);
                    releaseAt = time + press_length;
                    break;
            }
        }

        private static Vector2 ballPosition(SliderData slider, double time)
        {
            int slides = Math.Max(1, slider.Slides);
            double span = (slider.EndTime - slider.StartTime) / slides;
            double into = Math.Clamp((time - slider.StartTime) / span, 0, slides);
            int index = Math.Min(slides - 1, (int)Math.Floor(into));
            double progress = into - index;

            if (index % 2 == 1)
                progress = 1 - progress;

            return SliderPath.FromSlider(slider).PositionAt(progress);
        }

        private DrawableHitObject? drawableFor(HitObjectData data) =>
            player.ChildrenOfType<DrawableHitObject>().FirstOrDefault(d => ReferenceEquals(d.Data, data));

        private void moveTo(HitObjectData data, Vector2 playfieldPosition)
        {
            // Any drawable hit object's local space is the playfield's.
            var drawable = drawableFor(data) ?? player.ChildrenOfType<DrawableHitObject>().FirstOrDefault();

            if (drawable != null)
                input.MoveMouseTo(drawable.ToScreenSpace(playfieldPosition));
        }
    }
}
