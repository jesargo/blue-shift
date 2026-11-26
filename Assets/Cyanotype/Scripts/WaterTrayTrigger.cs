using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class WaterTrayTrigger : MonoBehaviour
{
    public enum TrayLiquid
    {
        Water = 0,
        AlkaliBath = 1,
        TanninBath = 2,
        PeroxideBath = 3
    }

    public CyanotypeFluidController fluidController;
    public TrayLiquid trayLiquid = TrayLiquid.Water;

    void Start()
    {
        if (fluidController == null)
            fluidController = FindFirstObjectByType<CyanotypeFluidController>();
    }

    private void ApplyTraySubmersion(bool submerged)
    {
        if (fluidController == null) return;

        if (trayLiquid == TrayLiquid.Water)
        {
            if (submerged)
                fluidController.currentTool = CyanotypeFluidController.ToolMode.SubmergeTrayWash;
            else if (fluidController.currentTool == CyanotypeFluidController.ToolMode.SubmergeTrayWash)
                fluidController.currentTool = CyanotypeFluidController.ToolMode.None;
        }
        else
        {
            fluidController.activeBathTray = submerged
                ? (CyanotypeFluidController.BathTrayMode)trayLiquid
                : CyanotypeFluidController.BathTrayMode.None;
        }
    }

    void OnTriggerStay(Collider other)
    {
        if (fluidController != null && other.transform == fluidController.paperTransform)
            ApplyTraySubmersion(true);
    }

    void OnTriggerExit(Collider other)
    {
        if (fluidController != null && other.transform == fluidController.paperTransform)
            ApplyTraySubmersion(false);
    }

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
                bool alreadyActive = trayLiquid == TrayLiquid.Water
                    ? fluidController.currentTool == CyanotypeFluidController.ToolMode.SubmergeTrayWash
                    : fluidController.activeBathTray == (CyanotypeFluidController.BathTrayMode)trayLiquid;

                ApplyTraySubmersion(!alreadyActive);
            }
        }
    }
}