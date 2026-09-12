using System;
using osu.Framework;
using osu.Framework.Platform;

namespace OsuClient.Tests.Visual
{
    /// <summary>
    /// Entry point for the interactive visual test browser:
    /// <c>dotnet run --project frontend/OsuClient.Tests</c>.
    /// <c>dotnet test</c> ignores this and runs the NUnit tests instead.
    /// </summary>
    public static class VisualTestRunner
    {
        [STAThread]
        public static int Main()
        {
            using (DesktopGameHost host = Host.GetSuitableDesktopHost(@"osu-client-visual-tests",
                       new HostOptions { PortableInstallation = true }))
            {
                host.Run(new OsuClientTestBrowser());
                return 0;
            }
        }
    }
}
