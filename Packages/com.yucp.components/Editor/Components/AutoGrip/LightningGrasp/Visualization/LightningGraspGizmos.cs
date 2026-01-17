using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace YUCP.Components.Editor.AutoGrip.LightningGrasp
{
    /// <summary>
    /// Gizmos for visualizing contact fields, domains, and grasp solutions.
    /// </summary>
    public static class LightningGraspGizmos
    {
        /// <summary>
        /// Draw contact patches on finger surfaces.
        /// </summary>
        public static void DrawContactPatches(
            Animator animator,
            ContactField field,
            bool isLeftHand,
            float alpha = 0.5f)
        {
            if (field == null) return;

            var boneIndexMap = HandForwardKinematics.CreateBoneIndexMap(isLeftHand);

            // Color by finger
            var fingerColors = new Dictionary<string, Color>
            {
                { "Thumb", new Color(1f, 0.3f, 0.3f, alpha) },
                { "Index", new Color(1f, 0.6f, 0.3f, alpha) },
                { "Middle", new Color(1f, 1f, 0.3f, alpha) },
                { "Ring", new Color(0.3f, 1f, 0.3f, alpha) },
                { "Little", new Color(0.3f, 0.6f, 1f, alpha) }
            };

            foreach (var patch in field.patches)
            {
                var boneTransform = animator.GetBoneTransform(patch.parentBone);
                if (boneTransform == null) continue;

                // Determine finger for coloring
                string boneName = patch.parentBone.ToString();
                Color patchColor = Color.white * alpha;
                foreach (var kvp in fingerColors)
                {
                    if (boneName.Contains(kvp.Key))
                    {
                        patchColor = kvp.Value;
                        break;
                    }
                }

                Handles.color = patchColor;

                // Transform keyvectors to world space
                Matrix4x4 boneMatrix = boneTransform.localToWorldMatrix;
                var worldKVs = patch.TransformAll(boneMatrix);

                // Draw each keyvector as a small disc + normal line
                foreach (var kv in worldKVs)
                {
                    Handles.DrawSolidDisc(kv.position, kv.normal, 0.002f);
                    Handles.color = new Color(patchColor.r, patchColor.g, patchColor.b, alpha * 0.5f);
                    Handles.DrawLine(kv.position, kv.position + kv.normal * 0.008f);
                    Handles.color = patchColor;
                }

                // Draw centroid with larger marker
                var worldCentroid = patch.centroid.Transform(boneMatrix);
                Handles.DrawWireDisc(worldCentroid.position, worldCentroid.normal, 0.004f);
            }
        }

        /// <summary>
        /// Draw contact domains on object surface.
        /// </summary>
        public static void DrawContactDomains(
            Dictionary<HumanBodyBones, ContactDomain> domains,
            float alpha = 0.6f)
        {
            if (domains == null) return;

            // Color by finger
            var fingerColors = new Dictionary<string, Color>
            {
                { "Thumb", new Color(1f, 0f, 0f, alpha) },
                { "Index", new Color(1f, 0.5f, 0f, alpha) },
                { "Middle", new Color(1f, 1f, 0f, alpha) },
                { "Ring", new Color(0f, 1f, 0f, alpha) },
                { "Little", new Color(0f, 0.5f, 1f, alpha) }
            };

            foreach (var kvp in domains)
            {
                var bone = kvp.Key;
                var domain = kvp.Value;

                // Determine finger for coloring
                string boneName = bone.ToString();
                Color domainColor = Color.white * alpha;
                foreach (var fc in fingerColors)
                {
                    if (boneName.Contains(fc.Key))
                    {
                        domainColor = fc.Value;
                        break;
                    }
                }

                Handles.color = domainColor;

                // Draw each point in domain
                for (int i = 0; i < domain.objectPoints.Count; i++)
                {
                    Vector3 pos = domain.objectPoints[i];
                    Vector3 normal = domain.objectNormals[i];

                    Handles.DrawSolidDisc(pos, normal, 0.003f);

                    // Draw normal spike
                    Handles.color = new Color(domainColor.r, domainColor.g, domainColor.b, alpha * 0.4f);
                    Handles.DrawLine(pos, pos + normal * 0.008f);
                    Handles.color = domainColor;
                }
            }
        }

        /// <summary>
        /// Draw grasp result: contact points and finger rays.
        /// </summary>
        public static void DrawGraspResult(
            Animator animator,
            GraspResult result,
            float alpha = 0.8f)
        {
            if (result == null || result.contactPositions == null) return;

            // Draw contact points
            Handles.color = new Color(0f, 1f, 1f, alpha);
            for (int i = 0; i < result.contactPositions.Length; i++)
            {
                Vector3 pos = result.contactPositions[i];
                Vector3 normal = result.contactNormals[i];

                Handles.DrawSolidDisc(pos, normal, 0.005f);
                Handles.DrawWireDisc(pos, normal, 0.008f);
                Handles.DrawLine(pos, pos - normal * 0.02f);

                // Label
                Handles.Label(pos + Vector3.up * 0.01f, $"C{i}");
            }

            // Draw finger-to-contact rays
            Handles.color = new Color(1f, 1f, 0f, alpha * 0.5f);
            if (result.contactBones != null)
            {
                for (int i = 0; i < result.contactBones.Length; i++)
                {
                    var t = animator.GetBoneTransform(result.contactBones[i]);
                    if (t != null)
                    {
                        Handles.DrawDottedLine(t.position, result.contactPositions[i], 2f);
                    }
                }
            }

            // Draw wrench score
            Vector3 scorePos = result.contactPositions.Length > 0
                ? result.contactPositions[0] + Vector3.up * 0.05f
                : Vector3.zero;

            string scoreText = result.isValid
                ? $"Wrench: {result.wrenchScore:F3} ✓"
                : $"Wrench: {result.wrenchScore:F3} ✗";

            Handles.color = result.isValid ? Color.green : Color.red;
            Handles.Label(scorePos, scoreText);
        }

        /// <summary>
        /// Draw BVH bounds (for debugging).
        /// </summary>
        public static void DrawBVHBounds(
            LBVHS2Bundle bvh,
            int maxDepth = 3,
            float alpha = 0.2f)
        {
            // Note: Would need to expose BVH nodes for visualization
            // For now, just draw a placeholder message
            Handles.color = new Color(0.5f, 0.5f, 0.5f, alpha);
            Handles.Label(Vector3.zero, $"BVH: {bvh?.NodeCount ?? 0} nodes, {bvh?.SampleCount ?? 0} samples");
        }

        /// <summary>
        /// Draw interaction matrix as rays from hand to object.
        /// </summary>
        public static void DrawInteractionRays(
            Animator animator,
            ContactField field,
            int[,] interactionMatrix,
            Vector3[] objectPoints,
            int maxRays = 50,
            float alpha = 0.3f)
        {
            if (field == null || interactionMatrix == null || objectPoints == null) return;

            int numPatches = interactionMatrix.GetLength(0);
            int numPoints = interactionMatrix.GetLength(1);

            int raysDrawn = 0;

            for (int p = 0; p < numPatches && raysDrawn < maxRays; p++)
            {
                var patch = field.GetPatch(p);
                if (patch == null) continue;

                var boneTransform = animator.GetBoneTransform(patch.parentBone);
                if (boneTransform == null) continue;

                Vector3 patchWorldPos = boneTransform.TransformPoint(patch.centroid.position);

                for (int pt = 0; pt < numPoints && raysDrawn < maxRays; pt++)
                {
                    if (interactionMatrix[p, pt] > 0)
                    {
                        // Determine finger for coloring
                        float hue = (float)p / numPatches;
                        Handles.color = Color.HSVToRGB(hue, 0.7f, 0.9f) * new Color(1, 1, 1, alpha);
                        
                        Handles.DrawDottedLine(patchWorldPos, objectPoints[pt], 3f);
                        raysDrawn++;
                    }
                }
            }
        }

        /// <summary>
        /// Draw legend for contact domain colors.
        /// </summary>
        public static void DrawLegend(Vector3 worldPosition)
        {
            var fingerNames = new[] { "Thumb", "Index", "Middle", "Ring", "Little" };
            var fingerColors = new[] {
                Color.red, new Color(1f, 0.5f, 0f), Color.yellow, Color.green, Color.blue
            };

            Vector3 pos = worldPosition;
            for (int i = 0; i < fingerNames.Length; i++)
            {
                Handles.color = fingerColors[i];
                Handles.DrawSolidDisc(pos, Vector3.up, 0.005f);
                Handles.Label(pos + Vector3.right * 0.01f, fingerNames[i]);
                pos += Vector3.up * 0.015f;
            }
        }
    }
}
