Shader "Cyanotype/SurfaceDisplay"
{
    Properties
    {
        _ChemStateTex ("State 1: E_uv, W_coat, E_bleach, washed", 2D) = "black" {}
        _ChemStateTex2 ("State 2: C_T, cFAC, cKFC, oxid", 2D) = "black" {}
        _ChemStateTex3 ("State 3: Committed Underlayer OD (RGB), Layer Count (A)", 2D) = "black" {}
        _FluidTex ("Fluid: water, sat, sens, wash", 2D) = "black" {}
        _BathTex ("Bath: alkali, tannin, peroxide", 2D) = "black" {}
        _PaperColor ("Cotton Paper Base Color", Color) = (0.96, 0.94, 0.89, 1.0)
        _Roughness ("Dry Paper Roughness", Range(0.2, 1.0)) = 0.86
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 200

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "CyanotypeCommon.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos       : SV_POSITION;
                float2 uv        : TEXCOORD0;
                float3 worldPos  : TEXCOORD1;
                float3 worldNorm : TEXCOORD2;
            };

            sampler2D _ChemStateTex;
            sampler2D _ChemStateTex2;
            sampler2D _ChemStateTex3;
            sampler2D _FluidTex;
            sampler2D _BathTex;
            float4 _PaperColor;
            float _Roughness;

            float hash21(float2 p)
            {
                p = frac(p * float2(127.1, 311.7));
                p += dot(p, p + 34.23);
                return frac(p.x * p.y);
            }

            float vnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash21(i);
                float b = hash21(i + float2(1.0, 0.0));
                float c = hash21(i + float2(0.0, 1.0));
                float d = hash21(i + float2(1.0, 1.0));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.pos       = UnityObjectToClipPos(v.vertex);
                o.uv        = v.uv;
                o.worldPos  = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNorm = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float4 s  = tex2D(_ChemStateTex, i.uv);
                float4 s2 = tex2D(_ChemStateTex2, i.uv);
                float4 s3 = tex2D(_ChemStateTex3, i.uv);
                float4 fl = tex2D(_FluidTex, i.uv);
                float4 bt = tex2D(_BathTex, i.uv);

                float E_uv     = s.r;
                float W        = saturate(max(s.g, fl.b));
                float E_bleach = s.b;
                float washed   = saturate(max(s.a, fl.a));

                float C_T  = s2.r;
                float cFAC = max(s2.g, 2.0);
                float cKFC = max(s2.b, 1.0);
                float oxid = saturate(s2.a);

                float fiber = 0.96 + 0.04 * vnoise(i.uv * float2(140.0, 195.0));

                float prog;
                float Y = CY_Yield(E_uv, W, cFAC, cKFC, prog);

                // 1. Active layer pre-wash optical density
                float3 odSens    = CY_EPS_SENS * W * (1.0 - prog);
                float3 odPrewash = CY_DBASE * W + CY_PrewashOD(prog, W, cFAC) + odSens;

                // 2. Active layer washed optical density
                float B       = 1.0 - exp(-E_bleach);
                float Y_pb    = Y * (1.0 - B);
                float Y_ochre = max(0.0, B * Y - C_T);
                float oxScale = lerp(0.62, 1.0, oxid);

                float3 odWashed = CY_DBASE  * W
                                + CY_DMAX   * CY_EPS_PB    * (Y_pb * oxScale)
                                + CY_DOCHRE * CY_EPS_OCHRE * Y_ochre
                                + CY_DTONE  * CY_EPS_TONE  * C_T
                                + odSens    * (1.0 - washed);

                float3 odBathLiquid = float3(0.02, 0.06, 0.14) * saturate(bt.g) * 0.45;

                // Additive base-10 OD: committed bleached underlayers (s3.rgb) + active layer
                float3 odTotal = s3.rgb + lerp(odPrewash, odWashed, washed) + odBathLiquid;

                #ifdef UNITY_COLORSPACE_GAMMA
                    float3 paperLinear = pow(max(_PaperColor.rgb, 0.0), 2.2) * fiber;
                #else
                    float3 paperLinear = _PaperColor.rgb * fiber;
                #endif

                float wetness = saturate(max(fl.g, fl.r * 0.7));
                paperLinear *= lerp(1.0, 0.84, wetness);

                float3 reflLinear = paperLinear * CY_OD2Tau(odTotal);

                float3 L     = normalize(_WorldSpaceLightPos0.xyz + float3(0.0, 0.85, 0.15));
                float  ndotl = saturate(dot(normalize(i.worldNorm), L));
                reflLinear  *= lerp(0.78, 1.05, ndotl);

                #ifdef UNITY_COLORSPACE_GAMMA
                    float3 outCol = pow(max(reflLinear, 0.0), 1.0 / 2.2);
                #else
                    float3 outCol = reflLinear;
                #endif

                return fixed4(saturate(outCol), 1.0);
            }
            ENDCG
        }
    }
}