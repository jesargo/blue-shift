Shader "Cyanotype/StencilShadowDepth"
{
    Properties
    {
        _StencilTex ("Stencil Film Texture", 2D) = "white" {}
        _FilmTint ("Film Base Tint", Color) = (0.15, 0.35, 0.85, 0.85)
        _TauClear ("Clear Film Transmittance", Range(0.5, 1.0)) = 0.92
        _InkDensity ("Ink Base-10 Optical Density", Range(0.0, 3.0)) = 1.05
        _IsStencilFilm ("Is Stencil Film (1=Film, 0=Opaque Blocker)", Float) = 0.0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "CyanotypeCommon.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos      : SV_POSITION;
                float2 uv       : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
            };

            sampler2D _StencilTex;
            float4 _FilmTint;
            float _TauClear;
            float _InkDensity;
            float _IsStencilFilm;
            float3 _LightPos;
            float _NearClip;
            float _FarClip;
            float _RenderingShadowMap;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos      = UnityObjectToClipPos(v.vertex);
                o.uv       = v.uv;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float lum     = dot(tex2D(_StencilTex, i.uv).rgb, float3(0.299, 0.587, 0.114));
                float inkMask = saturate(1.0 - lum);

                // Built-in RP shadow camera pass: output RG = (normalized depth, base-10 transmittance)
                if (_RenderingShadowMap > 0.5)
                {
                    float dist  = length(i.worldPos - _LightPos);
                    float depth = saturate((dist - _NearClip) / max(_FarClip - _NearClip, 1e-4));
                    float tau   = (_IsStencilFilm > 0.5)
                        ? CY_InkTau(_TauClear, _InkDensity, inkMask)
                        : 0.0;
                    return float4(depth, tau, 0.0, 1.0);
                }

                // Main camera pass: translucent blue-tinted acetate with dark negative ink
                float3 visRGB   = lerp(_FilmTint.rgb, float3(0.06, 0.06, 0.08), inkMask);
                float  visAlpha = lerp(0.16, 0.90, inkMask) * _FilmTint.a;
                return float4(visRGB, visAlpha);
            }
            ENDCG
        }
    }
}
