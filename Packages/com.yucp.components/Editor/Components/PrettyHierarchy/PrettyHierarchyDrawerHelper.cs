using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using YUCP.Components;

namespace YUCP.Components.Editor
{
    internal static class PrettyHierarchyDrawerHelper
    {
        private static Material _gradientMaterial;



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


            if (_gradientCache.Count > 100)
            {
                 foreach(var t in _gradientCache.Values) if(t) Object.DestroyImmediate(t);
                 _gradientCache.Clear();
            }

            _gradientCache[hash] = tex;
            return tex;
        }


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
                var shader = Shader.Find("Hidden/YUCP/PrettyHierarchyRoundedGradient");
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



        public static void DrawRoundedRect(Rect rect, Color color, float topLeft, float topRight, float bottomRight, float bottomLeft, float alpha = 1f)
        {
            DrawRoundedGradientRect(rect, Texture2D.whiteTexture, color, 0, topLeft, topRight, bottomRight, bottomLeft, alpha, 0.5f);
        }


        public static void DrawShadow(Rect rect, Color color, Vector2 offset, float blur, float topLeft, float topRight, float bottomRight, float bottomLeft)
        {
            Rect shadowRect = new Rect(rect.x + offset.x, rect.y + offset.y, rect.width, rect.height);



            DrawRoundedGradientRect(shadowRect, Texture2D.whiteTexture, color, 0, topLeft, topRight, bottomRight, bottomLeft, color.a, Mathf.Max(0.5f, blur));
        }

        /// <summary>Draws text with a gradient by sampling the gradient along horizontal strips. Angle 0 = left-to-right, 90 = top-to-bottom.</summary>
        public static void DrawGradientText(Rect rect, string text, GUIStyle baseStyle, Gradient gradient, float angleDeg)
        {
            if (gradient == null || string.IsNullOrEmpty(text)) return;
            const int segments = 8;
            bool horizontal = (angleDeg % 180f) < 90f;
            if (horizontal)
            {
                float segH = rect.height / segments;
                for (int i = 0; i < segments; i++)
                {
                    float t0 = i / (float)segments;
                    float t1 = (i + 1) / (float)segments;
                    Color c = gradient.Evaluate((t0 + t1) * 0.5f);
                    Rect strip = new Rect(rect.x, rect.y + i * segH, rect.width, segH + 1f);
                    GUI.BeginClip(strip);
                    var style = new GUIStyle(baseStyle) { normal = new GUIStyleState { textColor = c } };
                    EditorGUI.LabelField(new Rect(0, -i * segH, rect.width, rect.height), text, style);
                    GUI.EndClip();
                }
            }
            else
            {
                float segW = rect.width / segments;
                for (int i = 0; i < segments; i++)
                {
                    float t0 = i / (float)segments;
                    float t1 = (i + 1) / (float)segments;
                    Color c = gradient.Evaluate((t0 + t1) * 0.5f);
                    Rect strip = new Rect(rect.x + i * segW, rect.y, segW + 1f, rect.height);
                    GUI.BeginClip(strip);
                    var style = new GUIStyle(baseStyle) { normal = new GUIStyleState { textColor = c } };
                    EditorGUI.LabelField(new Rect(-i * segW, 0, rect.width, rect.height), text, style);
                    GUI.EndClip();
                }
            }
        }

        public static void DrawBorder(Rect rect, float width, Color32 color, float topLeft, float topRight, float bottomRight, float bottomLeft)
        {



             if (width <= 0) return;









             float maxR = Mathf.Min(rect.width, rect.height) * 0.5f;
             topLeft = Mathf.Clamp(topLeft, 0, maxR);


             EditorGUI.DrawRect(new Rect(rect.x + topLeft, rect.y, rect.width - topLeft - topRight, width), color);

             EditorGUI.DrawRect(new Rect(rect.x + bottomLeft, rect.y + rect.height - width, rect.width - bottomLeft - bottomRight, width), color);

             EditorGUI.DrawRect(new Rect(rect.x, rect.y + topLeft, width, rect.height - topLeft - bottomLeft), color);

             EditorGUI.DrawRect(new Rect(rect.x + rect.width - width, rect.y + topRight, width, rect.height - topRight - bottomRight), color);
        }
    }
}
