#ifndef CYANOTYPE_COMMON_INCLUDED
#define CYANOTYPE_COMMON_INCLUDED

// Conventions: every optical density (OD) is BASE 10 -> tau = 10^(-OD).
// Concentrations are g/100 mL. Light is in arbitrary dose units.
static const float CY_NAT2OD10 = 0.4342945; // natural-log absorbance -> base-10 OD

// Chemistry
static const float CY_MW_FAC    = 261.97; // nominal; commercial FAC varies
static const float CY_MW_KFC    = 329.24;
static const float CY_N_STD_FAC = 20.0 / 261.97; // mol/100 mL at the standard recipe
static const float CY_N_STD_KFC = 8.0 / 329.24;
static const float CY_K0        = 0.0085; // T: sets exposure speed (E63 = 18 dose units at standard)
static const float CY_GAMMA     = 1.65;   // Weibull shape (induction). NOT a reciprocity exponent.
static const float CY_PHI_K     = 1.9;    // T: phiMax = 1 - exp(-K * nLim) -> 0.85 at standard

// Optical (base-10 OD)
static const float  CY_DBASE     = 0.01; // paper fog on coated area
static const float  CY_DMAX      = 2.2;  // red-channel OD of full Prussian blue
static const float3 CY_EPS_PB    = float3(1.00, 0.65, 0.43);
static const float  CY_DOCHRE    = 0.37;
static const float3 CY_EPS_OCHRE = float3(0.16, 0.36, 0.72);
static const float  CY_DTONE     = 1.19;
static const float3 CY_EPS_TONE  = float3(0.46, 0.68, 1.00);
static const float3 CY_EPS_SENS  = float3(0.32, 0.18, 2.25) * CY_NAT2OD10;

// Baths (per second of bath time, before _BathTimeScale)
static const float CY_K_BLEACH      = 0.055;
static const float CY_NU            = 1.20;
static const float CY_DENSE_PROTECT = 3.0; // T: thin deposits bleach faster than dense ones
static const float CY_K_TONE        = 0.032;
static const float CY_KAPPA         = 1.10;

float CY_OD2Tau(float od)
{
    return exp2(-3.321928 * od);
}

float3 CY_OD2Tau(float3 od)
{
    return exp2(-3.321928 * od);
}

// x = phiMax, y = rate constant k
float2 CY_RecipeParams(float cFAC, float cKFC)
{
    float nFAC   = cFAC / CY_MW_FAC;
    float nKFC   = cKFC / CY_MW_KFC;
    float nLim   = min(nFAC, nKFC) / CY_N_STD_KFC; // 1.0 at the standard recipe
    float phiMax = 1.0 - exp(-CY_PHI_K * nLim);
    float k      = CY_K0 * sqrt(max(nFAC / CY_N_STD_FAC, 0.05));
    return float2(phiMax, k);
}

// prog = reaction extent 0..1 (drives pre-wash color). Returns Y, the density-carrying yield.
// Thick coat speeds the reaction (W), while density saturates at one full coat (saturate(W)).
float CY_Yield(float E_uv, float W, float cFAC, float cKFC, out float prog)
{
    float2 rp = CY_RecipeParams(cFAC, cKFC);
    prog = 1.0 - exp(-rp.y * max(W, 0.0) * pow(max(E_uv, 0.0), CY_GAMMA));
    return saturate(saturate(W) * rp.x * prog);
}

float CY_Yield(float E_uv, float W, float cFAC, float cKFC)
{
    float p;
    return CY_Yield(E_uv, W, cFAC, cKFC, p);
}

// Unwashed paper: chartreuse ramp -> sage-slate gray across 0.90 < prog < 1.00 (paper Eqs. 8-10)
float3 CY_PrewashOD(float prog, float W, float cFAC)
{
    float  u        = pow(min(prog / 0.90, 1.0), 1.15);
    float3 odMid    = float3(0.08 + 0.34 * u, 0.04 + 0.34 * u, 0.44 + 0.15 * u);
    float3 odGray   = float3(0.456, 0.412, 0.513);
    float  s        = saturate((prog - 0.90) / 0.10);
    float  g        = s * s * (3.0 - 2.0 * s);
    float  richness = clamp(cFAC / 20.0, 0.3, 2.0);
    return CY_NAT2OD10 * saturate(W) * lerp(odMid * richness, odGray, g);
}

// Bleach: first order in remaining blue, L^nu, slower where the deposit is dense
float CY_BleachRate(float L, float Y)
{
    return CY_K_BLEACH * pow(max(L, 0.0), CY_NU) / (1.0 + CY_DENSE_PROTECT * Y);
}

float CY_ToneRate(float T)
{
    return CY_K_TONE * pow(max(T, 0.0), CY_KAPPA);
}

// Light
float CY_Lambert(float3 n, float3 l)
{
    return max(0.0, dot(n, l));
}

float CY_LampFalloff(float d)
{
    float r = 0.5 / max(d, 0.15);
    return r * r; // 1.0 at 0.5 m
}

float CY_InkTau(float tauClear, float inkOD10, float inkMask)
{
    return tauClear * CY_OD2Tau(inkOD10 * inkMask);
}

#endif