using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[RequireComponent(typeof(Camera))]
public class CyanotypePlayerController : MonoBehaviour
{
    [Header("Optional Builder Compatibility References")]
    public Camera playerCamera;
    public LayerMask interactableLayers = ~0;
    public LayerMask placementSurfaceLayers = ~0;

    [Header("Movement & Mouse Look")]
    public float moveSpeed = 2.4f;
    public float panSpeed = 0.0035f;
    public float zoomSpeed = 0.35f;
    public float lookSensitivity = 0.15f;
    public Vector2 pitchLimits = new Vector2(-85f, 85f);

    [Header("Default Workbench View")]
    public Vector3 homePosition = new Vector3(0f, 1.55f, -2.35f);
    public Vector3 homeEulerAngles = new Vector3(48f, 0f, 0f);
    public float minEyeHeight = 0.65f;

    public bool IsFreeLookLocked { get; private set; }
    private bool _cursorReleasedToOS;
    private float _yaw;
    private float _pitch;

    void Awake()
    {
        if (playerCamera == null)
            playerCamera = GetComponent<Camera>();

        AudioListener myListener = GetComponent<AudioListener>();
        if (myListener == null)
            myListener = gameObject.AddComponent<AudioListener>();

        foreach (var listener in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
        {
            if (listener != myListener)
                listener.enabled = false;
        }
    }

    void Start()
    {
        if (transform.position.y < 0.25f)
        {
            transform.position = homePosition;
            transform.eulerAngles = homeEulerAngles;
        }

        Vector3 euler = transform.eulerAngles;
        _yaw = euler.y;
        _pitch = euler.x > 180f ? euler.x - 360f : euler.x;

        SetWorkbenchCursorMode();
    }

    void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus && !_cursorReleasedToOS)
        {
            if (IsFreeLookLocked) SetFreeLookMode();
            else SetWorkbenchCursorMode();
        }
    }

    void Update()
    {
        HandleCursorModeInput();
        HandleLook();
        HandleMousePanAndZoom();
        HandleMovement();
        HandleResetView();

        if (transform.position.y < minEyeHeight)
        {
            Vector3 pos = transform.position;
            pos.y = minEyeHeight;
            transform.position = pos;
        }
    }

    private void HandleCursorModeInput()
    {
        if (WasEscapePressed())
        {
            _cursorReleasedToOS = true;
            IsFreeLookLocked = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            return;
        }

        // Re-engage window confinement when clicking back into the game view
        if (_cursorReleasedToOS && (IsLeftMousePressedThisFrame() || IsRightMousePressedThisFrame()))
        {
            _cursorReleasedToOS = false;
            SetWorkbenchCursorMode();
        }

        if (_cursorReleasedToOS) return;

        // Tab toggles full 360° FPS camera look vs. confined workbench cursor
        if (WasTabPressed())
        {
            if (IsFreeLookLocked) SetWorkbenchCursorMode();
            else SetFreeLookMode();
        }

        // Holding Right-Click temporarily locks cursor for smooth camera rotation without leaving the window
        if (!IsFreeLookLocked)
        {
            if (IsRightMouseHeld())
            {
                if (Cursor.lockState != CursorLockMode.Locked)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
            }
            else if (Cursor.lockState != CursorLockMode.Confined)
            {
                SetWorkbenchCursorMode();
            }
        }
    }

    public void SetWorkbenchCursorMode()
    {
        IsFreeLookLocked = false;
        _cursorReleasedToOS = false;
        Cursor.lockState = CursorLockMode.Confined;
        Cursor.visible = true;
    }

    public void SetFreeLookMode()
    {
        IsFreeLookLocked = true;
        _cursorReleasedToOS = false;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void HandleLook()
    {
        if (_cursorReleasedToOS) return;

        bool allowLook = IsFreeLookLocked || IsRightMouseHeld();
        if (!allowLook) return;

        Vector2 delta = GetMouseDelta();
        if (delta.sqrMagnitude < 0.0001f) return;

        _yaw += delta.x * lookSensitivity;
        _pitch = Mathf.Clamp(_pitch - delta.y * lookSensitivity, pitchLimits.x, pitchLimits.y);
        transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
    }

    private void HandleMousePanAndZoom()
    {
        if (_cursorReleasedToOS || CyanotypeStudioHUD.IsPointerOverHUD) return;

        if (IsMiddleMouseHeld())
        {
            Vector2 delta = GetMouseDelta();
            Vector3 pan = (-transform.right * delta.x - transform.up * delta.y) * panSpeed;
            transform.position += pan;
        }

        float scroll = GetScrollDelta();
        if (Mathf.Abs(scroll) > 0.001f)
        {
            transform.position += transform.forward * (scroll * zoomSpeed);
        }
    }

    private void HandleMovement()
    {
        Vector2 moveInput = GetMoveAxes();
        float vertical = GetVerticalAxis();

        if (moveInput.sqrMagnitude < 0.001f && Mathf.Abs(vertical) < 0.001f)
            return;

        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        Vector3 right = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;

        Vector3 velocity = (forward * moveInput.y + right * moveInput.x + Vector3.up * vertical) * moveSpeed;
        transform.position += velocity * Time.deltaTime;
    }

    private void HandleResetView()
    {
        if (WasRPressed())
        {
            transform.position = homePosition;
            transform.eulerAngles = homeEulerAngles;
            _yaw = homeEulerAngles.y;
            _pitch = homeEulerAngles.x;
        }
    }

    private static bool IsRightMouseHeld()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.rightButton.isPressed;
#else
        return Input.GetMouseButton(1);
#endif
    }

    private static bool IsRightMousePressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
#else
        return Input.GetMouseButtonDown(1);
#endif
    }

    private static bool IsLeftMousePressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
        return Input.GetMouseButtonDown(0);
#endif
    }

    private static bool IsMiddleMouseHeld()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.middleButton.isPressed;
#else
        return Input.GetMouseButton(2);
#endif
    }

    private static float GetScrollDelta()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null ? Mouse.current.scroll.ReadValue().y * 0.01f : 0f;
#else
        return Input.GetAxis("Mouse ScrollWheel");
#endif
    }

    private static Vector2 GetMouseDelta()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
#else
        return new Vector2(Input.GetAxis("Mouse X") * 12f, Input.GetAxis("Mouse Y") * 12f);
#endif
    }

    private static Vector2 GetMoveAxes()
    {
        float x = 0f, y = 0f;
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) x += 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)  x -= 1f;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed)    y += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed)  y -= 1f;
        }
#else
        x = Input.GetAxisRaw("Horizontal");
        y = Input.GetAxisRaw("Vertical");
#endif
        return new Vector2(x, y).normalized;
    }

    private static float GetVerticalAxis()
    {
        float v = 0f;
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.eKey.isPressed && kb.leftShiftKey.isPressed) v += 1f;
            if (kb.leftCtrlKey.isPressed) v -= 1f;
        }
#else
        if (Input.GetKey(KeyCode.LeftControl)) v -= 1f;
#endif
        return v;
    }

    private static bool WasTabPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Tab);
#endif
    }

    private static bool WasEscapePressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Escape);
#endif
    }

    private static bool WasRPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.R);
#endif
    }
}