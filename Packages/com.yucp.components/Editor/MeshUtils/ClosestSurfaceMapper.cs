using System;
using System.Collections.Generic;
using UnityEngine;
using YUCP.Components;

namespace YUCP.Components.Editor.MeshUtils
{
    /// <summary>
    /// Build-time surface correspondence helper for AttachToBlendshape merge baking.
    /// Maps attachment vertices onto the base mesh surface via raycast triangleIndex + barycentric,
    /// then can compute per-vertex deltas for arbitrary base mesh deformations (blendshape frames).
    /// </summary>
    public static class ClosestSurfaceMapper
    {
        public struct SurfaceMap
        {
            public bool matched;
            public int triIndex;
            public Vector3 barycentric;
            public Vector3 basePointLocal; // base mesh local (SkinnedMeshRenderer local space)
        }

        private static readonly Vector3[] RayDirs = {
            Vector3.forward, Vector3.back, Vector3.up, Vector3.down, Vector3.right, Vector3.left
        };

        public static SurfaceMap[] MapAttachmentVerticesToBaseSurface(
            SkinnedMeshRenderer baseSmr,
            Mesh baseMesh,
            Vector3[] attachmentVerticesInBaseLocal,
            AttachToBlendshapeData data,
            out int matchedCount)
        {
            matchedCount = 0;
            if (baseSmr == null || baseMesh == null || attachmentVerticesInBaseLocal == null)
            {
                return Array.Empty<SurfaceMap>();
            }

            // Build a temporary MeshCollider in world space matching the base renderer transform.
            var tempGo = new GameObject("__YUCP_ClosestSurfaceMapper_TempCollider");
            tempGo.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                // Put the temp object in world space matching baseSmr's world transform.
                tempGo.transform.position = baseSmr.transform.position;
                tempGo.transform.rotation = baseSmr.transform.rotation;
                tempGo.transform.localScale = baseSmr.transform.lossyScale;

                var mc = tempGo.AddComponent<MeshCollider>();
                mc.sharedMesh = baseMesh;
                mc.convex = false;
                mc.inflateMesh = false;

                var maps = new SurfaceMap[attachmentVerticesInBaseLocal.Length];
                var boundsCenter = mc.bounds.center;

                for (int i = 0; i < maps.Length; i++)
                {
                    var worldPoint = baseSmr.transform.TransformPoint(attachmentVerticesInBaseLocal[i]);
                    maps[i] = TryMapPoint(mc, baseSmr, worldPoint, boundsCenter, data);
                    if (maps[i].matched) matchedCount++;
                }

                if (data != null && data.debugMode)
                {
                    Debug.Log($"[ClosestSurfaceMapper] Mapped {matchedCount}/{maps.Length} attachment vertices to base surface", data);
                }

                return maps;
            }
            finally
            {
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(tempGo);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(tempGo);
                }
            }
        }

        private static SurfaceMap TryMapPoint(
            MeshCollider mc,
            SkinnedMeshRenderer baseSmr,
            Vector3 worldPoint,
            Vector3 boundsCenter,
            AttachToBlendshapeData data)
        {
            // Heuristic: try raycasts toward/away from bounds center and along cardinal axes.
            var best = new SurfaceMap { matched = false, triIndex = -1, barycentric = Vector3.zero, basePointLocal = Vector3.zero };
            float bestDistSqr = float.MaxValue;

            Vector3 toCenter = boundsCenter - worldPoint;
            if (toCenter.sqrMagnitude < 1e-8f) toCenter = Vector3.up;
            toCenter.Normalize();

            TryRay(worldPoint - toCenter * 0.25f, toCenter, 1.0f);
            TryRay(worldPoint + toCenter * 0.25f, -toCenter, 1.0f);

            // Try a few fixed directions as fallback (helps when point is inside collider bounds).
            for (int i = 0; i < RayDirs.Length; i++)
            {
                var dir = RayDirs[i];
                TryRay(worldPoint - dir * 0.25f, dir, 1.0f);
                TryRay(worldPoint + dir * 0.25f, -dir, 1.0f);
            }

            return best;

            void TryRay(Vector3 origin, Vector3 dir, float maxDist)
            {
                if (mc == null) return;
                RaycastHit hit;
                if (!mc.Raycast(new Ray(origin, dir), out hit, maxDist)) return;
                if (hit.triangleIndex < 0) return;
                var d = (hit.point - worldPoint).sqrMagnitude;
                if (d >= bestDistSqr) return;
                bestDistSqr = d;
                best.matched = true;
                best.triIndex = hit.triangleIndex;
                best.barycentric = hit.barycentricCoordinate;
                best.basePointLocal = baseSmr.transform.InverseTransformPoint(hit.point);
            }
        }

        public static Vector3[] ComputeAttachmentDeltasFromBaseSurface(
            SurfaceMap[] maps,
            Vector3[] baseVerticesAtZero,
            Vector3[] baseVerticesAtWeight,
            int[] baseTriangles,
            Vector3[] attachmentVerticesInBaseLocal,
            int[] attachmentTriangles,
            AttachToBlendshapeUnmatchedHandling unmatchedHandling,
            int smoothIterations = 12)
        {
            if (maps == null || baseVerticesAtZero == null || baseVerticesAtWeight == null ||
                baseTriangles == null || attachmentVerticesInBaseLocal == null)
            {
                return Array.Empty<Vector3>();
            }

            int n = attachmentVerticesInBaseLocal.Length;
            var deltas = new Vector3[n];

            for (int i = 0; i < n; i++)
            {
                if (!maps[i].matched || maps[i].triIndex < 0)
                {
                    deltas[i] = Vector3.zero;
                    continue;
                }

                int tri = maps[i].triIndex;
                int t0 = tri * 3;
                if (t0 + 2 >= baseTriangles.Length)
                {
                    deltas[i] = Vector3.zero;
                    continue;
                }

                int i0 = baseTriangles[t0];
                int i1 = baseTriangles[t0 + 1];
                int i2 = baseTriangles[t0 + 2];
                if ((uint)i0 >= (uint)baseVerticesAtZero.Length ||
                    (uint)i1 >= (uint)baseVerticesAtZero.Length ||
                    (uint)i2 >= (uint)baseVerticesAtZero.Length)
                {
                    deltas[i] = Vector3.zero;
                    continue;
                }

                Vector3 b = maps[i].barycentric;

                Vector3 p0 =
                    baseVerticesAtZero[i0] * b.x +
                    baseVerticesAtZero[i1] * b.y +
                    baseVerticesAtZero[i2] * b.z;

                Vector3 pw =
                    baseVerticesAtWeight[i0] * b.x +
                    baseVerticesAtWeight[i1] * b.y +
                    baseVerticesAtWeight[i2] * b.z;

                // Preserve the attachment's offset from the base surface at rest (pure translation offset).
                Vector3 offset = attachmentVerticesInBaseLocal[i] - p0;
                Vector3 target = pw + offset;
                deltas[i] = target - attachmentVerticesInBaseLocal[i];
            }

            if (unmatchedHandling == AttachToBlendshapeUnmatchedHandling.Skip)
            {
                return deltas;
            }

            var adj = BuildAdjacency(n, attachmentTriangles);

            if (unmatchedHandling == AttachToBlendshapeUnmatchedHandling.NeighborPropagate)
            {
                PropagateFromMatched(maps, adj, deltas);
            }
            else if (unmatchedHandling == AttachToBlendshapeUnmatchedHandling.SmoothDiffusion)
            {
                SmoothUnmatched(maps, adj, deltas, smoothIterations);
            }

            return deltas;
        }

        private static List<int>[] BuildAdjacency(int vertexCount, int[] triangles)
        {
            var adj = new List<int>[vertexCount];
            for (int i = 0; i < vertexCount; i++) adj[i] = new List<int>(8);
            if (triangles == null) return adj;

            void AddEdge(int a, int b)
            {
                if ((uint)a >= (uint)vertexCount || (uint)b >= (uint)vertexCount) return;
                if (a == b) return;
                if (!adj[a].Contains(b)) adj[a].Add(b);
                if (!adj[b].Contains(a)) adj[b].Add(a);
            }

            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                int a = triangles[t];
                int b = triangles[t + 1];
                int c = triangles[t + 2];
                AddEdge(a, b);
                AddEdge(b, c);
                AddEdge(c, a);
            }

            return adj;
        }

        private static void PropagateFromMatched(SurfaceMap[] maps, List<int>[] adj, Vector3[] deltas)
        {
            int n = deltas.Length;
            var dist = new int[n];
            var src = new int[n];
            var q = new Queue<int>(n);
            for (int i = 0; i < n; i++)
            {
                dist[i] = int.MaxValue;
                src[i] = -1;
                if (maps[i].matched)
                {
                    dist[i] = 0;
                    src[i] = i;
                    q.Enqueue(i);
                }
            }

            while (q.Count > 0)
            {
                int v = q.Dequeue();
                int s = src[v];
                foreach (var nb in adj[v])
                {
                    if (dist[nb] != int.MaxValue) continue;
                    dist[nb] = dist[v] + 1;
                    src[nb] = s;
                    q.Enqueue(nb);
                }
            }

            for (int i = 0; i < n; i++)
            {
                if (maps[i].matched) continue;
                int s = src[i];
                if (s >= 0 && maps[s].matched)
                {
                    deltas[i] = deltas[s];
                }
            }
        }

        private static void SmoothUnmatched(SurfaceMap[] maps, List<int>[] adj, Vector3[] deltas, int iterations)
        {
            int n = deltas.Length;
            var tmp = new Vector3[n];

            for (int it = 0; it < iterations; it++)
            {
                Array.Copy(deltas, tmp, n);
                for (int i = 0; i < n; i++)
                {
                    if (maps[i].matched) continue;
                    var neigh = adj[i];
                    if (neigh == null || neigh.Count == 0) continue;
                    Vector3 sum = Vector3.zero;
                    for (int j = 0; j < neigh.Count; j++)
                    {
                        sum += tmp[neigh[j]];
                    }
                    deltas[i] = sum / neigh.Count;
                }
            }
        }
    }
}

