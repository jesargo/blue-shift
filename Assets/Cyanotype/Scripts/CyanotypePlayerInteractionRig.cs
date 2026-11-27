using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Cyanotype.Gameplay
{
    /// <summary>
    /// Connects CyanotypePlayerController with CyanotypePhysicsInteractable tools,
    /// studio trays, and DraggableCyanotypePaper without modifying base scripts.
    /// </summary>
    public class CyanotypePlayerInteractionRig : MonoBehaviour
    {
        [Header("Camera & Workbench Reach")]
        [SerializeField] private Camera _playerCamera;
        [SerializeField] private float _maxReachWorld = 2.2f;
        [SerializeField] private LayerMask _interactableMask = ~0;
        [SerializeField] private LayerMask _paperMask = ~0;
        [SerializeField] private LayerMask _trayMask = ~0;

        [Header("3D Tool Hold & Paper-Conforming Stroke Offsets (World Units)")]
        [SerializeField] private Transform _holdAnchor;
        [SerializeField] private Vector3 _defaultHoldLocalOffset = new Vector3(0.22f, -0.16f, 0.42f);
        [SerializeField] private Vector3 _defaultHoldLocalEuler = new Vector3(28f, -18f, 12f);
        [SerializeField] private float _paperHoverOffset = 0.022f;
        [SerializeField] private float _paperStrokePressOffset = 0.002f;
        [SerializeField] private float _toolFollowSharpness = 18f;

        [Header("Dipping Motion")]
        [SerializeField] private float _dipDuration = 0.28f;

        private CyanotypePhysicsInteractable _heldTool;
        private Transform _heldOriginalParent;
        private Vector3 _heldRestPosition;
        private Quaternion _heldRestRotation;

        private bool _isPainting;
        private bool _isDipping;
        private float _dipTimer;
        private Vector3 _dipWorldTarget;
        private Vector3 _lastStrokeWorldPos;

        public CyanotypePhysicsInteractable HeldTool => _heldTool;
        public bool IsHoldingTool => _heldTool != null;

        private void Awake()
        {
            if (_playerCamera == null)
            {
                _playerCamera = GetComponentInChildren<Camera>();
                if (_playerCamera == null) _playerCamera = Camera.main;
            }

            if (_holdAnchor == null && _playerCamera != null)
            {
                var anchorGo = new GameObject("Cyanotype_ToolHoldAnchor");
                anchorGo.transform.SetParent(_playerCamera.transform, false);
                anchorGo.transform.localPosition = _defaultHoldLocalOffset;
                anchorGo.transform.localRotation = Quaternion.Euler(_defaultHoldLocalEuler);
                _holdAnchor = anchorGo.transform;
            }
        }

        private void OnDisable()
        {
            ReleaseHeldTool(returnToRestPose: true);
        }

        private void Update()
        {
            if (_playerCamera == null) return;

            bool pointerOverHUD = CyanotypeStudioHUD.IsPointerOverHUD;
            bool primaryDown = !pointerOverHUD && ReadPrimaryPressedThisFrame();
            bool primaryHeld = !pointerOverHUD && ReadPrimaryHeld();
            bool secondaryDown = !pointerOverHUD && ReadSecondaryPressedThisFrame();
            bool dipKeyDown = ReadDipKeyPressedThisFrame();

            if (_heldTool == null)
            {
                if (primaryDown)
                {
                    TryPickupTargetTool();
                }
                return;
            }

            // Drop / return tool to bench
            if (secondaryDown)
            {
                ReleaseHeldTool(returnToRestPose: true);
                return;
            }

            // Explicit dip key (E) or clicking directly on a tray while holding an applicator
            if (dipKeyDown && TryFindTrayInSight(out CyanotypeDipTray hotkeyTray))
            {
                BeginTrayDip(hotkeyTray);
                return;
            }

            if (primaryDown && TryFindTrayInSight(out CyanotypeDipTray clickedTray))
            {
                BeginTrayDip(clickedTray);
                return;
            }

            // Update stroke state when aiming at DraggableCyanotypePaper
            bool aimingAtPaper = TryRaycastPaper(out RaycastHit paperHit);
            bool wantsStroke = primaryHeld && aimingAtPaper && !_isDipping && IsLiquidApplicator(_heldTool.Role);

            if (wantsStroke != _isPainting)
            {
                _isPainting = wantsStroke;
                _heldTool.SetStrokeActive(_isPainting);
                if (_isPainting)
                {
                    _lastStrokeWorldPos = paperHit.point;
                }
            }

            UpdateHeldToolTransform(aimingAtPaper, paperHit);
        }

        private void TryPickupTargetTool()
        {
            Ray ray = ActiveAimRay();
            if (!Physics.Raycast(ray, out RaycastHit hit, _maxReachWorld, _interactableMask, QueryTriggerInteraction.Collide))
                return;

            var interactable = hit.collider.GetComponentInParent<CyanotypePhysicsInteractable>();
            if (interactable == null) return;

            _heldTool = interactable;
            _heldOriginalParent = _heldTool.transform.parent;
            _heldRestPosition = _heldTool.transform.position;
            _heldRestRotation = _heldTool.transform.rotation;

            if (_heldTool.TryGetComponent<Rigidbody>(out var rb))
            {
                rb.isKinematic = true;
                rb.useGravity = false;
            }

            // Disable tool colliders while held so raycasts to paper/trays pass cleanly through
            foreach (var col in _heldTool.GetComponentsInChildren<Collider>())
            {
                col.enabled = false;
            }

            _heldTool.SetHeld(true);
            _isPainting = false;
            _isDipping = false;

            SyncFluidControllerToolFromHeld(_heldTool);
        }

        public void ReleaseHeldTool(bool returnToRestPose)
        {
            if (_heldTool == null) return;

            if (_isPainting)
            {
                _isPainting = false;
                _heldTool.SetStrokeActive(false);
            }

            _heldTool.SetHeld(false);

            foreach (var col in _heldTool.GetComponentsInChildren<Collider>())
            {
                col.enabled = true;
            }

            if (_heldTool.TryGetComponent<Rigidbody>(out var rb))
            {
                bool isProp = _heldTool.Role == CyanotypeToolRole.ShadowOrStencilProp;
                rb.isKinematic = !isProp;
                rb.useGravity = isProp;
            }

            if (returnToRestPose)
            {
                _heldTool.transform.SetParent(_heldOriginalParent, true);
                _heldTool.transform.SetPositionAndRotation(_heldRestPosition, _heldRestRotation);
            }

            _heldTool = null;
            _isDipping = false;

            var fluidCtrl = FindAnyObjectByType<CyanotypeFluidController>();
            if (fluidCtrl != null)
            {
                fluidCtrl.currentTool = CyanotypeFluidController.ToolMode.None;
            }
        }

        private static void SyncFluidControllerToolFromHeld(CyanotypePhysicsInteractable tool)
        {
            var fluidCtrl = FindAnyObjectByType<CyanotypeFluidController>();
            if (fluidCtrl == null || tool == null) return;

            fluidCtrl.applicatorShape = tool.ApplicatorShape;
            fluidCtrl.currentTool = tool.Role switch
            {
                CyanotypeToolRole.SensitizerApplicator => CyanotypeFluidController.ToolMode.SensitizerBrush,
                CyanotypeToolRole.WaterWashApplicator  => CyanotypeFluidController.ToolMode.WaterPourWash,
                CyanotypeToolRole.AlkaliBleachBath     => CyanotypeFluidController.ToolMode.AlkaliBath,
                CyanotypeToolRole.TanninTonerBath      => CyanotypeFluidController.ToolMode.TanninBath,
                CyanotypeToolRole.PeroxideOxidizerBath => CyanotypeFluidController.ToolMode.PeroxideSprayBrush,
                _ => CyanotypeFluidController.ToolMode.None
            };
        }

        private void BeginTrayDip(CyanotypeDipTray tray)
        {
            if (!IsLiquidApplicator(_heldTool.Role)) return;

            if (_isPainting)
            {
                _isPainting = false;
                _heldTool.SetStrokeActive(false);
            }

            _isDipping = true;
            _dipTimer = _dipDuration;
            _dipWorldTarget = tray.GetDipWorldPoint();

            if (tray.CanAcceptTool(_heldTool))
            {
                _heldTool.RefillWetLoad(1f);
            }
            tray.OnToolDipped(_heldTool);
        }

        private void UpdateHeldToolTransform(bool aimingAtPaper, RaycastHit paperHit)
        {
            Vector3 targetPos;
            Quaternion targetRot;

            if (_isDipping)
            {
                _dipTimer -= Time.deltaTime;
                targetPos = _dipWorldTarget;
                targetRot = Quaternion.LookRotation(_playerCamera.transform.forward, Vector3.up) * Quaternion.Euler(35f, 0f, 0f);

                if (_dipTimer <= 0f)
                {
                    _isDipping = false;
                }
            }
            else if (aimingAtPaper)
            {
                // Conform brush position and tilt to DraggableCyanotypePaper surface normal
                float clearance = _isPainting ? _paperStrokePressOffset : _paperHoverOffset;
                targetPos = paperHit.point + paperHit.normal * clearance;

                Vector3 strokeDelta = paperHit.point - _lastStrokeWorldPos;
                Vector3 forwardTangent = strokeDelta.sqrMagnitude > 1e-6f
                    ? Vector3.ProjectOnPlane(strokeDelta.normalized, paperHit.normal).normalized
                    : Vector3.ProjectOnPlane(_playerCamera.transform.forward, paperHit.normal).normalized;

                if (forwardTangent.sqrMagnitude < 1e-4f)
                {
                    forwardTangent = Vector3.forward;
                }

                // Tilt hake brush / glass rod at a natural ~55 deg coating angle relative to paper normal
                Quaternion surfaceFrame = Quaternion.LookRotation(forwardTangent, paperHit.normal);
                targetRot = surfaceFrame * Quaternion.Euler(35f, 0f, 0f);

                if (_isPainting)
                {
                    _lastStrokeWorldPos = Vector3.Lerp(_lastStrokeWorldPos, paperHit.point, 0.35f);
                }
            }
            else
            {
                targetPos = _holdAnchor.position;
                targetRot = _holdAnchor.rotation;
            }

            float t = 1f - Mathf.Exp(-_toolFollowSharpness * Time.deltaTime);
            _heldTool.transform.position = Vector3.Lerp(_heldTool.transform.position, targetPos, t);
            _heldTool.transform.rotation = Quaternion.Slerp(_heldTool.transform.rotation, targetRot, t);
        }

        private bool TryRaycastPaper(out RaycastHit hit)
        {
            Ray ray = ActiveAimRay();
            if (!Physics.Raycast(ray, out hit, _maxReachWorld, _paperMask, QueryTriggerInteraction.Collide))
                return false;

            // Verify the hit collider belongs to a DraggableCyanotypePaper sheet
            return hit.collider.GetComponentInParent<DraggableCyanotypePaper>() != null;
        }

        private bool TryFindTrayInSight(out CyanotypeDipTray tray)
        {
            tray = null;
            Ray ray = ActiveAimRay();
            if (!Physics.Raycast(ray, out RaycastHit hit, _maxReachWorld, _trayMask, QueryTriggerInteraction.Collide))
                return false;

            tray = hit.collider.GetComponentInParent<CyanotypeDipTray>();
            return tray != null;
        }

        private Ray ActiveAimRay()
        {
            if (Cursor.lockState == CursorLockMode.Locked)
                return _playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

            return _playerCamera.ScreenPointToRay(ReadMouseScreenPos());
        }

        private static bool IsLiquidApplicator(CyanotypeToolRole role)
        {
            return role == CyanotypeToolRole.SensitizerApplicator
                || role == CyanotypeToolRole.WaterWashApplicator
                || role == CyanotypeToolRole.AlkaliBleachBath
                || role == CyanotypeToolRole.TanninTonerBath
                || role == CyanotypeToolRole.PeroxideOxidizerBath;
        }

        // --- Cross-Backend Input Helpers (Legacy Input & New Input System) ---

        private static Vector2 ReadMouseScreenPos()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
            return Input.mousePosition;
#endif
        }

        private static bool ReadPrimaryPressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(0);
#endif
        }

        private static bool ReadPrimaryHeld()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.leftButton.isPressed;
#else
            return Input.GetMouseButton(0);
#endif
        }

        private static bool ReadSecondaryPressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(1);
#endif
        }

        private static bool ReadDipKeyPressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.E);
#endif
        }
    }
}
