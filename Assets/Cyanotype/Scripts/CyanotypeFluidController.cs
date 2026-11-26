using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class CyanotypeFluidController : MonoBehaviour
{
    public enum ToolMode
    {
        None = 0,
        SensitizerBrush = 1,
        WaterPourWash = 2,
        SubmergeTrayWash = 3,
        AlkaliBath = 4,
        TanninBath = 5,
        PeroxideSprayBrush = 6
    }

    public enum BathTrayMode
    {
        None = 0,
        Alkali = 1,
        Tannin = 2,
        Peroxide = 3
    }

    [Header("References")]

    public CyanotypeEnvironmentRig rig;
    public Material fluidMaterial;
    public Material bathMaterial;
    public Transform paperTransform;
	public bool hasBeenBleached { get; private set; }
    [Header("Fluid & Bath Tool Settings")]
    public ToolMode currentTool = ToolMode.SensitizerBrush;
    public BathTrayMode activeBathTray = BathTrayMode.None;
    [Range(0.01f, 0.2f)] public float brushRadius = 0.05f;
    [Range(0.06f, 0.35f)] public float sprayRadius = 0.16f;
    [Range(0.2f, 3.0f)] public float flowStrength = 1.2f;
    public bool startPaperUncoated = true;

    private RenderTexture _fluidA;
    private RenderTexture _fluidB;
    private RenderTexture _bathA;
    private RenderTexture _bathB;
    private Vector2 _prevBrushUV = new Vector2(-1f, -1f);
    private Camera _cam;

    public RenderTexture FluidRT => _fluidA;
    public RenderTexture BathRT  => _bathA;

    void Start()
    {
        if (GetComponent<CyanotypeStudioHUD>() == null)
            gameObject.AddComponent<CyanotypeStudioHUD>();

        Shader fluidShader = Shader.Find("Cyanotype/FluidTransport");
        if (fluidShader != null && (fluidMaterial == null || fluidMaterial.shader != fluidShader))
            fluidMaterial = new Material(fluidShader);

        Shader bathShader = Shader.Find("Cyanotype/BathTransport");
        if (bathShader != null && (bathMaterial == null || bathMaterial.shader != bathShader))
            bathMaterial = new Material(bathShader);

        _cam    = Camera.main;
        _fluidA = CreateFluidRT(512, 512);
        _fluidB = CreateFluidRT(512, 512);
        _bathA  = CreateFluidRT(512, 512);
        _bathB  = CreateFluidRT(512, 512);
        ResetFluid();

        if (paperTransform != null)
        {
            if (paperTransform.GetComponent<MeshCollider>() == null)
            {
                var boxCol = paperTransform.GetComponent<BoxCollider>();
                if (boxCol != null) Destroy(boxCol);
                paperTransform.gameObject.AddComponent<MeshCollider>();
            }

            Transform triggerChild = paperTransform.Find("WaterTriggerSensor");
            if (triggerChild == null)
            {
                GameObject sensor = new GameObject("WaterTriggerSensor");
                sensor.layer = 2; // Ignore Raycast
                sensor.transform.SetParent(paperTransform, false);
                var box = sensor.AddComponent<BoxCollider>();
                box.size = new Vector3(1f, 1f, 0.06f);
                box.isTrigger = true;
            }
            else
            {
                triggerChild.gameObject.layer = 2;
            }

            var rb = paperTransform.GetComponent<Rigidbody>();
            if (rb == null) rb = paperTransform.gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            var dragPaper = paperTransform.GetComponent<DraggableCyanotypePaper>();
            if (dragPaper == null) dragPaper = paperTransform.gameObject.AddComponent<DraggableCyanotypePaper>();
            dragPaper.fluidController = this;
        }

        if (GetComponent<CyanotypeApplicatorTools>() == null)
            gameObject.AddComponent<CyanotypeApplicatorTools>();

        if (startPaperUncoated && rig != null)
        {
            rig.coatingThickness = 0.0f;
            rig.ClearChemicalState();
        }
    }

    private RenderTexture CreateFluidRT(int w, int h)
    {
        var rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGBFloat);
        rt.filterMode = FilterMode.Bilinear;
        rt.wrapMode = TextureWrapMode.Clamp;
        rt.Create();
        return rt;
    }

    public void ResetFluid()
    {
        ClearRT(_fluidA, Color.clear);
        ClearRT(_fluidB, Color.clear);
        ClearRT(_bathA,  Color.clear);
        ClearRT(_bathB,  Color.clear);
        activeBathTray  = BathTrayMode.None;
        hasBeenBleached = false;
        _prevBrushUV    = new Vector2(-1f, -1f);
    }
	
    private void ClearRT(RenderTexture rt, Color col)
    {
        if (rt == null) return;
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        GL.Clear(true, true, col);
        RenderTexture.active = prev;
    }

    void Update()
    {
        if (fluidMaterial == null || paperTransform == null || rig == null) return;
        if (_cam == null || !_cam.isActiveAndEnabled) _cam = FindActiveCamera();

        HandleToolHotkeys();
        StepFluidAndBathSimulation();
    }

    private static Camera FindActiveCamera()
    {
        if (Camera.main != null && Camera.main.isActiveAndEnabled)
            return Camera.main;

        foreach (var cam in Camera.allCameras)
        {
            if (cam.isActiveAndEnabled && cam.targetTexture == null)
                return cam;
        }
        return null;
    }

    

    private void HandleToolHotkeys()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.digit1Key.wasPressedThisFrame) currentTool = ToolMode.SensitizerBrush;
            if (kb.digit2Key.wasPressedThisFrame) currentTool = ToolMode.WaterPourWash;
            if (kb.digit3Key.wasPressedThisFrame) currentTool = ToolMode.SubmergeTrayWash;
            if (kb.digit4Key.wasPressedThisFrame) currentTool = ToolMode.AlkaliBath;
            if (kb.digit5Key.wasPressedThisFrame) currentTool = ToolMode.TanninBath;
            if (kb.digit6Key.wasPressedThisFrame) currentTool = ToolMode.PeroxideSprayBrush;
            if (kb.digit0Key.wasPressedThisFrame) currentTool = ToolMode.None;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.Alpha1)) currentTool = ToolMode.SensitizerBrush;
        if (Input.GetKeyDown(KeyCode.Alpha2)) currentTool = ToolMode.WaterPourWash;
        if (Input.GetKeyDown(KeyCode.Alpha3)) currentTool = ToolMode.SubmergeTrayWash;
        if (Input.GetKeyDown(KeyCode.Alpha4)) currentTool = ToolMode.AlkaliBath;
        if (Input.GetKeyDown(KeyCode.Alpha5)) currentTool = ToolMode.TanninBath;
        if (Input.GetKeyDown(KeyCode.Alpha6)) currentTool = ToolMode.PeroxideSprayBrush;
        if (Input.GetKeyDown(KeyCode.Alpha0)) currentTool = ToolMode.None;
#endif
    }

    private void StepFluidAndBathSimulation()
    {
        Vector2 brushUV = new Vector2(-1f, -1f);
        float activeMode = 0f;

        if (currentTool == ToolMode.SubmergeTrayWash)
        {
            activeMode = 3f;
        }
        else if (currentTool != ToolMode.None && IsPrimaryButtonHeld() && !CyanotypeStudioHUD.IsPointerOverHUD && _cam != null)
        {
            Vector2 screenPos = Cursor.lockState == CursorLockMode.Locked
                ? new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)
                : GetMousePos();

            Ray ray = _cam.ScreenPointToRay(screenPos);
            // RaycastAll passes through stencils/props above the sheet to hit CyanotypePaper directly
            RaycastHit[] hits = Physics.RaycastAll(ray, 50f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i].transform == paperTransform)
                {
                    Vector3 localHit = paperTransform.InverseTransformPoint(hits[i].point);
                    brushUV = new Vector2(
                        Mathf.Clamp01(localHit.x + 0.5f),
                        Mathf.Clamp01(localHit.y + 0.5f)
                    );
                    activeMode = (float)currentTool;

                    if (currentTool == ToolMode.SensitizerBrush && rig != null && rig.recipe != null)
                    {
                        rig.recipe.locked = true;
                        hasBeenBleached   = false;
                    }
                    else if (currentTool == ToolMode.AlkaliBath)
                    {
                        UnlockForRecoat();
                    }
                    break;
                }
            }
        }

        if (brushUV.x < 0f)
            _prevBrushUV = new Vector2(-1f, -1f);
        else if (_prevBrushUV.x < 0f)
            _prevBrushUV = brushUV;

        // 1. Step Fluid Transport
        float fluidMode = (activeMode <= 3.5f) ? activeMode : 0f;
        fluidMaterial.SetTexture("_PrevFluid", _fluidA);
        fluidMaterial.SetVector("_BrushPos", brushUV);
        fluidMaterial.SetVector("_PrevBrushPos", _prevBrushUV);
        fluidMaterial.SetFloat("_BrushRadius", brushRadius);
        fluidMaterial.SetFloat("_BrushStrength", flowStrength);
        fluidMaterial.SetFloat("_BrushMode", fluidMode);
        fluidMaterial.SetFloat("_DeltaTime", Time.deltaTime);

        Graphics.Blit(_fluidA, _fluidB, fluidMaterial);
        (_fluidA, _fluidB) = (_fluidB, _fluidA);

        // 2. Step Per-Texel Bath Transport
        if (bathMaterial != null)
        {
            float bathBrushMode = (activeMode >= 3.5f) ? activeMode : 0f;
            bathMaterial.SetTexture("_PrevBath", _bathA);
            bathMaterial.SetTexture("_FluidTex", _fluidA);
            bathMaterial.SetVector("_BrushPos", brushUV);
            bathMaterial.SetVector("_PrevBrushPos", _prevBrushUV);
            bathMaterial.SetFloat("_BrushRadius", brushRadius);
            bathMaterial.SetFloat("_SprayRadius", sprayRadius);
            bathMaterial.SetFloat("_BrushStrength", flowStrength);
            bathMaterial.SetFloat("_BathMode", bathBrushMode);
            bathMaterial.SetFloat("_SubmergeBath", (float)activeBathTray);
            bathMaterial.SetFloat("_DeltaTime", Time.deltaTime);
			if (activeBathTray == BathTrayMode.Alkali)
            {
                UnlockForRecoat();
            }
            Graphics.Blit(_bathA, _bathB, bathMaterial);
            (_bathA, _bathB) = (_bathB, _bathA);
        }

        if (brushUV.x >= 0f)
            _prevBrushUV = brushUV;

        if (rig.simMaterial != null)
        {
            rig.simMaterial.SetTexture("_FluidTex", _fluidA);
            rig.simMaterial.SetTexture("_BathTex", _bathA);
        }
        if (rig.displayMaterial != null)
        {
            rig.displayMaterial.SetTexture("_FluidTex", _fluidA);
            rig.displayMaterial.SetTexture("_BathTex", _bathA);
        }
    }
	private void UnlockForRecoat()
    {
        if (!hasBeenBleached)
        {
            hasBeenBleached = true;
            if (rig != null && rig.recipe != null)
            {
                rig.recipe.Unlock();
            }
        }
    }
    private static bool IsPrimaryButtonHeld()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.isPressed;
#else
        return Input.GetMouseButton(0);
#endif
    }

    private static Vector2 GetMousePos()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
        return Input.mousePosition;
#endif
    }
}