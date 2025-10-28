using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using YUCP.Components.Editor.MeshUtils;

namespace YUCP.Components.Editor.Utils
{
    /// <summary>
    /// Detects VRChat viseme blendshapes on avatar meshes.
    /// Supports both standard naming conventions and Avatar Descriptor viseme mappings.
    /// </summary>
    public static class VRChatVisemeDetector
    {
        // Standard VRChat viseme blendshape names
        private static readonly string[] StandardVisemeNames = new string[]
        {
            "vrc.v_sil",  // Silence
            "vrc.v_pp",   // PP, MB, P, B, M
            "vrc.v_ff",   // FF, V
            "vrc.v_th",   // TH
            "vrc.v_dd",   // DD, T, D
            "vrc.v_kk",   // KK, G, K, N, NG
            "vrc.v_ch",   // CH, SH, ZH, J
            "vrc.v_ss",   // SS, S, Z
            "vrc.v_nn",   // NN, L
            "vrc.v_rr",   // RR, R
            "vrc.v_aa",   // AA
            "vrc.v_e",    // E
            "vrc.v_i",    // I (ih)
            "vrc.v_o",    // O
            "vrc.v_u"     // U
        };

        // Alternative common naming patterns
        private static readonly Dictionary<string, string[]> VisemeAliases = new Dictionary<string, string[]>
        {
            { "sil", new[] { "sil", "silence", "neutral" } },
            { "pp", new[] { "pp", "PP", "p", "b", "m" } },
            { "ff", new[] { "ff", "FF", "f", "v" } },
            { "th", new[] { "th", "TH" } },
            { "dd", new[] { "dd", "DD", "d", "t" } },
            { "kk", new[] { "kk", "KK", "k", "g", "n", "ng" } },
            { "ch", new[] { "ch", "CH", "sh", "j", "zh" } },
            { "ss", new[] { "ss", "SS", "s", "z" } },
            { "nn", new[] { "nn", "NN", "n", "l" } },
            { "rr", new[] { "rr", "RR", "r" } },
            { "aa", new[] { "aa", "AA", "ah" } },
            { "e", new[] { "e", "E", "eh" } },
            { "i", new[] { "i", "I", "ih" } },
            { "o", new[] { "o", "O", "oh" } },
            { "u", new[] { "u", "U", "ou" } }
        };

        /// <summary>
        /// Detect viseme blendshapes from Avatar Descriptor.
        /// </summary>
        public static List<string> DetectVisemesFromDescriptor(VRCAvatarDescriptor descriptor)
        {
            List<string> visemeBlendshapes = new List<string>();

            if (descriptor == null)
            {
                Debug.LogWarning("[VRChatVisemeDetector] No Avatar Descriptor provided");
                return visemeBlendshapes;
            }

            // Check if using blendshape visemes
            if (descriptor.lipSync == VRC.SDKBase.VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape)
            {
                if (descriptor.VisemeSkinnedMesh != null && descriptor.VisemeSkinnedMesh.sharedMesh != null)
                {
                    Mesh mesh = descriptor.VisemeSkinnedMesh.sharedMesh;

                    // Extract viseme blendshape names from descriptor
                    var visemeBlendshapesField = typeof(VRCAvatarDescriptor).GetField("VisemeBlendShapes");
                    if (visemeBlendshapesField != null)
                    {
                        string[] visemeBlendshapeNames = visemeBlendshapesField.GetValue(descriptor) as string[];
                        if (visemeBlendshapeNames != null)
                        {
                            foreach (string name in visemeBlendshapeNames)
                            {
                                if (!string.IsNullOrEmpty(name) && mesh.GetBlendShapeIndex(name) >= 0)
                                {
                                    visemeBlendshapes.Add(name);
                                }
                            }
                        }
                    }
                }
            }

            return visemeBlendshapes;
        }

        /// <summary>
        /// Detect potential viseme blendshapes by name matching.
        /// </summary>
        public static List<string> DetectVisemesByNaming(Mesh mesh)
        {
            List<string> visemeBlendshapes = new List<string>();

            if (mesh == null || mesh.blendShapeCount == 0)
            {
                return visemeBlendshapes;
            }

            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                string blendshapeName = mesh.GetBlendShapeName(i);
                
                if (IsVisemeBlendshape(blendshapeName))
                {
                    visemeBlendshapes.Add(blendshapeName);
                }
            }

            // Reduce log spam - only log when actually used in build
            return visemeBlendshapes;
        }

        /// <summary>
        /// Check if a blendshape name matches viseme patterns.
        /// </summary>
        public static bool IsVisemeBlendshape(string blendshapeName)
        {
            if (string.IsNullOrEmpty(blendshapeName))
            {
                return false;
            }

            string lowerName = blendshapeName.ToLower();

            // Check standard VRC names
            foreach (string standardName in StandardVisemeNames)
            {
                if (lowerName.Contains(standardName.ToLower()))
                {
                    return true;
                }
            }

            // Check aliases
            foreach (var aliasGroup in VisemeAliases.Values)
            {
                foreach (string alias in aliasGroup)
                {
                    if (lowerName == alias.ToLower() || 
                        lowerName.Contains($"_{alias.ToLower()}") ||
                        lowerName.Contains($".{alias.ToLower()}") ||
                        lowerName.StartsWith($"{alias.ToLower()}_"))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Get standard VRChat viseme names for reference.
        /// </summary>
        public static string[] GetStandardVisemeNames()
        {
            return (string[])StandardVisemeNames.Clone();
        }

        /// <summary>
        /// Smart detection: test which blendshapes actually move the cluster vertices.
        /// </summary>
        public static List<string> DetectActiveBlendshapes(
            SkinnedMeshRenderer renderer,
            SurfaceCluster cluster,
            float threshold = 0.001f)
        {
            List<string> activeBlendshapes = new List<string>();

            if (renderer == null || renderer.sharedMesh == null || cluster == null)
            {
                return activeBlendshapes;
            }

            // Use PoseSampler to test all blendshapes
            var displacements = PoseSampler.SampleAllBlendshapesAtWeight(renderer, cluster, 100f);

            foreach (var kvp in displacements)
            {
                float displacement = kvp.Value.magnitude;
                
                if (displacement >= threshold)
                {
                    activeBlendshapes.Add(kvp.Key);
                    // Reduce log spam - only log in debug builds
                    // Debug.Log($"[VRChatVisemeDetector] Active blendshape: '{kvp.Key}' (displacement: {displacement:F4}m)");
                }
            }

            // Log summary only when explicitly requested
            return activeBlendshapes;
        }

        /// <summary>
        /// Get visemes from avatar, with fallback to name-based detection.
        /// </summary>
        public static List<string> GetVisemeBlendshapes(
            SkinnedMeshRenderer renderer,
            GameObject avatarRoot = null)
        {
            List<string> visemes = new List<string>();

            // Try Avatar Descriptor first
            if (avatarRoot != null)
            {
                var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
                if (descriptor != null)
                {
                    visemes = DetectVisemesFromDescriptor(descriptor);
                    if (visemes.Count > 0)
                    {
                        return visemes;
                    }
                }
            }

            // Fallback to name-based detection
            if (renderer != null && renderer.sharedMesh != null)
            {
                visemes = DetectVisemesByNaming(renderer.sharedMesh);
            }

            return visemes;
        }
    }
}

