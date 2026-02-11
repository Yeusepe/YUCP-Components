Shader "Hidden/YUCP/PrettyHierarchyRoundedGradient"
{
    Properties
    {
        _GradientTex ("Gradient Texture", 2D) = "white" {}
        _Color ("Main Color", Color) = (1,1,1,1) // Multiplier
        _Angle ("Angle", Float) = 0
        _RectW ("Rect Width", Float) = 100
        _RectH ("Rect Height", Float) = 20
        _RadiusTL ("Radius Top Left", Float) = 4
        _RadiusTR ("Radius Top Right", Float) = 4
        _RadiusBR ("Radius Bottom Right", Float) = 4
        _RadiusBL ("Radius Bottom Left", Float) = 4
        _Softness ("Softness", Float) = 0.5
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 100
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _GradientTex;
            float4 _Color;
            float _Angle;
            float _RectW;
            float _RectH;
            float _RadiusTL;
            float _RadiusTR;
            float _RadiusBR;
            float _RadiusBL;
            float _Softness;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // Standard SDF for a rounded box with independent corners
            // p: point relative to center of box
            // b: half-extents of box
            // r: radii (x=TR, y=BR, z=TL, w=BL)
            float sdRoundedBox(float2 p, float2 b, float4 r)
            {
                r.xy = (p.x > 0.0) ? r.xy : r.zw;
                r.x  = (p.y > 0.0) ? r.x  : r.y;
                float2 q = abs(p) - b + r.x;
                return min(max(q.x,q.y),0.0) + length(max(q,0.0)) - r.x;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // 1. Rotate UVs for Gradient
                float ang = _Angle * 0.0174532925; // Deg to Rad
                float c = cos(ang);
                float s = sin(ang);
                
                // Pivot at 0.5, 0.5
                float2 center = float2(0.5, 0.5);
                float2 uvCentered = i.uv - center;
                float2 uvRotated = float2(
                    uvCentered.x * c - uvCentered.y * s,
                    uvCentered.x * s + uvCentered.y * c
                ) + center;

                // Clamp/Repeat logic usually handled by Texture Import Settings, but for generated texture used here:
                // We want Clamp.
                
                fixed4 col = tex2D(_GradientTex, float2(uvRotated.x, 0.5)) * _Color;

                // 2. SDF Masking (Rounded Rect)
                float2 pos = i.uv * float2(_RectW, _RectH);
                float2 halfSize = float2(_RectW * 0.5, _RectH * 0.5);
                float2 p = pos - halfSize;
                
                // Flip Y check? If drawing on screen (0,0) is usually bottom-left in GL, top-left in GUI.
                // If Top-Left is (0,0), then p.y > 0 is Bottom.
                // Let's assume standard behavior where RenderTextue/Texture is 0,0 bottom-left.
                // But DrawPreviewTexture?
                
                float4 radii = float4(_RadiusTR, _RadiusBR, _RadiusTL, _RadiusBL);
                float dist = sdRoundedBox(p, halfSize, radii);
                
                // Softness:
                // dist < 0 inside.
                // smoothstep edge.
                // If softness is 0.5 (standard AA): -0.5 to 0.5.
                // If softness is 10 (blur): -10 to 10?
                // For shadow, we want it to fade out.
                
                float alpha = 1.0 - smoothstep(-_Softness, _Softness, dist);
                col.a *= alpha;
                
                return col;
            }
            ENDCG
        }
    }
}
