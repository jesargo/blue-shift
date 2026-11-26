Shader "Cyanotype/StencilShadowDepth"
{
    Properties
    {
        _StencilTex ("Stencil Film (Luminance = Transmittance)", 2D) = "white" {}
        _FilmTint ("Acetate Film Visual Tint", Color) = (0.15, 0.35, 0.85, 0.65)
        _TauClear ("Clear Film Transmittance", Range(0.5, 1.0)) = 0.92
        _InkDensity ("Max Ink Optical Density", Range(0.5, 4.0)) = 2.40
        _IsStencil ("Is Variable Opacity Stencil", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        // Pass 0: Visible Scene Rendering in URP (Translucent Blue Acetate Film)
        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vertVis
            #pragma fragment fragVis
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_StencilTex);
            SAMPLER(sampler_StencilTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _StencilTex_ST;
                half4 _FilmTint;
                half _TauClear;
                half _InkDensity;
                half _IsStencil;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vertVis(Attributes v)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _StencilTex);
                return o;
            }

            half4 fragVis(Varyings i) : SV_Target
            {
                half3 texCol = SAMPLE_TEXTURE2D(_StencilTex, sampler_StencilTex, i.uv).rgb;
                half lum = dot(texCol, half3(0.299, 0.587, 0.114));
                // Dark ink regions appear dense/opaque; bright aperture regions appear clear
                half alpha = lerp(0.92h, 0.25h, lum) * _FilmTint.a;
                half3 col = lerp(half3(0.03h, 0.05h, 0.12h), _FilmTint.rgb, lum);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }

    // Fallback SubShader for Built-in Render Pipeline (Visible Pass + UVShadowPass)
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vertVisBuiltIn
            #pragma fragment fragVisBuiltIn
            #include "UnityCG.cginc"

            sampler2D _StencilTex;
            float4 _StencilTex_ST;
            float4 _FilmTint;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vertVisBuiltIn(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _StencilTex);
                return o;
            }

            float4 fragVisBuiltIn(v2f i) : SV_Target
            {
                float lum = dot(tex2D(_StencilTex, i.uv).rgb, float3(0.299, 0.587, 0.114));
                float alpha = lerp(0.92, 0.25, lum) * _FilmTint.a;
                float3 col = lerp(float3(0.03, 0.05, 0.12), _FilmTint.rgb, lum);
                return float4(col, alpha);
            }
            ENDCG
        }

        Pass
        {
            Name "UVShadowPass"
            Tags { "LightMode" = "UVShadowPass" }
            Cull Off
            ZWrite On

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _StencilTex;
            float4 _StencilTex_ST;
            float _TauClear;
            float _InkDensity;
            float _IsStencil;

            float3 _LightPos;
            float _NearClip;
            float _FarClip;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float2 uv : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.uv = TRANSFORM_TEX(v.uv, _StencilTex);
                return o;
            }

            float2 frag(v2f i) : SV_Target
            {
                float dist = length(i.worldPos - _LightPos);
                float normDepth = saturate((dist - _NearClip) / (_FarClip - _NearClip));

                float transmittance = 0.0;
                if (_IsStencil > 0.5)
                {
                    float lum = dot(tex2D(_StencilTex, i.uv).rgb, float3(0.299, 0.587, 0.114));
                    float inkMask = 1.0 - lum;
                    transmittance = _TauClear * exp(-_InkDensity * inkMask);
                }

                return float2(normDepth, transmittance);
            }
            ENDCG
        }
    }

    // Fallback SubShader for standard opaque walls/props when calling RenderWithShader(shader, "")
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vertOpaque
            #pragma fragment fragOpaque
            #include "UnityCG.cginc"

            float3 _LightPos;
            float _NearClip;
            float _FarClip;

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 worldPos : TEXCOORD0; };

            v2f vertOpaque(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            float2 fragOpaque(v2f i) : SV_Target
            {
                float dist = length(i.worldPos - _LightPos);
                float normDepth = saturate((dist - _NearClip) / (_FarClip - _NearClip));
                return float2(normDepth, 0.0);
            }
            ENDCG
        }
    }
}