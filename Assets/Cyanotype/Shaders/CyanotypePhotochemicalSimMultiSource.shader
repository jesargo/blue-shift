Shader "Cyanotype/PhotochemicalSimMultiSource"
{
    Properties
    {
        _PrevState ("State 1: E_uv, W_coat, E_bleach, washed", 2D) = "black" {}
        _PrevState2 ("State 2: C_T, cFAC, cKFC, oxid", 2D) = "black" {}
        _PrevState3 ("State 3: Committed Base-10 OD (RGB), Layer Count (A)", 2D) = "black" {}
        _FluidTex ("Fluid: water, sat, sens, wash", 2D) = "black" {}
        _BathTex ("Bath: alkali, tannin, peroxide", 2D) = "black" {}
        _ObjPosTex ("Baked Object Positions", 2D) = "black" {}
        _ObjNormTex ("Baked Object Normals", 2D) = "bump" {}
        _SunShadowMap ("Sun depth map (Built-in RP only)", 2D) = "white" {}
        _LampShadowMap ("Lamp depth map (Built-in RP only)", 2D) = "white" {}
        _SunOccTex ("Sun occlusion: R transmittance, G blocker distance", 2D) = "white" {}
        _LampOccTex ("Lamp occlusion: R transmittance, G blocker distance", 2D) = "white" {}
        _StencilTex ("Stencil Film Texture", 2D) = "white" {}
        _StencilMaskTex ("Custom Stencil Prop Mask", 2D) = "white" {}
        _StencilBounds ("Custom Stencil UV Bounds", Vector) = (0, 0, 0, 0)
        _StencilOpacity ("Custom Stencil Opacity", Float) = 0.0
        _StencilInvert ("Custom Stencil Invert", Float) = 0.0
    }
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        CGINCLUDE
        #include "UnityCG.cginc"
        #include "CyanotypeCommon.cginc"

        sampler2D _PrevState, _PrevState2, _PrevState3, _FluidTex, _BathTex, _ObjPosTex, _ObjNormTex;
        sampler2D _SunShadowMap, _SunOccTex, _LampShadowMap, _LampOccTex;
        sampler2D _StencilTex, _StencilMaskTex;

        float4x4 _SunVP, _LampVP, _StencilWorldToLocal, _LocalToWorld;
        float3 _SunDir, _SunPos, _LampPos, _LampDir;
        float _SunNear, _SunFar, _LampNear, _LampFar, _SunUVIntensity, _LampUVIntensity, _WindowAmbientUV;
        float _LampCosOuter, _LampCosInner, _ShadowBias, _UseShadowMaps;
        float _StencilActive, _TauClear, _InkDensity;
        float4 _StencilBounds;
        float _StencilOpacity, _StencilInvert;
        float _TimeScale, _DeltaTime, _BathTimeScale, _DebugQuickWash;
        float4 _PaperSizeM;
        float4 _RecipeNow;
        float _NormalSign;
        float _OccMaxHeight;
        float _SunPenumbra;
        float _LampSize;

        static const float2 CY_DISC[8] = {
            float2( 1.000,  0.000), float2(-1.000,  0.000),
            float2( 0.000,  1.000), float2( 0.000, -1.000),
            float2( 0.707,  0.707), float2(-0.707,  0.707),
            float2( 0.707, -0.707), float2(-0.707, -0.707)
        };

        // True when a texel has been washed + bleached (B >= 0.15) and new sensitizer is brushed on
        bool CanRecoatTexel(float4 s, float flSens)
        {
            float B = 1.0 - exp(-s.b);
            return (s.a > 0.35) && (B >= 0.15) && (flSens > 0.02);
        }

        float SoftVis(sampler2D occ, float2 uv, float penumbraPerUnit)
        {
            float2 uvPerUnit = 1.0 / max(_PaperSizeM.xy, 1e-3);
            float4 c         = tex2Dlod(occ, float4(uv, 0, 0));
            float  rMax      = penumbraPerUnit * _OccMaxHeight;
            float  hSum      = (c.r < 0.99) ? c.g : 0.0;
            float  n         = (c.r < 0.99) ? 1.0 : 0.0;

            [unroll] for (int k = 0; k < 8; k++)
            {
                float4 d = tex2Dlod(occ, float4(uv + CY_DISC[k] * rMax * uvPerUnit, 0, 0));
                if (d.r < 0.99)
                {
                    hSum += d.g;
                    n += 1.0;
                }
            }

            if (n < 0.5) return 1.0;

            float r   = penumbraPerUnit * (hSum / n);
            float acc = c.r;

            [unroll] for (int j = 0; j < 8; j++)
            {
                acc += tex2Dlod(occ, float4(uv + CY_DISC[j] * r * uvPerUnit, 0, 0)).r;
                acc += tex2Dlod(occ, float4(uv + CY_DISC[j] * r * 0.5 * uvPerUnit, 0, 0)).r;
            }
            return acc / 17.0;
        }

        float DepthMapVis(sampler2D shadowMap, float4x4 lightVP, float3 worldPos, float3 lightPos, float nearClip, float farClip)
        {
            float4 clipPos = mul(lightVP, float4(worldPos, 1.0));
            float3 ndc     = clipPos.xyz / clipPos.w;
            float2 uv      = ndc.xy * 0.5 + 0.5;
            if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0 || ndc.z < 0.0)
                return 0.0;

            float2 sd  = tex2D(shadowMap, uv).rg;
            float  cur = saturate((length(worldPos - lightPos) - nearClip) / (farClip - nearClip));
            return (cur - _ShadowBias > sd.r) ? sd.g : 1.0;
        }

        float EvaluateStencilRay(float3 worldPos, float3 dirToSource, float maxDist)
        {
            if (_StencilActive < 0.5) return 1.0;

            float3 lo = mul(_StencilWorldToLocal, float4(worldPos, 1.0)).xyz;
            float3 ld = mul((float3x3)_StencilWorldToLocal, dirToSource);
            if (abs(ld.z) < 1e-5) return 1.0;

            float t = -lo.z / ld.z;
            if (t <= 0.0001 || t >= maxDist) return 1.0;

            float3 hit = lo + ld * t;
            if (abs(hit.x) <= 0.5 && abs(hit.y) <= 0.5)
            {
                float lum = dot(tex2D(_StencilTex, hit.xy + 0.5).rgb, float3(0.299, 0.587, 0.114));
                return CY_InkTau(_TauClear, _InkDensity, 1.0 - lum);
            }
            return 1.0;
        }

        float EvaluateCustomStencilProp(float2 paperUV)
        {
            if (_StencilOpacity <= 0.001) return 1.0;

            if (paperUV.x >= _StencilBounds.x && paperUV.x <= _StencilBounds.z &&
                paperUV.y >= _StencilBounds.y && paperUV.y <= _StencilBounds.w)
            {
                float2 stUV = (paperUV - _StencilBounds.xy) / max(_StencilBounds.zw - _StencilBounds.xy, 1e-4);
                float4 sm   = tex2D(_StencilMaskTex, stUV);
                float  luma = dot(sm.rgb, float3(0.299, 0.587, 0.114));
                float  ink  = (sm.a < 0.95) ? sm.a : (1.0 - luma);
                if (_StencilInvert > 0.5) ink = 1.0 - ink;
                float od = -log10(max(1.0 - _StencilOpacity, 1e-3));
                return CY_OD2Tau(od * ink);
            }
            return 1.0;
        }
        ENDCG

        // Pass 0: State 1 (UV dose, coat, bleach dose, wash)
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment fragState

            float4 fragState(v2f_img i) : SV_Target
            {
                float4 s  = tex2D(_PrevState, i.uv);
                float4 s2 = tex2D(_PrevState2, i.uv);
                float4 fl = tex2D(_FluidTex, i.uv);
                float4 bt = tex2D(_BathTex, i.uv);

                bool recoat = CanRecoatTexel(s, fl.b);

                float E_uv   = recoat ? 0.0  : s.r;
                float W      = recoat ? fl.b : max(s.g, fl.b);
                float E_b    = recoat ? 0.0  : s.b;
                float washed = recoat ? 0.0  : max(s.a, fl.a);

                if (!recoat && _DebugQuickWash > 0.5)
                    washed = saturate(washed + _DeltaTime * 1.5);

                float3 worldPos  = mul(_LocalToWorld, float4(tex2D(_ObjPosTex, i.uv).xyz, 1.0)).xyz;
                float3 worldNorm = normalize(mul((float3x3)_LocalToWorld, tex2D(_ObjNormTex, i.uv).xyz)) * _NormalSign;

                float customT = EvaluateCustomStencilProp(i.uv);
                bool  useMaps = _UseShadowMaps > 0.5;

                // Sun
                float sunCos = CY_Lambert(worldNorm, -_SunDir);
                float sunVis = useMaps
                    ? DepthMapVis(_SunShadowMap, _SunVP, worldPos, _SunPos, _SunNear, _SunFar)
                    : SoftVis(_SunOccTex, i.uv, _SunPenumbra);
                float I_sun = _SunUVIntensity * sunCos * sunVis * EvaluateStencilRay(worldPos, -_SunDir, 100.0) * customT;

                // Carbon-Arc Lamp (inverse-square)
                float3 toLamp   = _LampPos - worldPos;
                float  lampDist = length(toLamp);
                float3 L1       = toLamp / max(lampDist, 1e-4);
                float  spot     = (_LampCosOuter > -0.5)
                    ? smoothstep(_LampCosOuter, _LampCosInner, dot(-L1, normalize(_LampDir)))
                    : 1.0;
                float lampVis = useMaps
                    ? DepthMapVis(_LampShadowMap, _LampVP, worldPos, _LampPos, _LampNear, _LampFar)
                    : SoftVis(_LampOccTex, i.uv, _LampSize / max(lampDist, 0.1));
                float I_lamp = _LampUVIntensity * CY_Lambert(worldNorm, L1) * spot * CY_LampFalloff(lampDist)
                    * lampVis * EvaluateStencilRay(worldPos, L1, lampDist) * customT;

                // Diffuse sky through the window (mip-blurred _SunOccTex works in both URP and Built-in RP)
                float ambVis = tex2Dlod(_SunOccTex, float4(i.uv, 0, 3)).r;
                float I_amb  = _WindowAmbientUV * ambVis * EvaluateStencilRay(worldPos, worldNorm, 100.0) * customT;

                float activeSens = max(0.0, 1.0 - washed);
                if (activeSens > 0.02 && W > 0.001)
                {
                    E_uv += (I_sun + I_lamp + I_amb) * activeSens * _DeltaTime * _TimeScale;
                }

                // Alkali bleach
                float2 activeRec = recoat ? _RecipeNow.xy : s2.gb;
                float  Y         = CY_Yield(E_uv, W, activeRec.x, activeRec.y);
                if (washed > 0.35 && bt.r > 0.001 && Y > 0.001)
                {
                    E_b += CY_BleachRate(bt.r, Y) * _DeltaTime * _BathTimeScale;
                }

                return float4(E_uv, W, E_b, washed);
            }
            ENDCG
        }

        // Pass 1: State 2 (toned sites, baked recipe, oxidation)
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment fragState2

            float4 fragState2(v2f_img i) : SV_Target
            {
                float4 s  = tex2D(_PrevState, i.uv);
                float4 s2 = tex2D(_PrevState2, i.uv);
                float4 fl = tex2D(_FluidTex, i.uv);
                float4 bt = tex2D(_BathTex, i.uv);

                if (CanRecoatTexel(s, fl.b))
                {
                    // Start a clean active state with the newly brushed recipe
                    return float4(0.0, _RecipeNow.x, _RecipeNow.y, 0.0);
                }

                float  C_T  = s2.r;
                float2 rec  = s2.gb;
                float  oxid = s2.a;
                float  dt   = _DeltaTime * _BathTimeScale;

                float dCoat = max(0.0, fl.b - s.g);
                if (dCoat > 1e-5)
                {
                    rec = (rec * s.g + _RecipeNow.xy * dCoat) / (s.g + dCoat);
                }

                float washed = max(s.a, fl.a);
                float W      = max(s.g, fl.b);
                float Y      = CY_Yield(s.r, W, rec.x, rec.y);
                float B      = 1.0 - exp(-s.b);
                float V      = B * Y;

                if (washed > 0.35)
                {
                    C_T += (V - C_T) * saturate(CY_ToneRate(bt.g) * dt);
                }
                C_T = min(C_T, V);

                float dry = 1.0 - saturate(fl.g);
                oxid = saturate(oxid + (bt.b * 0.5 + 0.01 * dry * washed) * dt);

                return float4(C_T, rec.x, rec.y, oxid);
            }
            ENDCG
        }

        // Pass 2: State 3 (accumulated base-10 optical density of bleached & recoated underlayers)
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment fragState3

            float4 fragState3(v2f_img i) : SV_Target
            {
                float4 s  = tex2D(_PrevState, i.uv);
                float4 s2 = tex2D(_PrevState2, i.uv);
                float4 s3 = tex2D(_PrevState3, i.uv);
                float4 fl = tex2D(_FluidTex, i.uv);

                if (CanRecoatTexel(s, fl.b))
                {
                    float W       = saturate(s.g);
                    float Y       = CY_Yield(s.r, W, max(s2.g, 2.0), max(s2.b, 1.0));
                    float B       = 1.0 - exp(-s.b);
                    float C_T     = s2.r;
                    float oxid    = saturate(s2.a);
                    float Y_pb    = Y * (1.0 - B);
                    float Y_ochre = max(0.0, B * Y - C_T);
                    float oxScale = lerp(0.62, 1.0, oxid);

                    float3 odCommitted = CY_DBASE  * W
                                       + CY_DMAX   * CY_EPS_PB    * (Y_pb * oxScale)
                                       + CY_DOCHRE * CY_EPS_OCHRE * Y_ochre
                                       + CY_DTONE  * CY_EPS_TONE  * C_T;

                    s3.rgb += odCommitted;
                    s3.a   += 1.0;
                }

                return s3;
            }
            ENDCG
        }
    }
}
