using System;
using osu.Framework;
using osu.Framework.Platform;

namespace OsuClient.Tests.Visual
{
    /// <summary>
    /// Entry point for the interactive visual test browser:
    /// <c>dotnet run --project frontend/OsuClient.Tests</c>.
    /// <c>dotnet test</c> ignores this and runs the NUnit tests instead.
    ///
    /// Passing <c>--screenshot &lt;scene&gt; &lt;out.png&gt;</c> renders one
    /// frame to a file and exits instead — see <see cref="ScreenshotHarness"/>.
    /// </summary>
    public static class VisualTestRunner
    {
        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--screenshot")
                return ScreenshotHarness.Run(args);

            using (DesktopGameHost host = Host.GetSuitableDesktopHost(@"osu-client-visual-tests",
                       new HostOptions { PortableInstallation = true }))
            {
                host.Run(new OsuClientTestBrowser());
                return 0;
            }
        }
    }
}
