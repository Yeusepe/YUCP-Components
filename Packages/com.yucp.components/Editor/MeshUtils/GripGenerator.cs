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
            
            return GenerateGripFromFingerTips(animator, data, isLeftHand);
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

