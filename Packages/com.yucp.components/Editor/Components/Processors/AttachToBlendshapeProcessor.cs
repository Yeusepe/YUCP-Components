using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using VRC.SDKBase.Editor.BuildPipeline;
using VRC.SDK3.Avatars.Components;
using com.vrcfury.api;
using YUCP.Components;
using YUCP.Components.Editor.MeshUtils;
using YUCP.Components.Editor.UI;
using YUCP.Components.Editor.Utils;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Processes Attach to Blendshape components during avatar build.
    /// Detects surface clusters, samples blendshape deformations, solves transforms,
    /// generates animation clips, and creates VRCFury components for dynamic positioning.
    /// </summary>
    public class AttachToBlendshapeProcessor : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MinValue + 10;


        public bool OnPreprocessAvatar(GameObject avatarRoot)
        {
            var dataList = avatarRoot.GetComponentsInChildren<AttachToBlendshapeData>(true);

            if (dataList.Length == 0)
            {
                return true;
            }

            var progressWindow = YUCPProgressWindow.Create();
            progressWindow.Progress(0, "Processing blendshape attachments...");

            try
            {
                var animator = avatarRoot.GetComponentInChildren<Animator>();
                if (animator == null)
                {
                    Debug.LogError("[AttachToBlendshapeProcessor] No Animator found on avatar");
                    progressWindow.CloseWindow();
                    return true;
                }

                for (int i = 0; i < dataList.Length; i++)
                {
                    var data = dataList[i];

                    if (!ValidateData(data))
                    {
                        Debug.LogError($"[AttachToBlendshapeProcessor] Validation failed for '{data.name}'", data);
                        continue;
                    }

                    try
                    {
                        ProcessAttachment(data, avatarRoot, animator);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[AttachToBlendshapeProcessor] Error processing '{data.name}': {ex.Message}", data);
                        Debug.LogException(ex);
                    }

                    float progress = (float)(i + 1) / dataList.Length;
                    progressWindow.Progress(progress, $"Processed blendshape attachment {i + 1}/{dataList.Length}");
                }

                progressWindow.CloseWindow();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AttachToBlendshapeProcessor] Fatal error: {ex.Message}");
                progressWindow.CloseWindow();
                return false;
            }

            return true;
        }

        private bool ValidateData(AttachToBlendshapeData data)
        {
            if (data.targetMesh == null)
            {
                Debug.LogError("[AttachToBlendshapeProcessor] Target mesh is not set", data);
                return false;
            }

            if (data.targetMesh.sharedMesh == null)
            {
                Debug.LogError("[AttachToBlendshapeProcessor] Target mesh has no mesh data", data);
                return false;
            }

            if (!PoseSampler.HasBlendshapes(data.targetMesh))
            {
                Debug.LogError("[AttachToBlendshapeProcessor] Target mesh has no blendshapes", data);
                return false;
            }

            if (data.trackingMode == BlendshapeTrackingMode.Specific && 
                (data.specificBlendshapes == null || data.specificBlendshapes.Count == 0))
            {
                Debug.LogError("[AttachToBlendshapeProcessor] Specific mode requires at least one blendshape name", data);
                return false;
            }

            return true;
        }

        private void ProcessAttachment(AttachToBlendshapeData data, GameObject avatarRoot, Animator animator)
        {
            if (data.debugMode)
            {
                Debug.Log($"[AttachToBlendshapeProcessor] Processing attachment for '{data.name}'", data);
            }

            // Step 1: Detect surface cluster
            SurfaceCluster cluster = SurfaceClusterDetector.DetectCluster(
                data.targetMesh,
                data.transform.position,
                data.clusterTriangleCount,
                data.searchRadius,
                data.manualTriangleIndex);

            if (cluster == null)
            {
                Debug.LogError($"[AttachToBlendshapeProcessor] Failed to detect surface cluster for '{data.name}'", data);
                return;
            }

            if (data.debugMode)
            {
                Debug.Log($"[AttachToBlendshapeProcessor] Detected cluster with {cluster.anchors.Count} triangles", data);
            }

            // Step 2: Determine which blendshapes to track
            List<string> blendshapesToTrack = DetermineBlendshapesToTrack(data, avatarRoot, cluster);

            if (blendshapesToTrack.Count == 0)
            {
                Debug.LogWarning($"[AttachToBlendshapeProcessor] No blendshapes to track for '{data.name}'", data);
                return;
            }

            Debug.Log($"[AttachToBlendshapeProcessor] Tracking {blendshapesToTrack.Count} blendshapes: {string.Join(", ", blendshapesToTrack)}", data);

            // Step 3: Create base bone attachment
            string bonePath = "";
            if (data.attachToClosestBone)
            {
                bonePath = AttachToClosestBone(data, animator);
                if (data.debugMode)
                {
                    Debug.Log($"[AttachToBlendshapeProcessor] Attached to bone: '{bonePath}'", data);
                }
            }

            // Step 4: Transfer blendshapes to target mesh
            bool transferSuccess = BlendshapeTransfer.TransferBlendshapes(
                data.targetMesh,
                data.targetMeshToModify,
                blendshapesToTrack,
                cluster,
                data);

            if (!transferSuccess)
            {
                Debug.LogError($"[AttachToBlendshapeProcessor] Failed to transfer blendshapes for '{data.name}'", data);
                return;
            }

            // Step 5: Create transform blendshapes and link them using VRCFury's BlendShapeLink
            CreateTransformBlendshapesAndLink(data, blendshapesToTrack, avatarRoot);

            // Step 6: Set build statistics
            data.SetBuildStats(cluster, blendshapesToTrack, blendshapesToTrack.Count, bonePath);

            Debug.Log($"[AttachToBlendshapeProcessor] Successfully processed '{data.name}': " +
                     $"Transferred {blendshapesToTrack.Count} blendshapes, {cluster.anchors.Count} triangle cluster", data);
        }

        private List<string> DetermineBlendshapesToTrack(
            AttachToBlendshapeData data,
            GameObject avatarRoot,
            SurfaceCluster cluster)
        {
            List<string> blendshapes = new List<string>();
            Mesh mesh = data.targetMesh.sharedMesh;

            switch (data.trackingMode)
            {
                case BlendshapeTrackingMode.All:
                    blendshapes = PoseSampler.GetAllBlendshapeNames(mesh);
                    Debug.Log($"[AttachToBlendshapeProcessor] All mode: tracking {blendshapes.Count} blendshapes");
                    break;

                case BlendshapeTrackingMode.Specific:
                    blendshapes = new List<string>(data.specificBlendshapes);
                    // Validate that they exist
                    blendshapes = blendshapes.Where(name => mesh.GetBlendShapeIndex(name) >= 0).ToList();
                    Debug.Log($"[AttachToBlendshapeProcessor] Specific mode: tracking {blendshapes.Count} blendshapes");
                    break;

                case BlendshapeTrackingMode.VisemsOnly:
                    blendshapes = VRChatVisemeDetector.GetVisemeBlendshapes(data.targetMesh, avatarRoot);
                    Debug.Log($"[AttachToBlendshapeProcessor] Viseme mode: tracking {blendshapes.Count} viseme blendshapes");
                    break;

                case BlendshapeTrackingMode.Smart:
                    blendshapes = VRChatVisemeDetector.DetectActiveBlendshapes(
                        data.targetMesh,
                        cluster,
                        data.smartDetectionThreshold);
                    Debug.Log($"[AttachToBlendshapeProcessor] Smart mode: detected {blendshapes.Count} active blendshapes");
                    break;
            }

            return blendshapes;
        }

        private string AttachToClosestBone(AttachToBlendshapeData data, Animator animator)
        {
            // Find all bones
            List<Transform> allBones = FindAllBones(animator, data.transform);

            // Filter bones
            List<Transform> filteredBones = FilterBones(allBones, data, animator);

            if (filteredBones.Count == 0)
            {
                Debug.LogWarning($"[AttachToBlendshapeProcessor] No bones found for '{data.name}'", data);
                return "";
            }

            // Find closest bone
            Transform closestBone = FindClosestBone(data.transform, filteredBones, data.boneSearchRadius);

            if (closestBone == null)
            {
                Debug.LogWarning($"[AttachToBlendshapeProcessor] No bone within range for '{data.name}'", data);
                return "";
            }

            // Get bone path
            string bonePath = GetBonePath(closestBone, animator.transform);

            // Create VRCFury armature link
            var link = FuryComponents.CreateArmatureLink(data.gameObject);
            if (link == null)
            {
                Debug.LogError($"[AttachToBlendshapeProcessor] Failed to create armature link for '{data.name}'", data);
                return bonePath;
            }

            // Link to bone
            if (!string.IsNullOrEmpty(data.boneOffset))
            {
                link.LinkTo(bonePath + "/" + data.boneOffset);
            }
            else
            {
                link.LinkTo(bonePath);
            }

            float distance = Vector3.Distance(data.transform.position, closestBone.position);
            Debug.Log($"[AttachToBlendshapeProcessor] Linked '{data.name}' to bone '{bonePath}' (distance: {distance:F3}m)", data);

            return bonePath;
        }


        private string GetRelativePath(Transform target, Transform root)
        {
            if (target == root)
                return "";

            List<string> path = new List<string>();
            Transform current = target;

            while (current != null && current != root)
            {
                path.Insert(0, current.name);
                current = current.parent;
            }

            return string.Join("/", path);
        }

        // Bone finding utilities (similar to AttachToClosestBoneProcessor)
        private List<Transform> FindAllBones(Animator animator, Transform exclude)
        {
            var bones = new List<Transform>();
            CollectBonesRecursive(animator.transform, bones, exclude);
            return bones;
        }

        private void CollectBonesRecursive(Transform current, List<Transform> bones, Transform exclude)
        {
            if (current == exclude || IsDescendantOf(current, exclude))
            {
                return;
            }

            if (current.GetComponent<Animator>() == null)
            {
                bones.Add(current);
            }

            for (int i = 0; i < current.childCount; i++)
            {
                CollectBonesRecursive(current.GetChild(i), bones, exclude);
            }
        }

        private bool IsDescendantOf(Transform child, Transform parent)
        {
            Transform current = child;
            while (current != null)
            {
                if (current == parent)
                {
                    return true;
                }
                current = current.parent;
            }
            return false;
        }

        private List<Transform> FilterBones(List<Transform> bones, AttachToBlendshapeData data, Animator animator)
        {
            var filtered = new List<Transform>();

            foreach (var bone in bones)
            {
                if (data.ignoreHumanoidBones && IsHumanoidBone(bone, animator))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(data.boneNameFilter))
                {
                    if (!bone.name.ToLower().Contains(data.boneNameFilter.ToLower()))
                    {
                        continue;
                    }
                }

                filtered.Add(bone);
            }

            return filtered;
        }

        private bool IsHumanoidBone(Transform bone, Animator animator)
        {
            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                var humanBone = (HumanBodyBones)i;
                var humanTransform = animator.GetBoneTransform(humanBone);
                if (humanTransform == bone)
                {
                    return true;
                }
            }
            return false;
        }

        private Transform FindClosestBone(Transform target, List<Transform> bones, float maxDistance)
        {
            Transform closest = null;
            float closestDistance = float.MaxValue;

            foreach (var bone in bones)
            {
                float distance = Vector3.Distance(target.position, bone.position);

                if (maxDistance > 0 && distance > maxDistance)
                {
                    continue;
                }

                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closest = bone;
                }
            }

            return closest;
        }

        private string GetBonePath(Transform bone, Transform root)
        {
            var pathParts = new List<string>();
            Transform current = bone;

            while (current != null && current != root)
            {
                pathParts.Insert(0, current.name);
                current = current.parent;
            }

            return string.Join("/", pathParts);
        }

        /// <summary>
        /// Creates blendshapes on the ATTACHED OBJECT mesh which represent the solved rigid motion at each weight.
        /// Then uses VRCFury's BlendShapeLink to mirror the base mesh's blendshape weights onto these new shapes.
        ///
        /// Key detail: Unity's Mesh.AddBlendShapeFrame expects DELTA vertices, not absolute vertices.
        /// Each frame we compute v' = R(w) * v + T(w) (about the object pivot/origin, like Preview),
        /// then delta = v' - v and store that as the frame.
        /// </summary>
        private void CreateTransformBlendshapesAndLink(AttachToBlendshapeData data, List<string> blendshapesToTrack, GameObject avatarRoot)
        {
            // Ensure the attached object is rendered by a SkinnedMeshRenderer so blendshapes can drive it.
            SkinnedMeshRenderer targetMeshRenderer = data.transform.GetComponent<SkinnedMeshRenderer>();
            Mesh targetMesh = null;

            if (targetMeshRenderer == null) {
                var meshFilter = data.transform.GetComponent<MeshFilter>();
                var meshRenderer = data.transform.GetComponent<MeshRenderer>();
                if (meshFilter == null || meshFilter.sharedMesh == null) {
                    Debug.LogError($"[AttachToBlendshapeProcessor] Target object '{data.transform.name}' has no mesh to bake transform blendshapes into (needs MeshFilter or SkinnedMeshRenderer).", data);
                    return;
                }

                // Convert MeshFilter/MeshRenderer -> SkinnedMeshRenderer (so blendshapes can apply).
                targetMeshRenderer = data.transform.gameObject.AddComponent<SkinnedMeshRenderer>();
                targetMeshRenderer.sharedMesh = meshFilter.sharedMesh;
                if (meshRenderer != null) {
                    targetMeshRenderer.sharedMaterials = meshRenderer.sharedMaterials;
                    meshRenderer.enabled = false; // prevent double-render
                }

                // Minimal skinning setup: one bone at self, so it renders like a normal mesh.
                targetMeshRenderer.rootBone = data.transform;
                targetMeshRenderer.bones = new[] { data.transform };
                targetMeshRenderer.updateWhenOffscreen = true;
            }

            targetMesh = targetMeshRenderer.sharedMesh;
            if (targetMesh == null) {
                Debug.LogError($"[AttachToBlendshapeProcessor] Target SkinnedMeshRenderer '{targetMeshRenderer.name}' has no mesh", data);
                return;
            }

            // Always work on a copy so we don't permanently mutate an imported/asset mesh and so repeated builds don't stack shapes.
            // (Unity doesn't allow removing blendshapes from a Mesh, so we replace the mesh each build.)
            var targetMeshCopy = UnityEngine.Object.Instantiate(targetMesh);
            targetMeshCopy.name = $"{targetMesh.name}_YUCP_AttachToBlendshape";
            targetMeshRenderer.sharedMesh = targetMeshCopy;
            targetMesh = targetMeshCopy;

            // Get base mesh path for VRCFury
            string baseMeshPath = AnimationUtility.CalculateTransformPath(data.targetMesh.transform, avatarRoot.transform);
            string baseMeshName = data.targetMesh.transform.name;

            // Create blendshapes on target mesh that represent the solved rigid motion
            Dictionary<string, string> blendshapeMappings = new Dictionary<string, string>();

            // Cache base vertices once; each frame uses deltas against this base.
            Vector3[] baseVertices = targetMesh.vertices;
            int vertexCount = baseVertices.Length;
            float maxTranslation = 0f;

            foreach (string blendshapeName in blendshapesToTrack)
            {
                var samples = BlendshapeTransfer.GetTransformSamples(blendshapeName);
                if (samples.Count == 0)
                    continue;

                // Create a blendshape name for the transform
                string transformBlendshapeName = $"_YUCP_Transform_{blendshapeName}";
                blendshapeMappings[blendshapeName] = transformBlendshapeName;

                // Use the exact sampled weights from the solver (matches Preview sampling density).
                var sortedSamples = samples.OrderBy(s => s.blendshapeWeight).ToList();

                foreach (var sample in sortedSamples) {
                    // Build deltaVertices for this frame: delta = (R * v + T) - v
                    // This simulates changing the object transform about its pivot (origin), like Preview does.
                    var deltaVertices = new Vector3[vertexCount];
                    var rot = sample.rotationDelta;
                    var pos = sample.positionDelta;
                    maxTranslation = Mathf.Max(maxTranslation, pos.magnitude);

                    for (int i = 0; i < vertexCount; i++) {
                        var v = baseVertices[i];
                        var v2 = (rot * v) + pos;
                        deltaVertices[i] = v2 - v;
                    }

                    // Add blendshape frame at the sampled weight.
                    targetMesh.AddBlendShapeFrame(transformBlendshapeName, sample.blendshapeWeight, deltaVertices, null, null);
                }
            }

            // Ensure bounds won't cull the mesh when vertices translate due to blendshape frames.
            // SkinnedMeshRenderer uses localBounds for culling.
            targetMesh.RecalculateBounds();
            var b = targetMesh.bounds;
            var expand = maxTranslation * 2f + 0.05f;
            b.extents += new Vector3(expand, expand, expand);
            targetMeshRenderer.localBounds = b;

            // Get or create VRCFury component on avatar root
            var vrcFuryType = System.Type.GetType("VF.Model.VRCFury, VRCFury");
            if (vrcFuryType == null)
            {
                Debug.LogError($"[AttachToBlendshapeProcessor] Could not find VRCFury type", data);
                return;
            }
            
            var vrcFury = avatarRoot.GetComponent(vrcFuryType);
            if (vrcFury == null)
            {
                vrcFury = avatarRoot.AddComponent(vrcFuryType);
            }

            // Add BlendShapeLink feature using reflection (since it's internal)
            AddBlendShapeLinkFeature(vrcFury, baseMeshName, targetMeshRenderer, blendshapeMappings, data, avatarRoot, vrcFuryType);

            // Create runtime component to read blendshape weights and apply transforms
            var runtimeComponent = data.GetComponent<AttachToBlendshapeRuntime>();
            if (runtimeComponent == null)
            {
                runtimeComponent = data.gameObject.AddComponent<AttachToBlendshapeRuntime>();
            }

            // Set up runtime component to read from the target mesh's blendshapes
            Dictionary<string, List<AttachToBlendshapeRuntime.TransformSample>> samplesDict = 
                new Dictionary<string, List<AttachToBlendshapeRuntime.TransformSample>>();

            foreach (string blendshapeName in blendshapesToTrack)
            {
                var editorSamples = BlendshapeTransfer.GetTransformSamples(blendshapeName);
                if (editorSamples.Count > 0)
                {
                    var runtimeSamples = editorSamples.Select(s => new AttachToBlendshapeRuntime.TransformSample
                    {
                        blendshapeWeight = s.blendshapeWeight,
                        positionDelta = s.positionDelta,
                        rotationDelta = new Vector4(s.rotationDelta.x, s.rotationDelta.y, 
                                                  s.rotationDelta.z, s.rotationDelta.w)
                    }).ToList();

                    samplesDict[blendshapeName] = runtimeSamples;
                }
            }

            runtimeComponent.SetBlendshapeData(targetMeshRenderer, samplesDict);

            if (data.debugMode)
            {
                Debug.Log($"[AttachToBlendshapeProcessor] Created {blendshapeMappings.Count} transform blendshapes (multi-frame deltas) and linked them via VRCFury BlendShapeLink", data);
            }
        }

        /// <summary>
        /// Adds a VRCFury BlendShapeLink feature using reflection (since the class is internal).
        /// </summary>
        private void AddBlendShapeLinkFeature(object vrcFury, string baseMeshName, 
                                            SkinnedMeshRenderer targetMesh, 
                                            Dictionary<string, string> mappings,
                                            AttachToBlendshapeData data,
                                            GameObject avatarRoot,
                                            System.Type vrcFuryType)
        {
            try
            {
                // Use reflection to create and add BlendShapeLink feature
                var blendShapeLinkType = System.Type.GetType("VF.Model.Feature.BlendShapeLink, VRCFury");
                if (blendShapeLinkType == null)
                {
                    Debug.LogWarning($"[AttachToBlendshapeProcessor] Could not find BlendShapeLink type. Transform blendshapes created but not linked.", data);
                    return;
                }

                var linkFeature = System.Activator.CreateInstance(blendShapeLinkType);
                
                // Set base mesh name
                var baseObjField = blendShapeLinkType.GetField("baseObj");
                if (baseObjField != null)
                {
                    baseObjField.SetValue(linkFeature, baseMeshName);
                }

                // Set link skins
                var linkSkinsField = blendShapeLinkType.GetField("linkSkins");
                if (linkSkinsField != null)
                {
                    var linkSkinListType = System.Type.GetType("VF.Model.Feature.BlendShapeLink+LinkSkin, VRCFury");
                    var linkSkin = System.Activator.CreateInstance(linkSkinListType);
                    var rendererField = linkSkinListType.GetField("renderer");
                    if (rendererField != null)
                    {
                        rendererField.SetValue(linkSkin, targetMesh);
                    }

                    var linkSkinsList = System.Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(linkSkinListType));
                    var addMethod = linkSkinsList.GetType().GetMethod("Add");
                    addMethod.Invoke(linkSkinsList, new object[] { linkSkin });
                    linkSkinsField.SetValue(linkFeature, linkSkinsList);
                }

                // Set includes (mappings)
                var includesField = blendShapeLinkType.GetField("includes");
                if (includesField != null)
                {
                    var includeType = System.Type.GetType("VF.Model.Feature.BlendShapeLink+Include, VRCFury");
                    var includesList = System.Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(includeType));
                    var addMethod = includesList.GetType().GetMethod("Add");

                    foreach (var mapping in mappings)
                    {
                        var include = System.Activator.CreateInstance(includeType);
                        var nameOnBaseField = includeType.GetField("nameOnBase");
                        var nameOnLinkedField = includeType.GetField("nameOnLinked");
                        if (nameOnBaseField != null) nameOnBaseField.SetValue(include, mapping.Key);
                        if (nameOnLinkedField != null) nameOnLinkedField.SetValue(include, mapping.Value);
                        addMethod.Invoke(includesList, new object[] { include });
                    }
                    includesField.SetValue(linkFeature, includesList);
                }

                // Set includeAll to false since we're using specific includes
                var includeAllField = blendShapeLinkType.GetField("includeAll");
                if (includeAllField != null)
                {
                    includeAllField.SetValue(linkFeature, false);
                }

                // Add to VRCFury - create a new VRCFury component for this feature
                // VRCFury's new system uses one feature per component
                var newVrcFury = avatarRoot.AddComponent(vrcFuryType);
                var contentField = vrcFuryType.GetField("content", 
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (contentField != null)
                {
                    contentField.SetValue(newVrcFury, linkFeature);
                    if (data.debugMode)
                    {
                        Debug.Log($"[AttachToBlendshapeProcessor] Created VRCFury BlendShapeLink feature with {mappings.Count} mappings", data);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[AttachToBlendshapeProcessor] Failed to add BlendShapeLink feature: {ex.Message}", data);
                Debug.LogException(ex, data);
            }
        }

        /// <summary>
        /// Creates a runtime component that monitors blendshape weights and applies transforms.
        /// This works for ALL blendshape animations, including visemes and parameter-driven blendshapes.
        /// </summary>
        private void CreateRuntimeComponent(AttachToBlendshapeData data, List<string> blendshapesToTrack)
        {
            // Get or create the runtime component
            var runtimeComponent = data.GetComponent<AttachToBlendshapeRuntime>();
            if (runtimeComponent == null)
            {
                runtimeComponent = data.gameObject.AddComponent<AttachToBlendshapeRuntime>();
            }

            // Collect transform samples for all blendshapes
            Dictionary<string, List<AttachToBlendshapeRuntime.TransformSample>> samples = 
                new Dictionary<string, List<AttachToBlendshapeRuntime.TransformSample>>();

            foreach (string blendshapeName in blendshapesToTrack)
            {
                var editorSamples = BlendshapeTransfer.GetTransformSamples(blendshapeName);
                if (editorSamples.Count > 0)
                {
                    var runtimeSamples = editorSamples.Select(s => new AttachToBlendshapeRuntime.TransformSample
                    {
                        blendshapeWeight = s.blendshapeWeight,
                        positionDelta = s.positionDelta,
                        rotationDelta = new Vector4(s.rotationDelta.x, s.rotationDelta.y, 
                                                  s.rotationDelta.z, s.rotationDelta.w)
                    }).ToList();

                    samples[blendshapeName] = runtimeSamples;
                }
            }

            // Set the data on the runtime component
            runtimeComponent.SetBlendshapeData(data.targetMesh, samples);

            if (data.debugMode)
            {
                Debug.Log($"[AttachToBlendshapeProcessor] Created runtime component with {samples.Count} blendshapes", data);
            }
        }

        /// <summary>
        /// Creates transform animation curves that sync with blendshape animations.
        /// Finds all animation clips that control the base mesh's blendshapes and adds
        /// corresponding transform curves to make the object move/rotate with the blendshapes.
        /// </summary>
        private void CreateTransformAnimations(
            AttachToBlendshapeData data,
            List<string> blendshapesToTrack,
            GameObject avatarRoot,
            Animator animator)
        {
            if (data.targetMesh == null || data.targetMesh.sharedMesh == null)
            {
                Debug.LogWarning($"[AttachToBlendshapeProcessor] Cannot create transform animations - source mesh is null", data);
                return;
            }

            // Get paths for animation bindings
            // Try multiple path formats since Unity/VRCFury might use different formats
            string baseMeshPath = AnimationUtility.CalculateTransformPath(data.targetMesh.transform, avatarRoot.transform);
            string objectPath = AnimationUtility.CalculateTransformPath(data.transform, avatarRoot.transform);
            
            // Also try path without root name (VRCFury sometimes uses this)
            string baseMeshPathNoRoot = baseMeshPath;
            if (baseMeshPath.Contains("/"))
            {
                var parts = baseMeshPath.Split('/');
                if (parts.Length > 1)
                {
                    baseMeshPathNoRoot = string.Join("/", parts.Skip(1));
                }
            }

            if (string.IsNullOrEmpty(baseMeshPath) || string.IsNullOrEmpty(objectPath))
            {
                Debug.LogWarning($"[AttachToBlendshapeProcessor] Failed to get paths for animation bindings. Base: '{baseMeshPath}', Object: '{objectPath}'", data);
                return;
            }

            if (data.debugMode)
            {
                Debug.Log($"[AttachToBlendshapeProcessor] Paths - Base mesh: '{baseMeshPath}' (no root: '{baseMeshPathNoRoot}'), Object: '{objectPath}'", data);
            }

            // Get base transform values (for calculating absolute positions from deltas)
            Vector3 baseLocalPosition = data.transform.localPosition;
            Quaternion baseLocalRotation = data.transform.localRotation;

            // Get all controllers from the avatar
            var avatarDescriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (avatarDescriptor == null)
            {
                Debug.LogWarning($"[AttachToBlendshapeProcessor] Avatar descriptor not found", data);
                return;
            }

            // Get all animation controllers from the avatar
            List<AnimatorController> allControllers = new List<AnimatorController>();
            
            // Get base animation layers
            foreach (var layer in avatarDescriptor.baseAnimationLayers)
            {
                if (layer.isDefault) continue;
                if (layer.animatorController is AnimatorController ac)
                {
                    allControllers.Add(ac);
                }
            }
            
            // Get special animation layers
            foreach (var layer in avatarDescriptor.specialAnimationLayers)
            {
                if (layer.isDefault) continue;
                if (layer.animatorController is AnimatorController ac)
                {
                    allControllers.Add(ac);
                }
            }
            
            int curvesAdded = 0;

            // Process each blendshape
            foreach (string blendshapeName in blendshapesToTrack)
            {
                // Get transform samples for this blendshape
                var samples = BlendshapeTransfer.GetTransformSamples(blendshapeName);
                if (samples.Count == 0)
                {
                    if (data.debugMode)
                    {
                        Debug.LogWarning($"[AttachToBlendshapeProcessor] No transform samples found for blendshape '{blendshapeName}'", data);
                    }
                    continue;
                }

                // Create animation curves from samples
                // Curves map blendshape weight (0-100) to transform deltas
                AnimationCurve posX = new AnimationCurve();
                AnimationCurve posY = new AnimationCurve();
                AnimationCurve posZ = new AnimationCurve();
                AnimationCurve rotX = new AnimationCurve();
                AnimationCurve rotY = new AnimationCurve();
                AnimationCurve rotZ = new AnimationCurve();
                AnimationCurve rotW = new AnimationCurve();

                foreach (var sample in samples.OrderBy(s => s.blendshapeWeight))
                {
                    float time = sample.blendshapeWeight / 100f; // Normalize to 0-1

                    // Position curves (deltas from base)
                    posX.AddKey(new Keyframe(time, sample.positionDelta.x));
                    posY.AddKey(new Keyframe(time, sample.positionDelta.y));
                    posZ.AddKey(new Keyframe(time, sample.positionDelta.z));

                    // Rotation curves (deltas from base)
                    rotX.AddKey(new Keyframe(time, sample.rotationDelta.x));
                    rotY.AddKey(new Keyframe(time, sample.rotationDelta.y));
                    rotZ.AddKey(new Keyframe(time, sample.rotationDelta.z));
                    rotW.AddKey(new Keyframe(time, sample.rotationDelta.w));
                }

                // Find all animation clips that control this blendshape
                string blendshapePropertyName = "blendShape." + blendshapeName;
                bool foundAnyClips = false;

                if (data.debugMode)
                {
                    Debug.Log($"[AttachToBlendshapeProcessor] Searching for blendshape '{blendshapeName}' (property: '{blendshapePropertyName}') on path '{baseMeshPath}' in {allControllers.Count} controllers", data);
                }

                foreach (var controller in allControllers)
                {
                    if (data.debugMode)
                    {
                        Debug.Log($"[AttachToBlendshapeProcessor] Checking controller '{controller.name}' with {controller.layers.Length} layers", data);
                    }

                    // Iterate through all clips in this controller
                    foreach (var layer in controller.layers)
                    {
                        if (layer.stateMachine == null) continue;
                        
                        // Get all states from this layer
                        var states = GetAllStates(layer.stateMachine);
                        
                        if (data.debugMode)
                        {
                            Debug.Log($"[AttachToBlendshapeProcessor] Layer '{layer.name}' has {states.Count} states", data);
                        }

                        foreach (var state in states)
                        {
                            if (state.motion == null || !(state.motion is AnimationClip clip))
                                continue;

                            // Get all float bindings from the clip
                            var bindings = AnimationUtility.GetCurveBindings(clip);
                            
                            if (data.debugMode && bindings.Length > 0)
                            {
                                var blendshapeBindings = bindings.Where(b => b.propertyName.StartsWith("blendShape.")).ToArray();
                                if (blendshapeBindings.Length > 0)
                                {
                                    Debug.Log($"[AttachToBlendshapeProcessor] Clip '{clip.name}' has {blendshapeBindings.Length} blendshape bindings. Paths: {string.Join(", ", blendshapeBindings.Select(b => b.path))}", data);
                                }
                            }

                            // Check if this clip has a curve for our blendshape
                            bool hasBlendshapeCurve = false;
                            AnimationCurve blendshapeCurve = null;

                            foreach (var binding in bindings)
                            {
                                // Check for blendshape bindings
                                if (binding.type == typeof(SkinnedMeshRenderer) &&
                                    binding.propertyName.StartsWith("blendShape."))
                                {
                                    // Try multiple path matching strategies
                                    bool pathMatches = binding.path == baseMeshPath || 
                                                       binding.path == baseMeshPathNoRoot ||
                                                       binding.path.EndsWith("/" + baseMeshPath) ||
                                                       binding.path.EndsWith("/" + baseMeshPathNoRoot) ||
                                                       baseMeshPath.EndsWith("/" + binding.path) ||
                                                       baseMeshPathNoRoot.EndsWith("/" + binding.path);

                                    if (pathMatches && binding.propertyName == blendshapePropertyName)
                                    {
                                        blendshapeCurve = AnimationUtility.GetEditorCurve(clip, binding);
                                        if (blendshapeCurve != null)
                                        {
                                            hasBlendshapeCurve = true;
                                            if (data.debugMode)
                                            {
                                                Debug.Log($"[AttachToBlendshapeProcessor] ✓ Found blendshape curve '{blendshapeName}' in clip '{clip.name}' on path '{binding.path}' (expected: '{baseMeshPath}')", data);
                                            }
                                            break;
                                        }
                                    }
                                    else if (data.debugMode && binding.propertyName == blendshapePropertyName)
                                    {
                                        Debug.Log($"[AttachToBlendshapeProcessor] Path mismatch for '{blendshapeName}': binding path '{binding.path}' vs expected '{baseMeshPath}' or '{baseMeshPathNoRoot}'", data);
                                    }
                                }
                            }

                            if (!hasBlendshapeCurve || blendshapeCurve == null)
                                continue;

                        // This clip controls our blendshape - add transform curves
                        // The transform curves should be driven by the same parameter/time as the blendshape curve
                        // We'll remap the curves to match the blendshape curve's keyframe times

                        // Remap transform curves to match blendshape curve timing
                        AnimationCurve remappedPosX = RemapCurveToBlendshapeTiming(posX, blendshapeCurve, baseLocalPosition.x);
                        AnimationCurve remappedPosY = RemapCurveToBlendshapeTiming(posY, blendshapeCurve, baseLocalPosition.y);
                        AnimationCurve remappedPosZ = RemapCurveToBlendshapeTiming(posZ, blendshapeCurve, baseLocalPosition.z);
                        AnimationCurve remappedRotX = RemapCurveToBlendshapeTiming(rotX, blendshapeCurve, baseLocalRotation.x);
                        AnimationCurve remappedRotY = RemapCurveToBlendshapeTiming(rotY, blendshapeCurve, baseLocalRotation.y);
                        AnimationCurve remappedRotZ = RemapCurveToBlendshapeTiming(rotZ, blendshapeCurve, baseLocalRotation.z);
                        AnimationCurve remappedRotW = RemapCurveToBlendshapeTiming(rotW, blendshapeCurve, baseLocalRotation.w);

                        // Add transform curves to the clip using Unity's AnimationUtility
                        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(objectPath, typeof(Transform), "m_LocalPosition.x"), remappedPosX);
                        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(objectPath, typeof(Transform), "m_LocalPosition.y"), remappedPosY);
                        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(objectPath, typeof(Transform), "m_LocalPosition.z"), remappedPosZ);
                        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(objectPath, typeof(Transform), "m_LocalRotation.x"), remappedRotX);
                        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(objectPath, typeof(Transform), "m_LocalRotation.y"), remappedRotY);
                        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(objectPath, typeof(Transform), "m_LocalRotation.z"), remappedRotZ);
                        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(objectPath, typeof(Transform), "m_LocalRotation.w"), remappedRotW);
                        
                        // Mark clip as dirty so changes are saved
                        EditorUtility.SetDirty(clip);

                        foundAnyClips = true;
                        curvesAdded++;

                        if (data.debugMode)
                        {
                            Debug.Log($"[AttachToBlendshapeProcessor] Added transform curves for '{blendshapeName}' to clip '{clip.name}' in controller '{controller.name}'", data);
                        }
                        }
                    }
                }

                if (!foundAnyClips)
                {
                    if (data.debugMode)
                    {
                        Debug.LogWarning($"[AttachToBlendshapeProcessor] No animation clips found that control blendshape '{blendshapeName}' on '{baseMeshPath}'. " +
                                       $"This might be because:\n" +
                                       $"1. The blendshape is controlled by VRChat's built-in systems (visemes, eye tracking, etc.)\n" +
                                       $"2. The path '{baseMeshPath}' doesn't match the animation clip bindings\n" +
                                       $"3. The blendshape is animated via parameters/expressions rather than direct animation clips\n" +
                                       $"Transform animations will not be created for this blendshape.", data);
                    }
                    else
                    {
                        Debug.LogWarning($"[AttachToBlendshapeProcessor] No animation clips found for blendshape '{blendshapeName}'. Enable debug mode for details.", data);
                    }
                }
            }

            if (curvesAdded > 0)
            {
                Debug.Log($"[AttachToBlendshapeProcessor] Created transform animations: {curvesAdded} curve sets added to animation clips", data);
            }
        }

        /// <summary>
        /// Recursively gets all states from an AnimatorStateMachine, including nested state machines.
        /// </summary>
        private List<AnimatorState> GetAllStates(AnimatorStateMachine stateMachine)
        {
            var states = new List<AnimatorState>();
            
            if (stateMachine == null)
                return states;
            
            // Add direct states
            states.AddRange(stateMachine.states.Select(s => s.state));
            
            // Recursively add states from nested state machines
            foreach (var subStateMachine in stateMachine.stateMachines)
            {
                states.AddRange(GetAllStates(subStateMachine.stateMachine));
            }
            
            return states;
        }

        /// <summary>
        /// Remaps a transform curve to match the timing of a blendshape curve.
        /// The blendshape curve may have different keyframe times than our sampled curve.
        /// We evaluate our curve at the blendshape curve's keyframe times and create a new curve.
        /// </summary>
        private AnimationCurve RemapCurveToBlendshapeTiming(
            AnimationCurve sourceCurve,
            AnimationCurve blendshapeCurve,
            float baseValue)
        {
            AnimationCurve remapped = new AnimationCurve();

            // Get all keyframe times from the blendshape curve
            HashSet<float> keyframeTimes = new HashSet<float>();
            foreach (var key in blendshapeCurve.keys)
            {
                keyframeTimes.Add(key.time);
            }

            // Also include start and end times
            if (blendshapeCurve.length > 0)
            {
                keyframeTimes.Add(0f);
                keyframeTimes.Add(blendshapeCurve.keys[blendshapeCurve.length - 1].time);
            }

            // Evaluate source curve at each keyframe time and add to remapped curve
            foreach (float time in keyframeTimes.OrderBy(t => t))
            {
                // Evaluate blendshape weight at this time
                // Note: blendshape curves in VRChat typically use 0-1 range, but our samples are 0-100
                float blendshapeWeight = blendshapeCurve.Evaluate(time);
                
                // Normalize blendshape weight to 0-100 range (in case it's 0-1)
                // VRChat blendshape curves are typically 0-1, but we'll handle both
                if (blendshapeWeight <= 1f && blendshapeWeight >= 0f)
                {
                    blendshapeWeight *= 100f; // Convert 0-1 to 0-100
                }
                
                // Map blendshape weight (0-100) to our curve's time (0-1)
                float sourceTime = Mathf.Clamp01(blendshapeWeight / 100f);
                
                // Evaluate our transform curve at the mapped time
                // sourceCurve contains deltas, so we add to base value
                float transformDelta = sourceCurve.Evaluate(sourceTime);
                
                // Add to remapped curve (absolute value = base + delta)
                remapped.AddKey(new Keyframe(time, baseValue + transformDelta));
            }

            // Set tangents for smooth interpolation
            if (remapped.length > 0)
            {
                for (int i = 0; i < remapped.length; i++)
                {
                    AnimationUtility.SetKeyLeftTangentMode(remapped, i, AnimationUtility.TangentMode.Auto);
                    AnimationUtility.SetKeyRightTangentMode(remapped, i, AnimationUtility.TangentMode.Auto);
                }
            }

            return remapped;
        }
    }
}
