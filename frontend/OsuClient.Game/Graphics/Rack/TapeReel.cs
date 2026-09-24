using System;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Graphics.Rack
{
    /// <summary>
    /// One reel of a reel-to-reel machine: a hub, three spokes, a wound band
    /// of tape, and a chrome centre.
    ///
    /// <para>
    /// It turns while <see cref="Running"/>, and that is the entire claim it
    /// makes. The generation screen has no percentage available — nothing
    /// streams from the backend but stdout lines — so this is a liveness
    /// indicator, which is exactly what a turning reel honestly is. It does
    /// not speed up, slow down or fill according to anything, because there
    /// is nothing truthful for it to follow.
    /// </para>
    ///
    /// <para>
    /// <see cref="TapeFill"/> is the one thing that does mean something, and
    /// only when a caller sets it: how much tape is on this reel, for the
    /// supply/take-up pair to hand across between them.
    /// </para>
    /// </summary>
    public partial class TapeReel : CompositeDrawable
    {
        /// <summary>Seconds for one full turn. Slow enough to read as a machine rather than a spinner.</summary>
        private const double turn_seconds = 2.4;

        /// <summary>Radius of the hub, as a fraction of the reel.</summary>
        private const float hub_fraction = 0.30f;

        private const int spokes = 3;

        private readonly Container rotating;
        private readonly Container tape;

        private float fill = 0.75f;

        public TapeReel()
        {
            Size = new Vector2(58);

            InternalChild = rotating = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Children = new Drawable[]
                {
                    // The flange: the disc the tape is wound on.
                    new Circle
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = new Color4(0.10f, 0.09f, 0.14f, 1f),
                    },
                    tape = new Container
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        RelativeSizeAxes = Axes.Both,
                        Child = new Circle
                        {
                            RelativeSizeAxes = Axes.Both,
                            // Oxide brown, so a wound reel reads as tape and
                            // not as a second piece of the chassis.
                            Colour = new Color4(0.38f, 0.24f, 0.17f, 1f),
                        },
                    },
                    spokeSet(),
                    new Circle
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        RelativeSizeAxes = Axes.Both,
                        Size = new Vector2(hub_fraction),
                        Colour = RetroPalette.Chrome,
                    },
                    // A nub on the hub, so a slow turn is visible even when
                    // the spokes happen to line up with where they started.
                    new Circle
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Size = new Vector2(4),
                        Y = -7,
                        Colour = RetroPalette.ChromeDark.Darken(0.3f),
                    },
                },
            };

            applyFill();
        }

        /// <summary>Whether the reel is turning.</summary>
        public bool Running { get; set; }

        /// <summary>Which way it turns. The take-up reel runs against the supply.</summary>
        public bool Reverse { get; set; }

        /// <summary>How much tape is wound on, 0 to 1.</summary>
        public float TapeFill
        {
            get => fill;
            set
            {
                fill = Math.Clamp(value, 0, 1);
                applyFill();
            }
        }

        private void applyFill()
        {
            // Tape never shrinks past the hub it is wound around.
            tape.Size = new Vector2(hub_fraction + (1 - hub_fraction) * fill);
        }

        protected override void Update()
        {
            base.Update();

            if (!Running)
                return;

            float degrees = (float)(Clock.ElapsedFrameTime / 1000 / turn_seconds * 360);

            rotating.Rotation += Reverse ? -degrees : degrees;
        }

        private static Drawable spokeSet()
        {
            var container = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
            };

            for (int i = 0; i < spokes; i++)
            {
                container.Add(new Box
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Y,
                    Width = 5,
                    Height = 0.92f,
                    Rotation = i * 180f / spokes,
                    Colour = new Color4(0.16f, 0.14f, 0.20f, 1f),
                });
            }

            return container;
        }
    }
}
