using System;
using UnityEngine;
using YUCP.Components;

namespace YUCP.Components.Editor.MeshUtils
{
    /// <summary>
    /// Build-time surface correspondence helper for AttachToBlendshape merge baking.
    /// Maps attachment vertices onto the base mesh surface via raycast triangleIndex + barycentric,
    /// </summary>
    public static class ClosestSurfaceMapper
    {
        public struct SurfaceMap
        {
            public bool matched;
            public int triIndex;
            public Vector3 barycentric;
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


            var tempGo = new GameObject("__YUCP_ClosestSurfaceMapper_TempCollider");
            tempGo.hideFlags = HideFlags.HideAndDontSave;
            try
            {

                tempGo.transform.position = baseSmr.transform.position;
                tempGo.transform.rotation = baseSmr.transform.rotation;
                tempGo.transform.localScale = baseSmr.transform.lossyScale;

                var mc = tempGo.AddComponent<MeshCollider>();
                mc.sharedMesh = baseMesh;
                mc.convex = false;

                var maps = new SurfaceMap[attachmentVerticesInBaseLocal.Length];
                var boundsCenter = mc.bounds.center;

                for (int i = 0; i < maps.Length; i++)
                {
                    var worldPoint = baseSmr.transform.TransformPoint(attachmentVerticesInBaseLocal[i]);
                    maps[i] = TryMapPoint(mc, worldPoint, boundsCenter);
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
            Vector3 worldPoint,
            Vector3 boundsCenter)
        {

            var best = new SurfaceMap { matched = false, triIndex = -1, barycentric = Vector3.zero };
            float bestDistSqr = float.MaxValue;

            Vector3 toCenter = boundsCenter - worldPoint;
            if (toCenter.sqrMagnitude < 1e-8f) toCenter = Vector3.up;
            toCenter.Normalize();

            TryRay(worldPoint - toCenter * 0.25f, toCenter, 1.0f);
            TryRay(worldPoint + toCenter * 0.25f, -toCenter, 1.0f);


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
            }
        }

    }
}

