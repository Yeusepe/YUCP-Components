using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace YUCP.Components.Editor.Tests
{
    public sealed class AdvancedVisemeBlendShapeLinkCatalogTests
    {
        [Test]
        public void ExplicitMappingsUseShortestBaseAndPreserveOneToMany()
        {
            using (var fixture = new Fixture())
            {
                var shallowBase = fixture.Renderer(fixture.root.transform, "Body", "Smile");
                var group = fixture.Object(fixture.root.transform, "Nested");
                fixture.Renderer(group.transform, "Body", "WrongShape");
                var linked = fixture.Renderer(
                    fixture.root.transform, "Accessory", "HatSmile", "HatSmile2");
                var host = fixture.Object(fixture.root.transform, "Link Host");
                fixture.Link(host, "Body", new[] { linked }, false, true,
                    includes: new[]
                    {
                        Pair("Smile", "HatSmile"),
                        Pair("Smile", "HatSmile"),
                        Pair("Smile", "HatSmile2")
                    });

                var catalog = AdvancedVisemeBlendShapeLinkCatalog.Scan(fixture.root);

                Assert.That(catalog.Targets, Has.Count.EqualTo(1));
                var target = catalog.Targets[0];
                Assert.That(target.baseRenderer, Is.SameAs(shallowBase),
                    "VRCFury selects the matching base object with the shortest avatar path.");
                Assert.That(target.linkedRenderer, Is.SameAs(linked));
                Assert.That(target.sourcePath, Is.EqualTo("Link Host"));
                Assert.That(target.basePath, Is.EqualTo("Body"));
                Assert.That(target.linkedPath, Is.EqualTo("Accessory"));
                Assert.That(target.mappings, Is.EqualTo(new[]
                {
                    new AdvancedVisemeBlendShapeLinkCatalog.Mapping("Smile", "HatSmile"),
                    new AdvancedVisemeBlendShapeLinkCatalog.Mapping("Smile", "HatSmile2")
                }));
                Assert.That(target.LinkedShapesFor("Smile"),
                    Is.EqualTo(new[] { "HatSmile", "HatSmile2" }));
                Assert.That(target.Maps("Smile"), Is.True);
                Assert.That(target.Maps("WrongShape"), Is.False);
                Assert.That(target.isExact, Is.True);
                Assert.That(catalog.FindTargets(shallowBase), Is.EqualTo(new[] { target }));
            }
        }

        [Test]
        public void IncludeAllUsesVrcFuryFuzzyExclusionAndAmbiguityRules()
        {
            using (var fixture = new Fixture())
            {
                var body = fixture.Renderer(
                    fixture.root.transform, "Body", "Jaw Open", "Smile", "Lipa");
                var linked = fixture.Renderer(
                    fixture.root.transform, "Linked", "jawopen", "Smile", "Lip A", "lipa");
                var host = fixture.Object(fixture.root.transform, "Link Host");
                fixture.Link(host, "Body", new[] { linked }, true, false,
                    excludes: new[] { "Smile" });

                var target = AdvancedVisemeBlendShapeLinkCatalog.Scan(fixture.root).Targets.Single();

                Assert.That(target.baseRenderer, Is.SameAs(body));
                Assert.That(target.mappings, Is.EqualTo(new[]
                {
                    new AdvancedVisemeBlendShapeLinkCatalog.Mapping("Jaw Open", "jawopen")
                }), "Excluded names are exact, while fuzzy candidates must be unique after case/whitespace normalization.");
                Assert.That(target.LinkedShapesFor("Smile"), Is.Empty);
                Assert.That(target.LinkedShapesFor("Lipa"), Is.Empty,
                    "The two normalized target candidates are ambiguous and must not be selected.");
            }
        }

        [Test]
        public void ExactModeDoesNotUseCaseOrWhitespaceFallback()
        {
            using (var fixture = new Fixture())
            {
                fixture.Renderer(fixture.root.transform, "Body", "Jaw Open");
                var linked = fixture.Renderer(fixture.root.transform, "Linked", "jawopen");
                var host = fixture.Object(fixture.root.transform, "Link Host");
                fixture.Link(host, "Body", new[] { linked }, true, true);

                var target = AdvancedVisemeBlendShapeLinkCatalog.Scan(fixture.root).Targets.Single();

                Assert.That(target.mappings, Is.Empty);
            }
        }

        [Test]
        public void ManyToOneIsDeterministicallyMarkedNonExact()
        {
            using (var fixture = new Fixture())
            {
                fixture.Renderer(fixture.root.transform, "Body", "Smile", "Frown");
                var linked = fixture.Renderer(fixture.root.transform, "Linked", "Expression");
                var host = fixture.Object(fixture.root.transform, "Link Host");
                fixture.Link(host, "Body", new[] { linked }, false, true,
                    includes: new[]
                    {
                        Pair("Smile", "Expression"),
                        Pair("Frown", "Expression"),
                        Pair("Smile", "Expression")
                    });

                var target = AdvancedVisemeBlendShapeLinkCatalog.Scan(fixture.root).Targets.Single();

                Assert.That(target.mappings, Has.Count.EqualTo(2),
                    "Duplicate identical mappings are harmless and collapse to one pair.");
                Assert.That(target.isExact, Is.False,
                    "VRCFury SetCurve order is unsafe when distinct base shapes target one linked property.");
                Assert.That(target.ambiguities, Has.Count.EqualTo(1));
                Assert.That(target.ambiguities[0].linkedBlendShape, Is.EqualTo("Expression"));
                Assert.That(target.ambiguities[0].baseBlendShapes,
                    Is.EqualTo(new[] { "Frown", "Smile" }));
                Assert.That(target.diagnostic, Does.Contain("Expression <- Frown, Smile"));
            }
        }

        [Test]
        public void FingerprintIsStableSensitiveAndScanIsReadOnly()
        {
            using (var fixture = new Fixture())
            {
                var body = fixture.Renderer(fixture.root.transform, "Body", "Jaw Open");
                var linked = fixture.Renderer(fixture.root.transform, "Linked", "jawopen");
                body.SetBlendShapeWeight(0, 23f);
                linked.SetBlendShapeWeight(0, 17f);
                var bodyMesh = body.sharedMesh;
                var linkedMesh = linked.sharedMesh;
                var host = fixture.Object(fixture.root.transform, "Link Host");
                var feature = fixture.Link(host, "Body", new[] { linked }, true, false);

                var first = AdvancedVisemeBlendShapeLinkCatalog.Scan(fixture.root);
                var second = AdvancedVisemeBlendShapeLinkCatalog.Scan(fixture.root);

                Assert.That(first.DependencyFingerprint, Is.EqualTo(second.DependencyFingerprint));
                Assert.That(first.DependencyFingerprint, Has.Length.EqualTo(32));
                Assert.That(body.sharedMesh, Is.SameAs(bodyMesh));
                Assert.That(linked.sharedMesh, Is.SameAs(linkedMesh));
                Assert.That(body.sharedMesh.blendShapeCount, Is.EqualTo(1));
                Assert.That(linked.sharedMesh.blendShapeCount, Is.EqualTo(1));
                Assert.That(body.GetBlendShapeWeight(0), Is.EqualTo(23f));
                Assert.That(linked.GetBlendShapeWeight(0), Is.EqualTo(17f));

                SetField(feature.content, "exactMatch", true);
                var changed = AdvancedVisemeBlendShapeLinkCatalog.Scan(fixture.root);
                Assert.That(changed.DependencyFingerprint, Is.Not.EqualTo(first.DependencyFingerprint));
                Assert.That(changed.Targets.Single().mappings, Is.Empty);
            }
        }

        [Test]
        public void PostGenerationMappingValidationDetectsIncludeAllRediscovery()
        {
            using (var fixture = new Fixture())
            {
                var body = fixture.Renderer(fixture.root.transform, "Body", "JawOpen");
                var linked = fixture.Renderer(fixture.root.transform, "Linked", "JawOpen");
                var host = fixture.Object(fixture.root.transform, "Link Host");
                fixture.Link(host, "Body", new[] { linked }, true, false);
                var before = AdvancedVisemeBlendShapeLinkCatalog.Scan(fixture.root);
                var bodyClone = UnityEngine.Object.Instantiate(body.sharedMesh);
                var linkedClone = UnityEngine.Object.Instantiate(linked.sharedMesh);
                try
                {
                    AddShape(bodyClone, "PrimaryOnlyCarrier");
                    AddShape(linkedClone, "TargetOnlyCarrier");
                    body.sharedMesh = bodyClone;
                    linked.sharedMesh = linkedClone;
                    Assert.That(before.HasEquivalentResolvedMappings(
                        AdvancedVisemeBlendShapeLinkCatalog.Scan(fixture.root), out var stableDiagnostic),
                        Is.True,
                        stableDiagnostic);

                    AddShape(bodyClone, "Generated Surface");
                    AddShape(linkedClone, "generatedsurface");
                    Assert.That(before.HasEquivalentResolvedMappings(
                        AdvancedVisemeBlendShapeLinkCatalog.Scan(fixture.root), out var driftDiagnostic),
                        Is.False);
                    Assert.That(driftDiagnostic, Does.Contain("added"));
                    Assert.That(driftDiagnostic, Does.Contain("Generated Surface"));
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(bodyClone);
                    UnityEngine.Object.DestroyImmediate(linkedClone);
                }
            }
        }

        private static void AddShape(Mesh mesh, string name)
        {
            mesh.AddBlendShapeFrame(
                name,
                100f,
                new[] { new Vector3(0.002f, 0f, 0f) },
                new Vector3[1],
                new Vector3[1]);
        }

        private static KeyValuePair<string, string> Pair(string baseName, string linkedName)
        {
            return new KeyValuePair<string, string>(baseName, linkedName);
        }

        private sealed class ReflectedFeature
        {
            public Component component;
            public object content;
        }

        private sealed class Fixture : IDisposable
        {
            public readonly GameObject root = new GameObject("Avatar");
            private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();

            public GameObject Object(Transform parent, string name)
            {
                var gameObject = new GameObject(name);
                gameObject.transform.SetParent(parent, false);
                owned.Add(gameObject);
                return gameObject;
            }

            public SkinnedMeshRenderer Renderer(Transform parent, string name, params string[] blendShapes)
            {
                var gameObject = Object(parent, name);
                var renderer = gameObject.AddComponent<SkinnedMeshRenderer>();
                var mesh = new Mesh { name = name + " Mesh" };
                mesh.vertices = new[] { Vector3.zero };
                foreach (var blendShape in blendShapes)
                {
                    mesh.AddBlendShapeFrame(
                        blendShape,
                        100f,
                        new[] { new Vector3(0.001f, 0f, 0f) },
                        new Vector3[1],
                        new Vector3[1]);
                }
                renderer.sharedMesh = mesh;
                owned.Add(mesh);
                return renderer;
            }

            public ReflectedFeature Link(
                GameObject host,
                string baseObjectName,
                IReadOnlyList<SkinnedMeshRenderer> linkedRenderers,
                bool includeAll,
                bool exactMatch,
                IReadOnlyList<string> excludes = null,
                IReadOnlyList<KeyValuePair<string, string>> includes = null)
            {
                var vrcFuryType = FindType("VF.Model.VRCFury");
                var linkType = FindType("VF.Model.Feature.BlendShapeLink");
                var component = host.AddComponent(vrcFuryType);
                var content = Activator.CreateInstance(linkType);
                SetField(content, "baseObj", baseObjectName);
                SetField(content, "includeAll", includeAll);
                SetField(content, "exactMatch", exactMatch);

                var linkSkinType = linkType.GetNestedType(
                    "LinkSkin", BindingFlags.Public | BindingFlags.NonPublic);
                var linkSkins = (IList)GetField(content, "linkSkins");
                foreach (var renderer in linkedRenderers)
                {
                    var linkSkin = Activator.CreateInstance(linkSkinType);
                    SetField(linkSkin, "renderer", renderer);
                    linkSkins.Add(linkSkin);
                }

                var excludeType = linkType.GetNestedType(
                    "Exclude", BindingFlags.Public | BindingFlags.NonPublic);
                var excludeList = (IList)GetField(content, "excludes");
                foreach (var name in excludes ?? Array.Empty<string>())
                {
                    var exclude = Activator.CreateInstance(excludeType);
                    SetField(exclude, "name", name);
                    excludeList.Add(exclude);
                }

                var includeType = linkType.GetNestedType(
                    "Include", BindingFlags.Public | BindingFlags.NonPublic);
                var includeList = (IList)GetField(content, "includes");
                foreach (var pair in includes ?? Array.Empty<KeyValuePair<string, string>>())
                {
                    var include = Activator.CreateInstance(includeType);
                    SetField(include, "nameOnBase", pair.Key);
                    SetField(include, "nameOnLinked", pair.Value);
                    includeList.Add(include);
                }

                SetField(component, "content", content);
                return new ReflectedFeature { component = component, content = content };
            }

            public void Dispose()
            {
                UnityEngine.Object.DestroyImmediate(root);
                foreach (var item in owned.Where(item => item is Mesh))
                    if (item != null) UnityEngine.Object.DestroyImmediate(item);
            }
        }

        private static Type FindType(string fullName)
        {
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(fullName, false))
                .FirstOrDefault(candidate => candidate != null);
            Assert.That(type, Is.Not.Null, "Required installed VRCFury type was not found: " + fullName);
            return type;
        }

        private static object GetField(object instance, string name)
        {
            var field = instance.GetType().GetField(
                name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Missing reflected VRCFury field: " + name);
            return field.GetValue(instance);
        }

        private static void SetField(object instance, string name, object value)
        {
            var field = instance.GetType().GetField(
                name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Missing reflected VRCFury field: " + name);
            field.SetValue(instance, value);
        }
    }
}
