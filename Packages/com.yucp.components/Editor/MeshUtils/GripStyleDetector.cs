using UnityEngine;
using YUCP.Components;

namespace YUCP.Components.Editor.MeshUtils
{
    /// <summary>
    /// Analyzes object geometry to determine optimal grip style.
    /// </summary>
    public static class GripStyleDetector
    {
        public class ObjectAnalysis
        {
            public Vector3 size;
            public Vector3 center;
            public float maxDimension;
            public float minDimension;
            public float aspectRatio;
            public bool hasHandle;
        }

        public static ObjectAnalysis AnalyzeObject(Transform obj)
        {
            var analysis = new ObjectAnalysis();

            Bounds bounds = CalculateBounds(obj);
            analysis.size = bounds.size;
            analysis.center = bounds.center;
            analysis.maxDimension = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            analysis.minDimension = Mathf.Min(bounds.size.x, bounds.size.y, bounds.size.z);
            analysis.aspectRatio = analysis.maxDimension / Mathf.Max(0.001f, analysis.minDimension);

            analysis.hasHandle = DetectHandle(obj, bounds);

            return analysis;
        }

        private static Bounds CalculateBounds(Transform obj)
        {
            var renderers = obj.GetComponentsInChildren<Renderer>();
            
            if (renderers.Length == 0)
            {
                return new Bounds(obj.position, Vector3.one * 0.1f);
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }

        private static bool DetectHandle(Transform obj, Bounds bounds)
        {
            if (bounds.size.y > bounds.size.x * 2f && bounds.size.y > bounds.size.z * 2f)
            {
                return true;
            }

            foreach (Transform child in obj.GetComponentsInChildren<Transform>())
            {
                if (child.name.ToLower().Contains("handle") || 
                    child.name.ToLower().Contains("grip"))
                {
                    return true;
                }
            }

            return false;
        }

        private static string DetermineGripStyle(ObjectAnalysis analysis)
        {
            // Only manual grip positioning is supported now
            return "Manual";
        }

        public static string GetGripStyleDescription()
        {
            return "Manual grip positioning - position finger gizmos manually in Scene view";
        }
    }
}



