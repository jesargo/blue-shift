using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Cyanotype.World
{
    /// <summary>
    /// New Input System replacement for CyanotypeCameraRig.
    /// Drives 360-degree yaw/pitch in ExplorationFullBody mode and clamps look angles
    /// around the workbench in ActiveCyanotypeArmsOnly mode, with Left-Alt cursor release for HUD sliders.
    /// </summary>
    public class CyanotypeInputSystemCameraRig : MonoBehaviour
    {
        [Header("Camera & Mode References")]
        [SerializeField] private Transform _cameraTransform;
        [SerializeField] private CyanotypeFirstPersonRigMode _rigMode;

        [Header("Look Sensitivity & Smoothing")]
        [SerializeField] private float _mouseSensitivity = 0.14f;
        [SerializeField] private float _gamepadLookSpeed = 145f;
        [SerializeField] private float _minPitch = -82f;
        [SerializeField] private float _maxPitch = 82f;
        [SerializeField] private float _lookSmoothing = 26f;

        [Header("Workbench Mode Look Bounds (ActiveCyanotypeArmsOnly)")]
        [SerializeField] private bool _clampYawAtWorkbench = true;
        [SerializeField] private float _benchMaxYawOffset = 58f;
        [SerializeField] private float _benchMinPitch = -78f;
        [SerializeField] private float _benchMaxPitch = 28f;

#if ENABLE_INPUT_SYSTEM
        private InputAction _lookAction;
        private InputAction _gamepadLookAction;
        private InputAction _cursorUnlockHoldAction;
#endif

        private float _targetYaw;
        private float _targetPitch;
        private float _currentYaw;
        private float _currentPitch;
        private float _benchAnchorYaw;
        private bool _wasInBenchMode;
        private bool _cursorLocked = true;
        private bool _hasStudioChildren;

        private void Awake()
        {
            if (_rigMode == null) _rigMode = GetComponent<CyanotypeFirstPersonRigMode>();

            if (_cameraTransform == null)
            {
                var cam = GetComponentInChildren<Camera>();
                if (cam != null) _cameraTransform = cam.transform;
            }

            var childBehaviours = GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < childBehaviours.Length; i++)
            {
                var b = childBehaviours[i];
                if (b == null) continue;
                string typeName = b.GetType().Name;
                if (typeName == "CyanotypeWorkbenchStation" || typeName == "CyanotypeDipTray")
                {
                    _hasStudioChildren = true;
                    break;
                }
            }

            bool singleObjectCamera = (_cameraTransform == null || _cameraTransform == transform);
            _targetYaw = _currentYaw = singleObjectCamera
                ? (_cameraTransform != null ? _cameraTransform.eulerAngles.y : transform.eulerAngles.y)
                : transform.eulerAngles.y;

            if (_cameraTransform != null)
            {
                float initialPitch = singleObjectCamera
                    ? _cameraTransform.eulerAngles.x
                    : _cameraTransform.localEulerAngles.x;
                if (initialPitch > 180f) initialPitch -= 360f;
                _targetPitch = _currentPitch = initialPitch;
            }

#if ENABLE_INPUT_SYSTEM
            _lookAction = new InputAction("LookMouse", InputActionType.Value, "<Mouse>/delta");
            _gamepadLookAction = new InputAction("LookGamepad", InputActionType.Value, "<Gamepad>/rightStick");
            _cursorUnlockHoldAction = new InputAction("UnlockCursorHold", InputActionType.Button, "<Keyboard>/leftAlt");
#endif
        }

        private void OnEnable()
        {
            DisableLegacyComponent("CyanotypeCameraRig");

#if ENABLE_INPUT_SYSTEM
            _lookAction?.Enable();
            _gamepadLookAction?.Enable();
            _cursorUnlockHoldAction?.Enable();
#endif
            SetCursorLock(true);
        }

        private void OnDisable()
        {
#if ENABLE_INPUT_SYSTEM
            _lookAction?.Disable();
            _gamepadLookAction?.Disable();
            _cursorUnlockHoldAction?.Disable();
#endif
            SetCursorLock(false);
        }

        private void OnDestroy()
        {
#if ENABLE_INPUT_SYSTEM
            _lookAction?.Dispose();
            _gamepadLookAction?.Dispose();
            _cursorUnlockHoldAction?.Dispose();
#endif
        }

        private void LateUpdate()
        {
            if (_cameraTransform == null) return;

#if ENABLE_INPUT_SYSTEM
            bool holdUnlock = _cursorUnlockHoldAction != null && _cursorUnlockHoldAction.IsPressed();
            if (holdUnlock && _cursorLocked)
            {
                SetCursorLock(false);
            }
            else if (!holdUnlock && !_cursorLocked)
            {
                SetCursorLock(true);
            }

            if (!_cursorLocked) return;

            Vector2 mouseDelta = _lookAction != null ? _lookAction.ReadValue<Vector2>() : Vector2.zero;
            Vector2 stickDelta = _gamepadLookAction != null ? _gamepadLookAction.ReadValue<Vector2>() : Vector2.zero;

            float deltaYaw = (mouseDelta.x * _mouseSensitivity) + (stickDelta.x * _gamepadLookSpeed * Time.deltaTime);
            float deltaPitch = (mouseDelta.y * _mouseSensitivity) + (stickDelta.y * _gamepadLookSpeed * Time.deltaTime);

            bool inBenchMode = _rigMode != null
                && _rigMode.CurrentMode == PlayerPresentationMode.ActiveCyanotypeArmsOnly;

            if (inBenchMode && !_wasInBenchMode)
            {
                _benchAnchorYaw = _targetYaw;
            }
            _wasInBenchMode = inBenchMode;

            _targetYaw += deltaYaw;
            _targetPitch -= deltaPitch;

            if (inBenchMode)
            {
                if (_clampYawAtWorkbench)
                {
                    float relativeYaw = Mathf.DeltaAngle(_benchAnchorYaw, _targetYaw);
                    relativeYaw = Mathf.Clamp(relativeYaw, -_benchMaxYawOffset, _benchMaxYawOffset);
                    _targetYaw = _benchAnchorYaw + relativeYaw;
                }
                _targetPitch = Mathf.Clamp(_targetPitch, _benchMinPitch, _benchMaxPitch);
            }
            else
            {
                _targetPitch = Mathf.Clamp(_targetPitch, _minPitch, _maxPitch);
            }

            float t = 1f - Mathf.Exp(-_lookSmoothing * Time.deltaTime);
            _currentYaw = Mathf.LerpAngle(_currentYaw, _targetYaw, t);
            _currentPitch = Mathf.Lerp(_currentPitch, _targetPitch, t);

            // Rotate the camera transform directly if attached to the camera or a shared container holding studio meshes.
            if (_cameraTransform == transform || _hasStudioChildren)
            {
                _cameraTransform.rotation = Quaternion.Euler(_currentPitch, _currentYaw, 0f);
            }
            else
            {
                transform.rotation = Quaternion.Euler(0f, _currentYaw, 0f);
                _cameraTransform.localRotation = Quaternion.Euler(_currentPitch, 0f, 0f);
            }
#endif
        }

        private void SetCursorLock(bool locked)
        {
            _cursorLocked = locked;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        private void DisableLegacyComponent(string legacyTypeName)
        {
            var components = GetComponents<MonoBehaviour>();
            for (int i = 0; i < components.Length; i++)
            {
                var c = components[i];
                if (c != null && c != this && c.GetType().Name == legacyTypeName)
                {
                    c.enabled = false;
                }
            }
        }
    }
}