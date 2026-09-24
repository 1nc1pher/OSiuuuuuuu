using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using OsuClient.Game.Screens.Gameplay;
using OsuClient.Game.Screens.Results;

namespace OsuClient.Game.Scores
{
    /// <summary>The best run on one map: what song select shows and a new run has to beat.</summary>
    public sealed class HighScore
    {
        public string Title { get; set; } = string.Empty;
        public string Difficulty { get; set; } = string.Empty;

        public long Score { get; set; }
        public double Accuracy { get; set; }
        public int MaxCombo { get; set; }
        public Grade Grade { get; set; }

        public int CountGreat { get; set; }
        public int CountOk { get; set; }
        public int CountMeh { get; set; }
        public int CountMiss { get; set; }

        /// <summary>When it was set, in UTC.</summary>
        public DateTime AchievedAt { get; set; }

        /// <summary>Whether <paramref name="other"/> beats this: more score, or the same score more accurately.</summary>
        public bool IsBeatenBy(HighScore other) =>
            other.Score > Score || (other.Score == Score && other.Accuracy > Accuracy);

        public static HighScore From(ResultsScreen.Result result, DateTime achievedAt) => new HighScore
        {
            Title = result.Title,
            Difficulty = result.Difficulty,
            Score = result.Score,
            Accuracy = result.Accuracy,
            MaxCombo = result.MaxCombo,
            Grade = result.Grade,
            CountGreat = result.CountGreat,
            CountOk = result.CountOk,
            CountMeh = result.CountMeh,
            CountMiss = result.CountMiss,
            AchievedAt = achievedAt,
        };
    }

    /// <summary>
    /// The best score on every map played, kept on disk between sessions.
    ///
    /// <para>
    /// Scores are kept against a map's content (<see cref="Beatmaps.Beatmap.ContentHash"/>),
    /// not its name: regenerating a song writes a new map under the same
    /// title and difficulty, and a score set on the old one would claim a
    /// map it was never played on. A failed run never sets one — a score
    /// from a map not survived is not a best on it.
    /// </para>
    ///
    /// <para>
    /// One small JSON file, rewritten whole on each new best: a map count in
    /// the hundreds is a few kilobytes. Written to a temporary file and then
    /// moved into place, so a crash mid-write cannot leave half a file that
    /// loses every score. A file that is missing or unreadable is an empty
    /// table, never an error — nothing here is worth failing to start for.
    /// </para>
    /// </summary>
    public sealed class HighScoreStore
    {
        private static readonly JsonSerializerOptions json_options = new JsonSerializerOptions { WriteIndented = true };

        private readonly string? path;
        private readonly Dictionary<string, HighScore> scores;

        /// <param name="path">Where the table lives. Null keeps it in memory only.</param>
        public HighScoreStore(string? path)
        {
            this.path = path;
            scores = load(path);
        }

        /// <summary>How many maps have a high score. Exposed for tests.</summary>
        public int Count => scores.Count;

        /// <summary>The best run on a map, or null when it has none.</summary>
        public HighScore? Get(string contentHash) =>
            !string.IsNullOrEmpty(contentHash) && scores.TryGetValue(contentHash, out var best) ? best : null;

        /// <summary>
        /// Offers a finished run. Returns true when it is a new best on its
        /// map — which then replaces the old one and is saved.
        /// </summary>
        public bool Submit(string contentHash, ResultsScreen.Result result, DateTime? now = null)
        {
            if (result.Failed || string.IsNullOrEmpty(contentHash))
                return false;

            var candidate = HighScore.From(result, now ?? DateTime.UtcNow);

            if (scores.TryGetValue(contentHash, out var current) && !current.IsBeatenBy(candidate))
                return false;

            scores[contentHash] = candidate;
            save();

            return true;
        }

        private static Dictionary<string, HighScore> load(string? path)
        {
            try
            {
                if (path != null && File.Exists(path))
                {
                    var read = JsonSerializer.Deserialize<Dictionary<string, HighScore>>(File.ReadAllText(path), json_options);

                    if (read != null)
                        return read;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
            {
            }

            return new Dictionary<string, HighScore>();
        }

        private void save()
        {
            if (path == null)
                return;

            try
            {
                string? directory = Path.GetDirectoryName(path);

                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                string temporary = path + ".tmp";

                File.WriteAllText(temporary, JsonSerializer.Serialize(scores, json_options));
                File.Move(temporary, path, overwrite: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Losing one save costs this best, not the game. It is still
                // held in memory for the rest of the session.
            }
        }
    }
}
