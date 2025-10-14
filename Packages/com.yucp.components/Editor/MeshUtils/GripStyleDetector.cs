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
            public GripStyle recommendedStyle;
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

            analysis.recommendedStyle = DetermineGripStyle(analysis);

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

        private static GripStyle DetermineGripStyle(ObjectAnalysis analysis)
        {
            if (analysis.maxDimension < 0.04f)
            {
                return GripStyle.Pinch;
            }

            if (analysis.hasHandle || analysis.aspectRatio > 3.0f)
            {
                if (analysis.size.y > analysis.size.x && analysis.size.y > analysis.size.z)
                {
                    return GripStyle.Point;
                }
            }

            return GripStyle.Wrap;
        }

        public static string GetGripStyleDescription(GripStyle style)
        {
            switch (style)
            {
                case GripStyle.Wrap:
                    return "All fingers curl around object";
                case GripStyle.Pinch:
                    return "Thumb and index finger primarily, light grip";
                case GripStyle.Point:
                    return "Index extended, other fingers curl (trigger/point grip)";
                case GripStyle.Auto:
                    return "Automatically detected based on object shape";
                default:
                    return "";
            }
        }
    }
}



