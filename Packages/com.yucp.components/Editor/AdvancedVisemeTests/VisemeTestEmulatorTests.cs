using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace YUCP.Components.Editor.Tests
{
    public sealed class VisemeTestEmulatorTests
    {
        [Test]
        public void DominantViseme_SelectsLargestFiniteWeight()
        {
            var weights = new float[15];
            weights[2] = float.NaN;
            weights[7] = 0.6f;
            weights[11] = 0.9f;
            Assert.That(VisemeTestMath.DominantViseme(weights), Is.EqualTo(11));
        }

        [Test]
        public void VoiceCurve_IsFiniteMonotonicAndGated()
        {
            Assert.That(VisemeTestMath.VoiceFromRms(0.005f, 0.01f, 1f), Is.EqualTo(0f));
            var quiet = VisemeTestMath.VoiceFromRms(0.02f, 0.01f, 1f);
            var loud = VisemeTestMath.VoiceFromRms(0.1f, 0.01f, 1f);
            Assert.That(quiet, Is.GreaterThan(0f));
            Assert.That(loud, Is.GreaterThan(quiet));
            Assert.That(float.IsNaN(loud) || float.IsInfinity(loud), Is.False);
        }

        [Test]
        public void AdaptiveInputLevel_RecoversQuietSpeechWithoutOpeningOnNoiseFloor()
        {
            const float configuredGate = 0.012f;
            const float noiseFloor = 0.00014f;
            var gate = VisemeTestMath.AdaptiveNoiseGate(noiseFloor, configuredGate);

            Assert.That(gate, Is.GreaterThan(noiseFloor));
            Assert.That(gate, Is.LessThan(0.001f),
                "A low-level Windows microphone must not remain trapped below a fixed 0.012 gate.");
            Assert.That(VisemeTestMath.AutomaticInputGain(noiseFloor, gate), Is.EqualTo(1f),
                "Idle room noise must not activate automatic gain.");

            const float quietSpeech = 0.001f;
            var gain = VisemeTestMath.AutomaticInputGain(quietSpeech, gate);
            var voice = VisemeTestMath.VoiceFromRms(
                quietSpeech * gain, gate * gain, 1f);
            Assert.That(gain, Is.EqualTo(15f));
            Assert.That(voice, Is.GreaterThan(0.025f),
                "The live HyperX-level regression must reach Oculus classification.");
        }

        [Test]
        public void AutomaticGain_CanBeDisabledAndDefaultsToEnabled()
        {
            var root = new GameObject("AutomaticGainTest");
            try
            {
                var component = root.AddComponent<VisemeTestEmulatorData>();
                Assert.That(component.automaticGain, Is.True);
                Assert.That(VisemeTestPreviewSession.ResolveAnalysisGain(
                    component.automaticGain, true, 15f), Is.EqualTo(15f));

                component.automaticGain = false;
                Assert.That(component.automaticGain, Is.False);
                Assert.That(VisemeTestPreviewSession.ResolveAnalysisGain(
                    component.automaticGain, true, 15f), Is.EqualTo(1f),
                    "Disabling automatic gain must bypass an existing boost immediately.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void AdaptiveNoiseFloor_FreezesDuringSpeechAndTracksSilenceAsymmetrically()
        {
            const float floor = 0.001f;
            var frozen = VisemeTestMath.UpdateNoiseFloor(floor, 0.02f, true, 0.02f);
            var quieter = VisemeTestMath.UpdateNoiseFloor(floor, 0.0002f, false, 0.02f);
            var slightlyLouder = VisemeTestMath.UpdateNoiseFloor(floor, 0.0012f, false, 0.02f);

            Assert.That(frozen, Is.EqualTo(floor));
            Assert.That(quieter, Is.LessThan(floor));
            Assert.That(slightlyLouder - floor, Is.LessThan(floor - quieter),
                "Noise-floor release must be much slower than downward adaptation.");
        }

        [Test]
        public void VisemeBlendShape_DrivesExactlyOneMappedShape()
        {
            var root = new GameObject("VisemeTest");
            var mesh = CreateVisemeMesh();
            try
            {
                var renderer = root.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = mesh;
                var descriptor = root.AddComponent<VRCAvatarDescriptor>();
                descriptor.lipSync = VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape;
                descriptor.VisemeSkinnedMesh = renderer;
                descriptor.VisemeBlendShapes = (string[])VisemeTestMath.VisemeNames.Clone();

                VisemeTestPreviewSession.ApplyDescriptor(descriptor, 7, 0.31f);

                for (var i = 0; i < 15; i++)
                    Assert.That(renderer.GetBlendShapeWeight(i), Is.EqualTo(i == 7 ? 100f : 0f).Within(1e-6f));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void VisemeBlendShape_AppliesContinuousOculusWeights()
        {
            var root = new GameObject("ContinuousVisemeTest");
            var mesh = CreateVisemeMesh();
            try
            {
                var renderer = root.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = mesh;
                var descriptor = root.AddComponent<VRCAvatarDescriptor>();
                descriptor.lipSync = VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape;
                descriptor.VisemeSkinnedMesh = renderer;
                descriptor.VisemeBlendShapes = (string[])VisemeTestMath.VisemeNames.Clone();
                var weights = new float[15];
                weights[2] = 0.17f;
                weights[10] = 0.42f;
                weights[13] = 0.31f;

                VisemeTestPreviewSession.ApplyDescriptor(descriptor, weights, 10, 0.6f);

                Assert.That(renderer.GetBlendShapeWeight(2), Is.EqualTo(17f).Within(1e-5f));
                Assert.That(renderer.GetBlendShapeWeight(10), Is.EqualTo(42f).Within(1e-5f));
                Assert.That(renderer.GetBlendShapeWeight(13), Is.EqualTo(31f).Within(1e-5f));
                Assert.That(renderer.GetBlendShapeWeight(7), Is.EqualTo(0f).Within(1e-5f));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void JawFlapBlendShape_UsesVoiceAsZeroToOneHundred()
        {
            var root = new GameObject("JawFlapTest");
            var mesh = CreateSingleShapeMesh("MouthOpen");
            try
            {
                var renderer = root.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = mesh;
                var descriptor = root.AddComponent<VRCAvatarDescriptor>();
                descriptor.lipSync = VRC_AvatarDescriptor.LipSyncStyle.JawFlapBlendShape;
                descriptor.VisemeSkinnedMesh = renderer;
                descriptor.MouthOpenBlendShapeName = "MouthOpen";

                VisemeTestPreviewSession.ApplyDescriptor(descriptor, 12, 0.37f);
                Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(37f).Within(1e-5f));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void JawFlapBone_InterpolatesDescriptorLocalRotations()
        {
            var root = new GameObject("JawBoneTest");
            var jaw = new GameObject("Jaw").transform;
            jaw.SetParent(root.transform);
            try
            {
                var descriptor = root.AddComponent<VRCAvatarDescriptor>();
                descriptor.lipSync = VRC_AvatarDescriptor.LipSyncStyle.JawFlapBone;
                descriptor.lipSyncJawBone = jaw;
                descriptor.lipSyncJawClosed = Quaternion.Euler(0f, 0f, 0f);
                descriptor.lipSyncJawOpen = Quaternion.Euler(30f, 0f, 0f);

                VisemeTestPreviewSession.ApplyDescriptor(descriptor, 0, 0.5f);
                Assert.That(Quaternion.Angle(jaw.localRotation, Quaternion.Euler(15f, 0f, 0f)), Is.LessThan(1e-4f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ManualPreview_StartsAndStopsWithoutPersistingAComponentState()
        {
            var root = new GameObject("ManualPreviewTest") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var descriptor = root.AddComponent<VRCAvatarDescriptor>();
                descriptor.lipSync = VRC_AvatarDescriptor.LipSyncStyle.VisemeParameterOnly;
                var component = root.AddComponent<VisemeTestEmulatorData>();
                component.input = VisemeTestInput.Manual;
                component.manualViseme = 10;
                component.manualVoice = 0.6f;

                Assert.That(VisemeTestPreviewSession.Start(component, out var error), Is.True, error);
                Assert.That(VisemeTestPreviewSession.IsRunning(component), Is.True);
                VisemeTestPreviewSession.Stop(component);
                Assert.That(VisemeTestPreviewSession.IsRunning(component), Is.False);
            }
            finally
            {
                var component = root.GetComponent<VisemeTestEmulatorData>();
                if (component != null) VisemeTestPreviewSession.Stop(component);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void DescriptorTarget_ParentWinsOverGestureManagerAndSceneInference()
        {
            var parentObject = new GameObject("ParentAvatar");
            var otherObject = new GameObject("OtherAvatar");
            try
            {
                var parent = parentObject.AddComponent<VRCAvatarDescriptor>();
                var other = otherObject.AddComponent<VRCAvatarDescriptor>();

                Assert.That(VisemeTestPreviewSession.TrySelectDescriptor(
                    parent,
                    new[] { parent, other },
                    new[] { other },
                    out var selected,
                    out var source,
                    out var error), Is.True, error);
                Assert.That(selected, Is.SameAs(parent));
                Assert.That(source, Is.EqualTo(VisemeTestPreviewSession.DescriptorTargetSource.Parent));
            }
            finally
            {
                Object.DestroyImmediate(parentObject);
                Object.DestroyImmediate(otherObject);
            }
        }

        [Test]
        public void DescriptorTarget_UsesUnambiguousGestureManagerTarget()
        {
            var firstObject = new GameObject("FirstAvatar");
            var secondObject = new GameObject("SecondAvatar");
            try
            {
                var first = firstObject.AddComponent<VRCAvatarDescriptor>();
                var second = secondObject.AddComponent<VRCAvatarDescriptor>();

                Assert.That(VisemeTestPreviewSession.TrySelectDescriptor(
                    null,
                    new[] { first, second },
                    new[] { second },
                    out var selected,
                    out var source,
                    out var error), Is.True, error);
                Assert.That(selected, Is.SameAs(second));
                Assert.That(source, Is.EqualTo(VisemeTestPreviewSession.DescriptorTargetSource.GestureManager));
            }
            finally
            {
                Object.DestroyImmediate(firstObject);
                Object.DestroyImmediate(secondObject);
            }
        }

        [Test]
        public void DescriptorTarget_UsesSoleActiveSceneAvatar()
        {
            var avatarObject = new GameObject("OnlyAvatar");
            try
            {
                var avatar = avatarObject.AddComponent<VRCAvatarDescriptor>();
                Assert.That(VisemeTestPreviewSession.TrySelectDescriptor(
                    null,
                    new[] { avatar },
                    new VRCAvatarDescriptor[0],
                    out var selected,
                    out var source,
                    out var error), Is.True, error);
                Assert.That(selected, Is.SameAs(avatar));
                Assert.That(source, Is.EqualTo(VisemeTestPreviewSession.DescriptorTargetSource.SoleSceneAvatar));
            }
            finally
            {
                Object.DestroyImmediate(avatarObject);
            }
        }

        [Test]
        public void DescriptorTarget_ReportsAmbiguousSceneAvatarsClearly()
        {
            var firstObject = new GameObject("FirstAvatar");
            var secondObject = new GameObject("SecondAvatar");
            try
            {
                var first = firstObject.AddComponent<VRCAvatarDescriptor>();
                var second = secondObject.AddComponent<VRCAvatarDescriptor>();

                Assert.That(VisemeTestPreviewSession.TrySelectDescriptor(
                    null,
                    new[] { first, second },
                    new VRCAvatarDescriptor[0],
                    out _,
                    out _,
                    out var error), Is.False);
                Assert.That(error, Does.Contain("2 active VRChat avatars"));
                Assert.That(error, Does.Contain("Place this component below"));
            }
            finally
            {
                Object.DestroyImmediate(firstObject);
                Object.DestroyImmediate(secondObject);
            }
        }

        [Test]
        public void InstalledOculusBackend_CreatesContextAndProcessesMonoPcm()
        {
            if (!VisemeTestPreviewSession.OculusBridge.IsAvailable(out _)) return;
            var bridge = VisemeTestPreviewSession.OculusBridge.TryCreate(48000, 1024, out var error);
            Assert.That(bridge, Is.Not.Null, error);
            try
            {
                Assert.That(bridge.TryProcess(new float[1024], out var weights), Is.True);
                Assert.That(weights, Is.Not.Null);
                Assert.That(weights.Length, Is.GreaterThanOrEqualTo(15));
            }
            finally
            {
                bridge?.Dispose();
            }
        }

        [Test]
        public void InstalledOculusBackend_SampleSpeechProducesFractionalWeights()
        {
            if (!VisemeTestPreviewSession.OculusBridge.IsAvailable(out _)) return;
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Oculus/LipSync/Audio/vox_lp_01.wav");
            if (clip == null) return;
            var bridge = VisemeTestPreviewSession.OculusBridge.TryCreate(clip.frequency, 1024, out var error);
            Assert.That(bridge, Is.Not.Null, error);
            try
            {
                var interleaved = new float[1024 * clip.channels];
                var mono = new float[1024];
                var foundFractional = false;
                for (var offset = 0; offset + 1024 < clip.samples && offset < clip.frequency * 4; offset += 1024)
                {
                    Assert.That(clip.GetData(interleaved, offset), Is.True);
                    for (var sample = 0; sample < mono.Length; sample++)
                    {
                        var sum = 0f;
                        for (var channel = 0; channel < clip.channels; channel++)
                            sum += interleaved[sample * clip.channels + channel];
                        mono[sample] = sum / clip.channels;
                    }
                    if (!bridge.TryProcess(mono, out var weights)) continue;
                    for (var i = 0; i < 15; i++)
                        if (weights[i] > 0.001f && weights[i] < 0.999f) foundFractional = true;
                    if (foundFractional) break;
                }
                Assert.That(foundFractional, Is.True, "Oculus sample speech should produce fractional viseme weights.");
            }
            finally
            {
                bridge?.Dispose();
            }
        }

        [Test]
        public void TrackingControl_MouthAnimationStopsAndTrackingResumesVisemes()
        {
            var root = new GameObject("TrackingControlTest");
            var animationControl = ScriptableObject.CreateInstance<VRCAnimatorTrackingControl>();
            var trackingControl = ScriptableObject.CreateInstance<VRCAnimatorTrackingControl>();
            try
            {
                var animator = root.AddComponent<Animator>();
                var descriptor = root.AddComponent<VRCAvatarDescriptor>();
                animationControl.trackingMouth = VRC_AnimatorTrackingControl.TrackingType.Animation;
                trackingControl.trackingMouth = VRC_AnimatorTrackingControl.TrackingType.Tracking;

                VisemeTestPreviewSession.TrackingControlBridge.ApplyForTests(animationControl, animator);
                Assert.That(VisemeTestPreviewSession.GestureManagerBridge.MouthTrackingEnabled(descriptor), Is.False);

                VisemeTestPreviewSession.TrackingControlBridge.ApplyForTests(trackingControl, animator);
                Assert.That(VisemeTestPreviewSession.GestureManagerBridge.MouthTrackingEnabled(descriptor), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(animationControl);
                Object.DestroyImmediate(trackingControl);
                Object.DestroyImmediate(root);
            }
        }

        private static Mesh CreateVisemeMesh()
        {
            var mesh = BaseMesh();
            for (var i = 0; i < 15; i++) AddShape(mesh, VisemeTestMath.VisemeNames[i], i + 1);
            return mesh;
        }

        private static Mesh CreateSingleShapeMesh(string name)
        {
            var mesh = BaseMesh();
            AddShape(mesh, name, 1);
            return mesh;
        }

        private static Mesh BaseMesh()
        {
            var mesh = new Mesh { name = "Viseme Test Mesh" };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            return mesh;
        }

        private static void AddShape(Mesh mesh, string name, int seed)
        {
            var vertices = new Vector3[3];
            vertices[0] = new Vector3(seed * 0.001f, 0f, 0f);
            mesh.AddBlendShapeFrame(name, 100f, vertices, new Vector3[3], new Vector3[3]);
        }
    }
}
