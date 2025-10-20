using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using VRC.SDKBase.Editor.BuildPipeline;
using com.vrcfury.api;
using YUCP.Components;
using YUCP.Components.Editor.MeshUtils;
using VF.Model;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Processes UVDiscardToggle components during avatar build.
    /// Merges clothing mesh into the body, applies UDIM discard, and creates a VRCFury toggle.
    /// </summary>
    public class UVDiscardToggleProcessor : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MinValue + 102; // Run after AutoBodyHiderProcessor

        public bool OnPreprocessAvatar(GameObject avatarRoot)
        {
            var components = avatarRoot.GetComponentsInChildren<UVDiscardToggleData>(true);

            foreach (var data in components)
            {
                ProcessUVDiscardToggle(data);
            }

            return true;
        }

        private void ProcessUVDiscardToggle(UVDiscardToggleData data)
        {
            if (!ValidateData(data)) return;

            Debug.Log($"[UVDiscardToggle] Processing '{data.name}'", data);

            // 1. Merge clothing mesh into body mesh
            Mesh mergedMesh = MergeClothingIntoBody(data);
            data.targetBodyMesh.sharedMesh = mergedMesh;

            // 2. Configure body material for UDIM discard
            Material poiyomiMaterial = ConfigureBodyMaterialForUDIM(data);
            if (poiyomiMaterial == null) return;

            // 3. Create VRCFury Toggle
            CreateVRCFuryToggle(data, poiyomiMaterial);

            Debug.Log($"[UVDiscardToggle] Successfully processed '{data.name}'. Clothing merged and toggle created.", data);
        }

        private bool ValidateData(UVDiscardToggleData data)
        {
            if (data.targetBodyMesh == null)
            {
                Debug.LogError($"[UVDiscardToggle] Target Body Mesh is not assigned on '{data.name}'.", data);
                return false;
            }
            if (data.clothingMesh == null)
            {
                Debug.LogError($"[UVDiscardToggle] Clothing Mesh is not assigned on '{data.name}'.", data);
                return false;
            }
            if (data.targetBodyMesh.sharedMesh == null)
            {
                Debug.LogError($"[UVDiscardToggle] Target Body Mesh has no mesh data on '{data.name}'.", data);
                return false;
            }
            if (data.clothingMesh.sharedMesh == null)
            {
                Debug.LogError($"[UVDiscardToggle] Clothing Mesh has no mesh data on '{data.name}'.", data);
                return false;
            }
            if (string.IsNullOrEmpty(data.menuPath) && string.IsNullOrEmpty(data.globalParameter))
            {
                Debug.LogError($"[UVDiscardToggle] Either Menu Path or Global Parameter must be set for '{data.name}'.", data);
                return false;
            }
            return true;
        }

        private Mesh MergeClothingIntoBody(UVDiscardToggleData data)
        {
            Mesh bodyMesh = data.targetBodyMesh.sharedMesh;
            Mesh clothingMesh = data.clothingMesh.sharedMesh;

            // Create a new combined mesh
            Mesh combinedMesh = new Mesh();
            combinedMesh.name = $"{bodyMesh.name}_Merged_{data.clothingMesh.name}";

            // Get vertices, normals, UVs from both meshes
            Vector3[] bodyVertices = bodyMesh.vertices;
            Vector3[] clothingVertices = clothingMesh.vertices;

            Vector3[] bodyNormals = bodyMesh.normals;
            Vector3[] clothingNormals = clothingMesh.normals;

            List<Vector2> bodyUV0 = new List<Vector2>(); bodyMesh.GetUVs(0, bodyUV0);
            List<Vector2> clothingUV0 = new List<Vector2>(); clothingMesh.GetUVs(0, clothingUV0);

            // Transform clothing vertices to body's local space
            Matrix4x4 clothingToBody = data.targetBodyMesh.transform.worldToLocalMatrix * data.clothingMesh.transform.localToWorldMatrix;
            Vector3[] transformedClothingVertices = new Vector3[clothingVertices.Length];
            Vector3[] transformedClothingNormals = new Vector3[clothingNormals.Length];

            for (int i = 0; i < clothingVertices.Length; i++)
            {
                transformedClothingVertices[i] = clothingToBody.MultiplyPoint3x4(clothingVertices[i]);
                transformedClothingNormals[i] = clothingToBody.MultiplyVector(clothingNormals[i]).normalized;
            }

            // Combine vertex data
            Vector3[] newVertices = bodyVertices.Concat(transformedClothingVertices).ToArray();
            Vector3[] newNormals = bodyNormals.Concat(transformedClothingNormals).ToArray();
            Vector2[] newUV0 = bodyUV0.Concat(clothingUV0).ToArray();

            // Create UV1 for UDIM discard
            Vector2[] newUV1 = new Vector2[newVertices.Length];
            Array.Copy(newUV0, newUV1, newUV0.Length); // Start with UV0 data

            // Move clothing UVs in UV1 to the discard tile
            float uOffset = data.udimDiscardColumn;
            float vOffset = data.udimDiscardRow;

            for (int i = bodyVertices.Length; i < newVertices.Length; i++)
            {
                newUV1[i] = new Vector2(newUV1[i].x + uOffset, newUV1[i].y + vOffset);
            }

            combinedMesh.vertices = newVertices;
            combinedMesh.normals = newNormals;
            combinedMesh.SetUVs(0, newUV0);
            combinedMesh.SetUVs(1, newUV1);

            // Combine submeshes and triangles
            List<Material> combinedMaterials = new List<Material>();
            combinedMaterials.AddRange(data.targetBodyMesh.sharedMaterials);
            combinedMaterials.AddRange(data.clothingMesh.sharedMaterials);

            combinedMesh.subMeshCount = combinedMaterials.Count;

            int currentVertexOffset = 0;
            for (int i = 0; i < data.targetBodyMesh.sharedMesh.subMeshCount; i++)
            {
                combinedMesh.SetTriangles(data.targetBodyMesh.sharedMesh.GetTriangles(i), i);
            }
            currentVertexOffset += bodyVertices.Length;

            for (int i = 0; i < data.clothingMesh.sharedMesh.subMeshCount; i++)
            {
                int[] clothingTriangles = data.clothingMesh.sharedMesh.GetTriangles(i);
                int[] transformedTriangles = new int[clothingTriangles.Length];
                for (int j = 0; j < clothingTriangles.Length; j++)
                {
                    transformedTriangles[j] = clothingTriangles[j] + currentVertexOffset;
                }
                combinedMesh.SetTriangles(transformedTriangles, data.targetBodyMesh.sharedMesh.subMeshCount + i);
            }

            // Copy bone weights
            BoneWeight[] bodyWeights = bodyMesh.boneWeights;
            BoneWeight[] clothingWeights = clothingMesh.boneWeights;
            BoneWeight[] combinedWeights = bodyWeights.Concat(clothingWeights).ToArray();
            combinedMesh.boneWeights = combinedWeights;
            combinedMesh.bindposes = bodyMesh.bindposes;

            // Update materials on the body renderer
            data.targetBodyMesh.sharedMaterials = combinedMaterials.ToArray();

            // Disable original clothing renderer
            data.clothingMesh.gameObject.SetActive(false);

            Debug.Log($"[UVDiscardToggle] Merged '{data.clothingMesh.name}' into '{data.targetBodyMesh.name}'. New vertex count: {combinedMesh.vertexCount}", data);
            return combinedMesh;
        }

        private Material ConfigureBodyMaterialForUDIM(UVDiscardToggleData data)
        {
            Material[] materials = data.targetBodyMesh.sharedMaterials;
            Material poiyomiMaterial = null;
            int poiyomiMaterialIndex = -1;

            for (int i = 0; i < materials.Length; i++)
            {
                if (UDIMManipulator.IsPoiyomiWithUDIMSupport(materials[i]))
                {
                    poiyomiMaterial = materials[i];
                    poiyomiMaterialIndex = i;
                    break;
                }
            }

            if (poiyomiMaterial == null)
            {
                Debug.LogError($"[UVDiscardToggle] No Poiyomi or FastFur material found on body mesh '{data.targetBodyMesh.name}'. UDIM discard cannot be configured.", data);
                return null;
            }
            
            string shaderName = UDIMManipulator.GetShaderDisplayName(poiyomiMaterial);
            Debug.Log($"[UVDiscardToggle] Using {shaderName} shader for UDIM discard", data);

            Material materialCopy = UnityEngine.Object.Instantiate(poiyomiMaterial);
            materialCopy.name = poiyomiMaterial.name + "_UVDiscardToggle";

            string shaderNameLower = materialCopy.shader.name.ToLower();
            
            materialCopy.SetFloat("_EnableUDIMDiscardOptions", 1f);
            
            // Enable appropriate shader keyword
            if (shaderNameLower.Contains("poiyomi"))
            {
                materialCopy.EnableKeyword("POI_UDIMDISCARD");
            }
            else if (shaderNameLower.Contains("fastfur") || shaderNameLower.Contains("wffs"))
            {
                materialCopy.EnableKeyword("WFFS_FEATURES_UVDISCARD");
                if (materialCopy.HasProperty("_WFFS_FEATURES_UVDISCARD"))
                {
                    materialCopy.SetFloat("_WFFS_FEATURES_UVDISCARD", 1f);
                }
            }
            
            materialCopy.SetFloat("_UDIMDiscardMode", 0f); // Vertex mode
            materialCopy.SetFloat("_UDIMDiscardUV", data.udimUVChannel);

            string tilePropertyName = $"_UDIMDiscardRow{data.udimDiscardRow}_{data.udimDiscardColumn}";
            if (materialCopy.HasProperty(tilePropertyName))
            {
                materialCopy.SetFloat(tilePropertyName, 0f); // Base state (OFF)
                materialCopy.SetOverrideTag(tilePropertyName + "Animated", "1");
                Debug.Log($"[UVDiscardToggle] Configured material for tile ({data.udimDiscardRow}, {data.udimDiscardColumn})", data);
            }
            else
            {
                Debug.LogWarning($"[UVDiscardToggle] Material property '{tilePropertyName}' not found.", data);
            }

            materials[poiyomiMaterialIndex] = materialCopy;
            data.targetBodyMesh.sharedMaterials = materials;
            EditorUtility.SetDirty(materialCopy);

            return materialCopy;
        }

        private void CreateVRCFuryToggle(UVDiscardToggleData data, Material poiyomiMaterial)
        {
            var toggle = FuryComponents.CreateToggle(data.gameObject);

            bool hasMenuPath = !string.IsNullOrEmpty(data.menuPath);
            bool hasGlobalParam = !string.IsNullOrEmpty(data.globalParameter);

            if (hasMenuPath)
            {
                toggle.SetMenuPath(data.menuPath);
            }
            else if (hasGlobalParam)
            {
                // Global parameter only - disable menu item creation
                var toggleType = toggle.GetType();
                var cField = toggleType.GetField("c", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var toggleModel = cField.GetValue(toggle);
                var addMenuItemField = toggleModel.GetType().GetField("addMenuItem", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (addMenuItemField != null)
                {
                    addMenuItemField.SetValue(toggleModel, false);
                    Debug.Log($"[UVDiscardToggle] Global parameter only mode - menu item disabled", data);
                }
            }

            if (data.saved) toggle.SetSaved();
            if (data.defaultOn) toggle.SetDefaultOn();
            if (data.slider) toggle.SetSlider();
            if (data.holdButton)
            {
                var toggleType = toggle.GetType();
                var cField = toggleType.GetField("c", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var toggleModel = cField.GetValue(toggle);
                var holdButtonField = toggleModel.GetType().GetField("holdButton", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (holdButtonField != null) holdButtonField.SetValue(toggleModel, true);
            }
            if (data.securityEnabled)
            {
                var toggleType = toggle.GetType();
                var cField = toggleType.GetField("c", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var toggleModel = cField.GetValue(toggle);
                var securityEnabledField = toggleModel.GetType().GetField("securityEnabled", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (securityEnabledField != null) securityEnabledField.SetValue(toggleModel, true);
            }
            if (data.enableExclusiveTag && !string.IsNullOrEmpty(data.exclusiveTag))
            {
                toggle.AddExclusiveTag(data.exclusiveTag);
            }
            if (data.enableIcon && data.icon != null)
            {
                var toggleType = toggle.GetType();
                var cField = toggleType.GetField("c", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var toggleModel = cField.GetValue(toggle);
                var enableIconField = toggleModel.GetType().GetField("enableIcon", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                var iconField = toggleModel.GetType().GetField("icon", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (enableIconField != null && iconField != null)
                {
                    enableIconField.SetValue(toggleModel, true);
                    // Use reflection to create GuidTexture2d instance via implicit operator
                    var guidTexture2dType = iconField.FieldType;
                    var implicitOp = guidTexture2dType.GetMethod("op_Implicit", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, null, new Type[] { typeof(Texture2D) }, null);
                    if (implicitOp != null)
                    {
                        var guidTextureInstance = implicitOp.Invoke(null, new object[] { data.icon });
                        iconField.SetValue(toggleModel, guidTextureInstance);
                    }
                }
            }

            if (hasGlobalParam) toggle.SetGlobalParameter(data.globalParameter);

            var actions = toggle.GetActions();
            AnimationClip toggleAnimation = CreateToggleAnimation(data, poiyomiMaterial);

            if (toggleAnimation != null)
            {
                actions.AddAnimationClip(toggleAnimation);
                var toggleType = toggle.GetType();
                var cField = toggleType.GetField("c", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var toggleModel = cField.GetValue(toggle);
                var stateField = toggleModel.GetType().GetField("state", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                var state = stateField.GetValue(toggleModel);
                var actionsField = state.GetType().GetField("actions", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                var actionsList = actionsField.GetValue(state) as System.Collections.IList;

                if (actionsList != null && actionsList.Count > 0)
                {
                    var lastAction = actionsList[actionsList.Count - 1];
                    var motionField = lastAction.GetType().GetField("motion", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    if (motionField != null)
                    {
                        motionField.SetValue(lastAction, toggleAnimation);
                    }
                }
            }
        }

        private AnimationClip CreateToggleAnimation(UVDiscardToggleData data, Material poiyomiMaterial)
        {
            AnimationClip clip = new AnimationClip();
            clip.name = $"UVDiscardToggle_{data.gameObject.name}";

            string rendererPath = GetRelativePath(data.targetBodyMesh.transform, data.transform.root);
            string tilePropertyName = $"_UDIMDiscardRow{data.udimDiscardRow}_{data.udimDiscardColumn}";

            if (!poiyomiMaterial.HasProperty(tilePropertyName))
            {
                Debug.LogError($"[UVDiscardToggle] Material doesn't have '{tilePropertyName}' property", data);
                return null;
            }

            float animValue = 1f; // Discard ON (clothing hidden)
            AnimationCurve discardCurve = new AnimationCurve();
            discardCurve.AddKey(0f, animValue);
            discardCurve.AddKey(1f / 60f, animValue);

            string propertyPath = $"material.{tilePropertyName}";
            EditorCurveBinding binding = EditorCurveBinding.FloatCurve(
                rendererPath,
                typeof(SkinnedMeshRenderer),
                propertyPath
            );
            AnimationUtility.SetEditorCurve(clip, binding, discardCurve);

            if (data.debugSaveAnimation)
            {
                string debugPath = $"Assets/Generated/YUCP_UVDiscardToggle_{data.gameObject.name}.anim";
                AssetDatabase.CreateAsset(clip, debugPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"[UVDiscardToggle] Animation saved to: {debugPath}", data);
            }

            return clip;
        }

        private string GetRelativePath(Transform target, Transform root)
        {
            if (target == root) return "";
            List<string> path = new List<string>();
            Transform current = target;
            while (current != null && current != root)
            {
                path.Insert(0, current.name);
                current = current.parent;
            }
            return string.Join("/", path);
        }
    }
}

