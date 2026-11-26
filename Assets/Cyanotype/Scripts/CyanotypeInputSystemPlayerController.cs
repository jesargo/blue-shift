using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Cyanotype.World
{
    /// <summary>
    /// New Input System replacement for CyanotypePlayerController.
    /// Drives CharacterController walking, sprinting, jumping, and gravity in ExplorationFullBody mode,
    /// and anchors the player in place during ActiveCyanotypeArmsOnly workbench sessions.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class CyanotypeInputSystemPlayerController : MonoBehaviour
    {
        [Header("Mode Reference")]
        [SerializeField] private CyanotypeFirstPersonRigMode _rigMode;
        [SerializeField] private bool _lockMovementInBenchMode = true;

        [Header("Locomotion Speeds (World Units / s)")]
        [SerializeField] private float _walkSpeed = 3.2f;
        [SerializeField] private float _sprintSpeed = 5.0f;
        [SerializeField] private float _groundAcceleration = 16f;
        [SerializeField] private float _airAcceleration = 5f;

        [Header("Jump & Gravity")]
        [SerializeField] private bool _allowJump = true;
        [SerializeField] private float _jumpHeight = 0.85f;
        [SerializeField] private float _gravity = -19.62f;

        private CharacterController _cc;
        private Vector3 _horizontalVelocity;
        private float _verticalVelocity;

#if ENABLE_INPUT_SYSTEM
        private InputAction _moveAction;
        private InputAction _sprintAction;
        private InputAction _jumpAction;
#endif

        public float CurrentPlanarSpeed => _horizontalVelocity.magnitude;

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            if (_rigMode == null) _rigMode = GetComponent<CyanotypeFirstPersonRigMode>();

#if ENABLE_INPUT_SYSTEM
            _moveAction = new InputAction("Move", InputActionType.Value);
            _moveAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            _moveAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/rightArrow");
            _moveAction.AddBinding("<Gamepad>/leftStick");

            _sprintAction = new InputAction("Sprint", InputActionType.Button, "<Keyboard>/leftShift");
            _sprintAction.AddBinding("<Gamepad>/leftStickPress");

            _jumpAction = new InputAction("Jump", InputActionType.Button, "<Keyboard>/space");
            _jumpAction.AddBinding("<Gamepad>/buttonSouth");
#endif
        }

        private void OnEnable()
        {
            DisableLegacyComponent("CyanotypePlayerController");

#if ENABLE_INPUT_SYSTEM
            _moveAction?.Enable();
            _sprintAction?.Enable();
            _jumpAction?.Enable();
#endif
        }

        private void OnDisable()
        {
#if ENABLE_INPUT_SYSTEM
            _moveAction?.Disable();
            _sprintAction?.Disable();
            _jumpAction?.Disable();
#endif
        }

        private void OnDestroy()
        {
#if ENABLE_INPUT_SYSTEM
            _moveAction?.Dispose();
            _sprintAction?.Dispose();
            _jumpAction?.Dispose();
#endif
        }

        private void Update()
        {
            if (_cc == null || !_cc.enabled) return;

#if ENABLE_INPUT_SYSTEM
            bool inBenchMode = _rigMode != null
                && _rigMode.CurrentMode == PlayerPresentationMode.ActiveCyanotypeArmsOnly;

            if (_cc.isGrounded && _verticalVelocity < 0f)
            {
                _verticalVelocity = -2f;
            }

            if (inBenchMode && _lockMovementInBenchMode)
            {
                _horizontalVelocity = Vector3.zero;
                _verticalVelocity += _gravity * Time.deltaTime;
                _cc.Move(Vector3.up * (_verticalVelocity * Time.deltaTime));
                return;
            }

            Vector2 input = _moveAction != null ? _moveAction.ReadValue<Vector2>() : Vector2.zero;
            if (input.sqrMagnitude > 1f) input.Normalize();

            bool isSprinting = _sprintAction != null && _sprintAction.IsPressed();
            float targetSpeed = isSprinting ? _sprintSpeed : _walkSpeed;

            Vector3 wishDir = (transform.right * input.x) + (transform.forward * input.y);
            Vector3 targetHorizontal = wishDir * targetSpeed;

            float accel = _cc.isGrounded ? _groundAcceleration : _airAcceleration;
            _horizontalVelocity = Vector3.MoveTowards(_horizontalVelocity, targetHorizontal, accel * Time.deltaTime);

            if (_allowJump && _cc.isGrounded && _jumpAction != null && _jumpAction.WasPressedThisFrame())
            {
                _verticalVelocity = Mathf.Sqrt(_jumpHeight * -2f * _gravity);
            }

            _verticalVelocity += _gravity * Time.deltaTime;

            Vector3 displacement = (_horizontalVelocity + Vector3.up * _verticalVelocity) * Time.deltaTime;
            _cc.Move(displacement);
#endif
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