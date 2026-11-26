using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class CyanotypeEnvironmentRig : MonoBehaviour
{
    [Header("Target Cyanotype Sheet")]
	[Tooltip("Fallback stencil mask texture applied if the stencil material resets.")]
    public Texture2D stencilTexture;
    public MeshFilter paperMeshFilter;
    public Renderer paperRenderer;
    public Renderer stencilRenderer;

    [Tooltip("Layers that cast UV shadows. If left Nothing (0), defaults to all layers except Ignore Raycast.")]
    public LayerMask shadowCasterLayers = ~0;

    [Header("Cyanotype Chemical Formulation")]
    [Tooltip("Ferric Ammonium Citrate concentration (g/100mL). Standard formula = 20.")]
    [Range(2f, 40f)] public float facConcentration = 20f;

    [Tooltip("Potassium Ferricyanide concentration (g/100mL). Standard formula = 8.")]
    [Range(1f, 25f)] public float kfcConcentration = 8f;

    [Tooltip("Base sensitizer coating weight multiplier.")]
    [Range(0.0f, 2.0f)] public float coatingThickness = 1.0f;

    [Header("Day/Night Sun Settings")]
    public Light sunLight;
    public Camera sunShadowCam;
    public float dayLengthSeconds = 120f;
    [Range(0f, 1f)] public float timeOfDay = 0.42f;
    public float peakSunUVIntensity = 0.30f;
    public float sunOrthoRadius = 3.5f;

    [Header("Open Window Passive UV (Works With Roof On)")]
    [Tooltip("Optional Transform placed at the open window pane. Leave null for uniform room skylight scatter.")]
    public Transform windowOpening;
    [Range(0f, 0.5f)] public float windowAmbientUV = 0.08f;

    [Header("Indoor UV Lamp Settings")]
    public Light uvLampLight;
    public Camera lampShadowCam;
    public bool lampIsOn = true;
    public float lampUVIntensity = 2.5f;

    [Header("Photochemical Shaders & Materials")]
    public Shader stencilDepthShader;
    public Material simMaterial;
    public Material displayMaterial;
    [Range(0.5f, 30f)] public float timeScale = 5f;

    private RenderTexture _sunShadowRT;
    private RenderTexture _lampShadowRT;
    private RenderTexture _stateA;
    private RenderTexture _stateB;
    private Texture2D _objPosTex;
    private Texture2D _objNormTex;
    private Texture2D _sunOccTex;
    private Texture2D _lampOccTex;

    private const int OCC_RES = 128;
    private bool _washed;
    private bool _pauseCycle = true;

    void Start()
    {
        Physics.queriesHitBackfaces = true;

        if (stencilDepthShader == null)
            stencilDepthShader = Shader.Find("Cyanotype/StencilShadowDepth");

        if (shadowCasterLayers.value == 0)
            shadowCasterLayers = ~(1 << 2);
        else
            shadowCasterLayers &= ~(1 << 2);

        if (uvLampLight != null)
            uvLampLight.shadows = LightShadows.None;

        _sunShadowRT  = CreateRT(2048, 2048, 16, RenderTextureFormat.RGFloat);
        _lampShadowRT = CreateRT(1024, 1024, 16, RenderTextureFormat.RGFloat);
        _stateA       = CreateRT(1024, 1024, 0,  RenderTextureFormat.ARGBFloat);
        _stateB       = CreateRT(1024, 1024, 0,  RenderTextureFormat.ARGBFloat);

        InitOcclusionMaps();
        ClearChemicalState();
        FitStencilToA4Aspect();

        if (sunShadowCam != null) ConfigureShadowCamera(sunShadowCam, _sunShadowRT, true);
        if (lampShadowCam != null) ConfigureShadowCamera(lampShadowCam, _lampShadowRT, false);

        BakeObjectSpaceGeometry();
    }
	private void OnValidate()
    {
        if (!Application.isPlaying && paperMeshFilter != null)
            FitStencilToA4Aspect();
    }
    private void InitOcclusionMaps()
    {
        if (_sunOccTex == null)
        {
            _sunOccTex = new Texture2D(OCC_RES, OCC_RES, TextureFormat.RFloat, false, true);
            _sunOccTex.wrapMode = TextureWrapMode.Clamp;
            _sunOccTex.filterMode = FilterMode.Bilinear;
        }
        if (_lampOccTex == null)
        {
            _lampOccTex = new Texture2D(OCC_RES, OCC_RES, TextureFormat.RFloat, false, true);
            _lampOccTex.wrapMode = TextureWrapMode.Clamp;
            _lampOccTex.filterMode = FilterMode.Bilinear;
        }
    }

    private RenderTexture CreateRT(int w, int h, int depth, RenderTextureFormat fmt)
    {
        var rt = new RenderTexture(w, h, depth, fmt);
        rt.filterMode = FilterMode.Bilinear;
        rt.wrapMode = TextureWrapMode.Clamp;
        rt.Create();
        return rt;
    }

    public void ClearChemicalState()
    {
        _washed = false;
        ClearRT(_stateA, new Color(0f, coatingThickness, 0f, 0f));
        ClearRT(_stateB, new Color(0f, coatingThickness, 0f, 0f));
    }

    private void ClearRT(RenderTexture rt, Color clearColor)
    {
        if (rt == null) return;
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        GL.Clear(true, true, clearColor);
        RenderTexture.active = prev;
    }

    private void FitStencilToA4Aspect()
    {
        if (paperMeshFilter == null) return;

        float aspect = 0.707f;
        Texture tex = stencilTexture;

        if (tex == null && stencilRenderer != null && stencilRenderer.sharedMaterial != null
            && stencilRenderer.sharedMaterial.HasProperty("_StencilTex"))
        {
            tex = stencilRenderer.sharedMaterial.GetTexture("_StencilTex");
        }

        if (tex != null && tex.width > 16 && tex.height > 16)
            aspect = Mathf.Clamp((float)tex.width / tex.height, 0.3f, 3.0f);

        float height = 0.89f;
        Vector3 a4Scale = new Vector3(height * aspect, height, 1f);
        paperMeshFilter.transform.localScale = a4Scale;

        Vector3 paperPos = paperMeshFilter.transform.position;
        if (paperPos.y < 0.805f)
        {
            paperPos.y = 0.806f;
            paperMeshFilter.transform.position = paperPos;
        }

        if (stencilRenderer != null)
        {
            stencilRenderer.transform.localScale = a4Scale;
            Vector3 stencilPos = stencilRenderer.transform.position;
            stencilPos.y = paperPos.y + 0.008f;
            stencilRenderer.transform.position = stencilPos;
        }
    }

    void Update()
    {
        if (paperMeshFilter == null || paperRenderer == null || simMaterial == null || displayMaterial == null)
            return;

        HandleInput();
        UpdateSunCycle();
        RenderShadowMaps();
        Update3DObjectOcclusion();
        StepPhotochemistry();
    }

    private void UpdateSunCycle()
    {
        Vector3 paperCenter = paperMeshFilter.transform.position;
        float sinElevation = 0.5f;

        if (sunLight != null)
        {
            if (!_pauseCycle && dayLengthSeconds > 0.01f)
                timeOfDay = (timeOfDay + Time.deltaTime / dayLengthSeconds) % 1f;

            float sunAngleDeg = (timeOfDay - 0.25f) * 360f;
            sunLight.transform.rotation = Quaternion.Euler(sunAngleDeg, 25f, 0f);

            if (sunShadowCam != null)
            {
                sunShadowCam.transform.position = paperCenter - sunLight.transform.forward * 10f;
                sunShadowCam.transform.rotation = sunLight.transform.rotation;
                simMaterial.SetVector("_SunPos", sunShadowCam.transform.position);
                simMaterial.SetFloat("_SunNear", sunShadowCam.nearClipPlane);
                simMaterial.SetFloat("_SunFar", sunShadowCam.farClipPlane);
            }

            sinElevation = Mathf.Max(0f, -sunLight.transform.forward.y);
            float sunUV = peakSunUVIntensity * Mathf.Pow(sinElevation, 1.8f);

            sunLight.intensity = peakSunUVIntensity > 0.001f
                ? Mathf.Lerp(0.15f, 1.6f, Mathf.Pow(sinElevation, 0.7f))
                : 0.45f;
            sunLight.color = peakSunUVIntensity > 0.001f
                ? Color.Lerp(new Color(1f, 0.55f, 0.3f), Color.white, sinElevation)
                : new Color(0.95f, 0.92f, 0.85f);

            simMaterial.SetFloat("_SunUVIntensity", sunUV);
            simMaterial.SetVector("_SunDir", sunLight.transform.forward);
            displayMaterial.SetFloat("_SunUVIntensity", sunUV);
        }
        else
        {
            simMaterial.SetFloat("_SunUVIntensity", 0f);
            displayMaterial.SetFloat("_SunUVIntensity", 0f);
        }

        float dayFactor = sunLight != null ? Mathf.Lerp(0.15f, 1.0f, sinElevation) : 1.0f;
        float activeWindowUV = windowAmbientUV * dayFactor;

        if (windowOpening != null)
        {
            Vector3 toWindow = windowOpening.position - paperCenter;
            float dist = Mathf.Max(toWindow.magnitude, 0.25f);
            activeWindowUV *= Mathf.Clamp01(1.5f / (1f + 0.5f * dist * dist));
        }

        simMaterial.SetFloat("_WindowAmbientUV", activeWindowUV);

        if (uvLampLight != null)
        {
            uvLampLight.enabled = lampIsOn;
            float activeLampUV = lampIsOn ? lampUVIntensity : 0f;
            simMaterial.SetFloat("_LampUVIntensity", activeLampUV);

            Vector3 lampPos = lampShadowCam != null ? lampShadowCam.transform.position : uvLampLight.transform.position;
            Vector3 lampDir = lampShadowCam != null ? lampShadowCam.transform.forward : uvLampLight.transform.forward;
            simMaterial.SetVector("_LampPos", lampPos);
            simMaterial.SetVector("_LampDir", lampDir);
            simMaterial.SetFloat("_LampNear", lampShadowCam != null ? lampShadowCam.nearClipPlane : 0.1f);
            simMaterial.SetFloat("_LampFar", lampShadowCam != null ? lampShadowCam.farClipPlane : 10f);

            float outerRad = uvLampLight.spotAngle * 0.5f * Mathf.Deg2Rad;
            float innerRad = outerRad * 0.75f;
            simMaterial.SetFloat("_LampCosOuter", Mathf.Cos(outerRad));
            simMaterial.SetFloat("_LampCosInner", Mathf.Cos(innerRad));
        }
    }

    private void RenderShadowMaps()
    {
        if (stencilDepthShader == null || sunShadowCam == null || lampShadowCam == null) return;
        if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null) return;

        Shader.SetGlobalVector("_LightPos", sunShadowCam.transform.position);
        Shader.SetGlobalFloat("_NearClip", sunShadowCam.nearClipPlane);
        Shader.SetGlobalFloat("_FarClip", sunShadowCam.farClipPlane);
        sunShadowCam.RenderWithShader(stencilDepthShader, "");

        Matrix4x4 sunVP = GL.GetGPUProjectionMatrix(sunShadowCam.projectionMatrix, false) * sunShadowCam.worldToCameraMatrix;
        simMaterial.SetTexture("_SunShadowMap", _sunShadowRT);
        simMaterial.SetMatrix("_SunVP", sunVP);

        if (lampIsOn)
        {
            Shader.SetGlobalVector("_LightPos", lampShadowCam.transform.position);
            Shader.SetGlobalFloat("_NearClip", lampShadowCam.nearClipPlane);
            Shader.SetGlobalFloat("_FarClip", lampShadowCam.farClipPlane);
            lampShadowCam.RenderWithShader(stencilDepthShader, "");

            Matrix4x4 lampVP = GL.GetGPUProjectionMatrix(lampShadowCam.projectionMatrix, false) * lampShadowCam.worldToCameraMatrix;
            simMaterial.SetTexture("_LampShadowMap", _lampShadowRT);
            simMaterial.SetMatrix("_LampVP", lampVP);
        }
    }

    private void Update3DObjectOcclusion()
    {
        if (paperMeshFilter == null || paperMeshFilter.sharedMesh == null) return;
        if (_sunOccTex == null || _lampOccTex == null) InitOcclusionMaps();

        // Temporarily disable paper collider so rays never hit the paper itself
        Collider paperCollider = paperMeshFilter.GetComponent<Collider>();
        bool wasPaperColEnabled = paperCollider != null && paperCollider.enabled;
        if (paperCollider != null) paperCollider.enabled = false;

        int total = OCC_RES * OCC_RES;
        var sunCmds   = new NativeArray<RaycastCommand>(total, Allocator.TempJob);
        var sunHits   = new NativeArray<RaycastHit>(total, Allocator.TempJob);
        var lampCmds  = new NativeArray<RaycastCommand>(total, Allocator.TempJob);
        var lampHits  = new NativeArray<RaycastHit>(total, Allocator.TempJob);

        Matrix4x4 l2w = paperMeshFilter.transform.localToWorldMatrix;
        Bounds b = paperMeshFilter.sharedMesh.bounds;
        bool isPlaneXZ = b.size.z > b.size.y;

        Vector3 localNormal = isPlaneXZ ? Vector3.up : Vector3.back;
        Vector3 worldNormal = paperMeshFilter.transform.TransformDirection(localNormal).normalized;
        if (Vector3.Dot(worldNormal, Vector3.up) < 0f) worldNormal = -worldNormal;

        Vector3 toSun = sunLight != null ? -sunLight.transform.forward.normalized : Vector3.up;
        if (toSun.y < 0.15f) toSun = Vector3.up;

        bool lampIsDirectional = uvLampLight != null && uvLampLight.type == LightType.Directional;
        Vector3 lampPos = uvLampLight != null
            ? uvLampLight.transform.position
            : (lampShadowCam != null ? lampShadowCam.transform.position : paperMeshFilter.transform.position + Vector3.up * 0.55f);
        Vector3 toLampDir = uvLampLight != null ? -uvLampLight.transform.forward.normalized : Vector3.up;

        // Short 0.42m cast window above the paper: hits props/stencils on the table, NEVER hits the roof (Y=2.6m) or lamp shade!
        const float castHeight = 0.42f;
        QueryParameters queryParams = new QueryParameters(shadowCasterLayers, false, QueryTriggerInteraction.Ignore, false);

        for (int y = 0; y < OCC_RES; y++)
        {
            float v01 = (y + 0.5f) / OCC_RES;
            for (int x = 0; x < OCC_RES; x++)
            {
                float u01 = (x + 0.5f) / OCC_RES;
                int idx = y * OCC_RES + x;

                Vector3 localPt = isPlaneXZ
                    ? new Vector3(Mathf.Lerp(b.min.x, b.max.x, u01), 0f, Mathf.Lerp(b.min.z, b.max.z, v01))
                    : new Vector3(Mathf.Lerp(b.min.x, b.max.x, u01), Mathf.Lerp(b.min.y, b.max.y, v01), 0f);

                Vector3 targetPt = l2w.MultiplyPoint3x4(localPt) + worldNormal * 0.0005f;

                // 1. Sun / Skylight Ray: start 0.42m above the paper along sun vector
                Vector3 sunOrigin = targetPt + toSun * castHeight;
                sunCmds[idx] = new RaycastCommand(sunOrigin, -toSun, queryParams, castHeight);

                // 2. UV Lamp Ray: start 0.42m toward the lamp (below the lamp housing mesh!)
                if (lampIsDirectional)
                {
                    Vector3 lampOrigin = targetPt + toLampDir * castHeight;
                    lampCmds[idx] = new RaycastCommand(lampOrigin, -toLampDir, queryParams, castHeight);
                }
                else
                {
                    Vector3 toLampVec = lampPos - targetPt;
                    float fullDist = toLampVec.magnitude;
                    Vector3 dirToLamp = toLampVec / Mathf.Max(fullDist, 0.0001f);
                    float startDist = Mathf.Min(castHeight, Mathf.Max(0.05f, fullDist - 0.12f));
                    Vector3 lampOrigin = targetPt + dirToLamp * startDist;
                    lampCmds[idx] = new RaycastCommand(lampOrigin, -dirToLamp, queryParams, startDist);
                }
            }
        }

        JobHandle hSun  = RaycastCommand.ScheduleBatch(sunCmds, sunHits, 64);
        JobHandle hLamp = RaycastCommand.ScheduleBatch(lampCmds, lampHits, 64);
        JobHandle.CompleteAll(ref hSun, ref hLamp);

        if (paperCollider != null) paperCollider.enabled = wasPaperColEnabled;

        var sunData  = _sunOccTex.GetRawTextureData<float>();
        var lampData = _lampOccTex.GetRawTextureData<float>();

        Collider stencilCollider = (stencilRenderer != null) ? stencilRenderer.GetComponent<Collider>() : null;

        for (int i = 0; i < total; i++)
        {
            sunData[i]  = EvaluateHitTransmittance(sunHits[i].collider, stencilCollider, paperCollider);
            lampData[i] = EvaluateHitTransmittance(lampHits[i].collider, stencilCollider, paperCollider);
        }

        _sunOccTex.Apply(false, false);
        _lampOccTex.Apply(false, false);

        sunCmds.Dispose();
        sunHits.Dispose();
        lampCmds.Dispose();
        lampHits.Dispose();

        simMaterial.SetTexture("_SunOccTex", _sunOccTex);
        simMaterial.SetTexture("_LampOccTex", _lampOccTex);
    }

    private float EvaluateHitTransmittance(Collider hitCol, Collider stencilCol, Collider paperCol)
    {
        if (hitCol == null || hitCol == stencilCol || hitCol == paperCol)
            return 1.0f;

        if (hitCol.TryGetComponent<UVTransmittanceProp>(out var transProp))
            return Mathf.Clamp01(transProp.uvTransmittance);

        return 0.0f;
    }

   private void StepPhotochemistry()
    {
        bool isURP = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null;
        simMaterial.SetFloat("_UseShadowMaps", isURP ? 0f : 1f);

        // Sync custom PNG/texture stencil prop if present
        CyanotypeStencilProp activeStencil = null;
        foreach (var st in FindObjectsByType<CyanotypeStencilProp>(FindObjectsSortMode.None))
        {
            if (st.isActiveAndEnabled && st.stencilMaskTexture != null)
            {
                activeStencil = st;
                break;
            }
        }

        if (activeStencil != null && activeStencil.TryGetComponent<Renderer>(out var stRend))
        {
            Transform paperT = paperMeshFilter.transform;
            Bounds b = stRend.bounds;
            Vector3 localMin = paperT.InverseTransformPoint(b.min);
            Vector3 localMax = paperT.InverseTransformPoint(b.max);

            Vector4 uvBounds = new Vector4(
                Mathf.Min(localMin.x, localMax.x) + 0.5f,
                Mathf.Min(localMin.y, localMax.y) + 0.5f,
                Mathf.Max(localMin.x, localMax.x) + 0.5f,
                Mathf.Max(localMin.y, localMax.y) + 0.5f
            );

            simMaterial.SetTexture("_StencilMaskTex", activeStencil.stencilMaskTexture);
            simMaterial.SetVector("_StencilBounds", uvBounds);
            simMaterial.SetFloat("_StencilOpacity", activeStencil.uvBlockingOpacity);
            simMaterial.SetFloat("_StencilInvert", activeStencil.invertMask ? 1f : 0f);
        }
        else
        {
            simMaterial.SetFloat("_StencilOpacity", 0f);
        }

        if (stencilRenderer != null && stencilRenderer.sharedMaterial != null)
        {
            Material mat = stencilRenderer.sharedMaterial;

            if (!mat.HasProperty("_StencilTex") && stencilDepthShader != null)
            {
                mat = new Material(stencilDepthShader) { name = "M_CyanotypeStencil_Runtime" };
                if (stencilTexture != null)
                    mat.SetTexture("_StencilTex", stencilTexture);
                mat.SetColor("_FilmTint", new Color(0.15f, 0.35f, 0.85f, 0.85f));
                stencilRenderer.sharedMaterial = mat;
            }
            else if (stencilTexture != null && mat.GetTexture("_StencilTex") == null)
            {
                mat.SetTexture("_StencilTex", stencilTexture);
            }

            if (mat.HasProperty("_StencilTex"))
            {
                simMaterial.SetFloat("_StencilActive", 1f);
                simMaterial.SetMatrix("_StencilWorldToLocal", stencilRenderer.transform.worldToLocalMatrix);
                simMaterial.SetTexture("_StencilTex", mat.GetTexture("_StencilTex"));
                simMaterial.SetFloat("_TauClear", mat.HasProperty("_TauClear") ? mat.GetFloat("_TauClear") : 0.92f);
                simMaterial.SetFloat("_InkDensity", mat.HasProperty("_InkDensity") ? mat.GetFloat("_InkDensity") : 2.40f);
            }
            else
            {
                simMaterial.SetFloat("_StencilActive", 0f);
            }
        }
        else
        {
            simMaterial.SetFloat("_StencilActive", 0f);
        }

        displayMaterial.SetFloat("_ConcFAC", facConcentration);
        displayMaterial.SetFloat("_ConcKFC", kfcConcentration);
        displayMaterial.SetFloat("_CoatingWeight", coatingThickness);

        simMaterial.SetMatrix("_LocalToWorld", paperMeshFilter.transform.localToWorldMatrix);
        simMaterial.SetFloat("_ShadowBias", 0.0005f);
        simMaterial.SetFloat("_TimeScale", timeScale);
        simMaterial.SetFloat("_DeltaTime", Time.deltaTime);
        simMaterial.SetFloat("_TriggerWash", _washed ? 1f : 0f);

        simMaterial.SetTexture("_PrevState", _stateA);
        Graphics.Blit(_stateA, _stateB, simMaterial);
        (_stateA, _stateB) = (_stateB, _stateA);

        displayMaterial.SetTexture("_ChemStateTex", _stateA);
        if (paperRenderer.sharedMaterial != displayMaterial)
            paperRenderer.sharedMaterial = displayMaterial;
    }
	
    private void ConfigureShadowCamera(Camera cam, RenderTexture target, bool ortho)
    {
        cam.enabled = false;
        cam.targetTexture = target;
        cam.cullingMask = shadowCasterLayers;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(1f, 1f, 0f, 0f);
        cam.orthographic = ortho;
        if (ortho) cam.orthographicSize = sunOrthoRadius;
    }

    private void BakeObjectSpaceGeometry()
    {
        if (paperMeshFilter == null || paperMeshFilter.sharedMesh == null) return;

        Mesh mesh = paperMeshFilter.sharedMesh;
        Vector3[] verts = mesh.vertices;
        Vector3[] norms = mesh.normals;
        Vector2[] uvs   = mesh.uv;
        int[] tris      = mesh.triangles;

        int res = 256;
        _objPosTex  = new Texture2D(res, res, TextureFormat.RGBAFloat, false, true);
        _objNormTex = new Texture2D(res, res, TextureFormat.RGBAFloat, false, true);
        _objPosTex.wrapMode  = TextureWrapMode.Clamp;
        _objNormTex.wrapMode = TextureWrapMode.Clamp;

        Color[] posPixels  = new Color[res * res];
        Color[] normPixels = new Color[res * res];

        for (int t = 0; t < tris.Length; t += 3)
        {
            int i0 = tris[t], i1 = tris[t + 1], i2 = tris[t + 2];
            Vector2 uv0 = uvs[i0] * (res - 1);
            Vector2 uv1 = uvs[i1] * (res - 1);
            Vector2 uv2 = uvs[i2] * (res - 1);

            int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(uv0.x, Mathf.Min(uv1.x, uv2.x))));
            int maxX = Mathf.Min(res - 1, Mathf.CeilToInt(Mathf.Max(uv0.x, Mathf.Max(uv1.x, uv2.x))));
            int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(uv0.y, Mathf.Min(uv1.y, uv2.y))));
            int maxY = Mathf.Min(res - 1, Mathf.CeilToInt(Mathf.Max(uv0.y, Mathf.Max(uv1.y, uv2.y))));

            float denom = (uv1.y - uv2.y) * (uv0.x - uv2.x) + (uv2.x - uv1.x) * (uv0.y - uv2.y);
            if (Mathf.Abs(denom) < 1e-6f) continue;

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float w0 = ((uv1.y - uv2.y) * (x - uv2.x) + (uv2.x - uv1.x) * (y - uv2.y)) / denom;
                    float w1 = ((uv2.y - uv0.y) * (x - uv2.x) + (uv0.x - uv2.x) * (y - uv2.y)) / denom;
                    float w2 = 1f - w0 - w1;

                    if (w0 >= -0.01f && w1 >= -0.01f && w2 >= -0.01f)
                    {
                        Vector3 p = verts[i0] * w0 + verts[i1] * w1 + verts[i2] * w2;
                        Vector3 n = (norms[i0] * w0 + norms[i1] * w1 + norms[i2] * w2).normalized;
                        int idx = y * res + x;
                        posPixels[idx]  = new Color(p.x, p.y, p.z, 1f);
                        normPixels[idx] = new Color(n.x, n.y, n.z, 1f);
                    }
                }
            }
        }

        _objPosTex.SetPixels(posPixels);
        _objPosTex.Apply(false, false);

        _objNormTex.SetPixels(normPixels);
        _objNormTex.Apply(false, false);

        simMaterial.SetTexture("_ObjPosTex", _objPosTex);
        simMaterial.SetTexture("_ObjNormTex", _objNormTex);
    }

    private void HandleInput()
    {
        bool toggleLamp = false;
        bool pauseSun   = false;
        bool washPrint  = false;
        bool resetPaper = false;

#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb != null)
        {
            toggleLamp = kb.lKey.wasPressedThisFrame;
            pauseSun   = kb.pKey.wasPressedThisFrame;
            washPrint  = kb.spaceKey.wasPressedThisFrame;
            resetPaper = kb.backspaceKey.wasPressedThisFrame;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        toggleLamp |= Input.GetKeyDown(KeyCode.L);
        pauseSun   |= Input.GetKeyDown(KeyCode.P);
        washPrint  |= Input.GetKeyDown(KeyCode.Space);
        resetPaper |= Input.GetKeyDown(KeyCode.Backspace);
#endif

        if (toggleLamp) lampIsOn = !lampIsOn;
        if (pauseSun) _pauseCycle = !_pauseCycle;
        if (washPrint)
        {
            _washed = true;
            Debug.Log("[Cyanotype] Water wash triggered! Print fixed in Prussian blue.");
        }
        if (resetPaper)
        {
            ClearChemicalState();
            Debug.Log("[Cyanotype] Fresh sensitized paper loaded.");
        }
    }
}