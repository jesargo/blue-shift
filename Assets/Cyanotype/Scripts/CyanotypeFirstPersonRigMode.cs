using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;

namespace Cyanotype.World
{
    public enum PlayerPresentationMode
    {
        ExplorationFullBody,      // Full 3D body visible (head casts shadows only) + world interaction
        ActiveCyanotypeArmsOnly   // Torso/legs hidden so arms work cleanly over 0.89m sheet & trays
    }

    public class CyanotypeFirstPersonRigMode : MonoBehaviour
    {
        [Header("3D Player Character Renderers")]
        [Tooltip("Head/hat meshes: kept in ShadowsOnly during 1st-person so camera never clips.")]
        [SerializeField] private Renderer[] _headRenderers;
        [Tooltip("Torso, coat, and legs visible when looking down in Exploration mode.")]
        [SerializeField] private Renderer[] _torsoAndLegRenderers;
        [Tooltip("Dedicated first-person arms/hands shown in Active Cyanotype mode.")]
        [SerializeField] private Renderer[] _firstPersonArmRenderers;

        [Header("Mode-Coupled Interaction Scripts")]
        [SerializeField] private MonoBehaviour _worldInteractor;          // CyanotypeWorldInteractor
        [SerializeField] private MonoBehaviour _cyanotypeBenchInteractor; // CyanotypePlayerInteractionRig

        [Header("Events")]
        [SerializeField] private UnityEvent<PlayerPresentationMode> _onModeChanged;

        [SerializeField] private PlayerPresentationMode _currentMode = PlayerPresentationMode.ExplorationFullBody;

        public PlayerPresentationMode CurrentMode => _currentMode;

        private void Start()
        {
            ApplyMode(_currentMode, forceNotify: true);
        }

        public void SetMode(PlayerPresentationMode newMode)
        {
            if (_currentMode == newMode) return;
            ApplyMode(newMode, forceNotify: true);
        }

        public void ToggleMode()
        {
            SetMode(_currentMode == PlayerPresentationMode.ExplorationFullBody
                ? PlayerPresentationMode.ActiveCyanotypeArmsOnly
                : PlayerPresentationMode.ExplorationFullBody);
        }

        private void ApplyMode(PlayerPresentationMode mode, bool forceNotify)
        {
            _currentMode = mode;
            bool isArmsOnly = (mode == PlayerPresentationMode.ActiveCyanotypeArmsOnly);

            // Head never renders directly to 1st-person camera, but keeps 3D shadow in exploration
            SetRendererGroupState(_headRenderers, visible: false, castShadowWhenHidden: !isArmsOnly);

            // Torso & legs hide during close-up workbench coating/washing
            SetRendererGroupState(_torsoAndLegRenderers, visible: !isArmsOnly, castShadowWhenHidden: false);

            // First-person arms activate for bench tools (or stay active if shared)
            SetRendererGroupState(_firstPersonArmRenderers, visible: true, castShadowWhenHidden: false);

            if (_worldInteractor != null) _worldInteractor.enabled = !isArmsOnly;
            if (_cyanotypeBenchInteractor != null) _cyanotypeBenchInteractor.enabled = isArmsOnly;

            if (forceNotify) _onModeChanged?.Invoke(_currentMode);
        }

        private static void SetRendererGroupState(Renderer[] renderers, bool visible, bool castShadowWhenHidden)
        {
            if (renderers == null) return;
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;

                if (visible)
                {
                    r.enabled = true;
                    r.shadowCastingMode = ShadowCastingMode.On;
                }
                else if (castShadowWhenHidden)
                {
                    r.enabled = true;
                    r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                }
                else
                {
                    r.enabled = false;
                }
            }
        }
    }
}