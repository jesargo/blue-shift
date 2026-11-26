using UnityEngine;

namespace Cyanotype.Gameplay
{
    public enum CyanotypeToolRole
    {
        SensitizerApplicator,   // Hake brush / glass rod -> writes _FluidTex.B & locks CyanotypeRecipe
        WaterWashApplicator,    // Rinse bottle / tray dip -> advances _FluidTex.R & .A (washed)
        AlkaliBleachBath,       // Soda ash bath/brush -> writes _BathTex.R (requires washed > 0.35)
        TanninTonerBath,        // Tannic acid bath/brush -> writes _BathTex.G (gated by alkali bleach)
        PeroxideOxidizerBath,   // Peroxide bath/mist -> writes _BathTex.B
        ShadowOrStencilProp,    // 3D prop or 2D botanical cutout -> height & base-10 OD shadow casting
        CarbonArcShutter        // Modulates artificial carbon-arc exposure beam
    }

    [RequireComponent(typeof(Animator))]
    public class CyanotypePhysicsInteractable : MonoBehaviour
    {
        [Header("Role & Contact Geometry")]
        [SerializeField] private CyanotypeToolRole _role = CyanotypeToolRole.SensitizerApplicator;
        [SerializeField] private Transform _contactTip;
        [SerializeField] private float _contactRadiusWorld = 0.035f;
        [SerializeField] private float _depositionPerTick = 0.18f;
        [SerializeField] private LayerMask _paperLayerMask = ~0;

        [Header("Base-10 Optical Density (Stencils / 2D Cutouts)")]
        [SerializeField] private float _base10OpticalDensity = 1.05f;

        [Header("Tool Wet-Load State")]
        [Range(0f, 1f)] [SerializeField] private float _wetLoad = 1.0f;
        [SerializeField] private float _depletionPerStroke = 0.04f;

        private Animator _animator;
        private bool _isHeld;
        private bool _isContactActive;

        // Cached Animator parameter hashes
        private static readonly int HashIsHeld = Animator.StringToHash("IsHeld");
        private static readonly int HashIsActive = Animator.StringToHash("IsActive");
        private static readonly int HashWetLoad = Animator.StringToHash("WetLoad");
        private static readonly int HashActionTrigger = Animator.StringToHash("ActionTrigger");

        // Shader property IDs matching CyanotypeSimMultiSource / CyanotypeEnvironmentRig
        private static readonly int PropContactUV = Shader.PropertyToID("_BrushContactUV");
        private static readonly int PropContactRadius = Shader.PropertyToID("_BrushContactRadius");
        private static readonly int PropDepositDelta = Shader.PropertyToID("_BrushDepositDelta");
        private static readonly int PropActiveChannel = Shader.PropertyToID("_BrushActiveChannel");

        public CyanotypeToolRole Role => _role;
        public float WetLoad => _wetLoad;
        public float Base10OpticalDensity => _base10OpticalDensity;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            if (_contactTip == null) _contactTip = transform;
        }

        private void Update()
        {
            _animator.SetBool(HashIsHeld, _isHeld);
            _animator.SetFloat(HashWetLoad, _wetLoad);
        }

        // Called by player interaction controller on pickup / drop
        public void SetHeld(bool held)
        {
            _isHeld = held;
            if (!held) SetStrokeActive(false);
        }

        // Called by player primary use button (mouse/trigger)
        public void SetStrokeActive(bool active)
        {
            _animator.SetBool(HashIsActive, active);
            if (!active) _isContactActive = false;
        }

        // Called when dipping brush/rod into a stock solution or bath tray
        public void RefillWetLoad(float amount = 1f)
        {
            _wetLoad = Mathf.Clamp01(_wetLoad + amount);
            _animator.SetTrigger(HashActionTrigger);
        }

        // --- AnimationEvent Callbacks (Injected by CyanotypeHybridAnimBuilderWindow) ---

        public void OnPhysicsContactBegin()
        {
            _isContactActive = true;
            TryApplyPaperPhysics(0.25f);
        }

        public void OnPhysicsDepositTick(float tickWeight)
        {
            if (!_isContactActive) return;
            TryApplyPaperPhysics(tickWeight > 0f ? tickWeight : 1f);
        }

        public void OnPhysicsContactEnd()
        {
            _isContactActive = false;
            Shader.SetGlobalVector(PropDepositDelta, Vector4.zero);
        }

        private void TryApplyPaperPhysics(float weight)
        {
            if (_role == CyanotypeToolRole.ShadowOrStencilProp || _role == CyanotypeToolRole.CarbonArcShutter)
                return;

            if (_wetLoad <= 0.001f) return;

            // Raycast from tool tip onto the cyanotype sheet to find normalized UV
            Vector3 origin = _contactTip.position + Vector3.up * 0.05f;
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 0.15f, _paperLayerMask))
                return;

            Vector2 uv = hit.textureCoord;
            float amount = _depositionPerTick * weight * _wetLoad;

            // Channel routing matching State 1 / _FluidTex / _BathTex layout:
            // 0 = Sensitizer (_FluidTex.B) -> locks recipe on first contact
            // 1 = Water Wash (_FluidTex.R & .A)
            // 2 = Alkali Bleach (_BathTex.R)
            // 3 = Tannin Toner (_BathTex.G)
            // 4 = Peroxide Oxidizer (_BathTex.B)
            int channelIndex = _role switch
            {
                CyanotypeToolRole.SensitizerApplicator => 0,
                CyanotypeToolRole.WaterWashApplicator => 1,
                CyanotypeToolRole.AlkaliBleachBath => 2,
                CyanotypeToolRole.TanninTonerBath => 3,
                CyanotypeToolRole.PeroxideOxidizerBath => 4,
                _ => -1
            };

            if (channelIndex < 0) return;

            // Lock the active CyanotypeRecipe on the very first sensitizer stroke
            // so cFAC and cKFC bake immutably into State 2 (G, B)
            if (channelIndex == 0)
            {
                LockActiveRecipe();
            }

            Shader.SetGlobalVector(PropContactUV, new Vector4(uv.x, uv.y, 0f, 0f));
            Shader.SetGlobalFloat(PropContactRadius, _contactRadiusWorld);
            Shader.SetGlobalInt(PropActiveChannel, channelIndex);
            Shader.SetGlobalVector(PropDepositDelta, new Vector4(amount, 0f, 0f, 0f));

            _wetLoad = Mathf.Clamp01(_wetLoad - _depletionPerStroke * weight);
        }

        private void LockActiveRecipe()
        {
            // Uses SendMessageUpwards/Broadcast so it integrates cleanly with CyanotypeEnvironmentRig
            // without tight coupling if rig lives on a parent studio root.
            var rig = FindFirstObjectByType<MonoBehaviour>();
            if (rig != null)
            {
                gameObject.SendMessage("OnFirstSensitizerStroke", SendMessageOptions.DontRequireReceiver);
            }
        }

#if UNITY_EDITOR
        public void ConfigureFromEditor(CyanotypeToolRole role, float radiusWorld, float depositPerTick, float base10OD)
        {
            _role = role;
            _contactRadiusWorld = radiusWorld;
            _depositionPerTick = depositPerTick;
            _base10OpticalDensity = Mathf.Clamp(base10OD, 0f, 4f);
        }
#endif
    }
}