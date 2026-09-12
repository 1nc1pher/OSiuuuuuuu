using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using OsuClient.Game.Beatmaps.HitObjects;
using osuTK;

namespace OsuClient.Game.Beatmaps
{
    /// <summary>Thrown when a .osu file can't be parsed.</summary>
    public class BeatmapDecodeException : Exception
    {
        public BeatmapDecodeException(string message)
            : base(message)
        {
        }

        public BeatmapDecodeException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }

    /// <summary>
    /// Decodes the .osu v14 text format into a <see cref="Beatmap"/>.
    ///
    /// Scope is the exact subset the Python backend's
    /// <c>src/export/osu_writer.py</c> emits:
    /// <c>[General]</c>, <c>[Metadata]</c>, <c>[Difficulty]</c>, one uninherited
    /// <c>[TimingPoints]</c> line, and <c>[HitObjects]</c> holding circles,
    /// linear sliders and spinners with new-combo flags. <c>[Editor]</c> and
    /// <c>[Events]</c> are recognised and skipped; unknown sections are ignored
    /// rather than treated as errors, so a hand-made map with extra sections
    /// still loads.
    ///
    /// Every number is parsed with <see cref="CultureInfo.InvariantCulture"/>.
    /// The format always uses '.' as the decimal separator, so parsing under a
    /// locale that uses ',' would otherwise silently mangle beat lengths and
    /// slider lengths.
    /// </summary>
    public static class BeatmapDecoder
    {
        private static readonly Regex format_header =
            new Regex(@"^osu file format v(?<version>\d+)\s*$", RegexOptions.Compiled);

        public static Beatmap DecodeFile(string path)
        {
            using var stream = File.OpenRead(path);
            return Decode(stream);
        }

        public static Beatmap Decode(Stream stream)
        {
            // detectEncodingFromByteOrderMarks strips a UTF-8 BOM, which would
            // otherwise break the strict first-line header match.
            using var reader = new StreamReader(stream, Encoding.UTF8, true, 1024, true);
            return Decode(reader);
        }

        public static Beatmap Decode(string text)
        {
            using var reader = new StringReader(text);
            return Decode(reader);
        }

        public static Beatmap Decode(TextReader reader)
        {
            var beatmap = new Beatmap();

            string? section = null;
            bool headerSeen = false;
            int lineNumber = 0;

            // Sliders can't be resolved until [Difficulty] and [TimingPoints]
            // are known, and the format puts [HitObjects] last but makes no
            // promise about it. Collect them and resolve after the whole file.
            var pendingSliders = new List<SliderData>();

            string? raw;

            while ((raw = reader.ReadLine()) != null)
            {
                lineNumber++;

                string line = stripBom(raw, lineNumber).Trim();

                if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
                    continue;

                if (!headerSeen)
                {
                    var match = format_header.Match(line);

                    if (!match.Success)
                        throw new BeatmapDecodeException(
                            $"not a .osu file: expected an \"osu file format vN\" header, got \"{line}\"");

                    beatmap.FormatVersion = int.Parse(match.Groups["version"].Value, CultureInfo.InvariantCulture);
                    headerSeen = true;
                    continue;
                }

                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                {
                    section = line.Substring(1, line.Length - 2);
                    continue;
                }

                try
                {
                    switch (section)
                    {
                        case "General":
                            parseGeneral(beatmap, line);
                            break;

                        case "Metadata":
                            parseMetadata(beatmap, line);
                            break;

                        case "Difficulty":
                            parseDifficulty(beatmap, line);
                            break;

                        case "TimingPoints":
                            beatmap.TimingPoints.Add(parseTimingPoint(line));
                            break;

                        case "HitObjects":
                            var hitObject = parseHitObject(line);
                            beatmap.HitObjects.Add(hitObject);

                            if (hitObject is SliderData slider)
                                pendingSliders.Add(slider);
                            break;

                        // [Editor], [Events], [Colours] and anything else carry
                        // nothing this client needs yet.
                    }
                }
                catch (BeatmapDecodeException e)
                {
                    throw new BeatmapDecodeException($"line {lineNumber}: {e.Message}", e);
                }
                catch (Exception e)
                {
                    throw new BeatmapDecodeException($"line {lineNumber}: could not parse \"{line}\"", e);
                }
            }

            if (!headerSeen)
                throw new BeatmapDecodeException("empty file: no \"osu file format vN\" header found");

            beatmap.TimingPoints.Sort((a, b) => a.Time.CompareTo(b.Time));
            beatmap.HitObjects.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));

            foreach (var slider in pendingSliders)
                slider.SetEndTime(slider.StartTime + beatmap.SliderDuration(slider.StartTime, slider.PixelLength, slider.Slides));

            return beatmap;
        }

        private static string stripBom(string line, int lineNumber) =>
            lineNumber == 1 ? line.TrimStart('﻿') : line;

        // ------------------------------------------------------------------
        // Key: value sections
        // ------------------------------------------------------------------

        /// <summary>
        /// Splits a <c>Key:Value</c> line. The backend writes no space after the
        /// colon; osu!'s own editor writes "Key: Value". Both are accepted, and
        /// a value containing ':' (a Windows path in AudioFilename, say) is kept
        /// intact by splitting on the FIRST colon only.
        /// </summary>
        private static (string key, string value) splitKeyValue(string line)
        {
            int index = line.IndexOf(':');

            if (index < 0)
                throw new BeatmapDecodeException($"expected \"Key:Value\", got \"{line}\"");

            return (line.Substring(0, index).Trim(), line.Substring(index + 1).Trim());
        }

        private static void parseGeneral(Beatmap beatmap, string line)
        {
            var (key, value) = splitKeyValue(line);

            switch (key)
            {
                case "AudioFilename":
                    beatmap.General.AudioFilename = value;
                    break;

                case "AudioLeadIn":
                    beatmap.General.AudioLeadIn = parseInt(value, key);
                    break;

                case "PreviewTime":
                    beatmap.General.PreviewTime = parseInt(value, key);
                    break;

                case "SampleSet":
                    beatmap.General.SampleSet = value;
                    break;

                case "StackLeniency":
                    beatmap.General.StackLeniency = parseDouble(value, key);
                    break;

                case "Mode":
                    beatmap.General.Mode = parseInt(value, key);
                    break;
            }
        }

        private static void parseMetadata(Beatmap beatmap, string line)
        {
            var (key, value) = splitKeyValue(line);

            switch (key)
            {
                case "Title":
                    beatmap.Metadata.Title = value;
                    break;

                case "TitleUnicode":
                    beatmap.Metadata.TitleUnicode = value;
                    break;

                case "Artist":
                    beatmap.Metadata.Artist = value;
                    break;

                case "ArtistUnicode":
                    beatmap.Metadata.ArtistUnicode = value;
                    break;

                case "Creator":
                    beatmap.Metadata.Creator = value;
                    break;

                case "Version":
                    beatmap.Metadata.Version = value;
                    break;

                case "Source":
                    beatmap.Metadata.Source = value;
                    break;

                case "Tags":
                    beatmap.Metadata.Tags = value;
                    break;

                case "BeatmapID":
                    beatmap.Metadata.BeatmapID = parseInt(value, key);
                    break;

                case "BeatmapSetID":
                    beatmap.Metadata.BeatmapSetID = parseInt(value, key);
                    break;
            }
        }

        private static void parseDifficulty(Beatmap beatmap, string line)
        {
            var (key, value) = splitKeyValue(line);

            switch (key)
            {
                case "HPDrainRate":
                    beatmap.Difficulty.HPDrainRate = parseDouble(value, key);
                    break;

                case "CircleSize":
                    beatmap.Difficulty.CircleSize = parseDouble(value, key);
                    break;

                case "OverallDifficulty":
                    beatmap.Difficulty.OverallDifficulty = parseDouble(value, key);
                    break;

                case "ApproachRate":
                    beatmap.Difficulty.ApproachRate = parseDouble(value, key);
                    break;

                case "SliderMultiplier":
                    beatmap.Difficulty.SliderMultiplier = parseDouble(value, key);
                    break;

                case "SliderTickRate":
                    beatmap.Difficulty.SliderTickRate = parseDouble(value, key);
                    break;
            }
        }

        // ------------------------------------------------------------------
        // Comma-separated record sections
        // ------------------------------------------------------------------

        /// <summary>
        /// <c>time,beatLength,meter,sampleSet,sampleIndex,volume,uninherited,effects</c>
        /// Only the first two fields are required; older files omit the rest.
        /// </summary>
        private static TimingPoint parseTimingPoint(string line)
        {
            string[] parts = line.Split(',');

            if (parts.Length < 2)
                throw new BeatmapDecodeException($"timing point needs at least 2 fields, got {parts.Length}");

            var point = new TimingPoint
            {
                Time = parseDouble(parts[0], "time"),
                BeatLength = parseDouble(parts[1], "beatLength"),
            };

            if (parts.Length > 2) point.Meter = parseInt(parts[2], "meter");
            if (parts.Length > 3) point.SampleSet = parseInt(parts[3], "sampleSet");
            if (parts.Length > 4) point.SampleIndex = parseInt(parts[4], "sampleIndex");
            if (parts.Length > 5) point.Volume = parseInt(parts[5], "volume");
            if (parts.Length > 7) point.Effects = parseInt(parts[7], "effects");

            // The uninherited flag only exists from v6 onwards. When it's absent,
            // osu! infers the kind from the sign of beatLength.
            point.Uninherited = parts.Length > 6
                ? parseInt(parts[6], "uninherited") != 0
                : point.BeatLength > 0;

            return point;
        }

        private static HitObjectData parseHitObject(string line)
        {
            string[] parts = line.Split(',');

            if (parts.Length < 5)
                throw new BeatmapDecodeException($"hit object needs at least 5 fields, got {parts.Length}");

            float x = (float)parseDouble(parts[0], "x");
            float y = (float)parseDouble(parts[1], "y");
            double time = parseDouble(parts[2], "time");
            int type = parseInt(parts[3], "type");
            int hitSound = parseInt(parts[4], "hitSound");

            HitObjectData hitObject;

            if ((type & HitObjectFlags.Hold) != 0)
                throw new BeatmapDecodeException("osu!mania hold notes are not supported");

            if ((type & HitObjectFlags.Spinner) != 0)
                hitObject = parseSpinner(parts, time);
            else if ((type & HitObjectFlags.Slider) != 0)
                hitObject = parseSlider(parts, new Vector2(x, y));
            else if ((type & HitObjectFlags.Circle) != 0)
                hitObject = new HitCircleData();
            else
                throw new BeatmapDecodeException($"hit object type {type} has no circle/slider/spinner bit set");

            hitObject.Position = new Vector2(x, y);
            hitObject.StartTime = time;
            hitObject.HitSound = hitSound;
            hitObject.NewCombo = (type & HitObjectFlags.NewCombo) != 0;
            hitObject.ComboColourSkip =
                (type & HitObjectFlags.ComboColourSkipMask) >> HitObjectFlags.ComboColourSkipShift;

            return hitObject;
        }

        /// <summary><c>x,y,time,type,hitSound,endTime,hitSample</c></summary>
        private static SpinnerData parseSpinner(string[] parts, double startTime)
        {
            if (parts.Length < 6)
                throw new BeatmapDecodeException($"spinner needs at least 6 fields, got {parts.Length}");

            double endTime = parseDouble(parts[5], "endTime");

            if (endTime < startTime)
                throw new BeatmapDecodeException($"spinner ends ({endTime}) before it starts ({startTime})");

            var spinner = new SpinnerData();
            spinner.SetEndTime(endTime);
            return spinner;
        }

        /// <summary>
        /// <c>x,y,time,type,hitSound,curve,slides,length,edgeSounds,edgeSets,hitSample</c>
        /// Everything from <c>edgeSounds</c> on is optional.
        /// </summary>
        private static SliderData parseSlider(string[] parts, Vector2 head)
        {
            if (parts.Length < 8)
                throw new BeatmapDecodeException($"slider needs at least 8 fields, got {parts.Length}");

            string[] curve = parts[5].Split('|');

            if (curve.Length < 2)
                throw new BeatmapDecodeException($"slider curve \"{parts[5]}\" has no anchor points");

            var slider = new SliderData
            {
                CurveType = parseCurveType(curve[0]),
                Slides = parseInt(parts[6], "slides"),
                PixelLength = parseDouble(parts[7], "length"),
            };

            if (slider.Slides < 1)
                throw new BeatmapDecodeException($"slider has {slider.Slides} slides, expected at least 1");

            if (slider.PixelLength < 0)
                throw new BeatmapDecodeException($"slider has negative length {slider.PixelLength}");

            // The file lists only the anchors AFTER the head; prepend the head
            // so Path is a complete, self-contained polyline.
            var path = new List<Vector2> { head };

            for (int i = 1; i < curve.Length; i++)
            {
                string[] pair = curve[i].Split(':');

                if (pair.Length != 2)
                    throw new BeatmapDecodeException($"slider anchor \"{curve[i]}\" is not \"x:y\"");

                path.Add(new Vector2(
                    (float)parseDouble(pair[0], "anchor x"),
                    (float)parseDouble(pair[1], "anchor y")));
            }

            slider.Path = path;

            if (parts.Length > 8 && parts[8].Length > 0)
            {
                slider.EdgeSounds = parts[8].Split('|')
                                            .Select(s => parseInt(s, "edgeSound"))
                                            .ToArray();
            }

            return slider;
        }

        private static SliderCurveType parseCurveType(string token)
        {
            if (token.Length == 0)
                throw new BeatmapDecodeException("slider curve type is empty");

            switch (token[0])
            {
                case 'L': return SliderCurveType.Linear;
                case 'B': return SliderCurveType.Bezier;
                case 'P': return SliderCurveType.PerfectCircle;
                case 'C': return SliderCurveType.Catmull;

                default:
                    throw new BeatmapDecodeException($"unknown slider curve type \"{token}\"");
            }
        }

        // ------------------------------------------------------------------
        // Invariant-culture number parsing
        // ------------------------------------------------------------------

        private static int parseInt(string value, string field)
        {
            if (!int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
                throw new BeatmapDecodeException($"{field}: \"{value}\" is not an integer");

            return result;
        }

        private static double parseDouble(string value, string field)
        {
            if (!double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double result))
                throw new BeatmapDecodeException($"{field}: \"{value}\" is not a number");

            return result;
        }
    }
}
