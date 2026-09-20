using System;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using OsuClient.Game.Graphics;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.SongSelect
{
    /// <summary>
    /// The arm resting on the record, pointing at whichever song is selected
    /// (CAROUSEL_REDESIGN_PLAN.md step 9).
    ///
    /// It nudges and settles on every selection change rather than snapping,
    /// which is the whole reason it's here: a static arm is decoration, an arm
    /// that reacts is what makes the wheel feel like a turntable.
    ///
    /// The arm swings about its own bearing, out past the rim, rather than
    /// about the hub. That's how a real arm works, and here it's structural:
    /// <see cref="Turntable"/> mounts a base under that bearing, and an arm
    /// that orbited the hub instead would slide off its own base every time
    /// the selection changed. <see cref="RestAngle"/> and
    /// <see cref="PivotRadius"/> are public so the deck and the arm agree on
    /// where that bearing sits.
    /// </summary>
    public partial class Tonearm : CompositeDrawable
    {
        /// <summary>Angle the bearing is mounted at, clockwise from 12.</summary>
        public const float RestAngle = 249;

        /// <summary>
        /// How far the bearing sits from the hub, as a fraction of the disc's
        /// radius. Just past 1, so it lands on the deck beside the platter
        /// rather than on the record.
        /// </summary>
        public const float PivotRadius = 1.045f;

        /// <summary>
        /// Where the needle rests, as a fraction of the disc's radius. Lands
        /// it in the middle of the wedge band.
        /// </summary>
        private const float needle_radius = 0.72f;

        /// <summary>
        /// How far the arm swings at the bearing, in degrees, between the
        /// first song and the last.
        ///
        /// Much wider than the angle the needle covers as seen from the hub
        /// (~13°), because the arm is short relative to its distance out: the
        /// needle traces an arc about the bearing, not about the hub.
        /// </summary>
        private const float travel = 30;

        private readonly Container arm;
        private readonly Circle pivotOuter;
        private readonly Circle pivotInner;
        private readonly Box counterweightStub;
        private readonly Circle counterweight;
        private readonly Container headshell;

        public Tonearm()
        {
            Anchor = Anchor.Centre;
            Origin = Anchor.Centre;

            float radians = MathHelper.DegreesToRadians(RestAngle);

            InternalChild = arm = new Container
            {
                Anchor = Anchor.Centre,
                // Origin at the bearing, so Rotation pivots there. A centred
                // origin would swing the arm about the hub instead, which is
                // what this used to do — see the class remarks.
                Origin = Anchor.TopCentre,
                RelativePositionAxes = Axes.Both,
                Position = new Vector2(MathF.Sin(radians), -MathF.Cos(radians)) * (PivotRadius / 2),
                // A container with a TopCentre origin extends straight down,
                // and rotating it by the bearing's own angle happens to aim it
                // exactly at the hub — which is where an arm at rest points.
                Rotation = RestAngle,
                Children = new Drawable[]
                {
                    // Counterweight, behind the bearing and therefore outside
                    // the container's bounds — nothing here masks, so it
                    // renders fine, and it swings opposite the needle the way
                    // the real thing does.
                    counterweightStub = new Box
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.BottomCentre,
                        Colour = RetroPalette.ChromeDark,
                    },
                    counterweight = new Circle
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.Centre,
                        Colour = ColourInfo.GradientVertical(
                            RetroPalette.Chrome.Darken(0.15f), RetroPalette.ChromeDark),
                    },
                    // The arm itself, running from the bearing inwards — it
                    // fills this container, which is sized to the arm's reach.
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = RetroPalette.Chrome.Opacity(0.92f),
                    },
                    // Headshell, with the needle glowing at its tip. The glow
                    // is a container edge effect, which renders that
                    // container's masking shape — exactly the needle, since
                    // it's a rectangle (see DrawableSpinner's note on why the
                    // same trick doesn't work for arcs).
                    headshell = new Container
                    {
                        Anchor = Anchor.BottomCentre,
                        Origin = Anchor.TopCentre,
                        Masking = true,
                        CornerRadius = 2,
                        EdgeEffect = new EdgeEffectParameters
                        {
                            Type = EdgeEffectType.Glow,
                            Colour = RetroPalette.Magenta.Opacity(0.75f),
                            Radius = 12,
                        },
                        Children = new Drawable[]
                        {
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = RetroPalette.ChromeDark,
                            },
                            new Box
                            {
                                RelativeSizeAxes = Axes.X,
                                Anchor = Anchor.BottomCentre,
                                Origin = Anchor.BottomCentre,
                                Height = 4,
                                Colour = RetroPalette.Magenta,
                            },
                        },
                    },
                    // Bearing cap, last so it covers where the shaft and the
                    // counterweight meet.
                    pivotOuter = new Circle
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.Centre,
                        Colour = RetroPalette.ChromeDark,
                    },
                    pivotInner = new Circle
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.Centre,
                        Colour = RetroPalette.Chrome,
                    },
                },
            };
        }

        protected override void Update()
        {
            base.Update();

            // Everything is proportional to the disc so the arm keeps its
            // shape at any window size — the disc's diameter is this
            // drawable's own size.
            float radius = DrawHeight / 2;
            float armLength = radius * (PivotRadius - needle_radius);
            float pivotSize = radius * 0.058f;

            arm.Size = new Vector2(MathF.Max(pivotSize * 0.24f, 4), armLength);

            pivotOuter.Size = new Vector2(pivotSize);
            pivotInner.Size = new Vector2(pivotSize * 0.5f);

            // Reaches past the base plate the bearing is mounted on, or the
            // weight is simply hidden behind it.
            float stub = pivotSize * 1.15f;

            counterweightStub.Size = new Vector2(MathF.Max(pivotSize * 0.16f, 3), stub);

            counterweight.Y = -stub;
            counterweight.Size = new Vector2(pivotSize * 0.55f, pivotSize * 0.75f);

            headshell.Size = new Vector2(pivotSize * 0.55f, pivotSize * 0.8f);
        }

        /// <summary>
        /// Swings the arm to where song <paramref name="index"/> of
        /// <paramref name="count"/> sits, overshooting slightly and settling —
        /// the needle-drop.
        /// </summary>
        public void PointAt(int index, int count)
        {
            if (count <= 0)
                return;

            float position = count == 1 ? 0.5f : index / (float)(count - 1);
            float target = RestAngle + (position - 0.5f) * travel;

            arm.RotateTo(target, 620, Easing.OutElastic);
        }
    }
}
