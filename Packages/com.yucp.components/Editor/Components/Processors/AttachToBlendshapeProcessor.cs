using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Editor.BuildPipeline;
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

        // Track previous tangents for smoothing across blendshape samples
        private Dictionary<string, Vector3> previousTangents = new Dictionary<string, Vector3>();

        private static readonly Dictionary<string, int> VisemeNameToIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "Viseme_Sil", 0 },
            { "Viseme_PP", 1 },
            { "Viseme_FF", 2 },
            { "Viseme_TH", 3 },
            { "Viseme_DD", 4 },
            { "Viseme_KK", 5 },
            { "Viseme_CH", 6 },
            { "Viseme_SS", 7 },
            { "Viseme_NN", 8 },
            { "Viseme_RR", 9 },
            { "Viseme_AA", 10 },
            { "Viseme_E_Eh", 11 },
            { "Viseme_IH_EE", 12 },
            { "Viseme_OH", 13 },
            { "Viseme_OU", 14 }
        };

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

            // Step 4: Generate animations for each blendshape
            List<BlendshapeAnimationData> generatedAnimations = new List<BlendshapeAnimationData>();

            foreach (string blendshapeName in blendshapesToTrack)
            {
                AnimationClip clip = GenerateBlendshapeAnimation(data, blendshapeName, cluster);

                if (clip != null)
                {
                    generatedAnimations.Add(new BlendshapeAnimationData
                    {
                        blendshapeName = blendshapeName,
                        animationClip = clip,
                        keyframeCount = CountKeyframes(clip)
                    });

                    if (data.debugMode)
                    {
                        Debug.Log($"[AttachToBlendshapeProcessor] Generated animation for '{blendshapeName}' with {CountKeyframes(clip)} keyframes", data);
                    }
                }
            }

            // Step 5: Save animation clips if direct animation mode is enabled
            if (data.createDirectAnimations || data.debugSaveAnimations)
            {
                SaveAnimationClips(data, generatedAnimations);
            }

            if (data.trackingMode == BlendshapeTrackingMode.VisemsOnly &&
                data.autoCreateVisemeFxLayer)
            {
                GenerateVisemeFxLayer(data, generatedAnimations);
            }

            // Step 6: Set build statistics
            data.SetBuildStats(cluster, blendshapesToTrack, generatedAnimations.Count, bonePath);

            Debug.Log($"[AttachToBlendshapeProcessor] Successfully processed '{data.name}': " +
                     $"{generatedAnimations.Count} animations, {cluster.anchors.Count} triangle cluster", data);
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

        private AnimationClip GenerateBlendshapeAnimation(
            AttachToBlendshapeData data,
            string blendshapeName,
            SurfaceCluster cluster)
        {
            // Sample the blendshape at multiple weights
            List<PoseSampler.PoseSample> samples = PoseSampler.SampleBlendshape(
                data.targetMesh,
                blendshapeName,
                cluster,
                data.samplesPerBlendshape);

            if (samples == null || samples.Count == 0)
            {
                Debug.LogError($"[AttachToBlendshapeProcessor] Failed to sample blendshape '{blendshapeName}'", data);
                return null;
            }

            // Create animation clip
            AnimationClip clip = new AnimationClip();
            clip.name = $"AttachBlendshape_{data.gameObject.name}_{blendshapeName}";

            // Get object's path relative to avatar root
            string objectPath = GetRelativePath(data.transform, data.transform.root);

            // Solve transforms for each sample
            Vector3? previousTangent = null;
            List<(float weight, Vector3 pos, Quaternion rot)> keyframes = new List<(float, Vector3, Quaternion)>();

            foreach (var sample in samples)
            {
                BlendshapeSolver.SolverResult result;

                switch (data.solverMode)
                {
                    case SolverMode.Rigid:
                        result = BlendshapeSolver.SolveRigid(
                            sample.clusterPosition,
                            sample.clusterNormal,
                            sample.clusterTangent,
                            data.transform,
                            data.targetMesh.transform,
                            data.alignRotationToSurface,
                            previousTangent,
                            data.rotationSmoothingFactor);
                        break;

                    case SolverMode.RigidNormalOffset:
                        result = BlendshapeSolver.SolveRigidNormalOffset(
                            sample.clusterPosition,
                            sample.clusterNormal,
                            sample.clusterTangent,
                            data.transform,
                            data.targetMesh.transform,
                            data.alignRotationToSurface,
                            data.normalOffset,
                            previousTangent,
                            data.rotationSmoothingFactor);
                        break;

                    case SolverMode.Affine:
                        // For affine, we need the base sample (first one)
                        var baseSample = samples[0];
                        result = BlendshapeSolver.SolveAffine(
                            sample.clusterPosition,
                            sample.clusterNormal,
                            sample.clusterTangent,
                            data.transform,
                            data.targetMesh.transform,
                            baseSample.clusterPosition,
                            baseSample.clusterNormal,
                            baseSample.clusterTangent,
                            data.alignRotationToSurface,
                            previousTangent,
                            data.rotationSmoothingFactor);
                        break;

                    case SolverMode.CageRBF:
                        // For RBF, use weighted cluster position/rotation
                        // RBF with small cluster essentially becomes weighted rigid transform
                        result = BlendshapeSolver.SolveRigid(
                            sample.clusterPosition,
                            sample.clusterNormal,
                            sample.clusterTangent,
                            data.transform,
                            data.targetMesh.transform,
                            data.alignRotationToSurface,
                            previousTangent,
                            data.rotationSmoothingFactor);
                        
                        if (data.debugMode)
                        {
                            Debug.Log($"[AttachToBlendshapeProcessor] CageRBF using {cluster.anchors.Count} driver triangles", data);
                        }
                        break;

                    default:
                        Debug.LogWarning($"[AttachToBlendshapeProcessor] Unknown solver mode {data.solverMode}, using Rigid");
                        result = BlendshapeSolver.SolveRigid(
                            sample.clusterPosition,
                            sample.clusterNormal,
                            sample.clusterTangent,
                            data.transform,
                            data.targetMesh.transform,
                            data.alignRotationToSurface,
                            previousTangent,
                            data.rotationSmoothingFactor);
                        break;
                }

                if (result.success)
                {
                    // Normalize weight to 0-1 range for animation curve
                    float normalizedWeight = sample.blendshapeWeight / 100f;
                    keyframes.Add((normalizedWeight, result.position, result.rotation));

                    // Store tangent for next sample
                    previousTangent = sample.clusterTangent;
                }
            }

            if (keyframes.Count == 0)
            {
                Debug.LogError($"[AttachToBlendshapeProcessor] No valid keyframes for blendshape '{blendshapeName}'", data);
                return null;
            }

            // Create animation curves
            AnimationCurve posX = new AnimationCurve();
            AnimationCurve posY = new AnimationCurve();
            AnimationCurve posZ = new AnimationCurve();
            AnimationCurve rotX = new AnimationCurve();
            AnimationCurve rotY = new AnimationCurve();
            AnimationCurve rotZ = new AnimationCurve();
            AnimationCurve rotW = new AnimationCurve();

            foreach (var (weight, pos, rot) in keyframes)
            {
                posX.AddKey(weight, pos.x);
                posY.AddKey(weight, pos.y);
                posZ.AddKey(weight, pos.z);
                rotX.AddKey(weight, rot.x);
                rotY.AddKey(weight, rot.y);
                rotZ.AddKey(weight, rot.z);
                rotW.AddKey(weight, rot.w);
            }

            // Set curves to clip
            clip.SetCurve(objectPath, typeof(Transform), "m_LocalPosition.x", posX);
            clip.SetCurve(objectPath, typeof(Transform), "m_LocalPosition.y", posY);
            clip.SetCurve(objectPath, typeof(Transform), "m_LocalPosition.z", posZ);
            
            if (data.alignRotationToSurface)
            {
                clip.SetCurve(objectPath, typeof(Transform), "m_LocalRotation.x", rotX);
                clip.SetCurve(objectPath, typeof(Transform), "m_LocalRotation.y", rotY);
                clip.SetCurve(objectPath, typeof(Transform), "m_LocalRotation.z", rotZ);
                clip.SetCurve(objectPath, typeof(Transform), "m_LocalRotation.w", rotW);
            }

            // Optionally save clip for debugging
            if (data.debugSaveAnimations)
            {
                string assetPath = $"Assets/Generated/YUCP_AttachBlendshape_{data.gameObject.name}_{blendshapeName}.anim";
                System.IO.Directory.CreateDirectory("Assets/Generated");
                AssetDatabase.CreateAsset(clip, assetPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"[AttachToBlendshapeProcessor] Saved animation to '{assetPath}'", data);
            }

            return clip;
        }

        private void SaveAnimationClips(
            AttachToBlendshapeData data,
            List<BlendshapeAnimationData> animations)
        {
            if (animations.Count == 0)
            {
                return;
            }

            // Save animations as assets for manual wiring or FX layer integration
            // This avoids VRCFury conflicts from multiple toggles animating the same properties
            
            string animationDir = "Assets/Generated/AttachToBlendshape";
            if (!System.IO.Directory.Exists(animationDir))
            {
                System.IO.Directory.CreateDirectory(animationDir);
                AssetDatabase.Refresh();
            }

            List<string> savedPaths = new List<string>();

            foreach (var animData in animations)
            {
                try
                {
                    string safeBlendshapeName = animData.blendshapeName.Replace("/", "_").Replace("\\", "_");
                    string assetPath = $"{animationDir}/{data.gameObject.name}_{safeBlendshapeName}.anim";
                    
                    // Check if asset already exists
                    AnimationClip existingClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
                    if (existingClip != null)
                    {
                        // Update existing
                        EditorUtility.CopySerialized(animData.animationClip, existingClip);
                        EditorUtility.SetDirty(existingClip);
                    }
                    else
                    {
                        // Create new
                        AssetDatabase.CreateAsset(animData.animationClip, assetPath);
                    }

                    savedPaths.Add(assetPath);

                    if (data.debugMode)
                    {
                        Debug.Log($"[AttachToBlendshapeProcessor] Saved animation '{animData.blendshapeName}' to '{assetPath}'", data);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[AttachToBlendshapeProcessor] Failed to save animation '{animData.blendshapeName}': {ex.Message}", data);
                }
            }

            AssetDatabase.SaveAssets();

            // Provide clear instructions
            Debug.Log($"<color=green><b>[AttachToBlendshapeProcessor] Successfully generated {animations.Count} animation clips for '{data.name}'</b></color>\n\n" +
                     $"<b>Animations saved to:</b> {animationDir}\n\n" +
                     $"<b>Setup Instructions:</b>\n" +
                     $"1. The object is linked to bone: '{data.SelectedBonePath}'\n" +
                     $"2. Animation clips control the object's position/rotation relative to that bone\n" +
                     $"3. Wire these animations to your Avatar's FX layer using VRCFury or Animator Controller\n" +
                     $"4. Drive each animation with its corresponding blendshape parameter from the target mesh\n\n" +
                     $"<b>Example FX Layer Setup:</b>\n" +
                     $"- Create blend trees that use blendshape weights as parameters\n" +
                     $"- Or use VRCFury's Direct Tree Controller to automatically sync animations with blendshapes\n\n" +
                     $"<b>Note:</b> Direct VRCFury toggle integration is disabled to prevent animation conflicts.\n" +
                     $"Manual setup gives you full control over how blendshapes drive the attachment.", data);
        }

        private void GenerateVisemeFxLayer(
            AttachToBlendshapeData data,
            List<BlendshapeAnimationData> animations)
        {
            if (animations == null || animations.Count == 0)
            {
                return;
            }

            var visemeClips = new List<(int index, string name, AnimationClip clip)>();

            foreach (var anim in animations)
            {
                if (!VisemeNameToIndex.TryGetValue(anim.blendshapeName, out int visemeIndex))
                {
                    continue;
                }

                var staticClip = CreateStaticPoseClip(anim.animationClip);
                if (staticClip == null)
                {
                    continue;
                }

                staticClip.name = $"YUCP_Viseme_{SanitizeFileName(anim.blendshapeName)}";
                visemeClips.Add((visemeIndex, anim.blendshapeName, staticClip));
            }

            if (visemeClips.Count == 0)
            {
                Debug.LogWarning($"[AttachToBlendshapeProcessor] Viseme FX layer enabled but no viseme animations were generated for '{data.name}'", data);
                return;
            }

            string controllerDir = "Assets/Generated/AttachToBlendshape/Controllers";
            Directory.CreateDirectory(controllerDir);

            string controllerPath = $"{controllerDir}/{GetSafeObjectIdentifier(data)}_Viseme.controller";
            controllerPath = controllerPath.Replace("\\", "/");

            var existingController = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (existingController != null)
            {
                AssetDatabase.DeleteAsset(controllerPath);
            }

            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);

            if (!controller.parameters.Any(p => p.name == "Viseme"))
            {
                controller.AddParameter("Viseme", AnimatorControllerParameterType.Int);
            }

            AnimatorControllerLayer layer;
            AnimatorStateMachine stateMachine;

            if (controller.layers.Length > 0)
            {
                layer = controller.layers[0];
                stateMachine = layer.stateMachine ?? new AnimatorStateMachine();
            }
            else
            {
                stateMachine = new AnimatorStateMachine();
                layer = new AnimatorControllerLayer { stateMachine = stateMachine };
            }

            layer.name = $"YUCP_AttachBlendshape_{data.gameObject.name}";
            layer.defaultWeight = 1f;
            layer.blendingMode = AnimatorLayerBlendingMode.Override;

            stateMachine = layer.stateMachine ?? new AnimatorStateMachine();
            ClearStateMachine(stateMachine);

            var idleState = stateMachine.AddState("Idle");
            idleState.writeDefaultValues = false;
            stateMachine.defaultState = idleState;

            foreach (var entry in visemeClips.OrderBy(v => v.index))
            {
                AssetDatabase.AddObjectToAsset(entry.clip, controller);
                entry.clip.hideFlags = HideFlags.HideInHierarchy;

                var state = stateMachine.AddState(entry.name);
                state.motion = entry.clip;
                state.writeDefaultValues = false;

                var enterTransition = stateMachine.AddAnyStateTransition(state);
                enterTransition.hasExitTime = false;
                enterTransition.hasFixedDuration = true;
                enterTransition.duration = 0f;
                enterTransition.canTransitionToSelf = false;
                enterTransition.AddCondition(AnimatorConditionMode.Equals, entry.index, "Viseme");

                var exitTransition = state.AddTransition(idleState);
                exitTransition.hasExitTime = false;
                exitTransition.hasFixedDuration = true;
                exitTransition.duration = 0f;
                exitTransition.AddCondition(AnimatorConditionMode.NotEqual, entry.index, "Viseme");
            }

            layer.stateMachine = stateMachine;
            controller.layers = new[] { layer };

            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(controllerPath);

            var fullController = FuryComponents.CreateFullController(data.gameObject);
            fullController.AddController(controller, VRCAvatarDescriptor.AnimLayerType.FX);
            fullController.AddGlobalParam("Viseme");

            Debug.Log($"[AttachToBlendshapeProcessor] Created viseme FX layer with {visemeClips.Count} states at '{controllerPath}'", data);
        }

        private AnimationClip CreateStaticPoseClip(AnimationClip source)
        {
            if (source == null)
            {
                return null;
            }

            var staticClip = new AnimationClip();

            var bindings = AnimationUtility.GetCurveBindings(source);
            foreach (var binding in bindings)
            {
                var curve = AnimationUtility.GetEditorCurve(source, binding);
                if (curve == null || curve.keys.Length == 0)
                {
                    continue;
                }

                float lastTime = curve.keys[curve.keys.Length - 1].time;
                float value = curve.Evaluate(lastTime);

                var staticCurve = new AnimationCurve(new Keyframe(0f, value));
                AnimationUtility.SetEditorCurve(staticClip, binding, staticCurve);
            }

            staticClip.EnsureQuaternionContinuity();
            return staticClip;
        }

        private void ClearStateMachine(AnimatorStateMachine stateMachine)
        {
            foreach (var childState in stateMachine.states.ToArray())
            {
                stateMachine.RemoveState(childState.state);
            }

            foreach (var childMachine in stateMachine.stateMachines.ToArray())
            {
                stateMachine.RemoveStateMachine(childMachine.stateMachine);
            }

            foreach (var transition in stateMachine.anyStateTransitions.ToArray())
            {
                stateMachine.RemoveAnyStateTransition(transition);
            }
        }

        private string GetSafeObjectIdentifier(AttachToBlendshapeData data)
        {
            string relative = GetRelativePath(data.transform, data.transform.root);
            string baseName = string.IsNullOrEmpty(relative) ? data.gameObject.name : relative.Replace("/", "_");
            return SanitizeFileName(baseName);
        }

        private string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "AttachBlendshape";
            }

            var invalid = Path.GetInvalidFileNameChars();
            var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
            return new string(chars);
        }

        private int CountKeyframes(AnimationClip clip)
        {
            if (clip == null) return 0;

            var bindings = AnimationUtility.GetCurveBindings(clip);
            if (bindings.Length == 0) return 0;

            var curve = AnimationUtility.GetEditorCurve(clip, bindings[0]);
            return curve?.keys.Length ?? 0;
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
    }
}
