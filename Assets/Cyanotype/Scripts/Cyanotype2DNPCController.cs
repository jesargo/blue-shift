using UnityEngine;
using UnityEngine.Events;

namespace Cyanotype.World
{
    [RequireComponent(typeof(Animator))]
    public class Cyanotype2DNPCController : MonoBehaviour
    {
        [Header("2D Visual & Facing Root")]
        [SerializeField] private SpriteRenderer _spriteRenderer;
        [SerializeField] private Transform _logicalFacingRoot; // Rotates in 3D for movement/look direction
        [SerializeField] private bool _lockVerticalTilt = true;

        [Header("NPC Identity & Dialogue")]
        [SerializeField] private string _npcDisplayName = "Town Botanist";
        [TextArea(2, 4)] [SerializeField] private string _greetingLine = "Have you gathered any fresh fern fronds from the ridge?";
        [SerializeField] private UnityEvent _onTalkStarted;

        private Animator _animator;
        private Transform _cameraTransform;
        private bool _isTalking;
        private float _moveSpeed;

        private static readonly int HashSpeed = Animator.StringToHash("MoveSpeed");
        private static readonly int HashViewDir = Animator.StringToHash("ViewDir"); // 0: Front, 1: Side, 2: Back
        private static readonly int HashIsTalking = Animator.StringToHash("IsTalking");
        private static readonly int HashInteractTrigger = Animator.StringToHash("InteractTrigger");

        public string DisplayName => _npcDisplayName;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            if (_spriteRenderer == null) _spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            if (_logicalFacingRoot == null) _logicalFacingRoot = transform;
            if (Camera.main != null) _cameraTransform = Camera.main.transform;
        }

        public void SetMoveSpeed(float worldSpeed)
        {
            _moveSpeed = worldSpeed;
        }

        public void TriggerConversation()
        {
            _isTalking = !_isTalking;
            _animator.SetTrigger(HashInteractTrigger);

            // Turn logical facing toward player when conversing
            if (_isTalking && _cameraTransform != null)
            {
                Vector3 toPlayer = _cameraTransform.position - _logicalFacingRoot.position;
                toPlayer.y = 0f;
                if (toPlayer.sqrMagnitude > 1e-4f)
                {
                    _logicalFacingRoot.rotation = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
                }
            }

            if (_isTalking)
            {
                Debug.Log($"[{_npcDisplayName}]: {_greetingLine}");
                _onTalkStarted?.Invoke();
            }
        }

        private void LateUpdate()
        {
            if (_cameraTransform == null)
            {
                if (Camera.main != null) _cameraTransform = Camera.main.transform;
                else return;
            }

            // 1. Y-Axis Cylindrical Billboard for the 2D SpriteRenderer
            if (_spriteRenderer != null)
            {
                Vector3 toCam = _cameraTransform.position - _spriteRenderer.transform.position;
                if (_lockVerticalTilt) toCam.y = 0f;

                if (toCam.sqrMagnitude > 1e-5f)
                {
                    _spriteRenderer.transform.rotation = Quaternion.LookRotation(-toCam.normalized, Vector3.up);
                }

                // 2. Compute relative view sector (Front / Side / Back + Left/Right flip)
                Vector3 npcForward = _logicalFacingRoot.forward;
                npcForward.y = 0f;
                npcForward.Normalize();

                Vector3 dirToPlayer = (_cameraTransform.position - _logicalFacingRoot.position);
                dirToPlayer.y = 0f;
                dirToPlayer.Normalize();

                float dot = Vector3.Dot(npcForward, dirToPlayer);
                float crossY = Vector3.Cross(npcForward, dirToPlayer).y;

                int viewDir;
                if (dot > 0.5f)
                {
                    viewDir = 0; // Front
                    _spriteRenderer.flipX = false;
                }
                else if (dot < -0.5f)
                {
                    viewDir = 2; // Back
                    _spriteRenderer.flipX = false;
                }
                else
                {
                    viewDir = 1; // Side profile
                    _spriteRenderer.flipX = (crossY < 0f);
                }

                _animator.SetInteger(HashViewDir, viewDir);
            }

            _animator.SetFloat(HashSpeed, _moveSpeed);
            _animator.SetBool(HashIsTalking, _isTalking);
        }
    }
}