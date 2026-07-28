using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Records the emulator's analysis stream while a preview session runs.
    /// Each frame stores the exact Oculus teacher weights the session already
    /// computes, the quantized index published to the avatar, and every
    /// animated output channel in the scene, so the teacher and whatever the
    /// avatars actually render are directly comparable on one timeline.
    ///
    /// Output channels are discovered rather than assumed: every blendshape on
    /// every skinned mesh is sampled, and channels that never move are dropped
    /// when the file is written. Nothing here depends on rig, naming, or on the
    /// avatar having been built.
    ///
    /// Recording holds the session's lossless-analysis scope, otherwise the
    /// preview drops any microphone backlog beyond a quarter second and the
    /// captured timeline covers only a fraction of what was spoken.
    /// </summary>
    internal static class VisemeTestRecorder
    {
        private sealed class Channel
        {
            internal string label;
            internal SkinnedMeshRenderer renderer;
            internal int shapeIndex;
            internal Animator animator;
            internal string parameter;
        }

        private static readonly List<Channel> channels = new List<Channel>();
        private static readonly List<float[]> rows = new List<float[]>();
        private static readonly List<float[]> teachers = new List<float[]>();
        private static readonly List<double> times = new List<double>();
        private static readonly List<int> visemes = new List<int>();
        private static readonly List<float> voices = new List<float>();
        private static readonly List<bool> exact = new List<bool>();

        private static VisemeTestEmulatorData recording;
        private static IDisposable losslessScope;

        internal static bool IsRecording => recording != null;
        internal static int FrameCount => times.Count;
        internal static double DurationSeconds =>
            times.Count > 1 ? times[times.Count - 1] - times[0] : 0d;
        internal static string LastWritePath { get; private set; }
        internal static string SubjectSummary { get; private set; }

        internal static void Start(VisemeTestEmulatorData data)
        {
            Stop(false);
            times.Clear(); visemes.Clear(); voices.Clear();
            exact.Clear(); teachers.Clear(); rows.Clear();
            channels.Clear();
            recording = data;
            ResolveChannels();
            losslessScope = VisemeTestPreviewSession.BeginLosslessAnalysis(data);
            VisemeTestPreviewSession.AnalysisFrameProcessed += OnFrame;
        }

        private static void ResolveChannels()
        {
            var meshes = 0;
            foreach (var renderer in UnityEngine.Object
                         .FindObjectsOfType<SkinnedMeshRenderer>())
            {
                var mesh = renderer != null ? renderer.sharedMesh : null;
                if (mesh == null || mesh.blendShapeCount == 0) continue;
                meshes++;
                var root = renderer.transform.root;
                for (var i = 0; i < mesh.blendShapeCount; i++)
                    channels.Add(new Channel
                    {
                        label = root.name + "/" + renderer.name + "/" +
                                mesh.GetBlendShapeName(i),
                        renderer = renderer,
                        shapeIndex = i
                    });
            }

            var animators = 0;
            foreach (var animator in UnityEngine.Object.FindObjectsOfType<Animator>())
            {
                if (animator == null || animator.runtimeAnimatorController == null)
                    continue;
                var added = false;
                foreach (var parameter in animator.parameters)
                {
                    if (parameter.type != AnimatorControllerParameterType.Float)
                        continue;
                    if (parameter.name.IndexOf("Viseme", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    channels.Add(new Channel
                    {
                        label = animator.gameObject.name + "@" + parameter.name,
                        animator = animator,
                        parameter = parameter.name
                    });
                    added = true;
                }
                if (added) animators++;
            }

            SubjectSummary = channels.Count == 0
                ? "no output channels found (teacher only)"
                : $"{channels.Count} channels from {meshes} meshes, {animators} animators";
        }

        private static void OnFrame(VisemeTestPreviewSession.AnalysisSample sample)
        {
            if (recording == null || sample.source != recording) return;

            times.Add(sample.timeSeconds);
            visemes.Add(sample.viseme);
            voices.Add(sample.voice);
            exact.Add(sample.hasExactOculusTeacher);

            var teacher = new float[VisemeReconstructionProfile.VisemeCount];
            for (var i = 0; i < teacher.Length; i++)
                teacher[i] = sample.continuousVisemeWeights[i];
            teachers.Add(teacher);

            var row = new float[channels.Count];
            for (var i = 0; i < channels.Count; i++)
            {
                var channel = channels[i];
                if (channel.renderer != null)
                    row[i] = channel.renderer.GetBlendShapeWeight(channel.shapeIndex);
                else if (channel.animator != null)
                    row[i] = channel.animator.GetFloat(channel.parameter);
            }
            rows.Add(row);
        }

        internal static string Stop(bool write = true)
        {
            if (recording == null) return null;
            VisemeTestPreviewSession.AnalysisFrameProcessed -= OnFrame;
            if (losslessScope != null) { losslessScope.Dispose(); losslessScope = null; }
            recording = null;
            if (!write || times.Count == 0) return null;

            // Keep only channels that actually moved: constant ones carry no
            // information and would bury the signal in hundreds of columns.
            var live = new List<int>();
            for (var c = 0; c < channels.Count; c++)
            {
                float min = float.MaxValue, max = float.MinValue;
                for (var r = 0; r < rows.Count; r++)
                {
                    var v = rows[r][c];
                    if (v < min) min = v;
                    if (v > max) max = v;
                }
                if (max - min > 1e-4f) live.Add(c);
            }

            var directory = Path.Combine(Directory.GetCurrentDirectory(), "VisemeCapture");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory,
                "viseme_capture_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");

            var text = new StringBuilder();
            text.Append("time,viseme,voice,exactTeacher");
            for (var i = 0; i < VisemeReconstructionProfile.VisemeCount; i++)
                text.Append(",teacher_").Append(VisemeReconstructionProfile.VisemeNames[i]);
            foreach (var index in live)
                text.Append(',').Append(channels[index].label.Replace(',', '_'));
            text.AppendLine();

            for (var r = 0; r < times.Count; r++)
            {
                text.Append(times[r].ToString("F5", CultureInfo.InvariantCulture));
                text.Append(',').Append(visemes[r]);
                text.Append(',').Append(voices[r].ToString("F5", CultureInfo.InvariantCulture));
                text.Append(',').Append(exact[r] ? 1 : 0);
                foreach (var value in teachers[r])
                    text.Append(',').Append(value.ToString("F5", CultureInfo.InvariantCulture));
                foreach (var index in live)
                    text.Append(',').Append(
                        rows[r][index].ToString("F5", CultureInfo.InvariantCulture));
                text.AppendLine();
            }

            File.WriteAllText(path, text.ToString());
            LastWritePath = path;
            SubjectSummary = $"{live.Count} moving channels of {channels.Count} sampled";
            return path;
        }
    }
}
