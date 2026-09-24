using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Screens;
using OsuClient.Game.Beatmaps;
using OsuClient.Game.Screens.Analysis;

namespace OsuClient.Tests.Visual
{
    /// <summary>
    /// The analyser's one load-bearing claim: turning the detector knobs
    /// re-runs the real picker, and landing them on a difficulty's own
    /// sensitivity gives the number the backend recorded for it.
    ///
    /// The picker itself is already proved against the backend in
    /// <see cref="Tests.Backend.OnsetPeakPickerTests"/>. What is only
    /// testable here is the wiring — that the knobs reach it at all, and that
    /// the readout follows. A screen that showed a stale count would look
    /// exactly as convincing as one that worked.
    /// </summary>
    [TestFixture]
    public partial class TestSceneDspInspector : osu.Framework.Testing.TestScene
    {
        private DspInspectorScreen inspector = null!;

        internal static string fixtureFolder =>
            Path.Combine(BeatmapLibrary.FindRepositoryRoot(AppContext.BaseDirectory) ?? string.Empty,
                         "frontend", "OsuClient.Tests", "Fixtures");

        private void pushInspector(string folder)
        {
            AddStep("push inspector", () =>
            {
                var stack = new ScreenStack { RelativeSizeAxes = Axes.Both };
                Child = stack;
                stack.Push(inspector = new DspInspectorScreen(folder));
            });

            AddUntilStep("loaded", () => inspector.IsLoaded);
        }

        /// <summary>
        /// The fixture folder plus a one-second silent WAV, so there is a
        /// track to play — short enough to run off its end inside a test.
        /// </summary>
        internal static string folderWithAudio(int seconds = 1)
        {
            string folder = Path.Combine(Path.GetTempPath(), "osuclient-inspector-" + Path.GetRandomFileName());
            Directory.CreateDirectory(folder);

            foreach (string file in Directory.EnumerateFiles(fixtureFolder))
                File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));

            const int rate = 44100;
            using var writer = new BinaryWriter(File.Create(Path.Combine(folder, "tone.wav")));

            writer.Write("RIFF"u8.ToArray());
            writer.Write(36 + rate * 2 * seconds);
            writer.Write("WAVEfmt "u8.ToArray());
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(rate);
            writer.Write(rate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write("data"u8.ToArray());
            writer.Write(rate * 2 * seconds);
            writer.Write(new byte[rate * 2 * seconds]);

            return folder;
        }

        [Test]
        public void TestSeekingAFinishedSongPlaysItAgain()
        {
            pushInspector(folderWithAudio());

            AddUntilStep("song runs to its end", () => !inspector.IsPlaying);

            // The fixture's analysis describes a longer song than this one
            // second of audio, so the fraction is taken to land 0.3s in.
            AddStep("seek back into it", () => inspector.SeekToFraction(0.3 / inspector.Analysis!.Track.Duration));
            AddAssert("playing again", () => inspector.IsPlaying);
        }

        [Test]
        public void TestRestartPlaysFromTheTop()
        {
            pushInspector(folderWithAudio());

            AddUntilStep("song runs to its end", () => !inspector.IsPlaying);

            AddStep("restart", () => inspector.Restart());
            AddAssert("playing", () => inspector.IsPlaying);
            AddAssert("from the top", () => inspector.PlaybackTime < 500);
        }

        [Test]
        public void TestReadsTheFixtureAnalysisAndTrace()
        {
            pushInspector(fixtureFolder);

            AddAssert("analysis read", () => inspector.Analysis != null);
            AddAssert("trace read", () => inspector.Detail != null);
            AddAssert("picker ran", () => inspector.LiveOnsetCount > 0);
        }

        [Test]
        public void TestEachTiersSensitivityGivesThatTiersRecordedCount()
        {
            pushInspector(fixtureFolder);

            AddAssert("every tier reproduces", () =>
            {
                var detail = inspector.Detail!;

                return detail.Tiers!.All(pair =>
                {
                    var sensitivity = detail.Detection.Tiers[pair.Key];

                    inspector.SetSensitivity(sensitivity.Margin, sensitivity.Delta);

                    return inspector.LiveOnsetCount == pair.Value.Funnel.Detected;
                });
            });
        }

        [Test]
        public void TestALooserSettingFindsMoreThanAStricterOne()
        {
            pushInspector(fixtureFolder);

            int strict = 0;
            int loose = 0;

            AddStep("strict", () =>
            {
                inspector.SetSensitivity(2.6, 0.10);
                strict = inspector.LiveOnsetCount;
            });

            AddStep("loose", () =>
            {
                inspector.SetSensitivity(1.05, 0.02);
                loose = inspector.LiveOnsetCount;
            });

            AddAssert("looser finds more", () => loose > strict);
        }

        [Test]
        public void TestAMapWithoutATraceStillOpens()
        {
            // Every set generated before the trace existed. The screen has to
            // come up and say so, not fail to build.
            pushInspector(Path.GetTempPath());

            AddAssert("no trace", () => inspector.Detail == null);
            AddAssert("nothing picked", () => inspector.LiveOnsetCount == 0);
        }
    }
}
