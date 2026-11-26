using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class CyanotypeStudioHUD : MonoBehaviour
{
    [Header("Studio References")]
    public CyanotypeEnvironmentRig rig;
    public CyanotypeFluidController fluidController;
    public CyanotypeApplicatorTools applicatorTools;
    public StudioEnvironmentToggle envToggle;
    public DraggableCyanotypePaper draggablePaper;

    [Header("HUD Layout")]
    public bool showHUD = true;
    [Range(0.8f, 2.5f)] public float manualScaleMultiplier = 1.0f;

    public static bool IsPointerOverHUD { get; private set; }

    private GUIStyle _panelStyle;
    private GUIStyle _headerStyle;
    private GUIStyle _labelStyle;
    private GUIStyle _buttonStyle;
    private GUIStyle _activeButtonStyle;
    private GUIStyle _badgeLockedStyle;
    private GUIStyle _badgeOpenStyle;
    private GUIStyle _keyHintStyle;

    private Texture2D _bgTex;
    private Texture2D _btnTex;
    private Texture2D _btnActiveTex;
    private Texture2D _lockTex;
    private Texture2D _openTex;

    void Start()
    {
        if (rig == null)             rig             = GetComponent<CyanotypeEnvironmentRig>()  ?? FindFirstObjectByType<CyanotypeEnvironmentRig>();
        if (fluidController == null) fluidController = GetComponent<CyanotypeFluidController>() ?? FindFirstObjectByType<CyanotypeFluidController>();
        if (applicatorTools == null) applicatorTools = GetComponent<CyanotypeApplicatorTools>() ?? FindFirstObjectByType<CyanotypeApplicatorTools>();
        if (envToggle == null)       envToggle       = GetComponent<StudioEnvironmentToggle>()  ?? FindFirstObjectByType<StudioEnvironmentToggle>();
        if (draggablePaper == null)  draggablePaper  = FindFirstObjectByType<DraggableCyanotypePaper>();
    }

    void Update()
    {
        bool toggleHud = false;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.hKey.wasPressedThisFrame)
            toggleHud = true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        toggleHud |= Input.GetKeyDown(KeyCode.H);
#endif
        if (toggleHud) showHUD = !showHUD;
    }

    private void InitStyles()
    {
        if (_panelStyle != null) return;

        _bgTex        = MakeTex(new Color(0.08f, 0.10f, 0.14f, 0.90f));
        _btnTex       = MakeTex(new Color(0.16f, 0.20f, 0.27f, 0.95f));
        _btnActiveTex = MakeTex(new Color(0.12f, 0.44f, 0.72f, 1.00f));
        _lockTex      = MakeTex(new Color(0.65f, 0.24f, 0.16f, 0.95f));
        _openTex      = MakeTex(new Color(0.16f, 0.50f, 0.30f, 0.95f));

        _panelStyle = new GUIStyle(GUI.skin.box)
        {
            normal = { background = _bgTex },
            padding = new RectOffset(14, 14, 12, 12)
        };

        _headerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 17,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.88f, 0.94f, 1.0f) }
        };

        _labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            normal = { textColor = new Color(0.92f, 0.94f, 0.97f) }
        };

        _buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(12, 10, 6, 6),
            normal = { background = _btnTex, textColor = Color.white },
            hover  = { background = _btnActiveTex, textColor = Color.white }
        };

        _activeButtonStyle = new GUIStyle(_buttonStyle)
        {
            normal = { background = _btnActiveTex, textColor = Color.white }
        };

        _badgeLockedStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { background = _lockTex, textColor = Color.white }
        };

        _badgeOpenStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { background = _openTex, textColor = Color.white }
        };

        _keyHintStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            wordWrap = true,
            normal = { textColor = new Color(0.75f, 0.82f, 0.90f) }
        };
    }

    private static Texture2D MakeTex(Color col)
    {
        var tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, col);
        tex.Apply();
        return tex;
    }

    void OnGUI()
    {
        if (!showHUD)
        {
            IsPointerOverHUD = false;
            return;
        }

        InitStyles();

        float scale = Mathf.Clamp(Screen.height / 760f, 1.15f, 2.2f) * manualScaleMultiplier;
        Matrix4x4 prevMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

        float virtualW = Screen.width / scale;
        float virtualH = Screen.height / scale;

        Rect leftRect  = new Rect(14f, 14f, 320f, Mathf.Min(virtualH - 28f, 675f));
        Rect rightRect = new Rect(virtualW - 324f, 14f, 310f, Mathf.Min(virtualH - 28f, 540f));

        Vector2 mouseGui = Event.current.mousePosition;
        IsPointerOverHUD = leftRect.Contains(mouseGui) || rightRect.Contains(mouseGui);

        DrawLeftWorkbenchPanel(leftRect);
        DrawRightStudioPanel(rightRect);

        GUI.matrix = prevMatrix;
    }

    private void DrawLeftWorkbenchPanel(Rect rect)
    {
        GUILayout.BeginArea(rect, _panelStyle);

        GUILayout.Label("CYANOTYPE WORKBENCH  [H: Hide]", _headerStyle);
        GUILayout.Space(6f);

        if (fluidController != null)
        {
            GUILayout.Label("1. Active Tool (Keys 0 - 6)", _labelStyle);
            DrawToolButton("[0] Move Paper & Props",       CyanotypeFluidController.ToolMode.None);
            DrawToolButton("[1] Sensitizer Brush",         CyanotypeFluidController.ToolMode.SensitizerBrush);
            DrawToolButton("[2] Water Pour Wash",          CyanotypeFluidController.ToolMode.WaterPourWash);
            DrawToolButton("[3] Submerge in Water Tray",   CyanotypeFluidController.ToolMode.SubmergeTrayWash);
            DrawToolButton("[4] Alkali Bleach (Soda Ash)", CyanotypeFluidController.ToolMode.AlkaliBath);
            DrawToolButton("[5] Tannin Toning Bath",       CyanotypeFluidController.ToolMode.TanninBath);
            DrawToolButton("[6] Peroxide Spray + Brush",   CyanotypeFluidController.ToolMode.PeroxideSprayBrush);

            GUILayout.Space(8f);
            GUILayout.Label($"Brush Core Size: {fluidController.brushRadius:F2}", _labelStyle);
            fluidController.brushRadius = GUILayout.HorizontalSlider(fluidController.brushRadius, 0.01f, 0.20f, GUILayout.Height(20f));

            if (fluidController.currentTool == CyanotypeFluidController.ToolMode.PeroxideSprayBrush)
            {
                GUILayout.Label($"Spray Mist Spread: {fluidController.sprayRadius:F2}", _labelStyle);
                fluidController.sprayRadius = GUILayout.HorizontalSlider(fluidController.sprayRadius, 0.06f, 0.35f, GUILayout.Height(20f));
            }

            GUILayout.Label($"Flow / Spray Strength: {fluidController.flowStrength:F1}", _labelStyle);
            fluidController.flowStrength = GUILayout.HorizontalSlider(fluidController.flowStrength, 0.2f, 3.0f, GUILayout.Height(20f));
        }

        if (rig != null && rig.recipe != null)
        {
            GUILayout.Space(10f);
            GUILayout.Label("2. Sensitizer Formula (g / 100 mL)", _labelStyle);

            if (rig.recipe.locked)
            {
                GUILayout.Label("RECIPE LOCKED (Baked on Sheet)", _badgeLockedStyle, GUILayout.Height(24f));
            }
            else if (fluidController != null && fluidController.hasBeenBleached)
            {
                GUILayout.Label("UNLOCKED (Bleached — Ready for Recoat)", _badgeOpenStyle, GUILayout.Height(24f));
            }
            else
            {
                GUILayout.Label("UNLOCKED (Editable Before Coating)", _badgeOpenStyle, GUILayout.Height(24f));
            }
            
            

            GUI.enabled = !rig.recipe.locked;
            GUILayout.Label($"FAC (Ferric Ammonium Citrate): {rig.recipe.facGramsPer100mL:F1} g", _labelStyle);
            float newFac = GUILayout.HorizontalSlider(rig.recipe.facGramsPer100mL, 2f, 40f, GUILayout.Height(20f));

            GUILayout.Label($"KFC (Potassium Ferricyanide): {rig.recipe.kfcGramsPer100mL:F1} g", _labelStyle);
            float newKfc = GUILayout.HorizontalSlider(rig.recipe.kfcGramsPer100mL, 1f, 25f, GUILayout.Height(20f));

            if (!rig.recipe.locked)
            {
                rig.recipe.Set(newFac, newKfc);
                rig.facConcentration = rig.recipe.facGramsPer100mL;
                rig.kfcConcentration = rig.recipe.kfcGramsPer100mL;
            }
            GUI.enabled = true;
        }

        GUILayout.EndArea();
    }

    private void DrawRightStudioPanel(Rect rect)
    {
        GUILayout.BeginArea(rect, _panelStyle);

        GUILayout.Label("EXPOSURE & STUDIO", _headerStyle);
        GUILayout.Space(6f);

        if (rig != null)
        {
            string lampText = rig.lampIsOn ? "[L] Carbon-Arc Lamp: ON" : "[L] Carbon-Arc Lamp: OFF";
            if (GUILayout.Button(lampText, rig.lampIsOn ? _activeButtonStyle : _buttonStyle, GUILayout.Height(30f)))
                rig.lampIsOn = !rig.lampIsOn;

            GUILayout.Space(4f);
            GUILayout.Label($"Sun Time of Day: {rig.timeOfDay:F2}  [P: Pause]", _labelStyle);
            rig.timeOfDay = GUILayout.HorizontalSlider(rig.timeOfDay, 0f, 1f, GUILayout.Height(20f));

            GUILayout.Label($"Sim Speed: {rig.timeScale:F1}x", _labelStyle);
            rig.timeScale = GUILayout.HorizontalSlider(rig.timeScale, 0.5f, 30f, GUILayout.Height(20f));
        }

        if (envToggle != null)
        {
            GUILayout.Space(4f);
            string roofText = envToggle.ceilingRemoved ? "[T] Roof: OPEN (Outdoor Sun)" : "[T] Roof: CLOSED (Window)";
            if (GUILayout.Button(roofText, envToggle.ceilingRemoved ? _activeButtonStyle : _buttonStyle, GUILayout.Height(30f)))
                envToggle.ToggleCeiling();
        }

        GUILayout.Space(8f);
        if (draggablePaper != null && GUILayout.Button("[C] Return Paper to Table", _buttonStyle, GUILayout.Height(30f)))
            draggablePaper.ReturnToWorkbench();

        if (rig != null && GUILayout.Button("[Backspace] Fresh Paper & Unlock", _buttonStyle, GUILayout.Height(30f)))
            rig.ResetFullSheet();

        GUILayout.Space(10f);
        GUILayout.Label("CONTROLS (UNIFIED)", _headerStyle);
        GUILayout.Label(
            "• WASD / Arrows: Move Camera\n" +
            "• Hold Right-Click: Look Around\n" +
            "• Middle-Drag / Wheel: Pan & Zoom\n" +
            "• Tab: Toggle Free-Look | R: Reset View\n" +
            "• Prop Selected: Q/E Height, Z/X Rotate\n" +
            "• Space: Debug Quick-Wash",
            _keyHintStyle
        );

        GUILayout.EndArea();
    }

    private void DrawToolButton(string label, CyanotypeFluidController.ToolMode mode)
    {
        bool isCurrent = fluidController.currentTool == mode;
        if (GUILayout.Button(label, isCurrent ? _activeButtonStyle : _buttonStyle, GUILayout.Height(28f)))
        {
            fluidController.currentTool = mode;
        }
    }
}