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
    /// <c>--bench-gameplay</c> measures frame pacing during autoplayed
    /// gameplay — see <see cref="GameplayFrameBench"/>.
    /// </summary>
    public static class VisualTestRunner
    {
        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--screenshot")
                return ScreenshotHarness.Run(args);

            if (args.Length > 0 && args[0] == "--record-gameplay")
                return GameplayRecorder.Run(args);

            if (args.Length > 0 && args[0] == "--bench-gameplay")
                return GameplayFrameBench.Run(args);

            using (DesktopGameHost host = Host.GetSuitableDesktopHost(@"osu-client-visual-tests",
                       new HostOptions { PortableInstallation = true }))
            {
                host.Run(new OsuClientTestBrowser());
                return 0;
            }
        }
    }
}
