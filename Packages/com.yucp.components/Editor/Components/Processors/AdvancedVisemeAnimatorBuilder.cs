using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;
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
            public Dictionary<AdvancedVisemeArticulator, string> trackingParameterNames;
            public string[] sourceVisemeBlendShapes;
            public AdvancedVisemeMeshCalibrator.Result calibration;
            public IReadOnlyList<AdvancedVisemeMeshCalibrator.BasisInput> calibrationBasis;
            public Dictionary<AdvancedVisemeArticulator, string> resolvedBlendShapes;
            public Dictionary<AdvancedVisemeArticulator, AdvancedVisemeExternalPose> externalPoses;
            public Mesh targetMesh;
            public bool trackingEnabled;
            public HashSet<string> existingExpressionParameters;
        }

        internal sealed class Result
        {
            public AnimatorController controller;
            public VRCExpressionParameters parameters;
            public readonly List<string> globalParameters = new List<string>();
            public readonly List<string> externalParameters = new List<string>();
            public readonly Dictionary<AdvancedVisemeArticulator, string> articulationParameters =
                new Dictionary<AdvancedVisemeArticulator, string>();
            public string manualTrackingParameter;
            public string trackingBlendParameter;
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
            graph.AddParameter("IsLocal", AnimatorControllerParameterType.Bool, 0f);
            result.externalParameters.Add("Viseme");
            result.externalParameters.Add("Voice");
            result.externalParameters.Add("IsLocal");

            var time = graph.Param("Time", 0f);
            var lastTime = graph.Param("LastTime", 0f);
            var frameTime = graph.Param("FrameTime", 1f / 60f);
            AddTimeLayer(controller, graph, time);
            var visemeIndex = graph.Param("Viseme/Index", 0f);
            AddIntToFloatLayer(controller, graph, "Viseme", visemeIndex, "YUCP AVR Viseme Decoder");

            var mathRoot = graph.Direct("Reconstruction Math");
            graph.AddOperation(mathRoot, graph.Linear(frameTime, new[]
            {
                Term.Positive(time, 1f), Term.Positive(lastTime, -1f)
            }));
            graph.AddOperation(mathRoot, graph.Copy(time, lastTime, false));

            var alphaViseme = graph.Param("Alpha/Viseme", 0.5f);
            graph.AddOperation(mathRoot, graph.AlphaFromDeltaTime(frameTime, alphaViseme, request.profile.visemeResponseSeconds));

            var voiceRaw = graph.Param("Voice/Raw", 0f);
            graph.AddOperation(mathRoot, graph.Map("Voice", voiceRaw, new[]
            {
                Point(0f, 0f),
                Point(request.profile.voiceNoiseFloor, 0f),
                Point(Mathf.Lerp(request.profile.voiceNoiseFloor, request.profile.voiceFullScale, 0.5f), 0.5f),
                Point(request.profile.voiceFullScale, 1f),
                Point(1f, 1f)
            }));
            var voiceFast = graph.Param("Voice/Fast", 0f);
            var voiceSlow = graph.Param(prefix + "/Speech/Energy", 0f, false);
            graph.AddOperation(mathRoot, graph.Smooth(voiceRaw, voiceFast, alphaViseme, false));
            graph.AddOperation(mathRoot, graph.Smooth(voiceFast, voiceSlow, alphaViseme, false));
            result.globalParameters.Add(voiceSlow);

            var voiceGain = graph.Param("Voice/Gain", request.profile.quietSpeechFloor);
            graph.AddOperation(mathRoot, graph.Linear(voiceGain, new[]
            {
                Term.Constant(request.profile.quietSpeechFloor),
                Term.Positive(voiceSlow, 1f - request.profile.quietSpeechFloor)
            }));

            var voiceVelocity = graph.Param("Voice/Velocity", 0f);
            graph.AddOperation(mathRoot, graph.Linear(voiceVelocity, new[]
            {
                Term.Positive(voiceFast, 1f / Mathf.Max(0.005f, request.profile.visemeResponseSeconds)),
                Term.Positive(voiceSlow, -1f / Mathf.Max(0.005f, request.profile.visemeResponseSeconds))
            }));
            var onset = graph.Param(prefix + "/Speech/Onset", 0f, false);
            var release = graph.Param(prefix + "/Speech/Release", 0f, false);
            graph.AddOperation(mathRoot, graph.Map(voiceVelocity, onset, new[] { Point(-1f, 0f), Point(0f, 0f), Point(1f, 1f) }));
            graph.AddOperation(mathRoot, graph.Map(voiceVelocity, release, new[] { Point(-1f, 1f), Point(0f, 0f), Point(1f, 0f) }));
            result.globalParameters.Add(onset);
            result.globalParameters.Add(release);

            var rawVisemes = new string[VisemeReconstructionProfile.VisemeCount];
            var fastVisemes = new string[VisemeReconstructionProfile.VisemeCount];
            var slowVisemes = new string[VisemeReconstructionProfile.VisemeCount];
            var speechWeights = new string[VisemeReconstructionProfile.VisemeCount];
            var fastSpeechWeights = new string[VisemeReconstructionProfile.VisemeCount];
            for (var i = 0; i < rawVisemes.Length; i++)
            {
                var defaultValue = i == 0 ? 1f : 0f;
                rawVisemes[i] = graph.Param($"Viseme/{i}/Raw", defaultValue);
                fastVisemes[i] = graph.Param($"Viseme/{i}/Fast", defaultValue);
                slowVisemes[i] = graph.Param(prefix + "/Viseme/" + VisemeReconstructionProfile.VisemeNames[i], defaultValue, false);
                graph.AddOperation(mathRoot, graph.EqualFloat(visemeIndex, rawVisemes[i], i));
                graph.AddOperation(mathRoot, graph.Smooth(rawVisemes[i], fastVisemes[i], alphaViseme, false));
                graph.AddOperation(mathRoot, graph.Smooth(fastVisemes[i], slowVisemes[i], alphaViseme, false));
                result.globalParameters.Add(slowVisemes[i]);

                speechWeights[i] = graph.Param($"Viseme/{i}/SpeechWeight", 0f);
                fastSpeechWeights[i] = graph.Param($"Viseme/{i}/FastSpeechWeight", 0f);
                graph.AddOperation(mathRoot, graph.Multiply(slowVisemes[i], voiceGain, speechWeights[i], false));
                graph.AddOperation(mathRoot, graph.Multiply(fastVisemes[i], voiceGain, fastSpeechWeights[i], false));
            }

            string trackingBlend = null;
            string alphaTracking = null;
            string localFactor = null;
            string oneMinusTracking = null;
            var trackingFast = new Dictionary<AdvancedVisemeArticulator, string>();
            var trackingSlow = new Dictionary<AdvancedVisemeArticulator, string>();

            if (request.trackingEnabled)
            {
                result.manualTrackingParameter = prefix + "/FaceTrackingEnabled";
                graph.AddParameter(result.manualTrackingParameter, AnimatorControllerParameterType.Float, 1f);
                result.externalParameters.Add(result.manualTrackingParameter);
                var activeParameter = string.IsNullOrEmpty(request.trackingActiveParameter)
                    ? "LipTrackingActive"
                    : request.trackingActiveParameter;
                graph.AddParameter(activeParameter, AnimatorControllerParameterType.Float, 0f);
                result.externalParameters.Add(activeParameter);
                var trackingGate = graph.Param("TrackingGate", 0f);
                graph.AddOperation(mathRoot, graph.Multiply(activeParameter, result.manualTrackingParameter, trackingGate, false));
                AddBoolFloatLayer(controller, graph, "IsLocal", "Local Tracking Selector", out localFactor);

                var alphaLocal = graph.Param("Alpha/TrackingLocal", 0.5f);
                var alphaRemote = graph.Param("Alpha/TrackingRemote", 0.2f);
                alphaTracking = graph.Param("Alpha/Tracking", 0.5f);
                graph.AddOperation(mathRoot, graph.AlphaFromDeltaTime(frameTime, alphaLocal, request.profile.localTrackingResponseSeconds));
                graph.AddOperation(mathRoot, graph.AlphaFromDeltaTime(frameTime, alphaRemote, request.profile.remoteTrackingResponseSeconds));
                var alphaDifference = graph.Param("Alpha/TrackingDifference", 0f);
                var alphaLocalPart = graph.Param("Alpha/TrackingLocalPart", 0f);
                graph.AddOperation(mathRoot, graph.Linear(alphaDifference, new[]
                {
                    Term.Positive(alphaLocal, 1f), Term.Positive(alphaRemote, -1f)
                }));
                graph.AddOperation(mathRoot, graph.Multiply(localFactor, alphaDifference, alphaLocalPart, true));
                graph.AddOperation(mathRoot, graph.Linear(alphaTracking, new[]
                {
                    Term.Positive(alphaRemote, 1f), Term.Signed(alphaLocalPart, 1f)
                }));

                var alphaTrackingBlend = graph.Param("Alpha/TrackingBlend", 0.2f);
                graph.AddOperation(mathRoot, graph.AlphaFromDeltaTime(frameTime, alphaTrackingBlend, request.profile.trackingBlendResponseSeconds));
                var trackingBlendFast = graph.Param("TrackingBlend/Fast", 0f);
                trackingBlend = graph.Param(prefix + "/Speech/TrackingBlend", 0f, false);
                graph.AddOperation(mathRoot, graph.Smooth(trackingGate, trackingBlendFast, alphaTrackingBlend, false));
                graph.AddOperation(mathRoot, graph.Smooth(trackingBlendFast, trackingBlend, alphaTrackingBlend, false));
                oneMinusTracking = graph.Param("TrackingBlend/Inverse", 1f);
                graph.AddOperation(mathRoot, graph.Linear(oneMinusTracking, new[]
                {
                    Term.Constant(1f), Term.Positive(trackingBlend, -1f)
                }));
                result.trackingBlendParameter = trackingBlend;
                result.globalParameters.Add(trackingBlend);

                foreach (var articulator in EnabledArticulators(request.effectiveTrackingInputs))
                {
                    var binding = request.profile.FindBinding(articulator);
                    if (binding == null || string.IsNullOrWhiteSpace(binding.trackingParameter)) continue;
                    var signed = IsSigned(articulator);
                    var parameter = request.trackingParameterNames != null &&
                                    request.trackingParameterNames.TryGetValue(articulator, out var resolvedParameter)
                        ? resolvedParameter
                        : TrackingParameterName(request.trackingPrefix, binding.trackingParameter);
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
                    var fast = graph.Param($"Tracking/{articulator}/Fast", 0f);
                    var slow = graph.Param($"Tracking/{articulator}/Slow", 0f);
                    graph.AddOperation(mathRoot, graph.Smooth(input, fast, alphaTracking, signed));
                    graph.AddOperation(mathRoot, graph.Smooth(fast, slow, alphaTracking, signed));
                    trackingFast[articulator] = fast;
                    trackingSlow[articulator] = slow;
                }
            }
            else
            {
                trackingBlend = graph.Param(prefix + "/Speech/TrackingBlend", 0f, false);
                result.trackingBlendParameter = trackingBlend;
                result.globalParameters.Add(trackingBlend);
            }

            var articulationFast = new Dictionary<AdvancedVisemeArticulator, string>();
            var articulationSlow = new Dictionary<AdvancedVisemeArticulator, string>();
            var articulators = EnabledArticulators(request.effectiveTrackingInputs).ToArray();
            foreach (var articulator in articulators)
            {
                var signed = IsSigned(articulator);
                var speechFast = graph.Param($"Articulation/{articulator}/SpeechFast", 0f);
                var speechSlow = graph.Param($"Articulation/{articulator}/SpeechSlow", 0f);
                var coefficients = GetSpeechCoefficients(request, articulator);
                graph.AddOperation(mathRoot, graph.Linear(speechFast, BuildVisemeTerms(fastSpeechWeights, coefficients, signed)));
                graph.AddOperation(mathRoot, graph.Linear(speechSlow, BuildVisemeTerms(speechWeights, coefficients, signed)));

                var finalFast = speechFast;
                var finalSlow = speechSlow;
                if (request.trackingEnabled && trackingSlow.TryGetValue(articulator, out var trackedSlow))
                {
                    var binding = request.profile.FindBinding(articulator);
                    var localReliability = request.component.fusionMode == AdvancedVisemeFusionMode.TrackerAuthoritative
                        ? 1f
                        : binding.localReliability;
                    var remoteReliability = request.component.fusionMode == AdvancedVisemeFusionMode.TrackerAuthoritative
                        ? 1f
                        : binding.remoteReliability;
                    var reliability = graph.Param($"Tracking/{articulator}/Reliability", remoteReliability);
                    graph.AddOperation(mathRoot, graph.Linear(reliability, new[]
                    {
                        Term.Constant(remoteReliability),
                        Term.Positive(localFactor, localReliability - remoteReliability)
                    }));
                    var gain = graph.Param($"Tracking/{articulator}/Gain", 0f);
                    graph.AddOperation(mathRoot, graph.Multiply(trackingBlend, reliability, gain, false));
                    var inverseGain = graph.Param($"Tracking/{articulator}/InverseGain", 1f);
                    graph.AddOperation(mathRoot, graph.Linear(inverseGain, new[]
                    {
                        Term.Constant(1f), Term.Positive(gain, -1f)
                    }));

                    var calibratedSlow = Calibrate(graph, mathRoot, trackedSlow, binding, articulator, "Slow");
                    var calibratedFast = Calibrate(graph, mathRoot, trackingFast[articulator], binding, articulator, "Fast");
                    var speechSlowPart = graph.Param($"Articulation/{articulator}/SpeechSlowPart", 0f);
                    var speechFastPart = graph.Param($"Articulation/{articulator}/SpeechFastPart", 0f);
                    var trackingSlowPart = graph.Param($"Articulation/{articulator}/TrackingSlowPart", 0f);
                    var trackingFastPart = graph.Param($"Articulation/{articulator}/TrackingFastPart", 0f);
                    graph.AddOperation(mathRoot, graph.Multiply(inverseGain, speechSlow, speechSlowPart, signed));
                    graph.AddOperation(mathRoot, graph.Multiply(inverseGain, speechFast, speechFastPart, signed));
                    graph.AddOperation(mathRoot, graph.Multiply(gain, calibratedSlow, trackingSlowPart, signed));
                    graph.AddOperation(mathRoot, graph.Multiply(gain, calibratedFast, trackingFastPart, signed));
                    finalSlow = graph.Param($"Articulation/{articulator}/FusedSlow", 0f);
                    finalFast = graph.Param($"Articulation/{articulator}/FusedFast", 0f);
                    graph.AddOperation(mathRoot, graph.Linear(finalSlow, new[]
                    {
                        Term.For(speechSlowPart, 1f, signed), Term.For(trackingSlowPart, 1f, signed)
                    }));
                    graph.AddOperation(mathRoot, graph.Linear(finalFast, new[]
                    {
                        Term.For(speechFastPart, 1f, signed), Term.For(trackingFastPart, 1f, signed)
                    }));
                }

                articulationFast[articulator] = finalFast;
                articulationSlow[articulator] = finalSlow;
            }

            if (request.trackingEnabled && request.component.fusionMode == AdvancedVisemeFusionMode.PhoneticAssist)
            {
                ApplyConstraints(graph, mathRoot, request.profile, slowVisemes, trackingBlend, articulationSlow);
            }

            foreach (var articulator in articulators)
            {
                var signed = IsSigned(articulator);
                var output = graph.Param(prefix + "/Articulation/" + articulator, 0f, false);
                graph.AddOperation(mathRoot, graph.Copy(articulationSlow[articulator], output, signed));
                articulationSlow[articulator] = output;
                result.articulationParameters[articulator] = output;
                result.globalParameters.Add(output);

                var velocityRaw = graph.Param($"Velocity/{articulator}/Raw", 0f);
                graph.AddOperation(mathRoot, graph.Linear(velocityRaw, new[]
                {
                    Term.For(articulationFast[articulator], 1f / Mathf.Max(0.005f, request.profile.visemeResponseSeconds), signed),
                    Term.For(output, -1f / Mathf.Max(0.005f, request.profile.visemeResponseSeconds), signed)
                }));
                var velocity = graph.Param(prefix + "/Velocity/" + articulator, 0f, false);
                graph.AddOperation(mathRoot, graph.Map(velocityRaw, velocity, new[]
                {
                    Point(-1f, -1f), Point(0f, 0f), Point(1f, 1f)
                }));
                result.globalParameters.Add(velocity);
            }

            AddMotionLayer(controller, graph, "YUCP AVR Math", mathRoot);

            if (request.component.mouthOwnership == AdvancedVisemeMouthOwnership.DriveLowerFace)
            {
                var outputRoot = graph.Direct("Lower Face Output");
                BuildOutputTree(request, result, graph, outputRoot, speechWeights, trackingBlend, oneMinusTracking);
                AddMotionLayer(controller, graph, "YUCP AVR Output", outputRoot);
            }

            var expressionParameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            expressionParameters.name = "YUCP Advanced Viseme Inputs";
            expressionParameters.parameters = BuildExpressionParameters(request, result).ToArray();
            AssetDatabase.CreateAsset(expressionParameters, request.parametersPath);
            result.parameters = expressionParameters;

            foreach (var global in result.globalParameters.Distinct())
            {
                graph.AddParameter(global, AnimatorControllerParameterType.Float, global.EndsWith("/Viseme/sil", StringComparison.Ordinal) ? 1f : 0f);
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(request.controllerPath);
            return result;
        }

        private static void ApplyConstraints(
            MathGraph graph,
            BlendTree root,
            VisemeReconstructionProfile profile,
            string[] visemes,
            string trackingBlend,
            IDictionary<AdvancedVisemeArticulator, string> articulation)
        {
            if (articulation.TryGetValue(AdvancedVisemeArticulator.LipClose, out var lipClose))
            {
                var floor = graph.Param("Constraint/PPClosure", 0f);
                var phoneticFloor = graph.Param("Constraint/PPClosurePhonetic", 0f);
                graph.AddOperation(root, graph.Linear(phoneticFloor, new[] { Term.Positive(visemes[1], profile.bilabialClosure) }));
                graph.AddOperation(root, graph.Multiply(trackingBlend, phoneticFloor, floor, false));
                var constrained = graph.Param("Constraint/LipClose", 0f);
                graph.AddOperation(root, graph.Max(lipClose, floor, constrained));
                articulation[AdvancedVisemeArticulator.LipClose] = constrained;
            }
            if (articulation.TryGetValue(AdvancedVisemeArticulator.LipBite, out var lipBite))
            {
                var floor = graph.Param("Constraint/FFBite", 0f);
                var phoneticFloor = graph.Param("Constraint/FFBitePhonetic", 0f);
                graph.AddOperation(root, graph.Linear(phoneticFloor, new[] { Term.Positive(visemes[2], profile.labiodentalBite) }));
                graph.AddOperation(root, graph.Multiply(trackingBlend, phoneticFloor, floor, false));
                var constrained = graph.Param("Constraint/LipBite", 0f);
                graph.AddOperation(root, graph.Max(lipBite, floor, constrained));
                articulation[AdvancedVisemeArticulator.LipBite] = constrained;
            }
            if (articulation.TryGetValue(AdvancedVisemeArticulator.JawOpen, out var jaw))
            {
                var sibilant = graph.Param("Constraint/Sibilant", 0f);
                graph.AddOperation(root, graph.Linear(sibilant, new[]
                {
                    Term.Positive(visemes[6], 1f), Term.Positive(visemes[7], 1f)
                }));
                var sibilantClamped = graph.Param("Constraint/SibilantClamped", 0f);
                graph.AddOperation(root, graph.Map(sibilant, sibilantClamped, new[] { Point(0f, 0f), Point(1f, 1f), Point(2f, 1f) }));
                var trackedSibilant = graph.Param("Constraint/TrackedSibilant", 0f);
                graph.AddOperation(root, graph.Multiply(trackingBlend, sibilantClamped, trackedSibilant, false));
                var ceiling = graph.Param("Constraint/JawCeiling", 1f);
                graph.AddOperation(root, graph.Linear(ceiling, new[]
                {
                    Term.Constant(1f), Term.Positive(trackedSibilant, -(1f - profile.sibilantJawMaximum))
                }));
                var constrained = graph.Param("Constraint/JawOpen", 0f);
                graph.AddOperation(root, graph.Min(jaw, ceiling, constrained));
                articulation[AdvancedVisemeArticulator.JawOpen] = constrained;
            }
        }

        private static string Calibrate(
            MathGraph graph,
            BlendTree root,
            string input,
            ArticulatorRigBinding binding,
            AdvancedVisemeArticulator articulator,
            string stage)
        {
            if (Mathf.Approximately(binding.trackingScale, 1f) && Mathf.Approximately(binding.trackingOffset, 0f)) return input;
            var output = graph.Param($"Tracking/{articulator}/{stage}Calibrated", 0f);
            graph.AddOperation(root, graph.Linear(output, new[]
            {
                Term.For(input, binding.trackingScale, IsSigned(articulator)),
                Term.Constant(binding.trackingOffset)
            }));
            return output;
        }

        private static Term[] BuildVisemeTerms(string[] inputs, float[] coefficients, bool signed)
        {
            var terms = new List<Term>();
            for (var i = 1; i < inputs.Length; i++)
            {
                if (Mathf.Abs(coefficients[i]) < 1e-6f) continue;
                terms.Add(Term.For(inputs[i], coefficients[i], signed || coefficients[i] < 0f));
            }
            if (terms.Count == 0) terms.Add(Term.Constant(0f));
            return terms.ToArray();
        }

        private static float[] GetSpeechCoefficients(Request request, AdvancedVisemeArticulator articulator)
        {
            var values = new float[VisemeReconstructionProfile.VisemeCount];
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

        private static void BuildOutputTree(
            Request request,
            Result result,
            MathGraph graph,
            BlendTree outputRoot,
            string[] speechWeights,
            string trackingBlend,
            string oneMinusTracking)
        {
            var hasCalibration = request.calibration != null && request.calibration.success;
            var anyVisemeOverride = request.profile.visemePoses.Any(p => p != null && p.animationOverride != null);
            var useResiduals = hasCalibration && !anyVisemeOverride;

            if (useResiduals)
            {
                foreach (var pair in result.articulationParameters)
                {
                    if (!request.resolvedBlendShapes.TryGetValue(pair.Key, out var shape) || string.IsNullOrEmpty(shape)) continue;
                    graph.AddOperation(outputRoot, graph.DrivePose(pair.Value,
                        graph.BlendShapeClip(request.rendererPath, shape, 100f), IsSigned(pair.Key)));
                }

                for (var i = 1; i < speechWeights.Length; i++)
                {
                    var residualWeight = speechWeights[i];
                    if (request.trackingEnabled)
                    {
                        residualWeight = graph.Param($"Viseme/{i}/ResidualWeight", 0f);
                        graph.AddOperation(outputRoot, graph.Multiply(speechWeights[i], oneMinusTracking, residualWeight, false));
                    }
                    var residualName = request.calibration.residualBlendShapeNames[i];
                    if (!string.IsNullOrEmpty(residualName))
                        graph.AddOperation(outputRoot, graph.DrivePose(residualWeight,
                            graph.BlendShapeClip(request.rendererPath, residualName, 100f), false));
                }
            }
            else
            {
                for (var i = 1; i < speechWeights.Length; i++)
                {
                    var weight = speechWeights[i];
                    if (request.trackingEnabled &&
                        (request.resolvedBlendShapes.Count > 0 || (request.externalPoses != null && request.externalPoses.Count > 0)))
                    {
                        weight = graph.Param($"Viseme/{i}/FallbackWeight", 0f);
                        graph.AddOperation(outputRoot, graph.Multiply(speechWeights[i], oneMinusTracking, weight, false));
                    }
                    var overrideClip = request.profile.visemePoses[i].animationOverride;
                    var clip = overrideClip != null
                        ? graph.PoseClip(overrideClip, "Viseme " + VisemeReconstructionProfile.VisemeNames[i])
                        : graph.BlendShapeClip(request.rendererPath, request.sourceVisemeBlendShapes[i], 100f);
                    graph.AddOperation(outputRoot, graph.DrivePose(weight, clip, false));
                }

                if (request.trackingEnabled)
                {
                    foreach (var pair in result.articulationParameters)
                    {
                        var binding = request.profile.FindBinding(pair.Key);
                        Motion positive = null;
                        Motion negative = null;
                        if (binding != null && binding.animationOverride != null)
                        {
                            positive = graph.PoseClipForRenderer(binding.animationOverride,
                                "Articulation " + pair.Key, request.rendererPath, request.targetMesh);
                            if (binding.negativeAnimationOverride != null)
                                negative = graph.PoseClipForRenderer(binding.negativeAnimationOverride,
                                    "Articulation " + pair.Key + " Negative", request.rendererPath, request.targetMesh);
                        }
                        else if (request.externalPoses != null && request.externalPoses.TryGetValue(pair.Key, out var external))
                        {
                            positive = graph.PoseClipForRenderer(external.positive,
                                "External " + pair.Key, request.rendererPath, request.targetMesh);
                            negative = graph.PoseClipForRenderer(external.negative,
                                "External " + pair.Key + " Negative", request.rendererPath, request.targetMesh);
                        }
                        else if (request.resolvedBlendShapes.TryGetValue(pair.Key, out var shape) && !string.IsNullOrEmpty(shape))
                        {
                            positive = graph.BlendShapeClip(request.rendererPath, shape, 100f);
                        }
                        if (positive != null || negative != null)
                            graph.AddOperation(outputRoot, graph.DrivePose(pair.Value, positive, negative, IsSigned(pair.Key)));
                    }
                }
            }
        }

        private static IEnumerable<VRCExpressionParameters.Parameter> BuildExpressionParameters(Request request, Result result)
        {
            if (!request.trackingEnabled) yield break;
            var names = request.existingExpressionParameters != null
                ? new HashSet<string>(request.existingExpressionParameters)
                : new HashSet<string>();
            if (!request.reuseExistingTracking)
            {
                foreach (var articulator in EnabledArticulators(request.effectiveTrackingInputs))
                {
                    var binding = request.profile.FindBinding(articulator);
                    if (binding == null || string.IsNullOrWhiteSpace(binding.trackingParameter)) continue;
                    var name = TrackingParameterName(request.trackingPrefix, binding.trackingParameter);
                    if (UsesBinaryTracking(request))
                    {
                        foreach (var bitName in BinaryParameterNames(name, articulator, request.component.trackingEncoding))
                        {
                            if (!names.Add(bitName)) continue;
                            yield return ExpressionParameter(bitName, VRCExpressionParameters.ValueType.Bool, 0f);
                        }
                    }
                    else if (names.Add(name))
                    {
                        yield return ExpressionParameter(name, VRCExpressionParameters.ValueType.Float, 0f);
                    }
                }
            }
            var activeParameter = string.IsNullOrEmpty(request.trackingActiveParameter)
                ? "LipTrackingActive"
                : request.trackingActiveParameter;
            if (names.Add(activeParameter))
                yield return ExpressionParameter(activeParameter, VRCExpressionParameters.ValueType.Bool, 0f);
            if (request.component.createFaceTrackingToggle && names.Add(result.manualTrackingParameter))
                yield return ExpressionParameter(result.manualTrackingParameter, VRCExpressionParameters.ValueType.Bool, 1f);
        }

        private static VRCExpressionParameters.Parameter ExpressionParameter(
            string name,
            VRCExpressionParameters.ValueType type,
            float defaultValue)
        {
            return new VRCExpressionParameters.Parameter
            {
                name = name,
                valueType = type,
                defaultValue = defaultValue,
                saved = false,
                networkSynced = true
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

        private static IEnumerable<AdvancedVisemeArticulator> EnabledArticulators(AdvancedVisemeTrackingInputs mode)
        {
            foreach (var articulator in CoreArticulators) yield return articulator;
            if (mode == AdvancedVisemeTrackingInputs.Quality12)
                foreach (var articulator in QualityArticulators) yield return articulator;
        }

        private static bool IsSigned(AdvancedVisemeArticulator articulator)
        {
            return articulator == AdvancedVisemeArticulator.SmileSad ||
                   articulator == AdvancedVisemeArticulator.JawX ||
                   articulator == AdvancedVisemeArticulator.JawZ ||
                   articulator == AdvancedVisemeArticulator.MouthX ||
                   articulator == AdvancedVisemeArticulator.TongueY;
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
            string layerName)
        {
            var stateMachine = AddStateLayer(controller, graph, layerName);
            AnimatorState silence = null;
            for (var i = 0; i < VisemeReconstructionProfile.VisemeCount; i++)
            {
                var state = stateMachine.AddState(VisemeReconstructionProfile.VisemeNames[i]);
                state.motion = graph.Setter(output, i);
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
            string layerName,
            out string output)
        {
            output = graph.Param("LocalFactor", 1f);
            var layer = AddStateLayer(controller, graph, layerName);
            var off = layer.AddState("False");
            var on = layer.AddState("True");
            off.motion = graph.Setter(output, 0f);
            on.motion = graph.Setter(output, 1f);
            layer.defaultState = on;
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
        }

        private sealed class MathGraph
        {
            private readonly AnimatorController controller;
            private readonly string prefix;
            private readonly HashSet<UnityEngine.Object> subAssets = new HashSet<UnityEngine.Object>();
            private const string AlwaysOne = "__YUCP_AVR_ONE";

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

            public AnimationClip Setter(string output, float value)
            {
                var clip = Clip($"{output} = {value:0.###}");
                AnimationUtility.SetEditorCurve(clip,
                    EditorCurveBinding.FloatCurve("", typeof(Animator), output),
                    AnimationCurve.Constant(0f, 1f / 60f, value));
                return clip;
            }

            public Motion Copy(string input, string output, bool signed)
            {
                return signed
                    ? Map(input, output, new[] { Point(-2f, -2f), Point(0f, 0f), Point(2f, 2f) })
                    : WeightedSetter(input, output, 1f);
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
                var tree = new BlendTree
                {
                    name = $"Map {input} -> {output}",
                    blendType = BlendTreeType.Simple1D,
                    blendParameter = input,
                    useAutomaticThresholds = false
                };
                SubAsset(tree);
                tree.children = points.OrderBy(p => p.input).Select(p => new ChildMotion
                {
                    motion = Setter(output, p.output), threshold = p.input, timeScale = 1f
                }).ToArray();
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
                var samples = new[] { 0f, 1f / 240f, 1f / 144f, 1f / 90f, 1f / 60f, 1f / 45f, 1f / 30f, 1f / 20f, 0.1f, 0.25f };
                return Map(deltaTime, output, samples.Select(dt => Point(dt, AdvancedVisemeMath.Alpha(dt, responseSeconds))).ToArray());
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
                return tree;
            }

            public Motion Linear(string output, IEnumerable<Term> terms)
            {
                var tree = Direct("Linear -> " + output);
                var children = new List<ChildMotion>();
                foreach (var term in terms)
                {
                    Motion motion;
                    if (term.constant)
                    {
                        motion = Setter(output, term.multiplier);
                    }
                    else if (term.signed)
                    {
                        motion = Map(term.parameter, output, new[]
                        {
                            Point(-2f, -2f * term.multiplier), Point(0f, 0f), Point(2f, 2f * term.multiplier)
                        });
                    }
                    else
                    {
                        motion = WeightedSetter(term.parameter, output, term.multiplier);
                    }
                    children.Add(new ChildMotion { motion = motion, directBlendParameter = AlwaysOne, timeScale = 1f });
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
                        new ChildMotion { motion = Clip("Pose Safety Zero"), directBlendParameter = AlwaysOne, timeScale = 1f }
                    };
                    return tree;
                }

                var negative = NegatedPose(pose);
                var zero = Clip("Signed Pose Zero");
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

            public Motion DrivePose(string weight, Motion positive, Motion negative, bool signed)
            {
                if (!signed) return positive != null ? DrivePose(weight, positive, false) : Clip("Missing Pose");
                if (positive == null && negative == null) return Clip("Missing Signed Pose");
                positive = positive ?? Clip("Signed Positive Zero");
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
                        new ChildMotion { motion = Clip("Signed Pose Zero"), threshold = 0f, timeScale = 1f },
                        new ChildMotion { motion = positive, threshold = 1f, timeScale = 1f }
                    }
                };
                SubAsset(tree);
                return tree;
            }

            public AnimationClip BlendShapeClip(string path, string blendShape, float value)
            {
                var clip = Clip("Blendshape " + blendShape);
                if (string.IsNullOrEmpty(blendShape)) return clip;
                AnimationUtility.SetEditorCurve(clip,
                    EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer), "blendShape." + blendShape),
                    AnimationCurve.Constant(0f, 1f / 60f, value));
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
                    AnimationUtility.SetEditorCurve(clip, binding,
                        AnimationCurve.Constant(0f, 1f / 60f, curve.Evaluate(time)));
                }
                return clip;
            }

            public AnimationClip PoseClipForRenderer(AnimationClip source, string name, string rendererPath, Mesh targetMesh)
            {
                if (source == null) return null;
                var clip = Clip(name);
                var time = Mathf.Max(0f, source.length);
                foreach (var sourceBinding in AnimationUtility.GetCurveBindings(source))
                {
                    var curve = AnimationUtility.GetEditorCurve(source, sourceBinding);
                    if (curve == null) continue;
                    var binding = sourceBinding;
                    if (binding.type == typeof(SkinnedMeshRenderer) &&
                        binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal))
                    {
                        var shape = binding.propertyName.Substring("blendShape.".Length);
                        if (targetMesh != null && targetMesh.GetBlendShapeIndex(shape) < 0) continue;
                        binding.path = rendererPath;
                    }
                    AnimationUtility.SetEditorCurve(clip, binding,
                        AnimationCurve.Constant(0f, 1f / 60f, curve.Evaluate(time)));
                }
                return AnimationUtility.GetCurveBindings(clip).Length == 0 ? null : clip;
            }

            public Motion NegatedPose(Motion motion)
            {
                if (!(motion is AnimationClip source)) return Clip("Unsupported Negative Pose");
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

            public void AddOperation(BlendTree root, Motion motion)
            {
                var children = root.children.ToList();
                children.Add(Child(motion));
                root.children = children.ToArray();
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
