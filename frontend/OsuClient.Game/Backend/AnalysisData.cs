using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OsuClient.Game.Backend
{
    /// <summary>One onset the detector found (Step 2).</summary>
    public sealed class AnalysisOnset
    {
        /// <summary>Seconds into the track.</summary>
        public double Time { get; init; }

        /// <summary>Normalised spectral flux at that moment, 0..1.</summary>
        public double Strength { get; init; }

        /// <summary>Which frequency band contributed most: <c>low</c>, <c>mid</c> or <c>high</c>.</summary>
        public string Band { get; init; } = string.Empty;
    }

    /// <summary>The constant-tempo beat grid the tracker settled on (Step 3).</summary>
    public sealed class AnalysisBeatGrid
    {
        public double Bpm { get; init; }

        /// <summary>Time of the first beat, in seconds.</summary>
        public double Offset { get; init; }

        /// <summary>How periodic the track looked, 0..1.</summary>
        public double Confidence { get; init; }

        public IReadOnlyList<double> BeatTimes { get; init; } = Array.Empty<double>();
    }

    /// <summary>One placed object, with the provenance the <c>.osu</c> file has no room for (Step 4).</summary>
    public sealed class AnalysisHitObject
    {
        /// <summary><c>circle</c>, <c>slider</c> or <c>spinner</c>.</summary>
        public string Kind { get; init; } = string.Empty;

        public double Time { get; init; }

        /// <summary>Centre position in osu!pixels, on the 512x384 playfield.</summary>
        public double X { get; init; }

        public double Y { get; init; }

        /// <summary>Strength of the onset this came from, 0..1.</summary>
        public double Strength { get; init; }

        /// <summary>The source onset's dominant band — why this became the kind it did.</summary>
        public string Band { get; init; } = string.Empty;

        /// <summary>The subdivision it snapped to: <c>1/1</c>, <c>1/2</c>, <c>1/4</c>.</summary>
        public string Snap { get; init; } = string.Empty;

        /// <summary>When it finishes, for objects that last (sliders, spinners); null for circles.</summary>
        public double? EndTime { get; init; }

        /// <summary>How long it lasts, or zero for an instant.</summary>
        public double Duration => Math.Max(0, (EndTime ?? Time) - Time);
    }

    /// <summary>What was analysed.</summary>
    public sealed class AnalysisTrack
    {
        public string Name { get; init; } = string.Empty;

        /// <summary>Length in seconds.</summary>
        public double Duration { get; init; }

        public int SampleRate { get; init; }
    }

    /// <summary>
    /// The backend's <c>analysis.json</c>: what the DSP saw, written next to
    /// the beatmap it produced (FRONTEND_PLAN.md Phase 7,
    /// <c>src/export/analysis_export.py</c>).
    ///
    /// This is the whole coupling between the two halves of the project for
    /// the visualization — a file in the beatmap folder, read once after the
    /// run finishes, exactly like the <c>.osz</c>. Nothing here streams from
    /// or waits on the Python process.
    ///
    /// Every load path is forgiving on purpose. A beatmap generated before
    /// this existed, a half-written file from an interrupted run, or a future
    /// backend writing a version this build doesn't know all mean the same
    /// thing to the frontend: no visualization, go straight to the map.
    /// </summary>
    public sealed class AnalysisData
    {
        /// <summary>The file name the backend writes inside the beatmap folder.</summary>
        public const string FileName = "analysis.json";

        /// <summary>The spectrogram image written beside it.</summary>
        public const string SpectrogramFileName = "spectrogram.png";

        /// <summary>The schema version this build understands.</summary>
        public const int SupportedVersion = 1;

        private static readonly JsonSerializerOptions options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };

        public int Version { get; init; }

        public AnalysisTrack Track { get; init; } = new AnalysisTrack();

        /// <summary>Which difficulty's detection pass <see cref="Onsets"/> came from.</summary>
        public string OnsetSource { get; init; } = string.Empty;

        public IReadOnlyList<AnalysisOnset> Onsets { get; init; } = Array.Empty<AnalysisOnset>();

        public AnalysisBeatGrid BeatGrid { get; init; } = new AnalysisBeatGrid();

        /// <summary>Placed objects per difficulty tier, keyed by tier name.</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<AnalysisHitObject>> HitObjects { get; init; } =
            new Dictionary<string, IReadOnlyList<AnalysisHitObject>>();

        /// <summary>
        /// Whether there is enough here to be worth animating. An analysis
        /// with no onsets and no objects — a silent or very short track —
        /// would play as an empty screen.
        /// </summary>
        public bool HasContent => Onsets.Count > 0 || HitObjects.Values.Any(list => list.Count > 0);

        /// <summary>
        /// The tier to show in the hit-object stage: the one the onsets came
        /// from when it's present, otherwise whichever has the most objects,
        /// since that is the fullest thing to look at.
        /// </summary>
        public string PreferredTier
        {
            get
            {
                if (HitObjects.Count == 0)
                    return string.Empty;

                if (!string.IsNullOrEmpty(OnsetSource) && HitObjects.ContainsKey(OnsetSource))
                    return OnsetSource;

                return HitObjects.OrderByDescending(pair => pair.Value.Count).First().Key;
            }
        }

        /// <summary>Objects for one tier, or an empty list if it isn't in the file.</summary>
        public IReadOnlyList<AnalysisHitObject> ObjectsFor(string tier) =>
            HitObjects.TryGetValue(tier, out var objects) ? objects : Array.Empty<AnalysisHitObject>();

        /// <summary>
        /// Reads the analysis out of a beatmap folder, or returns null when
        /// there isn't a usable one — see the class remarks for why that is a
        /// normal outcome rather than an error.
        /// </summary>
        public static AnalysisData? LoadFromFolder(string? beatmapFolder)
        {
            if (string.IsNullOrWhiteSpace(beatmapFolder))
                return null;

            return Load(Path.Combine(beatmapFolder, FileName));
        }

        /// <summary>Reads one <c>analysis.json</c>, or returns null if it can't be used.</summary>
        public static AnalysisData? Load(string path)
        {
            if (!File.Exists(path))
                return null;

            try
            {
                return Parse(File.ReadAllText(path));
            }
            catch (IOException)
            {
                return null;
            }
        }

        /// <summary>Parses analysis JSON, or returns null if it isn't a version this build can animate.</summary>
        public static AnalysisData? Parse(string json)
        {
            try
            {
                var data = JsonSerializer.Deserialize<AnalysisData>(json, options);

                // A newer backend may add fields this build ignores happily,
                // but a different major version means a different document.
                if (data == null || data.Version != SupportedVersion)
                    return null;

                return data;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>The spectrogram image beside an analysis file, or null when it isn't there.</summary>
        public static string? FindSpectrogram(string? beatmapFolder)
        {
            if (string.IsNullOrWhiteSpace(beatmapFolder))
                return null;

            string path = Path.Combine(beatmapFolder, SpectrogramFileName);

            return File.Exists(path) ? path : null;
        }
    }
}
