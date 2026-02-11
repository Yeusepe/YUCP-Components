using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using YUCP.Components;

namespace YUCP.Components.Editor
{
    internal static class PrettyHierarchyDrawerHelper
    {
        private static Material _gradientMaterial;
        
        // Cache for generated gradient textures to avoid rebuilding them every frame if data hasn't changed.
        // HashCode is collision-prone but acceptable for Editor GUI visual updates.
        private static readonly Dictionary<int, Texture2D> _gradientCache = new Dictionary<int, Texture2D>();

        public static Texture2D GetGradientTexture(Gradient gradient)
        {
            if (gradient == null) return Texture2D.whiteTexture;

            int hash = GetGradientHash(gradient);
            if (_gradientCache.TryGetValue(hash, out Texture2D tex) && tex != null)
            {
                return tex;
            }

            tex = new Texture2D(256, 1);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.hideFlags = HideFlags.HideAndDontSave;
            var pixels = new Color[256];
            for (int i = 0; i < 256; i++)
            {
                float t = i / 255f;
                pixels[i] = gradient.Evaluate(t);
            }
            tex.SetPixels(pixels);
            tex.Apply();
            
            // Simple cache management: clear if too big
            if (_gradientCache.Count > 100)
            {
                 foreach(var t in _gradientCache.Values) if(t) Object.DestroyImmediate(t);
                 _gradientCache.Clear();
            }
            
            _gradientCache[hash] = tex;
            return tex;
        }

        // Helper to get a semi-unique hash for a gradient's current state
        private static int GetGradientHash(Gradient g)
        {
            int hash = 17;
            foreach (var k in g.colorKeys) hash = hash * 23 + k.color.GetHashCode() + k.time.GetHashCode();
            foreach (var k in g.alphaKeys) hash = hash * 23 + k.alpha.GetHashCode() + k.time.GetHashCode();
            hash = hash * 23 + g.mode.GetHashCode();
            return hash;
        }

        public static void DrawRoundedGradientRect(Rect rect, Texture2D gradientTex, Color colorMultiplier, float angle, 
            float topLeft, float topRight, float bottomRight, float bottomLeft, float alpha, float softness = 0.5f)
        {
            if (rect.width <= 0 || rect.height <= 0 || alpha <= 0f) return;

            if (_gradientMaterial == null)
            {
                var shader = ConvertToShader();
                if (shader != null) _gradientMaterial = new Material(shader);
                else return;
            }
            
            if (_gradientMaterial == null) return;

            if (gradientTex == null) gradientTex = Texture2D.whiteTexture;

            _gradientMaterial.SetTexture("_GradientTex", gradientTex);
            _gradientMaterial.SetColor("_Color", new Color(colorMultiplier.r, colorMultiplier.g, colorMultiplier.b, colorMultiplier.a * alpha));
            _gradientMaterial.SetFloat("_Angle", angle);
            
            _gradientMaterial.SetFloat("_RectW", rect.width);
            _gradientMaterial.SetFloat("_RectH", rect.height);
            _gradientMaterial.SetFloat("_RadiusTL", topLeft);
            _gradientMaterial.SetFloat("_RadiusTR", topRight);
            _gradientMaterial.SetFloat("_RadiusBR", bottomRight);
            _gradientMaterial.SetFloat("_RadiusBL", bottomLeft);
            _gradientMaterial.SetFloat("_Softness", softness);

            EditorGUI.DrawPreviewTexture(rect, Texture2D.whiteTexture, _gradientMaterial, ScaleMode.StretchToFill);
        }

        private static Shader ConvertToShader()
        {
            return Shader.Find("Hidden/YUCP/PrettyHierarchyRoundedGradient");
        }
        
        // Wrapper for Solid Color (uses same shader)
        public static void DrawRoundedRect(Rect rect, Color color, float topLeft, float topRight, float bottomRight, float bottomLeft, float alpha = 1f)
        {
            DrawRoundedGradientRect(rect, Texture2D.whiteTexture, color, 0, topLeft, topRight, bottomRight, bottomLeft, alpha, 0.5f);
        }

        // Wrapper for Shadows
        public static void DrawShadow(Rect rect, Color color, Vector2 offset, float blur, float topLeft, float topRight, float bottomRight, float bottomLeft)
        {
            Rect shadowRect = new Rect(rect.x + offset.x, rect.y + offset.y, rect.width, rect.height);
            // We use 'blur' as the softness parameter.
            // SDF Softness: 0.5 is sharp. higher is blurry.
            // A blur of 4-10 looks shadow-like.
            DrawRoundedGradientRect(shadowRect, Texture2D.whiteTexture, color, 0, topLeft, topRight, bottomRight, bottomLeft, color.a, Mathf.Max(0.5f, blur));
        }

        // Compatibility for older calls using GradientDirection
        public static void DrawGradientRect(Rect rect, Color32 start, Color32 end, PrettyHierarchyGradientDirection direction, float alpha = 1f)
        {
            // Bake a simple 2-color gradient on the fly (or reuse if we want, but this is legacy path)
            Gradient g = new Gradient();
            g.SetKeys(
                new GradientColorKey[] { new GradientColorKey(start, 0), new GradientColorKey(end, 1) },
                new GradientAlphaKey[] { new GradientAlphaKey(start.a/255f, 0), new GradientAlphaKey(end.a/255f, 1) }
            );
            
            float angle = (direction == PrettyHierarchyGradientDirection.Horizontal) ? 0 : 90; // 90 for vertical? Needs verify.
            // If Horizontal: Left to Right. Angle 0.
            // If Vertical: Top to Bottom. Angle -90 or 90?
            // Shader: 0 deg = +X. 90 deg = +Y (Up?). If we want Top(0) to Bottom(1), we might need -90.
            // Let's assume 0 is Horizontal for now. Vertical might be 90.
            
            DrawRoundedGradientRect(rect, GetGradientTexture(g), Color.white, angle, 0, 0, 0, 0, alpha);
        }

        public static void DrawBorder(Rect rect, float width, Color32 color, float topLeft, float topRight, float bottomRight, float bottomLeft)
        {
            // Simple border implementation or...
            // Ideally we'd use the shader SDF to draw a border only (hollow).
            // But we can stick to the simple implementation for now to avoid complexity, 
             if (width <= 0) return;
             
             // ... [Previous logic or just standard DrawRects]
             // Let's use simple DrawRects for distinct borders, or write a Border Shader pass?
             // Since we have the SDF shader, we COULD just draw a slightly larger rect behind? 
             // Or better: Draw the border using the standard Unity Editor handles or simple rects.
             
             // Reusing the simple rect implementation for now as it supports separate sides somewhat.
             // Actually, let's just draw the simple rects. Artifacts on corners are minor if width is small.
             
             float maxR = Mathf.Min(rect.width, rect.height) * 0.5f;
             topLeft = Mathf.Clamp(topLeft, 0, maxR);
             
             // Top
             EditorGUI.DrawRect(new Rect(rect.x + topLeft, rect.y, rect.width - topLeft - topRight, width), color);
             // Bottom
             EditorGUI.DrawRect(new Rect(rect.x + bottomLeft, rect.y + rect.height - width, rect.width - bottomLeft - bottomRight, width), color);
             // Left
             EditorGUI.DrawRect(new Rect(rect.x, rect.y + topLeft, width, rect.height - topLeft - bottomLeft), color);
             // Right
             EditorGUI.DrawRect(new Rect(rect.x + rect.width - width, rect.y + topRight, width, rect.height - topRight - bottomRight), color);
        }
    }
}
