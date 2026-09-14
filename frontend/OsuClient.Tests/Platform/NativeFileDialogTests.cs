using System;
using System.Linq;
using NUnit.Framework;
using OsuClient.Game.Backend;
using OsuClient.Game.Platform;

namespace OsuClient.Tests.Platform
{
    /// <summary>
    /// The parts of the native picker that can be checked without a user in
    /// front of it: the comdlg32 filter string, whose null-separated layout is
    /// otherwise only visibly wrong once the dialog is open and showing no
    /// files.
    /// </summary>
    [TestFixture]
    public class NativeFileDialogTests
    {
        [Test]
        public void TestFilterListsEveryExtensionTheBackendAccepts()
        {
            string filter = NativeFileDialog.BuildFilter(BackendRunner.SupportedAudioExtensions);

            foreach (string extension in BackendRunner.SupportedAudioExtensions)
                Assert.That(filter, Does.Contain("*" + extension));
        }

        [Test]
        public void TestFilterIsNullSeparatedLabelAndPatternPairs()
        {
            string filter = NativeFileDialog.BuildFilter(new[] { ".mp3", ".wav" });

            // comdlg32 reads label\0pattern\0…\0, so an odd number of parts (or
            // a missing trailing null) leaves it reading past the end.
            Assert.That(filter, Does.EndWith("\0"));

            string[] parts = filter.Split('\0', StringSplitOptions.RemoveEmptyEntries);

            Assert.That(parts.Length % 2, Is.Zero);
            Assert.That(parts[0], Is.EqualTo("Audio files (*.mp3;*.wav)"));
            Assert.That(parts[1], Is.EqualTo("*.mp3;*.wav"));
            Assert.That(parts.Last(), Is.EqualTo("*.*"));
        }

        [Test]
        public void TestExtensionsAreDotPrefixedEitherWay()
        {
            Assert.That(NativeFileDialog.BuildFilter(new[] { "mp3" }), Does.Contain("*.mp3"));
            Assert.That(NativeFileDialog.BuildFilter(new[] { "mp3" }), Does.Not.Contain("*mp3;"));
        }

        [Test]
        public void TestNoExtensionsStillGivesAUsableFilter()
        {
            string filter = NativeFileDialog.BuildFilter(Array.Empty<string>());

            Assert.That(filter, Is.EqualTo("All files (*.*)\0*.*\0"));
        }

        [Test]
        public void TestWindowsHasAPicker()
        {
            if (!OperatingSystem.IsWindows())
                Assert.Ignore("Only Windows has a native picker implementation.");

            Assert.That(NativeFileDialog.IsSupported, Is.True);
        }

        [Test]
        public void TestUnsupportedPlatformsReportFailureRatherThanHanging()
        {
            if (OperatingSystem.IsWindows())
                Assert.Ignore("Windows has a picker, so this path isn't taken here.");

            FileDialogResult result = default;

            NativeFileDialog.OpenFile("pick", new[] { ".mp3" }, null, r => result = r);

            Assert.That(result.Error, Is.Not.Null);
            Assert.That(result.Path, Is.Null);
        }
    }
}
