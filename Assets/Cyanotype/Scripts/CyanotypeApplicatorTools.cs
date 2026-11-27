using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class CyanotypeApplicatorTools : MonoBehaviour
{
    public enum ApplicatorType
    {
        HakeBrush = 0,
        GlassCoatingRod = 1
    }

    [Header("References")]
    public CyanotypeFluidController fluidController;
    public Transform paperTransform;
    public ApplicatorType activeApplicator = ApplicatorType.HakeBrush;

    [Header("Workbench Rest Pose")]
    public Vector3 standOrigin = new Vector3(-0.38f, 0.815f, -1.05f);

    private GameObject _brushRoot;
    private GameObject _rodRoot;
    private Transform _rodShaft;
    private Camera _cam;
    private Vector3 _lastHitWorld;
    private bool _suppressedByFirstPersonRig;

    void Start()
    {
        if (fluidController == null)
            fluidController = GetComponent<CyanotypeFluidController>() ?? FindAnyObjectByType<CyanotypeFluidController>();
        if (paperTransform == null && fluidController != null)
            paperTransform = fluidController.paperTransform;

        _cam = Camera.main;
        Build3DTools();
        SelectApplicator(activeApplicator);
    }

    private void OnDestroy()
    {
        if (_brushRoot != null) Destroy(_brushRoot);
        if (_rodRoot != null) Destroy(_rodRoot);
    }

    private void Build3DTools()
    {
        Shader lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        Material woodMat    = new Material(lit) { color = new Color(0.55f, 0.36f, 0.20f) };
        Material brassMat   = new Material(lit) { color = new Color(0.78f, 0.65f, 0.28f) };
        Material bristleMat = new Material(lit) { color = new Color(0.86f, 0.84f, 0.18f) };
        Material glassMat   = new Material(lit) { color = new Color(0.76f, 0.88f, 0.92f) };

        // 1900s Hake Brush
        _brushRoot = new GameObject("Tool_HakeBrush_1900s");
        CreatePart(_brushRoot.transform, PrimitiveType.Cube, "Handle",   new Vector3(0f, 0.11f, 0f),  new Vector3(0.014f, 0.14f, 0.008f), woodMat);
        CreatePart(_brushRoot.transform, PrimitiveType.Cube, "Ferrule",  new Vector3(0f, 0.035f, 0f), new Vector3(0.048f, 0.022f, 0.011f), brassMat);
        CreatePart(_brushRoot.transform, PrimitiveType.Cube, "Bristles", new Vector3(0f, 0.012f, 0f), new Vector3(0.052f, 0.026f, 0.009f), bristleMat);

        // 1900s Glass Coating Rod (puddle pusher)
        _rodRoot = new GameObject("Tool_GlassCoatingRod_1900s");
        CreatePart(_rodRoot.transform, PrimitiveType.Cylinder, "GlassHandle", new Vector3(0f, 0.09f, -0.065f), new Vector3(0.008f, 0.065f, 0.008f), glassMat, Quaternion.Euler(55f, 0f, 0f));
        GameObject rod = CreatePart(_rodRoot.transform, PrimitiveType.Cylinder, "CoatingRod", new Vector3(0f, 0.007f, 0f), new Vector3(0.012f, 0.075f, 0.012f), glassMat, Quaternion.Euler(0f, 0f, 90f));
        _rodShaft = rod.transform;
    }

    private static GameObject CreatePart(Transform parent, PrimitiveType type, string name, Vector3 localPos, Vector3 scale, Material mat, Quaternion? rot = null)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = rot ?? Quaternion.identity;
        go.transform.localScale = scale;
        Destroy(go.GetComponent<Collider>());
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    void Update()
    {
        // Use B key to toggle Hake Brush / Glass Coating Rod without colliding with tool keys 0-6
        bool toggleApplicator = false;
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb != null)
            toggleApplicator = kb.bKey.wasPressedThisFrame;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        toggleApplicator |= Input.GetKeyDown(KeyCode.B);
#endif
        if (toggleApplicator)
        {
            ToggleApplicator();
        }
    }

    // Run in LateUpdate after camera movement so the 3D brush/rod never jitters
    void LateUpdate()
    {
        if (_brushRoot == null || _rodRoot == null) return;
        if (_cam == null || !_cam.isActiveAndEnabled) _cam = Camera.main;

        // Suppress procedural fallback meshes if a 1st-person workbench interaction rig is active
        var fpRig = FindAnyObjectByType<Cyanotype.Gameplay.CyanotypePlayerInteractionRig>();
        bool shouldSuppress = fpRig != null && fpRig.isActiveAndEnabled;
        if (shouldSuppress != _suppressedByFirstPersonRig)
        {
            _suppressedByFirstPersonRig = shouldSuppress;
            _brushRoot.SetActive(!shouldSuppress);
            _rodRoot.SetActive(!shouldSuppress);
        }
        if (_suppressedByFirstPersonRig) return;

        bool coatingActive = fluidController != null && fluidController.currentTool == CyanotypeFluidController.ToolMode.SensitizerBrush;
        Transform activeTransform = (activeApplicator == ApplicatorType.HakeBrush) ? _brushRoot.transform : _rodRoot.transform;
        Transform idleTransform   = (activeApplicator == ApplicatorType.HakeBrush) ? _rodRoot.transform : _brushRoot.transform;

        idleTransform.position = standOrigin + new Vector3(0.09f, 0f, 0f);
        idleTransform.rotation = Quaternion.Euler(0f, 20f, 88f);

        if (!coatingActive || _cam == null || paperTransform == null)
        {
            activeTransform.position = standOrigin;
            activeTransform.rotation = Quaternion.Euler(0f, -15f, 88f);
            return;
        }

        Vector2 screenPos = Cursor.lockState == CursorLockMode.Locked
            ? new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)
            : GetMousePos();

        // Raycast against the workbench horizontal plane at paper height so the tool never teleports at paper edges
        Plane tablePlane = new Plane(Vector3.up, paperTransform.position);
        Ray ray = _cam.ScreenPointToRay(screenPos);
        if (tablePlane.Raycast(ray, out float enter))
        {
            Vector3 planePt = ray.GetPoint(enter);
            planePt.x = Mathf.Clamp(planePt.x, paperTransform.position.x - 0.75f, paperTransform.position.x + 0.75f);
            planePt.z = Mathf.Clamp(planePt.z, paperTransform.position.z - 0.75f, paperTransform.position.z + 0.75f);

            bool pressed = IsPrimaryHeld() && !CyanotypeStudioHUD.IsPointerOverHUD;
            float hoverOffset = pressed ? 0.002f : 0.035f;
            activeTransform.position = planePt + Vector3.up * hoverOffset;

            if (activeApplicator == ApplicatorType.HakeBrush)
            {
                activeTransform.rotation = Quaternion.Euler(18f, 15f, -12f);
            }
            else
            {
                activeTransform.rotation = Quaternion.identity;
                float moveDist = Vector3.Distance(planePt, _lastHitWorld);
                if (pressed && _rodShaft != null && moveDist > 0.0001f)
                    _rodShaft.Rotate(Vector3.up, moveDist * 1800f, Space.Self);
            }
            _lastHitWorld = planePt;
        }
    }

    public void ToggleApplicator()
    {
        SelectApplicator(activeApplicator == ApplicatorType.HakeBrush
            ? ApplicatorType.GlassCoatingRod
            : ApplicatorType.HakeBrush);
    }

    public void SelectApplicator(ApplicatorType type)
    {
        activeApplicator = type;
        if (fluidController != null)
        {
            fluidController.applicatorShape = (type == ApplicatorType.GlassCoatingRod)
                ? CyanotypeFluidController.ApplicatorShape.GlassCoatingRod
                : CyanotypeFluidController.ApplicatorShape.HakeBrush;
            fluidController.brushRadius = (type == ApplicatorType.GlassCoatingRod) ? 0.07f : 0.055f;

            if (fluidController.fluidMaterial != null)
                fluidController.fluidMaterial.SetFloat("_ApplicatorShape", (float)type);
        }
    }

    private static bool IsPrimaryHeld()
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
