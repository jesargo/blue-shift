using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class WaterTrayTrigger : MonoBehaviour
{
    public CyanotypeFluidController fluidController;

    void Start()
    {
        if (fluidController == null)
            fluidController = FindFirstObjectByType<CyanotypeFluidController>();
    }

    // Trigger wash automatically if the paper is dragged/moved into the water tray
    void OnTriggerStay(Collider other)
    {
        if (fluidController != null && other.transform == fluidController.paperTransform)
        {
            fluidController.currentTool = CyanotypeFluidController.ToolMode.SubmergeTrayWash;
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (fluidController != null && other.transform == fluidController.paperTransform)
        {
            fluidController.currentTool = CyanotypeFluidController.ToolMode.None;
        }
    }

    // Or click the water tray directly to dunk the sheet
    void Update()
    {
        if (fluidController == null || Camera.main == null) return;

        bool clicked = false;
#if ENABLE_INPUT_SYSTEM
        clicked = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        Vector2 screenPos = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
        clicked = Input.GetMouseButtonDown(0);
        Vector2 screenPos = Input.mousePosition;
#endif
        if (clicked)
        {
            if (Cursor.lockState == CursorLockMode.Locked)
                screenPos = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

            Ray ray = Camera.main.ScreenPointToRay(screenPos);
            if (Physics.Raycast(ray, out RaycastHit hit, 50f) && hit.transform == transform)
            {
                bool alreadySubmerged = fluidController.currentTool == CyanotypeFluidController.ToolMode.SubmergeTrayWash;
                fluidController.currentTool = alreadySubmerged
                    ? CyanotypeFluidController.ToolMode.None
                    : CyanotypeFluidController.ToolMode.SubmergeTrayWash;
            }
        }
    }
}