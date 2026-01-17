using UnityEngine;
using UnityEditor;
using YUCP.Components.HandPoses;
using System.Collections.Generic;
using YUCP.Components.Editor.AutoGrip.LightningGrasp;

namespace YUCP.Components.Editor.AutoGrip
{
    /// <summary>
    /// Custom editor for AutoGripData component with real-time preview.
    /// Uses Lightning Grasp pipeline for full grasp synthesis.
    /// </summary>
    [CustomEditor(typeof(AutoGripData))]
    public class AutoGripDataEditor : UnityEditor.Editor
    {
        private AutoGripData data;
        private Animator cachedAnimator;
        private HandProxyData leftHandProxy;
        private HandProxyData rightHandProxy;
        private PropCollisionData cachedPropCollision;
        private bool isPreviewActive;
        
        // Lightning Grasp pipeline
        private LightningGraspPipeline leftHandPipeline;
        private LightningGraspPipeline rightHandPipeline;
        private GraspResult leftGraspResult;
        private GraspResult rightGraspResult;
        private Dictionary<HumanBodyBones, ContactDomain> leftContactDomains;
        private Dictionary<HumanBodyBones, ContactDomain> rightContactDomains;
        
        // Fallback simple curl state
        private Dictionary<HumanBodyBones, float> fingerCurls = new Dictionary<HumanBodyBones, float>();
        private Dictionary<HumanBodyBones, Quaternion> originalPose;
        private float lastUpdateTime;
        private const float UpdateInterval = 0.033f; // ~30fps for optimization

        private void OnEnable()
        {
            data = target as AutoGripData;
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            EditorApplication.update -= RealTimeUpdate;
            CleanupPreview();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawDefaultInspector();

            EditorGUILayout.Space(10);

            // Avatar status
            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);
            DrawStatusInfo();

            EditorGUILayout.Space(10);

            // Action buttons
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);
            DrawActionButtons();

            // Preview status
            if (isPreviewActive)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox("Real-time preview active. Move prop to see grip adjust.", MessageType.Info);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawStatusInfo()
        {
            var animator = FindAnimator();
            if (animator == null)
            {
                EditorGUILayout.HelpBox("No avatar found. Place this component under an avatar with an Animator.", MessageType.Warning);
                return;
            }

            if (!animator.isHuman)
            {
                EditorGUILayout.HelpBox("Avatar must have a Humanoid rig.", MessageType.Error);
                return;
            }

            EditorGUILayout.LabelField("Avatar:", animator.name);

            Transform gripPoint = data.gripPoint != null ? data.gripPoint : data.transform;
            EditorGUILayout.LabelField("Grip Point:", gripPoint.name);

            var colliders = data.GetComponentsInChildren<Collider>();
            if (colliders.Length > 0)
            {
                EditorGUILayout.LabelField("Colliders:", $"{colliders.Length} found");
            }
            else
            {
                var meshFilter = data.GetComponentInChildren<MeshFilter>();
                var skinnedMesh = data.GetComponentInChildren<SkinnedMeshRenderer>();
                if (meshFilter != null || skinnedMesh != null)
                {
                    EditorGUILayout.LabelField("Colliders:", "Will generate from mesh");
                }
                else
                {
                    EditorGUILayout.HelpBox("No colliders or meshes found on prop.", MessageType.Warning);
                }
            }

            if (data.bakeCache != null && !string.IsNullOrEmpty(data.bakeCache.lastBakeTimestamp))
            {
                EditorGUILayout.LabelField("Last Bake:", data.bakeCache.lastBakeTimestamp);
            }
        }

        private void DrawActionButtons()
        {
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Resolve Avatar", GUILayout.Height(25)))
            {
                ResolveAvatar();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            EditorGUILayout.BeginHorizontal();

            GUI.enabled = FindAnimator() != null;

            if (GUILayout.Button("Bake Now", GUILayout.Height(30)))
            {
                BakeGripPose();
            }

            // Real-time preview toggle
            GUI.backgroundColor = isPreviewActive ? Color.green : Color.white;
            string previewLabel = isPreviewActive ? "■ Stop Preview" : "▶ Preview (Real-time)";
            if (GUILayout.Button(previewLabel, GUILayout.Height(30)))
            {
                TogglePreview();
            }
            GUI.backgroundColor = Color.white;

            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();

            if (data.leftHandClip != null || data.rightHandClip != null)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.BeginHorizontal();

                if (GUILayout.Button("Regenerate"))
                {
                    RegenerateClips();
                }

                if (GUILayout.Button("Delete Generated Assets"))
                {
                    DeleteGeneratedAssets();
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        private void ResolveAvatar()
        {
            var animator = FindAnimator();
            if (animator != null)
            {
                cachedAnimator = animator;
                Debug.Log($"[AutoGrip] Found avatar: {animator.name}");
                Repaint();
            }
            else
            {
                Debug.LogWarning("[AutoGrip] No avatar found in parents");
            }
        }

        private void TogglePreview()
        {
            if (isPreviewActive)
            {
                StopPreview();
            }
            else
            {
                StartPreview();
            }
        }

        private void StartPreview()
        {
            var animator = FindAnimator();
            if (animator == null) return;

            // Capture original pose
            originalPose = CaptureCurrentPose(animator);

            // Prepare collision data
            cachedPropCollision = PropCollisionSource.PrepareCollisionData(data.transform, data.collisionMask);
            if (cachedPropCollision == null)
            {
                Debug.LogError("[AutoGrip] Failed to prepare collision data for preview");
                return;
            }

            // Build hand proxies (for visualization)
            leftHandProxy = null;
            rightHandProxy = null;
            if (data.generateLeftHand)
            {
                leftHandProxy = HandProxyBuilder.BuildHandProxy(animator, true, data.fingerPaddingMm, data.fingerRadiusOverrides);
            }
            if (data.generateRightHand)
            {
                rightHandProxy = HandProxyBuilder.BuildHandProxy(animator, false, data.fingerPaddingMm, data.fingerRadiusOverrides);
            }

            // Initialize Lightning Grasp pipelines
            BuildLightningGraspPipelines(animator);

            // Initialize fallback finger curls to 0
            InitializeFingerCurls(animator);

            isPreviewActive = true;
            lastUpdateTime = 0f;

            // Subscribe to editor update for real-time solving
            EditorApplication.update += RealTimeUpdate;

            // Run initial grasp synthesis
            RunGraspSynthesis(animator);

            SceneView.RepaintAll();
            Repaint();
        }

        private void BuildLightningGraspPipelines(Animator animator)
        {
            var pipelineConfig = new LightningGraspPipeline.PipelineConfig
            {
                numContacts = 3,
                numObjectPoints = 1024, // Lower for real-time
                contactFieldConfig = new ContactFieldConfig
                {
                    jointSamples = 5000, // Lower for faster building
                    useGPU = true
                },
                optimizerConfig = new WrenchOptimizer.Config
                {
                    totalSteps = 5,  // Fewer steps for real-time
                    variantsPerStep = 5
                }
            };

            // Build pipelines for each hand - always rebuild since pipelines are new
            if (data.generateLeftHand)
            {
                leftHandPipeline = new LightningGraspPipeline(animator, true, pipelineConfig);
                leftHandPipeline.LoadShaders();
                
                EditorUtility.DisplayProgressBar("AutoGrip", "Building left hand contact field...", 0.25f);
                var skinMesh = animator.GetComponentInChildren<SkinnedMeshRenderer>();
                leftHandPipeline.BuildContactField(skinMesh);
            }

            if (data.generateRightHand)
            {
                rightHandPipeline = new LightningGraspPipeline(animator, false, pipelineConfig);
                rightHandPipeline.LoadShaders();
                
                EditorUtility.DisplayProgressBar("AutoGrip", "Building right hand contact field...", 0.5f);
                var skinMesh = animator.GetComponentInChildren<SkinnedMeshRenderer>();
                rightHandPipeline.BuildContactField(skinMesh);
            }

            EditorUtility.ClearProgressBar();
        }

        private void StopPreview()
        {
            EditorApplication.update -= RealTimeUpdate;

            // Restore original pose
            var animator = FindAnimator();
            if (animator != null && originalPose != null)
            {
                RestorePose(animator, originalPose);
            }

            // Cleanup
            cachedPropCollision?.Cleanup();
            cachedPropCollision = null;
            
            // Release pipeline GPU resources
            leftHandPipeline?.Release();
            rightHandPipeline?.Release();
            leftHandPipeline = null;
            rightHandPipeline = null;
            leftGraspResult = null;
            rightGraspResult = null;
            leftContactDomains = null;
            rightContactDomains = null;

            isPreviewActive = false;
            SceneView.RepaintAll();
            Repaint();
        }

        private void CleanupPreview()
        {
            if (isPreviewActive)
            {
                StopPreview();
            }
        }

        private void RealTimeUpdate()
        {
            if (!isPreviewActive) return;
            if (data == null)
            {
                StopPreview();
                return;
            }

            float time = (float)EditorApplication.timeSinceStartup;
            if (time - lastUpdateTime < UpdateInterval) return;
            lastUpdateTime = time;

            var animator = FindAnimator();
            if (animator == null) return;

            // Update hand proxy positions based on current bone transforms
            if (leftHandProxy != null)
            {
                HandProxyBuilder.UpdateProxyPositions(leftHandProxy, animator);
            }
            if (rightHandProxy != null)
            {
                HandProxyBuilder.UpdateProxyPositions(rightHandProxy, animator);
            }

            // Apply Lightning Grasp results (already computed in RunGraspSynthesis)
            ApplyGraspResults(animator);

            SceneView.RepaintAll();
        }

        private void RunGraspSynthesis(Animator animator)
        {
            if (cachedPropCollision == null) return;

            // Get prop colliders
            var colliders = cachedPropCollision.colliders;
            if (colliders == null || colliders.Length == 0) return;

            // Sample object surface points
            var (objectPoints, objectNormals) = SampleObjectSurface(colliders, 1024);
            if (objectPoints.Length == 0) return;

            // Run Lightning Grasp synthesis for each hand
            if (data.generateLeftHand && leftHandPipeline != null)
            {
                leftGraspResult = leftHandPipeline.SynthesizeGrasp(objectPoints, objectNormals, colliders);
            }

            if (data.generateRightHand && rightHandPipeline != null)
            {
                rightGraspResult = rightHandPipeline.SynthesizeGrasp(objectPoints, objectNormals, colliders);
            }

            // Apply results immediately
            ApplyGraspResults(animator);
        }

        private (Vector3[], Vector3[]) SampleObjectSurface(Collider[] colliders, int numSamples)
        {
            var points = new List<Vector3>();
            var normals = new List<Vector3>();

            if (colliders.Length == 0) return (new Vector3[0], new Vector3[0]);

            // Combine bounds
            Bounds bounds = colliders[0].bounds;
            for (int i = 1; i < colliders.Length; i++)
            {
                bounds.Encapsulate(colliders[i].bounds);
            }

            // Sample using raycasting from random directions
            int attempts = 0;
            int maxAttempts = numSamples * 5;

            while (points.Count < numSamples && attempts < maxAttempts)
            {
                attempts++;

                Vector3 dir = Random.onUnitSphere;
                Vector3 origin = bounds.center + dir * bounds.extents.magnitude * 2f;
                Ray ray = new Ray(origin, -dir);

                foreach (var col in colliders)
                {
                    if (col != null && col.Raycast(ray, out RaycastHit hit, bounds.extents.magnitude * 4f))
                    {
                        bool isDuplicate = false;
                        foreach (var existing in points)
                        {
                            if (Vector3.Distance(existing, hit.point) < 0.003f)
                            {
                                isDuplicate = true;
                                break;
                            }
                        }

                        if (!isDuplicate)
                        {
                            points.Add(hit.point);
                            normals.Add(hit.normal);
                        }
                        break;
                    }
                }
            }

            return (points.ToArray(), normals.ToArray());
        }

        private void ApplyGraspResults(Animator animator)
        {
            // Apply left hand grasp result
            if (leftGraspResult != null && leftGraspResult.jointAngles != null)
            {
                foreach (var kvp in leftGraspResult.jointAngles)
                {
                    var t = animator.GetBoneTransform(kvp.Key);
                    if (t != null)
                    {
                        t.localRotation = kvp.Value;
                    }
                }
            }

            // Apply right hand grasp result
            if (rightGraspResult != null && rightGraspResult.jointAngles != null)
            {
                foreach (var kvp in rightGraspResult.jointAngles)
                {
                    var t = animator.GetBoneTransform(kvp.Key);
                    if (t != null)
                    {
                        t.localRotation = kvp.Value;
                    }
                }
            }

            // Fallback: if no Lightning Grasp result, use simple curl
            if ((data.generateLeftHand && leftGraspResult == null) ||
                (data.generateRightHand && rightGraspResult == null))
            {
                SolveGripRealTimeFallback(animator);
            }
        }

        private void SolveGripRealTimeFallback(Animator animator)
        {
            if (cachedPropCollision == null) return;

            // Process each finger independently using simple curl-until-collision
            if (data.generateLeftHand && leftHandProxy != null && leftGraspResult == null)
            {
                SolveHandGrip(animator, leftHandProxy, true);
            }
            if (data.generateRightHand && rightHandProxy != null && rightGraspResult == null)
            {
                SolveHandGrip(animator, rightHandProxy, false);
            }
        }

        private void SolveHandGrip(Animator animator, HandProxyData handProxy, bool isLeftHand)
        {
            // Get grip point in world space
            Transform gripPoint = data.gripPoint != null ? data.gripPoint : data.transform;
            Vector3 gripCenter = gripPoint.position;

            // Process each finger
            SolveFinger(animator, handProxy.indexProxies, gripCenter, 0.02f);
            SolveFinger(animator, handProxy.middleProxies, gripCenter, 0.02f);
            SolveFinger(animator, handProxy.ringProxies, gripCenter, 0.02f);
            SolveFinger(animator, handProxy.littleProxies, gripCenter, 0.02f);
            SolveFinger(animator, handProxy.thumbProxies, gripCenter, 0.015f);
        }

        private void SolveFinger(Animator animator, List<FingerCapsuleProxy> fingerProxies, Vector3 gripCenter, float curlSpeed)
        {
            if (fingerProxies == null || fingerProxies.Count == 0) return;

            // Get current curl value for this finger
            var firstProxy = fingerProxies[0];
            if (!fingerCurls.TryGetValue(firstProxy.bone, out float currentCurl))
            {
                currentCurl = 0f;
            }

            // Check if any segment is colliding with prop
            bool isColliding = false;
            bool isThumb = firstProxy.fingerType == HandPoses.YUCPFingerType.Thumb;
            
            foreach (var proxy in fingerProxies)
            {
                var boneTransform = animator.GetBoneTransform(proxy.bone);
                if (boneTransform == null) continue;

                // Get the direction from bone to child (actual finger direction)
                Vector3 fingerDir;
                if (boneTransform.childCount > 0)
                {
                    fingerDir = (boneTransform.GetChild(0).position - boneTransform.position).normalized;
                }
                else
                {
                    // For tip bone, use parent-to-this direction
                    fingerDir = (boneTransform.position - boneTransform.parent.position).normalized;
                }
                
                Vector3 tipPos = boneTransform.position + fingerDir * 0.01f;
                Vector3 closest = PropCollisionSource.GetClosestPoint(cachedPropCollision, tipPos);
                float dist = Vector3.Distance(tipPos, closest);

                if (dist < proxy.radius + 0.002f)
                {
                    isColliding = true;
                    break;
                }
            }

            // Curl towards grip center until collision
            if (!isColliding && currentCurl < 90f)
            {
                // Check distance to grip center
                var tipBone = animator.GetBoneTransform(fingerProxies[fingerProxies.Count - 1].bone);
                if (tipBone != null)
                {
                    float distToGrip = Vector3.Distance(tipBone.position, gripCenter);
                    if (distToGrip > 0.01f) // Only curl if not yet at grip
                    {
                        currentCurl += curlSpeed * 60f * UpdateInterval;
                        currentCurl = Mathf.Min(currentCurl, 90f);
                    }
                }
            }
            else if (isColliding && currentCurl > 0f)
            {
                // Back off slightly on collision
                currentCurl -= curlSpeed * 30f * UpdateInterval;
                currentCurl = Mathf.Max(currentCurl, 0f);
            }

            fingerCurls[firstProxy.bone] = currentCurl;

            // Apply curl to all segments
            ApplyFingerCurl(animator, fingerProxies, currentCurl);
        }

        private void ApplyFingerCurl(Animator animator, List<FingerCapsuleProxy> fingerProxies, float curlDegrees)
        {
            if (fingerProxies.Count == 0) return;
            
            bool isThumb = fingerProxies[0].fingerType == HandPoses.YUCPFingerType.Thumb;
            
            for (int i = 0; i < fingerProxies.Count; i++)
            {
                var proxy = fingerProxies[i];
                var boneTransform = animator.GetBoneTransform(proxy.bone);
                if (boneTransform == null) continue;

                // Distribute curl across segments
                float segmentCurl = curlDegrees;
                if (i == 0) segmentCurl *= 0.5f;  // Proximal
                else if (i == 1) segmentCurl *= 0.35f; // Intermediate
                else segmentCurl *= 0.15f; // Distal

                // For non-thumb fingers, negate the curl (they curl in opposite direction)
                if (!isThumb)
                {
                    segmentCurl = -segmentCurl;
                }

                // Apply rotation around local X axis (curl)
                Quaternion curlRotation = Quaternion.Euler(segmentCurl, 0, 0);
                if (originalPose != null && originalPose.TryGetValue(proxy.bone, out Quaternion orig))
                {
                    boneTransform.localRotation = orig * curlRotation;
                }
            }
        }

        private void InitializeFingerCurls(Animator animator)
        {
            fingerCurls.Clear();

            var fingerBones = new HumanBodyBones[]
            {
                HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftMiddleProximal,
                HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftLittleProximal,
                HumanBodyBones.LeftThumbProximal,
                HumanBodyBones.RightIndexProximal, HumanBodyBones.RightMiddleProximal,
                HumanBodyBones.RightRingProximal, HumanBodyBones.RightLittleProximal,
                HumanBodyBones.RightThumbProximal
            };

            foreach (var bone in fingerBones)
            {
                fingerCurls[bone] = 0f;
            }
        }

        private void BakeGripPose()
        {
            var animator = FindAnimator();
            if (animator == null)
            {
                Debug.LogError("[AutoGrip] Cannot bake: No avatar found");
                return;
            }

            if (data.bakeCache == null)
            {
                data.bakeCache = CreateInstance<AutoGripBakeCache>();
                string cachePath = $"Assets/YUCP/AutoGrip/Cache/{data.name}_Cache.asset";
                string dir = System.IO.Path.GetDirectoryName(cachePath);
                if (!System.IO.Directory.Exists(dir))
                {
                    System.IO.Directory.CreateDirectory(dir);
                }
                AssetDatabase.CreateAsset(data.bakeCache, cachePath);
            }

            var startTime = System.DateTime.Now;
            data.bakeCache.Clear();

            var propCollision = PropCollisionSource.PrepareCollisionData(data.transform, data.collisionMask);
            if (propCollision == null)
            {
                Debug.LogError("[AutoGrip] Failed to prepare collision data");
                return;
            }

            try
            {
                var backend = new ProceduralContactAndCollisionBackend();

                if (data.generateLeftHand)
                {
                    EditorUtility.DisplayProgressBar("AutoGrip Bake", "Synthesizing left hand grip...", 0.2f);

                    var leftPose = backend.SynthesizeGrasp(animator, true, data, propCollision);
                    if (leftPose != null)
                    {
                        string path = HandMuscleClipBaker.GenerateClipPath(animator.gameObject, data.gameObject, true);
                        data.leftHandClip = HandMuscleClipBaker.BakeHandMuscles(animator, leftPose, true, path);
                        data.bakeCache.generatedClipPathLeft = path;
                    }
                }

                if (data.generateRightHand)
                {
                    EditorUtility.DisplayProgressBar("AutoGrip Bake", "Synthesizing right hand grip...", 0.6f);

                    var rightPose = backend.SynthesizeGrasp(animator, false, data, propCollision);
                    if (rightPose != null)
                    {
                        string path = HandMuscleClipBaker.GenerateClipPath(animator.gameObject, data.gameObject, false);
                        data.rightHandClip = HandMuscleClipBaker.BakeHandMuscles(animator, rightPose, false, path);
                        data.bakeCache.generatedClipPathRight = path;
                    }
                }

                var elapsed = System.DateTime.Now - startTime;
                data.bakeCache.lastBakeDuration = (float)elapsed.TotalSeconds;
                data.bakeCache.lastBakeTimestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                EditorUtility.SetDirty(data);
                EditorUtility.SetDirty(data.bakeCache);
                AssetDatabase.SaveAssets();

                Debug.Log($"[AutoGrip] Bake complete in {elapsed.TotalSeconds:F2}s");
            }
            finally
            {
                propCollision?.Cleanup();
                EditorUtility.ClearProgressBar();
            }
        }

        private void RegenerateClips()
        {
            if (data.bakeCache != null)
            {
                data.bakeCache.Clear();
            }
            data.leftHandClip = null;
            data.rightHandClip = null;
            BakeGripPose();
        }

        private void DeleteGeneratedAssets()
        {
            if (data.leftHandClip != null)
            {
                string path = AssetDatabase.GetAssetPath(data.leftHandClip);
                if (!string.IsNullOrEmpty(path))
                {
                    AssetDatabase.DeleteAsset(path);
                }
                data.leftHandClip = null;
            }

            if (data.rightHandClip != null)
            {
                string path = AssetDatabase.GetAssetPath(data.rightHandClip);
                if (!string.IsNullOrEmpty(path))
                {
                    AssetDatabase.DeleteAsset(path);
                }
                data.rightHandClip = null;
            }

            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
        }

        private Animator FindAnimator()
        {
            if (cachedAnimator != null) return cachedAnimator;

            var animator = data.GetComponentInParent<Animator>();
            if (animator != null && animator.isHuman)
            {
                cachedAnimator = animator;
                return animator;
            }

            return null;
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (data == null) return;
            if (!Selection.Contains(data.gameObject)) return;

            var animator = FindAnimator();
            if (animator == null) return;

            // Always draw grip point
            DrawGripPointGizmo();

            // Always draw contact domains on prop
            DrawContactDomains(animator);

            // Draw hand proxies (always, not just during preview)
            // Build temporary proxies if not in preview mode
            if (!isPreviewActive)
            {
                // Build proxies on-demand for visualization
                if (data.generateLeftHand)
                {
                    var tempLeft = HandProxyBuilder.BuildHandProxy(animator, true, data.fingerPaddingMm, data.fingerRadiusOverrides, null);
                    if (tempLeft != null)
                    {
                        Handles.color = new Color(0f, 1f, 0.5f, 0.3f);
                        DrawProxyGizmos(tempLeft, animator);
                    }
                }
                if (data.generateRightHand)
                {
                    var tempRight = HandProxyBuilder.BuildHandProxy(animator, false, data.fingerPaddingMm, data.fingerRadiusOverrides, null);
                    if (tempRight != null)
                    {
                        Handles.color = new Color(0.5f, 0.5f, 1f, 0.3f);
                        DrawProxyGizmos(tempRight, animator);
                    }
                }
            }
            else
            {
                // During preview, draw cached proxies with brighter colors
                DrawHandProxyGizmos(animator);
                
                // Draw Lightning Grasp results
                DrawLightningGraspResults(animator);
            }

            // Draw prop collision bounds
            if (cachedPropCollision != null)
            {
                Handles.color = new Color(1f, 0.5f, 0f, 0.3f);
                Handles.DrawWireCube(cachedPropCollision.bounds.center, cachedPropCollision.bounds.size);
            }
            else
            {
                // Draw estimated bounds from colliders/meshes
                var bounds = GetPropBounds();
                Handles.color = new Color(1f, 0.5f, 0f, 0.2f);
                Handles.DrawWireCube(bounds.center, bounds.size);
            }
        }

        private void DrawLightningGraspResults(Animator animator)
        {
            // Draw contact patches from pipelines
            if (leftHandPipeline != null)
            {
                var field = leftHandPipeline.GetContactField();
                if (field != null)
                {
                    LightningGraspGizmos.DrawContactPatches(animator, field, true, 0.3f);
                }
            }
            if (rightHandPipeline != null)
            {
                var field = rightHandPipeline.GetContactField();
                if (field != null)
                {
                    LightningGraspGizmos.DrawContactPatches(animator, field, false, 0.3f);
                }
            }

            // Draw contact domains
            if (leftContactDomains != null)
            {
                LightningGraspGizmos.DrawContactDomains(leftContactDomains, 0.5f);
            }
            if (rightContactDomains != null)
            {
                LightningGraspGizmos.DrawContactDomains(rightContactDomains, 0.5f);
            }

            // Draw grasp results (contact points, normals, score)
            if (leftGraspResult != null)
            {
                LightningGraspGizmos.DrawGraspResult(animator, leftGraspResult, 0.8f);
            }
            if (rightGraspResult != null)
            {
                LightningGraspGizmos.DrawGraspResult(animator, rightGraspResult, 0.8f);
            }
        }

        private Bounds GetPropBounds()
        {
            Transform gripPoint = data.gripPoint != null ? data.gripPoint : data.transform;
            var bounds = new Bounds(gripPoint.position, Vector3.one * 0.05f);
            
            var colliders = data.GetComponentsInChildren<Collider>();
            foreach (var col in colliders)
            {
                bounds.Encapsulate(col.bounds);
            }
            var meshFilters = data.GetComponentsInChildren<MeshFilter>();
            foreach (var mf in meshFilters)
            {
                if (mf.sharedMesh != null)
                {
                    var renderer = mf.GetComponent<Renderer>();
                    if (renderer != null)
                    {
                        bounds.Encapsulate(renderer.bounds);
                    }
                }
            }
            
            return bounds;
        }

        private void DrawHandProxyGizmos(Animator animator)
        {
            Handles.color = new Color(0f, 1f, 0.5f, 0.5f);
            if (leftHandProxy != null) DrawProxyGizmos(leftHandProxy, animator);

            Handles.color = new Color(0.5f, 0.5f, 1f, 0.5f);
            if (rightHandProxy != null) DrawProxyGizmos(rightHandProxy, animator);
        }

        private void DrawProxyGizmos(HandProxyData proxy, Animator animator)
        {
            if (proxy == null) return;

            foreach (var capsule in proxy.AllProxies)
            {
                var boneTransform = animator.GetBoneTransform(capsule.bone);
                if (boneTransform == null) continue;

                Vector3 start = boneTransform.position;
                Vector3 end = boneTransform.childCount > 0 
                    ? boneTransform.GetChild(0).position 
                    : boneTransform.position + boneTransform.forward * 0.015f;

                // Draw capsule
                Handles.DrawWireDisc(start, (end - start).normalized, capsule.radius);
                Handles.DrawWireDisc(end, (end - start).normalized, capsule.radius);
                Handles.DrawLine(start, end);
            }
        }

        private void DrawGripPointGizmo()
        {
            Transform gripPoint = data.gripPoint != null ? data.gripPoint : data.transform;

            Handles.color = Color.yellow;
            Handles.DrawWireCube(gripPoint.position, Vector3.one * 0.02f);

            Handles.color = Color.red;
            Handles.DrawLine(gripPoint.position, gripPoint.position + gripPoint.right * 0.03f);
            Handles.color = Color.green;
            Handles.DrawLine(gripPoint.position, gripPoint.position + gripPoint.up * 0.03f);
            Handles.color = Color.blue;
            Handles.DrawLine(gripPoint.position, gripPoint.position + gripPoint.forward * 0.03f);
        }

        /// <summary>
        /// Draws contact domains on the prop surface.
        /// These represent potential contact regions for fingers.
        /// </summary>
        private void DrawContactDomains(Animator animator)
        {
            Transform gripPoint = data.gripPoint != null ? data.gripPoint : data.transform;
            Vector3 center = gripPoint.position;
            
            // Get prop bounds
            var bounds = new Bounds(center, Vector3.one * 0.05f);
            var colliders = data.GetComponentsInChildren<Collider>();
            foreach (var col in colliders)
            {
                bounds.Encapsulate(col.bounds);
            }
            var meshFilters = data.GetComponentsInChildren<MeshFilter>();
            foreach (var mf in meshFilters)
            {
                if (mf.sharedMesh != null)
                {
                    var meshBounds = mf.sharedMesh.bounds;
                    meshBounds.center = mf.transform.TransformPoint(meshBounds.center);
                    meshBounds.size = Vector3.Scale(meshBounds.size, mf.transform.lossyScale);
                    bounds.Encapsulate(meshBounds);
                }
            }

            // Draw domain grid on prop surface
            // These are the potential contact regions similar to Lightning Grasp's contact field patches
            int gridSize = 4;
            float domainRadius = 0.008f;
            
            // Get hand position for reference
            var handBone = animator.GetBoneTransform(data.generateRightHand 
                ? HumanBodyBones.RightHand 
                : HumanBodyBones.LeftHand);
            Vector3 handPos = handBone != null ? handBone.position : center - Vector3.right * 0.1f;
            Vector3 toHand = (handPos - center).normalized;
            
            // Sample contact domains on the "grippable" side of the object
            for (int i = 0; i < gridSize; i++)
            {
                for (int j = 0; j < gridSize; j++)
                {
                    // Calculate domain position on object surface facing hand
                    float u = (i / (float)(gridSize - 1)) - 0.5f;
                    float v = (j / (float)(gridSize - 1)) - 0.5f;
                    
                    // Create local offset perpendicular to hand direction
                    Vector3 right = Vector3.Cross(toHand, Vector3.up).normalized;
                    Vector3 up = Vector3.Cross(right, toHand).normalized;
                    
                    Vector3 domainOffset = right * u * bounds.extents.x + up * v * bounds.extents.y;
                    Vector3 domainPos = center + domainOffset;
                    
                    // Color based on distance to grip point
                    float distToCenter = Vector3.Distance(domainPos, center);
                    float normalizedDist = distToCenter / bounds.extents.magnitude;
                    
                    // Primary contact domains (near grip point) are cyan
                    // Secondary domains (further out) are magenta
                    Color domainColor = Color.Lerp(
                        new Color(0f, 1f, 1f, 0.6f),  // Cyan (primary)
                        new Color(1f, 0f, 1f, 0.3f), // Magenta (secondary)
                        normalizedDist
                    );
                    
                    Handles.color = domainColor;
                    Handles.DrawWireDisc(domainPos, toHand, domainRadius);
                    Handles.DrawSolidDisc(domainPos, toHand, domainRadius * 0.5f);
                    
                    // Draw normal direction (pointing toward hand)
                    Handles.color = new Color(1f, 1f, 0f, 0.4f);
                    Handles.DrawLine(domainPos, domainPos + toHand * 0.01f);
                }
            }
            
            // Label
            Handles.color = Color.white;
            Handles.Label(center + Vector3.up * (bounds.extents.y + 0.02f), "Contact Domains");
        }

        private Dictionary<HumanBodyBones, Quaternion> CaptureCurrentPose(Animator animator)
        {
            var pose = new Dictionary<HumanBodyBones, Quaternion>();

            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                var bone = (HumanBodyBones)i;
                var transform = animator.GetBoneTransform(bone);
                if (transform != null)
                {
                    pose[bone] = transform.localRotation;
                }
            }

            return pose;
        }

        private void RestorePose(Animator animator, Dictionary<HumanBodyBones, Quaternion> pose)
        {
            foreach (var kvp in pose)
            {
                var transform = animator.GetBoneTransform(kvp.Key);
                if (transform != null)
                {
                    transform.localRotation = kvp.Value;
                }
            }
        }
    }
}

