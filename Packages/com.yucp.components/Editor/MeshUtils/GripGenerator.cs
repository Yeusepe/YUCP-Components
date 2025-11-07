using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using YUCP.Components;

namespace YUCP.Components.Editor.MeshUtils
{
    /// <summary>
    /// Generates hand grip animations using contact-based mesh analysis.
    /// Prevents finger clipping by detecting where finger mesh surfaces would contact the object.
    /// </summary>
    public static class GripGenerator
    {
        public class GripResult
        {
            public AnimationClip animation;
            public Vector3 gripOffset;
            public Dictionary<string, float> muscleValues = new Dictionary<string, float>();
            public List<ContactPoint> contactPoints = new List<ContactPoint>();
            public Dictionary<HumanBodyBones, Quaternion> solvedRotations = new Dictionary<HumanBodyBones, Quaternion>();
        }

        public class ContactPoint
        {
            public Vector3 position;
            public Vector3 normal;
            public string fingerName;
            public int segment;
        }

        public static GripResult GenerateGrip(
            Animator animator,
            Transform grippedObject,
            AutoGripData data,
            bool isLeftHand)
        {
            Debug.Log($"[GripGenerator] Starting grip generation for {(isLeftHand ? "left" : "right")} hand using finger tip gizmos");
            
            return GenerateCurlGripFromFingerTips(animator, grippedObject, data, isLeftHand);
        }

        /// <summary>
        /// One-call bake: generates a grip and returns an AnimationClip with baked muscle curves.
        /// If saveAssetPath is provided, the clip is saved/overwritten at that path.
        /// </summary>
        public static AnimationClip BakeGripClip(
            Animator animator,
            Transform grippedObject,
            AutoGripData data,
            bool isLeftHand,
            string saveAssetPath = null)
        {
            var result = GenerateGrip(animator, grippedObject, data, isLeftHand);
            if (result == null || result.animation == null)
            {
                Debug.LogError("[GripGenerator] BakeGripClip failed - no animation produced");
                return null;
            }

            var clip = result.animation;
            clip.name = $"AutoGrip_{(isLeftHand ? "Left" : "Right")}";

#if UNITY_EDITOR
            if (!string.IsNullOrEmpty(saveAssetPath))
            {
                // Ensure extension
                if (!saveAssetPath.EndsWith(".anim")) saveAssetPath += ".anim";

                var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(saveAssetPath);
                if (existing == null)
                {
                    AssetDatabase.CreateAsset(clip, saveAssetPath);
                    Debug.Log($"[GripGenerator] Baked grip saved to {saveAssetPath}");
                }
                else
                {
                    // Overwrite by copying curves
                    EditorUtility.CopySerialized(clip, existing);
                    AssetDatabase.SaveAssets();
                    clip = existing;
                    Debug.Log($"[GripGenerator] Baked grip overwritten at {saveAssetPath}");
                }
            }
#endif

            return clip;
        }

        /// <summary>
        /// Collision-aware baker using capsule samples and a raycast distance field.
        /// Produces a baked AnimationClip of muscles without IK look-at.
        /// </summary>
        public static AnimationClip BakeGripClipCollisionAware(
            Animator animator,
            Transform grippedObject,
            AutoGripData data,
            bool isLeftHand,
            string saveAssetPath = null)
        {
            if (animator == null || grippedObject == null)
            {
                Debug.LogError("[GripGenerator] BakeGripClipCollisionAware requires animator and grippedObject");
                return null;
            }

            // Build combined distance field: object + avatar proxy (if present)
            int objectMask = 1 << grippedObject.gameObject.layer;
            int avatarMask = LayerMask.NameToLayer("AvatarProxy");
            int avatarMaskBits = avatarMask >= 0 ? (1 << avatarMask) : 0;
            var objectDF = new RaycastDistanceField(objectMask, 0.0015f);
            var df = avatarMaskBits != 0
                ? (IDistanceField)new CombinedDistanceField(objectDF, new RaycastDistanceField(avatarMaskBits, 0.0015f))
                : (IDistanceField)objectDF;

            var snapped = GetSnappedTargets(animator, data, isLeftHand, out _);

            CollisionAwareFingerSolver.Capsule MakeCaps(Transform a, Transform b)
            {
                var cap = new CollisionAwareFingerSolver.Capsule
                {
                    bone = a,
                    p0Local = Vector3.zero,
                    p1Local = a.InverseTransformPoint(b.position),
                    radius = 0.008f
                };
                return cap;
            }

            void SolveOne(string finger, HumanBodyBones mcpB, HumanBodyBones pipB, HumanBodyBones dipB, Vector3 tip)
            {
                if (tip == Vector3.zero) return;
                var mcp = animator.GetBoneTransform(mcpB);
                var pip = animator.GetBoneTransform(pipB);
                var dip = animator.GetBoneTransform(dipB);
                if (mcp == null || pip == null || dip == null) return;
                var caps = new[] { MakeCaps(mcp, pip), MakeCaps(pip, dip) };
                var lim = CollisionAwareFingerSolver.FingerLimits.Defaults();
                // Safety padding per finger (default 1.5 mm); can be tuned per finger type later
                const float safetyPadding = 0.0015f;
                CollisionAwareFingerSolver.CloseFinger(mcp, pip, dip, caps, lim, df, 0.0015f, safetyPadding, 40);
            }

            if (isLeftHand)
            {
                SolveOne("Thumb", HumanBodyBones.LeftThumbProximal, HumanBodyBones.LeftThumbIntermediate, HumanBodyBones.LeftThumbDistal, snapped.thumbTip);
                SolveOne("Index", HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexDistal, snapped.indexTip);
                SolveOne("Middle", HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal, snapped.middleTip);
                SolveOne("Ring", HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftRingDistal, snapped.ringTip);
                SolveOne("Little", HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleDistal, snapped.littleTip);
            }
            else
            {
                SolveOne("Thumb", HumanBodyBones.RightThumbProximal, HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightThumbDistal, snapped.thumbTip);
                SolveOne("Index", HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal, snapped.indexTip);
                SolveOne("Middle", HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal, snapped.middleTip);
                SolveOne("Ring", HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal, snapped.ringTip);
                SolveOne("Little", HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal, snapped.littleTip);
            }

            var solved = new System.Collections.Generic.Dictionary<HumanBodyBones, Quaternion>();
            void AddRot(HumanBodyBones b) { var t = animator.GetBoneTransform(b); if (t != null) solved[b] = t.localRotation; }
            if (isLeftHand)
            {
                AddRot(HumanBodyBones.LeftThumbProximal); AddRot(HumanBodyBones.LeftThumbIntermediate); AddRot(HumanBodyBones.LeftThumbDistal);
                AddRot(HumanBodyBones.LeftIndexProximal); AddRot(HumanBodyBones.LeftIndexIntermediate); AddRot(HumanBodyBones.LeftIndexDistal);
                AddRot(HumanBodyBones.LeftMiddleProximal); AddRot(HumanBodyBones.LeftMiddleIntermediate); AddRot(HumanBodyBones.LeftMiddleDistal);
                AddRot(HumanBodyBones.LeftRingProximal); AddRot(HumanBodyBones.LeftRingIntermediate); AddRot(HumanBodyBones.LeftRingDistal);
                AddRot(HumanBodyBones.LeftLittleProximal); AddRot(HumanBodyBones.LeftLittleIntermediate); AddRot(HumanBodyBones.LeftLittleDistal);
            }
            else
            {
                AddRot(HumanBodyBones.RightThumbProximal); AddRot(HumanBodyBones.RightThumbIntermediate); AddRot(HumanBodyBones.RightThumbDistal);
                AddRot(HumanBodyBones.RightIndexProximal); AddRot(HumanBodyBones.RightIndexIntermediate); AddRot(HumanBodyBones.RightIndexDistal);
                AddRot(HumanBodyBones.RightMiddleProximal); AddRot(HumanBodyBones.RightMiddleIntermediate); AddRot(HumanBodyBones.RightMiddleDistal);
                AddRot(HumanBodyBones.RightRingProximal); AddRot(HumanBodyBones.RightRingIntermediate); AddRot(HumanBodyBones.RightRingDistal);
                AddRot(HumanBodyBones.RightLittleProximal); AddRot(HumanBodyBones.RightLittleIntermediate); AddRot(HumanBodyBones.RightLittleDistal);
            }

            var muscles = BoneRotationToMuscle.ConvertFingerRotationsToMuscles(animator, isLeftHand, solved);
            var clip = CreateAnimationClip(muscles, isLeftHand);

#if UNITY_EDITOR
            if (!string.IsNullOrEmpty(saveAssetPath))
            {
                if (!saveAssetPath.EndsWith(".anim")) saveAssetPath += ".anim";
                var existing = UnityEditor.AssetDatabase.LoadAssetAtPath<AnimationClip>(saveAssetPath);
                if (existing == null) UnityEditor.AssetDatabase.CreateAsset(clip, saveAssetPath);
                else { UnityEditor.EditorUtility.CopySerialized(clip, existing); UnityEditor.AssetDatabase.SaveAssets(); clip = existing; }
            }
#endif

            return clip;
        }

        /// <summary>
        /// Get snapped finger tip targets from manually positioned gizmos.
        /// Converts stored positions to world space and returns them with default rotations.
        /// </summary>
        public static FingerTipSolver.FingerTipTarget GetSnappedTargets(Animator animator, AutoGripData data, bool isLeftHand, out List<ContactPoint> contactPoints)
        {
			contactPoints = new List<ContactPoint>();
			var contacts = new List<ContactPoint>();
			var result = new FingerTipSolver.FingerTipTarget();
			if (data == null || data.grippedObject == null) { contactPoints = contacts; return result; }

			// Utilities
			var colliders = SurfaceQuery.GetAllColliders(data.grippedObject);
			var renderers = SurfaceQuery.GetAllRenderers(data.grippedObject);
			Vector3 center = renderers != null && renderers.Length > 0 ? SurfaceQuery.GetBounds(renderers).center : data.grippedObject.position;
			var handT = animator.GetBoneTransform(isLeftHand ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
			Vector3 handUp = handT != null ? handT.up : Vector3.up;
            // Build world triangles for robust closest-point snapping (works on thin meshes)
            var worldTris = GetObjectMeshTriangles(data.grippedObject);

			// Auto-detect whether stored tip is local-to-object or already world, then return world
			Vector3 ToWorldSmart(Vector3 stored)
			{
				if (stored == Vector3.zero) return Vector3.zero;
				Vector3 asLocalWorld = data.grippedObject.TransformPoint(stored);
				float distLocal = (asLocalWorld - center).sqrMagnitude;
				float distWorld = (stored - center).sqrMagnitude;
				return distWorld < distLocal ? stored : asLocalWorld;
			}

			// List of already chosen positions for spacing penalty
			var chosenPositions = new List<Vector3>();

			// Ring-based snap: sample K candidates, score them, pick best
			void Snap(string label, Vector3 worldPos, HumanBodyBones distalBone, ref Vector3 outPos, ref Quaternion outRot)
			{
				if (worldPos == Vector3.zero) return;
				var distal = animator.GetBoneTransform(distalBone);
				// Use stable origin from intended world position to avoid feedback with IK-updated bones
				Vector3 tipOrigin = worldPos;
				
				// Build finger frame
				Vector3 inward = (center - tipOrigin).sqrMagnitude > 1e-6f ? (center - tipOrigin).normalized : Vector3.forward;
				// Use purely inward direction to stabilize frame across frames
				Vector3 frameZ = inward;
				Vector3 spreadAxis = isLeftHand ? -handT.right : handT.right;
				Vector3 frameX = Vector3.ProjectOnPlane(spreadAxis, frameZ).normalized;
				if (frameX.sqrMagnitude < 1e-6f) frameX = Vector3.right;
				Vector3 frameY = Vector3.Cross(frameZ, frameX).normalized;
				
				// Sample ring candidates
				const int K = 16;
				const float ringRadius = 0.018f;
				var candidates = SampleRingCandidates(tipOrigin, frameX, frameY, ringRadius, K, worldTris, center);
				
				// Score each candidate
				for (int i = 0; i < candidates.Count; i++)
				{
					var cand = candidates[i];
					var neighborNormals = new List<Vector3>();
					if (i > 0) neighborNormals.Add(candidates[i - 1].normal);
					if (i < candidates.Count - 1) neighborNormals.Add(candidates[i + 1].normal);
					
					cand.score = ScoreCandidate(cand, distal, center, chosenPositions, neighborNormals, worldTris);
					candidates[i] = cand;
				}
				
				// Pick best
				RingCandidate best = candidates[0];
				for (int i = 1; i < candidates.Count; i++)
				{
					if (candidates[i].score > best.score)
						best = candidates[i];
				}
				
				// Set output
				outPos = best.position + best.normal * 0.002f;
				Vector3 fwd = (best.position - tipOrigin).sqrMagnitude > 1e-8f ? (best.position - tipOrigin).normalized : (distal != null ? distal.forward : best.normal);
				Vector3 up = Vector3.ProjectOnPlane(handUp, fwd).normalized;
				if (up.sqrMagnitude < 1e-6f) up = Vector3.up;
				outRot = Quaternion.LookRotation(fwd, up);
				
				contacts.Add(new ContactPoint { position = outPos, normal = best.normal, fingerName = label, segment = 3 });
				chosenPositions.Add(best.position);
			}

			if (isLeftHand)
			{
				Snap("Left Thumb", ToWorldSmart(data.leftThumbTip), HumanBodyBones.LeftThumbDistal, ref result.thumbTip, ref result.thumbRotation);
				Snap("Left Index", ToWorldSmart(data.leftIndexTip), HumanBodyBones.LeftIndexDistal, ref result.indexTip, ref result.indexRotation);
				Snap("Left Middle", ToWorldSmart(data.leftMiddleTip), HumanBodyBones.LeftMiddleDistal, ref result.middleTip, ref result.middleRotation);
				Snap("Left Ring", ToWorldSmart(data.leftRingTip), HumanBodyBones.LeftRingDistal, ref result.ringTip, ref result.ringRotation);
				Snap("Left Little", ToWorldSmart(data.leftLittleTip), HumanBodyBones.LeftLittleDistal, ref result.littleTip, ref result.littleRotation);
			}
			else
			{
				Snap("Right Thumb", ToWorldSmart(data.rightThumbTip), HumanBodyBones.RightThumbDistal, ref result.thumbTip, ref result.thumbRotation);
				Snap("Right Index", ToWorldSmart(data.rightIndexTip), HumanBodyBones.RightIndexDistal, ref result.indexTip, ref result.indexRotation);
				Snap("Right Middle", ToWorldSmart(data.rightMiddleTip), HumanBodyBones.RightMiddleDistal, ref result.middleTip, ref result.middleRotation);
				Snap("Right Ring", ToWorldSmart(data.rightRingTip), HumanBodyBones.RightRingDistal, ref result.ringTip, ref result.ringRotation);
				Snap("Right Little", ToWorldSmart(data.rightLittleTip), HumanBodyBones.RightLittleDistal, ref result.littleTip, ref result.littleRotation);
			}
			contactPoints = contacts;
			// Enforce minimal lateral spacing to avoid fingertips piling together
			if (handT != null)
			{
				Vector3 spreadAxis = isLeftHand ? -handT.right : handT.right;
				float minSep = 0.015f; // 1.5 cm
				void Separate(ref Vector3 a, ref Vector3 b)
				{
					float sa = Vector3.Dot(a, spreadAxis);
					float sb = Vector3.Dot(b, spreadAxis);
					float delta = (sb - sa);
					if (Mathf.Abs(delta) < minSep)
					{
						float corr = 0.5f * (minSep - Mathf.Abs(delta));
						Vector3 off = spreadAxis.normalized * corr;
						if (delta >= 0f) { a += -off; b += off; } else { a += off; b += -off; }
					}
				}
				// Apply on neighbors: index-middle, middle-ring, ring-little
				Separate(ref result.indexTip, ref result.middleTip);
				Separate(ref result.middleTip, ref result.ringTip);
				Separate(ref result.ringTip, ref result.littleTip);
			}

			return result;
        }

        /// <summary>
        /// Compute default finger tip positions by initializing from object and returning them.
        /// Used when resetting grip or when no manual positions are set.
        /// </summary>
        public static FingerTipSolver.FingerTipTarget ComputeSnappedDefaultTargets(Animator animator, AutoGripData data, bool isLeftHand, out List<ContactPoint> contactPoints)
        {
            contactPoints = new List<ContactPoint>();
            var contacts = new List<ContactPoint>();
            var outTarget = new FingerTipSolver.FingerTipTarget();
            if (data == null || data.grippedObject == null) { contactPoints = contacts; return outTarget; }

            var colliders = SurfaceQuery.GetAllColliders(data.grippedObject);
            var renderers = SurfaceQuery.GetAllRenderers(data.grippedObject);
            Vector3 center = renderers != null && renderers.Length > 0 ? SurfaceQuery.GetBounds(renderers).center : data.grippedObject.position;
            var handT = animator != null ? animator.GetBoneTransform(isLeftHand ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand) : null;
            Vector3 handUp = handT != null ? handT.up : Vector3.up;

            // Seed default world positions around the object
            var seed = FingerTipSolver.InitializeFingerTips(data.grippedObject, isLeftHand);
            var worldTris = GetObjectMeshTriangles(data.grippedObject);
            var chosenPositions = new List<Vector3>();

            void SnapWorld(string label, Vector3 worldPos, Transform distal, ref Vector3 outPos, ref Quaternion outRot)
            {
                if (worldPos == Vector3.zero) return;
                // Use stable origin from the seed world position to avoid feedback with IK-updated bones
                Vector3 tipOrigin = worldPos;
                
                // Build finger frame (stable): purely inward-based frame, not dependent on current bone forward
                Vector3 inward = (center - tipOrigin).sqrMagnitude > 1e-6f ? (center - tipOrigin).normalized : Vector3.forward;
                Vector3 frameZ = inward;
                Vector3 spreadAxis = isLeftHand ? -handT.right : handT.right;
                Vector3 frameX = Vector3.ProjectOnPlane(spreadAxis, frameZ).normalized;
                if (frameX.sqrMagnitude < 1e-6f) frameX = Vector3.right;
                Vector3 frameY = Vector3.Cross(frameZ, frameX).normalized;
                
                // Sample ring candidates
                const int K = 16;
                const float ringRadius = 0.018f;
                var candidates = SampleRingCandidates(tipOrigin, frameX, frameY, ringRadius, K, worldTris, center);
                
                // Score each candidate
                for (int i = 0; i < candidates.Count; i++)
                {
                    var cand = candidates[i];
                    var neighborNormals = new List<Vector3>();
                    if (i > 0) neighborNormals.Add(candidates[i - 1].normal);
                    if (i < candidates.Count - 1) neighborNormals.Add(candidates[i + 1].normal);
                    
                    cand.score = ScoreCandidate(cand, distal, center, chosenPositions, neighborNormals, worldTris);
                    candidates[i] = cand;
                }
                
                // Pick best
                RingCandidate best = candidates[0];
                for (int i = 1; i < candidates.Count; i++)
                {
                    if (candidates[i].score > best.score)
                        best = candidates[i];
                }
                
                // Set output
                outPos = best.position + best.normal * 0.002f;
                Vector3 fwd = (best.position - tipOrigin).sqrMagnitude > 1e-8f ? (best.position - tipOrigin).normalized : (distal != null ? distal.forward : best.normal);
                Vector3 up = Vector3.ProjectOnPlane(handUp, fwd).normalized;
                if (up.sqrMagnitude < 1e-6f) up = Vector3.up;
                outRot = Quaternion.LookRotation(fwd, up);
                
                contacts.Add(new ContactPoint { position = outPos, normal = best.normal, fingerName = label, segment = 3 });
                chosenPositions.Add(best.position);
            }

            Transform Distal(bool left, HumanBodyBones l, HumanBodyBones r) => animator != null ? animator.GetBoneTransform(left ? l : r) : null;
            bool L = isLeftHand;
            SnapWorld(L?"Left Thumb":"Right Thumb", seed.thumbTip, Distal(L, HumanBodyBones.LeftThumbDistal, HumanBodyBones.RightThumbDistal), ref outTarget.thumbTip, ref outTarget.thumbRotation);
            SnapWorld(L?"Left Index":"Right Index", seed.indexTip, Distal(L, HumanBodyBones.LeftIndexDistal, HumanBodyBones.RightIndexDistal), ref outTarget.indexTip, ref outTarget.indexRotation);
            SnapWorld(L?"Left Middle":"Right Middle", seed.middleTip, Distal(L, HumanBodyBones.LeftMiddleDistal, HumanBodyBones.RightMiddleDistal), ref outTarget.middleTip, ref outTarget.middleRotation);
            SnapWorld(L?"Left Ring":"Right Ring", seed.ringTip, Distal(L, HumanBodyBones.LeftRingDistal, HumanBodyBones.RightRingDistal), ref outTarget.ringTip, ref outTarget.ringRotation);
            SnapWorld(L?"Left Little":"Right Little", seed.littleTip, Distal(L, HumanBodyBones.LeftLittleDistal, HumanBodyBones.RightLittleDistal), ref outTarget.littleTip, ref outTarget.littleRotation);

            contactPoints = contacts;
            return outTarget;
        }

        /// <summary>
        /// New FK curl-driven solver path: compute muscle values by solving a natural curl amount per finger.
        /// Guarantees 0-100 degree joint limits by construction and avoids unnatural tip rotations.
        /// </summary>
        private static GripResult GenerateCurlGripFromFingerTips(Animator animator, Transform grippedObject, AutoGripData data, bool isLeftHand)
        {
            var result = new GripResult();
            if (animator == null)
            {
                Debug.LogError("[GripGenerator] Animator is null");
                return result;
            }

            // Use snapped targets (world space) as contact intentions
            var snapped = GetSnappedTargets(animator, data, isLeftHand, out var contacts);
            result.contactPoints = contacts;

            string side = isLeftHand ? "Left" : "Right";
            // Compute muscles directly from curl t per finger (stable, directionally correct)
            Transform B(HumanBodyBones b) => animator.GetBoneTransform(b);

            void SolveFingerMuscles(string finger, HumanBodyBones prox, HumanBodyBones inter, HumanBodyBones dist, Vector3 tipTarget)
            {
                if (tipTarget == Vector3.zero) return;
                var tProx = B(prox); var tInter = B(inter); var tDist = B(dist);
                if (tProx == null || tInter == null || tDist == null) return;

                float L1 = Vector3.Distance(tProx.position, tInter.position);
                float L2 = Vector3.Distance(tInter.position, tDist.position);
                float L3 = 0.018f;
                Vector3 basePos = tProx.position;
                float reach = Vector3.Distance(basePos, tipTarget);
                float straight = Mathf.Max(0.01f, L1 + L2 + L3);
                reach = Mathf.Clamp(reach, 0.02f, straight);

                float tLow = 0f, tHigh = 1f;
                for (int it = 0; it < 18; it++)
                {
                    float t = 0.5f * (tLow + tHigh);
                    float a1 = t * 100f * Mathf.Deg2Rad;
                    float a2 = 0.7f * t * 100f * Mathf.Deg2Rad;
                    float a3 = 0.66f * (0.7f * t * 100f) * Mathf.Deg2Rad;
                    float d = L1 * Mathf.Cos(a1) + L2 * Mathf.Cos(a1 + a2) + L3 * Mathf.Cos(a1 + a2 + a3);
                    if (d > reach) tLow = t; else tHigh = t;
                }
                float tFinal = tHigh;

                // Map to Human muscle values (stretched negative when curled)
                float maxCurl = -0.8f;
                float m1 = maxCurl * Mathf.Clamp01(tFinal);
                float m2 = maxCurl * (0.7f * Mathf.Clamp01(tFinal));
                float m3 = maxCurl * (0.66f * 0.7f * Mathf.Clamp01(tFinal));

                string mName1 = GetFingerMuscleName(side, finger, 1);
                string mName2 = GetFingerMuscleName(side, finger, 2);
                string mName3 = GetFingerMuscleName(side, finger, 3);
                if (!string.IsNullOrEmpty(mName1)) result.muscleValues[mName1] = m1;
                if (!string.IsNullOrEmpty(mName2)) result.muscleValues[mName2] = m2;
                if (!string.IsNullOrEmpty(mName3)) result.muscleValues[mName3] = m3;
            }

            SolveFingerMuscles("Thumb",
                isLeftHand ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal,
                isLeftHand ? HumanBodyBones.LeftThumbIntermediate : HumanBodyBones.RightThumbIntermediate,
                isLeftHand ? HumanBodyBones.LeftThumbDistal : HumanBodyBones.RightThumbDistal,
                snapped.thumbTip);
            SolveFingerMuscles("Index",
                isLeftHand ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal,
                isLeftHand ? HumanBodyBones.LeftIndexIntermediate : HumanBodyBones.RightIndexIntermediate,
                isLeftHand ? HumanBodyBones.LeftIndexDistal : HumanBodyBones.RightIndexDistal,
                snapped.indexTip);
            SolveFingerMuscles("Middle",
                isLeftHand ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal,
                isLeftHand ? HumanBodyBones.LeftMiddleIntermediate : HumanBodyBones.RightMiddleIntermediate,
                isLeftHand ? HumanBodyBones.LeftMiddleDistal : HumanBodyBones.RightMiddleDistal,
                snapped.middleTip);
            SolveFingerMuscles("Ring",
                isLeftHand ? HumanBodyBones.LeftRingProximal : HumanBodyBones.RightRingProximal,
                isLeftHand ? HumanBodyBones.LeftRingIntermediate : HumanBodyBones.RightRingIntermediate,
                isLeftHand ? HumanBodyBones.LeftRingDistal : HumanBodyBones.RightRingDistal,
                snapped.ringTip);
            SolveFingerMuscles("Little",
                isLeftHand ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal,
                isLeftHand ? HumanBodyBones.LeftLittleIntermediate : HumanBodyBones.RightLittleIntermediate,
                isLeftHand ? HumanBodyBones.LeftLittleDistal : HumanBodyBones.RightLittleDistal,
                snapped.littleTip);

            AddSpreadValues(result, side, 0f);
            result.animation = CreateAnimationClip(result.muscleValues, isLeftHand);
            return result;
        }

        private static GripResult GenerateGripFromFingerTips(Animator animator, AutoGripData data, bool isLeftHand)
        {
            Debug.Log($"[GripGenerator] Generating grip from finger tip gizmos for {(isLeftHand ? "left" : "right")} hand");
            
            var result = new GripResult();
            
            // Get finger tip targets from gizmo positions
            var targets = new FingerTipSolver.FingerTipTarget();
            if (isLeftHand)
            {
                targets.thumbTip = data.leftThumbTip;
                targets.indexTip = data.leftIndexTip;
                targets.middleTip = data.leftMiddleTip;
                targets.ringTip = data.leftRingTip;
                targets.littleTip = data.leftLittleTip;
            }
            else
            {
                targets.thumbTip = data.rightThumbTip;
                targets.indexTip = data.rightIndexTip;
                targets.middleTip = data.rightMiddleTip;
                targets.ringTip = data.rightRingTip;
                targets.littleTip = data.rightLittleTip;
            }

            // Initialize targets if they're not set
            if (targets.thumbTip == Vector3.zero && targets.indexTip == Vector3.zero)
            {
                Debug.Log("[GripGenerator] Initializing finger tip positions from object");
                targets = FingerTipSolver.InitializeFingerTips(data.grippedObject, isLeftHand);
                
                // Update the data with initialized positions
                if (isLeftHand)
                {
                    data.leftThumbTip = targets.thumbTip;
                    data.leftIndexTip = targets.indexTip;
                    data.leftMiddleTip = targets.middleTip;
                    data.leftRingTip = targets.ringTip;
                    data.leftLittleTip = targets.littleTip;
                }
                else
                {
                    data.rightThumbTip = targets.thumbTip;
                    data.rightIndexTip = targets.indexTip;
                    data.rightMiddleTip = targets.middleTip;
                    data.rightRingTip = targets.ringTip;
                    data.rightLittleTip = targets.littleTip;
                }
            }

            // Solve for muscle values using finger tip positions
            var solverResult = FingerTipSolver.SolveFingerTips(animator, targets, isLeftHand);
            if (!solverResult.success)
            {
                Debug.LogError($"[GripGenerator] Finger tip solving failed: {solverResult.errorMessage}");
                return null;
            }

            // Copy muscle values to result
            result.muscleValues = solverResult.muscleValues;
            
            // Create contact points from finger tip positions
            CreateContactPointsFromFingerTips(targets, result, isLeftHand);
            
            // Create animation clip
            result.animation = CreateAnimationClip(result.muscleValues, isLeftHand);
            
            Debug.Log($"[GripGenerator] Finger tip grip generation complete - {result.muscleValues.Count} muscles, {result.contactPoints.Count} contacts");
            return result;
        }

        private static void CreateContactPointsFromFingerTips(FingerTipSolver.FingerTipTarget targets, GripResult result, bool isLeftHand)
        {
            string hand = isLeftHand ? "Left" : "Right";
            
            if (targets.thumbTip != Vector3.zero)
                result.contactPoints.Add(new ContactPoint { position = targets.thumbTip, normal = Vector3.up, fingerName = $"{hand} Thumb", segment = 3 });
            
            if (targets.indexTip != Vector3.zero)
                result.contactPoints.Add(new ContactPoint { position = targets.indexTip, normal = Vector3.up, fingerName = $"{hand} Index", segment = 3 });
            
            if (targets.middleTip != Vector3.zero)
                result.contactPoints.Add(new ContactPoint { position = targets.middleTip, normal = Vector3.up, fingerName = $"{hand} Middle", segment = 3 });
            
            if (targets.ringTip != Vector3.zero)
                result.contactPoints.Add(new ContactPoint { position = targets.ringTip, normal = Vector3.up, fingerName = $"{hand} Ring", segment = 3 });
            
            if (targets.littleTip != Vector3.zero)
                result.contactPoints.Add(new ContactPoint { position = targets.littleTip, normal = Vector3.up, fingerName = $"{hand} Little", segment = 3 });
        }

        private static void CalculateFingerMuscles(
            HandAnalyzer.HandData handData,
            Transform grippedObject,
            Vector3 gripPoint,
            AutoGripData data,
            GripResult result,
            bool isLeftHand)
        {
            string side = isLeftHand ? "Left" : "Right";

            ProcessFinger(handData.indexSegments, "Index", side, grippedObject, gripPoint, data, result, true);
            ProcessFinger(handData.middleSegments, "Middle", side, grippedObject, gripPoint, data, result, true);
            ProcessFinger(handData.ringSegments, "Ring", side, grippedObject, gripPoint, data, result, true);
            ProcessFinger(handData.littleSegments, "Little", side, grippedObject, gripPoint, data, result, true);
            ProcessFinger(handData.thumbSegments, "Thumb", side, grippedObject, gripPoint, data, result, true);

            AddSpreadValues(result, side, 0f);
        }

        private static void ProcessFinger(
            List<HandAnalyzer.FingerSegment> segments,
            string fingerName,
            string side,
            Transform grippedObject,
            Vector3 gripPoint,
            AutoGripData data,
            GripResult result,
            bool shouldCurl)
        {
            if (segments.Count == 0) return;

            for (int i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                float curlValue = 0f;

                if (shouldCurl)
                {
                    curlValue = CalculateSegmentCurl(
                        segment,
                        grippedObject,
                        gripPoint,
                        data,
                        result
                    );

                    float progressiveFactor = 1.0f - (i * 0.2f);
                    curlValue *= progressiveFactor;
                }

                // Get actual muscle name from Unity's HumanTrait system
                string actualMuscleName = GetFingerMuscleName(side, fingerName, i + 1);
                if (!string.IsNullOrEmpty(actualMuscleName))
                {
                    result.muscleValues[actualMuscleName] = curlValue;
                    Debug.Log($"[GripGenerator] Using muscle: {actualMuscleName} = {curlValue}");
                }
                else
                {
                    Debug.LogWarning($"[GripGenerator] Could not find muscle for {side} {fingerName} {i + 1}");
                }
            }
        }

        // Convert HumanTrait muscle name to .anim file format
        // "Left Index 1 Stretched" -> "LeftHand.Index.1 Stretched"
        // "Left Index Spread" -> "LeftHand.Index.Spread"
        // "Right Thumb 2 Stretched" -> "RightHand.Thumb.2 Stretched"
        private static string ConvertToAnimFormat(string humanTraitName)
        {
            if (string.IsNullOrEmpty(humanTraitName)) return null;
            
            // Replace "Left " with "LeftHand." or "Right " with "RightHand."
            string result = humanTraitName;
            result = result.Replace("Left Thumb", "LeftHand.Thumb");
            result = result.Replace("Left Index", "LeftHand.Index");
            result = result.Replace("Left Middle", "LeftHand.Middle");
            result = result.Replace("Left Ring", "LeftHand.Ring");
            result = result.Replace("Left Little", "LeftHand.Little");
            
            result = result.Replace("Right Thumb", "RightHand.Thumb");
            result = result.Replace("Right Index", "RightHand.Index");
            result = result.Replace("Right Middle", "RightHand.Middle");
            result = result.Replace("Right Ring", "RightHand.Ring");
            result = result.Replace("Right Little", "RightHand.Little");
            
            // Replace segment numbers: " 1 " -> ".1 ", " 2 " -> ".2 ", " 3 " -> ".3 "
            result = result.Replace(" 1 Stretched", ".1 Stretched");
            result = result.Replace(" 2 Stretched", ".2 Stretched");
            result = result.Replace(" 3 Stretched", ".3 Stretched");
            
            // Replace spread: " Spread" -> ".Spread"
            result = result.Replace(" Spread", ".Spread");
            
            return result;
        }
        
        private static string GetFingerMuscleName(string side, string fingerName, int segment)
        {
            string[] muscleNames = HumanTrait.MuscleName;
            string sidePrefix = side == "Left" ? "Left" : "Right";
            string fingerPattern = fingerName.ToLower();
            
            foreach (string muscleName in muscleNames)
            {
                string lowerMuscleName = muscleName.ToLower();
                
                // Check if this muscle matches our finger and side
                if (lowerMuscleName.Contains(sidePrefix.ToLower()) && 
                    lowerMuscleName.Contains(fingerPattern) && 
                    lowerMuscleName.Contains("stretched"))
                {
                    // Check if this is the right segment - look for the segment number
                    if (lowerMuscleName.Contains($" {segment} "))
                    {
                        // Convert to .anim format
                        string animFormatName = ConvertToAnimFormat(muscleName);
                        Debug.Log($"[GripGenerator] Found muscle: {muscleName} -> {animFormatName}");
                        return animFormatName;
                    }
                }
            }
            
            Debug.LogWarning($"[GripGenerator] Could not find muscle for {side} {fingerName} {segment}");
            return null;
        }

        private static string GetFingerSpreadMuscleName(string side, string finger)
        {
            string[] muscleNames = HumanTrait.MuscleName;
            string sidePrefix = side == "Left" ? "Left" : "Right";
            string fingerPattern = finger.ToLower();
            
            foreach (string muscleName in muscleNames)
            {
                string lowerMuscleName = muscleName.ToLower();
                
                // Check if this muscle matches our finger and side for spread
                if (lowerMuscleName.Contains(sidePrefix.ToLower()) && 
                    lowerMuscleName.Contains(fingerPattern) && 
                    lowerMuscleName.Contains("spread"))
                {
                    // Convert to .anim format
                    string animFormatName = ConvertToAnimFormat(muscleName);
                    Debug.Log($"[GripGenerator] Found spread muscle: {muscleName} -> {animFormatName}");
                    return animFormatName;
                }
            }
            
            return null;
        }

        private static float CalculateSegmentCurl(
            HandAnalyzer.FingerSegment segment,
            Transform grippedObject,
            Vector3 gripPoint,
            AutoGripData data,
            GripResult result)
        {
            Vector3 segmentPos = segment.transform.position;
            float distanceToGrip = Vector3.Distance(segmentPos, gripPoint);
            
            Debug.Log($"[GripGenerator] Segment {segment.bone} - Distance to grip point: {distanceToGrip:F4}m");
            
            float curlValue = CalculateCurlFromDistance(distanceToGrip, 1.0f);
            
            result.contactPoints.Add(new ContactPoint
            {
                position = gripPoint,
                normal = Vector3.up,
                fingerName = segment.bone.ToString(),
                segment = GetSegmentIndex(segment.bone)
            });
            
            Debug.Log($"[GripGenerator] Segment {segment.bone} curl value: {curlValue:F2}");
            
            return curlValue;
        }
        
        private static float CalculateCurlFromDistance(float distance, float gripStrength)
        {
            if (distance > 0.15f)
            {
                return 0.5f;
            }
            
            if (distance < 0.05f)
            {
                return Mathf.Lerp(-0.2f, -0.8f, gripStrength);
            }
            
            float t = (distance - 0.05f) / (0.15f - 0.05f);
            float baseCurl = Mathf.Lerp(-0.8f, 0.5f, t);
            return Mathf.Lerp(baseCurl * 0.5f, baseCurl, gripStrength);
        }

        private static float CalculateFallbackCurl(
            Transform bone,
            Transform grippedObject,
            Vector3 gripPoint,
            AutoGripData data,
            GripResult result)
        {
            Vector3 bonePos = bone.position;
            Vector3 direction = (gripPoint - bonePos).normalized;
            
            var objectMeshes = GetObjectMeshTriangles(grippedObject);
            Ray ray = new Ray(bonePos, direction);

            if (RayIntersectsMesh(ray, objectMeshes, 0.1f, out float hitDistance, out Vector3 hitPoint, out Vector3 hitNormal))
            {
                result.contactPoints.Add(new ContactPoint
                {
                    position = hitPoint,
                    normal = hitNormal,
                    fingerName = bone.name,
                    segment = 0
                });
                
                float distance = hitDistance - 0.01f;
                float curlRatio = 1.0f - Mathf.Clamp01(distance / 0.04f);
                return Mathf.Lerp(0.5f, -0.8f, curlRatio * 1.0f);
            }

            return 0f;
        }

        private struct Triangle
        {
            public Vector3 v0, v1, v2;
            
            public Triangle(Vector3 v0, Vector3 v1, Vector3 v2)
            {
                this.v0 = v0;
                this.v1 = v1;
                this.v2 = v2;
            }
        }

        private static List<Triangle> GetObjectMeshTriangles(Transform obj)
        {
            var triangles = new List<Triangle>();
            var meshFilters = obj.GetComponentsInChildren<MeshFilter>();
            var skinnedMeshRenderers = obj.GetComponentsInChildren<SkinnedMeshRenderer>();

            foreach (var mf in meshFilters)
            {
                if (mf.sharedMesh != null)
                {
                    AddMeshTriangles(triangles, mf.sharedMesh, mf.transform);
                }
            }

            foreach (var smr in skinnedMeshRenderers)
            {
                if (smr.sharedMesh != null)
                {
                    AddMeshTriangles(triangles, smr.sharedMesh, smr.transform);
                }
            }

            Debug.Log($"[GripGenerator] Found {triangles.Count} triangles on object");
            return triangles;
        }

        private static void AddMeshTriangles(List<Triangle> triangles, Mesh mesh, Transform transform)
        {
            Vector3[] vertices = mesh.vertices;
            int[] indices = mesh.triangles;

            for (int i = 0; i < indices.Length; i += 3)
            {
                Vector3 v0 = transform.TransformPoint(vertices[indices[i]]);
                Vector3 v1 = transform.TransformPoint(vertices[indices[i + 1]]);
                Vector3 v2 = transform.TransformPoint(vertices[indices[i + 2]]);

                triangles.Add(new Triangle(v0, v1, v2));
            }
        }

        private static bool RayIntersectsMesh(
            Ray ray,
            List<Triangle> triangles,
            float maxDistance,
            out float hitDistance,
            out Vector3 hitPoint,
            out Vector3 hitNormal)
        {
            hitDistance = float.MaxValue;
            hitPoint = Vector3.zero;
            hitNormal = Vector3.up;
            bool hit = false;

            foreach (var triangle in triangles)
            {
                if (RayIntersectsTriangle(ray, triangle, out float distance))
                {
                    if (distance > 0 && distance < maxDistance && distance < hitDistance)
                    {
                        hitDistance = distance;
                        hitPoint = ray.origin + ray.direction * distance;
                        hitNormal = Vector3.Cross(triangle.v1 - triangle.v0, triangle.v2 - triangle.v0).normalized;
                        hit = true;
                    }
                }
            }

            return hit;
        }

        private static bool RayIntersectsTriangle(Ray ray, Triangle triangle, out float distance)
        {
            const float EPSILON = 0.0000001f;
            distance = 0;

            Vector3 edge1 = triangle.v1 - triangle.v0;
            Vector3 edge2 = triangle.v2 - triangle.v0;
            Vector3 h = Vector3.Cross(ray.direction, edge2);
            float a = Vector3.Dot(edge1, h);

            if (a > -EPSILON && a < EPSILON)
                return false;

            float f = 1.0f / a;
            Vector3 s = ray.origin - triangle.v0;
            float u = f * Vector3.Dot(s, h);

            if (u < 0.0f || u > 1.0f)
                return false;

            Vector3 q = Vector3.Cross(s, edge1);
            float v = f * Vector3.Dot(ray.direction, q);

            if (v < 0.0f || u + v > 1.0f)
                return false;

            float t = f * Vector3.Dot(edge2, q);

            if (t > EPSILON)
            {
                distance = t;
                return true;
            }

            return false;
        }

        // Compute closest point on a triangle in world space
        private static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            // From "Real-Time Collision Detection" (Christer Ericson)
            Vector3 ab = b - a; Vector3 ac = c - a; Vector3 ap = p - a;
            float d1 = Vector3.Dot(ab, ap);
            float d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;

            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp);
            float d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;

            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            {
                float v = d1 / (d1 - d3);
                return a + v * ab;
            }

            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp);
            float d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;

            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            {
                float w = d2 / (d2 - d6);
                return a + w * ac;
            }

            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
            {
                float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                return b + w * (c - b);
            }

            float denom = 1f / (va + vb + vc);
            float v2 = vb * denom; float w2 = vc * denom; float u2 = 1f - v2 - w2;
            return u2 * a + v2 * b + w2 * c;
        }

        private struct RingCandidate
        {
            public Vector3 position;
            public Vector3 normal;
            public float score;
        }

        /// <summary>
        /// Sample K candidates on a tangential ring around origin, project to mesh surface.
        /// </summary>
        private static List<RingCandidate> SampleRingCandidates(
            Vector3 origin,
            Vector3 frameX,
            Vector3 frameY,
            float radius,
            int K,
            List<Triangle> triangles,
            Vector3 objectCenter)
        {
            var candidates = new List<RingCandidate>();
            float angleStep = 360f / K;
            
            for (int i = 0; i < K; i++)
            {
                float angle = i * angleStep * Mathf.Deg2Rad;
                Vector3 ringPoint = origin + radius * (Mathf.Cos(angle) * frameX + Mathf.Sin(angle) * frameY);
                
                // Find closest point on mesh triangles
                Vector3 bestPt = ringPoint;
                Vector3 bestN = Vector3.up;
                float bestD2 = float.MaxValue;
                
                foreach (var tri in triangles)
                {
                    Vector3 cp = ClosestPointOnTriangle(ringPoint, tri.v0, tri.v1, tri.v2);
                    float d2 = (cp - ringPoint).sqrMagnitude;
                    if (d2 < bestD2)
                    {
                        bestD2 = d2;
                        bestPt = cp;
                        bestN = Vector3.Cross(tri.v1 - tri.v0, tri.v2 - tri.v0).normalized;
                    }
                }
                
                // Ensure outward normal
                if (Vector3.Dot(bestN, (bestPt - objectCenter)) < 0f)
                    bestN = -bestN;
                
                candidates.Add(new RingCandidate
                {
                    position = bestPt,
                    normal = bestN,
                    score = 0f
                });
            }
            
            return candidates;
        }

        /// <summary>
        /// Estimate local thickness at a point by probing along ±normal.
        /// </summary>
        private static float FindThickness(Vector3 point, Vector3 normal, List<Triangle> triangles, float probeDistance)
        {
            Vector3 probeOut = point + normal * probeDistance;
            Vector3 probeIn = point - normal * probeDistance;
            
            float distOut = probeDistance;
            float distIn = probeDistance;
            
            // Find closest triangle distance from each probe
            foreach (var tri in triangles)
            {
                Vector3 cpOut = ClosestPointOnTriangle(probeOut, tri.v0, tri.v1, tri.v2);
                Vector3 cpIn = ClosestPointOnTriangle(probeIn, tri.v0, tri.v1, tri.v2);
                
                float dOut = Vector3.Distance(probeOut, cpOut);
                float dIn = Vector3.Distance(probeIn, cpIn);
                
                if (dOut < distOut) distOut = dOut;
                if (dIn < distIn) distIn = dIn;
            }
            
            return distOut + distIn;
        }

        /// <summary>
        /// Estimate curvature by comparing normal to neighbors.
        /// </summary>
        private static float EstimateCurvature(Vector3 normal, List<Vector3> neighborNormals)
        {
            if (neighborNormals.Count == 0) return 0f;
            
            float maxDelta = 0f;
            foreach (var n in neighborNormals)
            {
                float delta = Vector3.Angle(normal, n);
                if (delta > maxDelta) maxDelta = delta;
            }
            
            return maxDelta;
        }

        /// <summary>
        /// Score a contact candidate based on multiple criteria.
        /// </summary>
        private static float ScoreCandidate(
            RingCandidate candidate,
            Transform distalBone,
            Vector3 objectCenter,
            List<Vector3> alreadyChosen,
            List<Vector3> neighborNormals,
            List<Triangle> triangles)
        {
            const float wThin = 3.0f;
            const float wCurv = 2.0f;
            const float wAlign = 1.5f;
            const float wFlat = 1.0f;
            const float wSpace = -4.0f;
            const float minSpacing = 0.015f;
            const float thickThresh = 0.008f;
            
            float score = 0f;
            
            // Thin bonus
            float thickness = FindThickness(candidate.position, candidate.normal, triangles, 0.005f);
            if (thickness < thickThresh)
            {
                score += wThin * (1f - thickness / thickThresh);
            }
            
            // Curvature
            float curvature = EstimateCurvature(candidate.normal, neighborNormals);
            score += wCurv * (curvature / 180f);
            
            // Approach alignment
            if (distalBone != null)
            {
                float alignment = -Vector3.Dot(candidate.normal, distalBone.forward);
                if (alignment > 0f)
                    score += wAlign * alignment;
            }
            
            // Anti-flat
            Vector3 toCenter = (objectCenter - candidate.position).normalized;
            float flatness = Mathf.Abs(Vector3.Dot(candidate.normal, toCenter));
            if (flatness < 0.3f)
                score += wFlat * (0.3f - flatness);
            
            // Spacing penalty
            foreach (var chosen in alreadyChosen)
            {
                float dist = Vector3.Distance(candidate.position, chosen);
                if (dist < minSpacing)
                {
                    score += wSpace * (1f - dist / minSpacing);
                }
            }
            
            return score;
        }

        private static bool IsPartOfObject(Transform hit, Transform target)
        {
            Transform current = hit;
            while (current != null)
            {
                if (current == target) return true;
                current = current.parent;
            }
            return false;
        }

        private static void AddSpreadValues(GripResult result, string side, float spreadAdjustment)
        {
            float baseSpread = 0.0f; // Manual grip uses default spread
            float finalSpread = baseSpread + spreadAdjustment;

            string[] fingers = { "Index", "Middle", "Ring", "Little", "Thumb" };
            foreach (var finger in fingers)
            {
                string actualMuscleName = GetFingerSpreadMuscleName(side, finger);
                if (!string.IsNullOrEmpty(actualMuscleName))
                {
                    result.muscleValues[actualMuscleName] = finalSpread;
                    Debug.Log($"[GripGenerator] Using spread muscle: {actualMuscleName} = {finalSpread}");
                }
                else
                {
                    Debug.LogWarning($"[GripGenerator] Could not find spread muscle for {side} {finger}");
                }
            }
        }

        private static AnimationClip CreateAnimationClip(Dictionary<string, float> muscleValues, bool isLeftHand)
        {
            AnimationClip clip = new AnimationClip();
            clip.name = $"AutoGrip_{(isLeftHand ? "Left" : "Right")}";
            clip.legacy = false;

            foreach (var kvp in muscleValues)
            {
                string muscleName = kvp.Key;
                float value = kvp.Value;

                AnimationCurve curve = AnimationCurve.Constant(0, 0, value);

                EditorCurveBinding binding = EditorCurveBinding.FloatCurve(
                    "",
                    typeof(Animator),
                    muscleName
                );

                AnimationUtility.SetEditorCurve(clip, binding, curve);
            }

            return clip;
        }

        private static Vector3 CalculateGripOffset(Transform handBone, Vector3 gripPoint)
        {
            return handBone.InverseTransformPoint(gripPoint);
        }

        private static int GetSegmentIndex(HumanBodyBones bone)
        {
            string boneName = bone.ToString();
            if (boneName.Contains("Proximal")) return 1;
            if (boneName.Contains("Intermediate")) return 2;
            if (boneName.Contains("Distal")) return 3;
            return 0;
        }

        public static string GetMuscleNameForBone(HumanBodyBones bone, int property)
        {
            string boneName = bone.ToString();
            bool isLeft = boneName.StartsWith("Left");
            string side = isLeft ? "Left" : "Right";

            string finger = "";
            if (boneName.Contains("Index")) finger = "Index";
            else if (boneName.Contains("Middle")) finger = "Middle";
            else if (boneName.Contains("Ring")) finger = "Ring";
            else if (boneName.Contains("Little")) finger = "Little";
            else if (boneName.Contains("Thumb")) finger = "Thumb";

            int segment = GetSegmentIndex(bone);
            string propertyName = property == 0 ? "Stretched" : "Spread";

            return $"{side}Hand.{finger}.{segment} {propertyName}";
        }
    }
}

