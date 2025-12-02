using UnityEngine;
using System.Collections.Generic;
using YUCP.Components;

namespace YUCP.Components.Editor.MeshUtils
{
    /// <summary>
    /// Contact planner for realistic finger placement on object surfaces.
    /// </summary>
    public static class ContactPlanner
    {
        /// <summary>
        /// Plan contact points for all fingers using grip type and object geometry.
        /// </summary>
        public static FingerTargets PlanContacts(
            Transform grippedObject,
            Animator animator,
            Transform handTransform,
            PlannerSettings settings)
        {
            var targets = new FingerTargets();
            
            if (grippedObject == null || animator == null || handTransform == null)
            {
                Debug.LogWarning("[ContactPlanner] Missing required components");
                return targets;
            }
            
            // Get MCP joint positions for approach rays
            var mcpPositions = GetMCPPositions(animator, handTransform);
            
            // Try primitive closed forms first
            if (settings.usePrimitives && TryPrimitiveContacts(grippedObject, handTransform, mcpPositions, settings, out targets))
            {
                if (settings.debugDraw)
                {
                    DrawDebugContacts(targets, Color.green);
                }
                return targets;
            }
            
            // Fallback to mesh/collider sampling
            targets = SampleMeshContacts(grippedObject, handTransform, mcpPositions, settings);
            
            if (settings.debugDraw)
            {
                DrawDebugContacts(targets, Color.blue);
            }
            
            return targets;
        }
        
        /// <summary>
        /// Try primitive closed-form contact generation.
        /// </summary>
        private static bool TryPrimitiveContacts(
            Transform grippedObject,
            Transform handTransform,
            Dictionary<string, Vector3> mcpPositions,
            PlannerSettings settings,
            out FingerTargets targets)
        {
            targets = new FingerTargets();
            
            var colliders = SurfaceQuery.GetAllColliders(grippedObject);
            if (colliders.Length == 0) return false;
            
            // Check for sphere
            var sphereCollider = grippedObject.GetComponent<SphereCollider>();
            if (sphereCollider != null)
            {
                targets = GenerateSphereContacts(sphereCollider, handTransform, mcpPositions, settings);
                return true;
            }
            
            // Check for cylinder
            var capsuleCollider = grippedObject.GetComponent<CapsuleCollider>();
            if (capsuleCollider != null && capsuleCollider.direction == 1) // Y-axis cylinder
            {
                targets = GenerateCylinderContacts(capsuleCollider, handTransform, mcpPositions, settings);
                return true;
            }
            
            // Check for box (plane approximation)
            var boxCollider = grippedObject.GetComponent<BoxCollider>();
            if (boxCollider != null)
            {
                targets = GenerateBoxContacts(boxCollider, handTransform, mcpPositions, settings);
                return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Generate contacts for sphere using closed-form math.
        /// </summary>
        private static FingerTargets GenerateSphereContacts(
            SphereCollider sphereCollider,
            Transform handTransform,
            Dictionary<string, Vector3> mcpPositions,
            PlannerSettings settings)
        {
            var targets = new FingerTargets();
            var center = sphereCollider.bounds.center;
            var radius = sphereCollider.radius * Mathf.Max(sphereCollider.transform.lossyScale.x, sphereCollider.transform.lossyScale.y, sphereCollider.transform.lossyScale.z);
            
            // Use default spherical contacts for manual grip positioning
            GenerateSphericalSphereContacts(center, radius, handTransform, mcpPositions, settings, ref targets);
            
            return targets;
        }
        
        /// <summary>
        /// Generate contacts for cylinder using closed-form math.
        /// </summary>
        private static FingerTargets GenerateCylinderContacts(
            CapsuleCollider capsuleCollider,
            Transform handTransform,
            Dictionary<string, Vector3> mcpPositions,
            PlannerSettings settings)
        {
            var targets = new FingerTargets();
            var bounds = capsuleCollider.bounds;
            var center = bounds.center;
            var radius = capsuleCollider.radius * Mathf.Max(capsuleCollider.transform.lossyScale.x, capsuleCollider.transform.lossyScale.z);
            var height = bounds.size.y;
            
            // Generate contacts around cylinder circumference
            GenerateCylinderWrapContacts(center, radius, height, handTransform, mcpPositions, settings, ref targets);
            
            return targets;
        }
        
        /// <summary>
        /// Generate contacts for box using plane approximation.
        /// </summary>
        private static FingerTargets GenerateBoxContacts(
            BoxCollider boxCollider,
            Transform handTransform,
            Dictionary<string, Vector3> mcpPositions,
            PlannerSettings settings)
        {
            var targets = new FingerTargets();
            var bounds = boxCollider.bounds;
            var center = bounds.center;
            var size = bounds.size;
            
            // Find closest face to hand
            Vector3 handToCenter = (center - handTransform.position).normalized;
            Vector3 faceNormal = Vector3.zero;
            float maxDot = -1f;
            
            Vector3[] faceNormals = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (var normal in faceNormals)
            {
                float dot = Vector3.Dot(handToCenter, normal);
                if (dot > maxDot)
                {
                    maxDot = dot;
                    faceNormal = normal;
                }
            }
            
            // Generate contacts on closest face
            GenerateBoxFaceContacts(center, size, faceNormal, handTransform, mcpPositions, settings, ref targets);
            
            return targets;
        }
        
        /// <summary>
        /// Sample mesh/collider contacts using candidate generation and scoring.
        /// </summary>
        private static FingerTargets SampleMeshContacts(
            Transform grippedObject,
            Transform handTransform,
            Dictionary<string, Vector3> mcpPositions,
            PlannerSettings settings)
        {
            var targets = new FingerTargets();
            var colliders = SurfaceQuery.GetAllColliders(grippedObject);
            
            if (colliders.Length == 0)
            {
                Debug.LogWarning("[ContactPlanner] No colliders found for mesh sampling");
                return targets;
            }
            
            // Generate candidates for each finger
            var fingerNames = new[] { "thumb", "index", "middle", "ring", "little" };
            var selectedContacts = new List<ContactCandidate>();
            
            foreach (var fingerName in fingerNames)
            {
                if (!mcpPositions.ContainsKey(fingerName)) continue;
                
                var mcpPos = mcpPositions[fingerName];
                var candidates = GenerateCandidates(mcpPos, grippedObject, colliders, settings);
                
                if (candidates.Count > 0)
                {
                    // Score candidates
                    ScoreCandidates(candidates, selectedContacts, fingerName, settings);
                    
                    // Select best candidate
                    var bestCandidate = SelectBestCandidate(candidates, selectedContacts, fingerName, settings);
                    if (bestCandidate != null)
                    {
                        selectedContacts.Add(bestCandidate);
                        
                        // Assign to target
                        var target = new FingerTarget
                        {
                            position = bestCandidate.position + bestCandidate.normal * settings.padOffset,
                            normal = bestCandidate.normal,
                            tangentPrimary = bestCandidate.tangentPrimary
                        };
                        
                        switch (fingerName)
                        {
                            case "thumb": targets.thumb = target; break;
                            case "index": targets.index = target; break;
                            case "middle": targets.middle = target; break;
                            case "ring": targets.ring = target; break;
                            case "little": targets.little = target; break;
                        }
                    }
                }
            }
            
            return targets;
        }
        
        /// <summary>
        /// Contact candidate for scoring.
        /// </summary>
        private class ContactCandidate
        {
            public Vector3 position;
            public Vector3 normal;
            public Vector3 tangentPrimary;
            public float score;
            public string fingerName;
        }
        
        /// <summary>
        /// Generate candidate contact points around MCP position.
        /// </summary>
        private static List<ContactCandidate> GenerateCandidates(
            Vector3 mcpPosition,
            Transform grippedObject,
            Collider[] colliders,
            PlannerSettings settings)
        {
            var candidates = new List<ContactCandidate>();
            
            // Generate approach ray from MCP to object center
            Vector3 objectCenter = grippedObject.position;
            Vector3 approachDirection = (objectCenter - mcpPosition).normalized;
            
            // Generate candidates in a ring around the approach direction
            var ringPoints = MeshSampler.SampleRingPoints(mcpPosition, approachDirection, 0.1f, settings.sampleCount);
            
            foreach (var ringPoint in ringPoints)
            {
                Vector3 rayDirection = (objectCenter - ringPoint).normalized;
                
                // Find closest point on colliders
                foreach (var collider in colliders)
                {
                    Vector3 closestPoint;
                    if (SurfaceQuery.TryClosestPoint(collider, ringPoint, out closestPoint))
                    {
                        Vector3 normal;
                        if (SurfaceQuery.TryRaycastNormal(collider, ringPoint, rayDirection, out normal))
                        {
                            // Build tangent frame
                            Vector3 tangentPrimary = BuildTangentFrame(normal, approachDirection);
                            
                            candidates.Add(new ContactCandidate
                            {
                                position = closestPoint,
                                normal = normal,
                                tangentPrimary = tangentPrimary,
                                score = 0f,
                                fingerName = ""
                            });
                        }
                    }
                }
            }
            
            return candidates;
        }
        
        /// <summary>
        /// Score candidates using various criteria.
        /// </summary>
        private static void ScoreCandidates(
            List<ContactCandidate> candidates,
            List<ContactCandidate> selectedContacts,
            string fingerName,
            PlannerSettings settings)
        {
            foreach (var candidate in candidates)
            {
                candidate.fingerName = fingerName;
                
                // Distance score (closer is better)
                float distanceScore = 1f / (1f + Vector3.SqrMagnitude(candidate.position - candidate.position));
                
                // Normal alignment score (pad should align with surface)
                float normalScore = 1f - Mathf.Abs(Vector3.Dot(candidate.normal, Vector3.up));
                
                // Separation score
                float separationScore = 1f;
                foreach (var selected in selectedContacts)
                {
                    float dist = Vector3.Distance(candidate.position, selected.position);
                    separationScore *= Mathf.Exp(-dist * dist / (settings.separationSigma * settings.separationSigma));
                }
                
                // Opposition score (thumb should oppose other fingers)
                float oppositionScore = 1f;
                if (fingerName == "thumb")
                {
                    Vector3 avgFingerNormal = Vector3.zero;
                    int count = 0;
                    foreach (var selected in selectedContacts)
                    {
                        if (selected.fingerName != "thumb")
                        {
                            avgFingerNormal += selected.normal;
                            count++;
                        }
                    }
                    if (count > 0)
                    {
                        avgFingerNormal /= count;
                        oppositionScore = -Vector3.Dot(candidate.normal, avgFingerNormal);
                    }
                }
                
                // Combined score
                candidate.score = settings.wDistance * distanceScore +
                                 settings.wNormal * normalScore +
                                 settings.wSeparation * separationScore +
                                 settings.wOpposition * oppositionScore;
            }
        }
        
        /// <summary>
        /// Select best candidate avoiding conflicts.
        /// </summary>
        private static ContactCandidate SelectBestCandidate(
            List<ContactCandidate> candidates,
            List<ContactCandidate> selectedContacts,
            string fingerName,
            PlannerSettings settings)
        {
            if (candidates.Count == 0) return null;
            
            // Sort by score
            candidates.Sort((a, b) => b.score.CompareTo(a.score));
            
            // Find best candidate that doesn't conflict with selected ones
            foreach (var candidate in candidates)
            {
                bool conflicts = false;
                foreach (var selected in selectedContacts)
                {
                    float dist = Vector3.Distance(candidate.position, selected.position);
                    if (dist < settings.separationSigma)
                    {
                        conflicts = true;
                        break;
                    }
                }
                
                if (!conflicts)
                {
                    return candidate;
                }
            }
            
            // If all conflict, return best anyway
            return candidates[0];
        }
        
        /// <summary>
        /// Build tangent frame from normal and reference direction.
        /// </summary>
        private static Vector3 BuildTangentFrame(Vector3 normal, Vector3 referenceDirection)
        {
            Vector3 up = Vector3.up;
            if (Mathf.Abs(Vector3.Dot(up, normal)) > 0.95f)
            {
                up = Vector3.right;
            }
            
            Vector3 tangent1 = Vector3.Cross(up, normal).normalized;
            Vector3 tangent2 = Vector3.Cross(normal, tangent1).normalized;
            
            // Project reference direction onto tangent plane
            Vector3 projected = referenceDirection - Vector3.Dot(referenceDirection, normal) * normal;
            return projected.normalized;
        }
        
        /// <summary>
        /// Get MCP joint positions for approach rays.
        /// </summary>
        private static Dictionary<string, Vector3> GetMCPPositions(Animator animator, Transform handTransform)
        {
            var positions = new Dictionary<string, Vector3>();
            
            // Get MCP joints (proximal finger joints)
            var thumbMCP = animator.GetBoneTransform(HumanBodyBones.LeftThumbProximal);
            var indexMCP = animator.GetBoneTransform(HumanBodyBones.LeftIndexProximal);
            var middleMCP = animator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
            var ringMCP = animator.GetBoneTransform(HumanBodyBones.LeftRingProximal);
            var littleMCP = animator.GetBoneTransform(HumanBodyBones.LeftLittleProximal);
            
            // Adjust for right hand if needed
            if (handTransform == animator.GetBoneTransform(HumanBodyBones.RightHand))
            {
                thumbMCP = animator.GetBoneTransform(HumanBodyBones.RightThumbProximal);
                indexMCP = animator.GetBoneTransform(HumanBodyBones.RightIndexProximal);
                middleMCP = animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
                ringMCP = animator.GetBoneTransform(HumanBodyBones.RightRingProximal);
                littleMCP = animator.GetBoneTransform(HumanBodyBones.RightLittleProximal);
            }
            
            if (thumbMCP != null) positions["thumb"] = thumbMCP.position;
            if (indexMCP != null) positions["index"] = indexMCP.position;
            if (middleMCP != null) positions["middle"] = middleMCP.position;
            if (ringMCP != null) positions["ring"] = ringMCP.position;
            if (littleMCP != null) positions["little"] = littleMCP.position;
            
            return positions;
        }
        
        /// <summary>
        /// Generate pinch grip contacts for sphere.
        /// </summary>
        private static void GeneratePinchSphereContacts(
            Vector3 center, float radius, Transform handTransform, Dictionary<string, Vector3> mcpPositions,
            PlannerSettings settings, ref FingerTargets targets)
        {
            // Thumb opposes index finger
            if (mcpPositions.ContainsKey("thumb") && mcpPositions.ContainsKey("index"))
            {
                Vector3 thumbDir = (mcpPositions["thumb"] - center).normalized;
                Vector3 indexDir = (mcpPositions["index"] - center).normalized;
                
                targets.thumb = new FingerTarget
                {
                    position = center + thumbDir * radius + thumbDir * settings.padOffset,
                    normal = thumbDir,
                    tangentPrimary = BuildTangentFrame(thumbDir, Vector3.up)
                };
                
                targets.index = new FingerTarget
                {
                    position = center + indexDir * radius + indexDir * settings.padOffset,
                    normal = indexDir,
                    tangentPrimary = BuildTangentFrame(indexDir, Vector3.up)
                };
            }
        }
        
        /// <summary>
        /// Generate hook grip contacts for sphere.
        /// </summary>
        private static void GenerateHookSphereContacts(
            Vector3 center, float radius, Transform handTransform, Dictionary<string, Vector3> mcpPositions,
            PlannerSettings settings, ref FingerTargets targets)
        {
            // Fingers curl over top, thumb provides support
            Vector3 up = Vector3.up;
            Vector3 handToCenter = (center - handTransform.position).normalized;
            
            targets.thumb = new FingerTarget
            {
                position = center - up * radius + handToCenter * settings.padOffset,
                normal = -up,
                tangentPrimary = BuildTangentFrame(-up, handToCenter)
            };
            
            targets.index = new FingerTarget
            {
                position = center + up * radius + handToCenter * settings.padOffset,
                normal = up,
                tangentPrimary = BuildTangentFrame(up, handToCenter)
            };
        }
        
        /// <summary>
        /// Generate spherical grip contacts for sphere.
        /// </summary>
        private static void GenerateSphericalSphereContacts(
            Vector3 center, float radius, Transform handTransform, Dictionary<string, Vector3> mcpPositions,
            PlannerSettings settings, ref FingerTargets targets)
        {
            // Spread fingers around sphere
            var fingerNames = new[] { "thumb", "index", "middle", "ring", "little" };
            
            for (int i = 0; i < fingerNames.Length; i++)
            {
                if (mcpPositions.ContainsKey(fingerNames[i]))
                {
                    Vector3 dir = (mcpPositions[fingerNames[i]] - center).normalized;
                    var target = new FingerTarget
                    {
                        position = center + dir * radius + dir * settings.padOffset,
                        normal = dir,
                        tangentPrimary = BuildTangentFrame(dir, Vector3.up)
                    };
                    
                    switch (fingerNames[i])
                    {
                        case "thumb": targets.thumb = target; break;
                        case "index": targets.index = target; break;
                        case "middle": targets.middle = target; break;
                        case "ring": targets.ring = target; break;
                        case "little": targets.little = target; break;
                    }
                }
            }
        }
        
        /// <summary>
        /// Generate power grip contacts for sphere.
        /// </summary>
        private static void GeneratePowerSphereContacts(
            Vector3 center, float radius, Transform handTransform, Dictionary<string, Vector3> mcpPositions,
            PlannerSettings settings, ref FingerTargets targets)
        {
            // Tight grip with all fingers
            GenerateSphericalSphereContacts(center, radius, handTransform, mcpPositions, settings, ref targets);
        }
        
        /// <summary>
        /// Generate lateral grip contacts for sphere.
        /// </summary>
        private static void GenerateLateralSphereContacts(
            Vector3 center, float radius, Transform handTransform, Dictionary<string, Vector3> mcpPositions,
            PlannerSettings settings, ref FingerTargets targets)
        {
            // Thumb presses against side of index
            GeneratePinchSphereContacts(center, radius, handTransform, mcpPositions, settings, ref targets);
        }
        
        /// <summary>
        /// Generate cylinder wrap contacts.
        /// </summary>
        private static void GenerateCylinderWrapContacts(
            Vector3 center, float radius, float height, Transform handTransform, Dictionary<string, Vector3> mcpPositions,
            PlannerSettings settings, ref FingerTargets targets)
        {
            // Wrap fingers around cylinder circumference
            var fingerNames = new[] { "thumb", "index", "middle", "ring", "little" };
            
            for (int i = 0; i < fingerNames.Length; i++)
            {
                if (mcpPositions.ContainsKey(fingerNames[i]))
                {
                    Vector3 mcpPos = mcpPositions[fingerNames[i]];
                    Vector3 toMCP = mcpPos - center;
                    
                    // Project to cylinder surface
                    Vector3 radial = new Vector3(toMCP.x, 0, toMCP.z).normalized;
                    Vector3 surfacePoint = center + radial * radius;
                    
                    var target = new FingerTarget
                    {
                        position = surfacePoint + radial * settings.padOffset,
                        normal = radial,
                        tangentPrimary = Vector3.up
                    };
                    
                    switch (fingerNames[i])
                    {
                        case "thumb": targets.thumb = target; break;
                        case "index": targets.index = target; break;
                        case "middle": targets.middle = target; break;
                        case "ring": targets.ring = target; break;
                        case "little": targets.little = target; break;
                    }
                }
            }
        }
        
        /// <summary>
        /// Generate box face contacts.
        /// </summary>
        private static void GenerateBoxFaceContacts(
            Vector3 center, Vector3 size, Vector3 faceNormal, Transform handTransform, Dictionary<string, Vector3> mcpPositions,
            PlannerSettings settings, ref FingerTargets targets)
        {
            // Place contacts on closest face
            Vector3 faceCenter = center + faceNormal * (size.magnitude * 0.5f);
            
            var fingerNames = new[] { "thumb", "index", "middle", "ring", "little" };
            
            for (int i = 0; i < fingerNames.Length; i++)
            {
                if (mcpPositions.ContainsKey(fingerNames[i]))
                {
                    Vector3 mcpPos = mcpPositions[fingerNames[i]];
                    Vector3 toMCP = mcpPos - faceCenter;
                    
                    // Project to face
                    Vector3 projected = toMCP - Vector3.Dot(toMCP, faceNormal) * faceNormal;
                    Vector3 contactPoint = faceCenter + projected;
                    
                    var target = new FingerTarget
                    {
                        position = contactPoint + faceNormal * settings.padOffset,
                        normal = faceNormal,
                        tangentPrimary = BuildTangentFrame(faceNormal, Vector3.up)
                    };
                    
                    switch (fingerNames[i])
                    {
                        case "thumb": targets.thumb = target; break;
                        case "index": targets.index = target; break;
                        case "middle": targets.middle = target; break;
                        case "ring": targets.ring = target; break;
                        case "little": targets.little = target; break;
                    }
                }
            }
        }
        
        /// <summary>
        /// Draw debug visualization of contacts.
        /// </summary>
        private static void DrawDebugContacts(FingerTargets targets, Color color)
        {
            #if UNITY_EDITOR
            var fingerTargets = new[] { targets.thumb, targets.index, targets.middle, targets.ring, targets.little };
            var fingerNames = new[] { "Thumb", "Index", "Middle", "Ring", "Little" };
            
            for (int i = 0; i < fingerTargets.Length; i++)
            {
                var target = fingerTargets[i];
                
                // Draw contact point
                UnityEditor.Handles.color = color;
                UnityEditor.Handles.DrawWireDisc(target.position, target.normal, 0.01f);
                
                // Draw normal
                UnityEditor.Handles.color = Color.red;
                UnityEditor.Handles.DrawLine(target.position, target.position + target.normal * 0.05f);
                
                // Draw tangent
                UnityEditor.Handles.color = Color.green;
                UnityEditor.Handles.DrawLine(target.position, target.position + target.tangentPrimary * 0.03f);
                
                // Draw label
                UnityEditor.Handles.Label(target.position + Vector3.up * 0.02f, fingerNames[i]);
            }
            #endif
        }
    }
}
