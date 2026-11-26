using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Cyanotype.World
{
    public class CyanotypeWorldInteractor : MonoBehaviour
    {
        [SerializeField] private Camera _playerCamera;
        [SerializeField] private float _interactDistance = 2.4f;
        [SerializeField] private LayerMask _worldInteractMask = ~0;
        [SerializeField] private Transform _carrySocket;

        private GameObject _carriedObject;
        private WorldInteractableItem _focusedItem;

        public WorldInteractableItem FocusedItem => _focusedItem;

        private void Awake()
        {
            if (_playerCamera == null) _playerCamera = GetComponentInChildren<Camera>();
            if (_carrySocket == null && _playerCamera != null)
            {
                var socket = new GameObject("World_CarrySocket");
                socket.transform.SetParent(_playerCamera.transform, false);
                socket.transform.localPosition = new Vector3(0.24f, -0.22f, 0.55f);
                _carrySocket = socket.transform;
            }
        }

        private void Update()
        {
            if (_playerCamera == null) return;

            // Drop carried rock/object with Right Click
            if (_carriedObject != null && ReadDropPressed())
            {
                DropCarriedObject();
                return;
            }

            Ray ray = _playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (Physics.Raycast(ray, out RaycastHit hit, _interactDistance, _worldInteractMask, QueryTriggerInteraction.Collide))
            {
                _focusedItem = hit.collider.GetComponentInParent<WorldInteractableItem>();
            }
            else
            {
                _focusedItem = null;
            }

            if (_focusedItem != null && ReadInteractPressed())
            {
                _focusedItem.Interact(this);
            }
        }

        public void CarryWorldObject(GameObject target)
        {
            if (_carriedObject != null) DropCarriedObject();

            _carriedObject = target;
            if (_carriedObject.TryGetComponent<Rigidbody>(out var rb))
            {
                rb.isKinematic = true;
            }
            foreach (var col in _carriedObject.GetComponentsInChildren<Collider>())
            {
                col.enabled = false;
            }

            _carriedObject.transform.SetParent(_carrySocket, false);
            _carriedObject.transform.localPosition = Vector3.zero;
            _carriedObject.transform.localRotation = Quaternion.identity;
        }

        public void DropCarriedObject()
        {
            if (_carriedObject == null) return;

            _carriedObject.transform.SetParent(null, true);
            foreach (var col in _carriedObject.GetComponentsInChildren<Collider>())
            {
                col.enabled = true;
            }
            if (_carriedObject.TryGetComponent<Rigidbody>(out var rb))
            {
                rb.isKinematic = false;
            }

            _carriedObject = null;
        }

        private static bool ReadInteractPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
                || (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame);
#else
            return Input.GetKeyDown(KeyCode.E) || Input.GetMouseButtonDown(0);
#endif
        }

        private static bool ReadDropPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(1);
#endif
        }
    }
}