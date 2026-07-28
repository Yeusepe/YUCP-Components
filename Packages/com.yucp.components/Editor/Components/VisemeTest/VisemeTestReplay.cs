using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using YUCP.Components.Editor.MeshUtils;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Replays a recorded emulator capture through a freshly built Advanced
    /// Viseme controller and writes the reconstruction beside the teacher
    /// weights that were captured with it.
    ///
    /// The capture supplies the exact quantized Viseme/Voice stream VRChat
    /// would deliver plus the continuous Oculus weights VRChat discards, so
    /// replaying the former through a real Animator and comparing against the
    /// latter measures the reconstruction end to end without Play Mode, a
    /// built avatar, or Gesture Manager.
    /// </summary>
    internal static class VisemeTestReplay
    {
        private const float FrameSeconds = 1024f / 48000f;
        internal const float AnalysisRateHz = 48000f / 1024f;
        internal static AdvancedVisemeReconstructionMode ReplayReconstructionMode =
            AdvancedVisemeReconstructionMode.BetaCoarticulation;

        [MenuItem("Tools/YUCP/Replay Sweep Voice Gain")]
        internal static void ReplaySweepVoiceGain()
        {
            var capture = NewestCapture();
            if (capture == null) return;
            var previous = AdvancedVisemeAnimatorBuilder.VoiceResponseGain;
            var previousEnabled = AdvancedVisemeAnimatorBuilder.EnableVoiceResponse;
            try
            {
                foreach (var gain in new[] { 0f, 0.25f, 0.5f, 1f, 1.5f })
                {
                    AdvancedVisemeAnimatorBuilder.EnableVoiceResponse = gain > 0f;
                    AdvancedVisemeAnimatorBuilder.VoiceResponseGain = gain;
                    var path = Replay(capture, 90f);
                    if (path != null)
                    {
                        var renamed = path.Replace("replay_",
                            "replayG" + Mathf.RoundToInt(gain * 100f) + "_");
                        if (File.Exists(renamed)) File.Delete(renamed);
                        File.Move(path, renamed);
                    }
                }
            }
            finally
            {
                AdvancedVisemeAnimatorBuilder.VoiceResponseGain = previous;
                AdvancedVisemeAnimatorBuilder.EnableVoiceResponse = previousEnabled;
            }
        }

        [MenuItem("Tools/YUCP/Replay Sweep Fusion Hold Amp")]
        internal static void ReplaySweepFusionAmp()
        {
            var capture = NewestCapture();
            if (capture == null) return;
            var amp = AdvancedVisemeAnimatorBuilder.FusionHoldAmp;
            var fusion = AdvancedVisemeAnimatorBuilder.EnableFusionHoldGenerator;
            var halo = AdvancedVisemeAnimatorBuilder.EnableDensityHalo;
            var syll = AdvancedVisemeAnimatorBuilder.EnableSyllabicResponse;
            try
            {
                // The hold generator is the new stage; the syllabic term and the
                // halo envelope are the current stack. Sweep the generator alone
                // (amp 0 = current stack) against several strengths.
                AdvancedVisemeAnimatorBuilder.EnableSyllabicResponse = false;
                // Isolate the two halves of the fusion so a transition defect can
                // be pinned on the envelope (sharpen + tau) or the hold generator:
                //   B = fusion fully off (plain observer envelope)
                //   E = fusion envelope on, generator amplitude 0
                //   F = full fusion at amp 0.06
                var configs = new[]
                {
                    ("B", false, 0f),
                    ("E", true, 0f),
                    ("F", true, 0.06f),
                };
                foreach (var cfg in configs)
                {
                    AdvancedVisemeAnimatorBuilder.EnableFusionHoldGenerator = cfg.Item2;
                    AdvancedVisemeAnimatorBuilder.FusionHoldAmp = cfg.Item3;
                    var path = Replay(capture, 90f);
                    if (path != null)
                    {
                        var renamed = path.Replace("replay_", "replay" + cfg.Item1 + "_");
                        if (File.Exists(renamed)) File.Delete(renamed);
                        File.Move(path, renamed);
                    }
                }
            }
            finally
            {
                AdvancedVisemeAnimatorBuilder.FusionHoldAmp = amp;
                AdvancedVisemeAnimatorBuilder.EnableFusionHoldGenerator = fusion;
                AdvancedVisemeAnimatorBuilder.EnableDensityHalo = halo;
                AdvancedVisemeAnimatorBuilder.EnableSyllabicResponse = syll;
            }
        }

        [MenuItem("Tools/YUCP/Replay Sweep Syllabic Gain")]
        internal static void ReplaySweepSyllabicGain()
        {
            var capture = NewestCapture();
            if (capture == null) return;
            var previousGain = AdvancedVisemeAnimatorBuilder.SyllabicResponseGain;
            var previousEnabled = AdvancedVisemeAnimatorBuilder.EnableSyllabicResponse;
            try
            {
                // Gain 0 is the baseline the candidate must beat. Three offline
                // fits have looked good and failed in the graph, so nothing
                // ships on an offline number.
                foreach (var gain in new[] { 0f, 0.4f, 0.6f, 0.8f })
                {
                    AdvancedVisemeAnimatorBuilder.EnableSyllabicResponse = gain > 0f;
                    AdvancedVisemeAnimatorBuilder.SyllabicResponseGain = gain;
                    var path = Replay(capture, 90f);
                    if (path != null)
                    {
                        var renamed = path.Replace("replay_",
                            "replayS" + Mathf.RoundToInt(gain * 100f) + "_");
                        if (File.Exists(renamed)) File.Delete(renamed);
                        File.Move(path, renamed);
                    }
                }
            }
            finally
            {
                AdvancedVisemeAnimatorBuilder.SyllabicResponseGain = previousGain;
                AdvancedVisemeAnimatorBuilder.EnableSyllabicResponse = previousEnabled;
            }
        }

        [MenuItem("Tools/YUCP/Replay Sweep Observer Response")]
        internal static void ReplaySweep()
        {
            var capture = NewestCapture();
            if (capture == null) return;
            // Scored on co-activation and phase-conditioned motion, not RMSE.
            // The original sweep picked 17 ms against an RMSE-like objective
            // that is blind to how long two visemes overlap, which is the
            // thing that actually reads as a seam.
            foreach (var tau in new[] { 0.017f, 0.028f, 0.040f, 0.055f, 0.070f })
                Replay(capture, 90f, tau);
        }

        private static string NewestCapture()
        {
            var directory = Path.Combine(Directory.GetCurrentDirectory(), "VisemeCapture");
            if (!Directory.Exists(directory)) return null;
            var newest = new DirectoryInfo(directory)
                .GetFiles("viseme_capture_*.csv")
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault();
            return newest?.FullName;
        }

        [MenuItem("Tools/YUCP/Replay Newest Viseme Capture")]
        internal static void ReplayNewest()
        {
            var directory = Path.Combine(
                Directory.GetCurrentDirectory(), "VisemeCapture");
            if (!Directory.Exists(directory))
            {
                Debug.LogError("[YUCP Viseme Replay] no VisemeCapture folder.");
                return;
            }
            var newest = new DirectoryInfo(directory)
                .GetFiles("viseme_capture_*.csv")
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault();
            if (newest == null)
            {
                Debug.LogError("[YUCP Viseme Replay] no capture files found.");
                return;
            }
            Replay(newest.FullName, 90f);
        }

        internal static string Replay(string capturePath, float renderRateHz, float responseOverride = 0f)
        {
            var lines = File.ReadAllLines(capturePath);
            if (lines.Length < 3)
            {
                Debug.LogError("[YUCP Viseme Replay] capture is empty.");
                return null;
            }
            var header = lines[0].Split(',');
            var teacherColumns = Enumerable.Range(0, header.Length)
                .Where(i => header[i].StartsWith("teacher_", StringComparison.Ordinal))
                .ToArray();

            var visemes = new List<int>();
            var voices = new List<float>();
            var teacher = new List<float[]>();
            for (var row = 1; row < lines.Length; row++)
            {
                if (string.IsNullOrWhiteSpace(lines[row])) continue;
                var cells = lines[row].Split(',');
                visemes.Add((int)Parse(cells[1]));
                voices.Add(Parse(cells[2]));
                teacher.Add(teacherColumns.Select(i => Parse(cells[i])).ToArray());
            }

            var folderName = "__YUCP_Replay_" + Guid.NewGuid().ToString("N");
            var folder = "Assets/" + folderName;
            AssetDatabase.CreateFolder("Assets", folderName);
            var root = new GameObject("YUCP Viseme Replay");
            GameObject runtime = null;
            try
            {
                var component = root.AddComponent<AdvancedVisemeReconstructorData>();
                component.mouthOwnership = AdvancedVisemeMouthOwnership.OutputsOnly;
                // Match the actual avatar under test: it runs BetaCoarticulation,
                // not Normal. Measuring Normal (as this harness did all along)
                // reconstructs a path the avatar never uses.
                component.reconstructionMode = ReplayReconstructionMode;
                component.trackingInputs = AdvancedVisemeTrackingInputs.Disabled;
                component.createTuningMenu = false;
                var profile = VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
                if (responseOverride > 0f)
                {
                    // Setting the profile alone is not enough: the density-halo
                    // branch in the builder discards it, which made every
                    // previous observer sweep silently inert.
                    profile.visemeResponseSeconds = responseOverride;
                    AdvancedVisemeAnimatorBuilder.ObserverResponseOverride = responseOverride;
                }

                var result = AdvancedVisemeAnimatorBuilder.Build(
                    new AdvancedVisemeAnimatorBuilder.Request
                    {
                        controllerPath = folder + "/AdvancedViseme.controller",
                        parametersPath = folder + "/Parameters.asset",
                        component = component,
                        profile = profile,
                        trackingPrefix = "YUCP/ReplayTracking",
                        effectiveTrackingInputs = AdvancedVisemeTrackingInputs.Disabled,
                        reuseExistingTracking = false,
                        trackingActiveParameter = "YUCP/ReplayTracking/LipTrackingActive",
                        trackingActiveAnimatorType = AnimatorControllerParameterType.Float,
                        trackingActiveDefault = 0f,
                        trackingParameterNames =
                            new Dictionary<AdvancedVisemeArticulator, string>(),
                        auxiliaryTrackingParameterNames = new Dictionary<string, string>(),
                        sourceVisemeBlendShapes =
                            new string[VisemeReconstructionProfile.VisemeCount],
                        calibrationBasis =
                            Array.Empty<AdvancedVisemeMeshCalibrator.BasisInput>(),
                        resolvedBlendShapes =
                            new Dictionary<AdvancedVisemeArticulator, string>(),
                        externalPoses = new Dictionary<
                            AdvancedVisemeArticulator, AdvancedVisemeExternalPose>(),
                        trackingEnabled = false,
                        existingExpressionParameters = new HashSet<string>()
                    });

                runtime = new GameObject("YUCP Viseme Replay Runtime");
                var animator = runtime.AddComponent<Animator>();
                animator.runtimeAnimatorController = result.controller;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Rebind();
                animator.Update(0f);
                animator.SetFloat("IsLocal", 1f);

                var prefix = component.NormalizedPrefix;
                var published = Enumerable
                    .Range(0, VisemeReconstructionProfile.VisemeCount)
                    .Select(i => AdvancedVisemeParameterContract.Viseme(prefix, i))
                    .ToArray();
                var missing = published
                    .Where(p => animator.parameters.All(q => q.name != p))
                    .ToArray();
                if (missing.Length > 0)
                {
                    Debug.LogError("[YUCP Viseme Replay] controller does not publish " +
                                   string.Join(", ", missing));
                    return null;
                }

                // The capture is at the analysis rate; the Animator is stepped
                // at the render rate so the reconstruction is measured exactly
                // as it would run on an avatar.
                var renderStep = 1f / Mathf.Max(1f, renderRateHz);
                var output = new StringBuilder();
                output.Append("time,viseme,voice");
                for (var i = 0; i < VisemeReconstructionProfile.VisemeCount; i++)
                    output.Append(",teacher_")
                        .Append(VisemeReconstructionProfile.VisemeNames[i]);
                for (var i = 0; i < VisemeReconstructionProfile.VisemeCount; i++)
                    output.Append(",avr_")
                        .Append(VisemeReconstructionProfile.VisemeNames[i]);

                // Diagnostic columns: the intermediate signals a correction is
                // built from, so a mismatch against an offline fit can be
                // localised to a node instead of guessed at.
                var probes = animator.parameters
                    .Where(p => p.type == AnimatorControllerParameterType.Float &&
                                (p.name.IndexOf("Syllabic", StringComparison.Ordinal) >= 0 ||
                                 p.name.IndexOf("Fusion/", StringComparison.Ordinal) >= 0 ||
                                 p.name.EndsWith("/Slew", StringComparison.Ordinal) ||
                                 p.name.EndsWith("/Fir", StringComparison.Ordinal) ||
                                 p.name.IndexOf("Fir/", StringComparison.Ordinal) >= 0))
                    .Select(p => p.name)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray();
                foreach (var probe in probes)
                    output.Append(",probe_").Append(probe.Replace(',', '_'));
                output.AppendLine();

                var carry = 0f;
                for (var frame = 0; frame < visemes.Count; frame++)
                {
                    animator.SetInteger("Viseme", visemes[frame]);
                    animator.SetFloat("Voice", voices[frame]);
                    carry += FrameSeconds;
                    while (carry >= renderStep)
                    {
                        animator.Update(renderStep);
                        carry -= renderStep;
                    }

                    output.Append((frame * FrameSeconds)
                        .ToString("F5", CultureInfo.InvariantCulture));
                    output.Append(',').Append(visemes[frame]);
                    output.Append(',').Append(
                        voices[frame].ToString("F5", CultureInfo.InvariantCulture));
                    foreach (var value in teacher[frame])
                        output.Append(',').Append(
                            value.ToString("F5", CultureInfo.InvariantCulture));
                    foreach (var parameter in published)
                        output.Append(',').Append(animator.GetFloat(parameter)
                            .ToString("F5", CultureInfo.InvariantCulture));
                    foreach (var probe in probes)
                        output.Append(',').Append(animator.GetFloat(probe)
                            .ToString("F5", CultureInfo.InvariantCulture));
                    output.AppendLine();
                }

                var suffix = responseOverride > 0f
                    ? "_tau" + Mathf.RoundToInt(responseOverride * 1000f)
                    : string.Empty;
                var outputPath = Path.Combine(
                    Path.GetDirectoryName(capturePath) ?? ".",
                    "replay" + suffix + "_" + Path.GetFileName(capturePath));
                File.WriteAllText(outputPath, output.ToString());
                Debug.Log($"[YUCP Viseme Replay] {visemes.Count} frames at " +
                          $"{renderRateHz:F0} Hz -> {outputPath}");
                return outputPath;
            }
            finally
            {
                AdvancedVisemeAnimatorBuilder.ObserverResponseOverride = 0f;
                if (runtime != null) UnityEngine.Object.DestroyImmediate(runtime);
                UnityEngine.Object.DestroyImmediate(root);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        private static float Parse(string value)
        {
            return float.TryParse(value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var parsed) ? parsed : 0f;
        }
    }
}
