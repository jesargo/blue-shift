Shader "Cyanotype/SurfaceDisplay"
{
    Properties
    {
        _ChemStateTex ("Chemical State (ARGBFloat)", 2D) = "black" {}
        _FluidTex ("Fluid State (ARGBFloat)", 2D) = "black" {}
        _SunUVIntensity ("Current Sun UV Intensity", Float) = 1.0

        [Header(Chemical Concentration Parameters)]
        _ConcFAC ("Ferric Ammonium Citrate (g/100mL)", Range(2.0, 40.0)) = 20.0
        _ConcKFC ("Potassium Ferricyanide (g/100mL)", Range(1.0, 25.0)) = 8.0
        _CoatingWeight ("Coating Thickness Multiplier", Range(0.0, 2.0)) = 1.0

        [Header(Post Wash Toning Baths)]
        _PeroxideBoost ("Hydrogen Peroxide Oxidation", Range(0.0, 1.0)) = 0.0
        _TanninTone ("Tea / Tannin Bath Toning", Range(0.0, 1.0)) = 0.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _ChemStateTex;
            sampler2D _FluidTex;
            float _SunUVIntensity;
            float _ConcFAC;
            float _ConcKFC;
            float _CoatingWeight;
            float _PeroxideBoost;
            float _TanninTone;

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

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float4 state   = tex2D(_ChemStateTex, i.uv);
                float4 fluid   = tex2D(_FluidTex, i.uv);

                float E_uv     = state.r;
                float rawCoat  = max(state.g, fluid.b);
                float W_coat   = clamp(rawCoat * max(_CoatingWeight, 1.0), 0.0, 1.35);
                float E_bleach = state.b;
                float washed   = max(state.a, fluid.a);

                float rFAC = _ConcFAC / 20.0;
                float rKFC = _ConcKFC / 8.0;
                float rLimiting = min(rFAC, rKFC);

                float phiMax = (1.0 - exp(-12.0 * rLimiting)) * clamp(rLimiting, 0.1, 1.35);
                float kEff   = 0.0085 * clamp(rFAC / max(rKFC, 0.15), 0.35, 2.5);
                float Y      = saturate(W_coat * phiMax * (1.0 - exp(-kEff * pow(max(E_uv, 0.0), 1.65))));

                // Hydrogen peroxide accelerates oxidation of Berlin white -> deep Prussian blue
                float Y_oxidized = saturate(Y * (1.0 + _PeroxideBoost * 0.45));
                float B = 1.0 - exp(-0.18 * E_bleach);

                float3 eps_sensitizer = float3(0.32, 0.18, 2.25) * rFAC;
                float3 eps_prussian   = float3(3.60, 1.65, 0.24);
                float3 eps_alkali     = float3(0.45, 0.65, 1.15);
                float3 eps_tannin     = float3(0.42, 0.82, 1.45); // Warm sepia/plum iron-tannate

                float D_base = 0.02;
                float D_max  = 1.85 * clamp(rLimiting, 0.15, 1.35);

                float3 tauUnwashed;
                {
                    float unexposedSens = clamp((1.0 - Y) * W_coat, 0.0, 1.30);
                    float3 D_rgb = D_base
                                 + eps_sensitizer * unexposedSens * 0.95
                                 + eps_prussian * (D_max * Y * 0.65);
                    tauUnwashed = exp(-D_rgb);
                }

                float3 tauWashed;
                {
                    float D_blue   = D_base + (D_max - D_base) * Y_oxidized * (1.0 - B);
                    float D_ochre  = D_max * Y_oxidized * B * 0.48;
                    float D_tannin = _TanninTone * (0.14 + 0.85 * Y_oxidized);
                    float dissolvedPlume = clamp(fluid.b * fluid.r * 1.15, 0.0, 0.85);
                    float3 D_rgb   = eps_prussian * D_blue
                                   + eps_alkali * D_ochre
                                   + eps_tannin * D_tannin
                                   + eps_sensitizer * dissolvedPlume;
                    tauWashed = exp(-D_rgb);
                }

                float washBlend = smoothstep(0.05, 0.92, clamp(washed, 0.0, 1.0));
                float3 tau = lerp(tauUnwashed, tauWashed, washBlend);

                float wetEdge = smoothstep(0.02, 0.18, fluid.r) * (1.0 - smoothstep(0.18, 0.45, fluid.r));
                float wetnessDarken = 1.0 - clamp(fluid.r * 0.12 + fluid.g * 0.05 + wetEdge * 0.09, 0.0, 0.24);
                tau *= wetnessDarken;

                float3 srgb = pow(saturate(tau), 1.0 / 2.2);
                return float4(srgb, 1.0);
            }
            ENDCG
        }
    }
}