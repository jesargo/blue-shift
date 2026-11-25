Shader "Cyanotype/PhotochemicalSimMultiSource"
{
    Properties
    {
        _PrevState ("Previous Chemical State (ARGBFloat)", 2D) = "black" {}
        _FluidTex ("Fluid State (ARGBFloat)", 2D) = "black" {}
        _ObjPosTex ("Baked Object Positions", 2D) = "black" {}
        _ObjNormTex ("Baked Object Normals", 2D) = "bump" {}
        _SunShadowMap ("Sun Shadow & Transmittance (RGFloat)", 2D) = "white" {}
        _LampShadowMap ("UV Lamp Shadow & Transmittance (RGFloat)", 2D) = "white" {}
        _SunOccTex ("Sun Raycast Occlusion (RFloat)", 2D) = "white" {}
        _LampOccTex ("UV Lamp Raycast Occlusion (RFloat)", 2D) = "white" {}
        _StencilTex ("Stencil Film Texture", 2D) = "white" {}

        _StencilMaskTex ("Custom Stencil Prop Mask", 2D) = "white" {}
        _StencilBounds ("Custom Stencil UV Bounds", Vector) = (0, 0, 0, 0)
        _StencilOpacity ("Custom Stencil Opacity", Float) = 0.0
        _StencilInvert ("Custom Stencil Invert", Float) = 0.0
    }
    SubShader
    {
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _PrevState;
            float4 _PrevState_TexelSize;
            sampler2D _FluidTex;
            sampler2D _ObjPosTex;
            sampler2D _ObjNormTex;

            sampler2D _SunShadowMap;
            sampler2D _SunOccTex;
            float4x4 _SunVP;
            float3 _SunDir;
            float3 _SunPos;
            float _SunNear;
            float _SunFar;
            float _SunUVIntensity;
            float _WindowAmbientUV;

            sampler2D _LampShadowMap;
            sampler2D _LampOccTex;
            float4x4 _LampVP;
            float3 _LampPos;
            float3 _LampDir;
            float _LampNear;
            float _LampFar;
            float _LampUVIntensity;
            float _LampCosOuter;
            float _LampCosInner;

            sampler2D _StencilTex;
            float4x4 _StencilWorldToLocal;
            float _StencilActive;
            float _TauClear;
            float _InkDensity;

            sampler2D _StencilMaskTex;
            float4 _StencilBounds;
            float _StencilOpacity;
            float _StencilInvert;

            float4x4 _LocalToWorld;
            float _ShadowBias;
            float _TimeScale;
            float _DeltaTime;
            float _TriggerWash;
            float _BleachIntensity;
            float _UseShadowMaps;

            float EvaluateLightTransmittance(
                sampler2D shadowMap, sampler2D occTex, float2 paperUV, float4x4 lightVP,
                float3 worldPos, float3 lightPos, float nearClip, float farClip)
            {
                if (_UseShadowMaps < 0.5)
                    return tex2D(occTex, paperUV).r;

                float4 clipPos = mul(lightVP, float4(worldPos, 1.0));
                float3 ndc = clipPos.xyz / clipPos.w;
                float2 uv = ndc.xy * 0.5 + 0.5;

                if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0 || ndc.z < 0.0)
                    return 0.0;

                float2 shadowData = tex2D(shadowMap, uv).rg;
                float casterDepth = shadowData.r;
                float casterTransmittance = shadowData.g;

                float currentDist = length(worldPos - lightPos);
                float currentDepth = saturate((currentDist - nearClip) / (farClip - nearClip));

                if (currentDepth - _ShadowBias > casterDepth)
                    return casterTransmittance;

                return 1.0;
            }

            float EvaluateStencilRay(float3 worldPos, float3 lightDirToSource, float maxDist)
            {
                if (_StencilActive < 0.5) return 1.0;

                float3 localOrigin = mul(_StencilWorldToLocal, float4(worldPos, 1.0)).xyz;
                float3 localDir    = mul((float3x3)_StencilWorldToLocal, lightDirToSource);

                if (abs(localDir.z) < 1e-5) return 1.0;
                float t = -localOrigin.z / localDir.z;

                if (t <= 0.0001 || t >= maxDist) return 1.0;

                float3 hitLocal = localOrigin + localDir * t;

                if (abs(hitLocal.x) <= 0.5 && abs(hitLocal.y) <= 0.5)
                {
                    float2 stencilUV = hitLocal.xy + 0.5;
                    float lum = dot(tex2D(_StencilTex, stencilUV).rgb, float3(0.299, 0.587, 0.114));
                    float inkMask = 1.0 - lum;
                    return _TauClear * exp(-_InkDensity * inkMask);
                }

                return 1.0;
            }

            float EvaluateCustomStencilProp(float2 paperUV)
            {
                if (_StencilOpacity <= 0.001) return 1.0;

                if (paperUV.x >= _StencilBounds.x && paperUV.x <= _StencilBounds.z &&
                    paperUV.y >= _StencilBounds.y && paperUV.y <= _StencilBounds.w)
                {
                    float2 stUV = float2(
                        (paperUV.x - _StencilBounds.x) / max(_StencilBounds.z - _StencilBounds.x, 1e-4),
                        (paperUV.y - _StencilBounds.y) / max(_StencilBounds.w - _StencilBounds.y, 1e-4)
                    );
                    float4 stSample = tex2D(_StencilMaskTex, stUV);
                    float luma = dot(stSample.rgb, float3(0.299, 0.587, 0.114));
                    float inkMask = (stSample.a < 0.95) ? stSample.a : (1.0 - luma);
                    if (_StencilInvert > 0.5) inkMask = 1.0 - inkMask;

                    return 1.0 - saturate(inkMask * _StencilOpacity);
                }

                return 1.0;
            }

            float4 frag(v2f_img i) : SV_Target
            {
                float4 state   = tex2D(_PrevState, i.uv);
                float4 fluid   = tex2D(_FluidTex, i.uv);

                float E_uv     = state.r;
                float W_coat   = max(state.g, fluid.b);
                float E_bleach = state.b;
                float washed   = max(state.a, fluid.a);

                if (_TriggerWash > 0.5)
                    washed = saturate(washed + _DeltaTime * 1.5);

                float3 localPos  = tex2D(_ObjPosTex, i.uv).xyz;
                float3 localNorm = tex2D(_ObjNormTex, i.uv).xyz;
                float3 worldPos  = mul(_LocalToWorld, float4(localPos, 1.0)).xyz;
                float3 worldNorm = normalize(mul((float3x3)_LocalToWorld, localNorm));
                if (worldNorm.y < 0.0) worldNorm = -worldNorm;

                float customStencilT = EvaluateCustomStencilProp(i.uv);

                float sunNdotL    = max(0.25, dot(worldNorm, -_SunDir));
                float sunVis      = EvaluateLightTransmittance(_SunShadowMap, _SunOccTex, i.uv, _SunVP, worldPos, _SunPos, _SunNear, _SunFar);
                float sunStencilT = EvaluateStencilRay(worldPos, -_SunDir, 100.0) * customStencilT;
                float I_sun_local = _SunUVIntensity * sunNdotL * sunVis * sunStencilT;

                float3 toLamp      = _LampPos - worldPos;
                float lampDist     = length(toLamp);
                float3 L_lamp      = toLamp / max(lampDist, 0.0001);
                float lampNdotL    = max(0.50, dot(worldNorm, L_lamp));
                float cosAngle     = dot(-L_lamp, normalize(_LampDir));
                float spotAtten    = (_LampCosOuter > -0.5) ? smoothstep(_LampCosOuter, _LampCosInner, cosAngle) : 1.0;
                float distAtten    = 1.0 / (1.0 + 0.15 * lampDist * lampDist);
                float lampVis      = EvaluateLightTransmittance(_LampShadowMap, _LampOccTex, i.uv, _LampVP, worldPos, _LampPos, _LampNear, _LampFar);
                float lampStencilT = EvaluateStencilRay(worldPos, L_lamp, lampDist) * customStencilT;
                float I_lamp_local = _LampUVIntensity * lampNdotL * spotAtten * distAtten * lampVis * lampStencilT;

                float windowOcc      = min(sunVis, lampVis) * customStencilT;
                float I_window_local = _WindowAmbientUV * windowOcc;

                float totalIrradiance = I_sun_local + I_lamp_local + I_window_local;

                float activeSens = max(0.0, 1.0 - washed);
                if (activeSens > 0.02 && W_coat > 0.001)
                {
                    E_uv += totalIrradiance * activeSens * _DeltaTime * _TimeScale;
                }

                if (washed > 0.35 && _BleachIntensity > 0.0)
                {
                    E_bleach += pow(_BleachIntensity, 1.20) * washed * _DeltaTime;
                }

                return float4(E_uv, W_coat, E_bleach, washed);
            }
            ENDCG
        }
    }
}