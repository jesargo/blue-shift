using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class CyanotypeApplicatorTools : MonoBehaviour
{
    public enum ApplicatorType
    {
        HakeBrush = 0,
        FoamRoller = 1
    }

    [Header("References")]
    public CyanotypeFluidController fluidController;
    public Transform paperTransform;
    public ApplicatorType activeApplicator = ApplicatorType.HakeBrush;

    [Header("Workbench Rest Pose")]
    public Vector3 standOrigin = new Vector3(-0.38f, 0.815f, -1.05f);

    private GameObject _brushRoot;
    private GameObject _rollerRoot;
    private Transform _rollerCylinder;
    private Camera _cam;
    private Vector3 _lastHitWorld;

    void Start()
    {
        if (fluidController == null)
            fluidController = GetComponent<CyanotypeFluidController>() ?? FindFirstObjectByType<CyanotypeFluidController>();
        if (paperTransform == null && fluidController != null)
            paperTransform = fluidController.paperTransform;

        _cam = Camera.main;
        Build3DTools();
        SelectApplicator(activeApplicator);
    }

    private void Build3DTools()
    {
        Shader lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        Material woodMat    = new Material(lit) { color = new Color(0.55f, 0.36f, 0.20f) };
        Material brassMat   = new Material(lit) { color = new Color(0.78f, 0.65f, 0.28f) };
        Material bristleMat = new Material(lit) { color = new Color(0.86f, 0.84f, 0.18f) };
        Material foamMat    = new Material(lit) { color = new Color(0.84f, 0.86f, 0.16f) };
        Material wireMat    = new Material(lit) { color = new Color(0.65f, 0.67f, 0.70f) };

        _brushRoot = new GameObject("Tool_HakeBrush");
        CreatePart(_brushRoot.transform, PrimitiveType.Cube, "Handle",  new Vector3(0f, 0.11f, 0f),  new Vector3(0.014f, 0.14f, 0.008f), woodMat);
        CreatePart(_brushRoot.transform, PrimitiveType.Cube, "Ferrule", new Vector3(0f, 0.035f, 0f), new Vector3(0.048f, 0.022f, 0.011f), brassMat);
        CreatePart(_brushRoot.transform, PrimitiveType.Cube, "Bristles",new Vector3(0f, 0.012f, 0f), new Vector3(0.052f, 0.026f, 0.009f), bristleMat);

        _rollerRoot = new GameObject("Tool_FoamRoller");
        CreatePart(_rollerRoot.transform, PrimitiveType.Cylinder, "Grip",         new Vector3(0f, 0.11f, -0.07f),  new Vector3(0.018f, 0.055f, 0.018f), woodMat, Quaternion.Euler(65f, 0f, 0f));
        CreatePart(_rollerRoot.transform, PrimitiveType.Cylinder, "WireArm",      new Vector3(0f, 0.045f, -0.025f), new Vector3(0.005f, 0.045f, 0.005f), wireMat, Quaternion.Euler(35f, 0f, 0f));
        GameObject cyl = CreatePart(_rollerRoot.transform, PrimitiveType.Cylinder, "FoamCylinder", new Vector3(0f, 0.016f, 0f), new Vector3(0.032f, 0.065f, 0.032f), foamMat, Quaternion.Euler(0f, 0f, 90f));
        _rollerCylinder = cyl.transform;
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
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.digit1Key.wasPressedThisFrame)
            {
                SelectApplicator(ApplicatorType.HakeBrush);
                if (fluidController != null) fluidController.currentTool = CyanotypeFluidController.ToolMode.SensitizerBrush;
            }
            if (kb.digit4Key.wasPressedThisFrame)
            {
                SelectApplicator(ApplicatorType.FoamRoller);
                if (fluidController != null) fluidController.currentTool = CyanotypeFluidController.ToolMode.SensitizerBrush;
            }
        }
#else
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            SelectApplicator(ApplicatorType.HakeBrush);
            if (fluidController != null) fluidController.currentTool = CyanotypeFluidController.ToolMode.SensitizerBrush;
        }
        if (Input.GetKeyDown(KeyCode.Alpha4))
        {
            SelectApplicator(ApplicatorType.FoamRoller);
            if (fluidController != null) fluidController.currentTool = CyanotypeFluidController.ToolMode.SensitizerBrush;
        }
#endif
    }

    // Run in LateUpdate after camera movement so the 3D brush/roller never jitters
    void LateUpdate()
    {
        if (_cam == null || !_cam.isActiveAndEnabled) _cam = Camera.main;

        bool coatingActive = fluidController != null && fluidController.currentTool == CyanotypeFluidController.ToolMode.SensitizerBrush;
        Transform activeTransform = (activeApplicator == ApplicatorType.HakeBrush) ? _brushRoot.transform : _rollerRoot.transform;
        Transform idleTransform   = (activeApplicator == ApplicatorType.HakeBrush) ? _rollerRoot.transform : _brushRoot.transform;

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

            bool pressed = IsPrimaryHeld();
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
                if (pressed && _rollerCylinder != null && moveDist > 0.0001f)
                    _rollerCylinder.Rotate(Vector3.up, moveDist * 3200f, Space.Self);
            }
            _lastHitWorld = planePt;
        }
    }

    public void SelectApplicator(ApplicatorType type)
    {
        activeApplicator = type;
        if (fluidController != null && fluidController.fluidMaterial != null)
        {
            fluidController.fluidMaterial.SetFloat("_ApplicatorShape", (float)type);
            fluidController.brushRadius = (type == ApplicatorType.FoamRoller) ? 0.07f : 0.06f;
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