using UnityEngine;
using UnityEngine.UI;

namespace Cyanotype.Gameplay
{
    public class CyanotypeDiegeticUXBinder : MonoBehaviour
    {
        [Header("Linked Tool & Visual Targets")]
        [SerializeField] private CyanotypePhysicsInteractable _interactable;
        [SerializeField] private SpriteRenderer _worldSpriteIndicator;
        [SerializeField] private Image _worldCanvasFillBar;

        [Header("Process Stage Palette (Unwashed -> Washed -> Bleached/Toned)")]
        [SerializeField] private Color _sensitizerChartreuse = new Color(0.76f, 0.82f, 0.24f, 0.9f);
        [SerializeField] private Color _washedPrussianBlue = new Color(0.06f, 0.22f, 0.44f, 0.9f);
        [SerializeField] private Color _alkaliOchre = new Color(0.72f, 0.54f, 0.24f, 0.9f);
        [SerializeField] private Color _tanninSepia = new Color(0.28f, 0.20f, 0.16f, 0.9f);
        [SerializeField] private Color _lockedGatingWarning = new Color(0.85f, 0.25f, 0.20f, 0.9f);

        private bool _isPrintWashed;

        // Hooked via UnityEvent from WaterTrayTrigger when print crosses washed > 0.35
        public void NotifyPrintWashedState(bool isWashed)
        {
            _isPrintWashed = isWashed;
        }

        private void LateUpdate()
        {
            if (_interactable == null) return;

            float load = _interactable.WetLoad;
            Color tint = ResolveDiegeticColor();

            if (_worldCanvasFillBar != null)
            {
                _worldCanvasFillBar.fillAmount = load;
                _worldCanvasFillBar.color = tint;
            }

            if (_worldSpriteIndicator != null)
            {
                tint.a = Mathf.Lerp(0.25f, 0.95f, load);
                _worldSpriteIndicator.color = tint;
            }

            // Keep world-space 2D UX facing the active player camera
            if (Camera.main != null)
            {
                transform.rotation = Quaternion.LookRotation(transform.position - Camera.main.transform.position);
            }
        }

        private Color ResolveDiegeticColor()
        {
            switch (_interactable.Role)
            {
                case CyanotypeToolRole.SensitizerApplicator:
                    return _sensitizerChartreuse;
                case CyanotypeToolRole.WaterWashApplicator:
                    return _washedPrussianBlue;
                case CyanotypeToolRole.AlkaliBleachBath:
                    // Warn player diegetically if attempting to bleach before washing (washed <= 0.35)
                    return _isPrintWashed ? _alkaliOchre : _lockedGatingWarning;
                case CyanotypeToolRole.TanninTonerBath:
                    // Tannin requires washed print + alkali-vacated mordant sites
                    return _isPrintWashed ? _tanninSepia : _lockedGatingWarning;
                default:
                    return Color.white;
            }
        }
    }
}