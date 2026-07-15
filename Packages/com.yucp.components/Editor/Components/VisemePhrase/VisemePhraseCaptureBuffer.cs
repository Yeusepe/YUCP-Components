using System;
using System.Collections.Generic;
using UnityEngine;

namespace YUCP.Components.Editor.VisemePhrase
{
    /// <summary>
    /// A classifier result on the microphone sample clock. It intentionally contains
    /// no PCM data, so enrollment can never persist a creator's voice recording.
    /// </summary>
    internal readonly struct VisemePhraseCapturedFrame
    {
        internal readonly int viseme;
        internal readonly float voice;
        internal readonly long sampleClock;
        internal readonly int sampleRate;

        internal VisemePhraseCapturedFrame(int viseme, float voice, long sampleClock, int sampleRate)
        {
            this.viseme = Mathf.Clamp(viseme, 0, VisemeTestMath.VisemeCount - 1);
            this.voice = Mathf.Clamp01(float.IsNaN(voice) || float.IsInfinity(voice) ? 0f : voice);
            this.sampleClock = Math.Max(0L, sampleClock);
            this.sampleRate = Math.Max(1, sampleRate);
        }
    }

    internal sealed class VisemePhraseCapturedTake
    {
        internal readonly List<VisemePhraseCapturedFrame> frames = new List<VisemePhraseCapturedFrame>();
        internal string backend = string.Empty;
        internal double durationSeconds;

        internal bool IsUseful(float voiceThreshold = 0.025f)
        {
            if (frames.Count < 2 || durationSeconds <= 0d) return false;
            var voiced = 0;
            var informativeRuns = 0;
            var previous = -1;
            for (var i = 0; i < frames.Count; i++)
            {
                var frame = frames[i];
                if (frame.voice >= voiceThreshold) voiced++;
                if (frame.viseme == 0 || frame.viseme == previous) continue;
                informativeRuns++;
                previous = frame.viseme;
            }
            return voiced >= 2 && informativeRuns >= 2;
        }
    }

    /// <summary>
    /// Collects frame events by audio sample clock. Delivery batching does not affect
    /// relative timestamps, which is important because Unity may process several
    /// microphone blocks during one EditorApplication.update.
    /// </summary>
    internal sealed class VisemePhraseCaptureBuffer
    {
        private readonly List<VisemePhraseCapturedFrame> frames = new List<VisemePhraseCapturedFrame>();
        private long firstClock = -1L;
        private long lastClock = -1L;
        private int referenceSampleRate;
        private string backend = string.Empty;

        internal int Count => frames.Count;

        internal void Clear()
        {
            frames.Clear();
            firstClock = -1L;
            lastClock = -1L;
            referenceSampleRate = 0;
            backend = string.Empty;
        }

        internal bool Append(
            int viseme,
            float voice,
            long sampleClock,
            int sampleRate,
            string engineName)
        {
            sampleRate = Math.Max(1, sampleRate);
            sampleClock = Math.Max(0L, sampleClock);

            if (lastClock >= 0L && sampleClock <= lastClock) return false;
            if (firstClock < 0L)
            {
                firstClock = sampleClock;
                referenceSampleRate = sampleRate;
            }

            // A microphone restart resets the sample clock. Callers start a fresh
            // buffer for that session; a rate change inside one take is rejected.
            if (sampleRate != referenceSampleRate) return false;

            frames.Add(new VisemePhraseCapturedFrame(viseme, voice, sampleClock, sampleRate));
            lastClock = sampleClock;
            if (!string.IsNullOrWhiteSpace(engineName)) backend = engineName.Trim();
            return true;
        }

        internal VisemePhraseCapturedTake Finish(
            long confirmedOnsetClock = -1L,
            double preRollSeconds = 0.045d)
        {
            var result = new VisemePhraseCapturedTake { backend = backend };
            if (frames.Count == 0) return result;

            var start = 0;
            if (confirmedOnsetClock >= 0L)
            {
                var preRoll = (long)Math.Ceiling(
                    Math.Max(0d, preRollSeconds) * referenceSampleRate);
                var minimumClock = Math.Max(0L, confirmedOnsetClock - preRoll);
                while (start < frames.Count - 1 &&
                       frames[start].sampleClock < minimumClock)
                    start++;
            }

            var origin = frames[start].sampleClock;
            for (var i = start; i < frames.Count; i++)
            {
                var frame = frames[i];
                result.frames.Add(new VisemePhraseCapturedFrame(
                    frame.viseme,
                    frame.voice,
                    frame.sampleClock - origin,
                    frame.sampleRate));
            }

            result.durationSeconds = frames.Count - start > 1
                ? (frames[frames.Count - 1].sampleClock - origin) / (double)referenceSampleRate
                : 0d;
            return result;
        }
    }
}
