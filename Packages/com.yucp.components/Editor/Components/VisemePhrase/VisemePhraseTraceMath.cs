using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace YUCP.Components.Editor.VisemePhrase
{
    public sealed class VisemePhraseDistanceOptions
    {
        public float bandRatio = 0.5f;
        public float softDtwGamma = 0.08f;
        public float durationWeight = 0.15f;
        public float voiceWeight = 0.05f;
        public float insertionPenalty = 0.04f;

        internal VisemePhraseDistanceOptions Sanitized()
        {
            return new VisemePhraseDistanceOptions
            {
                bandRatio = Mathf.Clamp01(FiniteOr(bandRatio, 0.5f)),
                softDtwGamma = Mathf.Max(0.001f, FiniteOr(softDtwGamma, 0.08f)),
                durationWeight = Mathf.Clamp(FiniteOr(durationWeight, 0.15f), 0f, 0.45f),
                voiceWeight = Mathf.Clamp(FiniteOr(voiceWeight, 0.05f), 0f, 0.25f),
                insertionPenalty = Mathf.Clamp01(FiniteOr(insertionPenalty, 0.04f))
            };
        }

        private static float FiniteOr(float value, float fallback)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
        }
    }

    public readonly struct VisemePhraseAlignmentPair
    {
        public readonly int first;
        public readonly int second;

        public VisemePhraseAlignmentPair(int first, int second)
        {
            this.first = first;
            this.second = second;
        }
    }

    public sealed class VisemePhraseAlignment
    {
        public float normalizedCost = 1f;
        public List<VisemePhraseAlignmentPair> pairs = new List<VisemePhraseAlignmentPair>();
    }

    /// <summary>
    /// Pure enrollment math. All methods return new values and never rewrite raw
    /// capture data, allowing compiler/schema upgrades to rebake old recordings.
    /// </summary>
    public static class VisemePhraseTraceMath
    {
        public const int VisemeCount = 15;
        public const float EnrollmentSpeechHangoverSeconds = 0.16f;
        public const float EnrollmentSilenceStability = 0.5f;
        public const float EnrollmentTalkingThreshold = 0.08f;

        public static VisemePhraseEnrollmentTrace Trim(
            VisemePhraseEnrollmentTrace trace,
            float voiceThreshold = 0.025f,
            float paddingSeconds = 0.045f)
        {
            var result = CopyMetadata(trace);
            if (trace?.frames == null || trace.frames.Count == 0) return result;

            var sampleRate = Math.Max(1, trace.sampleRate);
            var frames = SanitizeFrames(trace.frames);
            if (frames.Count == 0) return result;

            var firstSpeech = -1;
            var lastSpeech = -1;
            var speechPresence = ReconstructSpeechPresence(frames, sampleRate);
            for (var i = 0; i < frames.Count; i++)
            {
                var hardSilence = frames[i].viseme == 0;
                var hasVoice = frames[i].voice >= voiceThreshold;
                var avrTalking = i < speechPresence.Length &&
                                 speechPresence[i] >= EnrollmentTalkingThreshold;
                if (hardSilence && !hasVoice && !avrTalking) continue;
                if (firstSpeech < 0) firstSpeech = i;
                lastSpeech = i;
            }
            if (firstSpeech < 0) return result;

            var padding = (long)Math.Ceiling(Math.Max(0f, paddingSeconds) * sampleRate);
            var minimumClock = Math.Max(0L, frames[firstSpeech].sampleClock - padding);
            var maximumClock = frames[lastSpeech].sampleClock + padding;
            var start = firstSpeech;
            while (start > 0 && frames[start - 1].sampleClock >= minimumClock) start--;
            var end = lastSpeech;
            while (end + 1 < frames.Count && frames[end + 1].sampleClock <= maximumClock) end++;

            var origin = frames[start].sampleClock;
            for (var i = start; i <= end; i++)
            {
                result.frames.Add(new VisemePhraseTraceFrame
                {
                    sampleClock = frames[i].sampleClock - origin,
                    viseme = frames[i].viseme,
                    voice = frames[i].voice
                });
            }

            var step = EstimateFrameStep(result.frames, sampleRate);
            result.durationSamples = result.frames.Count == 0
                ? 0L
                : result.frames[result.frames.Count - 1].sampleClock + step;
            return result;
        }

        /// <summary>
        /// Replays the same pure causal speech-presence observer used by the
        /// Advanced Viseme Reconstructor. Enrollment assets only persist raw
        /// Viseme/Voice samples, so this derived signal remains deterministic and
        /// automatically rebakes when the observer contract changes.
        /// </summary>
        public static float[] ReconstructSpeechPresence(
            IReadOnlyList<VisemePhraseTraceFrame> source,
            int sampleRate,
            float speechHangoverSeconds = EnrollmentSpeechHangoverSeconds,
            float silenceStability = EnrollmentSilenceStability)
        {
            if (source == null || source.Count == 0) return Array.Empty<float>();
            sampleRate = Math.Max(1, sampleRate);
            var frames = SanitizeFrames(source);
            if (frames.Count == 0) return Array.Empty<float>();

            var output = new float[frames.Count];
            var state = new AdvancedVisemeMath.SpeechHistoryState();
            var fallbackStep = EstimateFrameStep(frames, sampleRate);
            for (var i = 0; i < frames.Count; i++)
            {
                var sampleStep = i == 0
                    ? fallbackStep
                    : Math.Max(1L, frames[i].sampleClock - frames[i - 1].sampleClock);
                AdvancedVisemeMath.StepSpeechHistory(
                    frames[i].viseme,
                    frames[i].voice,
                    sampleStep / (float)sampleRate,
                    speechHangoverSeconds,
                    silenceStability,
                    ref state);
                output[i] = Mathf.Clamp01(state.Presence);
            }
            return output;
        }

        public static List<VisemePhraseToken> RunLengthEncode(
            VisemePhraseEnrollmentTrace trace)
        {
            var output = new List<VisemePhraseToken>();
            if (trace?.frames == null || trace.frames.Count == 0) return output;
            var sampleRate = Math.Max(1, trace.sampleRate);
            var frames = SanitizeFrames(trace.frames);
            if (frames.Count == 0) return output;

            var groups = new List<Group>();
            var group = new Group(frames[0].viseme, 0);
            for (var i = 0; i < frames.Count; i++)
            {
                if (frames[i].viseme != group.viseme)
                {
                    group.endIndex = i;
                    groups.Add(group);
                    group = new Group(frames[i].viseme, i);
                }
                group.voiceSum += frames[i].voice;
                group.frameCount++;
            }
            group.endIndex = frames.Count;
            groups.Add(group);

            while (groups.Count > 0 && groups[0].viseme == 0) groups.RemoveAt(0);
            while (groups.Count > 0 && groups[groups.Count - 1].viseme == 0)
                groups.RemoveAt(groups.Count - 1);
            if (groups.Count == 0) return output;

            var fallbackStep = EstimateFrameStep(frames, sampleRate);
            var traceEnd = Math.Max(
                trace.durationSamples,
                frames[frames.Count - 1].sampleClock + fallbackStep);
            for (var i = 0; i < groups.Count; i++)
            {
                var current = groups[i];
                var startClock = frames[current.startIndex].sampleClock;
                long endClock;
                if (current.endIndex < frames.Count)
                    endClock = frames[current.endIndex].sampleClock;
                else
                    endClock = traceEnd;
                if (endClock <= startClock) endClock = startClock + fallbackStep;

                output.Add(new VisemePhraseToken
                {
                    viseme = current.viseme,
                    startSeconds = startClock / (float)sampleRate,
                    endSeconds = endClock / (float)sampleRate,
                    meanVoice = current.frameCount > 0
                        ? Mathf.Clamp01(current.voiceSum / current.frameCount)
                        : 0f,
                    frameCount = current.frameCount
                });
            }
            return RemoveLowConfidenceBounces(output);
        }

        public static List<VisemePhraseToken> RemoveLowConfidenceBounces(
            IReadOnlyList<VisemePhraseToken> source,
            float maximumBounceSeconds = 0.03f)
        {
            var output = source == null
                ? new List<VisemePhraseToken>()
                : source.Select(CloneToken).ToList();
            var limit = IsFinite(maximumBounceSeconds)
                ? Mathf.Clamp(maximumBounceSeconds, 0f, 0.1f)
                : 0.03f;
            var index = 1;
            while (index + 1 < output.Count)
            {
                var previous = output[index - 1];
                var bounce = output[index];
                var next = output[index + 1];

                // Voice is microphone amplitude, not classifier confidence. A
                // loud one-block A-B-A classifier flicker is still a transient
                // label and must be treated exactly like a quiet one. Duration
                // is the only confidence signal available both here and from
                // VRChat's runtime hard Viseme parameter.
                if (previous.viseme != next.viseme ||
                    bounce.viseme == previous.viseme ||
                    bounce.DurationSeconds >= limit)
                {
                    index++;
                    continue;
                }

                var previousDuration = previous.DurationSeconds;
                var bounceDuration = bounce.DurationSeconds;
                var nextDuration = next.DurationSeconds;
                var totalDuration = Mathf.Max(
                    1e-6f,
                    previousDuration + bounceDuration + nextDuration);
                previous.meanVoice = Mathf.Clamp01(
                    (previous.meanVoice * previousDuration +
                     bounce.meanVoice * bounceDuration +
                     next.meanVoice * nextDuration) / totalDuration);
                previous.endSeconds = next.endSeconds;
                previous.frameCount += bounce.frameCount + next.frameCount;
                output.RemoveAt(index + 1);
                output.RemoveAt(index);
                if (index > 1) index--;
            }
            return output;
        }

        /// <summary>
        /// Removes hard-classifier winners that never survive long enough to be
        /// observable as a stable runtime token. Individual-take validation and
        /// model construction both use this stabilized sequence, so classifier
        /// flicker cannot manufacture the minimum phrase length. Voice is
        /// amplitude and is never used here.
        /// </summary>
        public static List<VisemePhraseToken> RemoveTransientRuns(
            IReadOnlyList<VisemePhraseToken> source,
            float maximumTransientSeconds = 0.03f)
        {
            var output = source == null
                ? new List<VisemePhraseToken>()
                : source.Select(CloneToken).ToList();
            var limit = IsFinite(maximumTransientSeconds)
                ? Mathf.Clamp(maximumTransientSeconds, 0f, 0.1f)
                : 0.03f;
            if (limit <= 0f) return output;

            var index = 0;
            while (index < output.Count)
            {
                if (output.Count <= 1 || output[index].DurationSeconds >= limit)
                {
                    index++;
                    continue;
                }

                var transient = output[index];
                if (index > 0 && index + 1 < output.Count &&
                    output[index - 1].viseme == output[index + 1].viseme)
                {
                    var previous = output[index - 1];
                    var next = output[index + 1];
                    var previousDuration = previous.DurationSeconds;
                    var transientDuration = transient.DurationSeconds;
                    var nextDuration = next.DurationSeconds;
                    var total = Mathf.Max(
                        0.000001f,
                        previousDuration + transientDuration + nextDuration);
                    previous.meanVoice = Mathf.Clamp01(
                        (previous.meanVoice * previousDuration +
                         transient.meanVoice * transientDuration +
                         next.meanVoice * nextDuration) / total);
                    previous.endSeconds = next.endSeconds;
                    previous.frameCount += transient.frameCount + next.frameCount;
                    output.RemoveAt(index + 1);
                    output.RemoveAt(index);
                    index = Math.Max(0, index - 1);
                    continue;
                }

                if (index > 0)
                {
                    var previous = output[index - 1];
                    var previousDuration = previous.DurationSeconds;
                    var transientDuration = transient.DurationSeconds;
                    var total = Mathf.Max(0.000001f, previousDuration + transientDuration);
                    previous.meanVoice = Mathf.Clamp01(
                        (previous.meanVoice * previousDuration +
                         transient.meanVoice * transientDuration) / total);
                    previous.endSeconds = transient.endSeconds;
                    previous.frameCount += transient.frameCount;
                }
                output.RemoveAt(index);
                if (index > 0) index--;
            }
            return output;
        }

        public static float DtwDistance(
            IReadOnlyList<VisemePhraseToken> first,
            IReadOnlyList<VisemePhraseToken> second,
            VisemePhraseDistanceOptions options = null)
        {
            return Align(first, second, options).normalizedCost;
        }

        public static float SoftDtwDistance(
            IReadOnlyList<VisemePhraseToken> first,
            IReadOnlyList<VisemePhraseToken> second,
            VisemePhraseDistanceOptions options = null)
        {
            if (first == null || second == null || first.Count == 0 || second.Count == 0)
                return 1f;
            var safe = (options ?? new VisemePhraseDistanceOptions()).Sanitized();
            var cross = SoftDtwRaw(first, second, safe);
            var selfFirst = SoftDtwRaw(first, first, safe);
            var selfSecond = SoftDtwRaw(second, second, safe);
            var divergence = cross - 0.5f * (selfFirst + selfSecond);
            return Mathf.Clamp01(Mathf.Max(0f, divergence) /
                                 Mathf.Max(1f, Mathf.Max(first.Count, second.Count)));
        }

        public static VisemePhraseAlignment Align(
            IReadOnlyList<VisemePhraseToken> first,
            IReadOnlyList<VisemePhraseToken> second,
            VisemePhraseDistanceOptions options = null)
        {
            var result = new VisemePhraseAlignment();
            if (first == null || second == null || first.Count == 0 || second.Count == 0)
                return result;
            var safe = (options ?? new VisemePhraseDistanceOptions()).Sanitized();
            var rows = first.Count;
            var columns = second.Count;
            var width = BandWidth(rows, columns, safe.bandRatio);
            var firstTotalDuration = TotalDuration(first);
            var secondTotalDuration = TotalDuration(second);
            var costs = CreateMatrix(rows + 1, columns + 1, float.PositiveInfinity);
            var directions = new byte[rows + 1, columns + 1];
            costs[0, 0] = 0f;

            for (var row = 1; row <= rows; row++)
            {
                costs[row, 0] = costs[row - 1, 0] +
                                GapCost(first[row - 1], firstTotalDuration);
                directions[row, 0] = 1;
            }
            for (var column = 1; column <= columns; column++)
            {
                costs[0, column] = costs[0, column - 1] +
                                   GapCost(second[column - 1], secondTotalDuration);
                directions[0, column] = 2;
            }

            for (var row = 1; row <= rows; row++)
            {
                var minimumColumn = Math.Max(1, row - width);
                var maximumColumn = Math.Min(columns, row + width);
                for (var column = minimumColumn; column <= maximumColumn; column++)
                {
                    var diagonal = costs[row - 1, column - 1] + LocalCost(
                        first[row - 1], second[column - 1], safe,
                        firstTotalDuration, secondTotalDuration);
                    var up = costs[row - 1, column] +
                             GapCost(first[row - 1], firstTotalDuration);
                    var left = costs[row, column - 1] +
                               GapCost(second[column - 1], secondTotalDuration);
                    var direction = (byte)0;
                    var previous = diagonal;
                    if (up < previous)
                    {
                        previous = up;
                        direction = 1;
                    }
                    if (left < previous)
                    {
                        previous = left;
                        direction = 2;
                    }
                    if (float.IsPositiveInfinity(previous)) continue;
                    costs[row, column] = previous;
                    directions[row, column] = direction;
                }
            }

            if (float.IsPositiveInfinity(costs[rows, columns])) return result;
            result.normalizedCost = Mathf.Clamp01(
                costs[rows, columns] / Mathf.Max(1, Math.Max(rows, columns)));
            var currentRow = rows;
            var currentColumn = columns;
            while (currentRow > 0 || currentColumn > 0)
            {
                var direction = directions[currentRow, currentColumn];
                if (currentRow == 0) direction = 2;
                else if (currentColumn == 0) direction = 1;
                switch (direction)
                {
                    case 1:
                        result.pairs.Add(new VisemePhraseAlignmentPair(
                            currentRow - 1,
                            -1));
                        currentRow--;
                        break;
                    case 2:
                        result.pairs.Add(new VisemePhraseAlignmentPair(
                            -1,
                            currentColumn - 1));
                        currentColumn--;
                        break;
                    default:
                        result.pairs.Add(new VisemePhraseAlignmentPair(
                            currentRow - 1,
                            currentColumn - 1));
                        currentRow--;
                        currentColumn--;
                        break;
                }
            }
            result.pairs.Reverse();
            return result;
        }

        public static float Median(IEnumerable<float> values)
        {
            if (values == null) return 0f;
            var finite = values.Where(IsFinite).OrderBy(value => value).ToArray();
            if (finite.Length == 0) return 0f;
            var middle = finite.Length / 2;
            return finite.Length % 2 == 0
                ? 0.5f * (finite[middle - 1] + finite[middle])
                : finite[middle];
        }

        public static float MedianAbsoluteDeviation(IEnumerable<float> values)
        {
            if (values == null) return 0f;
            var finite = values.Where(IsFinite).ToArray();
            if (finite.Length == 0) return 0f;
            var median = Median(finite);
            return Median(finite.Select(value => Mathf.Abs(value - median)));
        }

        public static float VisemeSubstitutionCost(int first, int second)
        {
            first = Mathf.Clamp(first, 0, VisemeCount - 1);
            second = Mathf.Clamp(second, 0, VisemeCount - 1);
            if (first == second) return 0f;
            if (first == 0 || second == 0) return 1f;
            if (IsVowel(first) && IsVowel(second)) return 0.55f;
            if (Pair(first, second, 6, 7)) return 0.35f; // CH / SS
            if (Pair(first, second, 4, 8)) return 0.45f; // DD / nn
            if (Pair(first, second, 1, 2)) return 0.62f; // PP / FF
            if (IsLingualConsonant(first) && IsLingualConsonant(second)) return 0.72f;
            return 1f;
        }

        private static float LocalCost(
            VisemePhraseToken first,
            VisemePhraseToken second,
            VisemePhraseDistanceOptions options,
            float firstTotalDuration,
            float secondTotalDuration)
        {
            var durationWeight = options.durationWeight;
            var voiceWeight = options.voiceWeight;
            var labelWeight = Mathf.Max(0f, 1f - durationWeight - voiceWeight);

            // Compare each run's share of its utterance rather than absolute
            // seconds. Whole-phrase timing is validated separately, so charging
            // absolute duration here would penalize speaking rate twice.
            var firstShare = Mathf.Max(
                0.0001f,
                first.DurationSeconds / Mathf.Max(0.0001f, firstTotalDuration));
            var secondShare = Mathf.Max(
                0.0001f,
                second.DurationSeconds / Mathf.Max(0.0001f, secondTotalDuration));
            var durationRatio = Mathf.Abs(Mathf.Log(
                firstShare / secondShare));
            var durationCost = Mathf.Clamp01(durationRatio / Mathf.Log(4f));
            var voiceCost = Mathf.Abs(first.meanVoice - second.meanVoice);
            return Mathf.Clamp01(
                labelWeight * VisemeSubstitutionCost(first.viseme, second.viseme) +
                durationWeight * durationCost +
                voiceWeight * voiceCost);
        }

        private static float GapCost(VisemePhraseToken token, float totalDuration)
        {
            if (token == null) return 0.55f;
            var duration = Mathf.Max(0f, token.DurationSeconds);
            var durationShare = duration / Mathf.Max(0.0001f, totalDuration);
            // A single 48 kHz / 1024-sample classifier winner is weak evidence;
            // longer runs pay a real edit cost. Voice is deliberately absent.
            var baseCost = duration < 0.03f ? 0.2f : 0.55f;
            return Mathf.Clamp(baseCost + 0.2f * durationShare, 0.18f, 0.75f);
        }

        private static float SoftDtwRaw(
            IReadOnlyList<VisemePhraseToken> first,
            IReadOnlyList<VisemePhraseToken> second,
            VisemePhraseDistanceOptions options)
        {
            var rows = first.Count;
            var columns = second.Count;
            var width = BandWidth(rows, columns, options.bandRatio);
            var firstTotalDuration = TotalDuration(first);
            var secondTotalDuration = TotalDuration(second);
            var costs = CreateMatrix(rows + 1, columns + 1, float.PositiveInfinity);
            costs[0, 0] = 0f;
            for (var row = 1; row <= rows; row++)
            {
                var minimumColumn = Math.Max(1, row - width);
                var maximumColumn = Math.Min(columns, row + width);
                for (var column = minimumColumn; column <= maximumColumn; column++)
                {
                    var previous = SoftMinimum(
                        costs[row - 1, column - 1],
                        costs[row - 1, column] + options.insertionPenalty,
                        costs[row, column - 1] + options.insertionPenalty,
                        options.softDtwGamma);
                    if (float.IsPositiveInfinity(previous)) continue;
                    costs[row, column] = LocalCost(
                        first[row - 1], second[column - 1], options,
                        firstTotalDuration, secondTotalDuration) + previous;
                }
            }
            return costs[rows, columns];
        }

        private static float SoftMinimum(float first, float second, float third, float gamma)
        {
            var minimum = Mathf.Min(first, Mathf.Min(second, third));
            if (float.IsPositiveInfinity(minimum)) return minimum;
            var sum = Math.Exp(-(first - minimum) / gamma) +
                      Math.Exp(-(second - minimum) / gamma) +
                      Math.Exp(-(third - minimum) / gamma);
            return minimum - gamma * (float)Math.Log(Math.Max(1e-30d, sum));
        }

        private static float[,] CreateMatrix(int rows, int columns, float value)
        {
            var matrix = new float[rows, columns];
            if (Mathf.Approximately(value, 0f)) return matrix;
            for (var row = 0; row < rows; row++)
                for (var column = 0; column < columns; column++)
                    matrix[row, column] = value;
            return matrix;
        }

        private static int BandWidth(int rows, int columns, float ratio)
        {
            return Math.Max(
                Math.Abs(rows - columns),
                Math.Max(1, Mathf.CeilToInt(Mathf.Max(rows, columns) * ratio)));
        }

        private static VisemePhraseEnrollmentTrace CopyMetadata(
            VisemePhraseEnrollmentTrace source)
        {
            return new VisemePhraseEnrollmentTrace
            {
                traceSchemaVersion = source?.traceSchemaVersion ??
                                     VisemePhraseEnrollmentTrace.CurrentTraceSchemaVersion,
                takeId = source?.takeId ?? string.Empty,
                backend = source?.backend ?? string.Empty,
                recordedUtcTicks = source?.recordedUtcTicks ?? 0L,
                sampleRate = Math.Max(1, source?.sampleRate ?? 48000),
                durationSamples = 0L,
                frames = new List<VisemePhraseTraceFrame>()
            };
        }

        private static List<VisemePhraseTraceFrame> SanitizeFrames(
            IReadOnlyList<VisemePhraseTraceFrame> source)
        {
            var result = new List<VisemePhraseTraceFrame>();
            var previousClock = -1L;
            for (var i = 0; i < source.Count; i++)
            {
                var frame = source[i];
                if (frame == null) continue;
                var clock = Math.Max(0L, frame.sampleClock);
                if (clock <= previousClock) continue;
                result.Add(new VisemePhraseTraceFrame
                {
                    sampleClock = clock,
                    viseme = Mathf.Clamp(frame.viseme, 0, VisemeCount - 1),
                    voice = IsFinite(frame.voice) ? Mathf.Clamp01(frame.voice) : 0f
                });
                previousClock = clock;
            }
            return result;
        }

        private static long EstimateFrameStep(
            IReadOnlyList<VisemePhraseTraceFrame> frames,
            int sampleRate)
        {
            if (frames.Count < 2) return Math.Max(1L, sampleRate / 60L);
            var steps = new List<float>();
            for (var i = 1; i < frames.Count; i++)
            {
                var difference = frames[i].sampleClock - frames[i - 1].sampleClock;
                if (difference > 0L) steps.Add(difference);
            }
            return Math.Max(1L, (long)Math.Round(Median(steps)));
        }

        private static float TotalDuration(IReadOnlyList<VisemePhraseToken> tokens)
        {
            if (tokens == null || tokens.Count == 0) return 0.0001f;
            var total = 0f;
            for (var i = 0; i < tokens.Count; i++)
                total += Mathf.Max(0f, tokens[i]?.DurationSeconds ?? 0f);
            return Mathf.Max(0.0001f, total);
        }

        private static bool IsVowel(int viseme)
        {
            return viseme >= 10 && viseme <= 14;
        }

        private static bool IsLingualConsonant(int viseme)
        {
            return viseme == 3 || viseme == 4 || viseme == 5 || viseme == 6 ||
                   viseme == 7 || viseme == 8 || viseme == 9;
        }

        private static bool Pair(int first, int second, int one, int two)
        {
            return first == one && second == two || first == two && second == one;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static VisemePhraseToken CloneToken(VisemePhraseToken source)
        {
            return new VisemePhraseToken
            {
                viseme = source.viseme,
                startSeconds = source.startSeconds,
                endSeconds = source.endSeconds,
                meanVoice = source.meanVoice,
                frameCount = source.frameCount
            };
        }

        private sealed class Group
        {
            internal readonly int viseme;
            internal readonly int startIndex;
            internal int endIndex;
            internal float voiceSum;
            internal int frameCount;

            internal Group(int viseme, int startIndex)
            {
                this.viseme = viseme;
                this.startIndex = startIndex;
            }
        }
    }
}
