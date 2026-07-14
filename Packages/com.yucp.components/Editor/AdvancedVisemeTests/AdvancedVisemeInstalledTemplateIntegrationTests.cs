using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace YUCP.Components.Editor.Tests
{
    public sealed class AdvancedVisemeInstalledTemplateIntegrationTests
    {
        private const string JerryArkitPrefabPath =
            "Packages/adjerry91.vrcft.templates/Prefabs/VF_ARKit_VRCFT.prefab";

        [Test]
        public void AutoDetectsInstalledJerryArkitUnifiedExpressionsSourceAndActivityGate()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(JerryArkitPrefabPath);
            if (prefab == null)
                Assert.Ignore($"Optional integration fixture is not installed: {JerryArkitPrefabPath}");

            var avatarRoot = new GameObject("Advanced Viseme Installed Template Integration Test");
            var profile = VisemeReconstructionProfile.CreateDefaultRuntimeProfile();
            GameObject templateInstance = null;
            try
            {
                var descriptor = avatarRoot.AddComponent<VRCAvatarDescriptor>();
                templateInstance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                Assert.That(templateInstance, Is.Not.Null, "The installed VRCFT template prefab could not be instantiated.");
                templateInstance.transform.SetParent(avatarRoot.transform, false);

                var catalog = AdvancedVisemeTrackingCatalog.Scan(avatarRoot, descriptor);
                Assert.That(catalog.Entries.ContainsKey("FT/v2/JawOpen"), Is.True,
                    "Scan should follow the VRCFury FullController reference to its decoded controller.");
                Assert.That(catalog.Entries.ContainsKey("LipTrackingActive"), Is.True,
                    "Scan should follow the VRCFury FullController reference to its parameter asset.");
                Assert.That(catalog.Entries["FT/v2/JawOpen"].animatorType,
                    Is.EqualTo(AnimatorControllerParameterType.Float));
                Assert.That(catalog.Entries["LipTrackingActive"].expression.valueType,
                    Is.EqualTo(VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType.Bool));
                var resolution = catalog.Resolve(profile, string.Empty, out var error);

                Assert.That(error, Is.Null);
                Assert.That(resolution, Is.Not.Null,
                    "Auto should discover the controller and parameter assets referenced by the VRCFury prefab.");
                Assert.That(resolution.prefix, Is.EqualTo("OSCm/Proxy/FT"),
                    "Auto should select the family structurally connected to the final face rig, not a raw or intermediate bus.");
                Assert.That(resolution.activeParameter, Is.EqualTo("LipTrackingActive"));
                Assert.That(resolution.poseCoverage, Is.GreaterThan(0));
                Assert.That(resolution.parameters[AdvancedVisemeArticulator.JawOpen],
                    Is.EqualTo("OSCm/Proxy/FT/v2/JawOpen"));
                Assert.That(resolution.parameters[AdvancedVisemeArticulator.LipClose],
                    Is.EqualTo("OSCm/Proxy/FT/v2/MouthClosed"));
                Assert.That(resolution.parameters[AdvancedVisemeArticulator.LipFunnel],
                    Is.EqualTo("OSCm/Proxy/FT/v2/LipFunnel"));
                Assert.That(resolution.parameters[AdvancedVisemeArticulator.TongueOut],
                    Is.EqualTo("OSCm/Proxy/FT/v2/TongueOut"));

                var poses = catalog.ExtractPoses(resolution);
                Assert.That(poses.ContainsKey(AdvancedVisemeArticulator.JawOpen), Is.True,
                    "The final rig's jaw aperture pose must be recoverable for authoritative reuse.");
            }
            finally
            {
                Object.DestroyImmediate(profile);
                if (templateInstance != null) Object.DestroyImmediate(templateInstance);
                Object.DestroyImmediate(avatarRoot);
            }
        }
    }
}
