using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using YUCP.Components;

namespace YUCP.Components.Editor.MeshUtils
{
    /// <summary>
    /// Builds one replacement mesh for a base renderer and one or more blendshape attachments.
    /// All calculations are performed in the unskinned base mesh local space.
    /// </summary>
    public static class AttachToBlendshapeMergedMeshBuilder
    {
        public sealed class AttachmentInput
        {
            public AttachToBlendshapeData data;
            public Mesh mesh;
            public Transform transform;
            public Material[] materials;
            public SkinnedMeshRenderer skinnedRenderer;
            public MeshRenderer meshRenderer;
            public SurfaceCluster cluster;
            public HashSet<string> trackedBlendshapes;
            public string displayName;
            public string animationPath;
        }

        public sealed class AttachmentResult
        {
            public AttachmentInput input;
            public int vertexStart;
            public int vertexCount;
            public int materialStart;
            public int materialCount;
            public int uvChannel = -1;
            public string visibilityBlendshapeName;
            public Dictionary<string, string> attachmentBlendshapeNameMap = new Dictionary<string, string>();
            public List<int> uvDiscardMaterialIndices = new List<int>();
            public bool defaultHidden;
        }

        public sealed class BuildResult
        {
            public bool success;
            public Mesh mesh;
            public Material[] materials;
            public List<AttachmentResult> attachments = new List<AttachmentResult>();
            public List<string> warnings = new List<string>();
            public string error;
        }

        private sealed class PreparedAttachment
        {
            public AttachmentInput input;
            public Vector3[] vertices;
            public Vector3[] normals;
            public Vector4[] tangents;
            public Vector3[] collapseVertices;
            public List<BoneWeight1>[] influences;
            public Matrix4x4[] skinMatrices;
            public Matrix4x4[] sourceSkinMatrices;
            public Matrix4x4 sourceToBaseCurrent;
            public Color[] colors;
            public List<Vector4>[] uvs;
            public int[] triangles;
            public ClosestSurfaceMapper.SurfaceMap[] maps;
            public AttachmentResult result;
        }

        public static BuildResult Build(SkinnedMeshRenderer baseRenderer, Mesh baseMesh, IList<AttachmentInput> inputs)
        {
            var output = new BuildResult();
            if (baseRenderer == null || baseMesh == null)
            {
                output.error = "Base renderer or mesh is null.";
                return output;
            }
            if (inputs == null || inputs.Count == 0)
            {
                output.error = "No attachments were supplied.";
                return output;
            }
            if (!baseMesh.isReadable)
            {
                output.error = $"Base mesh '{baseMesh.name}' is not readable. Enable Read/Write on its importer.";
                return output;
            }
            if (!HasTriangleTopology(baseMesh))
            {
                output.error = $"Base mesh '{baseMesh.name}' contains non-triangle topology.";
                return output;
            }

            int[] baseTriangles = SkinnedMeshMerge.GetCombinedTriangles(baseMesh);
            Vector3[] baseVertices = baseMesh.vertices;
            var baseInfluences = ReadBoneInfluences(baseMesh);
            if (baseInfluences == null || baseInfluences.Length != baseVertices.Length || baseMesh.bindposes == null || baseMesh.bindposes.Length == 0)
            {
                output.error = $"Base mesh '{baseMesh.name}' has invalid legacy bone weights or bind poses.";
                return output;
            }

            Matrix4x4[] baseBoneMatrices;
            Mesh currentBaseSurface;
            try
            {
                baseBoneMatrices = BuildBoneMatrices(baseRenderer, baseMesh.bindposes);
                currentBaseSurface = BuildCurrentPoseSurface(baseMesh, baseInfluences, baseBoneMatrices);
            }
            catch (Exception ex)
            {
                output.error = $"Base mesh '{baseMesh.name}' cannot be evaluated in its current skinning pose: {ex.Message}";
                return output;
            }

            var prepared = new List<PreparedAttachment>(inputs.Count);
            int totalVertices = baseMesh.vertexCount;
            int totalSubmeshes = baseMesh.subMeshCount;

            try
            {
                foreach (var input in inputs)
                {
                    if (input == null || input.data == null || input.mesh == null || input.transform == null)
                    {
                        output.error = "An attachment target could not be resolved.";
                        return output;
                    }
                    if (!input.mesh.isReadable)
                    {
                        output.error = $"Attachment mesh '{input.mesh.name}' is not readable. Enable Read/Write on its importer.";
                        return output;
                    }
                    if (!HasTriangleTopology(input.mesh))
                    {
                        output.error = $"Attachment mesh '{input.mesh.name}' contains non-triangle topology.";
                        return output;
                    }

                    var p = PrepareAttachment(baseRenderer, currentBaseSurface, baseTriangles, baseInfluences,
                        baseBoneMatrices, input, totalVertices, totalSubmeshes, output);
                    if (p == null)
                    {
                        return output;
                    }
                    prepared.Add(p);
                    output.attachments.Add(p.result);
                    totalVertices += input.mesh.vertexCount;
                    totalSubmeshes += input.mesh.subMeshCount;
                }
            }
            catch (Exception ex)
            {
                output.error = $"Attachment preflight failed: {ex.Message}";
                return output;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(currentBaseSurface);
            }

            try
            {
                Mesh merged = BuildGeometry(baseRenderer, baseMesh, prepared, totalVertices, totalSubmeshes, baseTriangles, baseInfluences);
                BuildBlendshapes(baseRenderer, baseMesh, merged, baseTriangles, prepared, output);
                CalibrateNeutralGeometryToCurrentWeights(baseRenderer, baseMesh, merged, prepared);
                ConfigureVisibility(merged, prepared, output);
                merged.RecalculateBounds();

                output.mesh = merged;
                output.materials = BuildMaterials(baseRenderer.sharedMaterials, baseMesh.subMeshCount, prepared, output);
                if (!string.IsNullOrEmpty(output.error))
                {
                    UnityEngine.Object.DestroyImmediate(merged);
                    output.mesh = null;
                    return output;
                }
                output.success = true;
                return output;
            }
            catch (Exception ex)
            {
                output.error = $"Merged mesh generation failed: {ex.Message}";
                if (output.mesh != null) UnityEngine.Object.DestroyImmediate(output.mesh);
                output.mesh = null;
                return output;
            }
        }

        private static PreparedAttachment PrepareAttachment(
            SkinnedMeshRenderer baseRenderer,
            Mesh currentBaseSurface,
            int[] baseTriangles,
            List<BoneWeight1>[] baseInfluences,
            Matrix4x4[] baseBoneMatrices,
            AttachmentInput input,
            int vertexStart,
            int materialStart,
            BuildResult output)
        {
            Vector3[] sourceVertices = input.mesh.vertices;
            Vector3[] sourceNormals = input.mesh.normals;
            Vector4[] sourceTangents = input.mesh.tangents;
            Matrix4x4[] sourceSkinMatrices = BuildSourceSkinMatrices(input, sourceVertices.Length);
            Matrix4x4 sourceToBaseCurrent = baseRenderer.transform.worldToLocalMatrix * input.transform.localToWorldMatrix;
            var currentVertices = new Vector3[sourceVertices.Length];
            var currentNormals = new Vector3[sourceVertices.Length];
            var currentTangents = new Vector4[sourceVertices.Length];
            for (int i = 0; i < currentVertices.Length; i++)
            {
                Matrix4x4 sourceToBase = sourceToBaseCurrent * sourceSkinMatrices[i];
                currentVertices[i] = sourceToBase.MultiplyPoint3x4(sourceVertices[i]);
                Vector3 sourceNormal = sourceNormals != null && sourceNormals.Length == sourceVertices.Length
                    ? sourceNormals[i]
                    : Vector3.up;
                currentNormals[i] = TransformNormal(sourceToBase, sourceNormal);
                Vector4 sourceTangent = sourceTangents != null && sourceTangents.Length == sourceVertices.Length
                    ? sourceTangents[i]
                    : new Vector4(1, 0, 0, 1);
                Vector3 tangent = sourceToBase.MultiplyVector(new Vector3(sourceTangent.x, sourceTangent.y, sourceTangent.z)).normalized;
                currentTangents[i] = new Vector4(tangent.x, tangent.y, tangent.z, sourceTangent.w);
            }

            var maps = ClosestSurfaceMapper.MapAttachmentVerticesToBaseSurface(
                baseRenderer, currentBaseSurface, currentVertices, input.data, out int matchedCount);
            int[] attachmentTriangles = SkinnedMeshMerge.GetCombinedTriangles(input.mesh);
            if (matchedCount != currentVertices.Length)
            {
                if (input.data.unmatchedHandling == AttachToBlendshapeUnmatchedHandling.Skip ||
                    !PropagateSurfaceMaps(maps, attachmentTriangles))
                {
                    output.error = $"Attachment '{input.displayName}' mapped {matchedCount}/{currentVertices.Length} vertices to the base surface.";
                    return null;
                }
                output.warnings.Add($"Attachment '{input.displayName}' propagated surface mapping to {currentVertices.Length - matchedCount} unmatched vertices.");
            }

            var vertices = new Vector3[currentVertices.Length];
            var normals = new Vector3[currentVertices.Length];
            var tangents = new Vector4[currentVertices.Length];
            var collapseVertices = new Vector3[currentVertices.Length];
            var influences = new List<BoneWeight1>[currentVertices.Length];
            var skinMatrices = new Matrix4x4[currentVertices.Length];
            Vector3 pivotCurrent = baseRenderer.transform.worldToLocalMatrix.MultiplyPoint3x4(input.transform.position);
            for (int i = 0; i < currentVertices.Length; i++)
            {
                influences[i] = InterpolateBoneWeights(maps[i], baseTriangles, baseInfluences);
                Matrix4x4 skin = BlendMatrices(influences[i], baseBoneMatrices);
                EnsureInvertible(skin, $"attachment vertex {i}");
                Matrix4x4 inverseSkin = skin.inverse;
                skinMatrices[i] = skin;
                vertices[i] = inverseSkin.MultiplyPoint3x4(currentVertices[i]);
                normals[i] = skin.transpose.MultiplyVector(currentNormals[i]).normalized;
                Vector3 currentTangent = new Vector3(currentTangents[i].x, currentTangents[i].y, currentTangents[i].z);
                Vector3 bindTangent = inverseSkin.MultiplyVector(currentTangent).normalized;
                tangents[i] = new Vector4(bindTangent.x, bindTangent.y, bindTangent.z, currentTangents[i].w);
                collapseVertices[i] = inverseSkin.MultiplyPoint3x4(pivotCurrent);
            }

            var colors = input.mesh.colors;
            var uvs = ReadAllUvs(input.mesh, vertices.Length);

            bool defaultHidden = !(input.skinnedRenderer != null
                ? input.skinnedRenderer.enabled && input.skinnedRenderer.gameObject.activeInHierarchy
                : input.meshRenderer == null || (input.meshRenderer.enabled && input.meshRenderer.gameObject.activeInHierarchy));

            return new PreparedAttachment
            {
                input = input,
                vertices = vertices,
                normals = normals,
                tangents = tangents,
                collapseVertices = collapseVertices,
                influences = influences,
                skinMatrices = skinMatrices,
                sourceSkinMatrices = sourceSkinMatrices,
                sourceToBaseCurrent = sourceToBaseCurrent,
                colors = colors != null && colors.Length == vertices.Length ? colors : null,
                uvs = uvs,
                triangles = attachmentTriangles,
                maps = maps,
                result = new AttachmentResult
                {
                    input = input,
                    vertexStart = vertexStart,
                    vertexCount = vertices.Length,
                    materialStart = materialStart,
                    materialCount = input.mesh.subMeshCount,
                    defaultHidden = defaultHidden
                }
            };
        }

        private static Mesh BuildGeometry(
            SkinnedMeshRenderer baseRenderer,
            Mesh baseMesh,
            List<PreparedAttachment> attachments,
            int totalVertices,
            int totalSubmeshes,
            int[] baseTriangles,
            List<BoneWeight1>[] baseInfluences)
        {
            var merged = new Mesh
            {
                name = baseMesh.name + "_YUCP_Attached",
                indexFormat = baseMesh.indexFormat == IndexFormat.UInt32 || totalVertices > ushort.MaxValue
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16
            };

            var vertices = new Vector3[totalVertices];
            var normals = new Vector3[totalVertices];
            var tangents = new Vector4[totalVertices];
            var colors = new Color[totalVertices];
            var influences = new List<BoneWeight1>[totalVertices];
            var uvs = new List<Vector4>[8];
            for (int channel = 0; channel < 8; channel++) uvs[channel] = new List<Vector4>(totalVertices);

            CopyBaseGeometry(baseMesh, vertices, normals, tangents, colors, uvs);
            for (int i = 0; i < baseMesh.vertexCount; i++) influences[i] = new List<BoneWeight1>(baseInfluences[i]);

            foreach (var p in attachments)
            {
                int start = p.result.vertexStart;
                Array.Copy(p.vertices, 0, vertices, start, p.vertices.Length);
                Array.Copy(p.normals, 0, normals, start, p.normals.Length);
                Array.Copy(p.tangents, 0, tangents, start, p.tangents.Length);
                for (int i = 0; i < p.vertices.Length; i++)
                {
                    colors[start + i] = p.colors != null ? p.colors[i] : Color.white;
                    influences[start + i] = new List<BoneWeight1>(p.influences[i]);
                }
                for (int channel = 0; channel < 8; channel++)
                {
                    uvs[channel].AddRange(p.uvs[channel]);
                }
            }

            merged.vertices = vertices;
            merged.normals = normals;
            merged.tangents = tangents;
            merged.colors = colors;
            for (int channel = 0; channel < 8; channel++) merged.SetUVs(channel, uvs[channel]);
            merged.bindposes = baseMesh.bindposes;
            WriteBoneWeights(merged, influences);
            merged.subMeshCount = totalSubmeshes;

            for (int s = 0; s < baseMesh.subMeshCount; s++) merged.SetTriangles(baseMesh.GetTriangles(s), s, false);
            foreach (var p in attachments)
            {
                for (int s = 0; s < p.input.mesh.subMeshCount; s++)
                {
                    int[] tris = p.input.mesh.GetTriangles(s);
                    for (int i = 0; i < tris.Length; i++) tris[i] += p.result.vertexStart;
                    merged.SetTriangles(tris, p.result.materialStart + s, false);
                }
            }
            return merged;
        }

        private static void CopyBaseGeometry(
            Mesh baseMesh,
            Vector3[] vertices,
            Vector3[] normals,
            Vector4[] tangents,
            Color[] colors,
            List<Vector4>[] uvs)
        {
            int count = baseMesh.vertexCount;
            Array.Copy(baseMesh.vertices, vertices, count);
            var baseNormals = baseMesh.normals;
            var baseTangents = baseMesh.tangents;
            var baseColors = baseMesh.colors;
            for (int i = 0; i < count; i++)
            {
                normals[i] = baseNormals != null && baseNormals.Length == count ? baseNormals[i] : Vector3.up;
                tangents[i] = baseTangents != null && baseTangents.Length == count ? baseTangents[i] : new Vector4(1, 0, 0, 1);
                colors[i] = baseColors != null && baseColors.Length == count ? baseColors[i] : Color.white;
            }
            for (int channel = 0; channel < 8; channel++)
            {
                var source = new List<Vector4>();
                baseMesh.GetUVs(channel, source);
                if (source.Count != count) source = Enumerable.Repeat(Vector4.zero, count).ToList();
                uvs[channel].AddRange(source);
            }
        }

        private static void BuildBlendshapes(
            SkinnedMeshRenderer baseRenderer,
            Mesh baseMesh,
            Mesh merged,
            int[] baseTriangles,
            List<PreparedAttachment> attachments,
            BuildResult output)
        {
            int total = merged.vertexCount;
            Vector3[] neutralBase = baseMesh.vertices;
            Vector3[] authoringBaseline = BuildAuthoringBlendshapeBaseline(baseRenderer, baseMesh, neutralBase);

            for (int bi = 0; bi < baseMesh.blendShapeCount; bi++)
            {
                string name = baseMesh.GetBlendShapeName(bi);
                int frameCount = baseMesh.GetBlendShapeFrameCount(bi);
                bool trackedByAttachment = attachments.Any(p =>
                    p.input.trackedBlendshapes != null && p.input.trackedBlendshapes.Contains(name));
                Vector3[] shapeBaseline = neutralBase;
                if (trackedByAttachment)
                {
                    shapeBaseline = (Vector3[])authoringBaseline.Clone();
                    SubtractCurrentShapeContribution(baseRenderer, baseMesh, bi, shapeBaseline);
                }
                for (int fi = 0; fi < frameCount; fi++)
                {
                    var baseDv = new Vector3[baseMesh.vertexCount];
                    var baseDn = new Vector3[baseMesh.vertexCount];
                    var baseDt = new Vector3[baseMesh.vertexCount];
                    baseMesh.GetBlendShapeFrameVertices(bi, fi, baseDv, baseDn, baseDt);

                    var fullDv = new Vector3[total];
                    var fullDn = new Vector3[total];
                    var fullDt = new Vector3[total];
                    Array.Copy(baseDv, fullDv, baseDv.Length);
                    Array.Copy(baseDn, fullDn, baseDn.Length);
                    Array.Copy(baseDt, fullDt, baseDt.Length);

                    foreach (var p in attachments)
                    {
                        if (p.input.trackedBlendshapes == null || !p.input.trackedBlendshapes.Contains(name)) continue;
                        CalculateAttachmentFrame(p, shapeBaseline, baseDv, baseTriangles, out var dv, out var dn, out var dt, output);
                        Array.Copy(dv, 0, fullDv, p.result.vertexStart, dv.Length);
                        Array.Copy(dn, 0, fullDn, p.result.vertexStart, dn.Length);
                        Array.Copy(dt, 0, fullDt, p.result.vertexStart, dt.Length);
                    }

                    float weight = baseMesh.GetBlendShapeFrameWeight(bi, fi);
                    merged.AddBlendShapeFrame(name, weight, fullDv, fullDn, fullDt);
                }
            }

            var usedNames = new HashSet<string>(Enumerable.Range(0, merged.blendShapeCount).Select(merged.GetBlendShapeName), StringComparer.Ordinal);
            foreach (var p in attachments)
            {
                CopyAttachmentBlendshapes(p, merged, usedNames);
            }
        }

        private static Vector3[] BuildAuthoringBlendshapeBaseline(
            SkinnedMeshRenderer renderer,
            Mesh mesh,
            Vector3[] neutral)
        {
            var baseline = (Vector3[])neutral.Clone();
            if (renderer == null) return baseline;
            var frameDv = new Vector3[mesh.vertexCount];
            var frameDn = new Vector3[mesh.vertexCount];
            var frameDt = new Vector3[mesh.vertexCount];
            for (int shape = 0; shape < mesh.blendShapeCount; shape++)
            {
                float weight = renderer.GetBlendShapeWeight(shape);
                if (Mathf.Abs(weight) <= 1e-6f) continue;
                foreach (FrameCoefficient coefficient in GetBlendshapeFrameCoefficients(mesh, shape, weight))
                {
                    mesh.GetBlendShapeFrameVertices(shape, coefficient.frameIndex, frameDv, frameDn, frameDt);
                    for (int i = 0; i < baseline.Length; i++) baseline[i] += frameDv[i] * coefficient.coefficient;
                }
            }
            return baseline;
        }

        private static void SubtractCurrentShapeContribution(
            SkinnedMeshRenderer renderer,
            Mesh mesh,
            int shape,
            Vector3[] baseline)
        {
            float weight = renderer != null ? renderer.GetBlendShapeWeight(shape) : 0f;
            if (Mathf.Abs(weight) <= 1e-6f) return;
            var frameDv = new Vector3[mesh.vertexCount];
            var frameDn = new Vector3[mesh.vertexCount];
            var frameDt = new Vector3[mesh.vertexCount];
            foreach (FrameCoefficient coefficient in GetBlendshapeFrameCoefficients(mesh, shape, weight))
            {
                mesh.GetBlendShapeFrameVertices(shape, coefficient.frameIndex, frameDv, frameDn, frameDt);
                for (int i = 0; i < baseline.Length; i++) baseline[i] -= frameDv[i] * coefficient.coefficient;
            }
        }

        private static void CalculateAttachmentFrame(
            PreparedAttachment p,
            Vector3[] baseVertices,
            Vector3[] baseDelta,
            int[] baseTriangles,
            out Vector3[] deltaVertices,
            out Vector3[] deltaNormals,
            out Vector3[] deltaTangents,
            BuildResult output)
        {
            int count = p.vertices.Length;
            deltaVertices = new Vector3[count];
            deltaNormals = new Vector3[count];
            deltaTangents = new Vector3[count];

            if (p.input.data.bakeMethod == AttachToBlendshapeBakeMethod.RigidPivotTransform)
            {
                var deformed = new Vector3[baseVertices.Length];
                for (int i = 0; i < deformed.Length; i++) deformed[i] = baseVertices[i] + baseDelta[i];
                SurfaceClusterDetector.EvaluateCluster(p.input.cluster, baseVertices, baseTriangles, out var p0, out var n0, out var t0);
                SurfaceClusterDetector.EvaluateCluster(p.input.cluster, deformed, baseTriangles, out var p1, out var n1, out var t1);
                Quaternion rotation = p.input.data.alignRotationToSurface ? FrameRotation(n1, t1) * Quaternion.Inverse(FrameRotation(n0, t0)) : Quaternion.identity;
                float scale = 1f;
                if (p.input.data.solverMode == SolverMode.Affine && t0.sqrMagnitude > 1e-8f)
                {
                    scale = Mathf.Clamp(t1.magnitude / t0.magnitude, 0.8f, 1.2f);
                }
                for (int i = 0; i < count; i++)
                {
                    Vector3 target = p1 + rotation * ((p.vertices[i] - p0) * scale);
                    deltaVertices[i] = target - p.vertices[i];
                    deltaNormals[i] = rotation * p.normals[i] - p.normals[i];
                    Vector3 tangent = new Vector3(p.tangents[i].x, p.tangents[i].y, p.tangents[i].z);
                    deltaTangents[i] = rotation * tangent - tangent;
                }
                return;
            }

            for (int i = 0; i < count; i++)
            {
                var map = p.maps[i];
                int t = map.triIndex * 3;
                int i0 = baseTriangles[t];
                int i1 = baseTriangles[t + 1];
                int i2 = baseTriangles[t + 2];
                Vector3 b = map.barycentric;
                Vector3 a0 = baseVertices[i0];
                Vector3 a1 = baseVertices[i1];
                Vector3 a2 = baseVertices[i2];
                Vector3 d0 = a0 + baseDelta[i0];
                Vector3 d1 = a1 + baseDelta[i1];
                Vector3 d2 = a2 + baseDelta[i2];
                Vector3 surface0 = a0 * b.x + a1 * b.y + a2 * b.z;
                Vector3 surface1 = d0 * b.x + d1 * b.y + d2 * b.z;
                Quaternion rotation = TriangleFrame(d0, d1, d2) * Quaternion.Inverse(TriangleFrame(a0, a1, a2));
                Vector3 target = surface1 + rotation * (p.vertices[i] - surface0);
                deltaVertices[i] = target - p.vertices[i];
                deltaNormals[i] = rotation * p.normals[i] - p.normals[i];
                Vector3 tangent = new Vector3(p.tangents[i].x, p.tangents[i].y, p.tangents[i].z);
                deltaTangents[i] = rotation * tangent - tangent;
            }
        }

        private static void CopyAttachmentBlendshapes(PreparedAttachment p, Mesh merged, HashSet<string> usedNames)
        {
            Mesh source = p.input.mesh;
            Vector3[] sourceVertices = source.vertices;
            Vector3[] sourceNormals = source.normals;
            Vector4[] sourceTangents = source.tangents;
            for (int bi = 0; bi < source.blendShapeCount; bi++)
            {
                string sourceName = source.GetBlendShapeName(bi);
                string targetName = sourceName;
                if (usedNames.Contains(targetName))
                {
                    targetName = $"Merged_{Sanitize(p.input.displayName)}__{Sanitize(sourceName)}";
                    int suffix = 1;
                    string root = targetName;
                    while (usedNames.Contains(targetName)) targetName = root + "_" + suffix++;
                }
                p.result.attachmentBlendshapeNameMap[sourceName] = targetName;
                usedNames.Add(targetName);

                for (int fi = 0; fi < source.GetBlendShapeFrameCount(bi); fi++)
                {
                    var dv = new Vector3[source.vertexCount];
                    var dn = new Vector3[source.vertexCount];
                    var dt = new Vector3[source.vertexCount];
                    source.GetBlendShapeFrameVertices(bi, fi, dv, dn, dt);
                    var fullDv = new Vector3[merged.vertexCount];
                    var fullDn = new Vector3[merged.vertexCount];
                    var fullDt = new Vector3[merged.vertexCount];
                    for (int i = 0; i < source.vertexCount; i++)
                    {
                        Matrix4x4 sourceToBase = p.sourceToBaseCurrent * p.sourceSkinMatrices[i];
                        Matrix4x4 baseSkin = p.skinMatrices[i];
                        Matrix4x4 inverseBaseSkin = baseSkin.inverse;

                        Vector3 shapedCurrent = sourceToBase.MultiplyPoint3x4(sourceVertices[i] + dv[i]);
                        Vector3 shapedBind = inverseBaseSkin.MultiplyPoint3x4(shapedCurrent);
                        fullDv[p.result.vertexStart + i] = shapedBind - p.vertices[i];

                        Vector3 sourceNormal = sourceNormals != null && sourceNormals.Length == source.vertexCount
                            ? sourceNormals[i]
                            : Vector3.up;
                        Vector3 shapedCurrentNormal = TransformNormal(sourceToBase, sourceNormal + dn[i]);
                        Vector3 shapedBindNormal = baseSkin.transpose.MultiplyVector(shapedCurrentNormal).normalized;
                        fullDn[p.result.vertexStart + i] = shapedBindNormal - p.normals[i];

                        Vector4 sourceTangent = sourceTangents != null && sourceTangents.Length == source.vertexCount
                            ? sourceTangents[i]
                            : new Vector4(1, 0, 0, 1);
                        Vector3 shapedSourceTangent = new Vector3(sourceTangent.x, sourceTangent.y, sourceTangent.z) + dt[i];
                        Vector3 shapedCurrentTangent = sourceToBase.MultiplyVector(shapedSourceTangent).normalized;
                        Vector3 shapedBindTangent = inverseBaseSkin.MultiplyVector(shapedCurrentTangent).normalized;
                        Vector3 neutralBindTangent = new Vector3(p.tangents[i].x, p.tangents[i].y, p.tangents[i].z);
                        fullDt[p.result.vertexStart + i] = shapedBindTangent - neutralBindTangent;
                    }
                    merged.AddBlendShapeFrame(targetName, source.GetBlendShapeFrameWeight(bi, fi), fullDv, fullDn, fullDt);
                }
            }
        }

        private static void ConfigureVisibility(Mesh merged, List<PreparedAttachment> attachments, BuildResult output)
        {
            foreach (var p in attachments)
            {
                if (p.input.data.visibilityMode == AttachToBlendshapeVisibilityMode.SizeBlendshape)
                {
                    string name = UniqueVisibilityName(merged, p.input.animationPath, p.input.displayName);
                    var delta = new Vector3[merged.vertexCount];
                    for (int i = 0; i < p.result.vertexCount; i++)
                    {
                        int index = p.result.vertexStart + i;
                        delta[index] = p.collapseVertices[i] - merged.vertices[index];
                    }
                    merged.AddBlendShapeFrame(name, 100f, delta, new Vector3[merged.vertexCount], new Vector3[merged.vertexCount]);
                    p.result.visibilityBlendshapeName = name;
                }
                else
                {
                    int channel = p.input.data.autoDetectUVChannel
                        ? Mathf.Clamp(UVManipulator.DetectBestUVChannel(merged), 0, 7)
                        : Mathf.Clamp(p.input.data.uvChannel, 0, 7);
                    p.result.uvChannel = channel;
                    ApplyUvTile(merged, p.result.vertexStart, p.result.vertexCount, channel, p.input.data.uvDiscardRow, p.input.data.uvDiscardColumn);
                }
            }
        }

        private static void CalibrateNeutralGeometryToCurrentWeights(
            SkinnedMeshRenderer baseRenderer,
            Mesh baseMesh,
            Mesh merged,
            List<PreparedAttachment> attachments)
        {
            if (baseRenderer == null || baseMesh == null || merged == null || attachments == null) return;

            var accumulatedPositions = attachments.ToDictionary(p => p, p => new Vector3[p.result.vertexCount]);
            var accumulatedNormals = attachments.ToDictionary(p => p, p => new Vector3[p.result.vertexCount]);
            var accumulatedTangents = attachments.ToDictionary(p => p, p => new Vector3[p.result.vertexCount]);
            var frameDv = new Vector3[merged.vertexCount];
            var frameDn = new Vector3[merged.vertexCount];
            var frameDt = new Vector3[merged.vertexCount];
            var effectiveDv = new Vector3[merged.vertexCount];
            var effectiveDn = new Vector3[merged.vertexCount];
            var effectiveDt = new Vector3[merged.vertexCount];

            for (int baseIndex = 0; baseIndex < baseMesh.blendShapeCount; baseIndex++)
            {
                float weight = baseRenderer.GetBlendShapeWeight(baseIndex);
                if (Mathf.Abs(weight) <= 1e-6f) continue;
                string name = baseMesh.GetBlendShapeName(baseIndex);
                int mergedIndex = merged.GetBlendShapeIndex(name);
                if (mergedIndex < 0) continue;

                Array.Clear(effectiveDv, 0, effectiveDv.Length);
                Array.Clear(effectiveDn, 0, effectiveDn.Length);
                Array.Clear(effectiveDt, 0, effectiveDt.Length);
                foreach (var coefficient in GetBlendshapeFrameCoefficients(merged, mergedIndex, weight))
                {
                    merged.GetBlendShapeFrameVertices(mergedIndex, coefficient.frameIndex, frameDv, frameDn, frameDt);
                    for (int i = 0; i < merged.vertexCount; i++)
                    {
                        effectiveDv[i] += frameDv[i] * coefficient.coefficient;
                        effectiveDn[i] += frameDn[i] * coefficient.coefficient;
                        effectiveDt[i] += frameDt[i] * coefficient.coefficient;
                    }
                }

                foreach (PreparedAttachment attachment in attachments)
                {
                    Vector3[] positions = accumulatedPositions[attachment];
                    Vector3[] normals = accumulatedNormals[attachment];
                    Vector3[] tangents = accumulatedTangents[attachment];
                    for (int i = 0; i < attachment.result.vertexCount; i++)
                    {
                        int mergedVertex = attachment.result.vertexStart + i;
                        positions[i] += effectiveDv[mergedVertex];
                        normals[i] += effectiveDn[mergedVertex];
                        tangents[i] += effectiveDt[mergedVertex];
                    }
                }
            }

            Vector3[] mergedVertices = merged.vertices;
            Vector3[] mergedNormals = merged.normals;
            Vector4[] mergedTangents = merged.tangents;
            foreach (PreparedAttachment attachment in attachments)
            {
                Vector3[] positionOffset = accumulatedPositions[attachment];
                Vector3[] normalOffset = accumulatedNormals[attachment];
                Vector3[] tangentOffset = accumulatedTangents[attachment];
                for (int i = 0; i < attachment.result.vertexCount; i++)
                {
                    int mergedVertex = attachment.result.vertexStart + i;
                    attachment.vertices[i] -= positionOffset[i];
                    attachment.normals[i] = (attachment.normals[i] - normalOffset[i]).normalized;
                    Vector3 tangent = new Vector3(attachment.tangents[i].x, attachment.tangents[i].y, attachment.tangents[i].z) - tangentOffset[i];
                    tangent = tangent.sqrMagnitude > 1e-12f ? tangent.normalized : Vector3.right;
                    attachment.tangents[i] = new Vector4(tangent.x, tangent.y, tangent.z, attachment.tangents[i].w);
                    mergedVertices[mergedVertex] = attachment.vertices[i];
                    mergedNormals[mergedVertex] = attachment.normals[i];
                    mergedTangents[mergedVertex] = attachment.tangents[i];
                }
            }
            merged.vertices = mergedVertices;
            merged.normals = mergedNormals;
            merged.tangents = mergedTangents;
        }

        private struct FrameCoefficient
        {
            public int frameIndex;
            public float coefficient;
        }

        private static IEnumerable<FrameCoefficient> GetBlendshapeFrameCoefficients(Mesh mesh, int shapeIndex, float weight)
        {
            int frameCount = mesh.GetBlendShapeFrameCount(shapeIndex);
            if (frameCount <= 0 || Mathf.Abs(weight) <= 1e-6f) yield break;
            if (frameCount == 1)
            {
                float frameWeight = mesh.GetBlendShapeFrameWeight(shapeIndex, 0);
                if (Mathf.Abs(frameWeight) > 1e-6f)
                    yield return new FrameCoefficient { frameIndex = 0, coefficient = weight / frameWeight };
                yield break;
            }

            float firstWeight = mesh.GetBlendShapeFrameWeight(shapeIndex, 0);
            if (weight <= firstWeight)
            {
                if (Mathf.Abs(firstWeight) > 1e-6f)
                    yield return new FrameCoefficient { frameIndex = 0, coefficient = weight / firstWeight };
                yield break;
            }

            for (int frame = 1; frame < frameCount; frame++)
            {
                float previousWeight = mesh.GetBlendShapeFrameWeight(shapeIndex, frame - 1);
                float currentWeight = mesh.GetBlendShapeFrameWeight(shapeIndex, frame);
                if (weight > currentWeight && frame < frameCount - 1) continue;
                float range = currentWeight - previousWeight;
                float t = Mathf.Abs(range) > 1e-6f ? (weight - previousWeight) / range : 1f;
                yield return new FrameCoefficient { frameIndex = frame - 1, coefficient = 1f - t };
                yield return new FrameCoefficient { frameIndex = frame, coefficient = t };
                yield break;
            }
        }

        private static Material[] BuildMaterials(Material[] baseMaterials, int baseSubmeshCount, List<PreparedAttachment> attachments, BuildResult output)
        {
            var materials = new List<Material>();
            baseMaterials = baseMaterials ?? Array.Empty<Material>();
            for (int i = 0; i < baseSubmeshCount; i++) materials.Add(i < baseMaterials.Length ? baseMaterials[i] : null);

            foreach (var p in attachments)
            {
                Material[] source = p.input.materials ?? Array.Empty<Material>();
                for (int i = 0; i < p.input.mesh.subMeshCount; i++)
                {
                    Material material = i < source.Length ? source[i] : (source.Length > 0 ? source[0] : null);
                    if (p.input.data.visibilityMode == AttachToBlendshapeVisibilityMode.UVDiscard)
                    {
                        if (!UVManipulator.IsPoiyomiWithUVSupport(material))
                        {
                            output.error = $"Attachment '{p.input.displayName}' material slot {i} does not support UV discard.";
                            return materials.ToArray();
                        }
                        material = CreateUvMaterialCopy(material, p.result.uvChannel, p.input.data.uvDiscardRow, p.input.data.uvDiscardColumn, p.result.defaultHidden);
                        p.result.uvDiscardMaterialIndices.Add(materials.Count);
                    }
                    materials.Add(material);
                }
            }
            return materials.ToArray();
        }

        private static Material CreateUvMaterialCopy(Material source, int channel, int row, int col, bool hidden)
        {
            var copy = UnityEngine.Object.Instantiate(source);
            copy.name = source.name + "_YUCP_AttachUV";
            string shader = copy.shader != null ? copy.shader.name.ToLowerInvariant() : string.Empty;
            if (shader.Contains("poiyomi")) copy.EnableKeyword("POI_UDIMDISCARD");
            else copy.EnableKeyword("WFFS_FEATURES_UVDISCARD");
            SetIfPresent(copy, "_WFFS_FEATURES_UVDISCARD", 1f);
            SetIfPresent(copy, "_EnableUDIMDiscardOptions", 1f);
            SetIfPresent(copy, "_UDIMDiscardMode", 0f);
            if (copy.HasProperty("_UDIMDiscardUV")) copy.SetFloat("_UDIMDiscardUV", channel);
            else SetIfPresent(copy, "_UDIMDiscardUVChannel", channel);
            string tile = FbxMergeToggleUvDiscardMapper.GetTilePropertyName(row, col);
            SetIfPresent(copy, tile, hidden ? 1f : 0f);
            copy.SetOverrideTag(tile + "Animated", "1");
            return copy;
        }

        private static void SetIfPresent(Material material, string property, float value)
        {
            if (material != null && material.HasProperty(property)) material.SetFloat(property, value);
        }

        private static void ApplyUvTile(Mesh mesh, int start, int count, int channel, int row, int col)
        {
            var uv = new List<Vector4>();
            mesh.GetUVs(channel, uv);
            while (uv.Count < mesh.vertexCount) uv.Add(Vector4.zero);
            int end = Mathf.Min(start + count, uv.Count);
            for (int i = start; i < end; i++)
            {
                Vector4 value = uv[i];
                value.x += col;
                value.y += row;
                uv[i] = value;
            }
            mesh.SetUVs(channel, uv);
        }

        private static Material[] BuildMaterialArray(Material[] source, int count)
        {
            var result = new Material[count];
            for (int i = 0; i < count; i++) result[i] = source != null && source.Length > 0 ? source[Mathf.Min(i, source.Length - 1)] : null;
            return result;
        }

        private static bool HasTriangleTopology(Mesh mesh)
        {
            for (int i = 0; i < mesh.subMeshCount; i++) if (mesh.GetTopology(i) != MeshTopology.Triangles) return false;
            return mesh.subMeshCount > 0;
        }

        private static List<Vector4>[] ReadAllUvs(Mesh mesh, int count)
        {
            var result = new List<Vector4>[8];
            for (int channel = 0; channel < 8; channel++)
            {
                result[channel] = new List<Vector4>();
                mesh.GetUVs(channel, result[channel]);
                if (result[channel].Count != count) result[channel] = Enumerable.Repeat(Vector4.zero, count).ToList();
            }
            return result;
        }

        private static Matrix4x4[] BuildBoneMatrices(SkinnedMeshRenderer renderer, Matrix4x4[] bindposes)
        {
            if (renderer == null || bindposes == null || bindposes.Length == 0)
                throw new InvalidOperationException("Renderer has no bind poses.");

            Transform[] bones = renderer.bones;
            var result = new Matrix4x4[bindposes.Length];
            Matrix4x4 worldToRenderer = renderer.transform.worldToLocalMatrix;
            for (int i = 0; i < result.Length; i++)
            {
                if (bones == null || i >= bones.Length || bones[i] == null) continue;
                result[i] = worldToRenderer * bones[i].localToWorldMatrix * bindposes[i];
            }
            return result;
        }

        private static Mesh BuildCurrentPoseSurface(
            Mesh baseMesh,
            List<BoneWeight1>[] influences,
            Matrix4x4[] boneMatrices)
        {
            Vector3[] source = baseMesh.vertices;
            var current = new Vector3[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                Matrix4x4 skin = BlendMatrices(influences[i], boneMatrices);
                EnsureInvertible(skin, $"base vertex {i}");
                current[i] = skin.MultiplyPoint3x4(source[i]);
            }

            var result = new Mesh
            {
                name = "__YUCP_CurrentPoseSurface",
                indexFormat = baseMesh.indexFormat
            };
            result.vertices = current;
            result.subMeshCount = baseMesh.subMeshCount;
            for (int i = 0; i < baseMesh.subMeshCount; i++)
                result.SetTriangles(baseMesh.GetTriangles(i), i, false);
            result.RecalculateBounds();
            return result;
        }

        private static Matrix4x4[] BuildSourceSkinMatrices(AttachmentInput input, int vertexCount)
        {
            var result = new Matrix4x4[vertexCount];
            if (input.skinnedRenderer == null)
            {
                for (int i = 0; i < result.Length; i++) result[i] = Matrix4x4.identity;
                return result;
            }

            var influences = ReadBoneInfluences(input.mesh);
            if (influences == null || influences.Length != vertexCount || input.mesh.bindposes == null || input.mesh.bindposes.Length == 0)
                throw new InvalidOperationException($"Skinned attachment '{input.displayName}' has invalid bone weights or bind poses.");
            Matrix4x4[] bones = BuildBoneMatrices(input.skinnedRenderer, input.mesh.bindposes);
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = BlendMatrices(influences[i], bones);
                EnsureInvertible(result[i], $"source attachment vertex {i}");
            }
            return result;
        }

        private static Matrix4x4 BlendMatrices(IList<BoneWeight1> influences, Matrix4x4[] boneMatrices)
        {
            if (influences == null || influences.Count == 0)
                throw new InvalidOperationException("A vertex has no skinning influences.");

            var result = new Matrix4x4();
            float totalWeight = 0f;
            foreach (BoneWeight1 influence in influences)
            {
                if (influence.weight <= 0f) continue;
                if (influence.boneIndex < 0 || boneMatrices == null || influence.boneIndex >= boneMatrices.Length)
                    throw new InvalidOperationException($"Bone index {influence.boneIndex} is outside the renderer bone array.");
                Matrix4x4 bone = boneMatrices[influence.boneIndex];
                for (int row = 0; row < 4; row++)
                for (int column = 0; column < 4; column++)
                    result[row, column] += bone[row, column] * influence.weight;
                totalWeight += influence.weight;
            }

            if (totalWeight <= 1e-8f)
                throw new InvalidOperationException("A vertex has no positive skinning weight.");
            if (!Mathf.Approximately(totalWeight, 1f))
            {
                float inverseWeight = 1f / totalWeight;
                for (int row = 0; row < 4; row++)
                for (int column = 0; column < 4; column++)
                    result[row, column] *= inverseWeight;
            }
            return result;
        }

        private static void EnsureInvertible(Matrix4x4 matrix, string context)
        {
            float determinant = matrix.determinant;
            if (float.IsNaN(determinant) || float.IsInfinity(determinant) || Mathf.Abs(determinant) <= 1e-8f)
                throw new InvalidOperationException($"Skinning matrix for {context} is singular.");
        }

        private static Vector3 TransformNormal(Matrix4x4 matrix, Vector3 normal)
        {
            EnsureInvertible(matrix, "normal transform");
            Vector3 transformed = matrix.inverse.transpose.MultiplyVector(normal);
            return transformed.sqrMagnitude > 1e-12f ? transformed.normalized : Vector3.up;
        }

        private static List<BoneWeight1> InterpolateBoneWeights(
            ClosestSurfaceMapper.SurfaceMap map,
            int[] triangles,
            List<BoneWeight1>[] weights)
        {
            int t = map.triIndex * 3;
            var accum = new Dictionary<int, float>();
            Accumulate(accum, weights[triangles[t]], map.barycentric.x);
            Accumulate(accum, weights[triangles[t + 1]], map.barycentric.y);
            Accumulate(accum, weights[triangles[t + 2]], map.barycentric.z);
            var top = accum.Where(kv => kv.Value > 0f).OrderByDescending(kv => kv.Value).Take(4).ToArray();
            float sum = top.Sum(kv => kv.Value);
            if (sum <= 1e-8f) throw new InvalidOperationException("Surface mapping produced no valid bone influence.");
            return top.Select(kv => new BoneWeight1 { boneIndex = kv.Key, weight = kv.Value / sum }).ToList();
        }

        private static void Accumulate(Dictionary<int, float> values, List<BoneWeight1> weights, float factor)
        {
            if (weights == null) return;
            foreach (var weight in weights) Add(values, weight.boneIndex, weight.weight * factor);
        }

        private static void Add(Dictionary<int, float> values, int bone, float weight)
        {
            if (weight <= 0f) return;
            values[bone] = values.TryGetValue(bone, out float current) ? current + weight : weight;
        }

        private static List<BoneWeight1>[] ReadBoneInfluences(Mesh mesh)
        {
            try
            {
                NativeArray<byte> perVertex = mesh.GetBonesPerVertex();
                NativeArray<BoneWeight1> all = mesh.GetAllBoneWeights();
                var result = new List<BoneWeight1>[mesh.vertexCount];
                int cursor = 0;
                for (int i = 0; i < result.Length; i++)
                {
                    result[i] = new List<BoneWeight1>(perVertex[i]);
                    for (int j = 0; j < perVertex[i]; j++) result[i].Add(all[cursor++]);
                }
                perVertex.Dispose();
                all.Dispose();
                return cursor > 0 ? result : null;
            }
            catch
            {
                return null;
            }
        }

        private static void WriteBoneWeights(Mesh mesh, List<BoneWeight1>[] influences)
        {
            int total = influences.Sum(list => list?.Count ?? 0);
            var perVertex = new NativeArray<byte>(influences.Length, Allocator.Temp);
            var all = new NativeArray<BoneWeight1>(total, Allocator.Temp);
            try
            {
                int cursor = 0;
                for (int i = 0; i < influences.Length; i++)
                {
                    var list = influences[i] ?? throw new InvalidOperationException($"Vertex {i} has no skinning influences.");
                    perVertex[i] = checked((byte)list.Count);
                    foreach (var weight in list) all[cursor++] = weight;
                }
                mesh.SetBoneWeights(perVertex, all);
            }
            finally
            {
                perVertex.Dispose();
                all.Dispose();
            }
        }

        private static bool PropagateSurfaceMaps(ClosestSurfaceMapper.SurfaceMap[] maps, int[] triangles)
        {
            if (maps == null || maps.Length == 0) return false;
            var adjacency = new List<int>[maps.Length];
            for (int i = 0; i < adjacency.Length; i++) adjacency[i] = new List<int>();
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                AddEdge(triangles[i], triangles[i + 1]);
                AddEdge(triangles[i + 1], triangles[i + 2]);
                AddEdge(triangles[i + 2], triangles[i]);
            }
            var queue = new Queue<int>();
            var source = Enumerable.Repeat(-1, maps.Length).ToArray();
            for (int i = 0; i < maps.Length; i++) if (maps[i].matched) { source[i] = i; queue.Enqueue(i); }
            while (queue.Count > 0)
            {
                int v = queue.Dequeue();
                foreach (int n in adjacency[v]) if (source[n] < 0) { source[n] = source[v]; queue.Enqueue(n); }
            }
            for (int i = 0; i < maps.Length; i++) if (!maps[i].matched && source[i] >= 0) maps[i] = maps[source[i]];
            return maps.All(m => m.matched && m.triIndex >= 0);

            void AddEdge(int a, int b)
            {
                if ((uint)a >= (uint)maps.Length || (uint)b >= (uint)maps.Length || a == b) return;
                adjacency[a].Add(b);
                adjacency[b].Add(a);
            }
        }

        private static Quaternion TriangleFrame(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 tangent = (b - a).normalized;
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            return FrameRotation(normal, tangent);
        }

        private static Quaternion FrameRotation(Vector3 normal, Vector3 tangent)
        {
            normal = normal.sqrMagnitude > 1e-8f ? normal.normalized : Vector3.forward;
            tangent = Vector3.ProjectOnPlane(tangent, normal).normalized;
            if (tangent.sqrMagnitude < 1e-8f) tangent = Vector3.Cross(normal, Vector3.up).normalized;
            if (tangent.sqrMagnitude < 1e-8f) tangent = Vector3.Cross(normal, Vector3.right).normalized;
            return Quaternion.LookRotation(normal, Vector3.Cross(normal, tangent).normalized);
        }

        private static string UniqueVisibilityName(Mesh mesh, string path, string displayName)
        {
            string root = $"__YUCP_Attach_Size_{Sanitize(displayName)}_{StableHash(path ?? displayName):X8}";
            string name = root;
            int suffix = 1;
            while (mesh.GetBlendShapeIndex(name) >= 0) name = root + "_" + suffix++;
            return name;
        }

        private static uint StableHash(string value)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in value ?? string.Empty) { hash ^= c; hash *= 16777619; }
                return hash;
            }
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value)) return "Attachment";
            return new string(value.Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '.' ? c : '_').ToArray());
        }
    }
}
