using System;
using System.Collections.Generic;
using UnityEngine;

namespace YUCP.Components.Editor.MeshUtils
{
    public static class MergedBlendshapeBuilder
    {
        public struct MergeBlendshapeResult
        {
            public int baseBlendshapeCount;
            public int attachmentBlendshapeCount;
            public int totalBlendshapeCount;
            public int renamedAttachmentBlendshapes;
            public Dictionary<string, string> attachmentBlendshapeNameMap;
            public List<string> warnings;
        }

        public static MergeBlendshapeResult MergeBlendshapes(
            Mesh baseMesh,
            Mesh attachmentMesh,
            Mesh mergedMesh,
            int baseVertexCount,
            int attachmentVertexStart,
            int attachmentVertexCount,
            string attachmentName,
            bool renameConflictingAttachmentBlendshapes,
            string attachmentBlendshapePrefix,
            bool debugMode,
            UnityEngine.Object context = null)
        {
            var result = new MergeBlendshapeResult
            {
                baseBlendshapeCount = 0,
                attachmentBlendshapeCount = 0,
                totalBlendshapeCount = 0,
                renamedAttachmentBlendshapes = 0,
                attachmentBlendshapeNameMap = new Dictionary<string, string>(),
                warnings = new List<string>()
            };

            if (baseMesh == null || mergedMesh == null)
            {
                result.warnings.Add("Cannot merge blendshapes: base or merged mesh is null.");
                return result;
            }

            var usedNames = new HashSet<string>(StringComparer.Ordinal);

            int baseCount = Math.Max(0, baseMesh.blendShapeCount);
            for (int bi = 0; bi < baseCount; bi++)
            {
                string blendshapeName = baseMesh.GetBlendShapeName(bi);
                if (string.IsNullOrEmpty(blendshapeName))
                {
                    blendshapeName = $"BaseBlendshape_{bi}";
                }

                bool added = CopyBlendshapeFramesFromSource(
                    sourceMesh: baseMesh,
                    sourceBlendshapeIndex: bi,
                    targetMesh: mergedMesh,
                    targetBlendshapeName: blendshapeName,
                    sourceVertexStart: 0,
                    sourceVertexCount: baseVertexCount,
                    targetVertexStart: 0,
                    targetVertexCount: mergedMesh.vertexCount,
                    warnings: result.warnings);

                if (added)
                {
                    usedNames.Add(blendshapeName);
                    result.baseBlendshapeCount++;
                }
            }

            if (attachmentMesh == null || attachmentMesh.blendShapeCount == 0)
            {
                result.totalBlendshapeCount = mergedMesh.blendShapeCount;
                return result;
            }

            string safeAttachmentName = string.IsNullOrWhiteSpace(attachmentName) ? "Attachment" : attachmentName;
            string prefix = string.IsNullOrWhiteSpace(attachmentBlendshapePrefix) ? "Merged_" : attachmentBlendshapePrefix;

            int attachmentCount = Math.Max(0, attachmentMesh.blendShapeCount);
            for (int ai = 0; ai < attachmentCount; ai++)
            {
                string sourceName = attachmentMesh.GetBlendShapeName(ai);
                if (string.IsNullOrEmpty(sourceName))
                {
                    sourceName = $"AttachmentBlendshape_{ai}";
                }

                string targetName = sourceName;
                if (usedNames.Contains(targetName) && renameConflictingAttachmentBlendshapes)
                {
                    targetName = $"{prefix}{safeAttachmentName}__{sourceName}";
                    int suffix = 1;
                    while (usedNames.Contains(targetName))
                    {
                        targetName = $"{prefix}{safeAttachmentName}__{sourceName}_{suffix}";
                        suffix++;
                    }
                    result.renamedAttachmentBlendshapes++;
                }
                else if (usedNames.Contains(targetName))
                {
                    targetName = $"{prefix}{safeAttachmentName}__{sourceName}";
                    int suffix = 1;
                    while (usedNames.Contains(targetName))
                    {
                        targetName = $"{prefix}{safeAttachmentName}__{sourceName}_{suffix}";
                        suffix++;
                    }
                    result.renamedAttachmentBlendshapes++;
                    result.warnings.Add($"Attachment blendshape '{sourceName}' conflicted with base and was renamed to '{targetName}'.");
                }

                bool added = CopyBlendshapeFramesFromSource(
                    sourceMesh: attachmentMesh,
                    sourceBlendshapeIndex: ai,
                    targetMesh: mergedMesh,
                    targetBlendshapeName: targetName,
                    sourceVertexStart: 0,
                    sourceVertexCount: attachmentVertexCount,
                    targetVertexStart: attachmentVertexStart,
                    targetVertexCount: mergedMesh.vertexCount,
                    warnings: result.warnings);

                if (added)
                {
                    usedNames.Add(targetName);
                    result.attachmentBlendshapeCount++;
                    result.attachmentBlendshapeNameMap[sourceName] = targetName;
                }
            }

            result.totalBlendshapeCount = mergedMesh.blendShapeCount;

            if (debugMode)
            {
                Debug.Log(
                    $"[MergedBlendshapeBuilder] Base blendshapes: {result.baseBlendshapeCount}, " +
                    $"Attachment blendshapes: {result.attachmentBlendshapeCount}, " +
                    $"Total: {result.totalBlendshapeCount}, Renamed: {result.renamedAttachmentBlendshapes}",
                    context);
            }

            return result;
        }

        private static bool CopyBlendshapeFramesFromSource(
            Mesh sourceMesh,
            int sourceBlendshapeIndex,
            Mesh targetMesh,
            string targetBlendshapeName,
            int sourceVertexStart,
            int sourceVertexCount,
            int targetVertexStart,
            int targetVertexCount,
            List<string> warnings)
        {
            if (sourceMesh == null || targetMesh == null)
            {
                return false;
            }

            int frameCount = sourceMesh.GetBlendShapeFrameCount(sourceBlendshapeIndex);
            if (frameCount <= 0)
            {
                return false;
            }

            var deltaVertices = new Vector3[sourceMesh.vertexCount];
            var deltaNormals = new Vector3[sourceMesh.vertexCount];
            var deltaTangents = new Vector3[sourceMesh.vertexCount];

            var fullDeltaVertices = new Vector3[targetVertexCount];
            var fullDeltaNormals = new Vector3[targetVertexCount];
            var fullDeltaTangents = new Vector3[targetVertexCount];

            bool addedAnyFrame = false;

            for (int fi = 0; fi < frameCount; fi++)
            {
                Array.Clear(fullDeltaVertices, 0, fullDeltaVertices.Length);
                Array.Clear(fullDeltaNormals, 0, fullDeltaNormals.Length);
                Array.Clear(fullDeltaTangents, 0, fullDeltaTangents.Length);

                float weight = sourceMesh.GetBlendShapeFrameWeight(sourceBlendshapeIndex, fi);
                sourceMesh.GetBlendShapeFrameVertices(sourceBlendshapeIndex, fi, deltaVertices, deltaNormals, deltaTangents);

                for (int i = 0; i < sourceVertexCount; i++)
                {
                    int sourceIndex = sourceVertexStart + i;
                    int targetIndex = targetVertexStart + i;
                    if ((uint)sourceIndex >= (uint)deltaVertices.Length || (uint)targetIndex >= (uint)fullDeltaVertices.Length)
                    {
                        continue;
                    }

                    fullDeltaVertices[targetIndex] = deltaVertices[sourceIndex];
                    fullDeltaNormals[targetIndex] = deltaNormals[sourceIndex];
                    fullDeltaTangents[targetIndex] = deltaTangents[sourceIndex];
                }

                try
                {
                    targetMesh.AddBlendShapeFrame(targetBlendshapeName, weight, fullDeltaVertices, fullDeltaNormals, fullDeltaTangents);
                    addedAnyFrame = true;
                }
                catch (Exception ex)
                {
                    warnings?.Add($"Failed adding blendshape frame '{targetBlendshapeName}' (weight {weight}): {ex.Message}");
                    return addedAnyFrame;
                }
            }

            return addedAnyFrame;
        }
    }
}

