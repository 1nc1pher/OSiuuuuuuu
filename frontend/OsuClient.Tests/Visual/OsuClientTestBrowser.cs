using osu.Framework.Graphics.Cursor;
using osu.Framework.Platform;
using osu.Framework.Testing;

namespace OsuClient.Tests.Visual
{
    /// <summary>
    /// Hosts osu.Framework's visual test browser, which lists every
    /// <see cref="TestScene"/> in this assembly and runs it interactively.
    /// </summary>
    public partial class OsuClientTestBrowser : osu.Framework.Game
    {
        protected override void LoadComplete()
        {
            base.LoadComplete();

            Add(new TestBrowser("OsuClient"));
            Add(new CursorContainer());
        }

        public override void SetHost(GameHost host)
        {
            base.SetHost(host);

            if (host.Window != null)
                host.Window.CursorState |= CursorState.Hidden;
        }
    }
}
