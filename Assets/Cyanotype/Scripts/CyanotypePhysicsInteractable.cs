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
        [SerializeField] private CyanotypeFluidController.ApplicatorShape _applicatorShape = CyanotypeFluidController.ApplicatorShape.HakeBrush;
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
        private CyanotypeFluidController _fluidController;
        private CyanotypeEnvironmentRig _envRig;
        private bool _isHeld;
        private bool _isStrokeCommanded;
        private bool _isContactActive;
        private int _lastAnimEventFrame = -1;

        // Cached Animator parameter hashes
        private static readonly int HashIsHeld = Animator.StringToHash("IsHeld");
        private static readonly int HashIsActive = Animator.StringToHash("IsActive");
        private static readonly int HashWetLoad = Animator.StringToHash("WetLoad");
        private static readonly int HashActionTrigger = Animator.StringToHash("ActionTrigger");

        // Optional global shader uniforms for custom diegetic/VFX shaders
        private static readonly int PropContactUV = Shader.PropertyToID("_BrushContactUV");
        private static readonly int PropContactRadius = Shader.PropertyToID("_BrushContactRadius");
        private static readonly int PropDepositDelta = Shader.PropertyToID("_BrushDepositDelta");
        private static readonly int PropActiveChannel = Shader.PropertyToID("_BrushActiveChannel");

        public CyanotypeToolRole Role => _role;
        public CyanotypeFluidController.ApplicatorShape ApplicatorShape => _applicatorShape;
        public float WetLoad => _wetLoad;
        public float Base10OpticalDensity => _base10OpticalDensity;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            if (_contactTip == null) _contactTip = transform;

            if (_applicatorShape == CyanotypeFluidController.ApplicatorShape.HakeBrush
                && gameObject.name.IndexOf("rod", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _applicatorShape = CyanotypeFluidController.ApplicatorShape.GlassCoatingRod;
            }
        }

        private void Start()
        {
            _fluidController = FindAnyObjectByType<CyanotypeFluidController>();
            _envRig = FindAnyObjectByType<CyanotypeEnvironmentRig>();
        }

        private bool HasPlayableAnimator =>
            _animator != null && _animator.isActiveAndEnabled && _animator.runtimeAnimatorController != null;

        private void Update()
        {
            if (HasPlayableAnimator)
            {
                _animator.SetBool(HashIsHeld, _isHeld);
                _animator.SetFloat(HashWetLoad, _wetLoad);
            }

            // Continuous fallback deposition when held and stroking, even if no AnimationEvent clip is attached
            if (_isHeld && _isStrokeCommanded && _lastAnimEventFrame != Time.frameCount)
            {
                TryApplyPaperPhysics(Time.deltaTime * 6.0f);
            }
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
            _isStrokeCommanded = active;
            if (HasPlayableAnimator)
                _animator.SetBool(HashIsActive, active);

            if (!active)
            {
                _isContactActive = false;
                Shader.SetGlobalVector(PropDepositDelta, Vector4.zero);
            }
        }

        // Called when dipping brush/rod into a stock solution or bath tray
        public void RefillWetLoad(float amount = 1f)
        {
            _wetLoad = Mathf.Clamp01(_wetLoad + amount);
            if (HasPlayableAnimator)
                _animator.SetTrigger(HashActionTrigger);
        }

        // --- AnimationEvent Callbacks (Injected by CyanotypeHybridAnimBuilderWindow) ---

        public void OnPhysicsContactBegin()
        {
            _isContactActive = true;
            _lastAnimEventFrame = Time.frameCount;
            TryApplyPaperPhysics(0.25f);
        }

        public void OnPhysicsDepositTick(float tickWeight)
        {
            if (!_isContactActive && !_isStrokeCommanded) return;
            _lastAnimEventFrame = Time.frameCount;
            TryApplyPaperPhysics(tickWeight > 0f ? tickWeight : 1f);
        }

        public void OnPhysicsContactEnd()
        {
            _isContactActive = false;
            _lastAnimEventFrame = Time.frameCount;
            Shader.SetGlobalVector(PropDepositDelta, Vector4.zero);
        }

        private void TryApplyPaperPhysics(float weight)
        {
            if (_role == CyanotypeToolRole.ShadowOrStencilProp || _role == CyanotypeToolRole.CarbonArcShutter)
                return;

            if (_wetLoad <= 0.001f) return;

            if (_fluidController == null)
                _fluidController = FindAnyObjectByType<CyanotypeFluidController>();
            if (_envRig == null)
                _envRig = FindAnyObjectByType<CyanotypeEnvironmentRig>();

            // Raycast from tool tip onto the cyanotype sheet
            Vector3 origin = _contactTip.position + Vector3.up * 0.06f;
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 0.20f, _paperLayerMask, QueryTriggerInteraction.Ignore);

            bool hitPaper = false;
            Vector2 uv = Vector2.zero;
            float radiusUV = 0.055f;

            for (int i = 0; i < hits.Length; i++)
            {
                var paper = hits[i].collider.GetComponentInParent<DraggableCyanotypePaper>();
                if (paper != null)
                {
                    Transform pt = paper.transform;
                    Vector3 localHit = pt.InverseTransformPoint(hits[i].point);
                    uv = new Vector2(Mathf.Clamp01(localHit.x + 0.5f), Mathf.Clamp01(localHit.y + 0.5f));
                    float minSpan = Mathf.Max(0.1f, Mathf.Min(Mathf.Abs(pt.lossyScale.x), Mathf.Abs(pt.lossyScale.y)));
                    radiusUV = Mathf.Clamp(_contactRadiusWorld / minSpan, 0.01f, 0.25f);
                    hitPaper = true;
                    break;
                }
            }

            if (!hitPaper) return;

            float amount = _depositionPerTick * weight * _wetLoad;

            int channelIndex = _role switch
            {
                CyanotypeToolRole.SensitizerApplicator => 0,
                CyanotypeToolRole.WaterWashApplicator  => 1,
                CyanotypeToolRole.AlkaliBleachBath     => 2,
                CyanotypeToolRole.TanninTonerBath      => 3,
                CyanotypeToolRole.PeroxideOxidizerBath => 4,
                _ => -1
            };

            if (channelIndex < 0) return;

            if (channelIndex == 0)
            {
                LockActiveRecipe();
            }

            // Route stroke into the live fluid/bath render texture simulation
            if (_fluidController != null)
            {
                float strengthScale = Mathf.Clamp(_wetLoad * (_depositionPerTick / 0.18f), 0.15f, 2.5f);
                _fluidController.SubmitExternalToolStroke(uv, radiusUV, strengthScale, _role, _applicatorShape);
            }

            // Also publish global shader uniforms for any auxiliary VFX/diegetic shaders
            Shader.SetGlobalVector(PropContactUV, new Vector4(uv.x, uv.y, 0f, 0f));
            Shader.SetGlobalFloat(PropContactRadius, _contactRadiusWorld);
            Shader.SetGlobalInt(PropActiveChannel, channelIndex);
            Shader.SetGlobalVector(PropDepositDelta, new Vector4(amount, 0f, 0f, 0f));

            _wetLoad = Mathf.Clamp01(_wetLoad - _depletionPerStroke * weight * 0.25f);
        }

        private void LockActiveRecipe()
        {
            if (_envRig == null)
                _envRig = FindAnyObjectByType<CyanotypeEnvironmentRig>();

            if (_envRig != null && _envRig.recipe != null)
                _envRig.recipe.locked = true;
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
