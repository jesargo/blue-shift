Shader "Cyanotype/BathTransport"
{
    Properties
    {
        _PrevBath ("Previous Bath State: R=Alkali, G=Tannin, B=Peroxide, A=Carrier", 2D) = "black" {}
        _FluidTex ("Fluid State: R=Water, G=Sat, B=Sens, A=Wash", 2D) = "black" {}
        _BrushPos ("Current Brush UV", Vector) = (-1, -1, 0, 0)
        _PrevBrushPos ("Previous Brush UV", Vector) = (-1, -1, 0, 0)
        _BrushRadius ("Brush Radius", Float) = 0.06
        _SprayRadius ("Spray Mist Radius", Float) = 0.16
        _BrushStrength ("Brush Strength", Float) = 1.2
        _BathMode ("0=None, 4=Alkali, 5=Tannin, 6=Peroxide Spray+Brush", Float) = 0.0
        _SubmergeBath ("0=None, 1=AlkaliTray, 2=TanninTray, 3=PeroxideTray", Float) = 0.0
        _DeltaTime ("Delta Time", Float) = 0.016
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

            sampler2D _PrevBath;
            float4 _PrevBath_TexelSize;
            sampler2D _FluidTex;
            float2 _BrushPos;
            float2 _PrevBrushPos;
            float _BrushRadius;
            float _SprayRadius;
            float _BrushStrength;
            float _BathMode;
            float _SubmergeBath;
            float _DeltaTime;

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float sdSegment(float2 p, float2 a, float2 b)
            {
                float2 pa = p - a, ba = b - a;
                float  h  = clamp(dot(pa, ba) / max(dot(ba, ba), 1e-6), 0.0, 1.0);
                return length(pa - ba * h);
            }

            float4 frag(v2f_img i) : SV_Target
            {
                float  dt    = clamp(_DeltaTime, 0.001, 0.033);
                float2 texel = _PrevBath_TexelSize.xy;

                float4 c  = tex2D(_PrevBath, i.uv);
                float4 n  = tex2D(_PrevBath, i.uv + float2(0.0, texel.y));
                float4 s  = tex2D(_PrevBath, i.uv - float2(0.0, texel.y));
                float4 e  = tex2D(_PrevBath, i.uv + float2(texel.x, 0.0));
                float4 w  = tex2D(_PrevBath, i.uv - float2(texel.x, 0.0));
                float4 fl = tex2D(_FluidTex, i.uv);

                // Lateral capillary diffusion across damp fibers
                float  wetMobility = lerp(0.03, 0.22, saturate(max(fl.g, c.a)));
                float4 lap         = (n + s + e + w - 4.0 * c);
                float4 bath        = max(0.0, c + lap * wetMobility * dt * 12.0);

                // Evaporation / consumption decay
                bath.rgb *= max(0.0, 1.0 - 0.045 * dt);
                bath.a    = max(0.0, bath.a - 0.08 * dt);

                // Clean water rinse flushes out dissolved bath chemicals
                float rinse = smoothstep(0.28, 0.85, fl.r);
                if (rinse > 0.01)
                {
                    bath.rgb *= max(0.0, 1.0 - 1.6 * rinse * dt);
                }

                // Tray submersion (1=Alkali, 2=Tannin, 3=Peroxide)
                if (_SubmergeBath > 0.5)
                {
                    float fill = 2.5 * dt;
                    if (abs(_SubmergeBath - 1.0) < 0.2) bath.r = min(1.0, bath.r + fill);
                    if (abs(_SubmergeBath - 2.0) < 0.2) bath.g = min(1.0, bath.g + fill);
                    if (abs(_SubmergeBath - 3.0) < 0.2) bath.b = min(1.0, bath.b + fill);
                    bath.a = min(1.0, bath.a + fill);
                }

                // Interactive application (4=Alkali Brush, 5=Tannin Brush, 6=Peroxide Spray + Brush Combo)
                if (_BathMode > 3.5 && _BathMode < 6.5 && _BrushPos.x >= 0.0)
                {
                    float dist      = sdSegment(i.uv, _PrevBrushPos, _BrushPos);
                    float brushMask = smoothstep(_BrushRadius, _BrushRadius * 0.12, dist);
                    float dose      = brushMask * _BrushStrength * dt * 3.5;

                    if (abs(_BathMode - 4.0) < 0.2)
                    {
                        bath.r = min(1.0, bath.r + dose);
                        bath.a = min(1.0, bath.a + dose);
                    }
                    else if (abs(_BathMode - 5.0) < 0.2)
                    {
                        bath.g = min(1.0, bath.g + dose);
                        bath.a = min(1.0, bath.a + dose);
                    }
                    else if (abs(_BathMode - 6.0) < 0.2)
                    {
                        // Combo: wide atomized mist cone + dense direct bristle stroke
                        float sprayRad   = max(_SprayRadius, _BrushRadius * 2.4);
                        float droplet    = step(0.32, hash21(i.uv * 420.0 + _Time.yy * 17.3));
                        float mistFallof = smoothstep(sprayRad, _BrushRadius * 0.5, dist);
                        float mistDose   = mistFallof * (0.45 + 0.55 * droplet) * _BrushStrength * dt * 2.2;
                        float totalDose  = max(dose, mistDose);

                        bath.b = min(1.0, bath.b + totalDose);
                        bath.a = min(1.0, bath.a + totalDose * 0.85);
                    }
                }

                return bath;
            }
            ENDCG
        }
    }
}