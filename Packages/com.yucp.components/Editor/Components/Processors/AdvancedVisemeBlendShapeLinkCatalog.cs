using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Read-only projection of installed VRCFury BlendShape Link features.
    ///
    /// VRCFury's model classes are internal, so this catalog deliberately uses
    /// reflection at the component boundary and exposes only Unity objects and
    /// immutable value descriptors to the rest of YUCP. Mapping behavior mirrors
    /// the installed VRCFury 1.1363 BlendShapeLinkBuilder contract.
    /// </summary>
    internal sealed class AdvancedVisemeBlendShapeLinkCatalog
    {
        private const string VrcFuryComponentType = "VF.Model.VRCFury";
        private const string BlendShapeLinkContentType = "VF.Model.Feature.BlendShapeLink";

        internal readonly struct Mapping : IEquatable<Mapping>
        {
            public readonly string baseBlendShape;
            public readonly string linkedBlendShape;

            public Mapping(string baseBlendShape, string linkedBlendShape)
            {
                this.baseBlendShape = baseBlendShape;
                this.linkedBlendShape = linkedBlendShape;
            }

            public bool Equals(Mapping other)
            {
                return string.Equals(baseBlendShape, other.baseBlendShape, StringComparison.Ordinal) &&
                       string.Equals(linkedBlendShape, other.linkedBlendShape, StringComparison.Ordinal);
            }

            public override bool Equals(object obj) => obj is Mapping other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((baseBlendShape != null ? baseBlendShape.GetHashCode() : 0) * 397) ^
                           (linkedBlendShape != null ? linkedBlendShape.GetHashCode() : 0);
                }
            }

            public override string ToString() => baseBlendShape + " -> " + linkedBlendShape;
        }

        internal sealed class MappingAmbiguity
        {
            public readonly string linkedBlendShape;
            public readonly IReadOnlyList<string> baseBlendShapes;

            internal MappingAmbiguity(string linkedBlendShape, IEnumerable<string> baseBlendShapes)
            {
                this.linkedBlendShape = linkedBlendShape;
                this.baseBlendShapes = Array.AsReadOnly(baseBlendShapes
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray());
            }
        }

        internal sealed class Target
        {
            private readonly IReadOnlyList<Mapping> mappingView;
            private readonly IReadOnlyList<MappingAmbiguity> ambiguityView;
            private readonly Dictionary<string, IReadOnlyList<string>> linkedByBase;

            public readonly Component sourceComponent;
            public readonly SkinnedMeshRenderer baseRenderer;
            public readonly SkinnedMeshRenderer linkedRenderer;
            public readonly string sourcePath;
            public readonly string basePath;
            public readonly string linkedPath;

            public IReadOnlyList<Mapping> mappings => mappingView;
            public IReadOnlyList<MappingAmbiguity> ambiguities => ambiguityView;
            public bool isExact => ambiguityView.Count == 0;
            public string diagnostic { get; }

            internal Target(
                Component sourceComponent,
                SkinnedMeshRenderer baseRenderer,
                SkinnedMeshRenderer linkedRenderer,
                string sourcePath,
                string basePath,
                string linkedPath,
                IReadOnlyList<Mapping> mappings)
            {
                this.sourceComponent = sourceComponent;
                this.baseRenderer = baseRenderer;
                this.linkedRenderer = linkedRenderer;
                this.sourcePath = sourcePath;
                this.basePath = basePath;
                this.linkedPath = linkedPath;

                var stableMappings = (mappings ?? Array.Empty<Mapping>())
                    .OrderBy(mapping => mapping.baseBlendShape, StringComparer.Ordinal)
                    .ThenBy(mapping => mapping.linkedBlendShape, StringComparer.Ordinal)
                    .ToArray();
                mappingView = Array.AsReadOnly(stableMappings);
                var stableAmbiguities = stableMappings
                    .GroupBy(mapping => mapping.linkedBlendShape, StringComparer.Ordinal)
                    .Where(group => group
                        .Select(mapping => mapping.baseBlendShape)
                        .Distinct(StringComparer.Ordinal)
                        .Count() > 1)
                    .Select(group => new MappingAmbiguity(
                        group.Key,
                        group.Select(mapping => mapping.baseBlendShape)))
                    .OrderBy(ambiguity => ambiguity.linkedBlendShape, StringComparer.Ordinal)
                    .ToArray();
                ambiguityView = Array.AsReadOnly(stableAmbiguities);
                diagnostic = stableAmbiguities.Length == 0
                    ? string.Empty
                    : "Multiple base blendshapes map to the same linked blendshape: " +
                      string.Join("; ", stableAmbiguities.Select(ambiguity =>
                          ambiguity.linkedBlendShape + " <- " +
                          string.Join(", ", ambiguity.baseBlendShapes)));
                linkedByBase = stableMappings
                    .GroupBy(mapping => mapping.baseBlendShape, StringComparer.Ordinal)
                    .ToDictionary(
                        group => group.Key,
                        group => (IReadOnlyList<string>)Array.AsReadOnly(group
                            .Select(mapping => mapping.linkedBlendShape)
                            .Distinct(StringComparer.Ordinal)
                            .OrderBy(name => name, StringComparer.Ordinal)
                            .ToArray()),
                        StringComparer.Ordinal);
            }

            public bool Maps(string baseBlendShape)
            {
                return !string.IsNullOrEmpty(baseBlendShape) && linkedByBase.ContainsKey(baseBlendShape);
            }

            public IReadOnlyList<string> LinkedShapesFor(string baseBlendShape)
            {
                return !string.IsNullOrEmpty(baseBlendShape) &&
                       linkedByBase.TryGetValue(baseBlendShape, out var linked)
                    ? linked
                    : Array.Empty<string>();
            }
        }

        private sealed class FeatureSnapshot
        {
            public Component component;
            public object content;
            public string sourcePath;
            public string hierarchyKey;
            public int componentIndex;
            public string baseObjectName;
            public bool includeAll;
            public bool exactMatch;
            public readonly List<string> excludes = new List<string>();
            public readonly List<IncludeSnapshot> includes = new List<IncludeSnapshot>();
            public readonly List<SkinnedMeshRenderer> linkedRenderers =
                new List<SkinnedMeshRenderer>();
        }

        private readonly struct IncludeSnapshot
        {
            public readonly string nameOnBase;
            public readonly string nameOnLinked;

            public IncludeSnapshot(string nameOnBase, string nameOnLinked)
            {
                this.nameOnBase = nameOnBase;
                this.nameOnLinked = nameOnLinked;
            }
        }

        private sealed class ShapeLookup
        {
            private readonly Func<string, string>[] normalizers;
            private readonly Dictionary<string, string>[] uniqueByNormalizedName;

            public ShapeLookup(IReadOnlyList<string> names, bool exact)
            {
                normalizers = exact
                    ? new Func<string, string>[] { Identity }
                    : new Func<string, string>[] { Identity, NormalizeWhitespaceAndCase };
                uniqueByNormalizedName = normalizers
                    .Select(normalizer => BuildUniqueLookup(names, normalizer))
                    .ToArray();
            }

            public string Lookup(string name)
            {
                if (name == null) return null;
                for (var index = 0; index < normalizers.Length; index++)
                {
                    var normalized = normalizers[index](name);
                    if (uniqueByNormalizedName[index].TryGetValue(normalized, out var resolved))
                        return resolved;
                }
                return null;
            }

            private static Dictionary<string, string> BuildUniqueLookup(
                IEnumerable<string> names,
                Func<string, string> normalizer)
            {
                return names
                    .Select(name => new KeyValuePair<string, string>(normalizer(name), name))
                    .GroupBy(pair => pair.Key, StringComparer.Ordinal)
                    .Where(group => group.Count() == 1)
                    .ToDictionary(
                        group => group.Key,
                        group => group.First().Value,
                        StringComparer.Ordinal);
            }

            private static string Identity(string value) => value;

            // This intentionally matches VRCFury 1.1363: exact lookup is tried
            // first, then a current-culture lowercase lookup with all whitespace
            // removed. Ambiguous normalized names are not eligible for matching.
            private static string NormalizeWhitespaceAndCase(string value)
            {
                return Regex.Replace(value.ToLower(), @"\s", string.Empty);
            }
        }

        private sealed class TargetCandidate
        {
            public Target target;
            public string hierarchyKey;
            public int componentIndex;
            public int linkIndex;
        }

        private readonly IReadOnlyList<Target> targetView;

        public IReadOnlyList<Target> Targets => targetView;
        public string DependencyFingerprint { get; }
        public bool IsReliable { get; }
        public string Error { get; }

        private AdvancedVisemeBlendShapeLinkCatalog(
            IReadOnlyList<Target> targets,
            string dependencyFingerprint,
            bool isReliable = true,
            string error = null)
        {
            targetView = Array.AsReadOnly((targets ?? Array.Empty<Target>()).ToArray());
            DependencyFingerprint = dependencyFingerprint ?? Hash128.Compute(string.Empty).ToString();
            IsReliable = isReliable;
            Error = error;
        }

        public IReadOnlyList<Target> FindTargets(SkinnedMeshRenderer baseRenderer)
        {
            if (baseRenderer == null) return Array.Empty<Target>();
            return Array.AsReadOnly(targetView
                .Where(target => target.baseRenderer == baseRenderer)
                .ToArray());
        }

        /// <summary>
        /// VRCFury resolves fuzzy/include-all mappings from the meshes that exist
        /// when its processor runs. AVR snapshots before mutation, so generated
        /// names must not make a previously absent mapping appear or make an
        /// existing mapping ambiguous. Component and renderer identity are stable
        /// within one avatar build and form the comparison key here.
        /// </summary>
        public bool HasEquivalentResolvedMappings(
            AdvancedVisemeBlendShapeLinkCatalog other,
            out string diagnostic)
        {
            diagnostic = null;
            if (other == null)
            {
                diagnostic = "The post-generation VRCFury link catalog is missing.";
                return false;
            }
            if (!IsReliable || !other.IsReliable)
            {
                diagnostic = other.Error ?? Error ??
                             "The VRCFury BlendShape Link schema could not be verified.";
                return false;
            }

            var before = BuildResolvedMappingSets(targetView);
            var after = BuildResolvedMappingSets(other.targetView);
            var keys = before.Keys.Concat(after.Keys)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            foreach (var key in keys)
            {
                before.TryGetValue(key, out var beforeMappings);
                after.TryGetValue(key, out var afterMappings);
                beforeMappings = beforeMappings ?? new HashSet<string>(StringComparer.Ordinal);
                afterMappings = afterMappings ?? new HashSet<string>(StringComparer.Ordinal);
                if (beforeMappings.SetEquals(afterMappings)) continue;

                var added = afterMappings.Except(beforeMappings, StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray();
                var removed = beforeMappings.Except(afterMappings, StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray();
                var parts = new List<string>();
                if (added.Length > 0) parts.Add("added " + string.Join(", ", added));
                if (removed.Length > 0) parts.Add("removed " + string.Join(", ", removed));
                diagnostic = $"Link '{key}' changed: {string.Join("; ", parts)}.";
                return false;
            }
            return true;
        }

        private static Dictionary<string, HashSet<string>> BuildResolvedMappingSets(
            IEnumerable<Target> targets)
        {
            var output = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var target in targets ?? Array.Empty<Target>())
            {
                var key = (target.sourceComponent != null
                              ? target.sourceComponent.GetInstanceID().ToString()
                              : "null") + "|" +
                          (target.baseRenderer != null
                              ? target.baseRenderer.GetInstanceID().ToString()
                              : "null") + "|" +
                          (target.linkedRenderer != null
                              ? target.linkedRenderer.GetInstanceID().ToString()
                              : "null");
                if (!output.TryGetValue(key, out var mappings))
                {
                    mappings = new HashSet<string>(StringComparer.Ordinal);
                    output[key] = mappings;
                }
                foreach (var mapping in target.mappings)
                    mappings.Add(mapping.baseBlendShape + " -> " + mapping.linkedBlendShape);
            }
            return output;
        }

        public static AdvancedVisemeBlendShapeLinkCatalog Scan(GameObject avatarRoot)
        {
            if (avatarRoot == null)
                return new AdvancedVisemeBlendShapeLinkCatalog(
                    Array.Empty<Target>(), Hash128.Compute("null-avatar").ToString(),
                    false, "The avatar root is missing.");

            var features = ReadFeatures(avatarRoot, out var schemaError);
            if (!string.IsNullOrEmpty(schemaError))
                return new AdvancedVisemeBlendShapeLinkCatalog(
                    Array.Empty<Target>(), Hash128.Compute(schemaError).ToString(),
                    false, schemaError);
            var candidates = new List<TargetCandidate>();
            var fingerprint = new StringBuilder();

            foreach (var feature in features)
            {
                var baseRenderer = ResolveBaseRenderer(avatarRoot, feature.baseObjectName);
                AppendFeatureFingerprint(fingerprint, avatarRoot, feature, baseRenderer);

                if (baseRenderer == null || baseRenderer.sharedMesh == null) continue;
                for (var linkIndex = 0; linkIndex < feature.linkedRenderers.Count; linkIndex++)
                {
                    var linkedRenderer = feature.linkedRenderers[linkIndex];
                    if (linkedRenderer == null || linkedRenderer.sharedMesh == null) continue;
                    var mappings = BuildMappings(feature, baseRenderer.sharedMesh, linkedRenderer.sharedMesh);
                    candidates.Add(new TargetCandidate
                    {
                        hierarchyKey = feature.hierarchyKey,
                        componentIndex = feature.componentIndex,
                        linkIndex = linkIndex,
                        target = new Target(
                            feature.component,
                            baseRenderer,
                            linkedRenderer,
                            feature.sourcePath,
                            RelativePath(avatarRoot.transform, baseRenderer.transform),
                            RelativePath(avatarRoot.transform, linkedRenderer.transform),
                            mappings)
                    });
                }
            }

            var targets = candidates
                .OrderBy(candidate => candidate.hierarchyKey, StringComparer.Ordinal)
                .ThenBy(candidate => candidate.componentIndex)
                .ThenBy(candidate => candidate.linkIndex)
                .ThenBy(candidate => candidate.target.linkedPath, StringComparer.Ordinal)
                .Select(candidate => candidate.target)
                .ToArray();
            AppendResolvedFingerprint(fingerprint, targets);
            return new AdvancedVisemeBlendShapeLinkCatalog(
                targets, Hash128.Compute(fingerprint.ToString()).ToString());
        }

        private static List<FeatureSnapshot> ReadFeatures(
            GameObject avatarRoot,
            out string error)
        {
            error = null;
            var output = new List<FeatureSnapshot>();
            foreach (var transform in avatarRoot.GetComponentsInChildren<Transform>(true))
            {
                var components = transform.GetComponents<Component>();
                for (var componentIndex = 0; componentIndex < components.Length; componentIndex++)
                {
                    var component = components[componentIndex];
                    if (component == null || component.GetType().FullName != VrcFuryComponentType) continue;

                    object content;
                    try
                    {
                        content = ReadRequiredField(component, "content");
                    }
                    catch (Exception exception)
                    {
                        error = $"Could not inspect VRCFury component at " +
                                $"'{RelativePath(avatarRoot.transform, transform)}': " +
                                exception.Message;
                        return output;
                    }
                    if (content == null || content.GetType().FullName != BlendShapeLinkContentType) continue;

                    try
                    {
                        var snapshot = new FeatureSnapshot
                        {
                            component = component,
                            content = content,
                            sourcePath = RelativePath(avatarRoot.transform, transform),
                            hierarchyKey = HierarchyKey(avatarRoot.transform, transform),
                            componentIndex = componentIndex,
                            baseObjectName = ReadRequiredString(content, "baseObj"),
                            includeAll = ReadRequiredBool(content, "includeAll"),
                            exactMatch = ReadRequiredBool(content, "exactMatch")
                        };

                        foreach (var exclude in ReadRequiredEnumerable(content, "excludes"))
                            snapshot.excludes.Add(ReadRequiredString(exclude, "name"));
                        foreach (var include in ReadRequiredEnumerable(content, "includes"))
                            snapshot.includes.Add(new IncludeSnapshot(
                                ReadRequiredString(include, "nameOnBase"),
                                ReadRequiredString(include, "nameOnLinked")));
                        foreach (var linkSkin in ReadRequiredEnumerable(content, "linkSkins"))
                        {
                            if (ReadRequiredField(linkSkin, "renderer") is SkinnedMeshRenderer renderer)
                                snapshot.linkedRenderers.Add(renderer);
                        }
                        output.Add(snapshot);
                    }
                    catch (Exception exception)
                    {
                        error = $"VRCFury BlendShape Link at " +
                                $"'{RelativePath(avatarRoot.transform, transform)}' uses an " +
                                $"unsupported schema: {exception.Message}";
                        return output;
                    }
                }
            }
            return output;
        }

        private static SkinnedMeshRenderer ResolveBaseRenderer(GameObject avatarRoot, string objectName)
        {
            return avatarRoot.GetComponentsInChildren<Transform>(true)
                .Where(transform => transform.name == objectName)
                .Select(transform => new
                {
                    renderer = transform.GetComponent<SkinnedMeshRenderer>(),
                    path = RelativePath(avatarRoot.transform, transform)
                })
                .Where(candidate => candidate.renderer != null)
                .OrderBy(candidate => candidate.path.Length)
                .Select(candidate => candidate.renderer)
                .FirstOrDefault();
        }

        private static Mapping[] BuildMappings(
            FeatureSnapshot feature,
            Mesh baseMesh,
            Mesh linkedMesh)
        {
            var baseNames = BlendShapeNames(baseMesh);
            var linkedNames = BlendShapeNames(linkedMesh);
            var baseLookup = new ShapeLookup(baseNames, feature.exactMatch);
            var linkedLookup = new ShapeLookup(linkedNames, feature.exactMatch);
            var output = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

            void Attempt(string from, string to, bool allowDuplicates)
            {
                from = baseLookup.Lookup(from);
                if (from == null) return;
                if (output.ContainsKey(from) && !allowDuplicates) return;
                to = linkedLookup.Lookup(to);
                if (to == null) return;
                if (!output.TryGetValue(from, out var linked))
                {
                    linked = new HashSet<string>(StringComparer.Ordinal);
                    output[from] = linked;
                }
                linked.Add(to);
            }

            foreach (var include in feature.includes)
            {
                if (string.IsNullOrWhiteSpace(include.nameOnBase))
                {
                    if (string.IsNullOrWhiteSpace(include.nameOnLinked)) continue;
                    Attempt(include.nameOnLinked, include.nameOnLinked, true);
                }
                else if (string.IsNullOrWhiteSpace(include.nameOnLinked))
                {
                    Attempt(include.nameOnBase, include.nameOnBase, true);
                }
                else
                {
                    Attempt(include.nameOnBase, include.nameOnLinked, true);
                }
            }

            if (feature.includeAll)
            {
                var excluded = new HashSet<string>(feature.excludes, StringComparer.Ordinal);
                foreach (var name in baseNames)
                {
                    if (excluded.Contains(name)) continue;
                    Attempt(name, name, false);
                }
            }

            return output
                .SelectMany(pair => pair.Value.Select(linked => new Mapping(pair.Key, linked)))
                .OrderBy(mapping => mapping.baseBlendShape, StringComparer.Ordinal)
                .ThenBy(mapping => mapping.linkedBlendShape, StringComparer.Ordinal)
                .ToArray();
        }

        private static string[] BlendShapeNames(Mesh mesh)
        {
            if (mesh == null) return Array.Empty<string>();
            var names = new string[mesh.blendShapeCount];
            for (var index = 0; index < names.Length; index++)
                names[index] = mesh.GetBlendShapeName(index);
            return names;
        }

        private static object ReadRequiredField(object instance, string name)
        {
            if (instance == null)
                throw new InvalidOperationException($"Cannot read required field '{name}' from null.");
            var field = instance.GetType().GetField(
                name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null)
                throw new MissingFieldException(instance.GetType().FullName, name);
            return field.GetValue(instance);
        }

        private static string ReadRequiredString(object instance, string name)
        {
            var value = ReadRequiredField(instance, name);
            if (value is string text) return text;
            throw new InvalidOperationException(
                $"Required field '{instance.GetType().FullName}.{name}' is not a string.");
        }

        private static bool ReadRequiredBool(object instance, string name)
        {
            var value = ReadRequiredField(instance, name);
            if (value is bool boolean) return boolean;
            throw new InvalidOperationException(
                $"Required field '{instance.GetType().FullName}.{name}' is not a bool.");
        }

        private static IEnumerable<object> ReadRequiredEnumerable(
            object instance,
            string name)
        {
            var value = ReadRequiredField(instance, name);
            if (!(value is IEnumerable enumerable))
                throw new InvalidOperationException(
                    $"Required field '{instance.GetType().FullName}.{name}' is not a collection.");
            foreach (var item in enumerable)
                if (item != null) yield return item;
        }

        private static string RelativePath(Transform root, Transform target)
        {
            if (target == root) return string.Empty;
            if (target != null && target.IsChildOf(root))
                return AnimationUtility.CalculateTransformPath(target, root);
            return target == null ? string.Empty : "<external>/" + target.name;
        }

        private static string HierarchyKey(Transform root, Transform target)
        {
            if (target == null) return string.Empty;
            var indices = new Stack<int>();
            var cursor = target;
            while (cursor != null && cursor != root)
            {
                indices.Push(cursor.GetSiblingIndex());
                cursor = cursor.parent;
            }
            return string.Join("/", indices.Select(index => index.ToString("D8")));
        }

        private static void AppendFeatureFingerprint(
            StringBuilder output,
            GameObject avatarRoot,
            FeatureSnapshot feature,
            SkinnedMeshRenderer baseRenderer)
        {
            AppendToken(output, "feature");
            AppendToken(output, feature.hierarchyKey);
            AppendToken(output, feature.componentIndex.ToString());
            AppendToken(output, feature.sourcePath);
            AppendToken(output, feature.baseObjectName);
            AppendToken(output, feature.includeAll ? "1" : "0");
            AppendToken(output, feature.exactMatch ? "1" : "0");
            foreach (var exclude in feature.excludes)
            {
                AppendToken(output, "exclude");
                AppendToken(output, exclude);
            }
            foreach (var include in feature.includes)
            {
                AppendToken(output, "include");
                AppendToken(output, include.nameOnBase);
                AppendToken(output, include.nameOnLinked);
            }
            AppendRendererFingerprint(output, avatarRoot, baseRenderer);
            foreach (var linked in feature.linkedRenderers)
                AppendRendererFingerprint(output, avatarRoot, linked);
        }

        private static void AppendResolvedFingerprint(StringBuilder output, IEnumerable<Target> targets)
        {
            foreach (var target in targets)
            {
                AppendToken(output, "target");
                AppendToken(output, target.sourcePath);
                AppendToken(output, target.basePath);
                AppendToken(output, target.linkedPath);
                foreach (var mapping in target.mappings)
                {
                    AppendToken(output, mapping.baseBlendShape);
                    AppendToken(output, mapping.linkedBlendShape);
                }
            }
        }

        private static void AppendRendererFingerprint(
            StringBuilder output,
            GameObject avatarRoot,
            SkinnedMeshRenderer renderer)
        {
            if (renderer == null)
            {
                AppendToken(output, "null-renderer");
                return;
            }
            AppendToken(output, RelativePath(avatarRoot.transform, renderer.transform));
            var mesh = renderer.sharedMesh;
            if (mesh == null)
            {
                AppendToken(output, "null-mesh");
                return;
            }

            var assetPath = AssetDatabase.GetAssetPath(mesh);
            AppendToken(output, assetPath);
            if (!string.IsNullOrEmpty(assetPath))
                AppendToken(output, AssetDatabase.GetAssetDependencyHash(assetPath).ToString());
            AppendToken(output, mesh.name);
            AppendToken(output, mesh.vertexCount.ToString());
            foreach (var name in BlendShapeNames(mesh)) AppendToken(output, name);
        }

        private static void AppendToken(StringBuilder output, string value)
        {
            value = value ?? string.Empty;
            output.Append(value.Length).Append(':').Append(value).Append(';');
        }
    }
}
