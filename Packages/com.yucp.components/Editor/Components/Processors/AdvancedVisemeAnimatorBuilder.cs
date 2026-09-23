using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;
using YUCP.Components.Editor.MeshUtils;

namespace YUCP.Components.Editor
{
    internal static class AdvancedVisemeAnimatorBuilder
    {
        internal sealed class Request
        {
            public string controllerPath;
            public string parametersPath;
            public string rendererPath;
            public AdvancedVisemeReconstructorData component;
            public VisemeReconstructionProfile profile;
            public string trackingPrefix;
            public AdvancedVisemeTrackingInputs effectiveTrackingInputs;
            public bool reuseExistingTracking;
            public string trackingActiveParameter;
            public AnimatorControllerParameterType? trackingActiveAnimatorType;
            public float trackingActiveDefault;
            public Dictionary<AdvancedVisemeArticulator, string> trackingParameterNames;
            public Dictionary<string, string> auxiliaryTrackingParameterNames;
            public IReadOnlyCollection<AdvancedVisemeArticulator> directPoseArticulators =
                Array.Empty<AdvancedVisemeArticulator>();
            public string[] sourceVisemeBlendShapes;
            public AdvancedVisemeMeshCalibrator.Result calibration;
            public IReadOnlyList<AdvancedVisemeMeshCalibrator.BasisInput> calibrationBasis;
            public Dictionary<AdvancedVisemeArticulator, string> resolvedBlendShapes;
            public Dictionary<AdvancedVisemeArticulator, AdvancedVisemeExternalPose> externalPoses;
            public Mesh targetMesh;
            public bool trackingEnabled;
            public HashSet<string> existingExpressionParameters;
            public IReadOnlyList<LinkedRendererOutput> linkedRendererOutputs =
                Array.Empty<LinkedRendererOutput>();
        }

        internal sealed class LinkedRendererOutput
        {
            public string rendererPath;
            public string label;
            public SkinnedMeshRenderer renderer;
            public Mesh sourceMesh;
            public AdvancedVisemeMeshCalibrator.Result calibration;
        }

        internal sealed class Result
        {
            public AnimatorController controller;
            public VRCExpressionParameters parameters;
            public readonly List<string> globalParameters = new List<string>();
            public readonly List<string> externalParameters = new List<string>();
            public readonly Dictionary<AdvancedVisemeArticulator, string> articulationParameters =
                new Dictionary<AdvancedVisemeArticulator, string>();
            public readonly Dictionary<AdvancedVisemeArticulator, string> speechArticulationParameters =
                new Dictionary<AdvancedVisemeArticulator, string>();
            public readonly Dictionary<AdvancedVisemeArticulator, string> trackingContributionParameters =
                new Dictionary<AdvancedVisemeArticulator, string>();
            public readonly Dictionary<AdvancedVisemeArticulator, string> trackingGainParameters =
                new Dictionary<AdvancedVisemeArticulator, string>();
            public readonly Dictionary<AdvancedVisemeArticulator, string> inverseTrackingGainParameters =
                new Dictionary<AdvancedVisemeArticulator, string>();
            public readonly Dictionary<AdvancedVisemeTuningControl, string> tuningParameters =
                new Dictionary<AdvancedVisemeTuningControl, string>();
            public string tuningSyncDataParameter;
            public string tuningSyncFocusParameter;
            public readonly List<string> tuningSyncIndexParameters = new List<string>();
            public int tuningSyncBits;
            public string manualTrackingParameter;
            public string trackingBlendParameter;
            public string trackingActiveWeightParameter;
        }

        private sealed class BetaWeights
        {
            public string[] fast;
            public string[] slow;
        }

        private sealed class BetaCoarticulationGraph
        {
            public BetaWeights common;
            public IReadOnlyList<string> raw;
            public IReadOnlyList<string> fast;
            public IReadOnlyList<string> slow;
            public readonly Dictionary<AdvancedVisemeArticulatorGroup, BetaWeights> groups =
                new Dictionary<AdvancedVisemeArticulatorGroup, BetaWeights>();
            public readonly Dictionary<AdvancedVisemeArticulatorGroup, string> leads =
                new Dictionary<AdvancedVisemeArticulatorGroup, string>();
        }

        private sealed class SpeechHangoverGraph
        {
            public string history;
            public string presence;
        }

        private sealed class FacePhonePosteriorGraph
        {
            public string mShareFast;
            public string mShareSlow;
            public string confidence;
            public string hiddenResidualDelta;
            public readonly Dictionary<AdvancedVisemeArticulatorGroup, BetaNasalCorrection> corrections =
                new Dictionary<AdvancedVisemeArticulatorGroup, BetaNasalCorrection>();
        }

        private sealed class BetaNasalCorrection
        {
            public string fast;
            public string slow;
        }

        private sealed class ConstraintConfidenceBases
        {
            public string bilabial;
            public string labiodental;
            public string sibilant;
        }

        private static readonly AdvancedVisemeArticulator[] CoreArticulators =
        {
            AdvancedVisemeArticulator.JawOpen,
            AdvancedVisemeArticulator.LipClose,
            AdvancedVisemeArticulator.MouthOpen,
            AdvancedVisemeArticulator.LipFunnel,
            AdvancedVisemeArticulator.LipPucker,
            AdvancedVisemeArticulator.LipSuck,
            AdvancedVisemeArticulator.SmileSad,
            AdvancedVisemeArticulator.LipBite,
            AdvancedVisemeArticulator.TongueOut
        };

        private static readonly AdvancedVisemeArticulator[] QualityArticulators =
        {
            AdvancedVisemeArticulator.JawX,
            AdvancedVisemeArticulator.JawZ,
            AdvancedVisemeArticulator.MouthX,
            AdvancedVisemeArticulator.TongueY
        };

        private static readonly AdvancedVisemeArticulator[] FullTongueArticulators =
        {
            AdvancedVisemeArticulator.TongueX,
            AdvancedVisemeArticulator.TongueRoll,
            AdvancedVisemeArticulator.TongueArchY,
            AdvancedVisemeArticulator.TongueShape,
            AdvancedVisemeArticulator.TongueTwistRight,
            AdvancedVisemeArticulator.TongueTwistLeft
        };

        // A coupled source-viseme pose may be faded globally only when the
        // tracker supplies the complete visible mouth basis. Tongue capability is
        // deliberately excluded: absent tongue hardware must not leave a
        // percentage of the entire authored jaw/lip pose over face tracking.
        private static readonly AdvancedVisemeArticulator[] VisiblePoseOwnershipArticulators =
        {
            AdvancedVisemeArticulator.JawOpen,
            AdvancedVisemeArticulator.LipClose,
            AdvancedVisemeArticulator.MouthOpen,
            AdvancedVisemeArticulator.LipFunnel,
            AdvancedVisemeArticulator.LipPucker,
            AdvancedVisemeArticulator.LipSuck,
            AdvancedVisemeArticulator.SmileSad
        };

        // A coupled authored viseme must yield whenever any visible coordinate
        // that it would move is already measured. Unlike complete calibration
        // ownership above, this support set is evaluated independently for every
        // viseme. Tongue channels stay outside it so speech can still infer
        // internal articulation when the visible lower face is fully tracked.
        private static readonly AdvancedVisemeArticulator[] VisibleSpeechArticulators =
        {
            AdvancedVisemeArticulator.JawOpen,
            AdvancedVisemeArticulator.LipClose,
            AdvancedVisemeArticulator.MouthOpen,
            AdvancedVisemeArticulator.LipFunnel,
            AdvancedVisemeArticulator.LipPucker,
            AdvancedVisemeArticulator.LipSuck,
            AdvancedVisemeArticulator.SmileSad,
            AdvancedVisemeArticulator.LipBite,
            AdvancedVisemeArticulator.JawX,
            AdvancedVisemeArticulator.JawZ,
            AdvancedVisemeArticulator.MouthX
        };

        // Unified Expressions does not expose per-channel capability bits. An
        // unsupported decoded channel is conventionally held at exact zero, so
        // remember sustained, unambiguous motion independently for every tongue
        // channel. This avoids one supported axis erasing learned motion on the
        // unsupported axes.
        internal const float NativeTongueCapabilityNoiseFloor = 0.001f;
        internal const float NativeTongueCapabilityThreshold = 0.01f;

        // Animator-friendly, One-Euro-inspired adaptive observer. At rest the
        // two-pole estimate rejects OSC/quantization chatter; once the fast and
        // slow observers disagree by a deliberate amount, the one-pole estimate
        // takes over. Values live in calibrated normalized articulator space.
        internal const float LocalTrackingMotionDeadband = 0.0025f;
        internal const float LocalTrackingMotionFullScale = 0.035f;
        internal const float RemoteTrackingMotionDeadband = 0.006f;
        internal const float RemoteTrackingMotionFullScale = 0.075f;
        internal const float TrackingAuthorityAgreementDeadband = 0.01f;
        internal const float TrackingAuthorityDisagreement = 0.12f;
        private const float TrackingMotionResponseSeconds = 0.012f;
        private const float ConstraintProjectionWidth = 0.05f;

        internal static Result Build(Request request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            request.profile.EnsureDefaults();

            AssetDatabase.DeleteAsset(request.controllerPath);
            AssetDatabase.DeleteAsset(request.parametersPath);

            var controller = new AnimatorController { name = "YUCP Advanced Viseme Reconstructor" };
            AssetDatabase.CreateAsset(controller, request.controllerPath);
            var result = new Result { controller = controller };
            var prefix = request.component.NormalizedPrefix;
            var internalPrefix = prefix + "/_Internal";

            var graph = new MathGraph(controller, internalPrefix);
            graph.AddParameter("Viseme", AnimatorControllerParameterType.Int, 0f);
            graph.AddParameter("Voice", AnimatorControllerParameterType.Float, 0f);
            // VRChat explicitly converts the built-in Bool to an Animator Float
            // (0/1). Keeping it as a Float avoids a selector state machine and a
            // one-frame animated-parameter handoff before local tracking math.
            graph.AddParameter("IsLocal", AnimatorControllerParameterType.Float, 0f);
            result.externalParameters.Add("Viseme");
            result.externalParameters.Add("Voice");
            result.externalParameters.Add("IsLocal");

            var time = graph.Param("Time", 0f);
            var lastTime = graph.Param("LastTime", 0f);
            var frameTime = graph.Param("FrameTime", 1f / 60f);
            AddTimeLayer(controller, graph, time);
            var visemeIndex = graph.Param("Viseme/Index", 0f);

            var mathRoot = graph.Direct("Reconstruction Math");
            graph.AddOperation(mathRoot, graph.Linear(frameTime, new[]
            {
                Term.Positive(time, 1f), Term.Positive(lastTime, -1f)
            }));
            graph.AddOperation(mathRoot, graph.Copy(time, lastTime, false));

            var tuning = BuildTuningParameters(graph, request, result);

            var alphaViseme = BuildTunableAlpha(
                graph, mathRoot, frameTime, "Alpha/Viseme",
                request.profile.visemeResponseSeconds,
                tuning[AdvancedVisemeTuningControl.SpeechSmoothness],
                0.006f, 0.12f);

            var voiceRaw = BuildTunableVoiceEvidence(
                graph, mathRoot, request.profile,
                tuning[AdvancedVisemeTuningControl.VoiceSensitivity]);
            var voiceFast = graph.Param("Voice/Fast", 0f);
            var voiceSlow = graph.Param(
                AdvancedVisemeParameterContract.Speech(prefix, "Energy"), 0f, false);
            graph.AddOperation(mathRoot, graph.Smooth(voiceRaw, voiceFast, alphaViseme, false));
            graph.AddOperation(mathRoot, graph.Smooth(voiceFast, voiceSlow, alphaViseme, false));
            result.globalParameters.Add(voiceSlow);

            var quietMotion = tuning[AdvancedVisemeTuningControl.QuietMotion];
            var voiceAmplitude = graph.Param("Voice/Amplitude", request.profile.quietSpeechFloor);
            graph.AddOperation(mathRoot, graph.Interpolate(
                quietMotion, MathGraph.AlwaysOneParameter,
                voiceAmplitude, voiceSlow, false));

            var voiceVelocity = graph.Param("Voice/Velocity", 0f);
            graph.AddOperation(mathRoot, graph.Linear(voiceVelocity, new[]
            {
                Term.Positive(voiceFast, 1f / Mathf.Max(0.005f, request.profile.visemeResponseSeconds)),
                Term.Positive(voiceSlow, -1f / Mathf.Max(0.005f, request.profile.visemeResponseSeconds))
            }));
            var onset = graph.Param(
                AdvancedVisemeParameterContract.Speech(prefix, "Onset"), 0f, false);
            var release = graph.Param(
                AdvancedVisemeParameterContract.Speech(prefix, "Release"), 0f, false);
            graph.AddOperation(mathRoot, graph.Map(voiceVelocity, onset, new[] { Point(-1f, 0f), Point(0f, 0f), Point(1f, 1f) }));
            graph.AddOperation(mathRoot, graph.Map(voiceVelocity, release, new[] { Point(-1f, 1f), Point(0f, 0f), Point(1f, 0f) }));
            result.globalParameters.Add(onset);
            result.globalParameters.Add(release);

            var rawVisemes = new string[VisemeReconstructionProfile.VisemeCount];
            var fastVisemes = new string[VisemeReconstructionProfile.VisemeCount];
            var slowVisemes = new string[VisemeReconstructionProfile.VisemeCount];
            var speechWeights = new string[VisemeReconstructionProfile.VisemeCount];
            var fastSpeechWeights = new string[VisemeReconstructionProfile.VisemeCount];
            var betaEnabled = request.component.reconstructionMode ==
                              AdvancedVisemeReconstructionMode.BetaCoarticulation;
            var betaFaceInferenceEnabled = betaEnabled &&
                                           CanBuildFaceConditionedTongueInference(request);
            for (var i = 0; i < rawVisemes.Length; i++)
            {
                var defaultValue = i == 0 ? 1f : 0f;
                rawVisemes[i] = graph.Param($"Viseme/{i}/Raw", defaultValue);
                fastVisemes[i] = graph.Param($"Viseme/{i}/Fast", defaultValue);
                // Keep the observer state internal. The public viseme simplex is
                // published after TrackingBlend exists so speech-only liveliness
                // and the visible mesh share the same causal trajectory.
                slowVisemes[i] = graph.Param($"Viseme/{i}/Slow", defaultValue);
            }
            AddIntToFloatLayer(
                controller, graph, "Viseme", visemeIndex, rawVisemes,
                "YUCP AVR Viseme Decoder");

            // VRChat emits sil both at a real utterance endpoint and in short gaps
            // between words. A leaky speech-history observer treats a short sil as
            // a temporarily missing phonetic sample. Sustained speech charges more
            // history than a brief click, but Voice alone can never pin the mouth.
            // The hold is selected inside each observer motion, rather than through
            // sibling target/alpha parameters, so VRCFury's BlendTree optimization
            // cannot turn it into a delayed feedback pipeline.
            var speechHangover = BuildSpeechHangover(
                graph, mathRoot, frameTime, visemeIndex,
                request.profile, tuning[AdvancedVisemeTuningControl.SilenceStability],
                prefix, result);
            graph.AddOperation(mathRoot, graph.SmoothVectorUnlessHeldSilence(
                rawVisemes, fastVisemes, alphaViseme,
                visemeIndex, speechHangover.history,
                tuning[AdvancedVisemeTuningControl.SilenceStability],
                "Viseme fast observer"));
            graph.AddOperation(mathRoot, graph.SmoothVector(
                fastVisemes, slowVisemes, alphaViseme,
                "Viseme slow observer"));
            var reconstructedFastVisemes = fastVisemes;
            var reconstructedSlowVisemes = slowVisemes;
            BetaCoarticulationGraph betaGraph = null;
            if (betaEnabled)
            {
                betaGraph = BuildBetaCoarticulationWeights(
                    graph, mathRoot,
                    tuning[AdvancedVisemeTuningControl.Coarticulation], frameTime,
                    rawVisemes, fastVisemes, slowVisemes,
                    visemeIndex, speechHangover.history,
                    tuning[AdvancedVisemeTuningControl.SilenceStability],
                    betaFaceInferenceEnabled);
                reconstructedFastVisemes = betaGraph.common.fast;
                reconstructedSlowVisemes = betaGraph.common.slow;
            }

            var speechPresence = speechHangover.presence;
            var voiceGainBase = graph.Param("Voice/GainBase", 0f);
            graph.AddOperation(mathRoot, graph.MultiplyUnlessHeldSilence(
                speechPresence, voiceAmplitude, voiceGainBase,
                visemeIndex, speechHangover.history,
                tuning[AdvancedVisemeTuningControl.SilenceStability], false));
            var voiceGain = graph.Param("Voice/Gain", 0f);
            graph.AddOperation(mathRoot, graph.Multiply(
                tuning[AdvancedVisemeTuningControl.SpeechMotion],
                voiceGainBase, voiceGain, false));

            for (var i = 0; i < rawVisemes.Length; i++)
            {
                speechWeights[i] = graph.Param($"Viseme/{i}/SpeechWeight", 0f);
                fastSpeechWeights[i] = graph.Param($"Viseme/{i}/FastSpeechWeight", 0f);
            }
            AddElementwiseProductProjection(
                graph, mathRoot, voiceGain,
                reconstructedSlowVisemes, speechWeights,
                "Voice-weighted viseme simplex");
            AddElementwiseProductProjection(
                graph, mathRoot, voiceGain,
                reconstructedFastVisemes, fastSpeechWeights,
                "Voice-weighted fast viseme simplex");

            string trackingBlend = null;
            string alphaTracking = null;
            string alphaTrackingMotion = null;
            string localFactor = null;
            var trackingRaw = new Dictionary<AdvancedVisemeArticulator, string>();
            var trackingFast = new Dictionary<AdvancedVisemeArticulator, string>();
            var trackingSlow = new Dictionary<AdvancedVisemeArticulator, string>();

            if (request.trackingEnabled)
            {
                var manualTrackingWeight = MathGraph.AlwaysOneParameter;
                if (request.component.trackingInputs != AdvancedVisemeTrackingInputs.Auto)
                {
                    result.manualTrackingParameter = prefix + "/FaceTrackingEnabled";
                    graph.AddParameter(result.manualTrackingParameter, AnimatorControllerParameterType.Float, 1f);
                    result.externalParameters.Add(result.manualTrackingParameter);
                    manualTrackingWeight = result.manualTrackingParameter;
                }
                var activeParameter = string.IsNullOrEmpty(request.trackingActiveParameter)
                    ? "LipTrackingActive"
                    : request.trackingActiveParameter;
                var activeWeight = activeParameter;
                if (request.trackingActiveAnimatorType ==
                    AnimatorControllerParameterType.Bool)
                {
                    // Respect an authored Bool declaration when merging tailored
                    // controllers. Fresh/generated and established VRCFT buses
                    // use the documented Bool-on-wire/Float-in-Animator conversion
                    // and therefore avoid this compatibility selector entirely.
                    graph.AddParameter(activeParameter, AnimatorControllerParameterType.Bool,
                        request.trackingActiveDefault);
                    AddBoolFloatLayer(
                        controller, graph, activeParameter, "TrackingActiveFactor",
                        request.trackingActiveDefault > 0.5f,
                        "Tracking Active Selector", out activeWeight);
                }
                else
                {
                    graph.AddParameter(activeParameter, AnimatorControllerParameterType.Float,
                        request.trackingActiveDefault);
                }
                result.trackingActiveWeightParameter = activeWeight;
                result.externalParameters.Add(activeParameter);
                if (request.reuseExistingTracking && request.auxiliaryTrackingParameterNames != null)
                {
                    foreach (var parameter in request.auxiliaryTrackingParameterNames.Values
                                 .Where(value => !string.IsNullOrWhiteSpace(value))
                                 .Distinct(StringComparer.Ordinal))
                    {
                        graph.AddParameter(parameter, AnimatorControllerParameterType.Float, 0f);
                        result.externalParameters.Add(parameter);
                    }
                }
                var trackingGate = graph.Param("TrackingGate", 0f);
                graph.AddOperation(mathRoot,
                    graph.Multiply(activeWeight, manualTrackingWeight, trackingGate, false));
                localFactor = "IsLocal";

                var trackingSmoothness =
                    tuning[AdvancedVisemeTuningControl.TrackingSmoothness];
                var alphaLocal = BuildTunableAlpha(
                    graph, mathRoot, frameTime, "Alpha/TrackingLocal",
                    request.profile.localTrackingResponseSeconds,
                    trackingSmoothness, 0.006f, 0.08f);
                var alphaRemote = BuildTunableAlpha(
                    graph, mathRoot, frameTime, "Alpha/TrackingRemote",
                    request.profile.remoteTrackingResponseSeconds,
                    trackingSmoothness, 0.015f, 0.2f);
                alphaTracking = graph.Param("Alpha/Tracking", 0.5f);
                alphaTrackingMotion = graph.Param("Alpha/TrackingMotion", 0.5f);
                graph.AddOperation(mathRoot, graph.AlphaFromDeltaTime(
                    frameTime, alphaTrackingMotion, TrackingMotionResponseSeconds));
                graph.AddOperation(mathRoot, graph.Interpolate(
                    alphaRemote, alphaLocal, alphaTracking, localFactor, false));

                // Acquiring an already-live tracker should feel immediate; losing
                // it should still cross-fade conservatively back to speech. A
                // single asymmetric pole avoids the former ~0.57 s two-pole lag.
                var alphaTrackingBlendAttack = graph.Param("Alpha/TrackingBlendAttack", 0.35f);
                var alphaTrackingBlend = graph.Param("Alpha/TrackingBlend", 0.2f);
                graph.AddOperation(mathRoot, graph.AlphaFromDeltaTime(
                    frameTime, alphaTrackingBlendAttack,
                    request.profile.trackingAcquireResponseSeconds));
                var alphaTrackingBlendRelease = BuildTunableAlpha(
                    graph, mathRoot, frameTime, "Alpha/TrackingBlendRelease",
                    request.profile.trackingBlendResponseSeconds,
                    tuning[AdvancedVisemeTuningControl.TrackingRelease],
                    0.02f, 0.5f);
                graph.AddOperation(mathRoot, graph.Interpolate(
                    alphaTrackingBlendRelease, alphaTrackingBlendAttack,
                    alphaTrackingBlend, trackingGate, false));
                trackingBlend = graph.Param(
                    AdvancedVisemeParameterContract.Speech(prefix, "TrackingBlend"),
                    0f, false);
                graph.AddOperation(mathRoot, graph.Smooth(
                    trackingGate, trackingBlend, alphaTrackingBlend, false));
                result.trackingBlendParameter = trackingBlend;
                result.globalParameters.Add(trackingBlend);

                var observerRaw = new Dictionary<AdvancedVisemeArticulator, string>();
                var observerFast = new Dictionary<AdvancedVisemeArticulator, string>();
                var observerSlow = new Dictionary<AdvancedVisemeArticulator, string>();
                foreach (var articulator in TrackedArticulators(request.effectiveTrackingInputs))
                {
                    var binding = request.profile.FindBinding(articulator);
                    if (binding == null || string.IsNullOrWhiteSpace(binding.trackingParameter)) continue;
                    var signed = IsSigned(articulator);
                    if (!TryResolveTrackingParameter(request, articulator, binding, out var parameter)) continue;
                    var input = UsesBinaryTracking(request)
                        ? DecodeBinaryTracking(graph, mathRoot, parameter, articulator, signed, request.component.trackingEncoding)
                        : parameter;
                    if (UsesBinaryTracking(request))
                    {
                        result.externalParameters.AddRange(BinaryParameterNames(
                            parameter, articulator, request.component.trackingEncoding));
                    }
                    else
                    {
                        graph.AddParameter(input, AnimatorControllerParameterType.Float, 0f);
                        result.externalParameters.Add(input);
                    }
                    trackingRaw[articulator] = input;
                    if (request.directPoseArticulators != null &&
                        request.directPoseArticulators.Contains(articulator))
                    {
                        // This is already the template's final decoded pose bus.
                        // Preserve its native local/remote response exactly.
                        trackingFast[articulator] = input;
                        trackingSlow[articulator] = input;
                        continue;
                    }
                    var fast = graph.Param($"Tracking/{articulator}/Fast", 0f);
                    var slow = graph.Param($"Tracking/{articulator}/Slow", 0f);
                    trackingFast[articulator] = fast;
                    trackingSlow[articulator] = slow;
                    observerRaw[articulator] = input;
                    observerFast[articulator] = fast;
                    observerSlow[articulator] = slow;
                }

                if (observerFast.Count > 0)
                {
                    // Every non-native tracking coordinate uses the same pole.
                    // Evaluating the observer as two articulation vectors is
                    // algebraically identical to one Smooth tree per scalar, but
                    // shares the interpolation traversal and zero baselines.
                    graph.AddOperation(mathRoot, graph.InterpolateArticulationVector(
                        observerFast, observerRaw, observerFast, alphaTracking,
                        "Tracking observer fast vector"));
                    graph.AddOperation(mathRoot, graph.InterpolateArticulationVector(
                        observerSlow, observerFast, observerSlow, alphaTracking,
                        "Tracking observer slow vector"));
                }
            }
            else
            {
                trackingBlend = graph.Param(
                    AdvancedVisemeParameterContract.Speech(prefix, "TrackingBlend"),
                    0f, false);
                result.trackingBlendParameter = trackingBlend;
                result.globalParameters.Add(trackingBlend);
            }

            // Speech-only rendering may follow the one-pole observer more
            // closely, but it never extrapolates beyond it. One shared lead is
            // used for all visemes and articulators, preserving the simplex and
            // the calibrated identity U(Cp) + Rp = Vp. TrackingBlend fades the
            // lead continuously to exact legacy/tracked behavior at one.
            var speechRenderLead = graph.Param("Speech/RenderLead", 0f);
            graph.AddOperation(mathRoot, graph.ScaleByInverseUnitWeight(
                tuning[AdvancedVisemeTuningControl.SpeechLiveliness],
                trackingBlend,
                speechRenderLead,
                AdvancedVisemeMath.MaximumSpeechLivelinessLead));

            var renderedVisemes = new string[reconstructedSlowVisemes.Length];
            for (var i = 0; i < renderedVisemes.Length; i++)
                renderedVisemes[i] = graph.Param(
                    AdvancedVisemeParameterContract.Viseme(prefix, i),
                    i == 0 ? 1f : 0f,
                    false);
            result.globalParameters.AddRange(renderedVisemes);

            var renderedSpeechWeights = new string[speechWeights.Length];
            for (var i = 0; i < renderedSpeechWeights.Length; i++)
                renderedSpeechWeights[i] = graph.Param(
                    $"Viseme/{i}/RenderedSpeechWeight", 0f);
            // The normalized public simplex and its voice-weighted rendering
            // share one vector operation, avoiding a second 15-channel pass.
            graph.AddOperation(mathRoot, graph.InterpolateVector(
                reconstructedSlowVisemes.Concat(speechWeights).ToArray(),
                reconstructedFastVisemes.Concat(fastSpeechWeights).ToArray(),
                renderedVisemes.Concat(renderedSpeechWeights).ToArray(),
                speechRenderLead,
                "Speech-liveliness viseme render vector"));

            var vowelWeightRaw = graph.Param("Speech/VowelWeightRaw", 0f);
            var vowelWeight = graph.Param(
                AdvancedVisemeParameterContract.Speech(prefix, "Vowel"), 0f, false);
            graph.AddOperation(mathRoot, graph.Linear(vowelWeightRaw, new[]
            {
                Term.Positive(renderedVisemes[10], 1f), Term.Positive(renderedVisemes[11], 1f),
                Term.Positive(renderedVisemes[12], 1f), Term.Positive(renderedVisemes[13], 1f),
                Term.Positive(renderedVisemes[14], 1f)
            }));
            graph.AddOperation(mathRoot,
                graph.Multiply(speechPresence, vowelWeightRaw, vowelWeight, false));
            result.globalParameters.Add(vowelWeight);

            var articulationFast = new Dictionary<AdvancedVisemeArticulator, string>();
            var articulationSlow = new Dictionary<AdvancedVisemeArticulator, string>();
            var speechArticulationFast = new Dictionary<AdvancedVisemeArticulator, string>();
            var speechArticulationSlow = new Dictionary<AdvancedVisemeArticulator, string>();
            var modelSpeechCenters = new Dictionary<AdvancedVisemeArticulator, string>();
            var calibratedTrackingRaw = new Dictionary<AdvancedVisemeArticulator, string>();
            var calibratedTrackingFast = new Dictionary<AdvancedVisemeArticulator, string>();
            var calibratedTrackingSlow = new Dictionary<AdvancedVisemeArticulator, string>();
            var calibratedTrackingPose = new Dictionary<AdvancedVisemeArticulator, string>();
            var calibratedTrackingLead = new Dictionary<AdvancedVisemeArticulator, string>();
            var trackingGains = new Dictionary<AdvancedVisemeArticulator, string>();
            var articulators = SynthesizedArticulators().ToArray();

            // Build the complete speech prior first. Beta inference needs both the
            // visible speech center and calibrated visible tracking before tongue
            // channels are fused, so articulation cannot be constructed in one
            // order-dependent pass.
            var normalFastProjection =
                new Dictionary<AdvancedVisemeArticulator, string>();
            var normalSlowProjection =
                new Dictionary<AdvancedVisemeArticulator, string>();
            var corpusFastProjection = new Dictionary<
                AdvancedVisemeArticulatorGroup,
                Dictionary<AdvancedVisemeArticulator, string>>();
            var corpusSlowProjection = new Dictionary<
                AdvancedVisemeArticulatorGroup,
                Dictionary<AdvancedVisemeArticulator, string>>();
            var betaUnscaledFast =
                new Dictionary<AdvancedVisemeArticulator, string>();
            var betaUnscaledSlow =
                new Dictionary<AdvancedVisemeArticulator, string>();

            foreach (var articulator in articulators)
            {
                var speechFast = graph.Param($"Articulation/{articulator}/SpeechFast", 0f);
                var speechSlow = graph.Param($"Articulation/{articulator}/SpeechSlow", 0f);
                var modelSpeechCenter = speechSlow;
                if (betaGraph == null)
                {
                    normalFastProjection[articulator] = speechFast;
                    normalSlowProjection[articulator] = speechSlow;
                }
                else
                {
                    var group = AdvancedVisemeCoarticulationModel.GroupFor(articulator);
                    if (!corpusFastProjection.TryGetValue(group, out var fastOutputs))
                    {
                        fastOutputs = new Dictionary<AdvancedVisemeArticulator, string>();
                        corpusFastProjection[group] = fastOutputs;
                        corpusSlowProjection[group] =
                            new Dictionary<AdvancedVisemeArticulator, string>();
                    }
                    var unscaledFast = graph.Param($"Articulation/{articulator}/CorpusFast", 0f);
                    var unscaledSlow = graph.Param($"Articulation/{articulator}/CorpusSlow", 0f);
                    fastOutputs[articulator] = unscaledFast;
                    corpusSlowProjection[group][articulator] = unscaledSlow;
                    betaUnscaledFast[articulator] = unscaledFast;
                    betaUnscaledSlow[articulator] = unscaledSlow;
                    // The corpus model is centered in normalized articulator space.
                    // Voice is an expressive amplitude, not part of that semantic
                    // calibration, so use the unscaled coarticulated center here.
                    modelSpeechCenter = unscaledSlow;

                }

                speechArticulationFast[articulator] = speechFast;
                speechArticulationSlow[articulator] = speechSlow;
                modelSpeechCenters[articulator] = modelSpeechCenter;
                // Output correction only needs to know which articulators have a
                // speech basis; it reconstructs that basis directly from the
                // visible simplex. Keeping a second projected scalar here was a
                // dead Animator output (and tongue tuning multiplied it again).
                result.speechArticulationParameters[articulator] = speechSlow;
            }

            if (betaGraph == null)
            {
                AddVisemeMatrixProjection(
                    graph, mathRoot, request, "Speech articulation fast",
                    fastSpeechWeights, normalFastProjection);
                AddVisemeMatrixProjection(
                    graph, mathRoot, request, "Speech articulation slow",
                    speechWeights, normalSlowProjection);
            }
            else
            {
                foreach (var group in corpusFastProjection.Keys
                             .OrderBy(value => (int)value))
                    AddContractedBetaArticulationProjection(
                        graph, mathRoot, request, group,
                        betaGraph, corpusFastProjection[group],
                        corpusSlowProjection[group], visemeIndex,
                        speechHangover.history,
                        tuning[AdvancedVisemeTuningControl.SilenceStability]);
                graph.AddOperation(mathRoot, graph.ScaleArticulationVector(
                    voiceGain, betaUnscaledFast, speechArticulationFast,
                    "Voice-scaled corpus articulation fast"));
                graph.AddOperation(mathRoot, graph.ScaleArticulationVector(
                    voiceGain, betaUnscaledSlow, speechArticulationSlow,
                    "Voice-scaled corpus articulation slow"));
            }

            foreach (var articulator in articulators)
            {
                if (!request.trackingEnabled || !trackingSlow.TryGetValue(articulator, out var trackedSlow))
                    continue;
                var binding = request.profile.FindBinding(articulator);
                AdvancedVisemeExternalPose externalPose = null;
                if (request.reuseExistingTracking && request.externalPoses != null)
                    request.externalPoses.TryGetValue(articulator, out externalPose);
                var calibratedRaw = Calibrate(
                    graph, mathRoot, trackingRaw[articulator], binding,
                    articulator, "ModelRaw", externalPose);
                calibratedTrackingRaw[articulator] = calibratedRaw;
                if (request.reuseExistingTracking &&
                    request.directPoseArticulators != null &&
                    request.directPoseArticulators.Contains(articulator))
                {
                    // A structurally pose-connected template proxy is already
                    // decoded, merged, and smoothed by that template. Calibrating
                    // it three times and feeding it through the adaptive observer
                    // only duplicated work and added parameter-frame latency.
                    calibratedTrackingSlow[articulator] = calibratedRaw;
                    calibratedTrackingFast[articulator] = calibratedRaw;
                    calibratedTrackingPose[articulator] = calibratedRaw;
                    calibratedTrackingLead[articulator] = calibratedRaw;
                    continue;
                }
                calibratedTrackingSlow[articulator] =
                    Calibrate(graph, mathRoot, trackedSlow, binding, articulator, "Slow", externalPose);
                calibratedTrackingFast[articulator] =
                    Calibrate(graph, mathRoot, trackingFast[articulator], binding, articulator, "Fast", externalPose);

                // Both reused proxy streams and freshly decoded parameters need one
                // and only one denoising stage here. The adaptive fast/slow observer
                // follows deliberate motion with the one-pole signal, but settles
                // onto the two-pole signal when only OSC or quantization noise is
                // moving. Raw values are never published to the mesh.
                var pose = BuildAdaptiveTrackingPose(
                    graph, mathRoot, articulator,
                    calibratedTrackingFast[articulator], calibratedTrackingSlow[articulator],
                    alphaTrackingMotion, localFactor, out var motion);
                calibratedTrackingPose[articulator] = pose;
                var lead = graph.Param($"Tracking/{articulator}/Lead", 0f);
                graph.AddOperation(mathRoot, graph.Interpolate(
                    calibratedTrackingFast[articulator], calibratedTrackingRaw[articulator],
                    lead, motion, IsSigned(articulator)));
                calibratedTrackingLead[articulator] = lead;
            }

            var nativeTongueCapabilities = BuildNativeTongueCapabilities(
                graph, mathRoot, request, frameTime, trackingBlend, trackingRaw);

            foreach (var articulator in articulators)
            {
                if (!calibratedTrackingPose.ContainsKey(articulator)) continue;
                var binding = request.profile.FindBinding(articulator);
                var remoteReliability = request.component.fusionMode == AdvancedVisemeFusionMode.TrackerAuthoritative
                    ? 1f
                    : binding.remoteReliability;
                var reliability = BuildTrackingAuthority(
                    graph, mathRoot, articulator,
                    speechArticulationSlow[articulator], calibratedTrackingPose[articulator],
                    localFactor, remoteReliability,
                    tuning[AdvancedVisemeTuningControl.RemoteTrust]);
                var baseGain = graph.Param($"Tracking/{articulator}/BaseGain", 0f);
                graph.AddOperation(mathRoot, graph.Multiply(
                    trackingBlend, reliability, baseGain, false));
                var gain = baseGain;
                if (RequiresNativeTongueCapability(articulator))
                {
                    gain = graph.Param($"Tracking/{articulator}/Gain", 0f);
                    var nativeTongueCapability = nativeTongueCapabilities.TryGetValue(
                        articulator, out var capability)
                        ? capability
                        : graph.Param($"Tracking/{articulator}/NativeCapability", 0f);
                    graph.AddOperation(mathRoot,
                        graph.Multiply(baseGain, nativeTongueCapability, gain, false));
                }
                trackingGains[articulator] = gain;
                result.trackingGainParameters[articulator] = gain;
                if (request.reuseExistingTracking &&
                    request.directPoseArticulators != null &&
                    request.directPoseArticulators.Contains(articulator))
                    continue;
                var inverseGain = graph.Param($"Tracking/{articulator}/InverseGain", 1f);
                graph.AddOperation(mathRoot, graph.Linear(inverseGain, new[]
                {
                    Term.Constant(1f), Term.Positive(gain, -1f)
                }));
                result.inverseTrackingGainParameters[articulator] = inverseGain;
            }

            var visibleSpeechWeights = BuildVisibleSpeechWeights(
                graph, mathRoot, request, renderedSpeechWeights, trackingGains);

            // The learned estimator is intentionally absent from Normal mode.
            // It is inserted here, before direct tongue measurements, so a real
            // tongue tracker remains authoritative at gain=1.
            FacePhonePosteriorGraph facePhonePosterior = null;
            if (betaGraph != null && betaFaceInferenceEnabled)
            {
                facePhonePosterior = ApplyBetaTongueInference(
                    graph, mathRoot, request, result, betaGraph, frameTime,
                    speechPresence, voiceGain,
                    speechArticulationFast, speechArticulationSlow, modelSpeechCenters,
                    calibratedTrackingRaw, trackingGains, tuning);
            }

            ApplyTongueAxisStrengths(
                graph, mathRoot, speechArticulationFast, speechArticulationSlow,
                tuning);

            var renderedSpeechArticulation = articulators.ToDictionary(
                articulator => articulator,
                articulator => graph.Param(
                    $"Articulation/{articulator}/RenderedSpeech", 0f));
            graph.AddOperation(mathRoot, graph.InterpolateArticulationVector(
                speechArticulationSlow, speechArticulationFast,
                renderedSpeechArticulation, speechRenderLead,
                "Speech-liveliness articulation vector"));

            string hiddenResidualSpeechDelta = null;
            if (facePhonePosterior != null &&
                !string.IsNullOrEmpty(facePhonePosterior.hiddenResidualDelta) &&
                request.calibration != null && request.calibration.success &&
                !string.IsNullOrEmpty(request.calibration.hiddenPhoneResidualBlendShapeName))
            {
                var hiddenResidualSpeechBase = graph.Param(
                    "PhonePosterior/Residual/SpeechBase", 0f);
                graph.AddOperation(mathRoot, graph.Multiply(
                    facePhonePosterior.hiddenResidualDelta, voiceGain,
                    hiddenResidualSpeechBase, true));
                hiddenResidualSpeechDelta = graph.Param(
                    "PhonePosterior/Residual/SpeechDelta", 0f);
                graph.AddOperation(mathRoot, graph.Multiply(
                    hiddenResidualSpeechBase,
                    tuning[AdvancedVisemeTuningControl.HiddenDetail],
                    hiddenResidualSpeechDelta, true));
            }

            foreach (var articulator in articulators)
            {
                var signed = IsSigned(articulator);
                var speechFast = speechArticulationFast[articulator];
                var speechSlow = renderedSpeechArticulation[articulator];

                var finalFast = speechFast;
                var finalSlow = speechSlow;
                var directPose = request.reuseExistingTracking &&
                                 request.directPoseArticulators != null &&
                                 request.directPoseArticulators.Contains(articulator);
                if (!directPose && trackingGains.TryGetValue(articulator, out var gain))
                {
                    var calibratedPose = calibratedTrackingPose[articulator];
                    var calibratedLead = calibratedTrackingLead[articulator];

                    var trackingContribution = graph.Param($"Tracking/{articulator}/Contribution", 0f);
                    graph.AddOperation(mathRoot, graph.Multiply(gain, calibratedPose, trackingContribution, signed));
                    result.trackingContributionParameters[articulator] = trackingContribution;

                    finalSlow = graph.Param($"Articulation/{articulator}/FusedSlow", 0f);
                    finalFast = graph.Param($"Articulation/{articulator}/FusedFast", 0f);
                    // Convex interpolation is exactly
                    //   (1 - gain) * speech + gain * tracking.
                    // Encoding it as one 1D tree removes four scalar products,
                    // two sums, and their temporary AAPs per articulator. It also
                    // keeps the measured tracker on the shortest Animator path.
                    graph.AddOperation(mathRoot, graph.Interpolate(
                        speechSlow, calibratedPose, finalSlow, gain, signed));
                    graph.AddOperation(mathRoot, graph.Interpolate(
                        speechFast, calibratedLead, finalFast, gain, signed));
                }

                articulationFast[articulator] = finalFast;
                articulationSlow[articulator] = finalSlow;
            }

            if (request.trackingEnabled && request.component.fusionMode == AdvancedVisemeFusionMode.PhoneticAssist)
            {
                var constraintBases = BuildConstraintConfidenceBases(
                    graph, mathRoot, speechPresence, trackingBlend, localFactor,
                    trackingGains, tuning);
                ApplyConstraints(
                    graph, mathRoot, request.profile, reconstructedFastVisemes,
                    constraintBases, articulationFast, "Fast");
                ApplyConstraints(
                    graph, mathRoot, request.profile, renderedVisemes,
                    constraintBases, articulationSlow, "Slow");
            }

            // Do not impose a generic MouthOpen/MouthClosed or Pucker/Suck
            // envelope after measurement fusion. Tailored VRCFT templates often
            // use coupled, non-exclusive coordinates; clamping them here changed a
            // valid measured pose and created a hard switching surface. Authored
            // speech remains rig-defined, while only the sparse phonetic
            // constraints above may alter a tracked lower face.

            var publicArticulationSources =
                new Dictionary<AdvancedVisemeArticulator, string>();
            var publicArticulationOutputs =
                new Dictionary<AdvancedVisemeArticulator, string>();
            foreach (var articulator in articulators)
            {
                var signed = IsSigned(articulator);
                var source = articulationSlow[articulator];
                var output = graph.Param(prefix + "/Articulation/" + articulator, 0f, false);
                if (request.reuseExistingTracking &&
                    request.directPoseArticulators != null &&
                    request.directPoseArticulators.Contains(articulator) &&
                    calibratedTrackingRaw.TryGetValue(articulator, out var directTrackingOutput) &&
                    trackingGains.TryGetValue(articulator, out var directTrackingGain))
                {
                    // Public articulation keeps its normalized YUCP contract.
                    // Only the visible calibrated pose consumes the native proxy
                    // directly; this diagnostic mirror is never read back into
                    // rendering, so its calibration stage cannot add face lag.
                    graph.AddOperation(mathRoot, graph.SelectMotion(
                        directTrackingGain,
                        graph.Copy(source, output, signed),
                        graph.Copy(directTrackingOutput, output, signed),
                        $"Native {articulator} public output gate"));
                }
                else
                {
                    publicArticulationSources[articulator] = source;
                    publicArticulationOutputs[articulator] = output;
                }

                articulationSlow[articulator] = output;
                result.articulationParameters[articulator] = output;
                result.globalParameters.Add(output);
            }

            if (publicArticulationOutputs.Count > 0)
                graph.AddOperation(mathRoot, graph.CopyArticulationVector(
                    publicArticulationSources, publicArticulationOutputs,
                    "Public articulation vector"));

            var velocityRawOutputs = new Dictionary<AdvancedVisemeArticulator, string>();
            foreach (var articulator in articulators)
                velocityRawOutputs[articulator] = graph.Param(
                    $"Velocity/{articulator}/Raw", 0f);
            graph.AddOperation(mathRoot, graph.DifferenceArticulationVector(
                articulationFast, articulationSlow, velocityRawOutputs,
                1f / Mathf.Max(0.005f, request.profile.visemeResponseSeconds),
                "Articulation velocity difference vector"));

            foreach (var articulator in articulators)
            {
                var velocity = graph.Param(prefix + "/Velocity/" + articulator, 0f, false);
                graph.AddOperation(mathRoot, graph.Map(
                    velocityRawOutputs[articulator], velocity, new[]
                    {
                        Point(-1f, -1f), Point(0f, 0f), Point(1f, 1f)
                    }));
                result.globalParameters.Add(velocity);
            }

            BuildSpeechEvidence(graph, mathRoot, prefix, renderedVisemes, voiceSlow, result);

            AddMotionLayer(controller, graph, "YUCP AVR Math", mathRoot);

            if (request.component.mouthOwnership == AdvancedVisemeMouthOwnership.DriveLowerFace)
            {
                var outputRoot = graph.Direct("Lower Face Output");
                BuildOutputTree(
                    request, result, graph, outputRoot, renderedSpeechWeights, visibleSpeechWeights,
                    hiddenResidualSpeechDelta,
                    tuning[AdvancedVisemeTuningControl.AuthoredDetail],
                    tuning[AdvancedVisemeTuningControl.ContradictionFade],
                    trackingGains);
                AddMotionLayer(controller, graph, "YUCP AVR Output", outputRoot);
            }

            BuildCompactTuningSync(controller, graph, request, result);

            var expressionParameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            expressionParameters.name = "YUCP Advanced Viseme Inputs";
            expressionParameters.parameters = BuildExpressionParameters(request, result).ToArray();
            AssetDatabase.CreateAsset(expressionParameters, request.parametersPath);
            result.parameters = expressionParameters;

            foreach (var global in result.globalParameters.Distinct())
            {
                graph.AddParameter(global, AnimatorControllerParameterType.Float, global.EndsWith("/Viseme/sil", StringComparison.Ordinal) ? 1f : 0f);
            }

            graph.PruneUnreachableMotions();
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssetIfDirty(controller);
            AssetDatabase.SaveAssetIfDirty(expressionParameters);
            AssetDatabase.ImportAsset(request.controllerPath);
            return result;
        }

        private static Dictionary<AdvancedVisemeTuningControl, string> BuildTuningParameters(
            MathGraph graph,
            Request request,
            Result result)
        {
            var tuning = new Dictionary<AdvancedVisemeTuningControl, string>();
            foreach (var control in AdvancedVisemeTuning.Controls)
            {
                var defaultValue = AdvancedVisemeTuning.DefaultValue(request.profile, control);
                var section = AdvancedVisemeTuning.Section(control);
                var exposed = request.component.createTuningMenu &&
                              IsTuningControlRelevant(request, control) &&
                              (request.component.tuningMenuSections & section) != 0;
                var parameter = exposed
                    ? graph.Param(request.component.TuningParameterName(control), defaultValue, false)
                    : graph.Param("Tuning/" + control, defaultValue);
                tuning[control] = parameter;
                if (!exposed) continue;
                result.tuningParameters[control] = parameter;
                result.externalParameters.Add(parameter);
            }
            return tuning;
        }

        private static void BuildCompactTuningSync(
            AnimatorController controller,
            MathGraph graph,
            Request request,
            Result result)
        {
            if (request.component.tuningSyncMode !=
                    AdvancedVisemeTuningSyncMode.CompactSynced ||
                result.tuningParameters.Count == 0)
                return;

            var channels = result.tuningParameters
                .OrderBy(pair => (int)pair.Key)
                .ToArray();
            var prefix = request.component.NormalizedPrefix;
            result.tuningSyncDataParameter =
                AdvancedVisemeTuning.CompactSyncDataParameter(prefix);
            result.tuningSyncFocusParameter =
                AdvancedVisemeTuning.CompactSyncFocusParameter(prefix);
            result.tuningSyncBits =
                AdvancedVisemeTuning.CompactSyncTransportBits(channels.Length);

            graph.AddParameter(
                result.tuningSyncDataParameter,
                AnimatorControllerParameterType.Int, 0f);
            graph.AddParameter(
                result.tuningSyncFocusParameter,
                AnimatorControllerParameterType.Int, 0f);
            result.externalParameters.Add(result.tuningSyncDataParameter);
            result.externalParameters.Add(result.tuningSyncFocusParameter);

            var indexBitCount =
                AdvancedVisemeTuning.CompactSyncTransportIndexBits;
            for (var bit = 0; bit < indexBitCount; bit++)
            {
                var parameter =
                    AdvancedVisemeTuning.CompactSyncIndexParameter(prefix, bit);
                graph.AddParameter(parameter, AnimatorControllerParameterType.Bool, 0f);
                result.tuningSyncIndexParameters.Add(parameter);
                result.externalParameters.Add(parameter);
            }

            var clockParameter = graph.Param("TuningSync/Clock", 0f);
            var clock = graph.Clip("Compact tuning sync clock");
            const float batchSeconds = 0.1f;
            AnimationUtility.SetEditorCurve(
                clock,
                EditorCurveBinding.FloatCurve(
                    string.Empty, typeof(Animator), clockParameter),
                AnimationCurve.Linear(0f, 0f, batchSeconds, 1f));

            var machine = AddStateLayer(
                controller, graph, "YUCP AVR Compact Tuning Sync");
            // Parameter drivers and transitions continue to run at zero layer
            // weight, as in VRCFury's compressor. The timing clip supplies only
            // normalized state time and contributes no blended Animator output.
            var transportLayers = controller.layers;
            transportLayers[transportLayers.Length - 1].defaultWeight = 0f;
            controller.layers = transportLayers;
            var idle = machine.AddState("Route");
            var localRouter = machine.AddState("Local Focus Router");
            var remoteLost = machine.AddState("Remote Awaiting Channel");
            idle.writeDefaultValues = true;
            localRouter.writeDefaultValues = true;
            remoteLost.writeDefaultValues = true;
            machine.defaultState = idle;

            var localEntry = idle.AddTransition(localRouter);
            ConfigureImmediate(localEntry);
            localEntry.AddCondition(
                AnimatorConditionMode.Greater, 0.5f, "IsLocal");
            var remoteEntry = idle.AddTransition(remoteLost);
            ConfigureImmediate(remoteEntry);
            remoteEntry.AddCondition(
                AnimatorConditionMode.Less, 0.5f, "IsLocal");

            var localBecameRemote = localRouter.AddTransition(remoteLost);
            ConfigureImmediate(localBecameRemote);
            localBecameRemote.AddCondition(
                AnimatorConditionMode.Less, 0.5f, "IsLocal");
            var remoteBecameLocal = remoteLost.AddTransition(localRouter);
            ConfigureImmediate(remoteBecameLocal);
            remoteBecameLocal.AddCondition(
                AnimatorConditionMode.Greater, 0.5f, "IsLocal");

            var sendStates = new AnimatorState[channels.Length];
            var extraFrameStates = new AnimatorState[channels.Length];
            var receiveStates = new AnimatorState[channels.Length];
            for (var index = 0; index < channels.Length; index++)
            {
                var id = AdvancedVisemeTuning.CompactSyncChannelId(
                    channels[index].Key);
                var label = AdvancedVisemeTuning.Label(channels[index].Key);

                var send = machine.AddState("Send " + label);
                send.writeDefaultValues = true;
                send.motion = clock;
                AddTuningDriver(graph, send, true,
                    CompactIndexWrites(
                            id, result.tuningSyncIndexParameters)
                        .Concat(new[]
                        {
                            CompactCopy(
                                channels[index].Value,
                                result.tuningSyncDataParameter,
                                0f, 1f,
                                // Avatar Parameter Driver truncates Float->Int.
                                // The half-code offset makes that truncation an
                                // exact round-to-nearest over integer codes 0..254.
                                0.5f,
                                AdvancedVisemeTuning.CompactSyncQuantizationMaximum +
                                0.5f)
                        })
                        .ToArray());
                sendStates[index] = send;

                var extra = machine.AddState("Extra Frame " + label);
                extra.writeDefaultValues = true;
                extraFrameStates[index] = extra;

                var receive = machine.AddState("Receive " + label);
                receive.writeDefaultValues = true;
                receive.motion = clock;
                AddTuningDriver(graph, receive, false,
                    CompactCopy(
                        result.tuningSyncDataParameter,
                        channels[index].Value,
                        0f,
                        AdvancedVisemeTuning.CompactSyncQuantizationMaximum,
                        0f, 1f));
                receiveStates[index] = receive;

                var route = localRouter.AddTransition(send);
                ConfigureImmediate(route);
                route.AddCondition(
                    AnimatorConditionMode.Equals, id,
                    result.tuningSyncFocusParameter);

                var recover = remoteLost.AddTransition(receive);
                ConfigureImmediate(recover);
                recover.AddCondition(
                    AnimatorConditionMode.Less, 0.5f, "IsLocal");
                AddCompactIndexConditions(
                    recover, id, result.tuningSyncIndexParameters);
            }

            var unfocusedRoute = localRouter.AddTransition(sendStates[0]);
            ConfigureImmediate(unfocusedRoute);
            unfocusedRoute.AddCondition(
                AnimatorConditionMode.Equals, 0f,
                result.tuningSyncFocusParameter);

            for (var index = 0; index < channels.Length; index++)
            {
                var id = AdvancedVisemeTuning.CompactSyncChannelId(
                    channels[index].Key);
                var next = (index + 1) % channels.Length;
                var nextId = AdvancedVisemeTuning.CompactSyncChannelId(
                    channels[next].Key);

                var sendBecameRemote =
                    sendStates[index].AddTransition(remoteLost);
                ConfigureImmediate(sendBecameRemote);
                sendBecameRemote.AddCondition(
                    AnimatorConditionMode.Less, 0.5f, "IsLocal");

                var extraBecameRemote =
                    extraFrameStates[index].AddTransition(remoteLost);
                ConfigureImmediate(extraBecameRemote);
                extraBecameRemote.AddCondition(
                    AnimatorConditionMode.Less, 0.5f, "IsLocal");

                var receiveBecameLocal =
                    receiveStates[index].AddTransition(localRouter);
                ConfigureImmediate(receiveBecameLocal);
                receiveBecameLocal.AddCondition(
                    AnimatorConditionMode.Greater, 0.5f, "IsLocal");

                var continueFocused = sendStates[index].AddTransition(localRouter);
                ConfigureTimed(continueFocused, 1f);
                continueFocused.AddCondition(
                    AnimatorConditionMode.Greater, 0.5f,
                    result.tuningSyncFocusParameter);

                var continueCarousel =
                    sendStates[index].AddTransition(extraFrameStates[index]);
                ConfigureTimed(continueCarousel, 1f);
                continueCarousel.AddCondition(
                    AnimatorConditionMode.Equals, 0f,
                    result.tuningSyncFocusParameter);

                var prioritizeFocus =
                    extraFrameStates[index].AddTransition(localRouter);
                ConfigureImmediate(prioritizeFocus);
                prioritizeFocus.AddCondition(
                    AnimatorConditionMode.Greater, 0.5f,
                    result.tuningSyncFocusParameter);
                var nextSend =
                    extraFrameStates[index].AddTransition(sendStates[next]);
                ConfigureImmediate(nextSend);

                var receiveNext =
                    receiveStates[index].AddTransition(receiveStates[next]);
                ConfigureImmediate(receiveNext);
                receiveNext.AddCondition(
                    AnimatorConditionMode.Less, 0.5f, "IsLocal");
                AddCompactIndexConditions(
                    receiveNext, nextId, result.tuningSyncIndexParameters);

                // A focused radial can leave the same channel selected while its
                // data changes. Re-enter at the network cadence so the copy driver
                // samples the newest carrier value without a strobe parameter.
                var refresh =
                    receiveStates[index].AddTransition(receiveStates[index]);
                ConfigureTimed(refresh, 1f);
                refresh.canTransitionToSelf = true;
                refresh.AddCondition(
                    AnimatorConditionMode.Less, 0.5f, "IsLocal");
                AddCompactIndexConditions(
                    refresh, id, result.tuningSyncIndexParameters);

                // The expected-next transition is intentionally first. Every
                // other index mismatch falls back to the recovery router, which
                // handles dropped/reordered packets and focused-channel jumps.
                for (var bit = 0;
                     bit < result.tuningSyncIndexParameters.Count;
                     bit++)
                {
                    var expected = (id & (1 << bit)) != 0;
                    var lost = receiveStates[index].AddTransition(remoteLost);
                    ConfigureImmediate(lost);
                    lost.AddCondition(
                        AnimatorConditionMode.Less, 0.5f, "IsLocal");
                    lost.AddCondition(
                        expected ? AnimatorConditionMode.IfNot : AnimatorConditionMode.If,
                        0f,
                        result.tuningSyncIndexParameters[bit]);
                }
            }
        }

        private static IEnumerable<VRC_AvatarParameterDriver.Parameter>
            CompactIndexWrites(int id, IReadOnlyList<string> parameters)
        {
            for (var bit = 0; bit < parameters.Count; bit++)
                yield return new VRC_AvatarParameterDriver.Parameter
                {
                    name = parameters[bit],
                    type = VRC_AvatarParameterDriver.ChangeType.Set,
                    value = (id & (1 << bit)) != 0 ? 1f : 0f
                };
        }

        private static VRC_AvatarParameterDriver.Parameter CompactCopy(
            string source,
            string destination,
            float sourceMinimum,
            float sourceMaximum,
            float destinationMinimum,
            float destinationMaximum)
        {
            return new VRC_AvatarParameterDriver.Parameter
            {
                source = source,
                name = destination,
                type = VRC_AvatarParameterDriver.ChangeType.Copy,
                convertRange = true,
                sourceMin = sourceMinimum,
                sourceMax = sourceMaximum,
                destMin = destinationMinimum,
                destMax = destinationMaximum
            };
        }

        private static void AddTuningDriver(
            MathGraph graph,
            AnimatorState state,
            bool localOnly,
            params VRC_AvatarParameterDriver.Parameter[] parameters)
        {
            var driver = ScriptableObject.CreateInstance<VRCAvatarParameterDriver>();
            driver.name = localOnly
                ? "YUCP Compact Tuning Sender"
                : "YUCP Compact Tuning Receiver";
            driver.localOnly = localOnly;
            driver.isEnabled = true;
            driver.debugString = localOnly
                ? "YUCP owner-only tuning transport"
                : "YUCP remote tuning decode";
            driver.parameters = parameters.ToList();
            graph.SubAsset(driver);
            state.behaviours = state.behaviours
                .Concat(new StateMachineBehaviour[] { driver })
                .ToArray();
        }

        private static void AddCompactIndexConditions(
            AnimatorStateTransition transition,
            int id,
            IReadOnlyList<string> parameters)
        {
            for (var bit = 0; bit < parameters.Count; bit++)
                transition.AddCondition(
                    (id & (1 << bit)) != 0
                        ? AnimatorConditionMode.If
                        : AnimatorConditionMode.IfNot,
                    0f, parameters[bit]);
        }

        private static void ConfigureImmediate(
            AnimatorStateTransition transition)
        {
            transition.hasExitTime = false;
            transition.duration = 0f;
            transition.hasFixedDuration = true;
            transition.canTransitionToSelf = false;
            transition.interruptionSource = TransitionInterruptionSource.None;
        }

        private static void ConfigureTimed(
            AnimatorStateTransition transition,
            float exitTime)
        {
            transition.hasExitTime = true;
            transition.exitTime = Mathf.Max(0f, exitTime);
            transition.duration = 0f;
            transition.hasFixedDuration = true;
            transition.canTransitionToSelf = false;
            transition.interruptionSource = TransitionInterruptionSource.None;
        }

        internal static bool IsTuningControlRelevant(
            Request request,
            AdvancedVisemeTuningControl control)
        {
            var beta = request.component.reconstructionMode ==
                       AdvancedVisemeReconstructionMode.BetaCoarticulation;
            var calibrated = request.calibration != null && request.calibration.success;
            var externalPoseCalibration = HasExternalPoseCalibration(request);
            var residualOutput = calibrated &&
                                 request.component.mouthOwnership ==
                                 AdvancedVisemeMouthOwnership.DriveLowerFace &&
                                 (!request.reuseExistingTracking || externalPoseCalibration) &&
                                 !request.profile.visemePoses.Any(
                                     pose => pose != null && pose.animationOverride != null) &&
                                 !request.profile.articulatorBindings.Any(binding =>
                                     binding != null &&
                                     (binding.animationOverride != null ||
                                      binding.negativeAnimationOverride != null));
            switch (control)
            {
                case AdvancedVisemeTuningControl.AuthoredDetail:
                    return residualOutput;
                case AdvancedVisemeTuningControl.Coarticulation:
                    return beta;
                case AdvancedVisemeTuningControl.TrackingSmoothness:
                case AdvancedVisemeTuningControl.TrackingRelease:
                case AdvancedVisemeTuningControl.RemoteTrust:
                    return request.trackingEnabled;
                case AdvancedVisemeTuningControl.ContradictionFade:
                    return request.trackingEnabled && residualOutput;
                case AdvancedVisemeTuningControl.ConstraintAmount:
                case AdvancedVisemeTuningControl.BilabialAssist:
                case AdvancedVisemeTuningControl.LabiodentalAssist:
                case AdvancedVisemeTuningControl.SibilantAssist:
                    return request.trackingEnabled &&
                           request.component.fusionMode ==
                           AdvancedVisemeFusionMode.PhoneticAssist;
                case AdvancedVisemeTuningControl.HiddenPhone:
                    return beta && request.trackingEnabled;
                case AdvancedVisemeTuningControl.HiddenDetail:
                    return beta && request.trackingEnabled && residualOutput &&
                           !string.IsNullOrEmpty(
                               request.calibration.hiddenPhoneResidualBlendShapeName);
                case AdvancedVisemeTuningControl.TongueInference:
                    return beta && request.trackingEnabled;
                default:
                    return true;
            }
        }

        private static SpeechHangoverGraph BuildSpeechHangover(
            MathGraph graph,
            BlendTree root,
            string frameTime,
            string visemeIndex,
            VisemeReconstructionProfile profile,
            string stabilityTuning,
            string publicPrefix,
            Result result)
        {
            // This is deliberately a soft VAD hangover rather than an explicit
            // countdown state machine. The asymmetric one-pole history rises in
            // roughly 60 ms, so sustained speech earns a full hold while a short
            // recognizer blip earns little or none. It then leaks away with the
            // profile response. There is no Voice input: a noisy microphone can
            // never keep an old phone alive after VRChat has stopped reporting it.
            var attackAlpha = graph.Param("Alpha/SpeechHistoryAttack", 0.25f);
            graph.AddOperation(root, graph.AlphaFromDeltaTime(
                frameTime, attackAlpha,
                AdvancedVisemeMath.SpeechHistoryAttackSeconds));

            var configuredReleaseSeconds = Mathf.Clamp(
                profile.speechHangoverSeconds, 0.04f, 0.4f);
            var extendedReleaseSeconds = AdvancedVisemeMath.SpeechHistoryReleaseSeconds(
                configuredReleaseSeconds, 1f);
            var configuredReleaseAlpha = graph.Param(
                "Alpha/SpeechHistoryRelease/Configured", 0.1f);
            var extendedReleaseAlpha = graph.Param(
                "Alpha/SpeechHistoryRelease/Extended", 0.05f);
            graph.AddOperation(root, graph.AlphaFromDeltaTime(
                frameTime, configuredReleaseAlpha, configuredReleaseSeconds));
            graph.AddOperation(root, graph.AlphaFromDeltaTime(
                frameTime, extendedReleaseAlpha, extendedReleaseSeconds));
            var extendedReleaseBlend = graph.Param(
                "Speech/Hangover/ExtendedReleaseBlend", 0f);
            graph.AddOperation(root, graph.Map(
                stabilityTuning, extendedReleaseBlend, new[]
                {
                    Point(0f, 0f), Point(0.5f, 0f), Point(1f, 1f)
                }));
            var releaseAlpha = graph.Param("Alpha/SpeechHistoryRelease", 0.1f);
            graph.AddOperation(root, graph.Interpolate(
                configuredReleaseAlpha, extendedReleaseAlpha,
                releaseAlpha, extendedReleaseBlend, false));

            var history = graph.Param("Speech/Hangover/History", 0f);
            graph.AddOperation(root, graph.AsymmetricBinarySmooth(
                visemeIndex, history,
                0f, releaseAlpha,
                1f, attackAlpha,
                false));

            // Talking is a fast visual envelope, kept high by the same held-sil
            // decision. It must not inherit the 60 ms history attack because this
            // value gates speech gain and would otherwise erase quiet short phones.
            var activityAttack = graph.Param("Alpha/SpeechActivityAttack", 0.8f);
            var activityRelease = graph.Param("Alpha/SpeechActivityRelease", 0.25f);
            graph.AddOperation(root, graph.AlphaFromDeltaTime(
                frameTime, activityAttack,
                AdvancedVisemeMath.SpeechPresenceAttackSeconds));
            graph.AddOperation(root, graph.AlphaFromDeltaTime(
                frameTime, activityRelease,
                AdvancedVisemeMath.SpeechPresenceReleaseSeconds));
            var presence = graph.Param(
                AdvancedVisemeParameterContract.Speech(publicPrefix, "Talking"),
                0f, false);
            graph.AddOperation(root, graph.SmoothActivityWithSilenceHold(
                visemeIndex, history, stabilityTuning,
                presence, activityAttack, activityRelease));
            result.globalParameters.Add(presence);

            return new SpeechHangoverGraph
            {
                history = history,
                presence = presence
            };
        }

        private static string BuildTunableAlpha(
            MathGraph graph,
            BlendTree root,
            string frameTime,
            string key,
            float configuredSeconds,
            string tuning,
            float minimumSeconds,
            float maximumSeconds)
        {
            configuredSeconds = Mathf.Clamp(configuredSeconds, minimumSeconds, maximumSeconds);
            var slowSeconds = Mathf.Clamp(configuredSeconds * 2f, minimumSeconds, maximumSeconds);
            var fastSeconds = Mathf.Clamp(configuredSeconds * 0.5f, minimumSeconds, maximumSeconds);
            var slow = graph.Param(key + "/Slow", 0.25f);
            var configured = graph.Param(key + "/Configured", 0.5f);
            var fast = graph.Param(key + "/Fast", 0.75f);
            graph.AddOperation(root, graph.AlphaFromDeltaTime(frameTime, slow, slowSeconds));
            graph.AddOperation(root, graph.AlphaFromDeltaTime(
                frameTime, configured, configuredSeconds));
            graph.AddOperation(root, graph.AlphaFromDeltaTime(frameTime, fast, fastSeconds));
            return BlendAroundConfigured(
                graph, root, key + "/Tuned", slow, configured, fast, tuning, false);
        }

        private static string BuildTunableVoiceEvidence(
            MathGraph graph,
            BlendTree root,
            VisemeReconstructionProfile profile,
            string tuning)
        {
            var voice = graph.Param("Voice", 0f, false);
            var baseNoise = Mathf.Clamp01(profile.voiceNoiseFloor);
            var baseFull = Mathf.Clamp(profile.voiceFullScale, baseNoise + 0.001f, 1f);
            var lessSensitive = graph.Param("Voice/Evidence/LessSensitive", 0f);
            var configured = graph.Param("Voice/Evidence/Configured", 0f);
            var moreSensitive = graph.Param("Voice/Evidence/MoreSensitive", 0f);
            var lessNoise = Mathf.Clamp(baseNoise * 2f, 0f, 0.98f);
            var lessFull = Mathf.Clamp(baseFull * 2f, lessNoise + 0.001f, 1f);
            var moreNoise = Mathf.Clamp01(baseNoise * 0.5f);
            var moreFull = Mathf.Clamp(baseFull * 0.5f, moreNoise + 0.001f, 1f);
            graph.AddOperation(root, graph.Map(voice, lessSensitive, new[]
            {
                Point(0f, 0f), Point(lessNoise, 0f), Point(lessFull, 1f), Point(1f, 1f)
            }));
            graph.AddOperation(root, graph.Map(voice, configured, new[]
            {
                Point(0f, 0f), Point(baseNoise, 0f), Point(baseFull, 1f), Point(1f, 1f)
            }));
            graph.AddOperation(root, graph.Map(voice, moreSensitive, new[]
            {
                Point(0f, 0f), Point(moreNoise, 0f), Point(moreFull, 1f), Point(1f, 1f)
            }));
            return BlendAroundConfigured(
                graph, root, "Voice/Evidence/Tuned", lessSensitive, configured,
                moreSensitive, tuning, false);
        }

        private static string BlendAroundConfigured(
            MathGraph graph,
            BlendTree root,
            string key,
            string low,
            string configured,
            string high,
            string tuning,
            bool signed)
        {
            var output = graph.Param(key, 0f);
            graph.AddOperation(root, graph.BlendThreeParameters(
                low, configured, high, output, tuning, signed,
                key + " three-point tuning"));
            return output;
        }

        private static void ApplyTongueAxisStrengths(
            MathGraph graph,
            BlendTree root,
            IDictionary<AdvancedVisemeArticulator, string> fast,
            IDictionary<AdvancedVisemeArticulator, string> slow,
            IReadOnlyDictionary<AdvancedVisemeTuningControl, string> tuning)
        {
            var controls = new Dictionary<AdvancedVisemeArticulator, AdvancedVisemeTuningControl>
            {
                { AdvancedVisemeArticulator.TongueOut, AdvancedVisemeTuningControl.TongueOut },
                { AdvancedVisemeArticulator.TongueY, AdvancedVisemeTuningControl.TongueVertical },
                { AdvancedVisemeArticulator.TongueX, AdvancedVisemeTuningControl.TongueLateral },
                { AdvancedVisemeArticulator.TongueRoll, AdvancedVisemeTuningControl.TongueRoll },
                { AdvancedVisemeArticulator.TongueArchY, AdvancedVisemeTuningControl.TongueArch },
                { AdvancedVisemeArticulator.TongueShape, AdvancedVisemeTuningControl.TongueShape },
                { AdvancedVisemeArticulator.TongueTwistRight, AdvancedVisemeTuningControl.TongueTwist },
                { AdvancedVisemeArticulator.TongueTwistLeft, AdvancedVisemeTuningControl.TongueTwist }
            };
            foreach (var pair in controls)
            {
                if (!fast.TryGetValue(pair.Key, out var fastSource) ||
                    !slow.TryGetValue(pair.Key, out var slowSource)) continue;
                var signed = IsSigned(pair.Key);
                var strength = tuning[pair.Value];
                var tunedFast = graph.Param($"Articulation/{pair.Key}/TunedSpeechFast", 0f);
                var tunedSlow = graph.Param($"Articulation/{pair.Key}/TunedSpeechSlow", 0f);
                graph.AddOperation(root, graph.Multiply(
                    strength, fastSource, tunedFast, signed));
                graph.AddOperation(root, graph.Multiply(
                    strength, slowSource, tunedSlow, signed));
                fast[pair.Key] = tunedFast;
                slow[pair.Key] = tunedSlow;
            }
        }

        private static string BuildAdaptiveTrackingPose(
            MathGraph graph,
            BlendTree root,
            AdvancedVisemeArticulator articulator,
            string fast,
            string slow,
            string alphaMotion,
            string localFactor,
            out string motion)
        {
            var signed = IsSigned(articulator);
            var difference = graph.Param($"Tracking/{articulator}/MotionDifference", 0f);
            var magnitude = graph.Param($"Tracking/{articulator}/MotionMagnitude", 0f);
            graph.AddOperation(root, graph.Linear(difference, new[]
            {
                Term.Signed(fast, 1f), Term.Signed(slow, -1f)
            }));
            graph.AddOperation(root, graph.Abs(difference, magnitude));

            var localRaw = graph.Param($"Tracking/{articulator}/LocalMotionRaw", 0f);
            var remoteRaw = graph.Param($"Tracking/{articulator}/RemoteMotionRaw", 0f);
            graph.AddOperation(root, graph.Map(magnitude, localRaw, SmoothStepPoints(
                LocalTrackingMotionDeadband, LocalTrackingMotionFullScale, 0f, 1f)));
            graph.AddOperation(root, graph.Map(magnitude, remoteRaw, SmoothStepPoints(
                RemoteTrackingMotionDeadband, RemoteTrackingMotionFullScale, 0f, 1f)));

            // Smooth the speed selector itself so a quantized value sitting on a
            // threshold cannot alternate between the one- and two-pole paths.
            var localMotion = graph.Param($"Tracking/{articulator}/LocalMotion", 0f);
            var remoteMotion = graph.Param($"Tracking/{articulator}/RemoteMotion", 0f);
            graph.AddOperation(root, graph.Smooth(localRaw, localMotion, alphaMotion, false));
            graph.AddOperation(root, graph.Smooth(remoteRaw, remoteMotion, alphaMotion, false));
            motion = graph.Param($"Tracking/{articulator}/Motion", 0f);
            graph.AddOperation(root, graph.Interpolate(
                remoteMotion, localMotion, motion, localFactor, false));

            var pose = graph.Param($"Tracking/{articulator}/Pose", 0f);
            graph.AddOperation(root, graph.Interpolate(slow, fast, pose, motion, signed));
            return pose;
        }

        private static string BuildTrackingAuthority(
            MathGraph graph,
            BlendTree root,
            AdvancedVisemeArticulator articulator,
            string speech,
            string tracking,
            string localFactor,
            float remoteReliability,
            string remoteTrust)
        {
            remoteReliability = Mathf.Clamp01(remoteReliability);
            var difference = graph.Param($"Tracking/{articulator}/PriorDifference", 0f);
            var magnitude = graph.Param($"Tracking/{articulator}/PriorMismatch", 0f);
            graph.AddOperation(root, graph.Linear(difference, new[]
            {
                Term.Signed(tracking, 1f), Term.Signed(speech, -1f)
            }));
            graph.AddOperation(root, graph.Abs(difference, magnitude));

            // A valid local measurement is the ground truth for its own visible
            // coordinate, even when it happens to agree with the speech prior.
            // Remote measurements keep a conservative mismatch-conditioned
            // reliability to absorb quantization and packet jitter.
            var remoteAuthority = graph.Param(
                $"Tracking/{articulator}/RemoteAuthority", remoteReliability);
            graph.AddOperation(root, graph.Map(magnitude, remoteAuthority, SmoothStepPoints(
                TrackingAuthorityAgreementDeadband * 1.5f,
                TrackingAuthorityDisagreement * 1.5f,
                remoteReliability, 1f)));
            var authority = graph.Param($"Tracking/{articulator}/Reliability", remoteReliability);
            graph.AddOperation(root, graph.SelectMotion(
                localFactor,
                graph.Multiply(remoteAuthority, remoteTrust, authority, false),
                graph.Setter(authority, 1f),
                $"Tracking {articulator} local authority bypass"));
            return authority;
        }

        private static string[] BuildVisibleSpeechWeights(
            MathGraph graph,
            BlendTree root,
            Request request,
            IReadOnlyList<string> speechWeights,
            IReadOnlyDictionary<AdvancedVisemeArticulator, string> trackingGains)
        {
            // A calibrated external basis is reconstructed as U(Cp) + Rp in the
            // authoritative output layer. Its common simplex must never be
            // support-suppressed; only the U coefficients are fused with tracking.
            if (HasExternalPoseCalibration(request)) return speechWeights.ToArray();

            var result = new string[speechWeights.Count];
            var suppressionMaximums = new Dictionary<string, string>(StringComparer.Ordinal);
            var unsuppressedInputs = new List<string>();
            var unsuppressedOutputs = new List<string>();
            var support = new Dictionary<AdvancedVisemeArticulator, float[]>();
            foreach (var articulator in VisibleSpeechArticulators)
            {
                if (!trackingGains.ContainsKey(articulator)) continue;

                // A reused template already owns its measured pose in a lower
                // controller layer. Fresh inputs count only when this generated
                // controller can actually drive the corresponding mesh basis.
                if (!request.reuseExistingTracking &&
                    !HasDriveableOutputPose(request, articulator))
                    continue;
                support[articulator] = GetAuthoredSpeechCoefficients(request, articulator);
            }

            var relevantGainsByViseme = Enumerable.Range(0, speechWeights.Count)
                .Select(viseme => support
                    .Where(pair => Mathf.Abs(pair.Value[viseme]) >= 1e-6f)
                    .Select(pair => trackingGains[pair.Key])
                    .Distinct()
                    .OrderBy(parameter => parameter, StringComparer.Ordinal)
                    .ToArray())
                .ToArray();
            // With no suppressible row, the complete vector can stay at its
            // current depth. The copy stage is needed only for a mixed vector.
            if (relevantGainsByViseme.All(gains => gains.Length == 0))
                return speechWeights.ToArray();

            for (var viseme = 0; viseme < speechWeights.Count; viseme++)
            {
                var relevantGains = relevantGainsByViseme[viseme];

                if (relevantGains.Length == 0)
                {
                    // Keep every visible viseme at the same AAP depth. Aliasing
                    // unsupported rows directly to speechWeights makes a mixed-
                    // frame pose whenever another row is tracking-suppressed.
                    var unsuppressedWeight = graph.Param(
                        $"Viseme/{viseme}/VisibleSpeechWeight", 0f);
                    unsuppressedInputs.Add(speechWeights[viseme]);
                    unsuppressedOutputs.Add(unsuppressedWeight);
                    result[viseme] = unsuppressedWeight;
                    continue;
                }

                string suppression;
                if (relevantGains.Length == 1)
                {
                    suppression = relevantGains[0];
                }
                else
                {
                    var key = string.Join("\u001f", relevantGains);
                    if (!suppressionMaximums.TryGetValue(key, out suppression))
                    {
                        suppression = MaxParameters(
                            graph, root,
                            $"Viseme/{viseme}/VisibleSuppressionMaximum",
                            relevantGains);
                        suppressionMaximums[key] = suppression;
                    }
                }

                var visibleWeight = graph.Param(
                    $"Viseme/{viseme}/VisibleSpeechWeight", 0f);
                graph.AddOperation(root, graph.ScaleByInverseUnitWeight(
                    speechWeights[viseme], suppression, visibleWeight, 1f));
                result[viseme] = visibleWeight;
            }
            if (unsuppressedInputs.Count > 0)
                graph.AddOperation(root, graph.CopyVector(
                    unsuppressedInputs, unsuppressedOutputs,
                    "Unsuppressed visible viseme vector"));
            return result;
        }

        private static (float input, float output)[] SmoothStepPoints(
            float start,
            float end,
            float low,
            float high)
        {
            end = Mathf.Max(start + 0.0001f, end);
            var span = end - start;
            return new[]
            {
                Point(0f, low),
                Point(start, low),
                Point(start + span * 0.25f, Mathf.Lerp(low, high, 0.15625f)),
                Point(start + span * 0.5f, Mathf.Lerp(low, high, 0.5f)),
                Point(start + span * 0.75f, Mathf.Lerp(low, high, 0.84375f)),
                Point(end, high),
                Point(2f, high)
            };
        }

        private static ConstraintConfidenceBases BuildConstraintConfidenceBases(
            MathGraph graph,
            BlendTree root,
            string speechPresence,
            string trackingBlend,
            string localFactor,
            IReadOnlyDictionary<AdvancedVisemeArticulator, string> trackingGains,
            IReadOnlyDictionary<AdvancedVisemeTuningControl, string> tuning)
        {
            var active = graph.Param("Constraint/Shared/ActiveBlend", 0f);
            graph.AddOperation(root, graph.Multiply(
                speechPresence, trackingBlend, active, false));
            return new ConstraintConfidenceBases
            {
                bilabial = BuildConstraintConfidenceBase(
                    graph, root, "Constraint/Shared/PP", active, localFactor,
                    trackingGains, AdvancedVisemeArticulator.LipClose,
                    tuning[AdvancedVisemeTuningControl.ConstraintAmount],
                    tuning[AdvancedVisemeTuningControl.BilabialAssist]),
                labiodental = BuildConstraintConfidenceBase(
                    graph, root, "Constraint/Shared/FF", active, localFactor,
                    trackingGains, AdvancedVisemeArticulator.LipBite,
                    tuning[AdvancedVisemeTuningControl.ConstraintAmount],
                    tuning[AdvancedVisemeTuningControl.LabiodentalAssist]),
                sibilant = BuildConstraintConfidenceBase(
                    graph, root, "Constraint/Shared/Sibilant", active, localFactor,
                    trackingGains, AdvancedVisemeArticulator.JawOpen,
                    tuning[AdvancedVisemeTuningControl.ConstraintAmount],
                    tuning[AdvancedVisemeTuningControl.SibilantAssist])
            };
        }

        private static string BuildConstraintConfidenceBase(
            MathGraph graph,
            BlendTree root,
            string key,
            string active,
            string localFactor,
            IReadOnlyDictionary<AdvancedVisemeArticulator, string> trackingGains,
            AdvancedVisemeArticulator target,
            string globalStrength,
            string channelStrength)
        {
            var strength = graph.Param(key + "/Strength", 0f);
            graph.AddOperation(root, graph.Multiply(
                globalStrength, channelStrength, strength, false));
            var activeStrength = graph.Param(key + "/ActiveStrength", 0f);
            graph.AddOperation(root, graph.Multiply(
                active, strength, activeStrength, false));
            if (!trackingGains.TryGetValue(target, out var gain))
                return activeStrength;

            var localAuthority = graph.Param(key + "/LocalAuthority", 0f);
            graph.AddOperation(root, graph.Multiply(
                localFactor, gain, localAuthority, false));
            var output = graph.Param(key + "/ConfidenceBase", 0f);
            graph.AddOperation(root, graph.ScaleByInverseUnitWeight(
                activeStrength, localAuthority, output, 1f));
            return output;
        }

        private static void ApplyConstraints(
            MathGraph graph,
            BlendTree root,
            VisemeReconstructionProfile profile,
            string[] visemes,
            ConstraintConfidenceBases bases,
            IDictionary<AdvancedVisemeArticulator, string> articulation,
            string stage)
        {
            var constraintRoot = "Constraint/" + stage;
            if (articulation.TryGetValue(AdvancedVisemeArticulator.LipClose, out var lipClose))
            {
                var confidence = graph.Param(constraintRoot + "/PPConfidence", 0f);
                graph.AddOperation(root, graph.Multiply(
                    bases.bilabial, visemes[1], confidence, false));
                articulation[AdvancedVisemeArticulator.LipClose] = SmoothFloorProjection(
                    graph, root, constraintRoot + "/PPClosure", lipClose,
                    profile.bilabialClosure, confidence);
            }
            if (articulation.TryGetValue(AdvancedVisemeArticulator.LipBite, out var lipBite))
            {
                var confidence = graph.Param(constraintRoot + "/FFConfidence", 0f);
                graph.AddOperation(root, graph.Multiply(
                    bases.labiodental, visemes[2], confidence, false));
                articulation[AdvancedVisemeArticulator.LipBite] = SmoothFloorProjection(
                    graph, root, constraintRoot + "/FFBite", lipBite,
                    profile.labiodentalBite, confidence);
            }
            if (articulation.TryGetValue(AdvancedVisemeArticulator.JawOpen, out var jaw))
            {
                var sibilant = graph.Param(constraintRoot + "/Sibilant", 0f);
                graph.AddOperation(root, graph.Linear(sibilant, new[]
                {
                    Term.Positive(visemes[6], 1f), Term.Positive(visemes[7], 1f)
                }));
                var confidence = graph.Param(constraintRoot + "/SibilantConfidence", 0f);
                graph.AddOperation(root, graph.Multiply(
                    bases.sibilant, sibilant, confidence, false));
                articulation[AdvancedVisemeArticulator.JawOpen] = SmoothCeilingProjection(
                    graph, root, constraintRoot + "/SibilantJaw", jaw,
                    profile.sibilantJawMaximum, confidence);
            }
        }

        private static string SmoothFloorProjection(
            MathGraph graph,
            BlendTree root,
            string key,
            string value,
            float floor,
            string confidence)
        {
            floor = Mathf.Clamp01(floor);
            var target = graph.Param(key + "/Target", floor);
            graph.AddOperation(root, graph.Map(
                value, target, MonotoneProjectionPoints(floor, ConstraintProjectionWidth, true)));
            var output = graph.Param(key + "/Projected", 0f);
            graph.AddOperation(root, graph.Interpolate(value, target, output, confidence, false));
            return output;
        }

        private static string SmoothCeilingProjection(
            MathGraph graph,
            BlendTree root,
            string key,
            string value,
            float ceiling,
            string confidence)
        {
            ceiling = Mathf.Clamp01(ceiling);
            var target = graph.Param(key + "/Target", ceiling);
            graph.AddOperation(root, graph.Map(
                value, target, MonotoneProjectionPoints(ceiling, ConstraintProjectionWidth, false)));
            var output = graph.Param(key + "/Projected", 0f);
            graph.AddOperation(root, graph.Interpolate(value, target, output, confidence, false));
            return output;
        }

        private static (float input, float output)[] MonotoneProjectionPoints(
            float boundary,
            float width,
            bool floor)
        {
            width = Mathf.Max(0.0001f, width);
            var samples = new List<float> { -1f, 0f, 1f, 2f };
            for (var i = 0; i <= 8; i++)
            {
                samples.Add(boundary - width + 2f * width * i / 8f);
            }
            samples.Sort();

            var points = new List<(float input, float output)>();
            foreach (var sample in samples)
            {
                if (points.Count > 0 && Mathf.Abs(points[points.Count - 1].input - sample) < 1e-6f)
                    continue;
                var output = floor
                    ? AdvancedVisemeMath.SmoothFloorProjection(sample, boundary, 1f, width)
                    : AdvancedVisemeMath.SmoothCeilingProjection(sample, boundary, 1f, width);
                points.Add(Point(sample, output));
            }
            return points.ToArray();
        }

        private static void ApplyMouthEnvelope(
            MathGraph graph,
            BlendTree root,
            string[] visemes,
            IDictionary<AdvancedVisemeArticulator, string> articulation)
        {
            if (articulation.TryGetValue(AdvancedVisemeArticulator.LipClose, out var lipClose) &&
                articulation.TryGetValue(AdvancedVisemeArticulator.MouthOpen, out var mouthOpen))
            {
                var maximum = graph.Param("Envelope/MouthOpenMaximum", 1f);
                graph.AddOperation(root, graph.Linear(maximum, new[]
                {
                    Term.Constant(1f), Term.Positive(lipClose, -1f)
                }));
                var constrained = graph.Param("Envelope/MouthOpen", 0f);
                graph.AddOperation(root, graph.Min(mouthOpen, maximum, constrained));
                articulation[AdvancedVisemeArticulator.MouthOpen] = constrained;
            }

            if (articulation.TryGetValue(AdvancedVisemeArticulator.LipSuck, out var lipSuck) &&
                articulation.TryGetValue(AdvancedVisemeArticulator.LipPucker, out var lipPucker))
            {
                var maximum = graph.Param("Envelope/LipPuckerMaximum", 1f);
                graph.AddOperation(root, graph.Linear(maximum, new[]
                {
                    Term.Constant(1f), Term.Positive(lipSuck, -1f)
                }));
                var constrained = graph.Param("Envelope/LipPucker", 0f);
                graph.AddOperation(root, graph.Min(lipPucker, maximum, constrained));
                articulation[AdvancedVisemeArticulator.LipPucker] = constrained;
            }

            if (articulation.TryGetValue(AdvancedVisemeArticulator.TongueOut, out var tongueOut))
            {
                var apertureRaw = graph.Param("Envelope/TongueApertureRaw", 0f);
                var aperture = graph.Param("Envelope/TongueAperture", 0f);
                var apertureTerms = new List<Term> { Term.Positive(visemes[3], 0.6f) };
                if (articulation.TryGetValue(AdvancedVisemeArticulator.JawOpen, out var jaw))
                    apertureTerms.Add(Term.Positive(jaw, 1f));
                if (articulation.TryGetValue(AdvancedVisemeArticulator.MouthOpen, out var mouth))
                    apertureTerms.Add(Term.Positive(mouth, 1f));
                graph.AddOperation(root, graph.Linear(apertureRaw, apertureTerms));
                graph.AddOperation(root, graph.Map(apertureRaw, aperture, new[]
                {
                    Point(0f, 0f), Point(1f, 1f), Point(2f, 1f)
                }));
                var maximum = graph.Param("Envelope/TongueOutMaximum", 0.08f);
                graph.AddOperation(root, graph.Linear(maximum, new[]
                {
                    Term.Constant(0.08f), Term.Positive(aperture, 0.92f)
                }));
                var constrained = graph.Param("Envelope/TongueOut", 0f);
                graph.AddOperation(root, graph.Min(tongueOut, maximum, constrained));
                articulation[AdvancedVisemeArticulator.TongueOut] = constrained;
            }
        }

        private static void BuildSpeechEvidence(
            MathGraph graph,
            BlendTree root,
            string prefix,
            string[] visemes,
            string energy,
            Result result)
        {
            PublishEvidence(graph, root,
                AdvancedVisemeParameterContract.Speech(prefix, "Bilabial"), result,
                new[] { Term.Positive(visemes[1], 1f) });
            PublishEvidence(graph, root,
                AdvancedVisemeParameterContract.Speech(prefix, "Labiodental"), result,
                new[] { Term.Positive(visemes[2], 1f) });
            PublishEvidence(graph, root,
                AdvancedVisemeParameterContract.Speech(prefix, "Sibilant"), result,
                new[] { Term.Positive(visemes[6], 1f), Term.Positive(visemes[7], 1f) });
            PublishEvidence(graph, root,
                AdvancedVisemeParameterContract.Speech(prefix, "Coronal"), result,
                new[]
                {
                    Term.Positive(visemes[3], 1f), Term.Positive(visemes[4], 1f),
                    Term.Positive(visemes[6], 1f), Term.Positive(visemes[7], 1f),
                    Term.Positive(visemes[8], 1f)
                });
            PublishEvidence(graph, root,
                AdvancedVisemeParameterContract.Speech(prefix, "Dorsal"), result,
                new[] { Term.Positive(visemes[5], 1f) });
            PublishEvidence(graph, root,
                AdvancedVisemeParameterContract.Speech(prefix, "Rhotic"), result,
                new[] { Term.Positive(visemes[9], 1f) });

            var lipClose = result.articulationParameters.TryGetValue(
                AdvancedVisemeArticulator.LipClose, out var closeParameter)
                ? closeParameter
                : graph.Param("Evidence/LipCloseFallback", 0f);
            var tongueContact = BuildTongueContact(graph, root, visemes, result);
            var tongueContactOutput = graph.Param(
                AdvancedVisemeParameterContract.Speech(prefix, "TongueContact"),
                0f, false);
            graph.AddOperation(root, graph.Copy(tongueContact, tongueContactOutput, false));
            result.globalParameters.Add(tongueContactOutput);

            var mSupport = graph.Param("Evidence/MSupport", 0.6f);
            graph.AddOperation(root, graph.Linear(mSupport, new[]
            {
                Term.Constant(0.6f), Term.Positive(lipClose, 0.4f)
            }));
            var mClosure = graph.Param("Evidence/MClosure", 0f);
            graph.AddOperation(root, graph.Multiply(visemes[1], mSupport, mClosure, false));
            var mOutput = graph.Param(
                AdvancedVisemeParameterContract.Speech(prefix, "M"), 0f, false);
            graph.AddOperation(root, graph.Multiply(energy, mClosure, mOutput, false));
            result.globalParameters.Add(mOutput);

            var nSupport = graph.Param("Evidence/NSupport", 0.6f);
            graph.AddOperation(root, graph.Linear(nSupport, new[]
            {
                Term.Constant(0.6f), Term.Positive(tongueContact, 0.4f)
            }));
            var nContact = graph.Param("Evidence/NContact", 0f);
            graph.AddOperation(root, graph.Multiply(visemes[8], nSupport, nContact, false));
            var nOutput = graph.Param(
                AdvancedVisemeParameterContract.Speech(prefix, "N"), 0f, false);
            // nn is the merged n/l class. Its visible lips are observational:
            // a speaker may produce it with closed lips, so closure cannot veto
            // otherwise valid tongue-contact evidence.
            graph.AddOperation(root, graph.Multiply(energy, nContact, nOutput, false));
            result.globalParameters.Add(nOutput);
        }

        private static string BuildTongueContact(
            MathGraph graph,
            BlendTree root,
            string[] visemes,
            Result result)
        {
            var candidates = new List<string>();
            foreach (var articulator in new[]
                     {
                         AdvancedVisemeArticulator.TongueY,
                         AdvancedVisemeArticulator.TongueArchY
                     })
            {
                if (!result.articulationParameters.TryGetValue(articulator, out var parameter)) continue;
                var positive = graph.Param("Evidence/" + articulator + "Positive", 0f);
                graph.AddOperation(root, graph.Map(parameter, positive, new[]
                {
                    Point(-1f, 0f), Point(0f, 0f), Point(1f, 1f)
                }));
                candidates.Add(positive);
            }

            var baseContact = graph.Param("Evidence/TongueContactBase", 0f);
            if (candidates.Count == 0)
            {
                graph.AddOperation(root, graph.Copy(visemes[8], baseContact, false));
            }
            else if (candidates.Count == 1)
            {
                graph.AddOperation(root, graph.Copy(candidates[0], baseContact, false));
            }
            else
            {
                graph.AddOperation(root, graph.Max(candidates[0], candidates[1], baseContact));
            }

            if (!result.articulationParameters.TryGetValue(
                    AdvancedVisemeArticulator.TongueOut, out var tongueOut)) return baseContact;
            var notOut = graph.Param("Evidence/TongueNotOut", 1f);
            graph.AddOperation(root, graph.Linear(notOut, new[]
            {
                Term.Constant(1f), Term.Positive(tongueOut, -1f)
            }));
            var contact = graph.Param("Evidence/TongueContact", 0f);
            graph.AddOperation(root, graph.Multiply(notOut, baseContact, contact, false));
            return contact;
        }

        private static void PublishEvidence(
            MathGraph graph,
            BlendTree root,
            string output,
            Result result,
            IEnumerable<Term> terms)
        {
            var parameter = graph.Param(output, 0f, false);
            var evidenceTerms = terms.ToArray();
            if (evidenceTerms.Length == 1 &&
                !evidenceTerms[0].constant &&
                !evidenceTerms[0].signed &&
                Mathf.Approximately(evidenceTerms[0].multiplier, 1f))
                graph.AddOperation(root,
                    graph.Copy(evidenceTerms[0].parameter, parameter, false));
            else
                graph.AddOperation(root, graph.Linear(parameter, evidenceTerms));
            result.globalParameters.Add(parameter);
        }

        private static bool RequiresNativeTongueCapability(AdvancedVisemeArticulator articulator)
        {
            return articulator == AdvancedVisemeArticulator.TongueOut ||
                   articulator == AdvancedVisemeArticulator.TongueY ||
                   articulator == AdvancedVisemeArticulator.TongueX ||
                   articulator == AdvancedVisemeArticulator.TongueRoll ||
                   articulator == AdvancedVisemeArticulator.TongueArchY ||
                   articulator == AdvancedVisemeArticulator.TongueShape ||
                   articulator == AdvancedVisemeArticulator.TongueTwistRight ||
                   articulator == AdvancedVisemeArticulator.TongueTwistLeft;
        }

        private static Dictionary<AdvancedVisemeArticulator, string> BuildNativeTongueCapabilities(
            MathGraph graph,
            BlendTree root,
            Request request,
            string frameTime,
            string trackingActivity,
            IReadOnlyDictionary<AdvancedVisemeArticulator, string> rawTracking)
        {
            var capabilities = new Dictionary<AdvancedVisemeArticulator, string>();
            // An explicitly generated FullTongue18 input set is a user declaration
            // of capability. Auto/reused templates merely declaring tongue
            // parameters are not evidence that the current hardware populates them.
            var explicitCapability = !request.reuseExistingTracking &&
                                     request.component.trackingInputs ==
                                     AdvancedVisemeTrackingInputs.FullTongue18;
            foreach (var pair in rawTracking)
            {
                var articulator = pair.Key;
                if (!RequiresNativeTongueCapability(articulator)) continue;
                if (explicitCapability)
                {
                    capabilities[articulator] = MathGraph.AlwaysOneParameter;
                    continue;
                }

                var parameter = pair.Value;
                var magnitude = graph.Param($"Tracking/{articulator}/NativeEvidenceMagnitude", 0f);
                graph.AddOperation(root, graph.Abs(parameter, magnitude));
                var observed = graph.Param($"Tracking/{articulator}/NativeCapabilityObserved", 0f);
                graph.AddOperation(root, graph.Map(magnitude, observed, new[]
                {
                    Point(0f, 0f), Point(NativeTongueCapabilityNoiseFloor, 0f),
                    Point(NativeTongueCapabilityThreshold, 1f), Point(1f, 1f)
                }));
                var activeObserved = graph.Param(
                    $"Tracking/{articulator}/NativeCapabilityActiveObserved", 0f);
                graph.AddOperation(root, graph.Multiply(
                    trackingActivity, observed, activeObserved, false));
                // Capability is channel-specific: TongueOut hardware must not
                // erase a learned TongueY (or vice versa). Require sustained
                // evidence before latching so one noisy OSC packet is harmless.
                var alphaEvidence = graph.Param($"Tracking/{articulator}/NativeEvidenceAlpha", 0.1f);
                graph.AddOperation(root, graph.AlphaFromDeltaTime(
                    frameTime, alphaEvidence, 0.12f));
                var accumulated = graph.Param($"Tracking/{articulator}/NativeEvidenceAccumulated", 0f);
                graph.AddOperation(root, graph.Smooth(
                    activeObserved, accumulated, alphaEvidence, false));
                var confirmed = graph.Param($"Tracking/{articulator}/NativeCapabilityConfirmed", 0f);
                graph.AddOperation(root, graph.Map(accumulated, confirmed, new[]
                {
                    Point(0f, 0f), Point(0.78f, 0f), Point(0.8f, 1f), Point(1f, 1f)
                }));
                var capability = graph.Param($"Tracking/{articulator}/NativeCapability", 0f);
                var latched = graph.Param($"Tracking/{articulator}/NativeCapabilityLatched", 0f);
                graph.AddOperation(root, graph.Max(capability, confirmed, latched));
                graph.AddOperation(root, graph.Copy(latched, capability, false));
                capabilities[articulator] = capability;
            }
            return capabilities;
        }

        internal static float StepNativeTongueCapability(
            float previousCapability,
            float tongueOut,
            float tongueY)
        {
            previousCapability = Mathf.Clamp01(previousCapability);
            var magnitude = Mathf.Max(Mathf.Abs(tongueOut), Mathf.Abs(tongueY));
            var observed = Mathf.InverseLerp(
                NativeTongueCapabilityNoiseFloor,
                NativeTongueCapabilityThreshold,
                magnitude);
            return Mathf.Max(previousCapability, observed);
        }

        internal static bool UsesPhoneticTrackingScale(AdvancedVisemeFusionMode mode)
        {
            // Retaining a percentage of a full authored vowel pose is additive
            // overshoot, not complementary fusion. PhoneticAssist now differs only
            // by its sparse PP/FF/sibilant projections.
            return false;
        }

        internal static bool UsesVowelIdentityRetention(AdvancedVisemeArticulator articulator)
        {
            // A measured funnel or pucker is already the visible vowel identity.
            // Hidden tongue-body channels retain the speech prior instead.
            return false;
        }

        internal static bool CanBuildFaceConditionedTongueInference(Request request)
        {
            if (request == null || !request.trackingEnabled || request.profile == null)
                return false;

            var available = new HashSet<AdvancedVisemeArticulator>(
                TrackedArticulators(request.effectiveTrackingInputs));
            var required = new[]
            {
                AdvancedVisemeArticulator.JawOpen,
                AdvancedVisemeArticulator.LipClose,
                AdvancedVisemeArticulator.MouthOpen
            };
            foreach (var articulator in required)
            {
                if (!available.Contains(articulator)) return false;
                var binding = request.profile.FindBinding(articulator);
                if (!TryResolveTrackingParameter(
                        request, articulator, binding, out _)) return false;
            }
            return true;
        }

        private static FacePhonePosteriorGraph ApplyBetaTongueInference(
            MathGraph graph,
            BlendTree root,
            Request request,
            Result result,
            BetaCoarticulationGraph betaGraph,
            string frameTime,
            string speechPresence,
            string speechGain,
            IDictionary<AdvancedVisemeArticulator, string> speechFast,
            IDictionary<AdvancedVisemeArticulator, string> speechSlow,
            IReadOnlyDictionary<AdvancedVisemeArticulator, string> speechCenters,
            IReadOnlyDictionary<AdvancedVisemeArticulator, string> calibratedTrackingRaw,
            IReadOnlyDictionary<AdvancedVisemeArticulator, string> trackingGains,
            IReadOnlyDictionary<AdvancedVisemeTuningControl, string> tuning)
        {
            var apertureRequired = new[]
            {
                AdvancedVisemeArticulator.JawOpen,
                AdvancedVisemeArticulator.LipClose,
                AdvancedVisemeArticulator.MouthOpen
            };
            if (apertureRequired.Any(articulator =>
                    !calibratedTrackingRaw.ContainsKey(articulator) ||
                    !trackingGains.ContainsKey(articulator) ||
                    !speechCenters.ContainsKey(articulator)))
                return null;
            if (!speechSlow.ContainsKey(AdvancedVisemeArticulator.TongueOut) ||
                !speechSlow.ContainsKey(AdvancedVisemeArticulator.TongueY))
                return null;

            var protrusionRequired = new[]
            {
                AdvancedVisemeArticulator.LipFunnel,
                AdvancedVisemeArticulator.LipPucker,
                AdvancedVisemeArticulator.LipSuck
            };
            var hasProtrusion = protrusionRequired.All(articulator =>
                calibratedTrackingRaw.ContainsKey(articulator) &&
                trackingGains.ContainsKey(articulator) &&
                speechCenters.ContainsKey(articulator));

            var quality = hasProtrusion &&
                          calibratedTrackingRaw.ContainsKey(AdvancedVisemeArticulator.JawZ) &&
                          trackingGains.ContainsKey(AdvancedVisemeArticulator.JawZ) &&
                          speechCenters.ContainsKey(AdvancedVisemeArticulator.JawZ);
            AdvancedVisemeVisibleTongueModelKind? tongueKind = hasProtrusion
                ? quality
                    ? AdvancedVisemeVisibleTongueModelKind.Quality
                    : AdvancedVisemeVisibleTongueModelKind.Balanced
                : (AdvancedVisemeVisibleTongueModelKind?)null;
            var phoneKind = quality
                ? AdvancedVisemeHiddenPhoneModelKind.Quality
                : hasProtrusion
                    ? AdvancedVisemeHiddenPhoneModelKind.Balanced
                    : AdvancedVisemeHiddenPhoneModelKind.Aperture;

            var current = new Dictionary<AdvancedVisemeVisibleFeatureChannel, string>();
            var center = new Dictionary<AdvancedVisemeVisibleFeatureChannel, string>();
            var featureGain = new Dictionary<AdvancedVisemeVisibleFeatureChannel, string>();

            current[AdvancedVisemeVisibleFeatureChannel.JawOpen] =
                calibratedTrackingRaw[AdvancedVisemeArticulator.JawOpen];
            center[AdvancedVisemeVisibleFeatureChannel.JawOpen] =
                speechCenters[AdvancedVisemeArticulator.JawOpen];
            featureGain[AdvancedVisemeVisibleFeatureChannel.JawOpen] =
                trackingGains[AdvancedVisemeArticulator.JawOpen];

            if (quality)
            {
                current[AdvancedVisemeVisibleFeatureChannel.JawAdvance] = BuildSignedUnitValue(
                    graph, root, calibratedTrackingRaw[AdvancedVisemeArticulator.JawZ],
                    "TongueInference/Visible/JawAdvance/Tracked");
                center[AdvancedVisemeVisibleFeatureChannel.JawAdvance] = BuildSignedUnitValue(
                    graph, root, speechCenters[AdvancedVisemeArticulator.JawZ],
                    "TongueInference/Visible/JawAdvance/Speech");
                featureGain[AdvancedVisemeVisibleFeatureChannel.JawAdvance] =
                    trackingGains[AdvancedVisemeArticulator.JawZ];
            }

            current[AdvancedVisemeVisibleFeatureChannel.LipAperture] = BuildOpposedUnitValue(
                graph, root,
                calibratedTrackingRaw[AdvancedVisemeArticulator.MouthOpen],
                calibratedTrackingRaw[AdvancedVisemeArticulator.LipClose],
                "TongueInference/Visible/LipAperture/Tracked");
            center[AdvancedVisemeVisibleFeatureChannel.LipAperture] = BuildOpposedUnitValue(
                graph, root,
                speechCenters[AdvancedVisemeArticulator.MouthOpen],
                speechCenters[AdvancedVisemeArticulator.LipClose],
                "TongueInference/Visible/LipAperture/Speech");
            featureGain[AdvancedVisemeVisibleFeatureChannel.LipAperture] = MinParameters(
                graph, root, "TongueInference/Gain/LipAperture",
                trackingGains[AdvancedVisemeArticulator.MouthOpen],
                trackingGains[AdvancedVisemeArticulator.LipClose]);

            if (hasProtrusion)
            {
                current[AdvancedVisemeVisibleFeatureChannel.LipProtrusion] = BuildProtrusionValue(
                    graph, root,
                    calibratedTrackingRaw[AdvancedVisemeArticulator.LipFunnel],
                    calibratedTrackingRaw[AdvancedVisemeArticulator.LipPucker],
                    calibratedTrackingRaw[AdvancedVisemeArticulator.LipSuck],
                    "TongueInference/Visible/LipProtrusion/Tracked");
                center[AdvancedVisemeVisibleFeatureChannel.LipProtrusion] = BuildProtrusionValue(
                    graph, root,
                    speechCenters[AdvancedVisemeArticulator.LipFunnel],
                    speechCenters[AdvancedVisemeArticulator.LipPucker],
                    speechCenters[AdvancedVisemeArticulator.LipSuck],
                    "TongueInference/Visible/LipProtrusion/Speech");
                var protrusionPositiveGain = MaxParameters(
                    graph, root, "TongueInference/Gain/LipProtrusionPositive",
                    trackingGains[AdvancedVisemeArticulator.LipFunnel],
                    trackingGains[AdvancedVisemeArticulator.LipPucker]);
                featureGain[AdvancedVisemeVisibleFeatureChannel.LipProtrusion] = MinParameters(
                    graph, root, "TongueInference/Gain/LipProtrusion",
                    protrusionPositiveGain,
                    trackingGains[AdvancedVisemeArticulator.LipSuck]);
            }

            var alpha = graph.Param("TongueInference/Observer/Alpha", 0.5f);
            graph.AddOperation(root, graph.AlphaFromDeltaTime(
                frameTime, alpha, AdvancedVisemeHiddenPhonePosterior.ObserverResponseSeconds));
            var featureChannels = quality
                ? new[]
                {
                    AdvancedVisemeVisibleFeatureChannel.JawOpen,
                    AdvancedVisemeVisibleFeatureChannel.JawAdvance,
                    AdvancedVisemeVisibleFeatureChannel.LipAperture,
                    AdvancedVisemeVisibleFeatureChannel.LipProtrusion
                }
                : hasProtrusion
                    ? new[]
                    {
                        AdvancedVisemeVisibleFeatureChannel.JawOpen,
                        AdvancedVisemeVisibleFeatureChannel.LipAperture,
                        AdvancedVisemeVisibleFeatureChannel.LipProtrusion
                    }
                    : new[]
                    {
                        AdvancedVisemeVisibleFeatureChannel.JawOpen,
                        AdvancedVisemeVisibleFeatureChannel.LipAperture
                    };
            var featureParameters = new string[
                AdvancedVisemeHiddenPhonePosterior.FeatureCount(phoneKind)];
            var measurementOodFactors = new List<string>();
            var tongueOodFactors = new List<string>();
            var gainFactors = new List<string>();
            for (var channelIndex = 0; channelIndex < featureChannels.Length; channelIndex++)
            {
                var channel = featureChannels[channelIndex];

                var residual = BuildHeadroomNormalizedResidual(
                    graph, root, current[channel], center[channel],
                    "TongueInference/Feature/" + channel, out var ood);
                var fast = graph.Param($"TongueInference/Feature/{channel}/Fast", 0f);
                var slow = graph.Param($"TongueInference/Feature/{channel}/Slow", 0f);
                graph.AddOperation(root, graph.Smooth(residual, fast, alpha, true));
                graph.AddOperation(root, graph.Smooth(fast, slow, alpha, true));
                var currentMinusFast = graph.Param(
                    $"TongueInference/Feature/{channel}/CurrentMinusFast", 0f);
                var fastMinusSlow = graph.Param(
                    $"TongueInference/Feature/{channel}/FastMinusSlow", 0f);
                graph.AddOperation(root, graph.Linear(currentMinusFast, new[]
                {
                    Term.Signed(residual, 1f), Term.Signed(fast, -1f)
                }));
                graph.AddOperation(root, graph.Linear(fastMinusSlow, new[]
                {
                    Term.Signed(fast, 1f), Term.Signed(slow, -1f)
                }));
                featureParameters[channelIndex] = residual;
                featureParameters[featureChannels.Length + channelIndex] =
                    currentMinusFast;
                featureParameters[2 * featureChannels.Length + channelIndex] =
                    fastMinusSlow;
                measurementOodFactors.Add(ood);
                if (tongueKind.HasValue)
                {
                    var tongueChannelIndex = AdvancedVisemeVisibleTongueResidual.FeatureChannelIndex(
                        tongueKind.Value, channel);
                    for (var stage = 0;
                         stage < AdvancedVisemeVisibleTongueResidual.FeatureStageCount;
                         stage++)
                    {
                        var featureIndex = stage * featureChannels.Length + channelIndex;
                        var tongueFeatureIndex = stage *
                            AdvancedVisemeVisibleTongueResidual.FeatureChannelCount(
                                tongueKind.Value) + tongueChannelIndex;
                        tongueOodFactors.Add(BuildEmpiricalFeatureSupport(
                            graph, root, featureParameters[featureIndex],
                            tongueKind.Value, tongueFeatureIndex,
                            $"TongueInference/Feature/{channel}/Stage{stage}/Support"));
                    }
                }
                gainFactors.Add(featureGain[channel]);
            }
            if (featureParameters.Any(string.IsNullOrEmpty)) return null;
            tongueOodFactors.AddRange(measurementOodFactors);

            var visibleGain = MinParameters(
                graph, root, "TongueInference/VisibleGain", gainFactors.ToArray());
            FacePhonePosteriorGraph phonePosterior = null;
            if (AdvancedVisemeHiddenPhonePosterior.FeatureCount(phoneKind) == featureParameters.Length)
            {
                // The hidden-phone classifier is trained around the exact Beta
                // group-center trajectory. Its support envelope is therefore
                // separate from the older tongue-residual estimator's hard-center
                // envelope; sharing that gate can silently reject valid evidence.
                var phoneSupport = new List<string>(measurementOodFactors);
                phoneSupport.AddRange(featureParameters.Select((parameter, featureIndex) =>
                    BuildEmpiricalFeatureSupport(
                        graph, root, parameter,
                        AdvancedVisemeHiddenPhonePosterior.FeatureAbsP995(
                            phoneKind, featureIndex),
                        AdvancedVisemeHiddenPhonePosterior.FeatureSafeBound(
                            phoneKind, featureIndex),
                        $"PhonePosterior/Feature/{featureIndex}/Support")));
                var phoneOodConfidence = BuildSmoothedSupportConfidence(
                    graph, root, "PhonePosterior/OodConfidence", phoneSupport, alpha);
                var phoneTrackingConfidence = graph.Param(
                    "PhonePosterior/Confidence/Tracking", 0f);
                var phoneActivityConfidence = graph.Param(
                    "PhonePosterior/Confidence/Activity", 0f);
                graph.AddOperation(root, graph.Multiply(
                    visibleGain, phoneOodConfidence, phoneTrackingConfidence, false));
                graph.AddOperation(root, graph.Multiply(
                    phoneTrackingConfidence, speechPresence, phoneActivityConfidence, false));
                var compatibleConfidence = graph.Param(
                    "PhonePosterior/Confidence/ModelCompatibility", 0f);
                graph.AddOperation(root, graph.Linear(compatibleConfidence, new[]
                {
                    Term.Positive(phoneActivityConfidence,
                        HiddenPhoneObserverCompatibility(
                            request.profile.visemeResponseSeconds))
                }));
                var coarticulatedConfidence = graph.Param(
                    "PhonePosterior/Confidence/Coarticulation", 0f);
                graph.AddOperation(root, graph.Multiply(
                    compatibleConfidence,
                    tuning[AdvancedVisemeTuningControl.Coarticulation],
                    coarticulatedConfidence, false));
                var phoneConfidence = graph.Param(
                    "PhonePosterior/Confidence/Tuned", 0f);
                graph.AddOperation(root, graph.Multiply(
                    coarticulatedConfidence,
                    tuning[AdvancedVisemeTuningControl.HiddenPhone],
                    phoneConfidence, false));
                phonePosterior = BuildFacePhonePosterior(
                    graph, root, request, result, betaGraph, phoneKind,
                    featureParameters, phoneConfidence, frameTime);
                RebuildConditionedTongueSpeech(
                    graph, root, request, phonePosterior, speechGain,
                    speechFast, speechSlow);
            }

            // Aperture-only tailored templates can still correct hidden M/N/L
            // tongue priors. The separate visible-to-tongue residual regressor
            // needs protrusion, so stop here only after applying that correction.
            if (!tongueKind.HasValue) return phonePosterior;
            var kind = tongueKind.Value;

            // The residual regressor's empirical envelope is from EMA rather
            // than paired UE captures. Leaving it is a conservative, smoothly
            // reversible abstention instead of a clamp or tracker failure.
            var oodConfidence = BuildSmoothedSupportConfidence(
                graph, root, "TongueInference/OodConfidence", tongueOodFactors, alpha);
            var confidenceTracking = graph.Param("TongueInference/Confidence/Tracking", 0f);
            var confidenceSpeech = graph.Param("TongueInference/Confidence/Speech", 0f);
            graph.AddOperation(root, graph.Multiply(
                visibleGain, oodConfidence, confidenceTracking, false));
            // Speech amplitude is applied exactly once to final articulation.
            // Posterior authority follows activity, so quiet tracked speech is
            // not weakened a second time.
            graph.AddOperation(root, graph.Multiply(
                confidenceTracking, speechPresence, confidenceSpeech, false));
            // Each visible-channel gain already contains its channel-specific
            // tracking blend; applying it again would square confidence.
            var betaTongueConfidence = graph.Param(
                "TongueInference/Confidence/Coarticulation", 0f);
            graph.AddOperation(root, graph.Multiply(
                confidenceSpeech,
                tuning[AdvancedVisemeTuningControl.Coarticulation],
                betaTongueConfidence, false));
            var tongueConfidence = graph.Param(
                "TongueInference/Confidence/Tuned", 0f);
            graph.AddOperation(root, graph.Multiply(
                betaTongueConfidence,
                tuning[AdvancedVisemeTuningControl.TongueInference],
                tongueConfidence, false));

            var latent = new string[AdvancedVisemeVisibleTongueResidual.LatentCount(kind)];
            var latentScales = new float[latent.Length];
            for (var latentIndex = 0; latentIndex < latent.Length; latentIndex++)
            {
                var latentScale = Mathf.Max(1e-6f,
                    AdvancedVisemeVisibleTongueResidual.VisibleLatentSafeBound(kind, latentIndex));
                latentScales[latentIndex] = latentScale;
                latent[latentIndex] = graph.Param(
                    $"TongueInference/Model/Visible/{latentIndex}", 0f);
            }
            graph.AddOperation(root, graph.SignedMatrixProjection(
                featureParameters,
                latent,
                new float[latent.Length],
                (featureIndex, latentIndex) =>
                    AdvancedVisemeVisibleTongueResidual.InputProjection(
                        kind, featureIndex, latentIndex) /
                    latentScales[latentIndex],
                "Tongue inference visible latent contraction"));

            var visemeWeights =
                betaGraph.groups[AdvancedVisemeArticulatorGroup.TongueTip].slow;
            var visemeRankOneDelta = phonePosterior != null &&
                                     phonePosterior.corrections.TryGetValue(
                                         AdvancedVisemeArticulatorGroup.TongueTip,
                                         out var tongueTipCorrection)
                ? tongueTipCorrection.slow
                : null;
            var reliability = graph.Param("TongueInference/Model/Reliability", 0f);
            var modelOutputs = Enum.GetValues(typeof(AdvancedVisemeVisibleTongueOutput))
                .Cast<AdvancedVisemeVisibleTongueOutput>()
                .ToArray();
            var outputScales = modelOutputs.ToDictionary(
                output => output,
                output => Mathf.Max(1e-6f,
                    AdvancedVisemeVisibleTongueResidual.ConservativeOutputBound(kind, output)));
            var contractedBase = modelOutputs.ToDictionary(
                output => output,
                output => graph.Param($"TongueInference/Model/{output}/ContractedBase", 0f));
            var contractedMix = new string[latent.Length, modelOutputs.Length];
            var contractedMixMinimum = new float[latent.Length, modelOutputs.Length];
            var contractedMixRange = new float[latent.Length, modelOutputs.Length];
            // For each latent/output ray, affine-shift the 15 viseme coefficients
            // into [0,1]. A simplex-weighted mixture stays in that interval and is
            // therefore a legal Direct-tree product weight. The final contraction
            // restores minimum + range * unitMix exactly; no model quantization or
            // latent-bound assumption is involved.
            for (var latentIndex = 0; latentIndex < latent.Length; latentIndex++)
            for (var outputIndex = 0; outputIndex < modelOutputs.Length; outputIndex++)
            {
                var output = modelOutputs[outputIndex];
                var coefficients = Enumerable.Range(0, VisemeReconstructionProfile.VisemeCount)
                    .Select(viseme => ContractedTongueMix(
                        kind, viseme, latentIndex, output, latentScales, outputScales))
                    .ToArray();
                var minimum = coefficients.Min();
                var maximum = coefficients.Max();
                contractedMixMinimum[latentIndex, outputIndex] = minimum;
                contractedMixRange[latentIndex, outputIndex] = maximum - minimum;
                contractedMix[latentIndex, outputIndex] = graph.Param(
                    $"TongueInference/Model/{output}/MixUnit/{latentIndex}", 0f);
            }

            var simplexOutputs = new List<string> { reliability };
            simplexOutputs.AddRange(modelOutputs.Select(output => contractedBase[output]));
            for (var latentIndex = 0; latentIndex < latent.Length; latentIndex++)
            for (var outputIndex = 0; outputIndex < modelOutputs.Length; outputIndex++)
                simplexOutputs.Add(contractedMix[latentIndex, outputIndex]);

            graph.AddOperation(root, graph.SimplexMatrixProjection(
                visemeWeights,
                simplexOutputs,
                (viseme, column) =>
                {
                    if (column == 0)
                        return AdvancedVisemeVisibleTongueResidual.Reliability(kind, viseme);

                    var baseEnd = 1 + modelOutputs.Length;
                    if (column < baseEnd)
                    {
                        var outputIndex = column - 1;
                        return ContractedTongueBias(
                            kind, viseme, modelOutputs[outputIndex], outputScales);
                    }

                    var mixColumn = column - baseEnd;
                    var latentIndexForColumn = mixColumn / modelOutputs.Length;
                    var outputIndexForColumn = mixColumn % modelOutputs.Length;
                    var range = contractedMixRange[
                        latentIndexForColumn, outputIndexForColumn];
                    if (range <= 1e-8f) return 0f;
                    return (ContractedTongueMix(
                                kind, viseme, latentIndexForColumn,
                                modelOutputs[outputIndexForColumn], latentScales, outputScales) -
                            contractedMixMinimum[
                                latentIndexForColumn, outputIndexForColumn]) / range;
                },
                visemeRankOneDelta,
                column =>
                {
                    if (column == 0)
                        return AdvancedVisemeVisibleTongueResidual.Reliability(kind, 1) -
                               AdvancedVisemeVisibleTongueResidual.Reliability(kind, 8);

                    var baseEnd = 1 + modelOutputs.Length;
                    if (column < baseEnd)
                    {
                        var outputIndex = column - 1;
                        return ContractedTongueBias(
                                   kind, 1, modelOutputs[outputIndex], outputScales) -
                               ContractedTongueBias(
                                   kind, 8, modelOutputs[outputIndex], outputScales);
                    }

                    var mixColumn = column - baseEnd;
                    var latentIndexForColumn = mixColumn / modelOutputs.Length;
                    var outputIndexForColumn = mixColumn % modelOutputs.Length;
                    var range = contractedMixRange[
                        latentIndexForColumn, outputIndexForColumn];
                    if (range <= 1e-8f) return 0f;
                    return (ContractedTongueMix(
                                kind, 1, latentIndexForColumn,
                                modelOutputs[outputIndexForColumn], latentScales, outputScales) -
                            ContractedTongueMix(
                                kind, 8, latentIndexForColumn,
                                modelOutputs[outputIndexForColumn], latentScales, outputScales)) / range;
                },
                "Tongue inference viseme contraction"));

            var contractedProducts = new string[latent.Length, modelOutputs.Length];
            for (var latentIndex = 0; latentIndex < latent.Length; latentIndex++)
            for (var outputIndex = 0; outputIndex < modelOutputs.Length; outputIndex++)
                contractedProducts[latentIndex, outputIndex] = graph.Param(
                    $"TongueInference/Model/{modelOutputs[outputIndex]}/Product/{latentIndex}", 0f);
            var productWeights = new List<string>();
            var productInputs = new string[latent.Length * modelOutputs.Length, 1];
            var productOutputs = new string[latent.Length * modelOutputs.Length, 1];
            for (var latentIndex = 0; latentIndex < latent.Length; latentIndex++)
            for (var outputIndex = 0; outputIndex < modelOutputs.Length; outputIndex++)
            {
                var productIndex = latentIndex * modelOutputs.Length + outputIndex;
                productWeights.Add(contractedMix[latentIndex, outputIndex]);
                productInputs[productIndex, 0] = latent[latentIndex];
                productOutputs[productIndex, 0] = contractedProducts[latentIndex, outputIndex];
            }
            graph.AddOperation(root, graph.GroupedElementwiseProducts(
                productWeights,
                productInputs,
                productOutputs,
                "Tongue inference contracted bilinear products"));

            var predictions = new Dictionary<AdvancedVisemeVisibleTongueOutput, string>();
            var normalizedOutputs = modelOutputs.ToDictionary(
                output => output,
                output => graph.Param($"TongueInference/Model/{output}/Normalized", 0f));
            var sumInputs = new List<string>();
            sumInputs.AddRange(modelOutputs.Select(output => contractedBase[output]));
            sumInputs.AddRange(latent);
            for (var latentIndex = 0; latentIndex < latent.Length; latentIndex++)
            for (var outputIndex = 0; outputIndex < modelOutputs.Length; outputIndex++)
                sumInputs.Add(contractedProducts[latentIndex, outputIndex]);
            graph.AddOperation(root, graph.SignedMatrixProjection(
                sumInputs,
                modelOutputs.Select(output => normalizedOutputs[output]).ToArray(),
                new float[modelOutputs.Length],
                (inputIndex, outputIndex) =>
                {
                    if (inputIndex < modelOutputs.Length)
                        return inputIndex == outputIndex ? 1f : 0f;
                    var latentEnd = modelOutputs.Length + latent.Length;
                    if (inputIndex < latentEnd)
                        return contractedMixMinimum[
                            inputIndex - modelOutputs.Length, outputIndex];
                    var productIndex = inputIndex - latentEnd;
                    return productIndex % modelOutputs.Length == outputIndex
                        ? contractedMixRange[productIndex / modelOutputs.Length, outputIndex]
                        : 0f;
                },
                "Tongue inference contracted output sum"));

            foreach (var output in modelOutputs)
            {
                var outputScale = outputScales[output];
                var normalized = normalizedOutputs[output];
                var reliable = graph.Param($"TongueInference/Model/{output}/Reliable", 0f);
                graph.AddOperation(root, graph.Multiply(reliability, normalized, reliable, true));
                var prediction = graph.Param($"TongueInference/Model/{output}", 0f);
                graph.AddOperation(root, graph.Map(
                    reliable, prediction, ScaledClampPoints(outputScale)));
                // The regressor intentionally consumes the feature convention it
                // was trained on, including its raw residual stage. Filter the
                // inferred latent output instead so OSC chatter cannot bypass the
                // visible-pose denoiser without shifting the model's input domain.
                var predictionFast = graph.Param($"TongueInference/Model/{output}/StableFast", 0f);
                var predictionStable = graph.Param($"TongueInference/Model/{output}/Stable", 0f);
                graph.AddOperation(root, graph.Smooth(prediction, predictionFast, alpha, true));
                graph.AddOperation(root, graph.Smooth(predictionFast, predictionStable, alpha, true));
                predictions[output] = predictionStable;
            }

            var tongueOutVisibility = graph.Param("TongueInference/TongueOut/Visibility", 0f);
            graph.AddOperation(root, graph.Linear(tongueOutVisibility, new[]
            {
                Term.Positive(visemeWeights[3], 0.85f),
                Term.Positive(current[AdvancedVisemeVisibleFeatureChannel.LipAperture], 0.15f)
            }));
            var tongueOutConfidenceVisible = graph.Param(
                "TongueInference/TongueOut/ConfidenceVisible", 0f);
            graph.AddOperation(root,
                graph.Multiply(tongueConfidence, tongueOutVisibility, tongueOutConfidenceVisible, false));
            var tongueOutConfidence = graph.Param("TongueInference/TongueOut/Confidence", 0f);
            graph.AddOperation(root, graph.Linear(tongueOutConfidence, new[]
            {
                Term.Positive(tongueOutConfidenceVisible, 0.30f)
            }));
            var tongueYConfidence = graph.Param("TongueInference/TongueY/Confidence", 0f);
            graph.AddOperation(root, graph.Linear(tongueYConfidence, new[]
            {
                Term.Positive(tongueConfidence, 0.65f)
            }));

            var tongueYPrediction = predictions[AdvancedVisemeVisibleTongueOutput.TongueY];
            var tongueYBinding = request.profile.FindBinding(AdvancedVisemeArticulator.TongueY);
            if (tongueYBinding != null && tongueYBinding.trackingScale < 0f)
            {
                var inverted = graph.Param("TongueInference/Model/TongueY/Inverted", 0f);
                graph.AddOperation(root, graph.Linear(inverted, new[]
                {
                    Term.Signed(tongueYPrediction, -1f)
                }));
                tongueYPrediction = inverted;
            }

            speechFast[AdvancedVisemeArticulator.TongueOut] = ApplyHeadroomResidual(
                graph, root, speechFast[AdvancedVisemeArticulator.TongueOut],
                predictions[AdvancedVisemeVisibleTongueOutput.TongueOut], tongueOutConfidence,
                false, "TongueInference/TongueOut/Fast");
            speechSlow[AdvancedVisemeArticulator.TongueOut] = ApplyHeadroomResidual(
                graph, root, speechSlow[AdvancedVisemeArticulator.TongueOut],
                predictions[AdvancedVisemeVisibleTongueOutput.TongueOut], tongueOutConfidence,
                false, "TongueInference/TongueOut/Slow");
            speechFast[AdvancedVisemeArticulator.TongueY] = ApplyHeadroomResidual(
                graph, root, speechFast[AdvancedVisemeArticulator.TongueY],
                tongueYPrediction, tongueYConfidence,
                true, "TongueInference/TongueY/Fast");
            speechSlow[AdvancedVisemeArticulator.TongueY] = ApplyHeadroomResidual(
                graph, root, speechSlow[AdvancedVisemeArticulator.TongueY],
                tongueYPrediction, tongueYConfidence,
                true, "TongueInference/TongueY/Slow");
            return phonePosterior;
        }

        private static FacePhonePosteriorGraph BuildFacePhonePosterior(
            MathGraph graph,
            BlendTree root,
            Request request,
            Result result,
            BetaCoarticulationGraph betaGraph,
            AdvancedVisemeHiddenPhoneModelKind kind,
            IReadOnlyList<string> features,
            string visibleConfidence,
            string frameTime)
        {
            var bound = Mathf.Max(1f,
                AdvancedVisemeHiddenPhonePosterior.ConservativeLogitBound(kind));
            var observationWeights = betaGraph.common.fast;
            var normalizedLogit = graph.Param("PhonePosterior/Model/NormalizedLogit", 0f);
            if (HiddenPhoneCoefficientsAreShared(kind, features.Count))
            {
                // The fitted observation likelihood is deliberately independent
                // of the hard Oculus class; only the empirical phone prior changes
                // with that class. Factor the shared affine likelihood once and
                // simplex-mix the 15 priors. This is algebraically identical to 15
                // full experts while avoiding their duplicated Animator work.
                var terms = observationWeights.Select((parameter, viseme) =>
                        Term.Positive(parameter,
                            AdvancedVisemeHiddenPhonePosterior.Bias(kind, viseme) / bound))
                    .ToList();
                for (var feature = 0; feature < features.Count; feature++)
                {
                    terms.Add(Term.Signed(features[feature],
                        AdvancedVisemeHiddenPhonePosterior.Coefficient(
                            kind, 0, feature) / bound));
                }
                graph.AddOperation(root, graph.Linear(normalizedLogit, terms));
            }
            else
            {
                // Retain the general mixture-of-experts form if a future generated
                // model intentionally introduces viseme-specific face likelihoods.
                var weightedExperts = new List<string>();
                for (var viseme = 0; viseme < VisemeReconstructionProfile.VisemeCount; viseme++)
                {
                    var conditional = graph.Param(
                        $"PhonePosterior/Model/Viseme/{viseme}/NormalizedLogit", 0f);
                    var terms = new List<Term>
                    {
                        Term.Constant(AdvancedVisemeHiddenPhonePosterior.Bias(kind, viseme) / bound)
                    };
                    for (var feature = 0; feature < features.Count; feature++)
                    {
                        terms.Add(Term.Signed(features[feature],
                            AdvancedVisemeHiddenPhonePosterior.Coefficient(
                                kind, viseme, feature) / bound));
                    }
                    graph.AddOperation(root, graph.Linear(conditional, terms));

                    var weighted = graph.Param(
                        $"PhonePosterior/Model/Viseme/{viseme}/WeightedLogit", 0f);
                    graph.AddOperation(root, graph.Multiply(
                        observationWeights[viseme], conditional, weighted, true));
                    weightedExperts.Add(weighted);
                }
                graph.AddOperation(root, graph.Linear(normalizedLogit,
                    weightedExperts.Select(parameter => Term.Signed(parameter, 1f))));
            }

            var logit = graph.Param("PhonePosterior/Model/Logit", 0f);
            graph.AddOperation(root, graph.Linear(logit, new[]
            {
                Term.Signed(normalizedLogit, bound)
            }));
            var rawShare = graph.Param("PhonePosterior/Model/MShareRaw", 0.5f);
            graph.AddOperation(root, graph.Map(
                logit, rawShare, LogisticPoints(bound)));

            var alpha = graph.Param("PhonePosterior/Observer/Alpha", 0.5f);
            graph.AddOperation(root, graph.AlphaFromDeltaTime(
                frameTime, alpha, AdvancedVisemeHiddenPhonePosterior.ObserverResponseSeconds));
            var shareFast = graph.Param("PhonePosterior/Model/MShareFast", 0.5f);
            var shareSlow = graph.Param("PhonePosterior/Model/MShareSlow", 0.5f);
            graph.AddOperation(root, graph.Smooth(rawShare, shareFast, alpha, false));
            graph.AddOperation(root, graph.Smooth(shareFast, shareSlow, alpha, false));

            var reliability = graph.Param("PhonePosterior/Model/Reliability", 0f);
            graph.AddOperation(root, graph.Linear(reliability,
                observationWeights.Select((parameter, viseme) => Term.Positive(
                    parameter,
                    AdvancedVisemeHiddenPhonePosterior.Reliability(kind, viseme)))));
            var centered = graph.Param("PhonePosterior/Model/CenteredShare", 0f);
            var margin = graph.Param("PhonePosterior/Model/Margin", 0f);
            var marginConfidence = graph.Param("PhonePosterior/Model/MarginConfidence", 0f);
            graph.AddOperation(root, graph.Linear(centered, new[]
            {
                Term.Constant(-1f), Term.Positive(shareSlow, 2f)
            }));
            graph.AddOperation(root, graph.Abs(centered, margin));
            graph.AddOperation(root, graph.Map(
                margin, marginConfidence, SmoothStepPoints(0.12f, 0.65f, 0f, 1f)));

            var reliableConfidence = graph.Param("PhonePosterior/Confidence/Reliable", 0f);
            var posteriorConfidence = graph.Param("PhonePosterior/Confidence", 0f);
            graph.AddOperation(root, graph.Multiply(
                visibleConfidence, reliability, reliableConfidence, false));
            graph.AddOperation(root, graph.Multiply(
                reliableConfidence, marginConfidence, posteriorConfidence, false));

            // Unified Expressions exposes a real velum channel on richer
            // installations. Reuse it only when it already exists and has shown
            // sustained non-noise motion. A closed soft palate is oral evidence
            // (p/b/l), so it lowers posterior authority instead of incorrectly
            // transferring that mass to N. No fresh synced parameter is created.
            if (request.auxiliaryTrackingParameterNames != null &&
                request.auxiliaryTrackingParameterNames.TryGetValue(
                    "SoftPalateClose", out var softPalate) &&
                !string.IsNullOrWhiteSpace(softPalate))
            {
                var palateFast = graph.Param("PhonePosterior/SoftPalate/Fast", 0f);
                var palateSlow = graph.Param("PhonePosterior/SoftPalate/Slow", 0f);
                graph.AddOperation(root, graph.Smooth(softPalate, palateFast, alpha, false));
                graph.AddOperation(root, graph.Smooth(palateFast, palateSlow, alpha, false));
                var observed = graph.Param("PhonePosterior/SoftPalate/Observed", 0f);
                graph.AddOperation(root, graph.Map(softPalate, observed, new[]
                {
                    Point(0f, 0f), Point(0.005f, 0f), Point(0.03f, 1f), Point(1f, 1f)
                }));
                var capabilityAlpha = graph.Param("PhonePosterior/SoftPalate/CapabilityAlpha", 0.1f);
                graph.AddOperation(root, graph.AlphaFromDeltaTime(
                    frameTime, capabilityAlpha, 0.12f));
                var accumulated = graph.Param("PhonePosterior/SoftPalate/Accumulated", 0f);
                graph.AddOperation(root, graph.Smooth(
                    observed, accumulated, capabilityAlpha, false));
                var confirmed = graph.Param("PhonePosterior/SoftPalate/Confirmed", 0f);
                graph.AddOperation(root, graph.Map(accumulated, confirmed, new[]
                {
                    Point(0f, 0f), Point(0.78f, 0f), Point(0.8f, 1f), Point(1f, 1f)
                }));
                var capability = graph.Param("PhonePosterior/SoftPalate/Capability", 0f);
                var latched = graph.Param("PhonePosterior/SoftPalate/Latched", 0f);
                graph.AddOperation(root, graph.Max(capability, confirmed, latched));
                graph.AddOperation(root, graph.Copy(latched, capability, false));
                var oralEvidence = graph.Param("PhonePosterior/SoftPalate/OralEvidence", 0f);
                var nasalCompatibility = graph.Param(
                    "PhonePosterior/SoftPalate/NasalCompatibility", 1f);
                var palateAdjusted = graph.Param("PhonePosterior/Confidence/PalateAdjusted", 0f);
                graph.AddOperation(root, graph.Multiply(
                    capability, palateSlow, oralEvidence, false));
                graph.AddOperation(root, graph.Linear(nasalCompatibility, new[]
                {
                    Term.Constant(1f), Term.Positive(oralEvidence, -1f)
                }));
                graph.AddOperation(root, graph.Multiply(
                    posteriorConfidence, nasalCompatibility, palateAdjusted, false));
                posteriorConfidence = palateAdjusted;
            }

            // Preserve the authored/public viseme simplex. A calibrated build can
            // apply only the complement-space PP<->nn geometry as one signed
            // correction: delta = confidence * (posteriorPP - originalPP).
            var hiddenCandidateMass = graph.Param(
                "PhonePosterior/Residual/CandidateMass", 0f);
            var hiddenTargetPp = graph.Param("PhonePosterior/Residual/TargetPP", 0f);
            var hiddenRawDelta = graph.Param("PhonePosterior/Residual/RawDelta", 0f);
            var hiddenResidualDelta = graph.Param("PhonePosterior/Residual/Delta", 0f);
            graph.AddOperation(root, graph.Linear(hiddenCandidateMass, new[]
            {
                Term.Positive(betaGraph.common.slow[1], 1f),
                Term.Positive(betaGraph.common.slow[8], 1f)
            }));
            graph.AddOperation(root, graph.Multiply(
                shareSlow, hiddenCandidateMass, hiddenTargetPp, false));
            graph.AddOperation(root, graph.Linear(hiddenRawDelta, new[]
            {
                Term.Signed(hiddenTargetPp, 1f),
                Term.Signed(betaGraph.common.slow[1], -1f)
            }));
            graph.AddOperation(root, graph.Multiply(
                hiddenRawDelta, posteriorConfidence, hiddenResidualDelta, true));

            var output = new FacePhonePosteriorGraph
            {
                mShareFast = shareFast,
                mShareSlow = shareSlow,
                confidence = posteriorConfidence,
                hiddenResidualDelta = hiddenResidualDelta
            };
            BuildMergedNasalCorrections(
                graph, root, betaGraph, shareFast, shareSlow,
                posteriorConfidence, output);

            var prefix = request.component.NormalizedPrefix;
            var mOutput = graph.Param(
                AdvancedVisemeParameterContract.Speech(prefix, "Hypothesis/M"),
                0f, false);
            var nOutput = graph.Param(
                AdvancedVisemeParameterContract.Speech(prefix, "Hypothesis/N"),
                0f, false);
            var confidenceOutput = graph.Param(
                AdvancedVisemeParameterContract.Speech(prefix, "Hypothesis/Confidence"),
                0f, false);
            var hypothesisBase = graph.MultiSetter(
                "Hidden phone hypothesis normalized base",
                new[]
                {
                    new KeyValuePair<string, float>(mOutput, 0f),
                    new KeyValuePair<string, float>(nOutput, 0f),
                    new KeyValuePair<string, float>(confidenceOutput, 1f)
                });
            var whenN = graph.Direct("Hidden phone hypothesis N endpoint");
            whenN.children = new[]
            {
                new ChildMotion
                {
                    motion = graph.Setter(nOutput, 1f),
                    directBlendParameter = hiddenCandidateMass,
                    timeScale = 1f
                },
                new ChildMotion
                {
                    motion = hypothesisBase,
                    directBlendParameter = MathGraph.AlwaysOneParameter,
                    timeScale = 1f
                }
            };
            var whenM = graph.Direct("Hidden phone hypothesis M endpoint");
            whenM.children = new[]
            {
                new ChildMotion
                {
                    motion = graph.Setter(mOutput, 1f),
                    directBlendParameter = hiddenCandidateMass,
                    timeScale = 1f
                },
                new ChildMotion
                {
                    motion = hypothesisBase,
                    directBlendParameter = MathGraph.AlwaysOneParameter,
                    timeScale = 1f
                }
            };
            var distributed = graph.InterpolateMotions(
                whenN, whenM, shareSlow,
                "Hidden phone hypothesis M-N distribution");
            var weightedHypothesis = graph.Direct(
                "Hidden phone confidence-weighted hypothesis outputs");
            weightedHypothesis.children = new[]
            {
                new ChildMotion
                {
                    motion = distributed,
                    directBlendParameter = posteriorConfidence,
                    timeScale = 1f
                },
                new ChildMotion
                {
                    motion = graph.MultiSetter(
                        "Hidden phone hypothesis safety zero",
                        new[]
                        {
                            new KeyValuePair<string, float>(mOutput, 0f),
                            new KeyValuePair<string, float>(nOutput, 0f),
                            new KeyValuePair<string, float>(confidenceOutput, 0f)
                        }),
                    directBlendParameter = MathGraph.AlwaysOneParameter,
                    timeScale = 1f
                }
            };
            graph.AddOperation(root, weightedHypothesis);
            result.globalParameters.Add(mOutput);
            result.globalParameters.Add(nOutput);
            result.globalParameters.Add(confidenceOutput);
            return output;
        }

        internal static (float input, float output)[] LogisticPoints(float bound)
        {
            bound = Mathf.Max(1f, bound);
            return new[]
                {
                    -bound, -4.71307f, -3.19953f, -2.28008f, -1.56254f, -0.90370f,
                    0f,
                    0.90370f, 1.56254f, 2.28008f, 3.19953f, 4.71307f, bound
                }
                .Select(value => Mathf.Clamp(value, -bound, bound))
                .Distinct()
                .OrderBy(value => value)
                .Select(value => Point(value, AdvancedVisemeMath.Logistic(value)))
                .ToArray();
        }

        private static bool HiddenPhoneCoefficientsAreShared(
            AdvancedVisemeHiddenPhoneModelKind kind,
            int featureCount)
        {
            for (var viseme = 1; viseme < VisemeReconstructionProfile.VisemeCount; viseme++)
            for (var feature = 0; feature < featureCount; feature++)
            {
                if (!AdvancedVisemeHiddenPhonePosterior.Coefficient(kind, viseme, feature).Equals(
                        AdvancedVisemeHiddenPhonePosterior.Coefficient(kind, 0, feature)))
                    return false;
            }
            return true;
        }

        private static float ContractedTongueBias(
            AdvancedVisemeVisibleTongueModelKind kind,
            int viseme,
            AdvancedVisemeVisibleTongueOutput output,
            IReadOnlyDictionary<AdvancedVisemeVisibleTongueOutput, float> outputScales)
        {
            var value = 0f;
            for (var target = 0;
                 target < AdvancedVisemeVisibleTongueResidual.TongueLatentCount(kind);
                 target++)
            {
                value += AdvancedVisemeVisibleTongueResidual.VisemeBias(
                             kind, viseme, target) *
                         AdvancedVisemeVisibleTongueResidual.OutputProjection(
                             kind, target, output) /
                         outputScales[output];
            }
            return value;
        }

        private static float ContractedTongueMix(
            AdvancedVisemeVisibleTongueModelKind kind,
            int viseme,
            int latent,
            AdvancedVisemeVisibleTongueOutput output,
            IReadOnlyList<float> latentScales,
            IReadOnlyDictionary<AdvancedVisemeVisibleTongueOutput, float> outputScales)
        {
            var value = 0f;
            for (var target = 0;
                 target < AdvancedVisemeVisibleTongueResidual.TongueLatentCount(kind);
                 target++)
            {
                value += AdvancedVisemeVisibleTongueResidual.VisemeMix(
                             kind, viseme, latent, target) *
                         AdvancedVisemeVisibleTongueResidual.OutputProjection(
                             kind, target, output);
            }
            return latentScales[latent] * value / outputScales[output];
        }

        internal static float HiddenPhoneObserverCompatibility(float responseSeconds)
        {
            // The checked model was fitted against one exact upstream observer.
            // A log-Gaussian support kernel is scale-symmetric and causes custom
            // response profiles to abstain instead of confidently evaluating a
            // differently phased trajectory. The default trained response is 1.
            if (!(responseSeconds > 0f) || float.IsNaN(responseSeconds) ||
                float.IsInfinity(responseSeconds)) return 0f;
            var logRatio = Mathf.Log(
                responseSeconds / AdvancedVisemeHiddenPhonePosterior.ObserverResponseSeconds);
            const float sigmaLog = 0.15f;
            return Mathf.Clamp01(Mathf.Exp(
                -0.5f * logRatio * logRatio / (sigmaLog * sigmaLog)));
        }

        private static string BuildEmpiricalFeatureSupport(
            MathGraph graph,
            BlendTree root,
            string feature,
            AdvancedVisemeVisibleTongueModelKind kind,
            int featureIndex,
            string key)
        {
            var safeBound = Mathf.Max(1e-6f,
                AdvancedVisemeVisibleTongueResidual.FeatureSafeBound(kind, featureIndex));
            var supported = Mathf.Clamp(
                AdvancedVisemeVisibleTongueResidual.FeatureAbsP995(kind, featureIndex),
                0f, safeBound);
            return BuildEmpiricalFeatureSupport(
                graph, root, feature, supported, safeBound, key);
        }

        private static string BuildEmpiricalFeatureSupport(
            MathGraph graph,
            BlendTree root,
            string feature,
            float supported,
            float safeBound,
            string key)
        {
            safeBound = Mathf.Max(1e-6f, safeBound);
            supported = Mathf.Clamp(supported, 0f, safeBound);
            if (safeBound - supported <= 1e-5f) return MathGraph.AlwaysOneParameter;

            var fadeEnd = Mathf.Min(
                safeBound,
                Mathf.Max(supported + 0.01f, supported * 1.5f));
            if (fadeEnd - supported <= 1e-5f) return MathGraph.AlwaysOneParameter;

            var magnitude = graph.Param(key + "/Magnitude", 0f);
            var confidence = graph.Param(key, 1f);
            graph.AddOperation(root, graph.Abs(feature, magnitude));
            var points = new List<(float input, float output)>
            {
                Point(0f, 1f), Point(supported, 1f),
                Point(fadeEnd, 0f)
            };
            if (safeBound - fadeEnd > 1e-5f) points.Add(Point(safeBound, 0f));
            graph.AddOperation(root, graph.Map(magnitude, confidence, points));
            return confidence;
        }

        private static string BuildSmoothedSupportConfidence(
            MathGraph graph,
            BlendTree root,
            string key,
            IReadOnlyList<string> factors,
            string alpha)
        {
            var raw = MinParameters(
                graph, root, key + "/Raw", factors?.ToArray() ?? Array.Empty<string>());
            var fast = graph.Param(key + "/Fast", 1f);
            var stable = graph.Param(key, 1f);
            graph.AddOperation(root, graph.Smooth(raw, fast, alpha, false));
            graph.AddOperation(root, graph.Smooth(fast, stable, alpha, false));
            return stable;
        }

        internal static (float input, float output)[] ScaledClampPoints(float scale)
        {
            scale = Mathf.Max(0f, scale);
            if (scale <= 1f)
            {
                return new[]
                {
                    Point(-1f, -scale), Point(0f, 0f), Point(1f, scale)
                };
            }

            var unitInput = 1f / scale;
            return new[]
            {
                Point(-1f, -1f), Point(-unitInput, -1f), Point(0f, 0f),
                Point(unitInput, 1f), Point(1f, 1f)
            };
        }

        private static string BuildSignedUnitValue(
            MathGraph graph, BlendTree root, string input, string key)
        {
            var output = graph.Param(key, 0.5f);
            graph.AddOperation(root, graph.Linear(output, new[]
            {
                Term.Constant(0.5f), Term.Signed(input, 0.5f)
            }));
            return output;
        }

        private static string BuildOpposedUnitValue(
            MathGraph graph, BlendTree root, string positive, string negative, string key)
        {
            var raw = graph.Param(key + "/Raw", 0f);
            var output = graph.Param(key, 0f);
            graph.AddOperation(root, graph.Linear(raw, new[]
            {
                Term.Positive(positive, 1f), Term.Positive(negative, -1f)
            }));
            graph.AddOperation(root, graph.Map(raw, output, new[]
            {
                Point(-1f, 0f), Point(0f, 0f), Point(1f, 1f), Point(2f, 1f)
            }));
            return output;
        }

        private static string BuildProtrusionValue(
            MathGraph graph,
            BlendTree root,
            string funnel,
            string pucker,
            string suck,
            string key)
        {
            var positive = MaxParameters(graph, root, key + "/Positive", funnel, pucker);
            return BuildOpposedUnitValue(graph, root, positive, suck, key);
        }

        private static string BuildHeadroomNormalizedResidual(
            MathGraph graph,
            BlendTree root,
            string tracked,
            string center,
            string key,
            out string oodConfidence)
        {
            var delta = graph.Param(key + "/Delta", 0f);
            graph.AddOperation(root, graph.Linear(delta, new[]
            {
                Term.Positive(tracked, 1f), Term.Positive(center, -1f)
            }));
            var positive = graph.Param(key + "/Positive", 0f);
            var negative = graph.Param(key + "/Negative", 0f);
            graph.AddOperation(root, graph.Map(delta, positive, new[]
            {
                Point(-2f, 0f), Point(0f, 0f), Point(2f, 2f)
            }));
            graph.AddOperation(root, graph.Map(delta, negative, new[]
            {
                Point(-2f, 2f), Point(0f, 0f), Point(2f, 0f)
            }));

            var reciprocalUpper = graph.Param(key + "/ReciprocalUpper", 1f);
            var reciprocalLower = graph.Param(key + "/ReciprocalLower", 1f);
            var headroomSamples = new[] { 0f, 0.075f, 0.125f, 0.25f, 0.5f, 0.75f, 0.875f, 0.925f, 1f };
            graph.AddOperation(root, graph.Map(center, reciprocalUpper,
                headroomSamples.Select(value => Point(value,
                    1f / Mathf.Max(1f - value, AdvancedVisemeVisibleTongueResidual.HeadroomFloor))).ToArray()));
            graph.AddOperation(root, graph.Map(center, reciprocalLower,
                headroomSamples.Select(value => Point(value,
                    1f / Mathf.Max(value, AdvancedVisemeVisibleTongueResidual.HeadroomFloor))).ToArray()));
            var positiveFraction = graph.Param(key + "/PositiveFraction", 0f);
            var negativeFraction = graph.Param(key + "/NegativeFraction", 0f);
            graph.AddOperation(root,
                graph.Multiply(positive, reciprocalUpper, positiveFraction, false));
            graph.AddOperation(root,
                graph.Multiply(negative, reciprocalLower, negativeFraction, false));
            var raw = graph.Param(key + "/Raw", 0f);
            graph.AddOperation(root, graph.Linear(raw, new[]
            {
                Term.Positive(positiveFraction, 1f), Term.Positive(negativeFraction, -1f)
            }));
            var magnitude = graph.Param(key + "/Magnitude", 0f);
            graph.AddOperation(root, graph.Abs(raw, magnitude));
            oodConfidence = graph.Param(key + "/OodConfidence", 1f);
            graph.AddOperation(root, graph.Map(magnitude, oodConfidence, new[]
            {
                Point(0f, 1f), Point(1f, 1f), Point(1.5f, 0f), Point(4f, 0f)
            }));
            var output = graph.Param(key + "/Clamped", 0f);
            graph.AddOperation(root, graph.Map(raw, output, new[]
            {
                Point(-4f, -1f), Point(-1f, -1f), Point(0f, 0f),
                Point(1f, 1f), Point(4f, 1f)
            }));
            return output;
        }

        private static string ApplyHeadroomResidual(
            MathGraph graph,
            BlendTree root,
            string center,
            string residual,
            string confidence,
            bool signed,
            string key)
        {
            var positive = graph.Param(key + "/Positive", 0f);
            var negative = graph.Param(key + "/Negative", 0f);
            graph.AddOperation(root, graph.Map(residual, positive, new[]
            {
                Point(-1f, 0f), Point(0f, 0f), Point(1f, 1f)
            }));
            graph.AddOperation(root, graph.Map(residual, negative, new[]
            {
                Point(-1f, 1f), Point(0f, 0f), Point(1f, 0f)
            }));
            var upperHeadroom = graph.Param(key + "/UpperHeadroom", 1f);
            var lowerHeadroom = graph.Param(key + "/LowerHeadroom", signed ? 1f : 0f);
            graph.AddOperation(root, graph.Linear(upperHeadroom, new[]
            {
                Term.Constant(1f), Term.For(center, -1f, signed)
            }));
            graph.AddOperation(root, graph.Linear(lowerHeadroom, signed
                ? new[] { Term.Constant(1f), Term.Signed(center, 1f) }
                : new[] { Term.Positive(center, 1f) }));
            var positiveDelta = graph.Param(key + "/PositiveDelta", 0f);
            var negativeDelta = graph.Param(key + "/NegativeDelta", 0f);
            graph.AddOperation(root,
                graph.Multiply(positive, upperHeadroom, positiveDelta, false));
            graph.AddOperation(root,
                graph.Multiply(negative, lowerHeadroom, negativeDelta, false));
            var targetRaw = graph.Param(key + "/TargetRaw", 0f);
            graph.AddOperation(root, graph.Linear(targetRaw, new[]
            {
                Term.For(center, 1f, signed),
                Term.Positive(positiveDelta, 1f),
                Term.Positive(negativeDelta, -1f)
            }));
            var target = graph.Param(key + "/Target", 0f);
            graph.AddOperation(root, graph.Map(targetRaw, target, signed
                ? new[] { Point(-2f, -1f), Point(-1f, -1f), Point(0f, 0f), Point(1f, 1f), Point(2f, 1f) }
                : new[] { Point(-1f, 0f), Point(0f, 0f), Point(1f, 1f), Point(2f, 1f) }));
            var output = graph.Param(key + "/Output", 0f);
            graph.AddOperation(root, graph.Interpolate(center, target, output, confidence, signed));
            return output;
        }

        private static string MinParameters(
            MathGraph graph, BlendTree root, string key, params string[] parameters)
        {
            if (parameters == null || parameters.Length == 0) return MathGraph.AlwaysOneParameter;
            var result = parameters[0];
            for (var i = 1; i < parameters.Length; i++)
            {
                var next = graph.Param(key + "/" + i, 0f);
                graph.AddOperation(root, graph.Min(result, parameters[i], next));
                result = next;
            }
            return result;
        }

        private static string MaxParameters(
            MathGraph graph, BlendTree root, string key, params string[] parameters)
        {
            if (parameters == null || parameters.Length == 0) return MathGraph.AlwaysOneParameter;
            var result = parameters[0];
            for (var i = 1; i < parameters.Length; i++)
            {
                var next = graph.Param(key + "/" + i, 0f);
                graph.AddOperation(root, graph.Max(result, parameters[i], next));
                result = next;
            }
            return result;
        }

        private static string Calibrate(
            MathGraph graph,
            BlendTree root,
            string input,
            ArticulatorRigBinding binding,
            AdvancedVisemeArticulator articulator,
            string stage,
            AdvancedVisemeExternalPose externalPose = null)
        {
            var calibrated = input;
            var templateNormalization = ExternalPoseNormalizationPoints(articulator, externalPose);
            if (templateNormalization != null)
            {
                // Tailored templates often reach their authored unit pose before
                // the semantic parameter reaches 1 (JawOpen commonly uses 0.8).
                // Reproduce that tree's linear coordinate system before applying
                // the user's profile calibration, so both the tracker endpoint and
                // the extracted pose remain mathematically identical.
                var normalized = graph.Param($"Tracking/{articulator}/{stage}TemplateNormalized", 0f);
                graph.AddOperation(root, graph.Map(input, normalized, templateNormalization));
                calibrated = normalized;
            }
            if (!Mathf.Approximately(binding.trackingScale, 1f) ||
                !Mathf.Approximately(binding.trackingOffset, 0f))
            {
                var profileCalibrationInput = calibrated;
                calibrated = graph.Param($"Tracking/{articulator}/{stage}CalibratedRaw", 0f);
                graph.AddOperation(root, graph.Linear(calibrated, new[]
                {
                    Term.For(profileCalibrationInput, binding.trackingScale, IsSigned(articulator)),
                    Term.Constant(binding.trackingOffset)
                }));
            }

            var output = graph.Param($"Tracking/{articulator}/{stage}Calibrated", 0f);
            graph.AddOperation(root, IsSigned(articulator)
                ? graph.Map(calibrated, output, new[]
                {
                    Point(-2f, -1f), Point(-1f, -1f), Point(0f, 0f), Point(1f, 1f), Point(2f, 1f)
                })
                : graph.Map(calibrated, output, new[]
                {
                    Point(-1f, 0f), Point(0f, 0f), Point(1f, 1f), Point(2f, 1f)
                }));
            return output;
        }

        internal static (float input, float output)[] ExternalPoseNormalizationPoints(
            AdvancedVisemeArticulator articulator,
            AdvancedVisemeExternalPose externalPose)
        {
            if (externalPose == null ||
                externalPose.positive == null && externalPose.negative == null)
                return null;
            var signed = IsSigned(articulator);
            if (!signed && externalPose.positive == null) return null;
            var needsCalibration = externalPose.positive == null ||
                                   !Mathf.Approximately(externalPose.positiveThreshold, 1f) ||
                                   signed && (externalPose.negative == null ||
                                              !Mathf.Approximately(
                                                  externalPose.negativeThreshold, -1f));
            if (!needsCalibration) return null;
            if (!signed)
                return new[]
                {
                    Point(0f, 0f), Point(externalPose.positiveThreshold, 1f)
                };
            if (externalPose.negative != null)
            {
                if (externalPose.positive == null)
                    return new[]
                    {
                        Point(externalPose.negativeThreshold, -1f),
                        Point(0f, 0f), Point(1f, 0f)
                    };
                return new[]
                {
                    Point(externalPose.negativeThreshold, -1f), Point(0f, 0f),
                    Point(externalPose.positiveThreshold, 1f)
                };
            }

            // A one-sided tailored tree explicitly defines negative values as
            // neutral, not as an inverse positive pose.
            return new[]
            {
                Point(-1f, 0f), Point(0f, 0f),
                Point(externalPose.positiveThreshold, 1f)
            };
        }

        private static Term[] BuildVisemeTerms(string[] inputs, float[] coefficients, bool signed)
        {
            var terms = new List<Term>();
            for (var i = 0; i < inputs.Length; i++)
            {
                if (Mathf.Abs(coefficients[i]) < 1e-6f) continue;
                terms.Add(Term.For(inputs[i], coefficients[i], signed || coefficients[i] < 0f));
            }
            if (terms.Count == 0) terms.Add(Term.Constant(0f));
            return terms.ToArray();
        }

        private static void AddVisemeMatrixProjection(
            MathGraph graph,
            BlendTree root,
            Request request,
            string name,
            IReadOnlyList<string> weights,
            IReadOnlyDictionary<AdvancedVisemeArticulator, string> outputs)
        {
            var projection = BuildVisemeMatrixProjectionMotion(
                graph, request, name, weights, outputs);
            if (projection != null) graph.AddOperation(root, projection);
        }

        private static Motion BuildVisemeMatrixProjectionMotion(
            MathGraph graph,
            Request request,
            string name,
            IReadOnlyList<string> weights,
            IReadOnlyDictionary<AdvancedVisemeArticulator, string> outputs)
        {
            if (weights == null || outputs == null || outputs.Count == 0) return null;
            if (weights.Count != VisemeReconstructionProfile.VisemeCount)
                throw new InvalidOperationException(
                    $"{name} expected {VisemeReconstructionProfile.VisemeCount} viseme weights, " +
                    $"but received {weights.Count}.");

            var ordered = outputs.OrderBy(pair => (int)pair.Key).ToArray();
            var coefficients = ordered.ToDictionary(
                pair => pair.Key,
                pair => GetAdjustedSpeechCoefficients(request, pair.Key));
            var projection = graph.Direct(name);
            var children = new List<ChildMotion>();
            for (var viseme = 0; viseme < weights.Count; viseme++)
            {
                var values = ordered
                    .Select(pair => new KeyValuePair<string, float>(
                        pair.Value, coefficients[pair.Key][viseme]))
                    .Where(pair => Mathf.Abs(pair.Value) >= 1e-8f)
                    .ToArray();
                if (values.Length == 0) continue;
                children.Add(new ChildMotion
                {
                    motion = graph.MultiSetter(
                        $"{name} from {VisemeReconstructionProfile.VisemeNames[viseme]}",
                        values),
                    directBlendParameter = weights[viseme],
                    timeScale = 1f
                });
            }
            children.Add(new ChildMotion
            {
                motion = graph.MultiSetter(
                    name + " safety zero",
                    ordered.Select(pair =>
                        new KeyValuePair<string, float>(pair.Value, 0f))),
                directBlendParameter = MathGraph.AlwaysOneParameter,
                timeScale = 1f
            });
            projection.children = children.ToArray();
            return projection;
        }

        private static void AddContractedBetaArticulationProjection(
            MathGraph graph,
            BlendTree root,
            Request request,
            AdvancedVisemeArticulatorGroup group,
            BetaCoarticulationGraph beta,
            IReadOnlyDictionary<AdvancedVisemeArticulator, string> fastOutputs,
            IReadOnlyDictionary<AdvancedVisemeArticulator, string> slowOutputs,
            string visemeIndex,
            string speechHistory,
            string silenceStability)
        {
            if (!beta.leads.TryGetValue(group, out var lead))
                throw new InvalidOperationException(
                    $"Missing Beta coarticulation lead for {group}.");

            var fastFrom = BuildVisemeMatrixProjectionMotion(
                graph, request, $"Corpus {group} fast source",
                beta.fast, fastOutputs);
            var fastTo = BuildVisemeMatrixProjectionMotion(
                graph, request, $"Corpus {group} raw source",
                beta.raw, fastOutputs);
            var fastRelease = graph.InterpolateMotions(
                fastFrom, fastTo, lead,
                $"Corpus {group} contracted fast");
            var fastFreeze = graph.CopyArticulationVector(
                fastOutputs, fastOutputs,
                $"Corpus {group} contracted fast freeze");
            graph.AddOperation(root, graph.SelectSilenceHoldMotion(
                fastRelease, fastRelease, fastFreeze,
                visemeIndex, speechHistory, silenceStability,
                $"Corpus {group} contracted fast transient-sil hold"));

            var slowFrom = BuildVisemeMatrixProjectionMotion(
                graph, request, $"Corpus {group} slow source",
                beta.slow, slowOutputs);
            var slowTo = BuildVisemeMatrixProjectionMotion(
                graph, request, $"Corpus {group} fast-to-slow source",
                beta.fast, slowOutputs);
            graph.AddOperation(root, graph.InterpolateMotions(
                slowFrom, slowTo, lead,
                $"Corpus {group} contracted slow"));
        }

        private static void AddElementwiseProductProjection(
            MathGraph graph,
            BlendTree root,
            string commonWeight,
            IReadOnlyList<string> inputs,
            IReadOnlyList<string> outputs,
            string name)
        {
            if (inputs == null || outputs == null || inputs.Count != outputs.Count)
                throw new InvalidOperationException(
                    $"{name} requires equally sized input and output vectors.");

            var vector = graph.Direct(name + " vector");
            var vectorChildren = new List<ChildMotion>();
            for (var i = 0; i < inputs.Count; i++)
            {
                vectorChildren.Add(new ChildMotion
                {
                    motion = graph.Setter(outputs[i], 1f),
                    directBlendParameter = inputs[i],
                    timeScale = 1f
                });
            }
            vectorChildren.Add(new ChildMotion
            {
                motion = graph.MultiSetter(
                    name + " vector safety zero",
                    outputs.Select(output =>
                        new KeyValuePair<string, float>(output, 0f))),
                directBlendParameter = MathGraph.AlwaysOneParameter,
                timeScale = 1f
            });
            vector.children = vectorChildren.ToArray();

            var product = graph.Direct(name);
            product.children = new[]
            {
                new ChildMotion
                {
                    motion = vector,
                    directBlendParameter = commonWeight,
                    timeScale = 1f
                },
                new ChildMotion
                {
                    motion = graph.MultiSetter(
                        name + " safety zero",
                        outputs.Select(output =>
                            new KeyValuePair<string, float>(output, 0f))),
                    directBlendParameter = MathGraph.AlwaysOneParameter,
                    timeScale = 1f
                }
            };
            graph.AddOperation(root, product);
        }

        internal static float[] GetAuthoredSpeechCoefficients(
            Request request,
            AdvancedVisemeArticulator articulator)
        {
            var values = new float[VisemeReconstructionProfile.VisemeCount];
            if (HasExternalPoseCalibration(request))
            {
                var axes = request.calibration.poseBasisAxes;
                var hasMatchingAxis = axes.Any(axis => axis.articulator == articulator);
                if (!hasMatchingAxis)
                {
                    for (var viseme = 0; viseme < values.Length; viseme++)
                        values[viseme] = request.profile.visemePoses[viseme].Get(articulator);
                    return values;
                }
                for (var viseme = 0; viseme < values.Length; viseme++)
                {
                    var value = 0f;
                    for (var axis = 0; axis < axes.Length; axis++)
                    {
                        if (axes[axis].articulator != articulator) continue;
                        value += Mathf.Sign(axes[axis].direction) *
                                 request.calibration.coefficients[viseme, axis];
                    }
                    values[viseme] = value;
                }
                return values;
            }

            var basisIndex = -1;
            if (request.calibration != null && request.calibration.success && request.calibrationBasis != null)
            {
                for (var i = 0; i < request.calibrationBasis.Count; i++)
                {
                    if (request.calibrationBasis[i].articulator == articulator) { basisIndex = i; break; }
                }
            }
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = basisIndex >= 0
                    ? request.calibration.coefficients[i, basisIndex]
                    : request.profile.visemePoses[i].Get(articulator);
            }
            return values;
        }

        internal static float[] GetAdjustedSpeechCoefficients(
            Request request,
            AdvancedVisemeArticulator articulator)
        {
            var values = GetAuthoredSpeechCoefficients(request, articulator);
            if (request?.profile == null) return values;
            for (var viseme = 0; viseme < values.Length; viseme++)
                values[viseme] *= request.profile.GetVisemeArticulationMultiplier(
                    viseme, articulator);
            return values;
        }

        private static bool HasExternalPoseCalibration(Request request)
        {
            return request?.calibration != null && request.calibration.success &&
                   request.calibration.poseBasisAxes != null &&
                   request.calibration.poseBasisAxes.Length > 0 &&
                   request.calibration.coefficients != null;
        }

        private static BetaCoarticulationGraph BuildBetaCoarticulationWeights(
            MathGraph graph,
            BlendTree root,
            string strength,
            string frameTime,
            IReadOnlyList<string> raw,
            IReadOnlyList<string> fast,
            IReadOnlyList<string> slow,
            string visemeIndex,
            string speechHistory,
            string silenceStability,
            bool materializeTongueSimplexes)
        {
            var output = new BetaCoarticulationGraph();
            var retentionParameters = new Dictionary<AdvancedVisemeArticulatorGroup, string>();
            for (var groupIndex = 0; groupIndex < AdvancedVisemeTransitionRetention.GroupCount; groupIndex++)
            {
                var group = (AdvancedVisemeArticulatorGroup)groupIndex;
                retentionParameters[group] = graph.Param($"BetaCoarticulation/Retention/{group}", 0f);
            }
            var groupsByDecay = retentionParameters.Keys
                .GroupBy(group => Mathf.RoundToInt(
                    AdvancedVisemeCoarticulationModel.DecaySeconds(group) * 1000000f))
                .OrderBy(grouping => grouping.Key);
            foreach (var decayGrouping in groupsByDecay)
            {
                var groups = decayGrouping.OrderBy(group => (int)group).ToArray();
                var decaySeconds = AdvancedVisemeCoarticulationModel.DecaySeconds(groups[0]);
                var contextAlpha = graph.Param(
                    $"BetaCoarticulation/Context/{decayGrouping.Key}/Alpha", 0.25f);
                graph.AddOperation(root, graph.AlphaFromDeltaTime(frameTime, contextAlpha, decaySeconds));
                var contextWeights = new string[VisemeReconstructionProfile.VisemeCount];
                for (var i = 0; i < contextWeights.Length; i++)
                    contextWeights[i] = graph.Param(
                        $"BetaCoarticulation/Context/{decayGrouping.Key}/{i}", i == 0 ? 1f : 0f);
                graph.AddOperation(root, graph.SmoothVectorUnlessHeldSilence(
                    raw, contextWeights, contextAlpha,
                    visemeIndex, speechHistory, silenceStability,
                    $"BetaCoarticulation context {decayGrouping.Key}"));

                // Exact staged tensor contraction:
                //   projected[g,c] = sum_p context[p] * R[g,p,c]
                //   retention[g]   = sum_c fast[c] * projected[g,c]
                //
                // The former graph expanded every (previous,current) pair into
                // 225 leaf clips per decay bucket. This factors the same full-rank
                // table without an SVD or approximation: fifteen context rows are
                // vector setters, followed by fifteen small destination vectors.
                var projected = groups.ToDictionary(
                    group => group,
                    group => Enumerable.Range(0, VisemeReconstructionProfile.VisemeCount)
                        .Select(current => graph.Param(
                            $"BetaCoarticulation/RetentionProjected/{group}/{current}", 0f))
                        .ToArray());
                var projectedValues = projected
                    .SelectMany(pair => pair.Value)
                    .ToArray();
                var contextProjection = graph.Direct(
                    $"Corpus context projection ({decaySeconds:0.###}s)");
                var contextChildren = new List<ChildMotion>();
                for (var previous = 0;
                     previous < VisemeReconstructionProfile.VisemeCount;
                     previous++)
                {
                    var values = new List<KeyValuePair<string, float>>();
                    foreach (var group in groups)
                    for (var current = 0;
                         current < VisemeReconstructionProfile.VisemeCount;
                         current++)
                        values.Add(new KeyValuePair<string, float>(
                            projected[group][current],
                            AdvancedVisemeCoarticulationModel.Retention(
                                group, previous, current)));
                    contextChildren.Add(new ChildMotion
                    {
                        motion = graph.MultiSetter(
                            "Corpus context row " +
                            VisemeReconstructionProfile.VisemeNames[previous],
                            values),
                        directBlendParameter = contextWeights[previous],
                        timeScale = 1f
                    });
                }
                contextChildren.Add(new ChildMotion
                {
                    motion = graph.MultiSetter(
                        "Corpus context projection safety zero",
                        projectedValues.Select(parameter =>
                            new KeyValuePair<string, float>(parameter, 0f))),
                    directBlendParameter = MathGraph.AlwaysOneParameter,
                    timeScale = 1f
                });
                contextProjection.children = contextChildren.ToArray();
                graph.AddOperation(root, contextProjection);

                var destinationContraction = graph.Direct(
                    $"Corpus destination contraction ({decaySeconds:0.###}s)");
                var destinationChildren = new List<ChildMotion>();
                for (var current = 0;
                     current < VisemeReconstructionProfile.VisemeCount;
                     current++)
                {
                    var vector = graph.Direct(
                        "Corpus projected destination " +
                        VisemeReconstructionProfile.VisemeNames[current]);
                    var vectorChildren = groups.Select(group => new ChildMotion
                    {
                        motion = graph.MultiSetter(
                            $"Projected {group} destination " +
                            VisemeReconstructionProfile.VisemeNames[current],
                            new[]
                            {
                                new KeyValuePair<string, float>(
                                    retentionParameters[group], 1f)
                            }),
                        directBlendParameter = projected[group][current],
                        timeScale = 1f
                    }).ToList();
                    vectorChildren.Add(new ChildMotion
                    {
                        motion = graph.MultiSetter(
                            "Projected destination safety zero",
                            groups.Select(group => new KeyValuePair<string, float>(
                                retentionParameters[group], 0f))),
                        directBlendParameter = MathGraph.AlwaysOneParameter,
                        timeScale = 1f
                    });
                    vector.children = vectorChildren.ToArray();
                    destinationChildren.Add(new ChildMotion
                    {
                        motion = vector,
                        directBlendParameter = fast[current],
                        timeScale = 1f
                    });
                }
                destinationChildren.Add(new ChildMotion
                {
                    motion = graph.MultiSetter(
                        "Corpus destination contraction safety zero",
                        groups.Select(group => new KeyValuePair<string, float>(
                            retentionParameters[group], 0f))),
                    directBlendParameter = MathGraph.AlwaysOneParameter,
                    timeScale = 1f
                });
                destinationContraction.children = destinationChildren.ToArray();
                graph.AddOperation(root, destinationContraction);
            }
            output.raw = raw;
            output.fast = fast;
            output.slow = slow;
            var groupLeads = BuildBetaLeads(
                graph, root, strength, retentionParameters, out var commonLead);
            output.common = BuildBetaStageWeights(
                graph, root, commonLead, "Mean", raw, fast, slow,
                visemeIndex, speechHistory, silenceStability);
            foreach (var pair in retentionParameters)
            {
                var lead = groupLeads[pair.Key];
                output.leads[pair.Key] = lead;
                // Face-conditioned inference observes only PP/nn for its nasal
                // correction. The visible-tongue regressor additionally needs the
                // complete TongueTip slow simplex. Materializing all four 15-wide
                // stage vectors made 39 coordinates that no consumer could read.
                if (!materializeTongueSimplexes ||
                    pair.Key != AdvancedVisemeArticulatorGroup.TongueTip &&
                    pair.Key != AdvancedVisemeArticulatorGroup.TongueBody)
                    continue;
                output.groups[pair.Key] = BuildBetaStageCoordinates(
                    graph, root, lead, pair.Key, raw, fast, slow,
                    visemeIndex, speechHistory, silenceStability);
            }
            return output;
        }

        private static Dictionary<AdvancedVisemeArticulatorGroup, string> BuildBetaLeads(
            MathGraph graph,
            BlendTree root,
            string strength,
            IReadOnlyDictionary<AdvancedVisemeArticulatorGroup, string> retentions,
            out string commonLead)
        {
            commonLead = graph.Param("BetaCoarticulation/Lead/Mean", 0f);
            var leads = retentions.Keys.ToDictionary(
                group => group,
                group => graph.Param($"BetaCoarticulation/Lead/{group}", 0f));
            var allLeads = leads.Values.Concat(new[] { commonLead }).ToArray();

            // All groups share one user strength. Evaluate
            //   lead_g = strength * (1 - retention_g)
            // and
            //   lead_mean = strength * (1 - mean_g retention_g)
            // as one nested vector motion. This removes five scalar remainder
            // parameters and an Animator-frame dependency without changing the
            // full-rank corpus table or its continuous contraction.
            var oneMinus = graph.Direct("Beta coarticulation one-minus retention vector");
            var oneMinusChildren = new List<ChildMotion>
            {
                new ChildMotion
                {
                    motion = graph.MultiSetter(
                        "Beta coarticulation lead unit vector",
                        allLeads.Select(lead =>
                            new KeyValuePair<string, float>(lead, 1f))),
                    directBlendParameter = MathGraph.AlwaysOneParameter,
                    timeScale = 1f
                }
            };
            foreach (var pair in retentions.OrderBy(pair => (int)pair.Key))
            {
                oneMinusChildren.Add(new ChildMotion
                {
                    motion = graph.MultiSetter(
                        $"Beta coarticulation {pair.Key} retention subtraction",
                        new[]
                        {
                            new KeyValuePair<string, float>(leads[pair.Key], -1f),
                            new KeyValuePair<string, float>(commonLead,
                                -1f / AdvancedVisemeTransitionRetention.GroupCount)
                        }),
                    directBlendParameter = pair.Value,
                    timeScale = 1f
                });
            }
            oneMinus.children = oneMinusChildren.ToArray();

            var scaled = graph.Direct("Beta coarticulation strength-scaled lead vector");
            scaled.children = new[]
            {
                new ChildMotion
                {
                    motion = oneMinus,
                    directBlendParameter = strength,
                    timeScale = 1f
                },
                new ChildMotion
                {
                    motion = graph.MultiSetter(
                        "Beta coarticulation lead safety zero",
                        allLeads.Select(lead =>
                            new KeyValuePair<string, float>(lead, 0f))),
                    directBlendParameter = MathGraph.AlwaysOneParameter,
                    timeScale = 1f
                }
            };
            graph.AddOperation(root, scaled);
            return leads;
        }

        private static BetaWeights BuildBetaStageWeights(
            MathGraph graph,
            BlendTree root,
            string lead,
            string key,
            IReadOnlyList<string> raw,
            IReadOnlyList<string> fast,
            IReadOnlyList<string> slow,
            string visemeIndex,
            string speechHistory,
            string silenceStability)
        {
            var output = new BetaWeights
            {
                fast = new string[VisemeReconstructionProfile.VisemeCount],
                slow = new string[VisemeReconstructionProfile.VisemeCount]
            };
            for (var i = 0; i < VisemeReconstructionProfile.VisemeCount; i++)
            {
                var defaultValue = i == 0 ? 1f : 0f;
                output.fast[i] = graph.Param($"BetaCoarticulation/{key}/Viseme/{i}/Fast", defaultValue);
                output.slow[i] = graph.Param($"BetaCoarticulation/{key}/Viseme/{i}/Slow", defaultValue);
            }
            graph.AddOperation(root, graph.InterpolateVectorUnlessHeldSilence(
                fast, raw, output.fast, lead,
                visemeIndex, speechHistory, silenceStability,
                $"BetaCoarticulation {key} fast"));
            graph.AddOperation(root, graph.InterpolateVector(
                slow, fast, output.slow, lead,
                $"BetaCoarticulation {key} slow"));
            return output;
        }

        private static BetaWeights BuildBetaStageCoordinates(
            MathGraph graph,
            BlendTree root,
            string lead,
            AdvancedVisemeArticulatorGroup group,
            IReadOnlyList<string> raw,
            IReadOnlyList<string> fast,
            IReadOnlyList<string> slow,
            string visemeIndex,
            string speechHistory,
            string silenceStability)
        {
            var key = group.ToString();
            var output = new BetaWeights
            {
                fast = new string[VisemeReconstructionProfile.VisemeCount],
                slow = new string[VisemeReconstructionProfile.VisemeCount]
            };
            var pairCoordinates = new[] { 1, 8 }; // PP and nn
            var slowCoordinates = group == AdvancedVisemeArticulatorGroup.TongueTip
                ? Enumerable.Range(0, VisemeReconstructionProfile.VisemeCount).ToArray()
                : pairCoordinates;

            foreach (var coordinate in pairCoordinates)
                output.fast[coordinate] = graph.Param(
                    $"BetaCoarticulation/{key}/Viseme/{coordinate}/Fast",
                    coordinate == 0 ? 1f : 0f);
            foreach (var coordinate in slowCoordinates)
                output.slow[coordinate] = graph.Param(
                    $"BetaCoarticulation/{key}/Viseme/{coordinate}/Slow",
                    coordinate == 0 ? 1f : 0f);

            graph.AddOperation(root, graph.InterpolateVectorUnlessHeldSilence(
                pairCoordinates.Select(index => fast[index]).ToArray(),
                pairCoordinates.Select(index => raw[index]).ToArray(),
                pairCoordinates.Select(index => output.fast[index]).ToArray(),
                lead, visemeIndex, speechHistory, silenceStability,
                $"BetaCoarticulation {key} observed fast"));
            graph.AddOperation(root, graph.InterpolateVector(
                slowCoordinates.Select(index => slow[index]).ToArray(),
                slowCoordinates.Select(index => fast[index]).ToArray(),
                slowCoordinates.Select(index => output.slow[index]).ToArray(),
                lead, $"BetaCoarticulation {key} observed slow"));
            return output;
        }

        private static void BuildMergedNasalCorrections(
            MathGraph graph,
            BlendTree root,
            BetaCoarticulationGraph beta,
            string mShareFast,
            string mShareSlow,
            string confidence,
            FacePhonePosteriorGraph output)
        {
            var groups = new[]
            {
                AdvancedVisemeArticulatorGroup.TongueTip,
                AdvancedVisemeArticulatorGroup.TongueBody
            };
            var shares = new[] { mShareFast, mShareSlow };
            var delta = new string[2, groups.Length];

            // Consumer-driven sum-product fusion. The former graph published
            // candidate, target, and raw-delta AAPs even though only the final
            // rank-one correction is observable:
            //   delta = confidence * (mShare * (PP + nn) - PP)
            // Keep the fast PP/nn evidence stateful (so transient silence still
            // freezes it), then evaluate this exact expression as two vectorized
            // nested motions with no intermediate Animator parameters.
            for (var stage = 0; stage < 2; stage++)
            {
                var stageName = stage == 0 ? "Fast" : "Slow";
                var outputs = new string[groups.Length];
                var sources = new BetaWeights[groups.Length];
                for (var groupIndex = 0; groupIndex < groups.Length; groupIndex++)
                {
                    var group = groups[groupIndex];
                    sources[groupIndex] = beta.groups[group];
                    outputs[groupIndex] = delta[stage, groupIndex] = graph.Param(
                        $"PhonePosterior/{group}/{stageName}/Delta", 0f);
                }

                var candidate = graph.Direct(
                    $"Hidden phone {stageName} PP-nn candidate vector");
                var candidateChildren = new List<ChildMotion>();
                for (var groupIndex = 0; groupIndex < groups.Length; groupIndex++)
                {
                    var source = stage == 0
                        ? sources[groupIndex].fast
                        : sources[groupIndex].slow;
                    candidateChildren.Add(new ChildMotion
                    {
                        motion = graph.Setter(outputs[groupIndex], 1f),
                        directBlendParameter = source[1],
                        timeScale = 1f
                    });
                    candidateChildren.Add(new ChildMotion
                    {
                        motion = graph.Setter(outputs[groupIndex], 1f),
                        directBlendParameter = source[8],
                        timeScale = 1f
                    });
                }
                candidateChildren.Add(new ChildMotion
                {
                    motion = graph.MultiSetter(
                        $"Hidden phone {stageName} candidate safety zero",
                        outputs.Select(parameter =>
                            new KeyValuePair<string, float>(parameter, 0f))),
                    directBlendParameter = MathGraph.AlwaysOneParameter,
                    timeScale = 1f
                });
                candidate.children = candidateChildren.ToArray();

                var centered = graph.Direct(
                    $"Hidden phone {stageName} posterior-centered vector");
                var centeredChildren = new List<ChildMotion>
                {
                    new ChildMotion
                    {
                        motion = candidate,
                        directBlendParameter = shares[stage],
                        timeScale = 1f
                    }
                };
                for (var groupIndex = 0; groupIndex < groups.Length; groupIndex++)
                {
                    var source = stage == 0
                        ? sources[groupIndex].fast
                        : sources[groupIndex].slow;
                    centeredChildren.Add(new ChildMotion
                    {
                        motion = graph.Setter(outputs[groupIndex], -1f),
                        directBlendParameter = source[1],
                        timeScale = 1f
                    });
                }
                centeredChildren.Add(new ChildMotion
                {
                    motion = graph.MultiSetter(
                        $"Hidden phone {stageName} centered safety zero",
                        outputs.Select(parameter =>
                            new KeyValuePair<string, float>(parameter, 0f))),
                    directBlendParameter = MathGraph.AlwaysOneParameter,
                    timeScale = 1f
                });
                centered.children = centeredChildren.ToArray();

                var weighted = graph.Direct(
                    $"Hidden phone {stageName} confidence-weighted vector");
                weighted.children = new[]
                {
                    new ChildMotion
                    {
                        motion = centered,
                        directBlendParameter = confidence,
                        timeScale = 1f
                    },
                    new ChildMotion
                    {
                        motion = graph.MultiSetter(
                            $"Hidden phone {stageName} correction safety zero",
                            outputs.Select(parameter =>
                                new KeyValuePair<string, float>(parameter, 0f))),
                        directBlendParameter = MathGraph.AlwaysOneParameter,
                        timeScale = 1f
                    }
                };
                graph.AddOperation(root, weighted);
            }

            for (var groupIndex = 0; groupIndex < groups.Length; groupIndex++)
                output.corrections[groups[groupIndex]] = new BetaNasalCorrection
                {
                    fast = delta[0, groupIndex],
                    slow = delta[1, groupIndex]
                };

        }

        private static void RebuildConditionedTongueSpeech(
            MathGraph graph,
            BlendTree root,
            Request request,
            FacePhonePosteriorGraph posterior,
            string speechGain,
            IDictionary<AdvancedVisemeArticulator, string> speechFast,
            IDictionary<AdvancedVisemeArticulator, string> speechSlow)
        {
            var entries = new List<(AdvancedVisemeArticulator articulator, bool slow,
                string source, string output)>();
            foreach (var articulator in SynthesizedArticulators())
            {
                var group = AdvancedVisemeCoarticulationModel.GroupFor(articulator);
                if (!posterior.corrections.ContainsKey(group) ||
                    !speechFast.ContainsKey(articulator) ||
                    !speechSlow.ContainsKey(articulator)) continue;

                var conditionedFast = graph.Param(
                    $"PhonePosterior/Articulation/{articulator}/Fast", 0f);
                var conditionedSlow = graph.Param(
                    $"PhonePosterior/Articulation/{articulator}/Slow", 0f);
                entries.Add((articulator, false, speechFast[articulator], conditionedFast));
                entries.Add((articulator, true, speechSlow[articulator], conditionedSlow));
            }
            if (entries.Count == 0) return;

            var groups = new[]
            {
                AdvancedVisemeArticulatorGroup.TongueTip,
                AdvancedVisemeArticulatorGroup.TongueBody
            };
            var correctionInputs = new string[1, groups.Length * 2];
            var scaledCorrections = new string[1, groups.Length * 2];
            for (var stage = 0; stage < 2; stage++)
            for (var groupIndex = 0; groupIndex < groups.Length; groupIndex++)
            {
                var index = stage * groups.Length + groupIndex;
                var correction = posterior.corrections[groups[groupIndex]];
                correctionInputs[0, index] = stage == 0 ? correction.fast : correction.slow;
                scaledCorrections[0, index] = graph.Param(
                    $"PhonePosterior/Articulation/{groups[groupIndex]}/" +
                    $"{(stage == 0 ? "Fast" : "Slow")}/ScaledDelta", 0f);
            }
            graph.AddOperation(root, graph.GroupedElementwiseProducts(
                new[] { speechGain }, correctionInputs, scaledCorrections,
                "Hidden phone speech-scaled rank-one deltas"));

            var projectionInputs = entries.Select(entry => entry.source).ToList();
            var flatScaledCorrections = new List<string>();
            for (var stage = 0; stage < 2; stage++)
            for (var groupIndex = 0; groupIndex < groups.Length; groupIndex++)
                flatScaledCorrections.Add(scaledCorrections[0, stage * groups.Length + groupIndex]);
            projectionInputs.AddRange(flatScaledCorrections);
            graph.AddOperation(root, graph.SignedMatrixProjection(
                projectionInputs,
                entries.Select(entry => entry.output).ToArray(),
                new float[entries.Count],
                (input, outputIndex) =>
                {
                    if (input < entries.Count) return input == outputIndex ? 1f : 0f;
                    var correctionIndex = input - entries.Count;
                    var correctionStage = correctionIndex / groups.Length;
                    var correctionGroup = groups[correctionIndex % groups.Length];
                    var entry = entries[outputIndex];
                    if ((entry.slow ? 1 : 0) != correctionStage ||
                        AdvancedVisemeCoarticulationModel.GroupFor(entry.articulator) !=
                        correctionGroup) return 0f;
                    var coefficients = GetAdjustedSpeechCoefficients(
                        request, entry.articulator);
                    return coefficients[1] - coefficients[8];
                },
                "Hidden phone rank-one tongue articulation correction"));

            foreach (var entry in entries)
            {
                if (entry.slow) speechSlow[entry.articulator] = entry.output;
                else speechFast[entry.articulator] = entry.output;
            }
        }

        private static void BuildOutputTree(
            Request request,
            Result result,
            MathGraph graph,
            BlendTree outputRoot,
            string[] speechWeights,
            string[] visibleSpeechWeights,
            string hiddenResidualSpeechDelta,
            string authoredDetail,
            string trackedSurfaceYield,
            IReadOnlyDictionary<AdvancedVisemeArticulator, string> trackingGains)
        {
            var hasCalibration = request.calibration != null && request.calibration.success;
            var externalPoseCalibration = HasExternalPoseCalibration(request);
            var anyVisemeOverride = request.profile.visemePoses.Any(p => p != null && p.animationOverride != null);
            var anyArticulatorOverride = request.profile.articulatorBindings.Any(binding =>
                binding != null &&
                (binding.animationOverride != null || binding.negativeAnimationOverride != null));
            // Reused templates can contain a rig-specific linear mouth basis. Keep
            // that basis in the authoritative final layer instead of selecting a
            // separately auto-mapped calibration basis that may animate different
            // properties and leave the lower template visible.
            var useResiduals = hasCalibration && !anyVisemeOverride && !anyArticulatorOverride &&
                               (!request.reuseExistingTracking || externalPoseCalibration);

            if (request.trackingEnabled && request.reuseExistingTracking)
                ValidateReusedTrackingPoses(request, result);

            if (useResiduals)
            {
                if (externalPoseCalibration)
                {
                    BuildExternalCalibratedBasisOutput(
                        request, result, graph, outputRoot, speechWeights);
                }
                else
                {
                    foreach (var pair in result.articulationParameters)
                    {
                        if (!request.resolvedBlendShapes.TryGetValue(pair.Key, out var shape) ||
                            string.IsNullOrEmpty(shape)) continue;
                        var positive = graph.BlendShapeClip(
                            request.rendererPath, shape, 100f);
                        Motion negative = null;
                        if (IsSigned(pair.Key))
                        {
                            var negativeShape = NegativeBasisShapeFor(
                                request.calibration, pair.Key, 1);
                            if (string.IsNullOrEmpty(negativeShape))
                                throw new InvalidOperationException(
                                    $"Calibrated signed articulator '{pair.Key}' has no " +
                                    "build-only inverse basis shape.");
                            negative = graph.BlendShapeClip(
                                request.rendererPath, negativeShape, 100f);
                        }
                        graph.AddOperation(outputRoot, graph.DrivePose(
                            pair.Value, positive, negative, IsSigned(pair.Key)));
                    }
                }

                // The neutral calibrated identity is Vp = U(Cp) + Rp. A
                // per-viseme trim replaces C with C⊙M while deliberately leaving
                // complementary residual detail R untouched. R is always driven
                // in full first. Its measured component is then removed as the
                // low-rank basis correction U*diag(g)*A^T*p. This is exact at
                // tracking-off, yields independently per measured coordinate, and
                // needs at most two nonnegative ±geometry carriers per basis ray
                // instead of one conflict morph per viseme.
                var residualWeights = new string[speechWeights.Length];
                for (var i = 0; i < speechWeights.Length; i++)
                {
                    residualWeights[i] = graph.Param($"Viseme/{i}/ResidualWeight", 0f);
                }
                AddElementwiseProductProjection(
                    graph, outputRoot, authoredDetail,
                    speechWeights, residualWeights,
                    "Authored residual simplex");
                for (var i = 0; i < speechWeights.Length; i++)
                {
                    var curves = new List<(string path, string blendShape, float value)>();
                    var residualName = request.calibration.residualBlendShapeNames[i];
                    if (!string.IsNullOrEmpty(residualName))
                        curves.Add((request.rendererPath, residualName, 100f));
                    if (request.linkedRendererOutputs != null)
                        foreach (var linked in request.linkedRendererOutputs)
                        {
                            var names = linked?.calibration?.residualBlendShapeNames;
                            var linkedName = names != null && i < names.Length
                                ? names[i]
                                : null;
                            if (linked?.calibration == null ||
                                !linked.calibration.success ||
                                linked.rendererPath == null ||
                                string.IsNullOrEmpty(linkedName)) continue;
                            curves.Add((linked.rendererPath, linkedName, 100f));
                        }
                    var residualPose = graph.CompositeBlendShapeClip(
                        "Composite residual " +
                        VisemeReconstructionProfile.VisemeNames[i], curves);
                    if (residualPose != null)
                        graph.AddOperation(outputRoot, graph.DrivePose(
                            residualWeights[i], residualPose, false));
                }

                var sharedOwnershipGains = new Dictionary<AdvancedVisemeArticulator, string>();
                BuildLowRankOwnershipCorrection(
                    request.calibration,
                    request.rendererPath,
                    "Primary",
                    graph,
                    outputRoot,
                    residualWeights,
                    trackedSurfaceYield,
                    trackingGains,
                    sharedOwnershipGains);

                if (!string.IsNullOrEmpty(hiddenResidualSpeechDelta) &&
                    !string.IsNullOrEmpty(request.calibration.hiddenPhoneResidualBlendShapeName) &&
                    !string.IsNullOrEmpty(
                        request.calibration.hiddenPhoneResidualNegativeBlendShapeName))
                {
                    graph.AddOperation(outputRoot, graph.DrivePose(
                        hiddenResidualSpeechDelta,
                        graph.BlendShapeClip(
                            request.rendererPath,
                            request.calibration.hiddenPhoneResidualBlendShapeName,
                            100f),
                        graph.BlendShapeClip(
                            request.rendererPath,
                            request.calibration.hiddenPhoneResidualNegativeBlendShapeName,
                            100f),
                        true));
                }

                BuildLinkedRendererResidualOutputs(
                    request, result, graph, outputRoot, residualWeights,
                    hiddenResidualSpeechDelta, trackedSurfaceYield,
                    trackingGains, sharedOwnershipGains);
            }
            else
            {
                for (var i = 0; i < visibleSpeechWeights.Length; i++)
                {
                    var overrideClip = request.profile.visemePoses[i].animationOverride;
                    var clip = overrideClip != null
                        ? graph.PoseClip(overrideClip, "Viseme " + VisemeReconstructionProfile.VisemeNames[i])
                        : graph.BlendShapeClip(request.rendererPath, request.sourceVisemeBlendShapes[i], 100f);
                    graph.AddOperation(outputRoot,
                        graph.DrivePose(visibleSpeechWeights[i], clip, false));
                }

                if (request.trackingEnabled)
                {
                    if (!request.reuseExistingTracking)
                    {
                        foreach (var pair in result.trackingContributionParameters)
                        {
                            var binding = request.profile.FindBinding(pair.Key);
                            Motion positive = null;
                            Motion negative = null;
                            if (binding != null && binding.animationOverride != null)
                            {
                                positive = graph.PoseClip(binding.animationOverride,
                                    "Articulation " + pair.Key);
                                if (binding.negativeAnimationOverride != null)
                                    negative = graph.PoseClip(binding.negativeAnimationOverride,
                                        "Articulation " + pair.Key + " Negative");
                            }
                            else if (request.resolvedBlendShapes.TryGetValue(pair.Key, out var shape) &&
                                     !string.IsNullOrEmpty(shape))
                            {
                                positive = graph.BlendShapeClip(request.rendererPath, shape, 100f);
                            }
                            if (positive != null || negative != null)
                                graph.AddOperation(outputRoot,
                                    graph.DrivePose(pair.Value, positive, negative, IsSigned(pair.Key)));
                        }
                    }
                }

                // Direct viseme clips contain coupled lower-face poses. Each one
                // has already yielded only to measurements in its own visible
                // support. Reconstruct the exact faded speech coordinate here,
                // then correct every available linear basis to the final fused
                // result. Beta needs the same correction even without tracking
                // because its articulator groups follow different trajectories.
                if (ShouldBuildFallbackArticulationCorrection(request))
                {
                    foreach (var pair in result.articulationParameters)
                    {
                        if (!result.speechArticulationParameters.ContainsKey(pair.Key)) continue;
                        var tongueTuningOnly = !request.trackingEnabled &&
                                               request.component.reconstructionMode ==
                                               AdvancedVisemeReconstructionMode.Normal;
                        if (tongueTuningOnly &&
                            !IsTunableTongueArticulator(pair.Key) &&
                            !request.profile.HasNonNeutralArticulationAdjustment(pair.Key))
                            continue;
                        var signed = IsSigned(pair.Key);
                        var speechBase = graph.Param($"Fallback/{pair.Key}/SpeechBase", 0f);
                        graph.AddOperation(outputRoot, graph.Linear(
                            speechBase, BuildVisemeTerms(
                                visibleSpeechWeights,
                                GetAuthoredSpeechCoefficients(request, pair.Key), signed)));
                        string trackingContribution = null;
                        if (ShouldSubtractGeneratedTrackingContribution(request.reuseExistingTracking) &&
                            result.trackingContributionParameters.TryGetValue(
                                pair.Key, out var generatedTrackingContribution))
                            trackingContribution = generatedTrackingContribution;

                        Motion positive = null;
                        Motion negative = null;
                        if (request.reuseExistingTracking && request.externalPoses != null &&
                            request.externalPoses.TryGetValue(pair.Key, out var external))
                        {
                            positive = graph.TargetRendererBlendShapePose(external.positive,
                                "Correction " + pair.Key, request.rendererPath, request.targetMesh);
                            negative = graph.TargetRendererBlendShapePose(external.negative,
                                "Correction " + pair.Key + " Negative", request.rendererPath, request.targetMesh);
                        }
                        if (positive == null && negative == null)
                        {
                            var binding = request.profile.FindBinding(pair.Key);
                            if (binding != null && binding.animationOverride != null)
                            {
                                // Corrections may be negative. Filter overrides down
                                // to target-renderer blendshape deltas so absolute
                                // transforms, materials, and other non-linear curves
                                // are never treated as an invertible linear basis.
                                positive = graph.TargetRendererBlendShapePose(
                                    binding.animationOverride, "Correction " + pair.Key,
                                    request.rendererPath, request.targetMesh);
                                if (binding.negativeAnimationOverride != null)
                                    negative = graph.TargetRendererBlendShapePose(
                                        binding.negativeAnimationOverride,
                                        "Correction " + pair.Key + " Negative",
                                        request.rendererPath, request.targetMesh);
                            }
                            if (positive == null && negative == null &&
                                request.resolvedBlendShapes.TryGetValue(pair.Key, out var shape) &&
                                !string.IsNullOrEmpty(shape))
                            {
                                positive = graph.BlendShapeClip(request.rendererPath, shape, 100f);
                            }
                        }
                        if (positive == null && negative == null) continue;

                        // A signed coordinate with distinct positive/negative
                        // poses is a pair of rays, not one linear axis. Subtract
                        // each ray independently so crossing Smile->Sad (or a
                        // lateral axis) cannot leave both shapes visible.
                        if (signed && positive != null && negative != null)
                        {
                            AddSignedRayCorrection(
                                graph, outputRoot, pair.Key.ToString(), pair.Value,
                                speechBase, trackingContribution, positive, negative);
                            continue;
                        }

                        var terms = new List<Term>
                        {
                            Term.For(pair.Value, 1f, signed),
                            Term.For(speechBase, -1f, signed)
                        };
                        // Fresh inputs are driven above as g*f and must be removed
                        // from this correction. Reused tracking is already present
                        // only in a lower Override layer; the later generated layer
                        // replaces it, so this correction supplies the complete
                        // authoritative final-minus-speech-basis pose.
                        if (!string.IsNullOrEmpty(trackingContribution))
                            terms.Add(Term.For(trackingContribution, -1f, signed));
                        var correction = graph.Param($"Fallback/{pair.Key}/ArticulationCorrection", 0f);
                        graph.AddOperation(outputRoot, graph.Linear(correction, terms));
                        graph.AddOperation(outputRoot,
                            graph.DrivePose(correction, positive, negative, true));
                    }
                }
            }
        }

        private static void BuildLinkedRendererResidualOutputs(
            Request request,
            Result result,
            MathGraph graph,
            BlendTree outputRoot,
            string[] residualWeights,
            string hiddenResidualSpeechDelta,
            string trackedSurfaceYield,
            IReadOnlyDictionary<AdvancedVisemeArticulator, string> trackingGains,
            IDictionary<AdvancedVisemeArticulator, string> sharedOwnershipGains)
        {
            if (request.linkedRendererOutputs == null ||
                request.linkedRendererOutputs.Count == 0) return;

            for (var linkedIndex = 0;
                 linkedIndex < request.linkedRendererOutputs.Count;
                 linkedIndex++)
            {
                var linked = request.linkedRendererOutputs[linkedIndex];
                var calibration = linked?.calibration;
                if (calibration == null || !calibration.success ||
                    linked.rendererPath == null) continue;

                var key = $"LinkedRenderer/{linkedIndex}";
                BuildLowRankOwnershipCorrection(
                    calibration,
                    linked.rendererPath,
                    key,
                    graph,
                    outputRoot,
                    residualWeights,
                    trackedSurfaceYield,
                    trackingGains,
                    sharedOwnershipGains);

                // VRCFury copies the original positive basis curves. A signed
                // negative coordinate cannot be copied as a negative weight,
                // because VRChat clamps the target shape at zero. Drive the
                // target-local -U basis clone with a nonnegative magnitude.
                if (!HasExternalPoseCalibration(request))
                {
                    var basisNames = calibration.basisNegativeBlendShapeNames;
                    var articulators = calibration.basisArticulators;
                    var directions = calibration.basisDirections;
                    if (basisNames != null && articulators != null)
                    {
                        for (var column = 0; column < basisNames.Length; column++)
                        {
                            if (string.IsNullOrEmpty(basisNames[column]) ||
                                column >= articulators.Length ||
                                !IsSigned(articulators[column]) ||
                                directions != null && column < directions.Length &&
                                directions[column] < 0 ||
                                !result.articulationParameters.TryGetValue(
                                    articulators[column], out var articulation))
                                continue;
                            SplitSignedMagnitude(
                                graph, outputRoot,
                                $"{key}/Basis/{column}/Signed",
                                articulation, out _, out var negativeMagnitude);
                            graph.AddOperation(outputRoot, graph.DrivePose(
                                negativeMagnitude,
                                graph.BlendShapeClip(
                                    linked.rendererPath, basisNames[column], 100f),
                                false));
                        }
                    }
                }

                if (string.IsNullOrEmpty(hiddenResidualSpeechDelta) ||
                    string.IsNullOrEmpty(
                        calibration.hiddenPhoneResidualBlendShapeName) ||
                    string.IsNullOrEmpty(
                        calibration.hiddenPhoneResidualNegativeBlendShapeName)) continue;
                graph.AddOperation(outputRoot, graph.DrivePose(
                    hiddenResidualSpeechDelta,
                    graph.BlendShapeClip(
                        linked.rendererPath,
                        calibration.hiddenPhoneResidualBlendShapeName,
                        100f),
                    graph.BlendShapeClip(
                        linked.rendererPath,
                        calibration.hiddenPhoneResidualNegativeBlendShapeName,
                        100f),
                    true));
            }
        }

        private static string NegativeBasisShapeFor(
            AdvancedVisemeMeshCalibrator.Result calibration,
            AdvancedVisemeArticulator articulator,
            int direction)
        {
            var names = calibration?.basisNegativeBlendShapeNames;
            var articulators = calibration?.basisArticulators;
            var directions = calibration?.basisDirections;
            if (names == null || articulators == null) return null;
            for (var column = 0; column < names.Length &&
                                 column < articulators.Length; column++)
            {
                if (articulators[column] != articulator ||
                    directions != null && column < directions.Length &&
                    Math.Sign(directions[column]) != Math.Sign(direction))
                    continue;
                if (!string.IsNullOrEmpty(names[column])) return names[column];
            }
            return null;
        }

        private static void BuildLowRankOwnershipCorrection(
            AdvancedVisemeMeshCalibrator.Result calibration,
            string rendererPath,
            string key,
            MathGraph graph,
            BlendTree outputRoot,
            IReadOnlyList<string> residualWeights,
            string trackedSurfaceYield,
            IReadOnlyDictionary<AdvancedVisemeArticulator, string> trackingGains,
            IDictionary<AdvancedVisemeArticulator, string> sharedOwnershipGains)
        {
            var coefficients = calibration?.ownershipProjectionCoefficients;
            var positiveCarriers = calibration?.ownershipCarrierBlendShapeNames;
            var positiveCarrierScales = calibration?.ownershipCarrierScales;
            var negativeCarriers = calibration?.ownershipNegativeCarrierBlendShapeNames;
            var negativeCarrierScales = calibration?.ownershipNegativeCarrierScales;
            var nonZeroSelectedColumns = calibration?.ownershipNonZeroSelectedColumns;
            var authorityGroups = calibration?.ownershipAuthorityGroups;
            var articulators = calibration?.basisArticulators;
            if (coefficients == null || positiveCarriers == null ||
                positiveCarrierScales == null || negativeCarriers == null ||
                negativeCarrierScales == null ||
                articulators == null ||
                coefficients.GetLength(0) != residualWeights.Count ||
                coefficients.GetLength(1) != positiveCarriers.Length ||
                positiveCarrierScales.Length != positiveCarriers.Length ||
                negativeCarriers.Length != positiveCarriers.Length ||
                negativeCarrierScales.Length != positiveCarriers.Length ||
                articulators.Length != positiveCarriers.Length || rendererPath == null)
                return;

            var dependencyGains = new Dictionary<string, string>(StringComparer.Ordinal);
            var carrierProjections = new List<OwnershipCarrierProjection>();

            for (var column = 0; column < positiveCarriers.Length; column++)
            {
                if (string.IsNullOrEmpty(positiveCarriers[column]) &&
                    string.IsNullOrEmpty(negativeCarriers[column])) continue;
                var articulator = articulators[column];
                if (!trackingGains.TryGetValue(articulator, out var trackingGain)) continue;

                var participantColumns = authorityGroups != null &&
                                         authorityGroups.GetLength(0) == articulators.Length &&
                                         authorityGroups.GetLength(1) == articulators.Length
                    ? Enumerable.Range(0, articulators.Length)
                        .Where(candidate => authorityGroups[column, candidate])
                        .ToArray()
                    : calibration.ownershipBasisRankDeficient
                        ? Enumerable.Range(0, articulators.Length)
                            .Where(candidate => nonZeroSelectedColumns != null &&
                                                candidate < nonZeroSelectedColumns.Length &&
                                                nonZeroSelectedColumns[candidate])
                            .ToArray()
                        : new[] { column };
                if (participantColumns.Length == 0) participantColumns = new[] { column };
                var participantArticulators = participantColumns
                    .Select(candidate => articulators[candidate])
                    .Distinct()
                    .OrderBy(candidate => (int)candidate)
                    .ToArray();
                if (participantArticulators.Any(candidate =>
                        !trackingGains.ContainsKey(candidate)))
                    continue;

                string effectiveGain;
                if (participantArticulators.Length > 1)
                {
                    var signature = string.Join("_", participantArticulators
                        .Select(candidate => ((int)candidate).ToString()));
                    if (!dependencyGains.TryGetValue(signature, out effectiveGain))
                    {
                        var conservative = MinParameters(
                            graph, outputRoot,
                            $"{key}/Dependency/{signature}/Authority",
                            participantArticulators
                                .Select(candidate => trackingGains[candidate])
                                .ToArray());
                        effectiveGain = graph.Param(
                            $"{key}/Dependency/{signature}/Yield", 0f);
                        graph.AddOperation(outputRoot, graph.Multiply(
                            conservative, trackedSurfaceYield,
                            effectiveGain, false));
                        dependencyGains[signature] = effectiveGain;
                    }
                }
                else
                {
                    if (!sharedOwnershipGains.TryGetValue(articulator, out effectiveGain))
                    {
                        effectiveGain = graph.Param(
                            $"Residual/Ownership/{articulator}/Yield", 0f);
                        graph.AddOperation(outputRoot, graph.Multiply(
                            trackingGain, trackedSurfaceYield,
                            effectiveGain, false));
                        sharedOwnershipGains[articulator] = effectiveGain;
                    }
                }

                AddOwnershipCarrierProjection(
                    carrierProjections, residualWeights, coefficients, column,
                    positiveCarriers[column], positiveCarrierScales[column],
                    effectiveGain, $"{key}/Ownership/{column}/Add",
                    coefficient => coefficient < 0f ? -coefficient : 0f);
                AddOwnershipCarrierProjection(
                    carrierProjections, residualWeights, coefficients, column,
                    negativeCarriers[column], negativeCarrierScales[column],
                    effectiveGain, $"{key}/Ownership/{column}/Subtract",
                    coefficient => coefficient > 0f ? coefficient : 0f);
            }

            BuildOwnershipCarrierProjection(
                graph, outputRoot, residualWeights, carrierProjections,
                rendererPath, key);
        }

        private sealed class OwnershipCarrierProjection
        {
            public string carrier;
            public string effectiveGain;
            public string key;
            public float[] coefficients;
        }

        private static void AddOwnershipCarrierProjection(
            ICollection<OwnershipCarrierProjection> projections,
            IReadOnlyList<string> residualWeights,
            float[,] coefficients,
            int column,
            string carrier,
            float carrierScale,
            string effectiveGain,
            string key,
            Func<float, float> contributionMagnitude)
        {
            if (string.IsNullOrEmpty(carrier) ||
                float.IsNaN(carrierScale) || float.IsInfinity(carrierScale) ||
                carrierScale <= 1e-7f) return;

            var projectedCoefficients = new float[residualWeights.Count];
            var any = false;
            for (var viseme = 0; viseme < residualWeights.Count; viseme++)
            {
                var magnitude = contributionMagnitude(coefficients[viseme, column]);
                if (magnitude <= 1e-7f) continue;
                projectedCoefficients[viseme] = magnitude / carrierScale;
                any = true;
            }
            if (!any) return;

            projections.Add(new OwnershipCarrierProjection
            {
                carrier = carrier,
                effectiveGain = effectiveGain,
                key = key,
                coefficients = projectedCoefficients
            });
        }

        private static void BuildOwnershipCarrierProjection(
            MathGraph graph,
            BlendTree outputRoot,
            IReadOnlyList<string> residualWeights,
            IReadOnlyList<OwnershipCarrierProjection> projections,
            string rendererPath,
            string key)
        {
            if (projections == null || projections.Count == 0) return;

            var projected = projections
                .Select(projection => graph.Param(projection.key + "/Projected", 0f))
                .ToArray();
            var matrix = graph.Direct(key + " ownership matrix projection");
            var children = new List<ChildMotion>(residualWeights.Count + 1);
            for (var viseme = 0; viseme < residualWeights.Count; viseme++)
            {
                var values = projections.Select((projection, index) =>
                    new KeyValuePair<string, float>(
                        projected[index], projection.coefficients[viseme]));
                children.Add(new ChildMotion
                {
                    motion = graph.MultiSetter(
                        $"{key} ownership from " +
                        VisemeReconstructionProfile.VisemeNames[viseme],
                        values),
                    directBlendParameter = residualWeights[viseme],
                    timeScale = 1f
                });
            }
            children.Add(new ChildMotion
            {
                motion = graph.MultiSetter(
                    key + " ownership safety zero",
                    projected.Select(parameter =>
                        new KeyValuePair<string, float>(parameter, 0f))),
                directBlendParameter = MathGraph.AlwaysOneParameter,
                timeScale = 1f
            });
            matrix.children = children.ToArray();
            graph.AddOperation(outputRoot, matrix);

            for (var index = 0; index < projections.Count; index++)
                graph.AddOperation(outputRoot, graph.DrivePoseProduct(
                    projections[index].effectiveGain,
                    projected[index],
                    graph.BlendShapeClip(
                        rendererPath, projections[index].carrier, 100f),
                    projections[index].key));
        }

        private static void BuildExternalCalibratedBasisOutput(
            Request request,
            Result result,
            MathGraph graph,
            BlendTree outputRoot,
            string[] speechWeights)
        {
            var axes = request.calibration.poseBasisAxes;
            var indexedAxes = axes
                .Select((axis, index) =>
                    new KeyValuePair<int, AdvancedVisemeMeshCalibrator.PoseBasisAxis>(index, axis))
                .GroupBy(pair => pair.Value.articulator);

            foreach (var group in indexedAxes)
            {
                var articulator = group.Key;
                if (!result.articulationParameters.TryGetValue(articulator, out var finalArticulation))
                    continue;

                var positiveAxes = group.Where(pair => pair.Value.direction > 0).ToArray();
                var negativeAxes = group.Where(pair => pair.Value.direction < 0).ToArray();
                if (!IsSigned(articulator) && negativeAxes.Length > 0)
                    throw new InvalidOperationException(
                        $"Calibrated external articulator '{articulator}' has a negative pose ray, " +
                        "but the articulator is unsigned.");
                if (positiveAxes.Length > 1 || negativeAxes.Length > 1)
                    throw new InvalidOperationException(
                        $"Calibrated external articulator '{articulator}' contains multiple " +
                        "pose rays in the same direction. Clamp-safe ownership requires at most " +
                        "one positive and one negative endpoint per channel.");

                var poses = new Dictionary<int, Motion>();
                foreach (var pair in group)
                {
                    var axis = pair.Value;
                    if (!IsEntireLinearCorrectionClip(axis.clip, axis.rendererPath, request.targetMesh))
                        throw new InvalidOperationException(
                            $"Calibrated external pose '{axis.clip?.name}' for '{articulator}' is no longer " +
                            "a complete target-face blendshape pose. Rebuild the avatar calibration.");
                    var pose = graph.TargetRendererBlendShapePose(
                        axis.clip,
                        $"Calibrated {articulator} {(axis.direction > 0 ? "Positive" : "Negative")}",
                        axis.rendererPath,
                        request.targetMesh);
                    if (pose == null)
                        throw new InvalidOperationException(
                            $"Calibrated external pose '{axis.clip?.name}' for '{articulator}' has no driveable curves.");
                    poses[pair.Key] = pose;
                }

                // A rig-connected controller-only proxy is already the value the
                // template uses to render this exact pose. Let that parameter
                // reach the mesh atomically while tracking is active. The legacy
                // observer/fusion value remains the tracking-off speech fallback
                // and public diagnostic, but it is no longer read back through a
                // long animated-parameter pipeline to render local face motion.
                if (request.reuseExistingTracking &&
                    request.directPoseArticulators != null &&
                    request.directPoseArticulators.Contains(articulator) &&
                    request.externalPoses != null &&
                    request.externalPoses.TryGetValue(articulator, out var externalPose) &&
                    result.trackingGainParameters.TryGetValue(
                        articulator, out var directTrackingGain) &&
                    TryResolveTrackingParameter(
                        request, articulator,
                        request.profile.FindBinding(articulator),
                        out var directTrackingParameter))
                {
                    var positive = positiveAxes.Length == 1
                        ? poses[positiveAxes[0].Key]
                        : null;
                    var negative = negativeAxes.Length == 1
                        ? poses[negativeAxes[0].Key]
                        : null;
                    // Calibrated template rays may deliberately be one-sided
                    // (JawForward/JawZ is a common example). In that case the
                    // missing direction means neutral geometry, not an inverse
                    // blendshape. Use the same explicit ray sampler for the
                    // fused fallback so a negative value never becomes a
                    // negative final blendshape weight.
                    var fallback = graph.DrivePoseAtThresholds(
                        finalArticulation, positive, negative,
                        1f, -1f, IsSigned(articulator));
                    var native = graph.DrivePoseAtThresholds(
                        directTrackingParameter, positive, negative,
                        externalPose.positiveThreshold,
                        externalPose.negativeThreshold,
                        IsSigned(articulator));
                    Motion selected = graph.SelectMotion(
                        directTrackingGain,
                        fallback, native,
                        $"Native {articulator} tracking gate");
                    graph.AddOperation(outputRoot, selected);
                    continue;
                }

                // The normal tailored-template case has one non-negative positive
                // ray for an unsigned coordinate, so its fused coordinate is
                // already the exact coefficient required by that pose.
                if (!IsSigned(articulator) && positiveAxes.Length == 1)
                {
                    graph.AddOperation(outputRoot,
                        graph.DrivePose(finalArticulation, poses[positiveAxes[0].Key], false));
                    continue;
                }

                BuildExternalCalibratedRays(
                    request, result, graph, outputRoot, speechWeights,
                    articulator, finalArticulation, positiveAxes, negativeAxes, poses);
            }
        }

        private static void BuildExternalCalibratedRays(
            Request request,
            Result result,
            MathGraph graph,
            BlendTree outputRoot,
            string[] speechWeights,
            AdvancedVisemeArticulator articulator,
            string finalArticulation,
            IReadOnlyList<KeyValuePair<int, AdvancedVisemeMeshCalibrator.PoseBasisAxis>> positiveAxes,
            IReadOnlyList<KeyValuePair<int, AdvancedVisemeMeshCalibrator.PoseBasisAxis>> negativeAxes,
            IReadOnlyDictionary<int, Motion> poses)
        {
            var positiveBase = BuildExternalRayBases(
                request, result, graph, outputRoot, speechWeights,
                articulator, "Positive", positiveAxes);
            var negativeBase = BuildExternalRayBases(
                request, result, graph, outputRoot, speechWeights,
                articulator, "Negative", negativeAxes);

            string trackingPositive = null;
            string trackingNegative = null;
            if (result.trackingContributionParameters.TryGetValue(
                    articulator, out var trackingContribution))
            {
                SplitSignedMagnitude(
                    graph, outputRoot, $"ExternalBasis/{articulator}/Tracking",
                    trackingContribution, out trackingPositive, out trackingNegative);
            }
            AddExternalTrackingRay(
                graph, outputRoot, positiveBase, trackingPositive,
                $"ExternalBasis/{articulator}/Positive/WithTracking");
            AddExternalTrackingRay(
                graph, outputRoot, negativeBase, trackingNegative,
                $"ExternalBasis/{articulator}/Negative/WithTracking");

            var positiveTotal = SumExternalRayBases(
                graph, outputRoot, $"ExternalBasis/{articulator}/PositiveBaseTotal", positiveBase);
            var negativeTotal = SumExternalRayBases(
                graph, outputRoot, $"ExternalBasis/{articulator}/NegativeBaseTotal", negativeBase);
            SplitSignedMagnitude(
                graph, outputRoot, $"ExternalBasis/{articulator}/Final",
                finalArticulation, out var finalPositive, out var finalNegative);

            // NNLS can legitimately use both signed rays at once. Preserve their
            // shared non-negative mass, then replace only their differential with
            // the final constrained articulation. This makes g=0 reproduce every
            // adjusted C⊙M ray (and the authored C ray when trims are neutral),
            // while g=1 reproduces the tracked coordinate.
            string common = null;
            if (positiveBase.Count > 0 && negativeBase.Count > 0)
                common = MinParameters(
                    graph, outputRoot, $"ExternalBasis/{articulator}/CommonRayMass",
                    positiveTotal, negativeTotal);
            ReconcileExternalRayDirection(
                graph, outputRoot, articulator, "Positive", positiveBase,
                common, finalPositive, poses);
            ReconcileExternalRayDirection(
                graph, outputRoot, articulator, "Negative", negativeBase,
                common, finalNegative, poses);
        }

        private static List<KeyValuePair<int, string>> BuildExternalRayBases(
            Request request,
            Result result,
            MathGraph graph,
            BlendTree outputRoot,
            string[] speechWeights,
            AdvancedVisemeArticulator articulator,
            string direction,
            IReadOnlyList<KeyValuePair<int, AdvancedVisemeMeshCalibrator.PoseBasisAxis>> axes)
        {
            var bases = new List<KeyValuePair<int, string>>(axes.Count);
            foreach (var pair in axes)
            {
                var coefficients = new float[VisemeReconstructionProfile.VisemeCount];
                for (var viseme = 0; viseme < coefficients.Length; viseme++)
                    coefficients[viseme] = Mathf.Max(
                        0f, request.calibration.coefficients[viseme, pair.Key]) *
                        request.profile.GetVisemeArticulationMultiplier(
                            viseme, articulator);
                var mass = graph.Param(
                    $"ExternalBasis/{articulator}/{direction}/{pair.Key}/SpeechMass", 0f);
                graph.AddOperation(outputRoot, graph.Linear(
                    mass, BuildVisemeTerms(speechWeights, coefficients, false)));

                var speechPart = mass;
                if (result.inverseTrackingGainParameters.TryGetValue(
                        articulator, out var inverseGain))
                {
                    speechPart = graph.Param(
                        $"ExternalBasis/{articulator}/{direction}/{pair.Key}/SpeechPart", 0f);
                    graph.AddOperation(outputRoot,
                        graph.Multiply(inverseGain, mass, speechPart, false));
                }
                bases.Add(new KeyValuePair<int, string>(pair.Key, speechPart));
            }
            return bases;
        }

        private static void AddExternalTrackingRay(
            MathGraph graph,
            BlendTree outputRoot,
            IList<KeyValuePair<int, string>> bases,
            string trackingRay,
            string key)
        {
            if (bases.Count == 0 || string.IsNullOrEmpty(trackingRay)) return;
            var first = bases[0];
            var fused = graph.Param(key, 0f);
            graph.AddOperation(outputRoot, graph.Linear(fused, new[]
            {
                Term.Positive(first.Value, 1f), Term.Positive(trackingRay, 1f)
            }));
            bases[0] = new KeyValuePair<int, string>(first.Key, fused);
        }

        private static string SumExternalRayBases(
            MathGraph graph,
            BlendTree outputRoot,
            string key,
            IReadOnlyList<KeyValuePair<int, string>> bases)
        {
            if (bases.Count == 0) return null;
            if (bases.Count == 1) return bases[0].Value;
            var sum = graph.Param(key, 0f);
            graph.AddOperation(outputRoot, graph.Linear(
                sum, bases.Select(pair => Term.Positive(pair.Value, 1f))));
            return sum;
        }

        private static void ReconcileExternalRayDirection(
            MathGraph graph,
            BlendTree outputRoot,
            AdvancedVisemeArticulator articulator,
            string direction,
            IReadOnlyList<KeyValuePair<int, string>> bases,
            string common,
            string finalMagnitude,
            IReadOnlyDictionary<int, Motion> poses)
        {
            if (bases.Count == 0) return;
            var targetRaw = finalMagnitude;
            if (!string.IsNullOrEmpty(common))
            {
                targetRaw = graph.Param(
                    $"ExternalBasis/{articulator}/{direction}/TargetMass", 0f);
                graph.AddOperation(outputRoot, graph.Linear(targetRaw, new[]
                {
                    Term.Positive(common, 1f), Term.Positive(finalMagnitude, 1f)
                }));
            }

            // The common coarticulation mass and the reconciled signed magnitude
            // are independently non-negative, but their sum can transiently
            // exceed one while tracking authority changes. Project the final ray
            // coordinate into the usable blendshape interval before it can reach
            // a tailored pose. This keeps every downstream pose drive compatible
            // with VRChat/VRCFury's final 0..100 blendshape clamp.
            var target = graph.Param(
                $"ExternalBasis/{articulator}/{direction}/TargetClamped", 0f);
            graph.AddOperation(outputRoot, graph.Map(targetRaw, target, new[]
            {
                Point(-1f, 0f), Point(0f, 0f), Point(1f, 1f), Point(2f, 1f)
            }));

            // There is normally one ray in each direction. If a future template
            // supplies several, retain every secondary speech ray and put the
            // aggregate reconciliation on the first ray; tracking-off remains an
            // exact reconstruction of every calibrated column.
            var total = SumExternalRayBases(
                graph, outputRoot, $"ExternalBasis/{articulator}/{direction}/BaseTotal", bases);
            var correction = graph.Param(
                $"ExternalBasis/{articulator}/{direction}/Reconciliation", 0f);
            graph.AddOperation(outputRoot, graph.Linear(correction, new[]
            {
                Term.Positive(target, 1f), Term.Positive(total, -1f)
            }));
            for (var i = 0; i < bases.Count; i++)
            {
                var weight = bases[i].Value;
                if (i == 0)
                {
                    weight = graph.Param(
                        $"ExternalBasis/{articulator}/{direction}/PrimaryWeight", 0f);
                    graph.AddOperation(outputRoot, graph.Linear(weight, new[]
                    {
                        Term.Positive(bases[i].Value, 1f), Term.Signed(correction, 1f)
                    }));
                }
                graph.AddOperation(outputRoot,
                    graph.DrivePose(weight, poses[bases[i].Key], false));
            }
        }

        private static void AddSignedRayCorrection(
            MathGraph graph,
            BlendTree root,
            string key,
            string final,
            string speechBase,
            string trackingContribution,
            Motion positivePose,
            Motion negativePose)
        {
            SplitSignedMagnitude(graph, root, "Fallback/" + key + "/Final", final,
                out var finalPositive, out var finalNegative);
            SplitSignedMagnitude(graph, root, "Fallback/" + key + "/Speech", speechBase,
                out var speechPositive, out var speechNegative);

            string trackingPositive = null;
            string trackingNegative = null;
            if (!string.IsNullOrEmpty(trackingContribution))
                SplitSignedMagnitude(
                    graph, root, "Fallback/" + key + "/Tracking", trackingContribution,
                    out trackingPositive, out trackingNegative);

            var positiveTerms = new List<Term>
            {
                Term.Positive(finalPositive, 1f),
                Term.Positive(speechPositive, -1f)
            };
            var negativeTerms = new List<Term>
            {
                Term.Positive(finalNegative, 1f),
                Term.Positive(speechNegative, -1f)
            };
            if (!string.IsNullOrEmpty(trackingPositive))
            {
                positiveTerms.Add(Term.Positive(trackingPositive, -1f));
                negativeTerms.Add(Term.Positive(trackingNegative, -1f));
            }

            var positiveCorrection = graph.Param(
                $"Fallback/{key}/PositiveRayCorrection", 0f);
            var negativeCorrection = graph.Param(
                $"Fallback/{key}/NegativeRayCorrection", 0f);
            graph.AddOperation(root, graph.Linear(positiveCorrection, positiveTerms));
            graph.AddOperation(root, graph.Linear(negativeCorrection, negativeTerms));
            graph.AddOperation(root,
                graph.DrivePose(positiveCorrection, positivePose, true));
            graph.AddOperation(root,
                graph.DrivePose(negativeCorrection, negativePose, true));
        }

        private static void SplitSignedMagnitude(
            MathGraph graph,
            BlendTree root,
            string key,
            string input,
            out string positive,
            out string negative)
        {
            positive = graph.Param(key + "/Positive", 0f);
            negative = graph.Param(key + "/Negative", 0f);
            graph.AddOperation(root, graph.Map(input, positive, new[]
            {
                Point(-2f, 0f), Point(0f, 0f), Point(2f, 2f)
            }));
            graph.AddOperation(root, graph.Map(input, negative, new[]
            {
                Point(-2f, 2f), Point(0f, 0f), Point(2f, 0f)
            }));
        }

        internal static bool ShouldBuildFallbackArticulationCorrection(Request request)
        {
            return request != null &&
                   (request.trackingEnabled ||
                    request.component != null && request.component.reconstructionMode ==
                    AdvancedVisemeReconstructionMode.BetaCoarticulation ||
                    request.component != null && request.component.createTuningMenu &&
                    (request.component.tuningMenuSections &
                     AdvancedVisemeTuningMenuSections.Tongue) != 0 ||
                    HasNonNeutralTongueStrength(request.profile) ||
                    request.profile != null &&
                    request.profile.HasNonNeutralVisemeAdjustments());
        }

        private static bool HasNonNeutralTongueStrength(VisemeReconstructionProfile profile)
        {
            if (profile == null) return false;
            return !Mathf.Approximately(profile.tongueOutStrength, 1f) ||
                   !Mathf.Approximately(profile.tongueYStrength, 1f) ||
                   !Mathf.Approximately(profile.tongueXStrength, 1f) ||
                   !Mathf.Approximately(profile.tongueRollStrength, 1f) ||
                   !Mathf.Approximately(profile.tongueArchStrength, 1f) ||
                   !Mathf.Approximately(profile.tongueShapeStrength, 1f) ||
                   !Mathf.Approximately(profile.tongueTwistStrength, 1f);
        }

        private static bool IsTunableTongueArticulator(
            AdvancedVisemeArticulator articulator)
        {
            return articulator == AdvancedVisemeArticulator.TongueOut ||
                   articulator == AdvancedVisemeArticulator.TongueY ||
                   articulator == AdvancedVisemeArticulator.TongueX ||
                   articulator == AdvancedVisemeArticulator.TongueRoll ||
                   articulator == AdvancedVisemeArticulator.TongueArchY ||
                   articulator == AdvancedVisemeArticulator.TongueShape ||
                   articulator == AdvancedVisemeArticulator.TongueTwistRight ||
                   articulator == AdvancedVisemeArticulator.TongueTwistLeft;
        }

        internal static bool ShouldSubtractGeneratedTrackingContribution(bool reuseExistingTracking)
        {
            return !reuseExistingTracking;
        }

        private static void ValidateReusedTrackingPoses(Request request, Result result)
        {
            foreach (var articulator in result.trackingContributionParameters.Keys)
            {
                if (request.externalPoses == null ||
                    !request.externalPoses.TryGetValue(articulator, out var pose) || pose == null ||
                    pose.positive == null && pose.negative == null)
                {
                    // A partial or highly tailored template is valid. BuildOutputTree
                    // first tries the profile's explicit clip/blendshape mapping; if
                    // none exists it emits no curve for this channel, allowing the
                    // installed lower tracking layer to keep ownership of that rig
                    // property instead of fabricating a generic pose.
                    continue;
                }

                if (pose.positive != null &&
                    !IsEntireLinearCorrectionClip(pose.positive, request.rendererPath, request.targetMesh) ||
                    pose.negative != null &&
                    !IsEntireLinearCorrectionClip(pose.negative, request.rendererPath, request.targetMesh))
                    throw new InvalidOperationException(
                        $"Existing tracking channel '{articulator}' animates a bone, material, another renderer, " +
                        "or a blendshape absent from the selected face mesh. Owning reuse requires the entire " +
                        "sampled pose to be target-face blendshape curves; use Outputs Only for this template.");
            }
        }

        private static IEnumerable<VRCExpressionParameters.Parameter> BuildExpressionParameters(Request request, Result result)
        {
            var names = request.existingExpressionParameters != null
                ? new HashSet<string>(request.existingExpressionParameters)
                : new HashSet<string>();
            if (request.trackingEnabled)
            {
                if (!request.reuseExistingTracking)
                {
                    foreach (var articulator in TrackedArticulators(request.effectiveTrackingInputs))
                    {
                        var binding = request.profile.FindBinding(articulator);
                        if (binding == null || string.IsNullOrWhiteSpace(binding.trackingParameter)) continue;
                        var name = TrackingParameterName(request.trackingPrefix, binding.trackingParameter);
                        if (UsesBinaryTracking(request))
                        {
                            foreach (var bitName in BinaryParameterNames(
                                         name, articulator, request.component.trackingEncoding))
                            {
                                if (!names.Add(bitName)) continue;
                                yield return ExpressionParameter(
                                    bitName, VRCExpressionParameters.ValueType.Bool, 0f);
                            }
                        }
                        else if (names.Add(name))
                        {
                            yield return ExpressionParameter(
                                name, VRCExpressionParameters.ValueType.Float, 0f);
                        }
                    }
                }
                var activeParameter = string.IsNullOrEmpty(request.trackingActiveParameter)
                    ? "LipTrackingActive"
                    : request.trackingActiveParameter;
                var autoReuseOnly = request.component.trackingInputs ==
                                    AdvancedVisemeTrackingInputs.Auto;
                if (!autoReuseOnly && names.Add(activeParameter))
                    yield return ExpressionParameter(
                        activeParameter, VRCExpressionParameters.ValueType.Bool, 0f);
                if (!autoReuseOnly && request.component.createFaceTrackingToggle &&
                    !string.IsNullOrEmpty(result.manualTrackingParameter) &&
                    names.Add(result.manualTrackingParameter))
                    yield return ExpressionParameter(
                        result.manualTrackingParameter,
                        VRCExpressionParameters.ValueType.Bool, 1f);
            }

            foreach (var pair in result.tuningParameters.OrderBy(pair => (int)pair.Key))
            {
                if (!names.Add(pair.Value)) continue;
                yield return ExpressionParameter(
                    pair.Value,
                    VRCExpressionParameters.ValueType.Float,
                    AdvancedVisemeTuning.DefaultValue(request.profile, pair.Key),
                    request.component.saveTuningValues,
                    false);
            }

            if (!string.IsNullOrEmpty(result.tuningSyncFocusParameter) &&
                names.Add(result.tuningSyncFocusParameter))
                yield return ExpressionParameter(
                    result.tuningSyncFocusParameter,
                    VRCExpressionParameters.ValueType.Int,
                    0f, false, false);

            if (!string.IsNullOrEmpty(result.tuningSyncDataParameter) &&
                names.Add(result.tuningSyncDataParameter))
                yield return ExpressionParameter(
                    result.tuningSyncDataParameter,
                    VRCExpressionParameters.ValueType.Int,
                    0f, false, true);

            foreach (var indexParameter in result.tuningSyncIndexParameters)
            {
                if (!names.Add(indexParameter)) continue;
                yield return ExpressionParameter(
                    indexParameter,
                    VRCExpressionParameters.ValueType.Bool,
                    0f, false, true);
            }
        }

        private static VRCExpressionParameters.Parameter ExpressionParameter(
            string name,
            VRCExpressionParameters.ValueType type,
            float defaultValue,
            bool saved = false,
            bool networkSynced = true)
        {
            return new VRCExpressionParameters.Parameter
            {
                name = name,
                valueType = type,
                defaultValue = defaultValue,
                saved = saved,
                networkSynced = networkSynced
            };
        }

        private static string TrackingParameterName(string prefix, string suffix)
        {
            prefix = (prefix ?? string.Empty).Trim().Trim('/');
            suffix = (suffix ?? string.Empty).Trim().Trim('/');
            return string.IsNullOrEmpty(prefix) ? "v2/" + suffix : prefix + "/v2/" + suffix;
        }

        private static bool UsesBinaryTracking(Request request)
        {
            return request.trackingEnabled &&
                   !request.reuseExistingTracking &&
                   request.component.trackingEncoding != AdvancedVisemeTrackingEncoding.FullFloat;
        }

        private static IEnumerable<string> BinaryParameterNames(
            string baseName,
            AdvancedVisemeArticulator articulator,
            AdvancedVisemeTrackingEncoding encoding)
        {
            var bitCount = AdvancedVisemeMath.TrackingMagnitudeBits(articulator, encoding);
            for (var bit = 0; bit < bitCount; bit++) yield return baseName + (1 << bit);
            if (AdvancedVisemeMath.IsSignedTrackingArticulator(articulator)) yield return baseName + "Negative";
        }

        private static string DecodeBinaryTracking(
            MathGraph graph,
            BlendTree root,
            string baseName,
            AdvancedVisemeArticulator articulator,
            bool signed,
            AdvancedVisemeTrackingEncoding encoding)
        {
            var bitCount = AdvancedVisemeMath.TrackingMagnitudeBits(articulator, encoding);
            var maximum = (1 << bitCount) - 1f;
            var terms = new List<Term>(bitCount);
            for (var bit = 0; bit < bitCount; bit++)
            {
                var bitName = baseName + (1 << bit);
                // The Expression Parameter is Bool; a Float Animator parameter intentionally
                // uses VRChat's documented Bool-to-Float type conversion.
                graph.AddParameter(bitName, AnimatorControllerParameterType.Float, 0f);
                terms.Add(Term.Positive(bitName, (1 << bit) / maximum));
            }

            var magnitude = graph.Param($"Tracking/{articulator}/BinaryMagnitude", 0f);
            graph.AddOperation(root, graph.Linear(magnitude, terms));
            if (!signed) return magnitude;

            var negative = baseName + "Negative";
            graph.AddParameter(negative, AnimatorControllerParameterType.Float, 0f);
            var negativeMagnitude = graph.Param($"Tracking/{articulator}/BinaryNegativeMagnitude", 0f);
            graph.AddOperation(root, graph.Multiply(negative, magnitude, negativeMagnitude, false));
            var decoded = graph.Param($"Tracking/{articulator}/BinarySigned", 0f);
            graph.AddOperation(root, graph.Linear(decoded, new[]
            {
                Term.Positive(magnitude, 1f),
                Term.Positive(negativeMagnitude, -2f)
            }));
            return decoded;
        }

        internal static IEnumerable<AdvancedVisemeArticulator> SynthesizedArticulators()
        {
            foreach (var articulator in CoreArticulators) yield return articulator;
            foreach (var articulator in QualityArticulators) yield return articulator;
            foreach (var articulator in FullTongueArticulators) yield return articulator;
        }

        internal static bool HasCompleteVisiblePoseOwnership(
            IEnumerable<AdvancedVisemeArticulator> measured)
        {
            if (measured == null) return false;
            var available = measured as ISet<AdvancedVisemeArticulator> ??
                            new HashSet<AdvancedVisemeArticulator>(measured);
            return VisiblePoseOwnershipArticulators.All(available.Contains);
        }

        internal static bool HasDriveableOutputPose(
            Request request,
            AdvancedVisemeArticulator articulator)
        {
            if (request == null) return false;
            if (request.resolvedBlendShapes != null &&
                request.resolvedBlendShapes.TryGetValue(articulator, out var shape) &&
                !string.IsNullOrEmpty(shape))
                return true;

            var binding = request.profile != null
                ? request.profile.FindBinding(articulator)
                : null;
            if (!request.reuseExistingTracking)
                return binding != null && binding.animationOverride != null;

            // Reused tracking is already present in a lower Override layer, so
            // the later correction needs a verified, invertible target-renderer
            // basis. Parameter availability alone is not visual ownership.
            if (request.externalPoses != null &&
                request.externalPoses.TryGetValue(articulator, out var external) &&
                external != null && external.positive != null &&
                IsEntireLinearCorrectionClip(
                    external.positive, request.rendererPath, request.targetMesh))
                return true;
            return binding != null && binding.animationOverride != null &&
                   IsEntireLinearCorrectionClip(
                       binding.animationOverride, request.rendererPath, request.targetMesh);
        }

        internal static IEnumerable<AdvancedVisemeArticulator> TrackedArticulators(
            AdvancedVisemeTrackingInputs mode)
        {
            if (mode == AdvancedVisemeTrackingInputs.Disabled ||
                mode == AdvancedVisemeTrackingInputs.Auto ||
                mode == AdvancedVisemeTrackingInputs.ReuseExisting)
                yield break;

            foreach (var articulator in CoreArticulators) yield return articulator;
            if (mode == AdvancedVisemeTrackingInputs.Quality12 ||
                mode == AdvancedVisemeTrackingInputs.FullTongue18)
                foreach (var articulator in QualityArticulators) yield return articulator;
            if (mode == AdvancedVisemeTrackingInputs.FullTongue18)
                foreach (var articulator in FullTongueArticulators) yield return articulator;
        }

        internal static bool TryResolveTrackingParameter(
            Request request,
            AdvancedVisemeArticulator articulator,
            ArticulatorRigBinding binding,
            out string parameter)
        {
            parameter = null;
            if (request == null || binding == null || string.IsNullOrWhiteSpace(binding.trackingParameter))
                return false;
            if (request.reuseExistingTracking)
            {
                // A partial custom template is valid. Missing measurements remain
                // speech-driven instead of becoming fabricated zero-valued inputs.
                return request.trackingParameterNames != null &&
                       request.trackingParameterNames.TryGetValue(articulator, out parameter) &&
                       !string.IsNullOrEmpty(parameter);
            }

            parameter = TrackingParameterName(request.trackingPrefix, binding.trackingParameter);
            return !string.IsNullOrEmpty(parameter);
        }

        private static bool IsSigned(AdvancedVisemeArticulator articulator)
        {
            return articulator == AdvancedVisemeArticulator.SmileSad ||
                   articulator == AdvancedVisemeArticulator.JawX ||
                   articulator == AdvancedVisemeArticulator.JawZ ||
                   articulator == AdvancedVisemeArticulator.MouthX ||
                   articulator == AdvancedVisemeArticulator.TongueY ||
                   articulator == AdvancedVisemeArticulator.TongueX ||
                   articulator == AdvancedVisemeArticulator.TongueArchY ||
                   articulator == AdvancedVisemeArticulator.TongueShape;
        }

        internal static bool IsLinearCorrectionCurve(
            EditorCurveBinding binding,
            string rendererPath,
            Mesh targetMesh)
        {
            if (binding.type != typeof(SkinnedMeshRenderer) ||
                !binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal) ||
                !string.Equals(binding.path, rendererPath, StringComparison.Ordinal))
                return false;
            var shape = binding.propertyName.Substring("blendShape.".Length);
            return targetMesh == null || targetMesh.GetBlendShapeIndex(shape) >= 0;
        }

        internal static bool IsEntireLinearCorrectionClip(
            AnimationClip source,
            string rendererPath,
            Mesh targetMesh)
        {
            if (source == null || AnimationUtility.GetObjectReferenceCurveBindings(source).Length != 0)
                return false;
            var bindings = AnimationUtility.GetCurveBindings(source);
            if (bindings.Length == 0) return false;
            var sampleTime = Mathf.Max(0f, source.length);
            foreach (var binding in bindings)
            {
                if (!IsLinearCorrectionCurve(binding, rendererPath, targetMesh)) return false;
                var curve = AnimationUtility.GetEditorCurve(source, binding);
                if (curve == null) return false;
                var value = curve.Evaluate(sampleTime);
                if (float.IsNaN(value) || float.IsInfinity(value)) return false;
            }
            return true;
        }

        private static (float input, float output) Point(float input, float output) => (input, output);

        private static void AddTimeLayer(AnimatorController controller, MathGraph graph, string timeParameter)
        {
            var clip = graph.Clip("Continuous Time");
            var curve = AnimationCurve.Linear(0f, 0f, 100000f, 100000f);
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), timeParameter), curve);
            AddMotionLayer(controller, graph, "YUCP AVR Time", clip);
        }

        private static void AddIntToFloatLayer(
            AnimatorController controller,
            MathGraph graph,
            string source,
            string output,
            IReadOnlyList<string> oneHotOutputs,
            string layerName)
        {
            var stateMachine = AddStateLayer(controller, graph, layerName);
            AnimatorState silence = null;
            for (var i = 0; i < VisemeReconstructionProfile.VisemeCount; i++)
            {
                var state = stateMachine.AddState(VisemeReconstructionProfile.VisemeNames[i]);
                var values = new List<KeyValuePair<string, float>>
                {
                    new KeyValuePair<string, float>(output, i)
                };
                if (oneHotOutputs != null)
                    for (var channel = 0; channel < oneHotOutputs.Count; channel++)
                        values.Add(new KeyValuePair<string, float>(
                            oneHotOutputs[channel], channel == i ? 1f : 0f));
                state.motion = graph.MultiSetter(
                    "Decode " + VisemeReconstructionProfile.VisemeNames[i],
                    values);
                state.writeDefaultValues = true;
                if (i == 0) silence = state;

                var transition = stateMachine.AddAnyStateTransition(state);
                transition.duration = 0f;
                transition.hasExitTime = false;
                transition.canTransitionToSelf = false;
                transition.AddCondition(AnimatorConditionMode.Equals, i, source);
            }
            stateMachine.defaultState = silence;
        }

        private static void AddTrackingGateLayer(
            AnimatorController controller,
            MathGraph graph,
            string manual,
            string active,
            out string output)
        {
            output = graph.Param("TrackingGate", 0f);
            var layer = AddStateLayer(controller, graph, "YUCP AVR Tracking Gate");
            var off = layer.AddState("Off");
            var on = layer.AddState("On");
            off.motion = graph.Setter(output, 0f);
            on.motion = graph.Setter(output, 1f);
            layer.defaultState = off;
            var enter = off.AddTransition(on);
            enter.duration = 0f;
            enter.hasExitTime = false;
            enter.AddCondition(AnimatorConditionMode.If, 0f, active);
            enter.AddCondition(AnimatorConditionMode.If, 0f, manual);
            AddBoolExit(on, off, active);
            AddBoolExit(on, off, manual);
        }

        private static void AddBoolFloatLayer(
            AnimatorController controller,
            MathGraph graph,
            string source,
            string outputName,
            bool defaultValue,
            string layerName,
            out string output)
        {
            output = graph.Param(outputName, defaultValue ? 1f : 0f);
            var layer = AddStateLayer(controller, graph, layerName);
            var off = layer.AddState("False");
            var on = layer.AddState("True");
            off.motion = graph.Setter(output, 0f);
            on.motion = graph.Setter(output, 1f);
            layer.defaultState = defaultValue ? on : off;
            var toOn = off.AddTransition(on);
            toOn.duration = 0f;
            toOn.hasExitTime = false;
            toOn.AddCondition(AnimatorConditionMode.If, 0f, source);
            AddBoolExit(on, off, source);
        }

        private static void AddBoolExit(AnimatorState from, AnimatorState to, string parameter)
        {
            var transition = from.AddTransition(to);
            transition.duration = 0f;
            transition.hasExitTime = false;
            transition.AddCondition(AnimatorConditionMode.IfNot, 0f, parameter);
        }

        private static AnimatorStateMachine AddStateLayer(AnimatorController controller, MathGraph graph, string name)
        {
            controller.AddLayer(name);
            var layers = controller.layers;
            var index = layers.Length - 1;
            layers[index].defaultWeight = 1f;
            controller.layers = layers;
            var stateMachine = layers[index].stateMachine;
            graph.SubAsset(stateMachine);
            return stateMachine;
        }

        private static void AddMotionLayer(AnimatorController controller, MathGraph graph, string name, Motion motion)
        {
            var stateMachine = AddStateLayer(controller, graph, name);
            var state = stateMachine.AddState(name);
            state.motion = motion;
            state.writeDefaultValues = true;
            stateMachine.defaultState = state;
        }

        private readonly struct Term
        {
            public readonly string parameter;
            public readonly float multiplier;
            public readonly bool signed;
            public readonly bool constant;

            private Term(string parameter, float multiplier, bool signed, bool constant)
            {
                this.parameter = parameter;
                this.multiplier = multiplier;
                this.signed = signed;
                this.constant = constant;
            }

            public static Term Positive(string parameter, float multiplier) => new Term(parameter, multiplier, false, false);
            public static Term Signed(string parameter, float multiplier) => new Term(parameter, multiplier, true, false);
            public static Term Constant(float value) => new Term(null, value, false, true);
            public static Term For(string parameter, float multiplier, bool signed) => new Term(parameter, multiplier, signed, false);
            public Term WithMultiplierScale(float scale) =>
                new Term(parameter, multiplier * scale, signed, constant);
        }

        private sealed class MathGraph
        {
            private readonly AnimatorController controller;
            private readonly string prefix;
            private readonly HashSet<UnityEngine.Object> subAssets = new HashSet<UnityEngine.Object>();
            private readonly Dictionary<(string output, float value), AnimationClip> setterCache =
                new Dictionary<(string output, float value), AnimationClip>();
            private readonly Dictionary<string, AlphaBatch> alphaBatches =
                new Dictionary<string, AlphaBatch>(StringComparer.Ordinal);
            private readonly Dictionary<Motion, MapDescriptor> mapDescriptors =
                new Dictionary<Motion, MapDescriptor>();
            private readonly Dictionary<(BlendTree root, string input), MapBatch> mapBatches =
                new Dictionary<(BlendTree root, string input), MapBatch>();
            private readonly Dictionary<Motion, ParameterBlendDescriptor> parameterBlendDescriptors =
                new Dictionary<Motion, ParameterBlendDescriptor>();
            private readonly Dictionary<(BlendTree root, string driver, string thresholds), ParameterBlendBatch>
                parameterBlendBatches =
                    new Dictionary<(BlendTree root, string driver, string thresholds), ParameterBlendBatch>();
            private readonly Dictionary<Motion, BinarySelectDescriptor> binarySelectDescriptors =
                new Dictionary<Motion, BinarySelectDescriptor>();
            private readonly Dictionary<(BlendTree root, string driver), BinarySelectBatch>
                binarySelectBatches =
                    new Dictionary<(BlendTree root, string driver), BinarySelectBatch>();
            private readonly Dictionary<Motion, SilenceHoldDescriptor> silenceHoldDescriptors =
                new Dictionary<Motion, SilenceHoldDescriptor>();
            private readonly Dictionary<(
                BlendTree root, string viseme, string history, string stability), SilenceHoldBatch>
                silenceHoldBatches =
                    new Dictionary<(
                        BlendTree root, string viseme, string history, string stability), SilenceHoldBatch>();
            private AnimationClip emptyClip;
            private const string AlwaysOne = "__YUCP_AVR_ONE";
            public const string AlwaysOneParameter = AlwaysOne;

            private sealed class AlphaBatch
            {
                public BlendTree tree;
                public float[] samples;
                public AnimationClip[] clips;
            }

            private sealed class MapDescriptor
            {
                public string input;
                public string output;
                public (float input, float output)[] points;
            }

            private sealed class MapBatch
            {
                public BlendTree tree;
                public readonly List<MapDescriptor> descriptors = new List<MapDescriptor>();
            }

            private sealed class ParameterBlendDescriptor
            {
                public string driver;
                public string output;
                public float[] thresholds;
                public string[] sources;
                public bool signed;
            }

            private sealed class ParameterBlendBatch
            {
                public BlendTree tree;
                public readonly List<ParameterBlendDescriptor> descriptors =
                    new List<ParameterBlendDescriptor>();
            }

            private sealed class BinarySelectDescriptor
            {
                public string driver;
                public Motion whenZero;
                public Motion whenOne;
            }

            private sealed class BinarySelectBatch
            {
                public BlendTree tree;
                public BlendTree whenZero;
                public BlendTree whenOne;
                public readonly HashSet<string> bindings =
                    new HashSet<string>(StringComparer.Ordinal);
            }

            private sealed class SilenceHoldDescriptor
            {
                public string viseme;
                public string history;
                public string stability;
                public Motion nonSilence;
                public Motion silenceRelease;
                public Motion silenceHold;
            }

            private sealed class SilenceHoldBatch
            {
                public BlendTree tree;
                public BlendTree nonSilence;
                public BlendTree silenceRelease;
                public BlendTree silenceHold;
                public readonly HashSet<string> bindings =
                    new HashSet<string>(StringComparer.Ordinal);
            }

            public MathGraph(AnimatorController controller, string prefix)
            {
                this.controller = controller;
                this.prefix = prefix;
                AddParameter(AlwaysOne, AnimatorControllerParameterType.Float, 1f);
            }

            public string Param(string name, float defaultValue, bool internalName = true)
            {
                var parameter = internalName ? prefix + "/" + name : name;
                AddParameter(parameter, AnimatorControllerParameterType.Float, defaultValue);
                return parameter;
            }

            public void AddParameter(string name, AnimatorControllerParameterType type, float defaultValue)
            {
                if (controller.parameters.Any(p => p.name == name)) return;
                var parameter = new AnimatorControllerParameter { name = name, type = type };
                if (type == AnimatorControllerParameterType.Float) parameter.defaultFloat = defaultValue;
                else if (type == AnimatorControllerParameterType.Int) parameter.defaultInt = Mathf.RoundToInt(defaultValue);
                else if (type == AnimatorControllerParameterType.Bool) parameter.defaultBool = defaultValue > 0.5f;
                controller.AddParameter(parameter);
            }

            public BlendTree Direct(string name)
            {
                var tree = new BlendTree
                {
                    name = name,
                    blendType = BlendTreeType.Direct,
                    useAutomaticThresholds = false
                };
                SubAsset(tree);
                return tree;
            }

            public AnimationClip Clip(string name)
            {
                var clip = new AnimationClip { name = name };
                SubAsset(clip);
                return clip;
            }

            public AnimationClip EmptyClip()
            {
                if (emptyClip != null) return emptyClip;
                emptyClip = Clip("YUCP AVR Empty");
                return emptyClip;
            }

            public AnimationClip Setter(string output, float value)
            {
                var key = (output, value);
                if (setterCache.TryGetValue(key, out var existing)) return existing;
                var clip = Clip($"{output} = {value:0.###}");
                AnimationUtility.SetEditorCurve(clip,
                    EditorCurveBinding.FloatCurve("", typeof(Animator), output),
                    AnimationCurve.Constant(0f, 0f, value));
                setterCache[key] = clip;
                return clip;
            }

            public AnimationClip MultiSetter(
                string name,
                IEnumerable<KeyValuePair<string, float>> values)
            {
                var clip = Clip(name);
                foreach (var pair in values)
                {
                    AnimationUtility.SetEditorCurve(clip,
                        EditorCurveBinding.FloatCurve("", typeof(Animator), pair.Key),
                        AnimationCurve.Constant(0f, 0f, pair.Value));
                }
                return clip;
            }

            public Motion Copy(string input, string output, bool signed)
            {
                return signed
                    ? Map(input, output, new[] { Point(-2f, -2f), Point(0f, 0f), Point(2f, 2f) })
                    : WeightedSetter(input, output, 1f);
            }

            public Motion CopyVector(
                IReadOnlyList<string> inputs,
                IReadOnlyList<string> outputs,
                string name)
            {
                if (inputs == null || outputs == null || inputs.Count != outputs.Count)
                    throw new InvalidOperationException(
                        $"{name} requires equally sized input and output vectors.");

                // Every vector element is a nonnegative simplex coordinate. A
                // single Direct tree can therefore copy the complete vector in
                // one Animator stage. The shared zero clip gives every output a
                // deterministic neutral contribution without compiling one
                // WeightedSetter tree per scalar.
                var tree = Direct(name);
                var children = new List<ChildMotion>(inputs.Count + 1);
                for (var i = 0; i < inputs.Count; i++)
                    children.Add(new ChildMotion
                    {
                        motion = Setter(outputs[i], 1f),
                        directBlendParameter = inputs[i],
                        timeScale = 1f
                    });
                children.Add(new ChildMotion
                {
                    motion = MultiSetter(
                        name + " safety zero",
                        outputs.Select(output =>
                            new KeyValuePair<string, float>(output, 0f))),
                    directBlendParameter = AlwaysOne,
                    timeScale = 1f
                });
                tree.children = children.ToArray();
                return tree;
            }

            private Motion CopyMixedVector(
                IReadOnlyList<string> inputs,
                IReadOnlyList<string> outputs,
                IReadOnlyList<bool> signed,
                string name)
            {
                if (inputs == null || outputs == null || signed == null ||
                    inputs.Count != outputs.Count || inputs.Count != signed.Count)
                    throw new InvalidOperationException(
                        $"{name} requires equally sized mixed vectors.");

                var tree = Direct(name);
                var children = new List<ChildMotion>(inputs.Count + 1);
                for (var i = 0; i < inputs.Count; i++)
                {
                    if (signed[i])
                    {
                        children.Add(new ChildMotion
                        {
                            motion = Map(inputs[i], outputs[i], new[]
                            {
                                Point(-2f, -2f), Point(0f, 0f), Point(2f, 2f)
                            }),
                            directBlendParameter = AlwaysOne,
                            timeScale = 1f
                        });
                    }
                    else
                    {
                        children.Add(new ChildMotion
                        {
                            motion = Setter(outputs[i], 1f),
                            directBlendParameter = inputs[i],
                            timeScale = 1f
                        });
                    }
                }
                children.Add(new ChildMotion
                {
                    motion = MultiSetter(
                        name + " safety zero",
                        outputs.Select(output =>
                            new KeyValuePair<string, float>(output, 0f))),
                    directBlendParameter = AlwaysOne,
                    timeScale = 1f
                });
                tree.children = children.ToArray();
                return tree;
            }

            public Motion SimplexMatrixProjection(
                IReadOnlyList<string> weights,
                IReadOnlyList<string> outputs,
                Func<int, int, float> coefficient,
                string name)
            {
                return SimplexMatrixProjection(
                    weights, outputs, coefficient, null, null, name);
            }

            public Motion SimplexMatrixProjection(
                IReadOnlyList<string> weights,
                IReadOnlyList<string> outputs,
                Func<int, int, float> coefficient,
                string rankOneDelta,
                Func<int, float> rankOneCoefficient,
                string name)
            {
                if (weights == null || outputs == null || coefficient == null)
                    throw new ArgumentNullException(name);
                if (!string.IsNullOrEmpty(rankOneDelta) && rankOneCoefficient == null)
                    throw new InvalidOperationException(
                        $"{name} requires coefficients for its rank-one correction.");

                var tree = Direct(name);
                var children = new List<ChildMotion>(weights.Count + 2);
                for (var row = 0; row < weights.Count; row++)
                {
                    var rowIndex = row;
                    var values = outputs
                        .Select((output, column) => new KeyValuePair<string, float>(
                            output, coefficient(rowIndex, column)))
                        .Where(pair => Mathf.Abs(pair.Value) >= 1e-8f)
                        .ToArray();
                    children.Add(new ChildMotion
                    {
                        motion = values.Length == 0
                            ? EmptyClip()
                            : MultiSetter($"{name} row {row}", values),
                        directBlendParameter = weights[row],
                        timeScale = 1f
                    });
                }
                if (!string.IsNullOrEmpty(rankOneDelta))
                {
                    const float signedBound = 2f;
                    var negative = outputs
                        .Select((output, column) => new KeyValuePair<string, float>(
                            output, -signedBound * rankOneCoefficient(column)))
                        .Where(pair => Mathf.Abs(pair.Value) >= 1e-8f)
                        .ToArray();
                    var positive = outputs
                        .Select((output, column) => new KeyValuePair<string, float>(
                            output, signedBound * rankOneCoefficient(column)))
                        .Where(pair => Mathf.Abs(pair.Value) >= 1e-8f)
                        .ToArray();
                    if (negative.Length > 0 || positive.Length > 0)
                    {
                        var zero = negative.Select(pair =>
                            new KeyValuePair<string, float>(pair.Key, 0f));
                        var correction = OneDimensional(
                            name + " rank-one correction", rankOneDelta,
                            new[]
                            {
                                Child(MultiSetter(name + " rank-one negative", negative),
                                    -signedBound),
                                Child(MultiSetter(name + " rank-one zero", zero), 0f),
                                Child(MultiSetter(name + " rank-one positive", positive),
                                    signedBound)
                            });
                        children.Add(new ChildMotion
                        {
                            motion = correction,
                            directBlendParameter = AlwaysOne,
                            timeScale = 1f
                        });
                    }
                }
                children.Add(new ChildMotion
                {
                    motion = MultiSetter(
                        name + " safety zero",
                        outputs.Select(output => new KeyValuePair<string, float>(output, 0f))),
                    directBlendParameter = AlwaysOne,
                    timeScale = 1f
                });
                tree.children = children.ToArray();
                return tree;
            }

            public Motion SignedMatrixProjection(
                IReadOnlyList<string> inputs,
                IReadOnlyList<string> outputs,
                IReadOnlyList<float> constants,
                Func<int, int, float> coefficient,
                string name)
            {
                if (inputs == null || outputs == null || constants == null || coefficient == null)
                    throw new ArgumentNullException(name);
                if (outputs.Count != constants.Count)
                    throw new InvalidOperationException(
                        $"{name} requires one affine constant per output.");

                const float signedBound = 2f;
                var tree = Direct(name);
                var children = new List<ChildMotion>(inputs.Count + 1)
                {
                    new ChildMotion
                    {
                        motion = MultiSetter(
                            name + " affine base",
                            outputs.Select((output, column) =>
                                new KeyValuePair<string, float>(output, constants[column]))),
                        directBlendParameter = AlwaysOne,
                        timeScale = 1f
                    }
                };

                for (var inputIndex = 0; inputIndex < inputs.Count; inputIndex++)
                {
                    var row = inputIndex;
                    var negativeValues = outputs
                        .Select((output, column) => new KeyValuePair<string, float>(
                            output, -signedBound * coefficient(row, column)))
                        .Where(pair => Mathf.Abs(pair.Value) >= 1e-8f)
                        .ToArray();
                    var positiveValues = outputs
                        .Select((output, column) => new KeyValuePair<string, float>(
                            output, signedBound * coefficient(row, column)))
                        .Where(pair => Mathf.Abs(pair.Value) >= 1e-8f)
                        .ToArray();
                    if (negativeValues.Length == 0 && positiveValues.Length == 0) continue;

                    var signed = OneDimensional(
                        $"{name} signed row {inputIndex}", inputs[inputIndex],
                        new[]
                        {
                            Child(MultiSetter($"{name} row {inputIndex} negative", negativeValues),
                                -signedBound),
                            Child(MultiSetter(
                                    $"{name} row {inputIndex} zero",
                                    negativeValues.Select(pair =>
                                        new KeyValuePair<string, float>(pair.Key, 0f))),
                                0f),
                            Child(MultiSetter($"{name} row {inputIndex} positive", positiveValues),
                                signedBound)
                        });
                    children.Add(new ChildMotion
                    {
                        motion = signed,
                        directBlendParameter = AlwaysOne,
                        timeScale = 1f
                    });
                }
                tree.children = children.ToArray();
                return tree;
            }

            public Motion GroupedElementwiseProducts(
                IReadOnlyList<string> nonNegativeWeights,
                string[,] inputs,
                string[,] outputs,
                string name)
            {
                if (nonNegativeWeights == null || inputs == null || outputs == null)
                    throw new ArgumentNullException(name);
                if (inputs.GetLength(0) != nonNegativeWeights.Count ||
                    outputs.GetLength(0) != nonNegativeWeights.Count ||
                    inputs.GetLength(1) != outputs.GetLength(1))
                    throw new InvalidOperationException(
                        $"{name} requires matching weight, input, and output dimensions.");

                var allOutputs = new List<string>();
                var tree = Direct(name);
                var children = new List<ChildMotion>();
                for (var group = 0; group < nonNegativeWeights.Count; group++)
                {
                    var vector = Direct($"{name} group {group}");
                    var vectorChildren = new List<ChildMotion>();
                    for (var column = 0; column < inputs.GetLength(1); column++)
                    {
                        var output = outputs[group, column];
                        allOutputs.Add(output);
                        vectorChildren.Add(new ChildMotion
                        {
                            motion = Copy(inputs[group, column], output, true),
                            directBlendParameter = AlwaysOne,
                            timeScale = 1f
                        });
                    }
                    vectorChildren.Add(new ChildMotion
                    {
                        motion = MultiSetter(
                            $"{name} group {group} safety zero",
                            Enumerable.Range(0, inputs.GetLength(1)).Select(column =>
                                new KeyValuePair<string, float>(outputs[group, column], 0f))),
                        directBlendParameter = AlwaysOne,
                        timeScale = 1f
                    });
                    vector.children = vectorChildren.ToArray();
                    children.Add(new ChildMotion
                    {
                        motion = vector,
                        directBlendParameter = nonNegativeWeights[group],
                        timeScale = 1f
                    });
                }
                children.Add(new ChildMotion
                {
                    motion = MultiSetter(
                        name + " safety zero",
                        allOutputs.Select(output => new KeyValuePair<string, float>(output, 0f))),
                    directBlendParameter = AlwaysOne,
                    timeScale = 1f
                });
                tree.children = children.ToArray();
                return tree;
            }

            public Motion CopyArticulationVector(
                IReadOnlyDictionary<AdvancedVisemeArticulator, string> inputs,
                IReadOnlyDictionary<AdvancedVisemeArticulator, string> outputs,
                string name)
            {
                if (inputs == null || outputs == null ||
                    outputs.Keys.Any(key => !inputs.ContainsKey(key)))
                    throw new InvalidOperationException(
                        $"{name} requires matching articulation vectors.");
                var ordered = outputs.OrderBy(pair => (int)pair.Key).ToArray();
                var tree = Direct(name);
                var children = new List<ChildMotion>(ordered.Length + 1);
                foreach (var pair in ordered)
                {
                    var input = inputs[pair.Key];
                    if (IsSigned(pair.Key))
                        children.Add(new ChildMotion
                        {
                            motion = Copy(input, pair.Value, true),
                            directBlendParameter = AlwaysOne,
                            timeScale = 1f
                        });
                    else
                        children.Add(new ChildMotion
                        {
                            motion = Setter(pair.Value, 1f),
                            directBlendParameter = input,
                            timeScale = 1f
                        });
                }
                children.Add(new ChildMotion
                {
                    motion = MultiSetter(
                        name + " safety zero",
                        ordered.Select(pair =>
                            new KeyValuePair<string, float>(pair.Value, 0f))),
                    directBlendParameter = AlwaysOne,
                    timeScale = 1f
                });
                tree.children = children.ToArray();
                return tree;
            }

            public Motion ScaleArticulationVector(
                string nonNegativeWeight,
                IReadOnlyDictionary<AdvancedVisemeArticulator, string> inputs,
                IReadOnlyDictionary<AdvancedVisemeArticulator, string> outputs,
                string name)
            {
                if (inputs == null || outputs == null ||
                    outputs.Keys.Any(key => !inputs.ContainsKey(key)))
                    throw new InvalidOperationException(
                        $"{name} requires matching articulation vectors.");

                var tree = Direct(name);
                tree.children = new[]
                {
                    new ChildMotion
                    {
                        motion = CopyArticulationVector(inputs, outputs, name + " values"),
                        directBlendParameter = nonNegativeWeight,
                        timeScale = 1f
                    },
                    new ChildMotion
                    {
                        motion = MultiSetter(
                            name + " safety zero",
                            outputs.Values.Select(output =>
                                new KeyValuePair<string, float>(output, 0f))),
                        directBlendParameter = AlwaysOne,
                        timeScale = 1f
                    }
                };
                return tree;
            }

            public Motion DifferenceArticulationVector(
                IReadOnlyDictionary<AdvancedVisemeArticulator, string> positive,
                IReadOnlyDictionary<AdvancedVisemeArticulator, string> negative,
                IReadOnlyDictionary<AdvancedVisemeArticulator, string> outputs,
                float scale,
                string name)
            {
                if (positive == null || negative == null || outputs == null ||
                    outputs.Keys.Any(key =>
                        !positive.ContainsKey(key) || !negative.ContainsKey(key)))
                    throw new InvalidOperationException(
                        $"{name} requires matching articulation vectors.");

                var tree = Direct(name);
                var children = new List<ChildMotion>();
                foreach (var pair in outputs.OrderBy(pair => (int)pair.Key))
                {
                    var articulator = pair.Key;
                    var output = pair.Value;
                    if (IsSigned(articulator))
                    {
                        children.Add(new ChildMotion
                        {
                            motion = Map(positive[articulator], output, new[]
                            {
                                Point(-2f, -2f * scale), Point(0f, 0f),
                                Point(2f, 2f * scale)
                            }),
                            directBlendParameter = AlwaysOne,
                            timeScale = 1f
                        });
                        children.Add(new ChildMotion
                        {
                            motion = Map(negative[articulator], output, new[]
                            {
                                Point(-2f, 2f * scale), Point(0f, 0f),
                                Point(2f, -2f * scale)
                            }),
                            directBlendParameter = AlwaysOne,
                            timeScale = 1f
                        });
                    }
                    else
                    {
                        children.Add(new ChildMotion
                        {
                            motion = Setter(output, scale),
                            directBlendParameter = positive[articulator],
                            timeScale = 1f
                        });
                        children.Add(new ChildMotion
                        {
                            motion = Setter(output, -scale),
                            directBlendParameter = negative[articulator],
                            timeScale = 1f
                        });
                    }
                }
                children.Add(new ChildMotion
                {
                    motion = MultiSetter(
                        name + " safety zero",
                        outputs.Values.Select(output =>
                            new KeyValuePair<string, float>(output, 0f))),
                    directBlendParameter = AlwaysOne,
                    timeScale = 1f
                });
                tree.children = children.ToArray();
                return tree;
            }

            public Motion InterpolateArticulationVector(
                IReadOnlyDictionary<AdvancedVisemeArticulator, string> from,
                IReadOnlyDictionary<AdvancedVisemeArticulator, string> to,
                IReadOnlyDictionary<AdvancedVisemeArticulator, string> outputs,
                string weight,
                string name)
            {
                if (from == null || to == null || outputs == null ||
                    outputs.Keys.Any(key =>
                        !from.ContainsKey(key) || !to.ContainsKey(key)))
                    throw new InvalidOperationException(
                        $"{name} requires matching articulation vectors.");

                return OneDimensional(
                    name, weight,
                    new[]
                    {
                        Child(CopyArticulationVector(
                            from, outputs, name + " slow"), 0f),
                        Child(CopyArticulationVector(
                            to, outputs, name + " fast"), 1f)
                    });
            }

            public Motion WeightedSetter(string weight, string output, float value)
            {
                var tree = Direct($"{output} <- {weight} * {value:0.###}");
                tree.children = new[]
                {
                    new ChildMotion { motion = Setter(output, value), directBlendParameter = weight, timeScale = 1f },
                    new ChildMotion { motion = Setter(output, 0f), directBlendParameter = AlwaysOne, timeScale = 1f }
                };
                return tree;
            }

            public Motion Map(string input, string output, IReadOnlyList<(float input, float output)> points)
            {
                var orderedPoints = points.OrderBy(p => p.input).ToArray();
                var tree = new BlendTree
                {
                    name = $"Map {input} -> {output}",
                    blendType = BlendTreeType.Simple1D,
                    blendParameter = input,
                    useAutomaticThresholds = false
                };
                SubAsset(tree);
                tree.children = orderedPoints.Select(p => new ChildMotion
                {
                    motion = Setter(output, p.output), threshold = p.input, timeScale = 1f
                }).ToArray();
                mapDescriptors[tree] = new MapDescriptor
                {
                    input = input,
                    output = output,
                    points = orderedPoints
                };
                return tree;
            }

            public Motion EqualFloat(string input, string output, int value)
            {
                return Map(input, output, new[]
                {
                    Point(value - 0.001f, 0f), Point(value, 1f), Point(value + 0.001f, 0f)
                });
            }

            public Motion AlphaFromDeltaTime(string deltaTime, string output, float responseSeconds)
            {
                if (!alphaBatches.TryGetValue(deltaTime, out var batch))
                {
                    var samples = new[]
                    {
                        0f, 1f / 240f, 1f / 144f, 1f / 90f, 1f / 60f,
                        1f / 45f, 1f / 30f, 1f / 20f, 0.1f, 0.25f
                    };
                    var clips = samples.Select((_, index) =>
                        Clip($"Frame-rate alpha sample {index}")).ToArray();
                    batch = new AlphaBatch
                    {
                        samples = samples,
                        clips = clips,
                        tree = OneDimensional(
                            "Frame-rate-correct alpha vector", deltaTime,
                            samples.Select((sample, index) =>
                                Child(clips[index], sample)).ToArray())
                    };
                    alphaBatches[deltaTime] = batch;
                }

                var binding = EditorCurveBinding.FloatCurve("", typeof(Animator), output);
                for (var index = 0; index < batch.samples.Length; index++)
                {
                    var value = AdvancedVisemeMath.Alpha(
                        batch.samples[index], responseSeconds);
                    AnimationUtility.SetEditorCurve(
                        batch.clips[index], binding,
                        AnimationCurve.Constant(0f, 0f, value));
                }
                return batch.tree;
            }

            public Motion Smooth(string target, string output, string alpha, bool signed)
            {
                var tree = new BlendTree
                {
                    name = $"Smooth {output} toward {target}",
                    blendType = BlendTreeType.Simple1D,
                    blendParameter = alpha,
                    useAutomaticThresholds = false,
                    children = new[]
                    {
                        new ChildMotion { motion = Copy(output, output, signed), threshold = 0f, timeScale = 1f },
                        new ChildMotion { motion = Copy(target, output, signed), threshold = 1f, timeScale = 1f }
                    }
                };
                SubAsset(tree);
                parameterBlendDescriptors[tree] = new ParameterBlendDescriptor
                {
                    driver = alpha,
                    output = output,
                    thresholds = new[] { 0f, 1f },
                    sources = new[] { output, target },
                    signed = signed
                };
                return tree;
            }

            public Motion SmoothVector(
                IReadOnlyList<string> targets,
                IReadOnlyList<string> outputs,
                string alpha,
                string name)
            {
                return InterpolateVector(outputs, targets, outputs, alpha, name);
            }

            public Motion SmoothVectorUnlessHeldSilence(
                IReadOnlyList<string> targets,
                IReadOnlyList<string> outputs,
                string alpha,
                string visemeIndex,
                string speechHistory,
                string stability,
                string name)
            {
                var release = SmoothVector(targets, outputs, alpha, name + " release");
                var freeze = CopyVector(outputs, outputs, name + " freeze");
                return SelectSilenceHold(
                    release, release, freeze,
                    visemeIndex, speechHistory, stability,
                    name + " transient-sil hold");
            }

            public Motion AsymmetricBinarySmooth(
                string binary,
                string output,
                float targetWhenZero,
                string alphaWhenZero,
                float targetWhenOne,
                string alphaWhenOne,
                bool signed)
            {
                var tree = OneDimensional(
                    $"Asymmetric smooth {output} by {binary}", binary,
                    new[]
                    {
                        Child(SmoothConstant(targetWhenZero, output, alphaWhenZero, signed), 0f),
                        Child(SmoothConstant(targetWhenOne, output, alphaWhenOne, signed), 1f)
                    });
                return tree;
            }

            public Motion SmoothUnlessHeldSilence(
                string target,
                string output,
                string alpha,
                string visemeIndex,
                string speechHistory,
                string stability,
                bool signed)
            {
                var release = Smooth(target, output, alpha, signed);
                var freeze = Copy(output, output, signed);
                return SelectSilenceHold(
                    release, release, freeze,
                    visemeIndex, speechHistory, stability,
                    $"Hold {output} across transient sil");
            }

            public Motion InterpolateUnlessHeldSilence(
                string from,
                string to,
                string output,
                string weight,
                string visemeIndex,
                string speechHistory,
                string stability,
                bool signed)
            {
                var release = Interpolate(from, to, output, weight, signed);
                var freeze = Copy(output, output, signed);
                return SelectSilenceHold(
                    release, release, freeze,
                    visemeIndex, speechHistory, stability,
                    $"Hold {output} coarticulation across transient sil");
            }

            public Motion MultiplyUnlessHeldSilence(
                string nonNegativeWeight,
                string value,
                string output,
                string visemeIndex,
                string speechHistory,
                string stability,
                bool valueSigned)
            {
                var release = Multiply(
                    nonNegativeWeight, value, output, valueSigned);
                var freeze = Copy(output, output, valueSigned);
                return SelectSilenceHold(
                    release, release, freeze,
                    visemeIndex, speechHistory, stability,
                    $"Hold {output} gain across transient sil");
            }

            public Motion SmoothActivityWithSilenceHold(
                string visemeIndex,
                string speechHistory,
                string stability,
                string output,
                string attackAlpha,
                string releaseAlpha)
            {
                var active = SmoothConstant(1f, output, attackAlpha, false);
                var inactive = SmoothConstant(0f, output, releaseAlpha, false);
                return SelectSilenceHold(
                    active, inactive, active,
                    visemeIndex, speechHistory, stability,
                    $"Speech activity with transient-sil hold -> {output}");
            }

            public Motion Interpolate(string from, string to, string output, string weight, bool signed)
            {
                var tree = new BlendTree
                {
                    name = $"Interpolate {from} -> {to}",
                    blendType = BlendTreeType.Simple1D,
                    blendParameter = weight,
                    useAutomaticThresholds = false,
                    children = new[]
                    {
                        new ChildMotion { motion = Copy(from, output, signed), threshold = 0f, timeScale = 1f },
                        new ChildMotion { motion = Copy(to, output, signed), threshold = 1f, timeScale = 1f }
                    }
                };
                SubAsset(tree);
                parameterBlendDescriptors[tree] = new ParameterBlendDescriptor
                {
                    driver = weight,
                    output = output,
                    thresholds = new[] { 0f, 1f },
                    sources = new[] { from, to },
                    signed = signed
                };
                return tree;
            }

            public Motion BlendThreeParameters(
                string low,
                string configured,
                string high,
                string output,
                string weight,
                bool signed,
                string name)
            {
                var tree = OneDimensional(
                    name, weight,
                    new[]
                    {
                        Child(Copy(low, output, signed), 0f),
                        Child(Copy(configured, output, signed), 0.5f),
                        Child(Copy(high, output, signed), 1f)
                    });
                parameterBlendDescriptors[tree] = new ParameterBlendDescriptor
                {
                    driver = weight,
                    output = output,
                    thresholds = new[] { 0f, 0.5f, 1f },
                    sources = new[] { low, configured, high },
                    signed = signed
                };
                return tree;
            }

            public Motion InterpolateVector(
                IReadOnlyList<string> from,
                IReadOnlyList<string> to,
                IReadOnlyList<string> outputs,
                string weight,
                string name)
            {
                if (from == null || to == null || outputs == null ||
                    from.Count != to.Count || from.Count != outputs.Count)
                    throw new InvalidOperationException(
                        $"{name} requires equally sized vectors.");
                return OneDimensional(
                    name, weight,
                    new[]
                    {
                        Child(CopyVector(from, outputs, name + " from"), 0f),
                        Child(CopyVector(to, outputs, name + " to"), 1f)
                    });
            }

            public Motion InterpolateMotions(
                Motion from,
                Motion to,
                string weight,
                string name)
            {
                return OneDimensional(
                    name, weight,
                    new[]
                    {
                        Child(from ?? EmptyClip(), 0f),
                        Child(to ?? EmptyClip(), 1f)
                    });
            }

            public Motion SelectSilenceHoldMotion(
                Motion nonSilence,
                Motion silenceRelease,
                Motion silenceHold,
                string visemeIndex,
                string speechHistory,
                string stability,
                string name)
            {
                return SelectSilenceHold(
                    nonSilence, silenceRelease, silenceHold,
                    visemeIndex, speechHistory, stability, name);
            }

            public Motion InterpolateVectorUnlessHeldSilence(
                IReadOnlyList<string> from,
                IReadOnlyList<string> to,
                IReadOnlyList<string> outputs,
                string weight,
                string visemeIndex,
                string speechHistory,
                string stability,
                string name)
            {
                var release = InterpolateVector(from, to, outputs, weight, name + " release");
                var freeze = CopyVector(outputs, outputs, name + " freeze");
                return SelectSilenceHold(
                    release, release, freeze,
                    visemeIndex, speechHistory, stability,
                    name + " transient-sil hold");
            }

            private Motion SmoothConstant(
                float target,
                string output,
                string alpha,
                bool signed)
            {
                return OneDimensional(
                    $"Smooth {output} toward {target:0.###}", alpha,
                    new[]
                    {
                        Child(Copy(output, output, signed), 0f),
                        Child(Setter(output, target), 1f)
                    });
            }

            private Motion SelectSilenceHold(
                Motion nonSilence,
                Motion silenceRelease,
                Motion silenceHold,
                string visemeIndex,
                string speechHistory,
                string stability,
                string name)
            {
                var byHistory = OneDimensional(
                    name + " (history)", speechHistory,
                    new[]
                    {
                        Child(silenceRelease, AdvancedVisemeMath.SpeechHistoryHoldStart),
                        Child(silenceHold, AdvancedVisemeMath.SpeechHistoryHoldFull)
                    });

                // Silence Stability is centered: zero is an exact bypass, the
                // default midpoint applies the complete configured hold, and the
                // upper half extends its release response without exceeding full
                // authority. Encoding this choice inside the same Motion avoids a
                // sibling enable parameter and its extra Animator-frame latency.
                var byStability = OneDimensional(
                    name + " (strength)", stability,
                    new[]
                    {
                        Child(silenceRelease, 0f),
                        Child(byHistory, 0.5f),
                        Child(byHistory, 1f)
                    });

                var tree = OneDimensional(
                    name, visemeIndex,
                    new[]
                    {
                        Child(byStability, 0f),
                        Child(nonSilence, 1f)
                    });
                silenceHoldDescriptors[tree] = new SilenceHoldDescriptor
                {
                    viseme = visemeIndex,
                    history = speechHistory,
                    stability = stability,
                    nonSilence = nonSilence,
                    silenceRelease = silenceRelease,
                    silenceHold = silenceHold
                };
                return tree;
            }

            private BlendTree OneDimensional(
                string name,
                string parameter,
                ChildMotion[] children)
            {
                var tree = new BlendTree
                {
                    name = name,
                    blendType = BlendTreeType.Simple1D,
                    blendParameter = parameter,
                    useAutomaticThresholds = false,
                    children = children
                };
                SubAsset(tree);
                return tree;
            }

            private static ChildMotion Child(Motion motion, float threshold)
            {
                return new ChildMotion
                {
                    motion = motion,
                    threshold = threshold,
                    timeScale = 1f
                };
            }

            public Motion Linear(string output, IEnumerable<Term> terms)
            {
                var tree = Direct("Linear -> " + output);
                var children = new List<ChildMotion>();
                foreach (var term in terms)
                {
                    if (term.constant)
                    {
                        children.Add(new ChildMotion
                        {
                            motion = Setter(output, term.multiplier),
                            directBlendParameter = AlwaysOne,
                            timeScale = 1f
                        });
                    }
                    else if (term.signed)
                    {
                        children.Add(new ChildMotion
                        {
                            motion = Map(term.parameter, output, new[]
                            {
                                Point(-2f, -2f * term.multiplier), Point(0f, 0f),
                                Point(2f, 2f * term.multiplier)
                            }),
                            directBlendParameter = AlwaysOne,
                            timeScale = 1f
                        });
                    }
                    else
                    {
                        // A Direct BlendTree's weight is already the nonnegative
                        // input parameter. Wrapping this clip in WeightedSetter and
                        // then weighting that tree by AlwaysOne emitted a redundant
                        // scalar tree for every affine coefficient.
                        children.Add(new ChildMotion
                        {
                            motion = Setter(output, term.multiplier),
                            directBlendParameter = term.parameter,
                            timeScale = 1f
                        });
                    }
                }
                children.Add(new ChildMotion { motion = Setter(output, 0f), directBlendParameter = AlwaysOne, timeScale = 1f });
                tree.children = children.ToArray();
                return tree;
            }

            public Motion Multiply(string nonNegativeWeight, string value, string output, bool valueSigned)
            {
                var tree = Direct($"Multiply {nonNegativeWeight} * {value} -> {output}");
                tree.children = new[]
                {
                    new ChildMotion { motion = Copy(value, output, valueSigned), directBlendParameter = nonNegativeWeight, timeScale = 1f },
                    new ChildMotion { motion = Setter(output, 0f), directBlendParameter = AlwaysOne, timeScale = 1f }
                };
                return tree;
            }

            public Motion ScaleByInverseUnitWeight(
                string nonNegativeValue,
                string unitWeight,
                string output,
                float scale)
            {
                scale = Mathf.Max(0f, scale);
                var unsuppressed = Mathf.Approximately(scale, 1f)
                    ? Copy(nonNegativeValue, output, false)
                    : WeightedSetter(nonNegativeValue, output, scale);
                return OneDimensional(
                    $"Scale {nonNegativeValue} by inverse {unitWeight}",
                    unitWeight,
                    new[]
                    {
                        Child(unsuppressed, 0f),
                        Child(Setter(output, 0f), 1f)
                    });
            }

            public Motion Abs(string input, string output)
            {
                return Map(input, output, new[] { Point(-2f, 2f), Point(0f, 0f), Point(2f, 2f) });
            }

            public Motion Max(string a, string b, string output)
            {
                var diff = Param("Max/" + Sanitize(output) + "/Diff", 0f);
                var abs = Param("Max/" + Sanitize(output) + "/Abs", 0f);
                var tree = Direct("Max -> " + output);
                tree.children = new[]
                {
                    Child(Linear(diff, new[] { Term.Positive(a, 1f), Term.Positive(b, -1f) })),
                    Child(Abs(diff, abs)),
                    Child(Linear(output, new[] { Term.Positive(a, 0.5f), Term.Positive(b, 0.5f), Term.Positive(abs, 0.5f) }))
                };
                return tree;
            }

            public Motion Min(string a, string b, string output)
            {
                var diff = Param("Min/" + Sanitize(output) + "/Diff", 0f);
                var abs = Param("Min/" + Sanitize(output) + "/Abs", 0f);
                var tree = Direct("Min -> " + output);
                tree.children = new[]
                {
                    Child(Linear(diff, new[] { Term.Positive(a, 1f), Term.Positive(b, -1f) })),
                    Child(Abs(diff, abs)),
                    Child(Linear(output, new[] { Term.Positive(a, 0.5f), Term.Positive(b, 0.5f), Term.Positive(abs, -0.5f) }))
                };
                return tree;
            }

            public Motion DrivePose(string weight, Motion pose, bool signed)
            {
                if (!signed)
                {
                    var tree = Direct("Drive pose by " + weight);
                    tree.children = new[]
                    {
                        new ChildMotion { motion = pose, directBlendParameter = weight, timeScale = 1f },
                        new ChildMotion { motion = EmptyClip(), directBlendParameter = AlwaysOne, timeScale = 1f }
                    };
                    return tree;
                }

                if (ContainsBlendShapeCurves(pose))
                    throw new InvalidOperationException(
                        $"Signed pose '{pose?.name}' has no build-only inverse geometry. " +
                        "A negative final blendshape weight would be clamped by VRChat.");

                var negative = NegatedPose(pose);
                var zero = EmptyClip();
                var signedTree = new BlendTree
                {
                    name = "Signed pose " + weight,
                    blendType = BlendTreeType.Simple1D,
                    blendParameter = weight,
                    useAutomaticThresholds = false,
                    children = new[]
                    {
                        new ChildMotion { motion = negative, threshold = -1f, timeScale = 1f },
                        new ChildMotion { motion = zero, threshold = 0f, timeScale = 1f },
                        new ChildMotion { motion = pose, threshold = 1f, timeScale = 1f }
                    }
                };
                SubAsset(signedTree);
                return signedTree;
            }

            public Motion DrivePoseProduct(
                string firstWeight,
                string secondWeight,
                Motion pose,
                string name)
            {
                // Both ownership weights are nonnegative. Nesting their Direct
                // weights evaluates g * projected geometry atomically and avoids
                // publishing a correction AAP that would add another Animator
                // frame before the carrier reaches the mesh.
                var tree = Direct(name + " product pose");
                tree.children = new[]
                {
                    new ChildMotion
                    {
                        motion = DrivePose(secondWeight, pose, false),
                        directBlendParameter = firstWeight,
                        timeScale = 1f
                    },
                    new ChildMotion
                    {
                        motion = EmptyClip(),
                        directBlendParameter = AlwaysOne,
                        timeScale = 1f
                    }
                };
                return tree;
            }

            public Motion DrivePose(string weight, Motion positive, Motion negative, bool signed)
            {
                if (!signed) return positive != null ? DrivePose(weight, positive, false) : EmptyClip();
                if (positive == null && negative == null) return EmptyClip();
                positive = positive ?? EmptyClip();
                if (negative == null && ContainsBlendShapeCurves(positive))
                    throw new InvalidOperationException(
                        $"Signed pose '{positive.name}' has no negative endpoint or build-only " +
                        "inverse geometry. A negative final blendshape weight would be clamped by VRChat.");
                negative = negative ?? NegatedPose(positive);
                var tree = new BlendTree
                {
                    name = "Signed pose " + weight,
                    blendType = BlendTreeType.Simple1D,
                    blendParameter = weight,
                    useAutomaticThresholds = false,
                    children = new[]
                    {
                        new ChildMotion { motion = negative, threshold = -1f, timeScale = 1f },
                        new ChildMotion { motion = EmptyClip(), threshold = 0f, timeScale = 1f },
                        new ChildMotion { motion = positive, threshold = 1f, timeScale = 1f }
                    }
                };
                SubAsset(tree);
                return tree;
            }

            public Motion DrivePoseAtThresholds(
                string input,
                Motion positive,
                Motion negative,
                float positiveThreshold,
                float negativeThreshold,
                bool signed)
            {
                var zero = EmptyClip();
                if (!signed)
                {
                    if (positive == null) return zero;
                    var threshold = Mathf.Max(1e-5f, positiveThreshold);
                    return OneDimensional(
                        "Native pose by " + input,
                        input,
                        new[]
                        {
                            Child(zero, 0f),
                            Child(positive, threshold)
                        });
                }

                var children = new List<ChildMotion>();
                if (negative != null)
                    children.Add(Child(
                        negative, Mathf.Min(-1e-5f, negativeThreshold)));
                else
                    children.Add(Child(zero, -1f));
                children.Add(Child(zero, 0f));
                if (positive != null)
                    children.Add(Child(
                        positive, Mathf.Max(1e-5f, positiveThreshold)));
                else
                    children.Add(Child(zero, 1f));
                return OneDimensional(
                    "Native signed pose by " + input,
                    input,
                    children.ToArray());
            }

            public Motion SelectMotion(
                string weight,
                Motion whenZero,
                Motion whenOne,
                string name)
            {
                whenZero = whenZero ?? EmptyClip();
                whenOne = whenOne ?? EmptyClip();
                var tree = OneDimensional(
                    name, weight,
                    new[]
                    {
                        Child(whenZero, 0f),
                        Child(whenOne, 1f)
                    });
                binarySelectDescriptors[tree] = new BinarySelectDescriptor
                {
                    driver = weight,
                    whenZero = whenZero,
                    whenOne = whenOne
                };
                return tree;
            }

            public AnimationClip BlendShapeClip(string path, string blendShape, float value)
            {
                var clip = Clip("Blendshape " + blendShape);
                if (string.IsNullOrEmpty(blendShape)) return clip;
                if (!IsBlendShapeWeightInRange(value))
                    throw new InvalidOperationException(
                        $"Blendshape '{blendShape}' requests weight {value:G9}; VRChat " +
                        "supports only the 0..100 final range.");
                AnimationUtility.SetEditorCurve(clip,
                    EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer), "blendShape." + blendShape),
                    AnimationCurve.Constant(0f, 0f, value));
                return clip;
            }

            public AnimationClip CompositeBlendShapeClip(
                string name,
                IEnumerable<(string path, string blendShape, float value)> curves)
            {
                var values = curves?
                    .Where(curve => !string.IsNullOrEmpty(curve.blendShape))
                    .ToArray() ??
                    Array.Empty<(string path, string blendShape, float value)>();
                if (values.Length == 0) return null;
                var clip = Clip(name);
                foreach (var curve in values)
                {
                    if (!IsBlendShapeWeightInRange(curve.value))
                        throw new InvalidOperationException(
                            $"Blendshape '{curve.blendShape}' requests weight " +
                            $"{curve.value:G9}; VRChat supports only the 0..100 final range.");
                    AnimationUtility.SetEditorCurve(clip,
                        EditorCurveBinding.FloatCurve(
                            curve.path ?? string.Empty,
                            typeof(SkinnedMeshRenderer),
                            "blendShape." + curve.blendShape),
                        AnimationCurve.Constant(0f, 0f, curve.value));
                }
                return clip;
            }

            public AnimationClip PoseClip(AnimationClip source, string name)
            {
                var clip = Clip(name);
                if (source == null) return clip;
                var time = Mathf.Max(0f, source.length);
                foreach (var binding in AnimationUtility.GetCurveBindings(source))
                {
                    var curve = AnimationUtility.GetEditorCurve(source, binding);
                    if (curve == null) continue;
                    var endpoint = curve.Evaluate(time);
                    ValidateBlendShapeEndpoint(binding, endpoint, source.name);
                    AnimationUtility.SetEditorCurve(clip, binding,
                        AnimationCurve.Constant(0f, 0f, endpoint));
                }
                return clip;
            }

            public AnimationClip TargetRendererBlendShapePose(
                AnimationClip source,
                string name,
                string rendererPath,
                Mesh targetMesh)
            {
                if (source == null) return null;
                var clip = Clip(name);
                var time = Mathf.Max(0f, source.length);
                foreach (var sourceBinding in AnimationUtility.GetCurveBindings(source))
                {
                    if (!IsLinearCorrectionCurve(sourceBinding, rendererPath, targetMesh)) continue;
                    var curve = AnimationUtility.GetEditorCurve(source, sourceBinding);
                    if (curve == null) continue;
                    var endpoint = curve.Evaluate(time);
                    ValidateBlendShapeEndpoint(sourceBinding, endpoint, source.name);
                    AnimationUtility.SetEditorCurve(clip, sourceBinding,
                        AnimationCurve.Constant(0f, 0f, endpoint));
                }
                return AnimationUtility.GetCurveBindings(clip).Length == 0 ? null : clip;
            }

            public Motion NegatedPose(Motion motion)
            {
                if (!(motion is AnimationClip source)) return EmptyClip();
                var clip = Clip(source.name + " Negative");
                foreach (var binding in AnimationUtility.GetCurveBindings(source))
                {
                    var curve = AnimationUtility.GetEditorCurve(source, binding);
                    if (curve == null) continue;
                    var keys = curve.keys;
                    for (var i = 0; i < keys.Length; i++) keys[i].value = -keys[i].value;
                    curve.keys = keys;
                    AnimationUtility.SetEditorCurve(clip, binding, curve);
                }
                return clip;
            }

            private static bool ContainsBlendShapeCurves(Motion motion)
            {
                return motion is AnimationClip clip &&
                       AnimationUtility.GetCurveBindings(clip).Any(binding =>
                           binding.type == typeof(SkinnedMeshRenderer) &&
                           binding.propertyName.StartsWith(
                               "blendShape.", StringComparison.Ordinal));
            }

            private static bool IsBlendShapeWeightInRange(float value)
            {
                return !float.IsNaN(value) && !float.IsInfinity(value) &&
                       value >= -1e-5f && value <= 100.00001f;
            }

            private static void ValidateBlendShapeEndpoint(
                EditorCurveBinding binding,
                float value,
                string poseName)
            {
                if (binding.type != typeof(SkinnedMeshRenderer) ||
                    !binding.propertyName.StartsWith(
                        "blendShape.", StringComparison.Ordinal) ||
                    IsBlendShapeWeightInRange(value)) return;
                throw new InvalidOperationException(
                    $"Pose '{poseName}' drives '{binding.propertyName}' to {value:G9}. " +
                    "Advanced Viseme cannot preserve an endpoint outside VRChat's " +
                    "0..100 final blendshape range.");
            }

            public void AddOperation(BlendTree root, Motion motion)
            {
                if (root == null || motion == null) return;
                // AlphaFromDeltaTime deliberately returns one mutable batched
                // lookup for every alpha request. Deduplicate only that motion;
                // other repeated references can be intentional additive output
                // contributions (notably linked residual geometry).
                if (alphaBatches.Values.Any(batch => batch.tree == motion) &&
                    root.children.Any(child => child.motion == motion)) return;

                AppendOperationChild(root, new ChildMotion
                {
                    motion = motion,
                    directBlendParameter = AlwaysOne,
                    timeScale = 1f
                });
            }

            private void AppendOperationChild(BlendTree root, ChildMotion child)
            {
                if (child.motion == null) return;
                var unweighted = child.directBlendParameter == AlwaysOne &&
                                 Mathf.Approximately(child.timeScale, 1f) &&
                                 !child.mirror &&
                                 Mathf.Approximately(child.cycleOffset, 0f);

                // The math/output roots are non-normalized Direct trees. An
                // unweighted Direct child is pure grouping, so lower its children
                // directly into the parent. This is the same semantics-preserving
                // rewrite VRCFury performs later, but doing it here prevents the
                // generated controller from containing hundreds of scalar wrapper
                // trees in the first place.
                if (unweighted && child.motion is BlendTree direct &&
                    direct.blendType == BlendTreeType.Direct)
                {
                    foreach (var nested in direct.children)
                        AppendOperationChild(root, nested);
                    return;
                }

                if (unweighted &&
                    silenceHoldDescriptors.TryGetValue(child.motion, out var silenceHold) &&
                    TryAppendSilenceHoldBatch(root, silenceHold)) return;

                if (unweighted &&
                    binarySelectDescriptors.TryGetValue(child.motion, out var binarySelect) &&
                    TryAppendBinarySelectBatch(root, binarySelect)) return;

                if (unweighted && mapDescriptors.TryGetValue(child.motion, out var map) &&
                    TryAppendMapBatch(root, map)) return;

                if (unweighted &&
                    parameterBlendDescriptors.TryGetValue(child.motion, out var parameterBlend) &&
                    TryAppendParameterBlendBatch(root, parameterBlend)) return;

                AppendRawChild(root, child);
            }

            private static void AppendRawChild(BlendTree root, ChildMotion child)
            {
                var children = root.children.ToList();
                children.Add(child);
                root.children = children.ToArray();
            }

            private bool TryAppendBinarySelectBatch(
                BlendTree root,
                BinarySelectDescriptor descriptor)
            {
                if (descriptor == null) return false;
                var descriptorBindings = MotionBindings(descriptor.whenZero)
                    .Concat(MotionBindings(descriptor.whenOne))
                    .ToHashSet(StringComparer.Ordinal);
                var key = (root, descriptor.driver ?? string.Empty);
                if (!binarySelectBatches.TryGetValue(key, out var batch))
                {
                    var whenZero = Direct($"Vector select {descriptor.driver} zero");
                    var whenOne = Direct($"Vector select {descriptor.driver} one");
                    batch = new BinarySelectBatch
                    {
                        whenZero = whenZero,
                        whenOne = whenOne,
                        tree = OneDimensional(
                            $"Vector select by {descriptor.driver}", descriptor.driver,
                            new[] { Child(whenZero, 0f), Child(whenOne, 1f) })
                    };
                    binarySelectBatches[key] = batch;
                    AppendRawChild(root, Child(batch.tree));
                }
                if (batch.bindings.Overlaps(descriptorBindings)) return false;

                batch.bindings.UnionWith(descriptorBindings);
                AppendOperationChild(batch.whenZero, Child(descriptor.whenZero));
                AppendOperationChild(batch.whenOne, Child(descriptor.whenOne));
                return true;
            }

            private bool TryAppendSilenceHoldBatch(
                BlendTree root,
                SilenceHoldDescriptor descriptor)
            {
                if (descriptor == null) return false;
                var descriptorBindings = MotionBindings(descriptor.nonSilence)
                    .Concat(MotionBindings(descriptor.silenceRelease))
                    .Concat(MotionBindings(descriptor.silenceHold))
                    .ToHashSet(StringComparer.Ordinal);
                var key = (
                    root,
                    descriptor.viseme ?? string.Empty,
                    descriptor.history ?? string.Empty,
                    descriptor.stability ?? string.Empty);
                if (!silenceHoldBatches.TryGetValue(key, out var batch))
                {
                    var nonSilence = Direct("Vector silence hold active");
                    var silenceRelease = Direct("Vector silence hold release");
                    var silenceHold = Direct("Vector silence hold freeze");
                    var byHistory = OneDimensional(
                        "Vector silence hold history", descriptor.history,
                        new[]
                        {
                            Child(silenceRelease, AdvancedVisemeMath.SpeechHistoryHoldStart),
                            Child(silenceHold, AdvancedVisemeMath.SpeechHistoryHoldFull)
                        });
                    var byStability = OneDimensional(
                        "Vector silence hold strength", descriptor.stability,
                        new[]
                        {
                            Child(silenceRelease, 0f),
                            Child(byHistory, 0.5f),
                            Child(byHistory, 1f)
                        });
                    batch = new SilenceHoldBatch
                    {
                        nonSilence = nonSilence,
                        silenceRelease = silenceRelease,
                        silenceHold = silenceHold,
                        tree = OneDimensional(
                            "Vector transient-silence hold", descriptor.viseme,
                            new[]
                            {
                                Child(byStability, 0f),
                                Child(nonSilence, 1f)
                            })
                    };
                    silenceHoldBatches[key] = batch;
                    AppendRawChild(root, Child(batch.tree));
                }
                if (batch.bindings.Overlaps(descriptorBindings)) return false;

                batch.bindings.UnionWith(descriptorBindings);
                AppendOperationChild(batch.nonSilence, Child(descriptor.nonSilence));
                AppendOperationChild(batch.silenceRelease, Child(descriptor.silenceRelease));
                AppendOperationChild(batch.silenceHold, Child(descriptor.silenceHold));
                return true;
            }

            private static IEnumerable<string> MotionBindings(Motion motion)
            {
                var result = new HashSet<string>(StringComparer.Ordinal);
                var visited = new HashSet<Motion>();
                void Visit(Motion current)
                {
                    if (current == null || !visited.Add(current)) return;
                    if (current is AnimationClip clip)
                    {
                        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                            result.Add(
                                $"{binding.type?.FullName}|{binding.path}|{binding.propertyName}");
                        return;
                    }
                    if (!(current is BlendTree tree)) return;
                    foreach (var child in tree.children) Visit(child.motion);
                }
                Visit(motion);
                return result;
            }

            private bool TryAppendMapBatch(BlendTree root, MapDescriptor descriptor)
            {
                if (descriptor == null || descriptor.points == null ||
                    descriptor.points.Length == 0 ||
                    descriptor.points.GroupBy(point => point.input).Any(group => group.Count() > 1))
                    return false;

                var key = (root, descriptor.input ?? string.Empty);
                if (!mapBatches.TryGetValue(key, out var batch))
                {
                    batch = new MapBatch
                    {
                        tree = OneDimensional(
                            $"Vector map {descriptor.input}", descriptor.input,
                            Array.Empty<ChildMotion>())
                    };
                    mapBatches[key] = batch;
                    AppendRawChild(root, Child(batch.tree));
                }
                else
                {
                    var existingThresholds = batch.descriptors
                        .SelectMany(existing => existing.points.Select(point => point.input))
                        .Distinct().Count();
                    var existingOutputs = batch.descriptors
                        .Select(existing => existing.output)
                        .Distinct(StringComparer.Ordinal).Count();
                    var combinedThresholds = batch.descriptors
                        .SelectMany(existing => existing.points.Select(point => point.input))
                        .Concat(descriptor.points.Select(point => point.input))
                        .Distinct().Count();
                    var combinedOutputs = batch.descriptors
                        .Select(existing => existing.output)
                        .Append(descriptor.output)
                        .Distinct(StringComparer.Ordinal).Count();
                    var currentBindings = existingThresholds * existingOutputs;
                    var separateBindings = currentBindings + descriptor.points.Length;
                    var combinedBindings = combinedThresholds * combinedOutputs;
                    // A shared lookup saves one connected tree, but Unity still
                    // evaluates every curve on every knot. Keep the vector form
                    // only when its union grid does not create a property cross-
                    // product large enough to erase that graph saving.
                    if (combinedBindings > separateBindings + 2) return false;
                }

                batch.descriptors.Add(descriptor);
                RebuildMapBatch(batch);
                return true;
            }

            private void RebuildMapBatch(MapBatch batch)
            {
                var thresholds = batch.descriptors
                    .SelectMany(descriptor => descriptor.points.Select(point => point.input))
                    .Distinct()
                    .OrderBy(value => value)
                    .ToArray();
                var outputs = batch.descriptors
                    .Select(descriptor => descriptor.output)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray();

                batch.tree.children = thresholds.Select(threshold => Child(
                    MultiSetter(
                        $"{batch.tree.name} at {threshold:0.#####}",
                        outputs.Select(output => new KeyValuePair<string, float>(
                            output,
                            batch.descriptors
                                .Where(descriptor => descriptor.output == output)
                                .Sum(descriptor => EvaluateMap(descriptor.points, threshold))))),
                    threshold)).ToArray();
            }

            private static float EvaluateMap(
                IReadOnlyList<(float input, float output)> points,
                float input)
            {
                if (input <= points[0].input) return points[0].output;
                for (var i = 1; i < points.Count; i++)
                {
                    if (input > points[i].input) continue;
                    var previous = points[i - 1];
                    var next = points[i];
                    var denominator = next.input - previous.input;
                    if (Mathf.Abs(denominator) <= 1e-8f) return next.output;
                    return Mathf.LerpUnclamped(
                        previous.output, next.output,
                        (input - previous.input) / denominator);
                }
                return points[points.Count - 1].output;
            }

            private bool TryAppendParameterBlendBatch(
                BlendTree root,
                ParameterBlendDescriptor descriptor)
            {
                if (descriptor == null || descriptor.thresholds == null ||
                    descriptor.sources == null ||
                    descriptor.thresholds.Length == 0 ||
                    descriptor.thresholds.Length != descriptor.sources.Length)
                    return false;

                var thresholdKey = string.Join(",",
                    descriptor.thresholds.Select(value => value.ToString("R")));
                var key = (root, descriptor.driver ?? string.Empty, thresholdKey);
                if (!parameterBlendBatches.TryGetValue(key, out var batch))
                {
                    batch = new ParameterBlendBatch
                    {
                        tree = OneDimensional(
                            $"Vector blend by {descriptor.driver}", descriptor.driver,
                            Array.Empty<ChildMotion>())
                    };
                    parameterBlendBatches[key] = batch;
                    AppendRawChild(root, Child(batch.tree));
                }

                // Two independent operations writing the same AAP are additive,
                // not a vector lane. Keep the later operation separate instead of
                // silently changing that authored behavior.
                if (batch.descriptors.Any(existing => existing.output == descriptor.output))
                    return false;

                batch.descriptors.Add(descriptor);
                RebuildParameterBlendBatch(batch);
                return true;
            }

            private void RebuildParameterBlendBatch(ParameterBlendBatch batch)
            {
                var outputs = batch.descriptors.Select(descriptor => descriptor.output).ToArray();
                var signed = batch.descriptors.Select(descriptor => descriptor.signed).ToArray();
                var thresholds = batch.descriptors[0].thresholds;
                batch.tree.children = Enumerable.Range(0, thresholds.Length)
                    .Select(index => Child(
                        CopyMixedVector(
                            batch.descriptors.Select(descriptor => descriptor.sources[index]).ToArray(),
                            outputs,
                            signed,
                            $"{batch.tree.name} sample {index}"),
                        thresholds[index]))
                    .ToArray();
            }

            public void PruneUnreachableMotions()
            {
                Motion OptimizeMotion(Motion motion)
                {
                    if (!(motion is BlendTree tree)) return motion;

                    var optimizedChildren = tree.children;
                    for (var i = 0; i < optimizedChildren.Length; i++)
                    {
                        var child = optimizedChildren[i];
                        child.motion = OptimizeMotion(child.motion);
                        optimizedChildren[i] = child;
                    }
                    tree.children = optimizedChildren;

                    if (tree.blendType == BlendTreeType.Direct)
                    {
                        var flattened = new List<ChildMotion>();
                        foreach (var child in tree.children)
                        {
                            if (child.directBlendParameter == AlwaysOne &&
                                Mathf.Approximately(child.timeScale, 1f) &&
                                !child.mirror &&
                                Mathf.Approximately(child.cycleOffset, 0f) &&
                                child.motion is BlendTree direct &&
                                direct.blendType == BlendTreeType.Direct)
                                flattened.AddRange(direct.children);
                            else
                                flattened.Add(child);
                        }

                        // Factor w*A + w*B as w*(A+B). Unity clamps Direct
                        // weights to nonnegative values, but distributivity still
                        // holds exactly for generated Direct math trees. This
                        // turns repeated scalar products sharing one gate into a
                        // single vector product without adding an AAP stage.
                        var factoredIndices = new HashSet<int>();
                        var replacements = new Dictionary<int, ChildMotion>();
                        var groups = flattened
                            .Select((child, index) => (child, index))
                            .Where(item => item.child.directBlendParameter != AlwaysOne &&
                                           Mathf.Approximately(item.child.timeScale, 1f) &&
                                           !item.child.mirror &&
                                           Mathf.Approximately(item.child.cycleOffset, 0f) &&
                                           item.child.motion is BlendTree nested &&
                                           nested.blendType == BlendTreeType.Direct)
                            .GroupBy(item => item.child.directBlendParameter,
                                StringComparer.Ordinal)
                            .Where(group => group.Count() > 1)
                            .ToArray();
                        foreach (var group in groups)
                        {
                            var items = group.ToArray();
                            var factored = Direct("Vector product by " + group.Key);
                            factored.children = items
                                .SelectMany(item => ((BlendTree)item.child.motion).children)
                                .ToArray();
                            var replacement = items[0].child;
                            replacement.motion = OptimizeMotion(factored);
                            replacements[items[0].index] = replacement;
                            foreach (var item in items.Skip(1)) factoredIndices.Add(item.index);
                        }

                        if (groups.Length > 0)
                        {
                            var rewritten = new List<ChildMotion>();
                            for (var index = 0; index < flattened.Count; index++)
                            {
                                if (factoredIndices.Contains(index)) continue;
                                rewritten.Add(replacements.TryGetValue(index, out var replacement)
                                    ? replacement
                                    : flattened[index]);
                            }
                            tree.children = rewritten.ToArray();
                        }
                        else
                        {
                            tree.children = flattened.ToArray();
                        }
                    }

                    if (tree.blendType == BlendTreeType.Direct &&
                        tree.children.Length == 1 &&
                        tree.children[0].directBlendParameter == AlwaysOne &&
                        Mathf.Approximately(tree.children[0].timeScale, 1f) &&
                        !tree.children[0].mirror &&
                        Mathf.Approximately(tree.children[0].cycleOffset, 0f))
                        return tree.children[0].motion;
                    return tree;
                }

                foreach (var layer in controller.layers)
                foreach (var state in layer.stateMachine.states)
                    state.state.motion = OptimizeMotion(state.state.motion);

                var reachable = new HashSet<Motion>();
                Action<Motion> visitMotion = null;
                visitMotion = motion =>
                {
                    if (motion == null || !reachable.Add(motion)) return;
                    if (!(motion is BlendTree tree)) return;
                    foreach (var child in tree.children) visitMotion(child.motion);
                };
                Action<AnimatorStateMachine> visitStateMachine = null;
                visitStateMachine = stateMachine =>
                {
                    if (stateMachine == null) return;
                    foreach (var state in stateMachine.states)
                        visitMotion(state.state.motion);
                    foreach (var child in stateMachine.stateMachines)
                        visitStateMachine(child.stateMachine);
                };
                foreach (var layer in controller.layers)
                    visitStateMachine(layer.stateMachine);

                var unreachable = subAssets
                    .OfType<Motion>()
                    .Where(motion => !reachable.Contains(motion))
                    .OrderByDescending(motion => motion is BlendTree)
                    .ToArray();
                foreach (var motion in unreachable)
                {
                    subAssets.Remove(motion);
                    UnityEngine.Object.DestroyImmediate(motion, true);
                }
            }

            public void SubAsset(UnityEngine.Object obj)
            {
                if (obj == null || subAssets.Contains(obj) || AssetDatabase.Contains(obj)) return;
                obj.hideFlags = HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(obj, controller);
                subAssets.Add(obj);
            }

            private static ChildMotion Child(Motion motion)
            {
                return new ChildMotion { motion = motion, directBlendParameter = AlwaysOne, timeScale = 1f };
            }

            private static string Sanitize(string value)
            {
                return new string((value ?? string.Empty).Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
            }
        }
    }
}
