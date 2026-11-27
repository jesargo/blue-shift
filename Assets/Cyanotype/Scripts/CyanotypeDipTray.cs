using UnityEngine;
using UnityEngine.Events;

namespace Cyanotype.Gameplay
{
    /// <summary>
    /// Marks a studio dish or developing tray as a dip target for CyanotypePhysicsInteractable
    /// applicators (hake brush, glass coating rod, bath brushes).
    /// </summary>
    public class CyanotypeDipTray : MonoBehaviour
    {
        [Header("Tray Solution Identity")]
        [SerializeField] private CyanotypeToolRole _traySolutionRole = CyanotypeToolRole.SensitizerApplicator;
        [SerializeField] private Transform _liquidSurfacePoint;
        [SerializeField] private bool _allowCrossRoleRefill = false;

        [Header("Optional 2D/3D Ripple & UX Events")]
        [SerializeField] private Animator _liquidSurfaceAnimator;
        [SerializeField] private UnityEvent<CyanotypePhysicsInteractable> _onToolDipped;

        private static readonly int HashDipRipple = Animator.StringToHash("DipRipple");

        public CyanotypeToolRole TraySolutionRole => _traySolutionRole;

        public Vector3 GetDipWorldPoint()
        {
            return _liquidSurfacePoint != null ? _liquidSurfacePoint.position : transform.position;
        }

        public bool CanAcceptTool(CyanotypePhysicsInteractable tool)
        {
            if (tool == null) return false;
            return _allowCrossRoleRefill || tool.Role == _traySolutionRole;
        }

        public void OnToolDipped(CyanotypePhysicsInteractable tool)
        {
            if (tool == null) return;

            if (!CanAcceptTool(tool))
            {
                Debug.LogWarning($"[Cyanotype] {tool.name} ({tool.Role}) dipped into mismatched tray ({_traySolutionRole}).");
                return;
            }

            if (_liquidSurfaceAnimator != null)
            {
                _liquidSurfaceAnimator.SetTrigger(HashDipRipple);
            }

            _onToolDipped?.Invoke(tool);
        }
    }
}
