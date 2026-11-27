using UnityEngine;

namespace Cyanotype.World
{
    /// <summary>
    /// Renders a 2D animated human NPC inside the 3D courtyard/studio using Y-axis cylindrical
    /// billboarding and relative viewing-angle directional states (0 = Front, 1 = Side, 2 = Back).
    /// </summary>
    public class Cyanotype2DNPCController : MonoBehaviour
    {
        [Header("Billboard & Facing References")]
        [SerializeField] private Transform _spriteBillboard;
        [SerializeField] private Transform _logicalFacingRoot;
        [SerializeField] private SpriteRenderer _spriteRenderer;
        [SerializeField] private Animator _animator;

        [Header("Optional Static Directional Sprites (Used When No AnimatorController Is Assigned)")]
        [SerializeField] private Sprite _frontSprite;
        [SerializeField] private Sprite _sideSprite;
        [SerializeField] private Sprite _backSprite;

        [Header("State & Dialogue")]
        [SerializeField] private bool _isTalking;
        [SerializeField] private float _talkDuration = 3.0f;

        private Camera _mainCamera;
        private Vector3 _lastWorldPosition;
        private float _talkTimer;

        private static readonly int HashMoveSpeed = Animator.StringToHash("MoveSpeed");
        private static readonly int HashViewDir = Animator.StringToHash("ViewDir");
        private static readonly int HashIsTalking = Animator.StringToHash("IsTalking");
        private static readonly int HashInteractTrigger = Animator.StringToHash("InteractTrigger");

        public bool IsTalking => _isTalking;

        private bool HasPlayableAnimator =>
            _animator != null && _animator.isActiveAndEnabled && _animator.runtimeAnimatorController != null;

        private void Awake()
        {
            if (_logicalFacingRoot == null) _logicalFacingRoot = transform;
            if (_animator == null) _animator = GetComponent<Animator>();

            if (_spriteBillboard == null)
            {
                Transform child = transform.Find("SpriteBillboard");
                if (child != null) _spriteBillboard = child;
            }

            if (_spriteRenderer == null)
            {
                _spriteRenderer = _spriteBillboard != null
                    ? _spriteBillboard.GetComponent<SpriteRenderer>()
                    : GetComponentInChildren<SpriteRenderer>();
            }

            if (_spriteBillboard == null && _spriteRenderer != null)
            {
                _spriteBillboard = _spriteRenderer.transform;
            }

            _lastWorldPosition = transform.position;
        }

        private void LateUpdate()
        {
            if (_mainCamera == null) _mainCamera = Camera.main;

            if (_isTalking && _talkTimer > 0f)
            {
                _talkTimer -= Time.deltaTime;
                if (_talkTimer <= 0f)
                {
                    _isTalking = false;
                }
            }

            float dt = Mathf.Max(Time.deltaTime, 1e-5f);
            Vector3 displacement = transform.position - _lastWorldPosition;
            displacement.y = 0f;
            float planarSpeed = displacement.magnitude / dt;
            _lastWorldPosition = transform.position;

            int viewDir = 0;
            bool flipX = false;

            if (_mainCamera != null)
            {
                Vector3 toCamera = _mainCamera.transform.position - _logicalFacingRoot.position;
                toCamera.y = 0f;

                if (toCamera.sqrMagnitude > 1e-5f)
                {
                    Vector3 camDir = toCamera.normalized;

                    // Cylindrical Y-axis billboard facing the camera
                    if (_spriteBillboard != null)
                    {
                        _spriteBillboard.rotation = Quaternion.LookRotation(camDir, Vector3.up);
                    }

                    // Compute relative angle between NPC logical forward and viewer direction
                    Vector3 npcForward = _logicalFacingRoot.forward;
                    npcForward.y = 0f;
                    if (npcForward.sqrMagnitude < 1e-5f) npcForward = Vector3.forward;
                    npcForward.Normalize();

                    float signedAngle = Vector3.SignedAngle(npcForward, camDir, Vector3.up);
                    float absAngle = Mathf.Abs(signedAngle);

                    if (absAngle <= 45f)
                    {
                        viewDir = 0; // Front
                    }
                    else if (absAngle >= 135f)
                    {
                        viewDir = 2; // Back
                    }
                    else
                    {
                        viewDir = 1; // Side
                        flipX = signedAngle < 0f;
                    }
                }
            }

            if (_spriteRenderer != null)
            {
                _spriteRenderer.flipX = flipX;

                if (!HasPlayableAnimator)
                {
                    Sprite fallback = viewDir switch
                    {
                        1 => _sideSprite != null ? _sideSprite : _frontSprite,
                        2 => _backSprite != null ? _backSprite : _frontSprite,
                        _ => _frontSprite
                    };
                    if (fallback != null) _spriteRenderer.sprite = fallback;
                }
            }

            if (HasPlayableAnimator)
            {
                _animator.SetFloat(HashMoveSpeed, planarSpeed);
                _animator.SetInteger(HashViewDir, viewDir);
                _animator.SetBool(HashIsTalking, _isTalking);
            }
        }

        public void SetTalking(bool talking, float duration = 0f)
        {
            _isTalking = talking;
            _talkTimer = talking ? duration : 0f;

            if (HasPlayableAnimator)
            {
                _animator.SetBool(HashIsTalking, _isTalking);
            }
        }

        public void TriggerConversation()
        {
            TriggerConversation(null);
        }

        public void TriggerConversation(CyanotypeWorldInteractor interactor)
        {
            SetTalking(true, _talkDuration);
            if (HasPlayableAnimator)
            {
                _animator.SetTrigger(HashInteractTrigger);
            }
        }

        public void OnInteract()
        {
            TriggerConversation(null);
        }
    }
}
