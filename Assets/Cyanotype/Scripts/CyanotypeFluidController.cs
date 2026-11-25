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
        SubmergeTrayWash = 3
    }

    [Header("References")]
    public CyanotypeEnvironmentRig rig;
    public Material fluidMaterial;
    public Transform paperTransform;

    [Header("Fluid Tool Settings")]
    public ToolMode currentTool = ToolMode.SensitizerBrush;
    [Range(0.01f, 0.2f)] public float brushRadius = 0.05f;
    [Range(0.2f, 3.0f)] public float flowStrength = 1.2f;
    public bool startPaperUncoated = true;

    private RenderTexture _fluidA;
    private RenderTexture _fluidB;
    private Vector2 _prevBrushUV = new Vector2(-1f, -1f);
    private Camera _cam;


    void Start()
    {
		if (GetComponent<CyanotypeStudioHUD>() == null)
			gameObject.AddComponent<CyanotypeStudioHUD>();
        Shader fluidShader = Shader.Find("Cyanotype/FluidTransport");
        if (fluidShader != null && (fluidMaterial == null || fluidMaterial.shader != fluidShader))
        {
            fluidMaterial = new Material(fluidShader);
        }

        _cam = Camera.main;
        _fluidA = CreateFluidRT(512, 512);
        _fluidB = CreateFluidRT(512, 512);
        ResetFluid();

        if (paperTransform != null)
        {
            // Keep MeshCollider on CyanotypePaper so hit.textureCoord works across the whole sheet
            if (paperTransform.GetComponent<MeshCollider>() == null)
            {
                var boxCol = paperTransform.GetComponent<BoxCollider>();
                if (boxCol != null) Destroy(boxCol);
                paperTransform.gameObject.AddComponent<MeshCollider>();
            }

            // Use a child trigger collider + Rigidbody for WaterWashTray detection without interfering with MeshCollider
            // Use a child trigger collider on layer 2 (Ignore Raycast) so UV shadow rays never hit it
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
        _prevBrushUV = new Vector2(-1f, -1f);
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
        if (_cam == null || !_cam.isActiveAndEnabled) _cam = Camera.main;

        HandleToolHotkeys();
        StepFluidSimulation();
    }

    private void HandleToolHotkeys()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.digit1Key.wasPressedThisFrame) { currentTool = ToolMode.SensitizerBrush; Debug.Log("[Cyanotype] Tool: Sensitizer Brush"); }
            if (kb.digit2Key.wasPressedThisFrame) { currentTool = ToolMode.WaterPourWash;   Debug.Log("[Cyanotype] Tool: Water Pour Wash"); }
            if (kb.digit3Key.wasPressedThisFrame) { currentTool = ToolMode.SubmergeTrayWash;Debug.Log("[Cyanotype] Tool: Submerge in Water Tray"); }
            if (kb.digit0Key.wasPressedThisFrame) { currentTool = ToolMode.None;            Debug.Log("[Cyanotype] Tool: Prop Placement Only"); }
            if (kb.backspaceKey.wasPressedThisFrame) ResetFluid();
        }
#else
        if (Input.GetKeyDown(KeyCode.Alpha1)) currentTool = ToolMode.SensitizerBrush;
        if (Input.GetKeyDown(KeyCode.Alpha2)) currentTool = ToolMode.WaterPourWash;
        if (Input.GetKeyDown(KeyCode.Alpha3)) currentTool = ToolMode.SubmergeTrayWash;
        if (Input.GetKeyDown(KeyCode.Alpha0)) currentTool = ToolMode.None;
        if (Input.GetKeyDown(KeyCode.Backspace)) ResetFluid();
#endif
    }

    private void StepFluidSimulation()
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
            if (Physics.Raycast(ray, out RaycastHit hit, 50f))
            {
                if (hit.transform == paperTransform)
                {
                    // Convert world hit point into Quad local space [-0.5, 0.5] -> UV [0, 1]
                    Vector3 localHit = paperTransform.InverseTransformPoint(hit.point);
                    brushUV = new Vector2(
                        Mathf.Clamp01(localHit.x + 0.5f),
                        Mathf.Clamp01(localHit.y + 0.5f)
                    );
                    activeMode = (float)currentTool;
                }
            }
        }

        if (brushUV.x < 0f)
            _prevBrushUV = new Vector2(-1f, -1f);
        else if (_prevBrushUV.x < 0f)
            _prevBrushUV = brushUV;

        fluidMaterial.SetTexture("_PrevFluid", _fluidA);
        fluidMaterial.SetVector("_BrushPos", brushUV);
        fluidMaterial.SetVector("_PrevBrushPos", _prevBrushUV);
        fluidMaterial.SetFloat("_BrushRadius", brushRadius);
        fluidMaterial.SetFloat("_BrushStrength", flowStrength);
        fluidMaterial.SetFloat("_BrushMode", activeMode);
        fluidMaterial.SetFloat("_DeltaTime", Time.deltaTime);

        Graphics.Blit(_fluidA, _fluidB, fluidMaterial);
        (_fluidA, _fluidB) = (_fluidB, _fluidA);

        if (brushUV.x >= 0f)
            _prevBrushUV = brushUV;

        if (rig.simMaterial != null)
            rig.simMaterial.SetTexture("_FluidTex", _fluidA);
        if (rig.displayMaterial != null)
            rig.displayMaterial.SetTexture("_FluidTex", _fluidA);
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