using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace OsuClient.Game.Screens.Generation
{
    /// <summary>A stage of the backend run, as its own output reports it.</summary>
    public enum PipelineStage
    {
        /// <summary>The process has started but hasn't said anything yet.</summary>
        Waiting,

        /// <summary>Decoding the audio file.</summary>
        Loading,

        /// <summary>Detecting onsets, classifying and placing — once per difficulty.</summary>
        Mapping,

        /// <summary>Writing the <c>.osu</c> files and packaging the <c>.osz</c>.</summary>
        Writing,

        /// <summary>Writing <c>analysis.json</c>, <c>dsp.json</c> and the spectrogram.</summary>
        Analysing,

        /// <summary>The run has ended, either way.</summary>
        Done,
    }

    /// <summary>
    /// Turns the backend's own stdout into the state the progress panel
    /// draws.
    ///
    /// <para>
    /// <b>This is the whole of the honest progress indication.</b> Nothing
    /// streams from the Python process except these lines — there is no
    /// percentage to be had, and the panel does not invent one. What there
    /// *is*, and what this extracts, is which stage is running and, during
    /// the slow one, which difficulty of how many. That is a real number and
    /// it is the only one the screen claims.
    /// </para>
    ///
    /// <para>
    /// <b>This is a real coupling with `src/main.py`.</b> The strings come
    /// from its <c>report()</c> calls; change one there and the lamps here
    /// stop lighting, silently, because an unrecognised line is simply log
    /// output. Both files carry a comment pointing at the other, and
    /// <c>PipelineProgressTests</c> asserts against strings copied from it.
    /// </para>
    ///
    /// <para>
    /// Pure, with no framework types, so the parsing is pinned by ordinary
    /// unit tests rather than by watching a real generation go past.
    /// </para>
    /// </summary>
    public class PipelineProgress
    {
        /// <summary>
        /// The stages a lamp row shows, in order.
        ///
        /// Four, not five. An earlier sketch had DETECT and MAP as separate
        /// lamps, but the backend emits <i>one</i> line covering both
        /// ("Analysing and mapping Hard") — lighting two lamps off one signal
        /// would be the panel inventing a stage boundary it cannot see.
        /// </summary>
        public static readonly IReadOnlyList<PipelineStage> LampStages = new[]
        {
            PipelineStage.Loading,
            PipelineStage.Mapping,
            PipelineStage.Writing,
            PipelineStage.Analysing,
        };

        /// <summary>Short legends for <see cref="LampStages"/>, in the same order.</summary>
        public static string LegendFor(PipelineStage stage) => stage switch
        {
            PipelineStage.Loading => "LOAD",
            PipelineStage.Mapping => "MAP",
            PipelineStage.Writing => "WRITE",
            PipelineStage.Analysing => "ANALYSE",
            _ => stage.ToString().ToUpperInvariant(),
        };

        // "[3/5] Analysing and mapping Hard" — the only line carrying numbers.
        private static readonly Regex mapping_line = new Regex(
            @"^\[(?<index>\d+)/(?<count>\d+)\]\s+Analysing and mapping\s+(?<tier>.+?)\s*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex loading_line = new Regex(
            @"^Loading\s+(?<file>.+?)\s*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>The stage currently running.</summary>
        public PipelineStage Stage { get; private set; } = PipelineStage.Waiting;

        /// <summary>The file being decoded, once the run has said which.</summary>
        public string SourceFile { get; private set; } = string.Empty;

        /// <summary>Which difficulty is being mapped, 1-based. Zero before mapping starts.</summary>
        public int TierIndex { get; private set; }

        /// <summary>How many difficulties this run is building. Zero before mapping starts.</summary>
        public int TierCount { get; private set; }

        /// <summary>The difficulty being mapped.</summary>
        public string TierName { get; private set; } = string.Empty;

        /// <summary>Whether a tier counter can be shown yet.</summary>
        public bool HasTierProgress => TierCount > 0;

        /// <summary>
        /// Consumes one line of backend output. Returns whether it changed
        /// anything, so a caller can skip redrawing for the majority of lines
        /// that are ordinary log noise.
        /// </summary>
        public bool Apply(string? line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return false;

            line = line.Trim();

            var mapping = mapping_line.Match(line);

            if (mapping.Success)
            {
                Stage = PipelineStage.Mapping;
                TierIndex = int.Parse(mapping.Groups["index"].Value, CultureInfo.InvariantCulture);
                TierCount = int.Parse(mapping.Groups["count"].Value, CultureInfo.InvariantCulture);
                TierName = mapping.Groups["tier"].Value;
                return true;
            }

            // Ordered before the generic "Loading" match only for clarity;
            // the two cannot both hit.
            if (line.Equals("Writing beatmap files", StringComparison.Ordinal))
            {
                Stage = PipelineStage.Writing;
                return true;
            }

            if (line.Equals("Writing analysis data", StringComparison.Ordinal))
            {
                Stage = PipelineStage.Analysing;
                return true;
            }

            var loading = loading_line.Match(line);

            if (loading.Success)
            {
                Stage = PipelineStage.Loading;
                SourceFile = loading.Groups["file"].Value;
                return true;
            }

            // Everything else is the run's own report at the end -- beatmap
            // folder, per-tier counts, file sizes -- or anything Python
            // decided to print. It belongs in the log, not in the lamps.
            return false;
        }

        /// <summary>Marks the run finished, whatever stage it stopped at.</summary>
        public void Finish() => Stage = PipelineStage.Done;

        /// <summary>
        /// How a lamp should read for a stage: lit once passed, breathing on
        /// the one running, dark ahead.
        ///
        /// A finished run lights every lamp, including any the output never
        /// mentioned — which is honest, because reaching the end means they
        /// all happened whether or not each printed a line.
        /// </summary>
        public LampReading ReadingFor(PipelineStage stage)
        {
            if (Stage == PipelineStage.Done)
                return LampReading.Passed;

            if (Stage == stage)
                return LampReading.Running;

            return Stage > stage ? LampReading.Passed : LampReading.Pending;
        }
    }

    /// <summary>How one stage lamp should read.</summary>
    public enum LampReading
    {
        /// <summary>Not reached yet.</summary>
        Pending,

        /// <summary>Happening now.</summary>
        Running,

        /// <summary>Finished.</summary>
        Passed,
    }
}
