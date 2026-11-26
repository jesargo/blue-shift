using UnityEngine;
using UnityEngine.UI;

namespace Cyanotype.World
{
    /// <summary>
    /// Reads CyanotypeWorldInteractor.FocusedItem and drives both a screen-space
    /// center reticle/prompt and a world-space floating marker above the target.
    /// </summary>
    public class CyanotypeWorldPromptHUD : MonoBehaviour
    {
        [Header("Core References")]
        [SerializeField] private CyanotypeWorldInteractor _worldInteractor;
        [SerializeField] private CyanotypeFirstPersonRigMode _rigMode;
        [SerializeField] private Camera _playerCamera;

        [Header("Screen-Space Prompt UI (Overlay Canvas)")]
        [SerializeField] private CanvasGroup _screenPromptGroup;
        [SerializeField] private Image _screenReticleDot;
        [SerializeField] private Image _screenContextIcon;
        [SerializeField] private Text _screenActionLabel;
        [SerializeField] private Text _screenKeyHintLabel;
        [SerializeField] private string _interactKeyText = "E";

        [Header("World-Space Prompt UI (Floating Target Marker)")]
        [SerializeField] private Transform _worldPromptRoot;
        [SerializeField] private CanvasGroup _worldPromptGroup;
        [SerializeField] private Image _worldContextIcon;
        [SerializeField] private float _worldVerticalOffset = 0.14f;
        [SerializeField] private float _bobAmplitude = 0.018f;
        [SerializeField] private float _bobFrequency = 2.6f;
        [SerializeField] private float _worldFollowSharpness = 22f;
        [SerializeField] private float _distanceScaleFactor = 0.45f;
        [SerializeField] private float _minWorldScale = 0.35f;
        [SerializeField] private float _maxWorldScale = 1.15f;

        [Header("Context Icons & Accent Colors")]
        [SerializeField] private Sprite _gatherBotanicalIcon;
        [SerializeField] private Sprite _pickUpRockIcon;
        [SerializeField] private Sprite _talkNpcIcon;
        [SerializeField] private Sprite _cyanotypeWorkbenchIcon;

        [SerializeField] private Color _gatherAccent = new Color(0.56f, 0.82f, 0.42f, 1f);
        [SerializeField] private Color _rockAccent = new Color(0.76f, 0.74f, 0.68f, 1f);
        [SerializeField] private Color _talkAccent = new Color(0.95f, 0.82f, 0.44f, 1f);
        [SerializeField] private Color _workbenchAccent = new Color(0.24f, 0.56f, 0.88f, 1f);
        [SerializeField] private Color _idleReticleColor = new Color(1f, 1f, 1f, 0.45f);

        [Header("Transition Timing")]
        [SerializeField] private float _fadeSpeed = 14f;

        private WorldInteractableItem _lastFocusedItem;
        private Vector3 _smoothedWorldPos;
        private float _currentAlpha;

        private void Awake()
        {
            if (_worldInteractor == null) _worldInteractor = GetComponent<CyanotypeWorldInteractor>();
            if (_rigMode == null) _rigMode = GetComponent<CyanotypeFirstPersonRigMode>();
            if (_playerCamera == null)
            {
                _playerCamera = GetComponentInChildren<Camera>();
                if (_playerCamera == null) _playerCamera = Camera.main;
            }

            if (_screenKeyHintLabel != null)
            {
                _screenKeyHintLabel.text = _interactKeyText;
            }

            SetGroupAlpha(_screenPromptGroup, 0f);
            SetGroupAlpha(_worldPromptGroup, 0f);
        }

        private void LateUpdate()
        {
            if (_worldInteractor == null || _playerCamera == null) return;

            // Hide exploration prompts while working in ActiveCyanotypeArmsOnly mode
            bool inActiveBenchMode = _rigMode != null
                && _rigMode.CurrentMode == PlayerPresentationMode.ActiveCyanotypeArmsOnly;

            WorldInteractableItem focused = (!inActiveBenchMode && _worldInteractor.enabled)
                ? _worldInteractor.FocusedItem
                : null;

            bool hasTarget = focused != null;

            if (hasTarget && focused != _lastFocusedItem)
            {
                ApplyContextVisuals(focused);
                // Snap immediately on target acquisition so marker doesn't fly across screen
                _smoothedWorldPos = ComputeTargetTopWorldPos(focused);
            }
            _lastFocusedItem = focused;

            // Smooth alpha fade for both screen-space and world-space groups
            float targetAlpha = hasTarget ? 1f : 0f;
            _currentAlpha = Mathf.MoveTowards(_currentAlpha, targetAlpha, _fadeSpeed * Time.deltaTime);

            SetGroupAlpha(_screenPromptGroup, _currentAlpha);
            SetGroupAlpha(_worldPromptGroup, _currentAlpha);

            if (_screenReticleDot != null)
            {
                _screenReticleDot.color = hasTarget
                    ? ResolveAccentColor(focused.Kind)
                    : _idleReticleColor;
            }

            if (hasTarget && _worldPromptRoot != null)
            {
                UpdateWorldPromptTransform(focused);
            }
        }

        private void ApplyContextVisuals(WorldInteractableItem item)
        {
            Sprite icon = ResolveIcon(item.Kind);
            Color accent = ResolveAccentColor(item.Kind);
            string label = string.IsNullOrWhiteSpace(item.PromptLabel)
                ? ResolveDefaultLabel(item.Kind)
                : item.PromptLabel;

            if (_screenContextIcon != null)
            {
                _screenContextIcon.sprite = icon;
                _screenContextIcon.color = accent;
                _screenContextIcon.enabled = (icon != null);
            }

            if (_screenActionLabel != null)
            {
                _screenActionLabel.text = label;
            }

            if (_worldContextIcon != null)
            {
                _worldContextIcon.sprite = icon;
                _worldContextIcon.color = accent;
                _worldContextIcon.enabled = (icon != null);
            }
        }

        private void UpdateWorldPromptTransform(WorldInteractableItem item)
        {
            Vector3 desiredPos = ComputeTargetTopWorldPos(item);
            float t = 1f - Mathf.Exp(-_worldFollowSharpness * Time.deltaTime);
            _smoothedWorldPos = Vector3.Lerp(_smoothedWorldPos, desiredPos, t);

            float bob = Mathf.Sin(Time.time * _bobFrequency) * _bobAmplitude;
            _worldPromptRoot.position = _smoothedWorldPos + Vector3.up * bob;

            // Billboard toward player camera
            Vector3 toCam = _playerCamera.transform.position - _worldPromptRoot.position;
            if (toCam.sqrMagnitude > 1e-5f)
            {
                _worldPromptRoot.rotation = Quaternion.LookRotation(-toCam.normalized, Vector3.up);
            }

            // Distance-compensated scale so icons stay readable on both small plants and 1.72m NPCs
            float dist = Vector3.Distance(_playerCamera.transform.position, _worldPromptRoot.position);
            float scale = Mathf.Clamp(dist * _distanceScaleFactor, _minWorldScale, _maxWorldScale);
            _worldPromptRoot.localScale = Vector3.one * scale;
        }

        private Vector3 ComputeTargetTopWorldPos(WorldInteractableItem item)
        {
            if (item.TryGetComponent<Collider>(out var col))
            {
                Bounds b = col.bounds;
                return new Vector3(b.center.x, b.max.y + _worldVerticalOffset, b.center.z);
            }

            var childCol = item.GetComponentInChildren<Collider>();
            if (childCol != null)
            {
                Bounds b = childCol.bounds;
                return new Vector3(b.center.x, b.max.y + _worldVerticalOffset, b.center.z);
            }

            return item.transform.position + Vector3.up * (_worldVerticalOffset + 0.25f);
        }

        private Sprite ResolveIcon(WorldInteractableKind kind)
        {
            return kind switch
            {
                WorldInteractableKind.ProceduralBotanical => _gatherBotanicalIcon,
                WorldInteractableKind.ProceduralRock => _pickUpRockIcon,
                WorldInteractableKind.HumanNPC2D => _talkNpcIcon,
                WorldInteractableKind.CyanotypeWorkbench => _cyanotypeWorkbenchIcon,
                _ => null
            };
        }

        private Color ResolveAccentColor(WorldInteractableKind kind)
        {
            return kind switch
            {
                WorldInteractableKind.ProceduralBotanical => _gatherAccent,
                WorldInteractableKind.ProceduralRock => _rockAccent,
                WorldInteractableKind.HumanNPC2D => _talkAccent,
                WorldInteractableKind.CyanotypeWorkbench => _workbenchAccent,
                _ => Color.white
            };
        }

        private static string ResolveDefaultLabel(WorldInteractableKind kind)
        {
            return kind switch
            {
                WorldInteractableKind.ProceduralBotanical => "Gather Specimen",
                WorldInteractableKind.ProceduralRock => "Pick Up Stone",
                WorldInteractableKind.HumanNPC2D => "Speak",
                WorldInteractableKind.CyanotypeWorkbench => "Use Cyanotype Bench",
                _ => "Interact"
            };
        }

        private static void SetGroupAlpha(CanvasGroup group, float alpha)
        {
            if (group == null) return;
            group.alpha = alpha;
            bool active = alpha > 0.01f;
            if (group.gameObject.activeSelf != active)
            {
                group.gameObject.SetActive(active);
            }
        }

#if UNITY_EDITOR
        public void AssignGeneratedHierarchy(
            CanvasGroup screenGroup,
            Image reticleDot,
            Image screenIcon,
            Text actionLabel,
            Text keyHintLabel,
            Transform worldRoot,
            CanvasGroup worldGroup,
            Image worldIcon,
            Sprite gatherIcon,
            Sprite rockIcon,
            Sprite talkIcon,
            Sprite workbenchIcon)
        {
            _screenPromptGroup = screenGroup;
            _screenReticleDot = reticleDot;
            _screenContextIcon = screenIcon;
            _screenActionLabel = actionLabel;
            _screenKeyHintLabel = keyHintLabel;
            _worldPromptRoot = worldRoot;
            _worldPromptGroup = worldGroup;
            _worldContextIcon = worldIcon;
            _gatherBotanicalIcon = gatherIcon;
            _pickUpRockIcon = rockIcon;
            _talkNpcIcon = talkIcon;
            _cyanotypeWorkbenchIcon = workbenchIcon;
        }
#endif
    }
}