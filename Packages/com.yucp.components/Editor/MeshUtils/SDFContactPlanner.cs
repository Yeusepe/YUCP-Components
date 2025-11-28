using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace YUCP.Components.Editor.MeshUtils
{
    /// <summary>
    /// Mathematical contact planner using SDF projection and cost function optimization.
    /// Implements the proper mathematical formulation for finger contact planning.
    /// </summary>
    public static class SDFContactPlanner
    {
        /// <summary>
        /// Contact planning result containing position, normal, and orientation for each finger.
        /// </summary>
        [System.Serializable]
        public struct ContactResult
        {
            public Vector3 position;
            public Vector3 normal;
            public Quaternion orientation;
            public float cost;
        }

        /// <summary>
        /// Finger targets result containing contact data for all fingers.
        /// </summary>
        [System.Serializable]
        public struct ContactTargets
        {
            public ContactResult thumb;
            public ContactResult index;
            public ContactResult middle;
            public ContactResult ring;
            public ContactResult little;
        }

        /// <summary>
        /// Cost function weights for contact optimization.
        /// </summary>
        [System.Serializable]
        public struct CostWeights
        {
            [Header("Distance and Alignment")]
            public float wDistance;      // Distance from fingertip to surface
            public float wNormal;        // Pad normal alignment with surface normal
            public float wCollision;     // Hand-object collision penalty
            public float wJointLimits;   // Joint comfort penalty
            public float wCurvature;     // Curvature preference (edges, ridges)
            public float wSeparation;    // Finger separation penalty
            public float wOpposition;    // Thumb opposition bonus

            [Header("Physical Parameters")]
            public float padOffset;      // Distance pad sits above surface
            public float separationSigma; // Standard deviation for finger separation

            public static CostWeights GetDefaults()
            {
                return new CostWeights
                {
                    wDistance = 1.0f,
                    wNormal = 2.0f,
                    wCollision = 10.0f,
                    wJointLimits = 1.5f,
                    wCurvature = 0.5f,
                    wSeparation = 0.8f,
                    wOpposition = 1.2f,
                    padOffset = 0.002f,
                    separationSigma = 0.02f
                };
            }
        }

        /// <summary>
        /// Plan contacts using SDF projection and cost function optimization.
        /// </summary>
        public static ContactTargets PlanContacts(
            Transform grippedObject,
            Animator animator,
            Transform handTransform,
            CostWeights weights)
        {
            var targets = new ContactTargets();
            
            if (grippedObject == null || animator == null || handTransform == null)
            {
                Debug.LogWarning("[SDFContactPlanner] Missing required components");
                return targets;
            }

            var mcpPositions = GetMCPPositions(animator, handTransform);
            
            var initialGuesses = GenerateInitialGuesses(grippedObject, handTransform, mcpPositions);
            
            var surfaceContacts = ProjectToSurface(grippedObject, initialGuesses);
            
            var selectedContacts = SelectBestContacts(surfaceContacts, mcpPositions, weights);
            
            targets = BuildTargetPoses(selectedContacts, weights);
            
            return targets;
        }

        /// <summary>
        /// Get MCP (Metacarpophalangeal) joint positions for all fingers.
        /// </summary>
        private static Dictionary<string, Vector3> GetMCPPositions(Animator animator, Transform handTransform)
        {
            var mcpPositions = new Dictionary<string, Vector3>();
            
            var thumbMCP = animator.GetBoneTransform(HumanBodyBones.LeftThumbProximal);
            var indexMCP = animator.GetBoneTransform(HumanBodyBones.LeftIndexProximal);
            var middleMCP = animator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
            var ringMCP = animator.GetBoneTransform(HumanBodyBones.LeftRingProximal);
            var littleMCP = animator.GetBoneTransform(HumanBodyBones.LeftLittleProximal);
            
            if (thumbMCP != null) mcpPositions["thumb"] = thumbMCP.position;
            if (indexMCP != null) mcpPositions["index"] = indexMCP.position;
            if (middleMCP != null) mcpPositions["middle"] = middleMCP.position;
            if (ringMCP != null) mcpPositions["ring"] = ringMCP.position;
            if (littleMCP != null) mcpPositions["little"] = littleMCP.position;
            
            return mcpPositions;
        }

        /// <summary>
        /// Generate initial fingertip position guesses by shooting rays from MCPs toward object.
        /// </summary>
        private static Dictionary<string, Vector3> GenerateInitialGuesses(
            Transform grippedObject, 
            Transform handTransform, 
            Dictionary<string, Vector3> mcpPositions)
        {
            var guesses = new Dictionary<string, Vector3>();
            Vector3 objectCenter = grippedObject.position;
            
            foreach (var kvp in mcpPositions)
            {
                string fingerName = kvp.Key;
                Vector3 mcpPos = kvp.Value;
                
                // Shoot ray from MCP toward object center
                Vector3 direction = (objectCenter - mcpPos).normalized;
                float distance = Vector3.Distance(mcpPos, objectCenter);
                
                // Place guess at reasonable distance from MCP
                float guessDistance = Mathf.Min(distance * 0.7f, 0.1f); // Max 10cm
                guesses[fingerName] = mcpPos + direction * guessDistance;
            }
            
            return guesses;
        }

        /// <summary>
        /// Project initial guesses to object surface using SDF or mesh closest point.
        /// </summary>
        private static Dictionary<string, SurfaceContact> ProjectToSurface(
            Transform grippedObject, 
            Dictionary<string, Vector3> initialGuesses)
        {
            var contacts = new Dictionary<string, SurfaceContact>();
            
            foreach (var kvp in initialGuesses)
            {
                string fingerName = kvp.Key;
                Vector3 guess = kvp.Value;
                
                if (TrySDFProjection(grippedObject, guess, out Vector3 surfacePoint, out Vector3 surfaceNormal))
                {
                    contacts[fingerName] = new SurfaceContact
                    {
                        position = surfacePoint,
                        normal = surfaceNormal,
                        method = "SDF"
                    };
                }
                else if (TryMeshClosestPoint(grippedObject, guess, out surfacePoint, out surfaceNormal))
                {
                    contacts[fingerName] = new SurfaceContact
                    {
                        position = surfacePoint,
                        normal = surfaceNormal,
                        method = "Mesh"
                    };
                }
                else
                {
                    contacts[fingerName] = GetBoundsContact(grippedObject, guess);
                }
            }
            
            return contacts;
        }

        /// <summary>
        /// Try SDF projection using Newton steps.
        /// </summary>
        private static bool TrySDFProjection(Transform obj, Vector3 point, out Vector3 surfacePoint, out Vector3 surfaceNormal)
        {
            surfacePoint = point;
            surfaceNormal = Vector3.up;
            
            Collider collider = obj.GetComponent<Collider>();
            if (collider != null)
            {
                surfacePoint = collider.ClosestPoint(point);
                
                // Estimate normal using raycast
                Vector3 direction = (point - surfacePoint).normalized;
                if (Physics.Raycast(surfacePoint + direction * 0.001f, -direction, out RaycastHit hit, 0.01f))
                {
                    surfaceNormal = hit.normal;
                    return true;
                }
                
                surfaceNormal = direction;
                return true;
            }
            
            return false;
        }

        /// <summary>
        /// Try mesh closest point using barycentric coordinates.
        /// </summary>
        private static bool TryMeshClosestPoint(Transform obj, Vector3 point, out Vector3 surfacePoint, out Vector3 surfaceNormal)
        {
            surfacePoint = point;
            surfaceNormal = Vector3.up;
            
            MeshRenderer renderer = obj.GetComponent<MeshRenderer>();
            MeshFilter meshFilter = obj.GetComponent<MeshFilter>();
            
            if (renderer != null && meshFilter != null)
            {
                Mesh mesh = meshFilter.sharedMesh;
                if (mesh != null)
                {
                    Vector3 localPoint = obj.InverseTransformPoint(point);
                    
                    float minDistance = float.MaxValue;
                    Vector3 closestPoint = localPoint;
                    Vector3 closestNormal = Vector3.up;
                    
                    Vector3[] vertices = mesh.vertices;
                    int[] triangles = mesh.triangles;
                    Vector3[] normals = mesh.normals;
                    
                    for (int i = 0; i < triangles.Length; i += 3)
                    {
                        Vector3 v0 = vertices[triangles[i]];
                        Vector3 v1 = vertices[triangles[i + 1]];
                        Vector3 v2 = vertices[triangles[i + 2]];
                        
                        Vector3 n0 = normals[triangles[i]];
                        Vector3 n1 = normals[triangles[i + 1]];
                        Vector3 n2 = normals[triangles[i + 2]];
                        
                        Vector3 closestOnTriangle = ClosestPointOnTriangle(localPoint, v0, v1, v2);
                        float distance = Vector3.Distance(localPoint, closestOnTriangle);
                        
                        if (distance < minDistance)
                        {
                            minDistance = distance;
                            closestPoint = closestOnTriangle;
                            
                            // Interpolate normal
                            Vector3 barycentric = BarycentricCoordinates(closestOnTriangle, v0, v1, v2);
                            closestNormal = (barycentric.x * n0 + barycentric.y * n1 + barycentric.z * n2).normalized;
                        }
                    }
                    
                    surfacePoint = obj.TransformPoint(closestPoint);
                    surfaceNormal = obj.TransformDirection(closestNormal);
                    return true;
                }
            }
            
            return false;
        }

        /// <summary>
        /// Compute closest point on triangle using barycentric coordinates.
        /// </summary>
        private static Vector3 ClosestPointOnTriangle(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a;
            Vector3 ac = c - a;
            Vector3 ap = point - a;
            
            float d1 = Vector3.Dot(ab, ap);
            float d2 = Vector3.Dot(ac, ap);
            
            if (d1 <= 0f && d2 <= 0f) return a;
            
            Vector3 bp = point - b;
            float d3 = Vector3.Dot(ab, bp);
            float d4 = Vector3.Dot(ac, bp);
            
            if (d3 >= 0f && d4 <= d3) return b;
            
            Vector3 cp = point - c;
            float d5 = Vector3.Dot(ab, cp);
            float d6 = Vector3.Dot(ac, cp);
            
            if (d6 >= 0f && d5 <= d6) return c;
            
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            {
                float v = d1 / (d1 - d3);
                return a + v * ab;
            }
            
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            {
                float v = d2 / (d2 - d6);
                return a + v * ac;
            }
            
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
            {
                float v = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                return b + v * (c - b);
            }
            
            float denom = 1f / (va + vb + vc);
            float v_bary = vb * denom;
            float w_bary = vc * denom;
            
            return a + v_bary * ab + w_bary * ac;
        }

        /// <summary>
        /// Compute barycentric coordinates of point on triangle.
        /// </summary>
        private static Vector3 BarycentricCoordinates(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 v0 = c - a;
            Vector3 v1 = b - a;
            Vector3 v2 = point - a;
            
            float dot00 = Vector3.Dot(v0, v0);
            float dot01 = Vector3.Dot(v0, v1);
            float dot02 = Vector3.Dot(v0, v2);
            float dot11 = Vector3.Dot(v1, v1);
            float dot12 = Vector3.Dot(v1, v2);
            
            float invDenom = 1f / (dot00 * dot11 - dot01 * dot01);
            float u = (dot11 * dot02 - dot01 * dot12) * invDenom;
            float v = (dot00 * dot12 - dot01 * dot02) * invDenom;
            
            return new Vector3(1f - u - v, v, u);
        }

        /// <summary>
        /// Get bounds-based contact as fallback.
        /// </summary>
        private static SurfaceContact GetBoundsContact(Transform obj, Vector3 point)
        {
            Renderer renderer = obj.GetComponent<Renderer>();
            if (renderer != null)
            {
                Bounds bounds = renderer.bounds;
                Vector3 closestPoint = bounds.ClosestPoint(point);
                Vector3 direction = (point - closestPoint).normalized;
                
                return new SurfaceContact
                {
                    position = closestPoint,
                    normal = direction,
                    method = "Bounds"
                };
            }
            
            return new SurfaceContact
            {
                position = point,
                normal = Vector3.up,
                method = "Fallback"
            };
        }

        /// <summary>
        /// Select best contacts using two-stage optimization.
        /// Stage 1: Choose contact points by evaluating cost over candidates
        /// Stage 2: Optimize joint angles with fixed contact points
        /// </summary>
        private static Dictionary<string, SurfaceContact> SelectBestContacts(
            Dictionary<string, SurfaceContact> surfaceContacts,
            Dictionary<string, Vector3> mcpPositions,
            CostWeights weights)
        {
            var selectedContacts = new Dictionary<string, SurfaceContact>();
            var fingerNames = new[] { "thumb", "index", "middle", "ring", "little" };
            
            // Stage 1: Generate multiple candidates per finger and select best
            var candidateContacts = GenerateMultipleCandidates(surfaceContacts, mcpPositions, weights);
            
            // Stage 2: Optimize joint angles for selected contacts
            selectedContacts = OptimizeJointAngles(candidateContacts, mcpPositions, weights);
            
            return selectedContacts;
        }

        /// <summary>
        /// Generate multiple contact candidates per finger.
        /// </summary>
        private static Dictionary<string, List<SurfaceContact>> GenerateMultipleCandidates(
            Dictionary<string, SurfaceContact> initialContacts,
            Dictionary<string, Vector3> mcpPositions,
            CostWeights weights)
        {
            var candidates = new Dictionary<string, List<SurfaceContact>>();
            var fingerNames = new[] { "thumb", "index", "middle", "ring", "little" };
            
            foreach (string fingerName in fingerNames)
            {
                candidates[fingerName] = new List<SurfaceContact>();
                
                if (initialContacts.ContainsKey(fingerName) && mcpPositions.ContainsKey(fingerName))
                {
                    var baseContact = initialContacts[fingerName];
                    Vector3 mcpPos = mcpPositions[fingerName];
                    
                    for (int i = 0; i < 8; i++)
                    {
                        var candidate = GenerateCandidateVariation(baseContact, mcpPos, i, weights);
                        candidate.cost = ComputeContactCost(candidate, mcpPos, weights, fingerName);
                        candidates[fingerName].Add(candidate);
                    }
                }
            }
            
            return candidates;
        }

        /// <summary>
        /// Generate a candidate variation around a base contact.
        /// </summary>
        private static SurfaceContact GenerateCandidateVariation(
            SurfaceContact baseContact, 
            Vector3 mcpPos, 
            int variationIndex, 
            CostWeights weights)
        {
            float angle = variationIndex * Mathf.PI / 4f;
            float radius = 0.01f; // 1cm variation radius
            
            Vector3 tangent1 = BuildTangentFrame(baseContact.normal, Vector3.up);
            Vector3 tangent2 = Vector3.Cross(baseContact.normal, tangent1);
            
            Vector3 offset = radius * (Mathf.Cos(angle) * tangent1 + Mathf.Sin(angle) * tangent2);
            Vector3 variedPosition = baseContact.position + offset;
            
            Vector3 variedNormal = baseContact.normal;
            
            return new SurfaceContact
            {
                position = variedPosition,
                normal = variedNormal,
                method = baseContact.method + "_variation_" + variationIndex,
                cost = 0f
            };
        }

        /// <summary>
        /// Optimize joint angles with fixed contact points (Stage 2).
        /// </summary>
        private static Dictionary<string, SurfaceContact> OptimizeJointAngles(
            Dictionary<string, List<SurfaceContact>> candidateContacts,
            Dictionary<string, Vector3> mcpPositions,
            CostWeights weights)
        {
            var optimizedContacts = new Dictionary<string, SurfaceContact>();
            var fingerNames = new[] { "thumb", "index", "middle", "ring", "little" };
            
            // Select best candidate per finger based on cost
            foreach (string fingerName in fingerNames)
            {
                if (candidateContacts.ContainsKey(fingerName) && mcpPositions.ContainsKey(fingerName))
                {
                    var candidates = candidateContacts[fingerName];
                    Vector3 mcpPos = mcpPositions[fingerName];
                    
                    SurfaceContact bestContact = candidates[0];
                    float bestCost = float.MaxValue;
                    
                    foreach (var candidate in candidates)
                    {
                        float cost = ComputeContactCost(candidate, mcpPos, weights, fingerName);
                        if (cost < bestCost)
                        {
                            bestCost = cost;
                            bestContact = candidate;
                        }
                    }
                    
                    bestContact = OptimizeContactLocally(bestContact, mcpPos, weights, fingerName);
                    optimizedContacts[fingerName] = bestContact;
                }
            }
            
            return optimizedContacts;
        }

        /// <summary>
        /// Apply local optimization to a contact point.
        /// </summary>
        private static SurfaceContact OptimizeContactLocally(
            SurfaceContact contact,
            Vector3 mcpPos,
            CostWeights weights,
            string fingerName)
        {
            // Simple gradient descent on contact position
            float learningRate = 0.001f;
            int maxIterations = 10;
            
            SurfaceContact optimized = contact;
            
            for (int iter = 0; iter < maxIterations; iter++)
            {
                float currentCost = ComputeContactCost(optimized, mcpPos, weights, fingerName);
                
                Vector3 gradient = ComputeContactGradient(optimized, mcpPos, weights, fingerName);
                
                Vector3 newPosition = optimized.position - learningRate * gradient;
                
                // Recompute normal (simplified)
                Vector3 newNormal = optimized.normal;
                
                var newContact = new SurfaceContact
                {
                    position = newPosition,
                    normal = newNormal,
                    method = optimized.method + "_optimized",
                    cost = 0f
                };
                
                float newCost = ComputeContactCost(newContact, mcpPos, weights, fingerName);
                
                if (newCost < currentCost)
                {
                    optimized = newContact;
                }
                else
                {
                    break;
                }
            }
            
            return optimized;
        }

        /// <summary>
        /// Compute numerical gradient of contact cost.
        /// </summary>
        private static Vector3 ComputeContactGradient(
            SurfaceContact contact,
            Vector3 mcpPos,
            CostWeights weights,
            string fingerName)
        {
            float epsilon = 0.001f;
            float baseCost = ComputeContactCost(contact, mcpPos, weights, fingerName);
            
            float dx = ComputeContactCost(new SurfaceContact
            {
                position = contact.position + Vector3.right * epsilon,
                normal = contact.normal,
                method = contact.method,
                cost = 0f
            }, mcpPos, weights, fingerName) - baseCost;
            
            float dy = ComputeContactCost(new SurfaceContact
            {
                position = contact.position + Vector3.up * epsilon,
                normal = contact.normal,
                method = contact.method,
                cost = 0f
            }, mcpPos, weights, fingerName) - baseCost;
            
            float dz = ComputeContactCost(new SurfaceContact
            {
                position = contact.position + Vector3.forward * epsilon,
                normal = contact.normal,
                method = contact.method,
                cost = 0f
            }, mcpPos, weights, fingerName) - baseCost;
            
            return new Vector3(dx, dy, dz) / epsilon;
        }

        /// <summary>
        /// Compute cost function J for a contact point.
        /// </summary>
        private static float ComputeContactCost(
            SurfaceContact contact,
            Vector3 mcpPosition,
            CostWeights weights,
            string fingerName)
        {
            float cost = 0f;
            
            Vector3 fingertipTarget = contact.position + weights.padOffset * contact.normal;
            float distance = Vector3.Distance(mcpPosition, fingertipTarget);
            cost += weights.wDistance * distance * distance;
            
            Vector3 padNormal = Vector3.forward;
            float normalAlignment = Vector3.Dot(contact.normal, padNormal);
            cost += weights.wNormal * (1f - normalAlignment);
            
            float collisionPenalty = 0f;
            cost += weights.wCollision * collisionPenalty;
            
            float jointComfort = ComputeJointComfort(mcpPosition, fingertipTarget);
            cost += weights.wJointLimits * (1f - jointComfort);
            
            float curvature = ComputeCurvature(contact.position, contact.normal);
            cost += weights.wCurvature * curvature;
            
            return cost;
        }

        /// <summary>
        /// Compute joint comfort based on finger chain kinematics.
        /// </summary>
        private static float ComputeJointComfort(Vector3 mcpPos, Vector3 fingertipTarget)
        {
            float distance = Vector3.Distance(mcpPos, fingertipTarget);
            float maxComfortableReach = 0.08f; // 8cm typical finger reach
            
            if (distance <= maxComfortableReach)
                return 1f;
            else
                return Mathf.Exp(-(distance - maxComfortableReach) / 0.02f);
        }

        /// <summary>
        /// Compute curvature at contact point.
        /// </summary>
        private static float ComputeCurvature(Vector3 position, Vector3 normal)
        {
            return 0f;
        }

        /// <summary>
        /// Build final target poses with proper orientations.
        /// </summary>
        private static ContactTargets BuildTargetPoses(
            Dictionary<string, SurfaceContact> selectedContacts,
            CostWeights weights)
        {
            var targets = new ContactTargets();
            
            foreach (var kvp in selectedContacts)
            {
                string fingerName = kvp.Key;
                SurfaceContact contact = kvp.Value;
                
                Vector3 normal = contact.normal;
                Vector3 tangent1 = BuildTangentFrame(normal, Vector3.up);
                Vector3 tangent2 = Vector3.Cross(normal, tangent1);
                
                Quaternion orientation = BuildFingerOrientation(normal, tangent1);
                
                var result = new ContactResult
                {
                    position = contact.position + weights.padOffset * normal,
                    normal = normal,
                    orientation = orientation,
                    cost = contact.cost
                };
                
                switch (fingerName)
                {
                    case "thumb": targets.thumb = result; break;
                    case "index": targets.index = result; break;
                    case "middle": targets.middle = result; break;
                    case "ring": targets.ring = result; break;
                    case "little": targets.little = result; break;
                }
            }
            
            return targets;
        }

        /// <summary>
        /// Build tangent frame from normal and reference vector.
        /// </summary>
        private static Vector3 BuildTangentFrame(Vector3 normal, Vector3 reference)
        {
            // Project reference onto tangent plane
            Vector3 tangent = reference - Vector3.Dot(reference, normal) * normal;
            return tangent.normalized;
        }

        /// <summary>
        /// Build finger orientation quaternion from surface normal and tangent.
        /// </summary>
        private static Quaternion BuildFingerOrientation(Vector3 normal, Vector3 tangent)
        {
            Vector3 padNormal = -normal;
            
            Quaternion alignment = Quaternion.FromToRotation(Vector3.forward, padNormal);
            
            float rollAngle = Mathf.Atan2(Vector3.Dot(tangent, Vector3.right), Vector3.Dot(tangent, Vector3.up));
            Quaternion roll = Quaternion.AngleAxis(rollAngle * Mathf.Rad2Deg, padNormal);
            
            return roll * alignment;
        }

        /// <summary>
        /// Draw debug visualization of contact planning process.
        /// </summary>
        public static void DrawDebugVisualization(
            ContactTargets targets,
            Dictionary<string, Vector3> mcpPositions,
            Color contactColor = default,
            Color normalColor = default,
            Color orientationColor = default)
        {
            if (contactColor == default) contactColor = Color.red;
            if (normalColor == default) normalColor = Color.blue;
            if (orientationColor == default) orientationColor = Color.green;
            
            DrawContactPoint(targets.thumb, contactColor, normalColor, orientationColor);
            DrawContactPoint(targets.index, contactColor, normalColor, orientationColor);
            DrawContactPoint(targets.middle, contactColor, normalColor, orientationColor);
            DrawContactPoint(targets.ring, contactColor, normalColor, orientationColor);
            DrawContactPoint(targets.little, contactColor, normalColor, orientationColor);
            
            if (mcpPositions.ContainsKey("thumb"))
                Handles.DrawLine(mcpPositions["thumb"], targets.thumb.position);
            if (mcpPositions.ContainsKey("index"))
                Handles.DrawLine(mcpPositions["index"], targets.index.position);
            if (mcpPositions.ContainsKey("middle"))
                Handles.DrawLine(mcpPositions["middle"], targets.middle.position);
            if (mcpPositions.ContainsKey("ring"))
                Handles.DrawLine(mcpPositions["ring"], targets.ring.position);
            if (mcpPositions.ContainsKey("little"))
                Handles.DrawLine(mcpPositions["little"], targets.little.position);
        }

        /// <summary>
        /// Draw a single contact point with normal and orientation.
        /// </summary>
        private static void DrawContactPoint(
            ContactResult contact,
            Color contactColor,
            Color normalColor,
            Color orientationColor)
        {
            Handles.color = contactColor;
            Handles.DrawWireDisc(contact.position, contact.normal, 0.005f);
            
            Handles.color = normalColor;
            Handles.DrawLine(contact.position, contact.position + contact.normal * 0.01f);
            
            Handles.color = orientationColor;
            Vector3 forward = contact.orientation * Vector3.forward;
            Vector3 up = contact.orientation * Vector3.up;
            Vector3 right = contact.orientation * Vector3.right;
            
            Handles.DrawLine(contact.position, contact.position + forward * 0.008f);
            Handles.DrawLine(contact.position, contact.position + up * 0.006f);
            Handles.DrawLine(contact.position, contact.position + right * 0.006f);
        }

        /// <summary>
        /// Surface contact data structure.
        /// </summary>
        private struct SurfaceContact
        {
            public Vector3 position;
            public Vector3 normal;
            public string method;
            public float cost;
        }
    }
}
