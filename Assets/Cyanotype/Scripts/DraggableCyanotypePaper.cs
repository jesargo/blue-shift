using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class DraggableCyanotypePaper : MonoBehaviour
{
    [Header("References")]
    public CyanotypeFluidController fluidController;

    [Header("Drag & Dip Heights")]
    public float restingY = 0.806f;
    public float liftHeight = 0.055f;
    public float submergedY = 0.818f;

    [Header("Workbench & Patio Bounds")]
    public Vector2 xBounds = new Vector2(-1.1f, 5.1f);
    public Vector2 zBounds = new Vector2(-2.1f, -0.9f);

    private Camera _cam;
    private bool _dragging;
    private bool _inWaterTray;
    private Vector3 _dragOffset;
    private Vector3 _homePosition;
    private Quaternion _homeRotation;

    void Start()
    {
        if (fluidController == null)
            fluidController = FindFirstObjectByType<CyanotypeFluidController>();

        restingY = transform.position.y;
        _homePosition = transform.position;
        _homeRotation = transform.rotation;
        _cam = FindActiveCamera();
    }

    void Update()
    {
        if (_cam == null || !_cam.isActiveAndEnabled)
            _cam = FindActiveCamera();
        if (_cam == null) return;

        HandleSnapHome();
        HandlePaperDrag();
    }

    public void ReturnToWorkbench()
    {
        _dragging = false;
        _inWaterTray = false;
        transform.position = _homePosition;
        transform.rotation = _homeRotation;
        if (fluidController != null && fluidController.currentTool == CyanotypeFluidController.ToolMode.SubmergeTrayWash)
            fluidController.currentTool = CyanotypeFluidController.ToolMode.None;
    }

    private void HandleSnapHome()
    {
        bool snapPressed = false;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame)
            snapPressed = true;
#else
        if (Input.GetKeyDown(KeyCode.C))
            snapPressed = true;
#endif
        if (snapPressed)
        {
            ReturnToWorkbench();
        }
    }

    private void HandlePaperDrag()
    {
        if (CyanotypeStudioHUD.IsPointerOverHUD && !_dragging)
            return;

        // Allow picking up the paper in Move mode (0) or while submerged in the tray (3)
        if (fluidController != null &&
            fluidController.currentTool != CyanotypeFluidController.ToolMode.None &&
            fluidController.currentTool != CyanotypeFluidController.ToolMode.SubmergeTrayWash)
        {
            _dragging = false;
            return;
        }

        Vector2 screenPos = GetPointerScreenPosition();
        bool pointerDown = WasPointerPressedThisFrame();
        bool pointerHeld = IsPointerHeld();
        bool pointerUp   = WasPointerReleasedThisFrame();

        if (pointerDown && IsPaperUnderPointer(screenPos))
        {
            _dragging = true;
            _inWaterTray = false;
            Vector3 liftedPos = transform.position;
            liftedPos.y = restingY + liftHeight;
            transform.position = liftedPos;

            Plane dragPlane = new Plane(Vector3.up, new Vector3(0f, liftedPos.y, 0f));
            Ray ray = _cam.ScreenPointToRay(screenPos);
            if (dragPlane.Raycast(ray, out float enter))
                _dragOffset = transform.position - ray.GetPoint(enter);
        }

        if (_dragging && pointerHeld)
        {
            float activeY = restingY + liftHeight;
            Plane dragPlane = new Plane(Vector3.up, new Vector3(0f, activeY, 0f));
            Ray ray = _cam.ScreenPointToRay(screenPos);
            if (dragPlane.Raycast(ray, out float enter))
            {
                Vector3 target = ray.GetPoint(enter) + _dragOffset;
                target.x = Mathf.Clamp(target.x, xBounds.x, xBounds.y);
                target.z = Mathf.Clamp(target.z, zBounds.x, zBounds.y);
                target.y = activeY;
                transform.position = target;
            }
        }

        if (_dragging && pointerUp)
        {
            _dragging = false;
            _inWaterTray = IsOverWaterTray();
            Vector3 settlePos = transform.position;
            settlePos.y = _inWaterTray ? submergedY : restingY;
            transform.position = settlePos;

            if (fluidController != null)
            {
                fluidController.currentTool = _inWaterTray
                    ? CyanotypeFluidController.ToolMode.SubmergeTrayWash
                    : CyanotypeFluidController.ToolMode.None;
            }
        }
    }

    private bool IsOverWaterTray()
    {
        Collider[] overlaps = Physics.OverlapBox(
            transform.position,
            new Vector3(0.25f, 0.15f, 0.35f),
            transform.rotation,
            ~0,
            QueryTriggerInteraction.Collide
        );

        for (int i = 0; i < overlaps.Length; i++)
        {
            if (overlaps[i].GetComponent<WaterTrayTrigger>() != null ||
                overlaps[i].name == "WaterVolume")
            {
                return true;
            }
        }
        return false;
    }

    private bool IsPaperUnderPointer(Vector2 screenPos)
    {
        Ray ray = _cam.ScreenPointToRay(screenPos);
        RaycastHit[] hits = Physics.RaycastAll(ray, 50f, ~0, QueryTriggerInteraction.Ignore);

        bool hitThisPaper = false;
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].collider.GetComponentInParent<DraggableShadowProp>() != null)
                return false;

            if (hits[i].transform == transform)
                hitThisPaper = true;
        }
        return hitThisPaper;
    }

    // Trigger ONCE upon entering the water tray so user tool switches aren't overridden every frame
    void OnTriggerEnter(Collider other)
    {
        if (_dragging) return;
        if (other.GetComponent<WaterTrayTrigger>() != null || other.name == "WaterVolume")
        {
            _inWaterTray = true;
            if (fluidController != null)
                fluidController.currentTool = CyanotypeFluidController.ToolMode.SubmergeTrayWash;
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.GetComponent<WaterTrayTrigger>() != null || other.name == "WaterVolume")
        {
            _inWaterTray = false;
            if (fluidController != null && fluidController.currentTool == CyanotypeFluidController.ToolMode.SubmergeTrayWash)
                fluidController.currentTool = CyanotypeFluidController.ToolMode.None;
        }
    }

    private static Camera FindActiveCamera()
    {
        if (Camera.main != null && Camera.main.isActiveAndEnabled) return Camera.main;
        foreach (var cam in Camera.allCameras)
            if (cam.isActiveAndEnabled && cam.targetTexture == null) return cam;
        return null;
    }

    private static Vector2 GetPointerScreenPosition()
    {
        if (Cursor.lockState == CursorLockMode.Locked)
            return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
        return Input.mousePosition;
#endif
    }

    private static bool WasPointerPressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
        return Input.GetMouseButtonDown(0);
#endif
    }

    private static bool IsPointerHeld()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.isPressed;
#else
        return Input.GetMouseButton(0);
#endif
    }

    private static bool WasPointerReleasedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame;
#else
        return Input.GetMouseButtonUp(0);
#endif
    }
}