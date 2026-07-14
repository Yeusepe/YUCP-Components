#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using YUCP.Components.Editor.MeshUtils;

namespace YUCP.Components.Editor.Tests
{
    /// <summary>
    /// Integration coverage for the deliberately asymmetric link overlay:
    /// VRCFury continues to copy the primary face's mapped articulation curves,
    /// while YUCP adds only target-local residual curves. The primary AVR face is
    /// therefore never rewritten to accommodate linked meshes.
    /// </summary>
    public sealed class AdvancedVisemeVrcFuryBlendShapeLinkTests
    {
        private const float GeometryTolerance = 1e-5f;
        private const string JawShape = "JawOpen";
        private const string PuckerShape = "LipPucker";

        [Test]
        public void CatalogExactlyMatchesInstalledVrcFuryForEveryCapabilityAndMappingMode()
        {
            using (var fixture = new LinkFixture())
            {
                var first = AdvancedVisemeBlendShapeLinkCatalog.Scan(fixture.root);
                var second = AdvancedVisemeBlendShapeLinkCatalog.Scan(fixture.root);
                var targets = first.FindTargets(fixture.primary);

                Assert.That(first.IsReliable, Is.True, first.Error);
                Assert.That(targets.Count, Is.EqualTo(fixture.targets.Count));
                Assert.That(first.DependencyFingerprint, Is.EqualTo(second.DependencyFingerprint),
                    "A read-only rescan must have a stable dependency fingerprint.");
                CollectionAssert.AreEqual(
                    targets.Select(target => target.linkedPath).ToArray(),
                    second.FindTargets(fixture.primary).Select(target => target.linkedPath).ToArray(),
                    "Resolved VRCFury target order must be deterministic.");

                foreach (var target in targets)
                {
                    var installed = ReadInstalledVrcFuryMappings(
                        target.sourceComponent, fixture.primary, target.linkedRenderer);
                    var catalog = target.mappings
                        .Select(mapping => PairKey(mapping.baseBlendShape, mapping.linkedBlendShape))
                        .OrderBy(value => value, StringComparer.Ordinal)
                        .ToArray();

                    CollectionAssert.AreEqual(installed, catalog,
                        $"Catalog drifted from installed VRCFury for '{target.linkedPath}'.");
                    Assert.That(target.isExact, Is.True, target.diagnostic);
                    Assert.That(target.ambiguities, Is.Empty,
                        "One base shape mapped to several target shapes is exact, not ambiguous.");
                }

                AssertMappingCount(targets, fixture.full, 17);
                AssertMappingCount(targets, fixture.visemesOnly, 15);
                AssertMappingCount(targets, fixture.articulationOnly, 2);
                AssertMappingCount(targets, fixture.subsetArticulation, 16);
                AssertMappingCount(targets, fixture.fuzzyIncludeAll, 17);
                AssertMappingCount(targets, fixture.renamed, 17);
                AssertMappingCount(targets, fixture.oneToMany, 18);

                var fuzzy = TargetFor(targets, fixture.fuzzyIncludeAll);
                CollectionAssert.AreEqual(new[] { "jaw open" }, fuzzy.LinkedShapesFor(JawShape));
                CollectionAssert.AreEqual(new[] { "lip pucker" }, fuzzy.LinkedShapesFor(PuckerShape));

                var renamed = TargetFor(targets, fixture.renamed);
                CollectionAssert.AreEqual(new[] { "Custom Jaw" }, renamed.LinkedShapesFor(JawShape));
                CollectionAssert.AreEqual(new[] { "Custom Pucker" }, renamed.LinkedShapesFor(PuckerShape));

                var oneToMany = TargetFor(targets, fixture.oneToMany);
                CollectionAssert.AreEqual(
                    new[] { "Jaw A", "Jaw B" }, oneToMany.LinkedShapesFor(JawShape));
                Assert.That(oneToMany.mappings.Count(mapping =>
                    mapping.baseBlendShape == JawShape && mapping.linkedBlendShape == "Jaw A"),
                    Is.EqualTo(1), "VRCFury's set semantics must collapse duplicate includes.");
            }
        }

        [Test]
        public void TargetResidualOverlayPreservesPrimaryOutputAndAllLinkedGeometry()
        {
            using (var fixture = new LinkFixture())
            {
                var catalog = AdvancedVisemeBlendShapeLinkCatalog.Scan(fixture.root);
                var targets = catalog.FindTargets(fixture.primary);
                var primaryMeshFingerprint = MeshFingerprint(fixture.primary.sharedMesh);
                var sourceFingerprints = fixture.targets.ToDictionary(
                    renderer => renderer,
                    renderer => MeshFingerprint(renderer.sharedMesh));
                var sourceMeshes = fixture.targets.ToDictionary(
                    renderer => renderer,
                    renderer => renderer.sharedMesh);
                var results = new Dictionary<AdvancedVisemeBlendShapeLinkCatalog.Target,
                    AdvancedVisemeMeshCalibrator.Result>();
                var clip = new AnimationClip { name = "AVR Linked Overlay Capability Matrix" };

                try
                {
                    foreach (var target in targets)
                    {
                        var result = BuildTargetOverlay(target, fixture.referenceCoefficients);
                        Assert.That(result.success, Is.True,
                            $"{target.linkedPath}: {result.error}");
                        Assert.That(result.mesh, Is.Not.SameAs(target.linkedRenderer.sharedMesh));
                        results[target] = result;
                        target.linkedRenderer.sharedMesh = result.mesh;

                        AssertReferenceCoefficients(result.coefficients, fixture.referenceCoefficients,
                            target.linkedPath);
                        AssertResidualAvailability(target, result);
                    }

                    AssertGeometryParityRandomized(
                        fixture, targets, sourceMeshes, results, sampleCount: 128);
                    AssertCurveOverlayIsDisjointAndPrimaryCurvesAreUnchanged(
                        fixture, targets, results, clip);

                    Assert.That(MeshFingerprint(fixture.primary.sharedMesh),
                        Is.EqualTo(primaryMeshFingerprint),
                        "Linked overlays must never replace or mutate the primary AVR mesh.");
                    foreach (var pair in sourceMeshes)
                    {
                        Assert.That(MeshFingerprint(pair.Value),
                            Is.EqualTo(sourceFingerprints[pair.Key]),
                            $"Linked source mesh '{pair.Key.name}' was modified in place.");
                        Assert.That(pair.Value.GetBlendShapeIndex("YUCP_AVR_Link_Residual_aa"),
                            Is.EqualTo(-1), "Generated residuals leaked into a source mesh.");
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(clip);
                    foreach (var result in results.Values)
                        if (result?.mesh != null) UnityEngine.Object.DestroyImmediate(result.mesh);
                }
            }
        }

        [Test]
        public void PlannerRejectsOrderDependentIndirectVisemeChain()
        {
            var root = new GameObject("Indirect Link Avatar");
            var meshes = new List<Mesh>();
            try
            {
                var primary = AddTestRenderer(root, meshes, "Primary", 101);
                var intermediate = AddTestRenderer(root, meshes, "Intermediate", 103);
                var downstream = AddTestRenderer(root, meshes, "Downstream", 107);
                var component = root.AddComponent<AdvancedVisemeReconstructorData>();

                // Deliberately reverse the feature order. Installed VRCFury makes
                // one pass, so Downstream cannot safely inherit curves that are
                // copied into Intermediate later.
                AddBlendShapeLink(root, intermediate, new[] { downstream }, true, true, null);
                AddBlendShapeLink(root, primary, new[] { intermediate }, true, true, null);

                var exception = Assert.Throws<TargetInvocationException>(() =>
                    InvokeLinkedPlanner(root, component, primary));
                Assert.That(exception.InnerException, Is.InstanceOf<InvalidOperationException>());
                Assert.That(exception.InnerException.Message,
                    Does.Contain("indirect BlendShape Link chains"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                foreach (var mesh in meshes) UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void PlannerRejectsCompetingIncomingWriterForOwnedTargetShape()
        {
            var root = new GameObject("Competing Link Avatar");
            var meshes = new List<Mesh>();
            try
            {
                var primary = AddTestRenderer(root, meshes, "Primary", 109);
                var other = AddTestRenderer(root, meshes, "Other", 113);
                var target = AddTestRenderer(root, meshes, "Target", 127);
                var component = root.AddComponent<AdvancedVisemeReconstructorData>();

                AddBlendShapeLink(root, primary, new[] { target }, true, true, null);
                AddBlendShapeLink(root, other, new[] { target }, true, true, null);

                var exception = Assert.Throws<TargetInvocationException>(() =>
                    InvokeLinkedPlanner(root, component, primary));
                Assert.That(exception.InnerException, Is.InstanceOf<InvalidOperationException>());
                Assert.That(exception.InnerException.Message,
                    Does.Contain("another base renderer writes AVR-owned target shapes"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                foreach (var mesh in meshes) UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void PlannerRejectsPrimarySelfLinkThatMapsAuthoredOculusViseme()
        {
            var root = new GameObject("Viseme Self Link Avatar");
            var meshes = new List<Mesh>();
            try
            {
                var primary = AddTestRenderer(root, meshes, "Primary", 131);
                var component = root.AddComponent<AdvancedVisemeReconstructorData>();
                var authoredViseme = VisemeName(10);
                AddBlendShapeLink(
                    root,
                    primary,
                    new[] { primary },
                    false,
                    true,
                    new[]
                    {
                        new KeyValuePair<string, string>(authoredViseme, authoredViseme)
                    });

                var exception = Assert.Throws<TargetInvocationException>(() =>
                    InvokeLinkedPlanner(root, component, primary));
                Assert.That(exception.InnerException, Is.InstanceOf<InvalidOperationException>());
                Assert.That(exception.InnerException.Message,
                    Does.Contain("primary face")
                        .And.Contain("authored Oculus viseme")
                        .And.Contain(authoredViseme)
                        .And.Contain("same renderer"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                foreach (var mesh in meshes) UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void PlannerKeepsDirectTargetWhenPrimarySelfLinkIsArticulationOnly()
        {
            var root = new GameObject("Articulation Self Link Avatar");
            var meshes = new List<Mesh>();
            try
            {
                var primary = AddTestRenderer(root, meshes, "Primary", 137);
                var target = AddTestRenderer(root, meshes, "Target", 139);
                var component = root.AddComponent<AdvancedVisemeReconstructorData>();
                AddBlendShapeLink(
                    root,
                    primary,
                    new[] { primary },
                    false,
                    true,
                    new[]
                    {
                        new KeyValuePair<string, string>(JawShape, JawShape)
                    });
                AddBlendShapeLink(root, primary, new[] { target }, true, true, null);

                var plans = (IEnumerable)InvokeLinkedPlanner(root, component, primary);
                Assert.That(plans.Cast<object>().Count(), Is.EqualTo(1),
                    "An articulation-only primary self-link must not poison a valid direct link.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                foreach (var mesh in meshes) UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void PlannerRejectsRenamedPrimarySelfLink()
        {
            var root = new GameObject("Renamed Self Link Avatar");
            var meshes = new List<Mesh>();
            try
            {
                var primary = AddTestRenderer(root, meshes, "Primary", 149);
                var component = root.AddComponent<AdvancedVisemeReconstructorData>();
                AddBlendShapeLink(
                    root,
                    primary,
                    new[] { primary },
                    false,
                    true,
                    new[]
                    {
                        new KeyValuePair<string, string>(JawShape, PuckerShape)
                    });

                var exception = Assert.Throws<TargetInvocationException>(() =>
                    InvokeLinkedPlanner(root, component, primary));
                Assert.That(exception.InnerException,
                    Is.InstanceOf<InvalidOperationException>());
                Assert.That(exception.InnerException.Message,
                    Does.Contain("renames blendshapes")
                        .And.Contain(JawShape + " -> " + PuckerShape)
                        .And.Contain("same renderer"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                foreach (var mesh in meshes) UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        private static SkinnedMeshRenderer AddTestRenderer(
            GameObject root,
            ICollection<Mesh> meshes,
            string name,
            int seed)
        {
            var mesh = CreateMesh(
                name, StandardVisemeNames(), new[] { JawShape },
                new[] { PuckerShape }, seed);
            meshes.Add(mesh);
            var child = new GameObject(name);
            child.transform.SetParent(root.transform, false);
            var renderer = child.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            return renderer;
        }

        private static object InvokeLinkedPlanner(
            GameObject root,
            AdvancedVisemeReconstructorData component,
            SkinnedMeshRenderer primary)
        {
            var method = typeof(AdvancedVisemeReconstructorProcessor).GetMethod(
                "BuildLinkedRendererPlans",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            var skipped = 0;
            var arguments = new object[]
            {
                root,
                component,
                primary,
                AnimationUtility.CalculateTransformPath(primary.transform, root.transform),
                primary.sharedMesh,
                StandardVisemeNames(),
                AdvancedVisemeBlendShapeLinkCatalog.Scan(root),
                skipped
            };
            return method.Invoke(null, arguments);
        }

        private static AdvancedVisemeMeshCalibrator.Result BuildTargetOverlay(
            AdvancedVisemeBlendShapeLinkCatalog.Target target,
            float[,] referenceCoefficients)
        {
            var mesh = target.linkedRenderer.sharedMesh;
            var visemes = new AdvancedVisemeMeshCalibrator.BlendShapePoseInput[
                VisemeReconstructionProfile.VisemeCount];
            for (var viseme = 0; viseme < visemes.Length; viseme++)
            {
                visemes[viseme] = PoseForMappings(
                    mesh,
                    target.LinkedShapesFor(VisemeName(viseme)));
            }

            var basis = new[]
            {
                new AdvancedVisemeMeshCalibrator.LinkedBasisPoseInput(
                    AdvancedVisemeArticulator.JawOpen,
                    1,
                    PoseForMappings(mesh, target.LinkedShapesFor(JawShape))),
                new AdvancedVisemeMeshCalibrator.LinkedBasisPoseInput(
                    AdvancedVisemeArticulator.LipPucker,
                    1,
                    PoseForMappings(mesh, target.LinkedShapesFor(PuckerShape)))
            };

            return AdvancedVisemeMeshCalibrator.BuildLinkedTarget(
                mesh,
                visemes,
                basis,
                referenceCoefficients,
                "YUCP_AVR_Link");
        }

        private static AdvancedVisemeMeshCalibrator.BlendShapePoseInput PoseForMappings(
            Mesh mesh,
            IEnumerable<string> shapeNames)
        {
            var elements = (shapeNames ?? Array.Empty<string>())
                .Select(mesh.GetBlendShapeIndex)
                .Where(index => index >= 0)
                .Select(index => new AdvancedVisemeMeshCalibrator.BlendShapePoseElement(index, 100f))
                .ToArray();
            return new AdvancedVisemeMeshCalibrator.BlendShapePoseInput(elements);
        }

        private static void AssertGeometryParityRandomized(
            LinkFixture fixture,
            IReadOnlyList<AdvancedVisemeBlendShapeLinkCatalog.Target> targets,
            IReadOnlyDictionary<SkinnedMeshRenderer, Mesh> sourceMeshes,
            IReadOnlyDictionary<AdvancedVisemeBlendShapeLinkCatalog.Target,
                AdvancedVisemeMeshCalibrator.Result> results,
            int sampleCount)
        {
            var random = new System.Random(0x51A7F00D);
            foreach (var target in targets)
            {
                var source = sourceMeshes[target.linkedRenderer];
                var result = results[target];
                var visemePoses = Enumerable.Range(0, VisemeReconstructionProfile.VisemeCount)
                    .Select(index => PoseForMappings(
                        source, target.LinkedShapesFor(VisemeName(index))))
                    .ToArray();
                var basisPoses = new[]
                {
                    PoseForMappings(source, target.LinkedShapesFor(JawShape)),
                    PoseForMappings(source, target.LinkedShapesFor(PuckerShape))
                };
                var hasAuthoredVisemes = visemePoses.Any(pose => pose.isMapped);

                for (var sample = 0; sample < sampleCount; sample++)
                {
                    var weights = sample < VisemeReconstructionProfile.VisemeCount
                        ? OneHot(sample)
                        : RandomSimplex(random, VisemeReconstructionProfile.VisemeCount);
                    var articulation = new[]
                    {
                        sample == 0 ? 0f : sample == 1 ? 1f : (float)random.NextDouble(),
                        sample == 2 ? 0f : sample == 3 ? 1f : (float)random.NextDouble()
                    };

                    var actual = Delta.Zero(source.vertexCount);
                    for (var column = 0; column < basisPoses.Length; column++)
                        AddScaled(actual, ReadPose(source, basisPoses[column]), articulation[column]);
                    for (var viseme = 0; viseme < weights.Length; viseme++)
                    {
                        var residualName = result.residualBlendShapeNames[viseme];
                        if (string.IsNullOrEmpty(residualName)) continue;
                        AddScaled(actual,
                            ReadShape(result.mesh, result.mesh.GetBlendShapeIndex(residualName)),
                            weights[viseme]);
                    }

                    var expected = Delta.Zero(source.vertexCount);
                    if (hasAuthoredVisemes)
                    {
                        for (var viseme = 0; viseme < weights.Length; viseme++)
                            AddScaled(expected, ReadPose(source, visemePoses[viseme]), weights[viseme]);
                        for (var column = 0; column < basisPoses.Length; column++)
                        {
                            var speechCoordinate = 0f;
                            for (var viseme = 0; viseme < weights.Length; viseme++)
                                speechCoordinate += fixture.referenceCoefficients[viseme, column] *
                                                    weights[viseme];
                            AddScaled(expected, ReadPose(source, basisPoses[column]),
                                articulation[column] - speechCoordinate);
                        }
                    }
                    else
                    {
                        // An articulation-only accessory has no authored viseme
                        // geometry to reconstruct. It must retain the full copied
                        // jaw/lip coordinate instead of receiving a carrier delta.
                        for (var column = 0; column < basisPoses.Length; column++)
                            AddScaled(expected, ReadPose(source, basisPoses[column]), articulation[column]);
                    }

                    AssertDeltaEqual(expected, actual,
                        $"{target.linkedPath}, randomized sample {sample}");
                }
            }
        }

        private static void AssertCurveOverlayIsDisjointAndPrimaryCurvesAreUnchanged(
            LinkFixture fixture,
            IReadOnlyList<AdvancedVisemeBlendShapeLinkCatalog.Target> targets,
            IReadOnlyDictionary<AdvancedVisemeBlendShapeLinkCatalog.Target,
                AdvancedVisemeMeshCalibrator.Result> results,
            AnimationClip clip)
        {
            var primaryCurves = new Dictionary<string, AnimationCurve>(StringComparer.Ordinal);
            foreach (var pair in new[]
                     {
                         new KeyValuePair<string, float>(JawShape, 37f),
                         new KeyValuePair<string, float>(PuckerShape, 61f)
                     })
            {
                var binding = BlendShapeBinding(
                    AnimationUtility.CalculateTransformPath(
                        fixture.primary.transform, fixture.root.transform), pair.Key);
                var curve = AnimationCurve.Constant(0f, 1f / 60f, pair.Value);
                AnimationUtility.SetEditorCurve(clip, binding, curve);
                primaryCurves[BindingKey(binding)] = CloneCurve(curve);
            }

            var allBindings = new HashSet<string>(primaryCurves.Keys, StringComparer.Ordinal);
            var copiedArticulation = new HashSet<string>(StringComparer.Ordinal);
            foreach (var target in targets)
            {
                foreach (var baseShape in new[] { JawShape, PuckerShape })
                {
                    var sourceBinding = BlendShapeBinding(target.basePath, baseShape);
                    var sourceCurve = AnimationUtility.GetEditorCurve(clip, sourceBinding);
                    Assert.That(sourceCurve, Is.Not.Null,
                        $"Primary output curve '{baseShape}' is missing.");
                    foreach (var linkedShape in target.LinkedShapesFor(baseShape))
                    {
                        var binding = BlendShapeBinding(target.linkedPath, linkedShape);
                        var key = BindingKey(binding);
                        Assert.That(allBindings.Add(key), Is.True,
                            $"VRCFury would write duplicate target binding '{key}'.");
                        copiedArticulation.Add(key);
                        AnimationUtility.SetEditorCurve(clip, binding, sourceCurve);
                    }
                }

                var result = results[target];
                for (var viseme = 0; viseme < VisemeReconstructionProfile.VisemeCount; viseme++)
                {
                    var residualName = result.residualBlendShapeNames[viseme];
                    if (string.IsNullOrEmpty(residualName)) continue;
                    var binding = BlendShapeBinding(target.linkedPath, residualName);
                    var key = BindingKey(binding);
                    Assert.That(copiedArticulation.Contains(key), Is.False,
                        "A target residual collided with a VRCFury-copied articulation curve.");
                    Assert.That(allBindings.Add(key), Is.True,
                        $"YUCP generated duplicate overlay binding '{key}'.");
                    AnimationUtility.SetEditorCurve(clip, binding,
                        AnimationCurve.Constant(0f, 1f / 60f, 17f + viseme));
                }
            }

            var emitted = AnimationUtility.GetCurveBindings(clip)
                .Select(BindingKey)
                .ToArray();
            Assert.That(emitted.Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(emitted.Length),
                "The final clip contains duplicate curve bindings.");
            CollectionAssert.AreEquivalent(allBindings, emitted);

            foreach (var pair in primaryCurves)
            {
                var binding = AnimationUtility.GetCurveBindings(clip)
                    .Single(candidate => BindingKey(candidate) == pair.Key);
                AssertCurvesEqual(pair.Value, AnimationUtility.GetEditorCurve(clip, binding), pair.Key);
            }
        }

        private static void AssertResidualAvailability(
            AdvancedVisemeBlendShapeLinkCatalog.Target target,
            AdvancedVisemeMeshCalibrator.Result result)
        {
            var mappedVisemeCount = Enumerable.Range(0, VisemeReconstructionProfile.VisemeCount)
                .Count(index => target.Maps(VisemeName(index)));
            Assert.That(result.residualBlendShapeNames.Count(name => !string.IsNullOrEmpty(name)),
                Is.EqualTo(mappedVisemeCount), target.linkedPath);
            for (var viseme = 0; viseme < VisemeReconstructionProfile.VisemeCount; viseme++)
            {
                var residualName = result.residualBlendShapeNames[viseme];
                if (target.Maps(VisemeName(viseme)))
                {
                    Assert.That(residualName, Is.Not.Null.And.Not.Empty, target.linkedPath);
                    Assert.That(result.mesh.GetBlendShapeIndex(residualName), Is.GreaterThanOrEqualTo(0));
                }
                else
                {
                    Assert.That(residualName, Is.Null.Or.Empty, target.linkedPath);
                }
            }
        }

        private static void AssertReferenceCoefficients(
            float[,] actual,
            float[,] expected,
            string context)
        {
            Assert.That(actual.GetLength(0), Is.EqualTo(expected.GetLength(0)), context);
            Assert.That(actual.GetLength(1), Is.EqualTo(expected.GetLength(1)), context);
            for (var row = 0; row < expected.GetLength(0); row++)
            for (var column = 0; column < expected.GetLength(1); column++)
                Assert.That(actual[row, column], Is.EqualTo(expected[row, column]).Within(1e-7f),
                    $"{context}: coefficient [{row},{column}]");
        }

        private static string[] ReadInstalledVrcFuryMappings(
            Component component,
            SkinnedMeshRenderer baseRenderer,
            SkinnedMeshRenderer linkedRenderer)
        {
            var content = ReadField(component, "content");
            Assert.That(content, Is.Not.Null, "VRCFury feature content is missing.");
            var builderType = FindType("VF.Feature.BlendShapeLinkBuilder");
            var getMappings = builderType.GetMethod(
                "GetMappings", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(getMappings, Is.Not.Null,
                "Installed VRCFury no longer exposes its structural mapping method.");
            var exact = (bool)ReadField(content, "exactMatch");
            var output = getMappings.Invoke(
                null, new object[] { content, baseRenderer, linkedRenderer, exact });
            Assert.That(output, Is.InstanceOf<IEnumerable>());

            var mappings = new List<string>();
            foreach (var entry in (IEnumerable)output)
            {
                var type = entry.GetType();
                var key = type.GetProperty("Key")?.GetValue(entry, null) as string;
                var value = type.GetProperty("Value")?.GetValue(entry, null) as string;
                mappings.Add(PairKey(key, value));
            }
            return mappings.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        }

        private static Component AddBlendShapeLink(
            GameObject host,
            SkinnedMeshRenderer baseRenderer,
            IReadOnlyList<SkinnedMeshRenderer> linkedRenderers,
            bool includeAll,
            bool exactMatch,
            IEnumerable<KeyValuePair<string, string>> includes)
        {
            var componentType = FindType("VF.Model.VRCFury");
            var featureType = FindType("VF.Model.Feature.BlendShapeLink");
            var linkSkinType = featureType.GetNestedType("LinkSkin", BindingFlags.Public | BindingFlags.NonPublic);
            var includeType = featureType.GetNestedType("Include", BindingFlags.Public | BindingFlags.NonPublic);
            var component = host.AddComponent(componentType);
            var feature = Activator.CreateInstance(featureType);
            WriteField(feature, "baseObj", baseRenderer.gameObject.name);
            WriteField(feature, "includeAll", includeAll);
            WriteField(feature, "exactMatch", exactMatch);

            var skins = (IList)ReadField(feature, "linkSkins");
            foreach (var renderer in linkedRenderers)
            {
                var link = Activator.CreateInstance(linkSkinType);
                WriteField(link, "renderer", renderer);
                skins.Add(link);
            }

            var includeList = (IList)ReadField(feature, "includes");
            foreach (var pair in includes ?? Enumerable.Empty<KeyValuePair<string, string>>())
            {
                var include = Activator.CreateInstance(includeType);
                WriteField(include, "nameOnBase", pair.Key);
                WriteField(include, "nameOnLinked", pair.Value);
                includeList.Add(include);
            }
            WriteField(component, "content", feature);
            return component;
        }

        private static Type FindType(string fullName)
        {
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(fullName, false))
                .FirstOrDefault(candidate => candidate != null);
            Assert.That(type, Is.Not.Null, $"Installed VRCFury type '{fullName}' was not found.");
            return type;
        }

        private static object ReadField(object instance, string name)
        {
            var field = instance.GetType().GetField(
                name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null,
                $"Field '{instance.GetType().FullName}.{name}' was not found.");
            return field.GetValue(instance);
        }

        private static void WriteField(object instance, string name, object value)
        {
            var field = instance.GetType().GetField(
                name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null,
                $"Field '{instance.GetType().FullName}.{name}' was not found.");
            field.SetValue(instance, value);
        }

        private static void AssertMappingCount(
            IEnumerable<AdvancedVisemeBlendShapeLinkCatalog.Target> targets,
            SkinnedMeshRenderer renderer,
            int expected)
        {
            Assert.That(TargetFor(targets, renderer).mappings.Count, Is.EqualTo(expected), renderer.name);
        }

        private static AdvancedVisemeBlendShapeLinkCatalog.Target TargetFor(
            IEnumerable<AdvancedVisemeBlendShapeLinkCatalog.Target> targets,
            SkinnedMeshRenderer renderer)
        {
            return targets.Single(target => target.linkedRenderer == renderer);
        }

        private static EditorCurveBinding BlendShapeBinding(string path, string shape)
        {
            return EditorCurveBinding.FloatCurve(
                path, typeof(SkinnedMeshRenderer), "blendShape." + shape);
        }

        private static string BindingKey(EditorCurveBinding binding)
        {
            return binding.path + "\u001f" + binding.type.FullName + "\u001f" + binding.propertyName;
        }

        private static string PairKey(string from, string to)
        {
            return (from ?? string.Empty) + "\u001f" + (to ?? string.Empty);
        }

        private static AnimationCurve CloneCurve(AnimationCurve source)
        {
            return new AnimationCurve(source.keys)
            {
                preWrapMode = source.preWrapMode,
                postWrapMode = source.postWrapMode
            };
        }

        private static void AssertCurvesEqual(AnimationCurve expected, AnimationCurve actual, string context)
        {
            Assert.That(actual, Is.Not.Null, context);
            Assert.That(actual.preWrapMode, Is.EqualTo(expected.preWrapMode), context);
            Assert.That(actual.postWrapMode, Is.EqualTo(expected.postWrapMode), context);
            Assert.That(actual.keys, Has.Length.EqualTo(expected.keys.Length), context);
            for (var index = 0; index < expected.keys.Length; index++)
            {
                Assert.That(actual.keys[index].time,
                    Is.EqualTo(expected.keys[index].time).Within(1e-7f), context);
                Assert.That(actual.keys[index].value,
                    Is.EqualTo(expected.keys[index].value).Within(1e-7f), context);
                Assert.That(actual.keys[index].inTangent,
                    Is.EqualTo(expected.keys[index].inTangent), context);
                Assert.That(actual.keys[index].outTangent,
                    Is.EqualTo(expected.keys[index].outTangent), context);
            }
        }

        private static string VisemeName(int index)
        {
            return "vrc.v_" + VisemeReconstructionProfile.VisemeNames[index];
        }

        private static float[,] ReferenceCoefficients()
        {
            var output = new float[VisemeReconstructionProfile.VisemeCount, 2];
            for (var viseme = 1; viseme < output.GetLength(0); viseme++)
            {
                output[viseme, 0] = 0.08f * (1 + viseme % 5);
                output[viseme, 1] = 0.07f * (viseme % 4);
            }
            return output;
        }

        private static float[] OneHot(int index)
        {
            var output = new float[VisemeReconstructionProfile.VisemeCount];
            output[Mathf.Clamp(index, 0, output.Length - 1)] = 1f;
            return output;
        }

        private static float[] RandomSimplex(System.Random random, int count)
        {
            var output = new float[count];
            var sum = 0f;
            for (var index = 0; index < count; index++)
            {
                output[index] = (float)(-Math.Log(Math.Max(1e-12, random.NextDouble())));
                sum += output[index];
            }
            for (var index = 0; index < count; index++) output[index] /= sum;
            return output;
        }

        private static Delta ReadPose(
            Mesh mesh,
            AdvancedVisemeMeshCalibrator.BlendShapePoseInput pose)
        {
            var output = Delta.Zero(mesh.vertexCount);
            for (var element = 0; element < pose.Count; element++)
            {
                var term = pose[element];
                AddScaled(output, ReadShape(mesh, term.blendShapeIndex), term.endpointWeight / 100f);
            }
            return output;
        }

        private static Delta ReadShape(Mesh mesh, int blendShapeIndex)
        {
            Assert.That(blendShapeIndex, Is.GreaterThanOrEqualTo(0));
            var output = Delta.Zero(mesh.vertexCount);
            var frame = mesh.GetBlendShapeFrameCount(blendShapeIndex) - 1;
            mesh.GetBlendShapeFrameVertices(
                blendShapeIndex, frame, output.vertices, output.normals, output.tangents);
            var frameWeight = mesh.GetBlendShapeFrameWeight(blendShapeIndex, frame);
            var scale = 100f / frameWeight;
            Scale(output, scale);
            return output;
        }

        private static void AddScaled(Delta target, Delta value, float scale)
        {
            for (var vertex = 0; vertex < target.vertices.Length; vertex++)
            {
                target.vertices[vertex] += value.vertices[vertex] * scale;
                target.normals[vertex] += value.normals[vertex] * scale;
                target.tangents[vertex] += value.tangents[vertex] * scale;
            }
        }

        private static void Scale(Delta target, float scale)
        {
            for (var vertex = 0; vertex < target.vertices.Length; vertex++)
            {
                target.vertices[vertex] *= scale;
                target.normals[vertex] *= scale;
                target.tangents[vertex] *= scale;
            }
        }

        private static void AssertDeltaEqual(Delta expected, Delta actual, string context)
        {
            for (var vertex = 0; vertex < expected.vertices.Length; vertex++)
            {
                Assert.That(Vector3.Distance(actual.vertices[vertex], expected.vertices[vertex]),
                    Is.LessThan(GeometryTolerance), $"{context}: vertex {vertex}");
                Assert.That(Vector3.Distance(actual.normals[vertex], expected.normals[vertex]),
                    Is.LessThan(GeometryTolerance), $"{context}: normal {vertex}");
                Assert.That(Vector3.Distance(actual.tangents[vertex], expected.tangents[vertex]),
                    Is.LessThan(GeometryTolerance), $"{context}: tangent {vertex}");
            }
        }

        private static string MeshFingerprint(Mesh mesh)
        {
            var output = new StringBuilder();
            output.Append(mesh.vertexCount).Append('|').Append(mesh.blendShapeCount).Append('|');
            for (var shape = 0; shape < mesh.blendShapeCount; shape++)
            {
                output.Append(mesh.GetBlendShapeName(shape)).Append('|');
                for (var frame = 0; frame < mesh.GetBlendShapeFrameCount(shape); frame++)
                {
                    output.Append(mesh.GetBlendShapeFrameWeight(shape, frame).ToString("R", CultureInfo.InvariantCulture))
                        .Append('|');
                    var delta = Delta.Zero(mesh.vertexCount);
                    mesh.GetBlendShapeFrameVertices(
                        shape, frame, delta.vertices, delta.normals, delta.tangents);
                    AppendVectors(output, delta.vertices);
                    AppendVectors(output, delta.normals);
                    AppendVectors(output, delta.tangents);
                }
            }
            return Hash128.Compute(output.ToString()).ToString();
        }

        private static void AppendVectors(StringBuilder output, IEnumerable<Vector3> values)
        {
            foreach (var value in values)
            {
                output.Append(value.x.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(value.y.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(value.z.ToString("R", CultureInfo.InvariantCulture)).Append(';');
            }
        }

        private sealed class LinkFixture : IDisposable
        {
            public readonly GameObject root;
            public readonly SkinnedMeshRenderer primary;
            public readonly SkinnedMeshRenderer full;
            public readonly SkinnedMeshRenderer visemesOnly;
            public readonly SkinnedMeshRenderer articulationOnly;
            public readonly SkinnedMeshRenderer subsetArticulation;
            public readonly SkinnedMeshRenderer fuzzyIncludeAll;
            public readonly SkinnedMeshRenderer renamed;
            public readonly SkinnedMeshRenderer oneToMany;
            public readonly IReadOnlyList<SkinnedMeshRenderer> targets;
            public readonly float[,] referenceCoefficients = ReferenceCoefficients();
            private readonly List<Mesh> sourceMeshes = new List<Mesh>();

            public LinkFixture()
            {
                root = new GameObject("Avatar Root");
                primary = AddRenderer("Primary Face", CreateMesh(
                    "Primary", StandardVisemeNames(), new[] { JawShape }, new[] { PuckerShape }, 11));
                full = AddRenderer("Full V plus U", CreateMesh(
                    "Full", StandardVisemeNames(), new[] { JawShape }, new[] { PuckerShape }, 23));
                visemesOnly = AddRenderer("V Only", CreateMesh(
                    "V Only", StandardVisemeNames(), Array.Empty<string>(), Array.Empty<string>(), 31));
                articulationOnly = AddRenderer("U Only", CreateMesh(
                    "U Only", null, new[] { JawShape }, new[] { PuckerShape }, 43));
                subsetArticulation = AddRenderer("Subset U", CreateMesh(
                    "Subset U", StandardVisemeNames(), new[] { JawShape }, Array.Empty<string>(), 59));
                fuzzyIncludeAll = AddRenderer("Fuzzy Include All", CreateMesh(
                    "Fuzzy", StandardVisemeNames().Select(name => name.ToUpperInvariant()).ToArray(),
                    new[] { "jaw open" }, new[] { "lip pucker" }, 61));
                renamed = AddRenderer("Renamed", CreateMesh(
                    "Renamed", VisemeReconstructionProfile.VisemeNames
                        .Select(name => "phone_" + name).ToArray(),
                    new[] { "Custom Jaw" }, new[] { "Custom Pucker" }, 73));
                oneToMany = AddRenderer("One To Many", CreateMesh(
                    "One To Many", VisemeReconstructionProfile.VisemeNames
                        .Select(name => "one_" + name).ToArray(),
                    new[] { "Jaw A", "Jaw B" }, new[] { "Pucker Linked" }, 89));

                targets = new[]
                {
                    full, visemesOnly, articulationOnly, subsetArticulation,
                    fuzzyIncludeAll, renamed, oneToMany
                };

                AddBlendShapeLink(
                    root,
                    primary,
                    new[] { full, visemesOnly, articulationOnly, subsetArticulation, fuzzyIncludeAll },
                    includeAll: true,
                    exactMatch: false,
                    includes: null);

                var renamedMappings = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>(JawShape, "Custom Jaw"),
                    new KeyValuePair<string, string>(PuckerShape, "Custom Pucker")
                };
                renamedMappings.AddRange(Enumerable.Range(0, VisemeReconstructionProfile.VisemeCount)
                    .Select(index => new KeyValuePair<string, string>(
                        VisemeName(index), "phone_" + VisemeReconstructionProfile.VisemeNames[index])));
                AddBlendShapeLink(root, primary, new[] { renamed }, false, true, renamedMappings);

                var oneToManyMappings = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>(JawShape, "Jaw A"),
                    new KeyValuePair<string, string>(JawShape, "Jaw A"),
                    new KeyValuePair<string, string>(JawShape, "Jaw B"),
                    new KeyValuePair<string, string>(PuckerShape, "Pucker Linked")
                };
                oneToManyMappings.AddRange(Enumerable.Range(0, VisemeReconstructionProfile.VisemeCount)
                    .Select(index => new KeyValuePair<string, string>(
                        VisemeName(index), "one_" + VisemeReconstructionProfile.VisemeNames[index])));
                AddBlendShapeLink(root, primary, new[] { oneToMany }, false, true, oneToManyMappings);
            }

            private SkinnedMeshRenderer AddRenderer(string objectName, Mesh mesh)
            {
                sourceMeshes.Add(mesh);
                var child = new GameObject(objectName);
                child.transform.SetParent(root.transform, false);
                var renderer = child.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = mesh;
                return renderer;
            }

            public void Dispose()
            {
                UnityEngine.Object.DestroyImmediate(root);
                foreach (var mesh in sourceMeshes)
                    if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        private static Mesh CreateMesh(
            string name,
            IReadOnlyList<string> visemeNames,
            IReadOnlyList<string> jawShapeNames,
            IReadOnlyList<string> puckerShapeNames,
            int seed)
        {
            const int vertexCount = 8;
            var mesh = new Mesh { name = name };
            mesh.vertices = Enumerable.Range(0, vertexCount)
                .Select(index => new Vector3(index * 0.01f, 0f, 0f)).ToArray();
            mesh.normals = Enumerable.Repeat(Vector3.forward, vertexCount).ToArray();
            mesh.tangents = Enumerable.Repeat(new Vector4(1f, 0f, 0f, 1f), vertexCount).ToArray();
            mesh.triangles = new[] { 0, 1, 2, 2, 3, 0 };

            var jawParts = CreateBasisParts(vertexCount, 0, jawShapeNames.Count, seed, true);
            var puckerParts = CreateBasisParts(vertexCount, 1, puckerShapeNames.Count, seed + 7, false);
            for (var index = 0; index < jawShapeNames.Count; index++)
                AddShape(mesh, jawShapeNames[index], jawParts[index]);
            for (var index = 0; index < puckerShapeNames.Count; index++)
                AddShape(mesh, puckerShapeNames[index], puckerParts[index]);

            // Even a V-only or subset target has an authored conceptual mouth
            // basis. Its absent mapped part is intentionally absorbed by R.
            var conceptualJaw = jawParts.Count == 0
                ? CreateBasisDelta(vertexCount, 0, seed, true)
                : Sum(jawParts, vertexCount);
            var conceptualPucker = puckerParts.Count == 0
                ? CreateBasisDelta(vertexCount, 1, seed + 7, false)
                : Sum(puckerParts, vertexCount);

            if (visemeNames != null)
            {
                Assert.That(visemeNames.Count, Is.EqualTo(VisemeReconstructionProfile.VisemeCount));
                var coefficients = ReferenceCoefficients();
                for (var viseme = 0; viseme < visemeNames.Count; viseme++)
                {
                    var pose = Delta.Zero(vertexCount);
                    AddScaled(pose, conceptualJaw, coefficients[viseme, 0]);
                    AddScaled(pose, conceptualPucker, coefficients[viseme, 1]);
                    if (viseme > 0)
                    {
                        var detailVertex = 2 + viseme % (vertexCount - 2);
                        var detail = Delta.Zero(vertexCount);
                        var amplitude = 0.001f * (1 + (seed + viseme) % 9);
                        detail.vertices[detailVertex] = new Vector3(amplitude, -amplitude * 0.4f, amplitude * 0.2f);
                        detail.normals[detailVertex] = new Vector3(-amplitude * 0.3f, amplitude * 0.5f, amplitude);
                        detail.tangents[detailVertex] = new Vector3(amplitude * 0.7f, amplitude * 0.1f, -amplitude * 0.2f);
                        AddScaled(pose, detail, 1f);
                    }
                    AddShape(mesh, visemeNames[viseme], pose);
                }
            }

            return mesh;
        }

        private static List<Delta> CreateBasisParts(
            int vertexCount,
            int vertex,
            int count,
            int seed,
            bool jaw)
        {
            var output = new List<Delta>();
            for (var index = 0; index < count; index++)
            {
                var full = CreateBasisDelta(vertexCount, vertex, seed, jaw);
                Scale(full, 1f / count);
                output.Add(full);
            }
            return output;
        }

        private static Delta CreateBasisDelta(
            int vertexCount,
            int vertex,
            int seed,
            bool jaw)
        {
            var output = Delta.Zero(vertexCount);
            var scale = 0.01f * (1 + seed % 7);
            output.vertices[vertex] = jaw
                ? new Vector3(scale, scale * 0.2f, -scale * 0.1f)
                : new Vector3(-scale * 0.15f, scale, scale * 0.25f);
            output.normals[vertex] = jaw
                ? new Vector3(scale * 0.3f, -scale * 0.2f, scale * 0.5f)
                : new Vector3(scale * 0.1f, scale * 0.4f, -scale * 0.2f);
            output.tangents[vertex] = jaw
                ? new Vector3(-scale * 0.1f, scale * 0.35f, scale * 0.2f)
                : new Vector3(scale * 0.45f, -scale * 0.1f, scale * 0.15f);
            return output;
        }

        private static Delta Sum(IEnumerable<Delta> values, int vertexCount)
        {
            var output = Delta.Zero(vertexCount);
            foreach (var value in values) AddScaled(output, value, 1f);
            return output;
        }

        private static void AddShape(Mesh mesh, string name, Delta delta)
        {
            mesh.AddBlendShapeFrame(name, 100f, delta.vertices, delta.normals, delta.tangents);
        }

        private static string[] StandardVisemeNames()
        {
            return VisemeReconstructionProfile.VisemeNames.Select(name => "vrc.v_" + name).ToArray();
        }

        private sealed class Delta
        {
            public readonly Vector3[] vertices;
            public readonly Vector3[] normals;
            public readonly Vector3[] tangents;

            private Delta(int vertexCount)
            {
                vertices = new Vector3[vertexCount];
                normals = new Vector3[vertexCount];
                tangents = new Vector3[vertexCount];
            }

            public static Delta Zero(int vertexCount) => new Delta(vertexCount);
        }
    }
}
#endif
