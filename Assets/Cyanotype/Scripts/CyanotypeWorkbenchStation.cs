using UnityEngine;
using UnityEngine.Events;
using Cyanotype.Gameplay;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Cyanotype.World
{
    /// <summary>
    /// Manages stepping into and out of ActiveCyanotypeArmsOnly mode at a 1900s studio workbench,
    /// safely returning held brushes/rods when returning to full-body 3D exploration.
    /// </summary>
    public class CyanotypeWorkbenchStation : MonoBehaviour
    {
        [Header("Optional Bench View Anchor")]
        [SerializeField] private Transform _benchStandPoint;
        [SerializeField] private bool _snapPlayerToStandPoint = true;

        [Header("Events")]
        [SerializeField] private UnityEvent _onBenchEntered;
        [SerializeField] private UnityEvent _onBenchExited;

        private CyanotypeFirstPersonRigMode _activePlayerRig;
        private CyanotypePlayerInteractionRig _activeBenchInteractor;

        public bool IsPlayerAtBench => _activePlayerRig != null
            && _activePlayerRig.CurrentMode == PlayerPresentationMode.ActiveCyanotypeArmsOnly;

        // Wire this to WorldInteractableItem._onInteracted on the workbench collider
        public void EnterWorkbench(CyanotypeWorldInteractor worldInteractor)
        {
            if (worldInteractor == null) return;

            _activePlayerRig = worldInteractor.GetComponent<CyanotypeFirstPersonRigMode>();
            _activeBenchInteractor = worldInteractor.GetComponent<CyanotypePlayerInteractionRig>();

            if (_snapPlayerToStandPoint && _benchStandPoint != null)
            {
                var cc = _activePlayerRig.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;

                _activePlayerRig.transform.position = new Vector3(
                    _benchStandPoint.position.x,
                    _activePlayerRig.transform.position.y,
                    _benchStandPoint.position.z);

                Vector3 flatForward = Vector3.ProjectOnPlane(_benchStandPoint.forward, Vector3.up).normalized;
                if (flatForward.sqrMagnitude > 1e-4f)
                {
                    _activePlayerRig.transform.rotation = Quaternion.LookRotation(flatForward, Vector3.up);
                }

                if (cc != null) cc.enabled = true;
            }

            if (_activePlayerRig != null)
            {
                _activePlayerRig.SetMode(PlayerPresentationMode.ActiveCyanotypeArmsOnly);
            }

            _onBenchEntered?.Invoke();
        }

        public void ExitWorkbench()
        {
            if (_activeBenchInteractor != null && _activeBenchInteractor.IsHoldingTool)
            {
                _activeBenchInteractor.ReleaseHeldTool(returnToRestPose: true);
            }

            if (_activePlayerRig != null)
            {
                _activePlayerRig.SetMode(PlayerPresentationMode.ExplorationFullBody);
            }

            _activePlayerRig = null;
            _activeBenchInteractor = null;
            _onBenchExited?.Invoke();
        }

        private void Update()
        {
            if (!IsPlayerAtBench) return;

            if (ReadExitBenchPressed())
            {
                ExitWorkbench();
            }
        }

        private static bool ReadExitBenchPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null
                && (Keyboard.current.tabKey.wasPressedThisFrame || Keyboard.current.escapeKey.wasPressedThisFrame);
#else
            return Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.Escape);
#endif
        }
    }
}