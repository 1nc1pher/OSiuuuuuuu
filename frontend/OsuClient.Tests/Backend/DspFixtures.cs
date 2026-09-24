using System;
using System.IO;
using NUnit.Framework;
using OsuClient.Game.Backend;
using OsuClient.Game.Beatmaps;

namespace OsuClient.Tests.Backend
{
    /// <summary>
    /// Locates the files the backend wrote for these tests to read.
    ///
    /// Two sets, in two places, for two reasons:
    ///
    /// <list type="bullet">
    /// <item><c>tests/fixtures/curve_pack_reference.json</c> — the curve
    /// format's shared reference, asserted against by the Python tests too.
    /// It lives on the backend's side precisely so that neither language owns
    /// it: if the two decoders ever disagree, both suites go red at once.</item>
    /// <item><c>frontend/OsuClient.Tests/Fixtures/</c> — a real
    /// <c>analysis.json</c> and <c>dsp.json</c> from an actual pipeline run
    /// over a short synthetic track, written by
    /// <c>tests/fixtures/make_client_fixture.py</c>. Hand-written JSON could
    /// never support the assertion that matters here — that re-picking onsets
    /// from the exported curves reproduces the backend's own list — because it
    /// would only prove the reader parses what the test author imagined the
    /// writer emits.</item>
    /// </list>
    ///
    /// Both are found by walking up to the repository root rather than by
    /// copying files into the build output, so there is one mechanism and it
    /// is the one <see cref="BeatmapLibrary.FindRepositoryRoot"/> already
    /// implements. Outside a checkout there is no root and the tests that need
    /// these are skipped rather than failed — an installed copy of the game is
    /// not a broken one.
    /// </summary>
    public static class DspFixtures
    {
        /// <summary>The repository root, or null when running outside a checkout.</summary>
        public static string? RepositoryRoot => BeatmapLibrary.FindRepositoryRoot(AppContext.BaseDirectory);

        /// <summary>The shared curve-format reference, agreed with the Python tests.</summary>
        public static string CurvePackReferencePath =>
            Path.Combine(Require(), "tests", "fixtures", "curve_pack_reference.json");

        /// <summary>The folder holding a real generated analysis.</summary>
        public static string BeatmapFolder =>
            Path.Combine(Require(), "frontend", "OsuClient.Tests", "Fixtures");

        /// <summary>The repository root, or an ignored test if there isn't one.</summary>
        public static string Require()
        {
            string? root = RepositoryRoot;

            if (root == null)
                Assert.Ignore("not running inside the repository — no backend fixtures to read");

            return root!;
        }

        /// <summary>The real <c>dsp.json</c>, or an ignored test if it hasn't been generated.</summary>
        public static DspDetail RequireDetail()
        {
            var detail = DspDetail.LoadFromFolder(BeatmapFolder);

            if (detail == null)
            {
                Assert.Ignore("no dsp.json fixture — regenerate with "
                              + "python tests/fixtures/make_client_fixture.py");
            }

            return detail!;
        }

        /// <summary>The real <c>analysis.json</c> beside it.</summary>
        public static AnalysisData RequireAnalysis()
        {
            var analysis = AnalysisData.LoadFromFolder(BeatmapFolder);

            if (analysis == null)
            {
                Assert.Ignore("no analysis.json fixture — regenerate with "
                              + "python tests/fixtures/make_client_fixture.py");
            }

            return analysis!;
        }
    }
}
