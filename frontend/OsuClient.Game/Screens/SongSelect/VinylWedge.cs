using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.UserInterface;
using OsuClient.Game.Graphics;
using osuTK;
using osuTK.Graphics;

namespace OsuClient.Game.Screens.SongSelect
{
    /// <summary>
    /// One song's slice of the record (CAROUSEL_REDESIGN_PLAN.md step 6).
    ///
    /// osu.Framework has no wedge primitive, so this is a
    /// <see cref="CircularProgress"/>: <see cref="CircularProgress.InnerRadius"/>
    /// at 1 fills all the way to the hub instead of drawing a ring, `Progress`
    /// sets how wide the slice is, and `Rotation` puts it at its angle.
    /// <c>DrawableSpinner</c>'s side meters already use the same primitive the
    /// same way.
    ///
    /// The cover art is handed straight to the shape rather than masked into
    /// it. `CircularProgress` maps its texture polar — horizontally around the
    /// sweep, vertically along the radius — so the art arrives already fanned
    /// into the wedge, which is how the Persona 3 reference art in
    /// <c>visualization From OSU! lazer/carousel UI/art3.jpg</c> treats its own
    /// slices.
    /// </summary>
    public partial class VinylWedge : CompositeDrawable
    {
        /// <summary>
        /// Cover art is downscaled on load. A wedge is a few hundred pixels
        /// across at most and a library can put a dozen on screen at once, so
        /// full-resolution art would be a lot of texture memory for detail
        /// that the polar warp throws away anyway.
        /// </summary>
        public const int ArtResolution = 320;

        /// <summary>
        /// Ring thickness as a fraction of the disc's radius — 1 would fill
        /// all the way to the hub.
        ///
        /// Keeping the slices in an outer band is both truer to a record (the
        /// label and lead-in grooves live in the middle) and much kinder to
        /// the art: a wedge that runs to the centre stretches its texture from
        /// nothing to full width, which smears a cover past recognising.
        /// </summary>
        public const float InnerRadius = 0.62f;

        private readonly string? artPath;
        private readonly Color4 accent;

        private readonly CircularProgress fill;
        private readonly CircularProgress tint;
        private readonly CircularProgress dim;

        private bool hasArt;

        [Resolved]
        private IRenderer renderer { get; set; } = null!;

        /// <param name="sweepDegrees">How wide the slice is.</param>
        /// <param name="accent">
        /// Tint multiplied over the art — the difficulty ramp colour for the
        /// set, so the wheel and the cassette column agree.
        /// </param>
        public VinylWedge(float sweepDegrees, Color4 accent, string? artPath)
        {
            this.artPath = artPath;

            this.accent = accent;

            // Centred is load-bearing, not cosmetic: the selected wedge is
            // scaled up, and Scale pivots about Origin. Left at the default
            // TopLeft, growing the wedge walks its arc centre off the hub by
            // a few percent of the disc — tens of pixels — so the highlighted
            // slice no longer lines up with the record it sits in.
            Anchor = Anchor.Centre;
            Origin = Anchor.Centre;
            RelativeSizeAxes = Axes.Both;

            InternalChildren = new Drawable[]
            {
                fill = slice(sweepDegrees),
                // The accent is a wash laid over the art, not the art's own
                // colour: CircularProgress multiplies its Colour into its
                // texture, so tinting the textured shape directly crushes a
                // cover down to a monochrome silhouette.
                tint = slice(sweepDegrees),
                dim = slice(sweepDegrees),
            };

            tint.Colour = accent;
            dim.Colour = Color4.Black;
        }

        /// <summary>
        /// One layer of the wedge. Anchor/Origin centred is load-bearing:
        /// Rotation pivots about Origin, and any other value swings the wedge
        /// off the hub instead of turning it in place.
        /// </summary>
        private static CircularProgress slice(float sweepDegrees) => new CircularProgress
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            InnerRadius = InnerRadius,
            Progress = sweepDegrees / 360.0,
        };

        /// <summary>Angle of the wedge's leading edge, clockwise from 12 o'clock.</summary>
        public float Angle
        {
            get => fill.Rotation;
            set => fill.Rotation = tint.Rotation = dim.Rotation = value;
        }

        /// <summary>How wide the slice is, in degrees.</summary>
        public float Sweep
        {
            get => (float)(fill.Progress * 360);
            set => fill.Progress = tint.Progress = dim.Progress = value / 360.0;
        }

        /// <summary>
        /// How strongly the accent washes over the art, 0 to 1. A wedge with
        /// no art of its own ignores this and stays fully accent-coloured —
        /// otherwise it would be a hole in the record.
        /// </summary>
        public void SetTint(float strength, float dimStrength, double duration = 0)
        {
            tint.FadeTo(hasArt ? strength : 1, duration, Easing.OutQuint);
            dim.FadeTo(dimStrength, duration, Easing.OutQuint);
        }

        /// <summary>
        /// Lifts the wedge out of the record: <paramref name="scale"/> grows
        /// its radius (it scales about the hub, since that's its centre), and
        /// <paramref name="nudge"/> slides it outward along its own bisector
        /// so it separates from its neighbours.
        /// </summary>
        public void SetElevation(float scale, float nudge, double duration = 0)
        {
            this.ScaleTo(scale, duration, Easing.OutQuint);

            // Local space rotates with the ring, so "outward along the
            // bisector" is a fixed local direction regardless of where the
            // record currently sits.
            float bisector = MathHelper.DegreesToRadians(Angle + Sweep / 2 - 90);

            this.MoveTo(new Vector2(MathF.Cos(bisector), MathF.Sin(bisector)) * nudge,
                duration, Easing.OutQuint);
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            var texture = CoverArt.Load(renderer, artPath, ArtResolution);

            hasArt = texture != null;

            if (texture != null)
                fill.Texture = texture;
            else
                // A set with unreadable art still gets its wedge, as a flat
                // accent colour — a missing slice would be a gap in the record.
                fill.Alpha = 0;
        }
    }
}
