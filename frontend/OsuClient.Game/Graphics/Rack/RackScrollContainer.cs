using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osuTK;

namespace OsuClient.Game.Graphics.Rack
{
    /// <summary>
    /// A vertical scroll area for inside a <see cref="RackPanel"/>, for
    /// content that is taller than the panel it sits in.
    ///
    /// The framework's own scrollbar is a flat grey block that reads as a
    /// debugging aid on a rack; this one is a thin chrome rail that brightens
    /// under the pointer, the same metal the panel's border is made of.
    /// </summary>
    public partial class RackScrollContainer : ScrollContainer<Drawable>
    {
        public RackScrollContainer()
            : base(Direction.Vertical)
        {
            ScrollbarOverlapsContent = false;
        }

        protected override ScrollbarContainer CreateScrollbar(Direction direction) => new RackScrollbar(direction);

        private partial class RackScrollbar : ScrollbarContainer
        {
            private const float rail_width = 4;

            public RackScrollbar(Direction direction)
                : base(direction)
            {
                Child = new Box { RelativeSizeAxes = Axes.Both };
                Colour = RetroPalette.Chrome.Opacity(0.45f);
                Masking = true;
                CornerRadius = rail_width / 2;
                Margin = new MarginPadding { Left = 4 };
            }

            public override void ResizeTo(float val, int duration = 0, Easing easing = Easing.None)
            {
                Vector2 size = new Vector2(rail_width)
                {
                    [(int)ScrollDirection] = val,
                };

                this.ResizeTo(size, duration, easing);
            }

            protected override bool OnHover(osu.Framework.Input.Events.HoverEvent e)
            {
                this.FadeColour(RetroPalette.Chrome, 120, Easing.OutQuint);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(osu.Framework.Input.Events.HoverLostEvent e)
            {
                this.FadeColour(RetroPalette.Chrome.Opacity(0.45f), 200, Easing.OutQuint);
                base.OnHoverLost(e);
            }
        }
    }
}
