using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[RequireComponent(typeof(Collider))]
public class DraggableShadowProp : MonoBehaviour
{
    [Header("Vertical Placement")]
    public bool matchInitialHeight = true;
    public float tableY = 0.805f;
    public float minHeight = 0.0f;
    public float maxHeight = 1.5f;

    [Header("Workbench Bounds")]
    public Vector2 xBounds = new Vector2(-3.5f, 3.5f);
    public Vector2 zBounds = new Vector2(-2.5f, 2.5f);

    private Camera _cam;
    private bool _dragging;
    private Vector3 _dragOffset;

    private static DraggableShadowProp _selectedProp;
    private static int _lastClickFrame = -1;
    private static int _lastCursorToggleFrame = -1;
    private static DraggableShadowProp _clickedPropThisFrame;

    public static DraggableShadowProp SelectedProp => _selectedProp;

    void Start()
    {
        _cam = FindActiveCamera();
        if (matchInitialHeight)
        {
            tableY = transform.position.y;
        }

        EnsureSolidShadowColliders();
    }

    private void EnsureSolidShadowColliders()
    {
        // Check if this prop already has at least one non-trigger collider for UV raycasts
        Collider[] existing = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < existing.Length; i++)
        {
            if (!existing[i].isTrigger)
                return;
        }

        // Otherwise, add solid MeshColliders (or BoxColliders) to child meshes so UV rays hit them
        MeshFilter[] meshes = GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < meshes.Length; i++)
        {
            if (meshes[i].sharedMesh == null) continue;
            if (meshes[i].TryGetComponent<Collider>(out var col))
            {
                col.isTrigger = false;
            }
            else
            {
                var mc = meshes[i].gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = meshes[i].sharedMesh;
                mc.convex = true;
            }
        }
    }

    void Update()
    {
        if (_cam == null || !_cam.isActiveAndEnabled)
            _cam = FindActiveCamera();

        if (_cam == null) return;

        HandleCursorToggle();
        HandlePointerInput();

        if (_selectedProp == this)
        {
            HandleKeyboardAdjustment();
        }
    }

    private static void HandleCursorToggle()
    {
        if (_lastCursorToggleFrame == Time.frameCount) return;

        bool togglePressed = false;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame)
            togglePressed = true;
#else
        if (Input.GetKeyDown(KeyCode.Tab))
            togglePressed = true;
#endif

        if (togglePressed)
        {
            _lastCursorToggleFrame = Time.frameCount;
            bool lockIt = Cursor.lockState != CursorLockMode.Locked;
            Cursor.lockState = lockIt ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !lockIt;
        }
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

    private void HandlePointerInput()
    {
        bool pointerDown = WasPointerPressedThisFrame();
        bool pointerHeld = IsPointerHeld();
        bool pointerUp = WasPointerReleasedThisFrame();
        Vector2 screenPos = GetPointerScreenPosition();

        if (pointerDown)
        {
            DraggableShadowProp hitProp = RaycastAllForProp(screenPos);
            if (hitProp == this)
            {
                _dragging = true;
                _selectedProp = this;

                Plane dragPlane = new Plane(Vector3.up, new Vector3(0f, transform.position.y, 0f));
                Ray ray = _cam.ScreenPointToRay(screenPos);
                if (dragPlane.Raycast(ray, out float enter))
                {
                    _dragOffset = transform.position - ray.GetPoint(enter);
                }
            }
            else if (hitProp == null && _selectedProp == this)
            {
                _selectedProp = null;
            }
        }

        if (_dragging && pointerHeld)
        {
            Plane dragPlane = new Plane(Vector3.up, new Vector3(0f, transform.position.y, 0f));
            Ray ray = _cam.ScreenPointToRay(screenPos);
            if (dragPlane.Raycast(ray, out float enter))
            {
                Vector3 target = ray.GetPoint(enter) + _dragOffset;
                target.x = Mathf.Clamp(target.x, xBounds.x, xBounds.y);
                target.z = Mathf.Clamp(target.z, zBounds.x, zBounds.y);
                transform.position = target;
            }
        }

        if (_dragging && pointerUp)
        {
            _dragging = false;
        }
    }

    private void HandleKeyboardAdjustment()
    {
        float hDelta = 0f;
        if (IsRaiseKeyHeld(true)) hDelta += 0.6f * Time.deltaTime;   // E
        if (IsRaiseKeyHeld(false)) hDelta -= 0.6f * Time.deltaTime;  // Q

        if (Mathf.Abs(hDelta) > 0f)
        {
            Vector3 p = transform.position;
            p.y = Mathf.Clamp(p.y + hDelta, tableY + minHeight, tableY + maxHeight);
            transform.position = p;
        }

        float rDelta = 0f;
        if (IsRotateKeyHeld(true)) rDelta += 75f * Time.deltaTime;   // D
        if (IsRotateKeyHeld(false)) rDelta -= 75f * Time.deltaTime;  // A

        if (Mathf.Abs(rDelta) > 0f)
        {
            transform.Rotate(Vector3.up, rDelta, Space.World);
        }
    }

    private DraggableShadowProp RaycastAllForProp(Vector2 screenPos)
    {
        if (_lastClickFrame != Time.frameCount)
        {
            _lastClickFrame = Time.frameCount;
            _clickedPropThisFrame = null;

            Ray ray = _cam.ScreenPointToRay(screenPos);
            RaycastHit[] hits = Physics.RaycastAll(ray, 100f, ~0, QueryTriggerInteraction.Collide);

            float closestPropDist = float.MaxValue;
            for (int i = 0; i < hits.Length; i++)
            {
                var prop = hits[i].collider.GetComponentInParent<DraggableShadowProp>();
                if (prop != null && hits[i].distance < closestPropDist)
                {
                    closestPropDist = hits[i].distance;
                    _clickedPropThisFrame = prop;
                }
            }
        }
        return _clickedPropThisFrame;
    }

    private static Vector2 GetPointerScreenPosition()
    {
        if (Cursor.lockState == CursorLockMode.Locked)
            return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
            return Mouse.current.position.ReadValue();
        return Vector2.zero;
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

    private static bool IsRaiseKeyHeld(bool raiseKey)
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current == null) return false;
        return raiseKey ? Keyboard.current.eKey.isPressed : Keyboard.current.qKey.isPressed;
#else
        return Input.GetKey(raiseKey ? KeyCode.E : KeyCode.Q);
#endif
    }

    private static bool IsRotateKeyHeld(bool rightKey)
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current == null) return false;
        return rightKey ? Keyboard.current.dKey.isPressed : Keyboard.current.aKey.isPressed;
#else
        return Input.GetKey(rightKey ? KeyCode.D : KeyCode.A);
#endif
    }
}