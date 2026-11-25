Shader "Cyanotype/FluidTransport"
{
    Properties
    {
        _PrevFluid ("Previous Fluid State (ARGBFloat)", 2D) = "black" {}
        _BrushPos ("Current Brush UV", Vector) = (-1, -1, 0, 0)
        _PrevBrushPos ("Previous Brush UV", Vector) = (-1, -1, 0, 0)
        _BrushRadius ("Brush Radius", Float) = 0.06
        _BrushStrength ("Brush Strength", Float) = 3.5
        _BrushMode ("0=None, 1=Coat Sensitizer, 2=Pour Water Wash, 3=Submerge Tray", Float) = 0.0
        _ApplicatorShape ("0=Hake Brush, 1=Foam Roller", Float) = 0.0
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

            sampler2D _PrevFluid;
            float4 _PrevFluid_TexelSize;
            float2 _BrushPos;
            float2 _PrevBrushPos;
            float _BrushRadius;
            float _BrushStrength;
            float _BrushMode;
            float _ApplicatorShape;
            float _DeltaTime;

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
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

            float fbm(float2 p)
            {
                float v = 0.0;
                float a = 0.5;
                for (int k = 0; k < 4; k++)
                {
                    v += a * vnoise(p);
                    p *= 2.03;
                    a *= 0.5;
                }
                return v;
            }

            float sdSegment(float2 p, float2 a, float2 b)
            {
                float2 pa = p - a, ba = b - a;
                float h = clamp(dot(pa, ba) / max(dot(ba, ba), 1e-6), 0.0, 1.0);
                return length(pa - ba * h);
            }

            float4 frag(v2f_img i) : SV_Target
            {
                float dt = clamp(_DeltaTime, 0.001, 0.033);
                float2 texel = _PrevFluid_TexelSize.xy;

                float fiberMap = fbm(i.uv * float2(26.0, 38.0));
                float fineGrain = hash21(i.uv * 512.0);
                float fiberPerm = 0.45 + 1.15 * fiberMap;

                float4 c = tex2D(_PrevFluid, i.uv);
                float4 n = tex2D(_PrevFluid, i.uv + float2(0.0, texel.y));
                float4 s = tex2D(_PrevFluid, i.uv - float2(0.0, texel.y));
                float4 e = tex2D(_PrevFluid, i.uv + float2(texel.x, 0.0));
                float4 w = tex2D(_PrevFluid, i.uv - float2(texel.x, 0.0));

                // 1. Shallow-water velocity field (only moves where standing water exists)
                float2 gradH = float2(e.r - w.r, n.r - s.r) * 0.5;
                float swirlAngle = (fiberMap - 0.5) * 6.2831 + _Time.y * 0.6;
                float2 swirl = float2(cos(swirlAngle), sin(swirlAngle)) * 0.0014 * smoothstep(0.15, 0.8, c.r);
                float2 vel = (-gradH * 0.024 + swirl) * fiberPerm;

                float2 backUV = clamp(i.uv - vel * dt * 50.0, 0.0, 1.0);
                float4 adv = tex2D(_PrevFluid, backUV);

                float hWater = lerp(c.r, adv.r, 0.55);
                float sat    = c.g;
                float sens   = lerp(c.b, adv.b, saturate(hWater * 0.75));
                float wash   = max(c.a, adv.a * 0.998);

                // 2. Finite-volume water spread + evaporation/dry-fiber absorption
                // Dry cotton fibers absorb thin films, halting spread unless you keep pouring!
                float lapWater = (n.r + s.r + e.r + w.r - 4.0 * hWater);
                float dryResistance = lerp(0.32, 0.06, sat);
                hWater = max(0.0, hWater + lapWater * 0.14 * fiberPerm - dryResistance * dt);

                float edgeDist = min(min(i.uv.x, 1.0 - i.uv.x), min(i.uv.y, 1.0 - i.uv.y));
                if (edgeDist < 0.015)
                {
                    hWater *= (1.0 - 2.5 * dt);
                    if (wash > 0.2)
                        sens *= (1.0 - 3.5 * dt);
                }

                float absorb = min(hWater, max(0.0, 1.0 - sat) * 2.6 * dt);
                hWater -= absorb;
                sat = saturate(sat + absorb - 0.05 * dt);

                // 3. Moisture-Gated Wash Front: ONLY spreads where hWater > 0.26 (drenched)
                float maxNeighborWash = max(max(n.a, s.a), max(e.a, w.a));
                float avgNeighborWash = 0.25 * (n.a + s.a + e.a + w.a);
                float wetnessGate = smoothstep(0.24, 0.65, hWater);

                if (wetnessGate > 0.01)
                {
                    float creep = saturate((2.8 + 4.2 * fiberMap) * wetnessGate * dt);
                    float targetWash = lerp(avgNeighborWash, maxNeighborWash, 0.55);
                    wash = max(wash, lerp(wash, targetWash, creep));

                    // Dissolve yellow sensitizer proportional to active standing water
                    float dissolveRate = wash * wetnessGate * (0.75 + 0.55 * fiberMap) * dt;
                    sens = max(0.0, sens - dissolveRate);
                }

                // 4. Submerge Tray Wash Mode (key 3 or dropped inside WaterWashTray)
                if (abs(_BrushMode - 3.0) < 0.2)
                {
                    float wave = 0.03 + 0.025 * sin(_Time.y * 4.0 + i.uv.x * 18.0) * cos(_Time.y * 3.2 + i.uv.y * 18.0);
                    if (edgeDist < wave)
                    {
                        hWater = min(1.5, hWater + 4.5 * dt);
                        wash   = saturate(wash + 2.2 * dt);
                    }
                    else
                    {
                        hWater = min(1.35, hWater + 1.4 * dt * fiberPerm);
                        wash   = max(wash, lerp(wash, maxNeighborWash, saturate(9.0 * dt * fiberPerm)));
                    }
                    sat = saturate(sat + 2.0 * dt);
                    sens = max(0.0, sens - wash * (0.95 + 0.45 * fiberMap) * dt);
                }

                // 5. Interactive Brush (0), Foam Roller (1), or Water Pour (2)
                if (_BrushMode > 0.5 && _BrushMode < 2.5 && _BrushPos.x >= 0.0)
                {
                    float dist = sdSegment(i.uv, _PrevBrushPos, _BrushPos);
                    float mask = 0.0;

                    if (_ApplicatorShape < 0.5)
                    {
                        float bristle = 0.78 + 0.22 * sin((i.uv.x * 320.0 + i.uv.y * 180.0) + fineGrain * 4.0);
                        mask = smoothstep(_BrushRadius, _BrushRadius * 0.10, dist) * bristle;
                    }
                    else
                    {
                        float2 deltaUV = i.uv - _BrushPos;
                        float2 strokeDir = _BrushPos - _PrevBrushPos;
                        float len = length(strokeDir);
                        float2 tangent = (len > 1e-4) ? (strokeDir / len) : float2(0.0, 1.0);
                        float2 normal  = float2(-tangent.y, tangent.x);

                        float alongRoller  = abs(dot(deltaUV, normal));
                        float acrossRoller = sdSegment(i.uv, _PrevBrushPos, _BrushPos);
                        float widthMask = smoothstep(_BrushRadius * 2.4, _BrushRadius * 1.9, alongRoller);
                        float rollMask  = smoothstep(_BrushRadius * 0.65, _BrushRadius * 0.15, acrossRoller);
                        mask = widthMask * rollMask * (0.94 + 0.06 * fineGrain);
                    }

                    if (abs(_BrushMode - 1.0) < 0.2)
                    {
                        float deposit = mask * _BrushStrength * dt * 6.5;
                        // Coating deposits sensitizer without flooding the sheet with wash water
                        hWater = min(0.22, hWater + deposit * 0.12);
                        sat    = min(1.0, sat + deposit * 0.6);
                        sens   = min(1.35, sens + deposit);
                        wash   = max(0.0, wash - deposit * 0.9);
                    }
                    else if (abs(_BrushMode - 2.0) < 0.2)
                    {
                        // Pouring water builds standing water locally; hold longer to drench and spread further
                        float waterIn = mask * _BrushStrength * dt * 4.8;
                        hWater = min(1.5, hWater + waterIn);
                        sat    = saturate(sat + waterIn * 0.8);
                        wash   = saturate(wash + waterIn * 0.65);
                        sens   = max(0.0, sens - waterIn * 0.85);
                    }
                }

                return float4(hWater, sat, sens, wash);
            }
            ENDCG
        }
    }
}