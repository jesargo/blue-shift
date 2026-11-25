using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class CyanotypeStudioHUD : MonoBehaviour
{
    public static bool IsPointerOverHUD { get; private set; }

    [Header("References")]
    public CyanotypeEnvironmentRig rig;
    public CyanotypeFluidController fluidController;
    public CyanotypeApplicatorTools applicatorTools;
    public StudioEnvironmentToggle envToggle;
    public DraggableCyanotypePaper draggablePaper;

    private bool _labOpen = true;
    private float _concFAC = 20.0f;
    private float _concKFC = 8.0f;
    private float _peroxideBoost = 0f;
    private float _tanninTone = 0f;
    private bool _sodaBleachActive = false;

    private Texture2D _panelTex;
    private Texture2D _btnNormalTex;
    private Texture2D _btnActiveTex;
    private Texture2D _accentBlueTex;

    private GUIStyle _panelStyle;
    private GUIStyle _btnStyle;
    private GUIStyle _activeBtnStyle;
    private GUIStyle _titleStyle;
    private GUIStyle _labelStyle;
    private GUIStyle _hintStyle;

    void Start()
    {
        if (rig == null) rig = FindFirstObjectByType<CyanotypeEnvironmentRig>();
        if (fluidController == null) fluidController = FindFirstObjectByType<CyanotypeFluidController>();
        if (applicatorTools == null) applicatorTools = FindFirstObjectByType<CyanotypeApplicatorTools>();
        if (envToggle == null) envToggle = FindFirstObjectByType<StudioEnvironmentToggle>();
        if (draggablePaper == null) draggablePaper = FindFirstObjectByType<DraggableCyanotypePaper>();

        if (rig != null && rig.displayMaterial != null)
        {
            if (rig.displayMaterial.HasProperty("_ConcFAC"))
                _concFAC = rig.displayMaterial.GetFloat("_ConcFAC");
            if (rig.displayMaterial.HasProperty("_ConcKFC"))
                _concKFC = rig.displayMaterial.GetFloat("_ConcKFC");
        }

        _panelTex      = MakeTex(new Color(0.09f, 0.11f, 0.14f, 0.90f));
        _btnNormalTex  = MakeTex(new Color(0.17f, 0.20f, 0.25f, 0.95f));
        _btnActiveTex  = MakeTex(new Color(0.12f, 0.42f, 0.68f, 1.00f));
        _accentBlueTex = MakeTex(new Color(0.14f, 0.48f, 0.78f, 1.00f));
    }

    private static Texture2D MakeTex(Color col)
    {
        var tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, col);
        tex.Apply();
        return tex;
    }

    private void InitStyles()
    {
        if (_panelStyle != null) return;

        _panelStyle = new GUIStyle(GUI.skin.box);
        _panelStyle.normal.background = _panelTex;

        _btnStyle = new GUIStyle(GUI.skin.button);
        _btnStyle.normal.background = _btnNormalTex;
        _btnStyle.hover.background = _accentBlueTex;
        _btnStyle.normal.textColor = new Color(0.90f, 0.92f, 0.95f);
        _btnStyle.hover.textColor = Color.white;
        _btnStyle.fontSize = 12;
        _btnStyle.fontStyle = FontStyle.Bold;

        _activeBtnStyle = new GUIStyle(_btnStyle);
        _activeBtnStyle.normal.background = _btnActiveTex;
        _activeBtnStyle.normal.textColor = Color.white;

        _titleStyle = new GUIStyle(GUI.skin.label);
        _titleStyle.fontSize = 13;
        _titleStyle.fontStyle = FontStyle.Bold;
        _titleStyle.normal.textColor = Color.white;

        _labelStyle = new GUIStyle(GUI.skin.label);
        _labelStyle.fontSize = 11;
        _labelStyle.normal.textColor = new Color(0.84f, 0.88f, 0.92f);

        _hintStyle = new GUIStyle(GUI.skin.label);
        _hintStyle.fontSize = 11;
        _hintStyle.alignment = TextAnchor.MiddleCenter;
        _hintStyle.normal.textColor = new Color(0.75f, 0.80f, 0.86f);
    }

    void Update()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb != null && kb.xKey.wasPressedThisFrame) _labOpen = !_labOpen;
#else
        if (Input.GetKeyDown(KeyCode.X)) _labOpen = !_labOpen;
#endif
        SyncShaderParameters();
    }

    private void SyncShaderParameters()
    {
        if (rig == null) return;

        if (rig.displayMaterial != null)
        {
            rig.displayMaterial.SetFloat("_ConcFAC", _concFAC);
            rig.displayMaterial.SetFloat("_ConcKFC", _concKFC);
            rig.displayMaterial.SetFloat("_PeroxideBoost", _peroxideBoost);
            rig.displayMaterial.SetFloat("_TanninTone", _tanninTone);
        }

        if (rig.simMaterial != null)
        {
            rig.simMaterial.SetFloat("_BleachIntensity", _sodaBleachActive ? 0.65f : 0f);
        }
    }

    void OnGUI()
    {
        InitStyles();

        float w = Screen.width;
        float h = Screen.height;

        // 1. Top Bar: Lighting, Ceiling, Lab Toggle & Sheet Actions
        Rect topRect = new Rect((w - 860f) * 0.5f, 12f, 860f, 48f);
        GUI.Box(topRect, GUIContent.none, _panelStyle);

        GUILayout.BeginArea(new Rect(topRect.x + 10f, topRect.y + 8f, topRect.width - 20f, 32f));
        GUILayout.BeginHorizontal();

        bool lampOn = rig != null && rig.lampIsOn;
        if (GUILayout.Button(lampOn ? "UV Lamp: ON [L]" : "UV Lamp: OFF [L]", lampOn ? _activeBtnStyle : _btnStyle, GUILayout.Height(30f), GUILayout.Width(135f)))
        {
            if (rig != null) rig.lampIsOn = !rig.lampIsOn;
        }

        bool ceilingOff = envToggle != null && envToggle.ceilingRemoved;
        if (GUILayout.Button(ceilingOff ? "Roof: OPEN [T]" : "Roof: CLOSED [T]", ceilingOff ? _activeBtnStyle : _btnStyle, GUILayout.Height(30f), GUILayout.Width(135f)))
        {
            if (envToggle != null) envToggle.ToggleCeiling();
        }

        if (GUILayout.Button(_labOpen ? "Chem Lab: ON [X]" : "Chem Lab: OFF [X]", _labOpen ? _activeBtnStyle : _btnStyle, GUILayout.Height(30f), GUILayout.Width(135f)))
        {
            _labOpen = !_labOpen;
        }

        if (GUILayout.Button("Center Paper [C]", _btnStyle, GUILayout.Height(30f), GUILayout.Width(130f)))
        {
            if (draggablePaper != null)
            {
                draggablePaper.transform.position = new Vector3(0f, draggablePaper.restingY, -1.5f);
                if (fluidController != null && fluidController.currentTool == CyanotypeFluidController.ToolMode.SubmergeTrayWash)
                    fluidController.currentTool = CyanotypeFluidController.ToolMode.None;
            }
        }

        if (GUILayout.Button("Fresh Sheet [Bksp]", _btnStyle, GUILayout.Height(30f), GUILayout.Width(140f)))
        {
            ResetSheetAndToning();
        }

        if (GUILayout.Button("Reset View [R]", _btnStyle, GUILayout.Height(30f), GUILayout.Width(115f)))
        {
            var camCtrl = FindFirstObjectByType<CyanotypePlayerController>();
            if (camCtrl != null)
            {
                camCtrl.transform.position = camCtrl.homePosition;
                camCtrl.transform.eulerAngles = camCtrl.homeEulerAngles;
            }
        }

        GUILayout.EndHorizontal();
        GUILayout.EndArea();

        // 2. Left Drawer: Darkroom Chemistry & Toning Lab
        Rect labRect = new Rect(14f, 72f, 255f, 385f);
        if (_labOpen && rig != null)
        {
            GUI.Box(labRect, GUIContent.none, _panelStyle);
            GUILayout.BeginArea(new Rect(labRect.x + 12f, labRect.y + 10f, labRect.width - 24f, labRect.height - 20f));

            GUILayout.Label("Sensitizer Chemistry", _titleStyle);
            GUILayout.Label($"Ferric Ammonium Citrate: {_concFAC:F1} g", _labelStyle);
            _concFAC = GUILayout.HorizontalSlider(_concFAC, 4f, 40f);

            GUILayout.Label($"Potassium Ferricyanide: {_concKFC:F1} g", _labelStyle);
            _concKFC = GUILayout.HorizontalSlider(_concKFC, 2f, 25f);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Classic 20:8", _btnStyle, GUILayout.Height(24f)))
            {
                _concFAC = 20f;
                _concKFC = 8f;
            }
            if (GUILayout.Button("High-Speed 32:10", _btnStyle, GUILayout.Height(24f)))
            {
                _concFAC = 32f;
                _concKFC = 10f;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            GUILayout.Label($"Exposure Speed: {rig.timeScale:F1}x", _labelStyle);
            rig.timeScale = GUILayout.HorizontalSlider(rig.timeScale, 0.5f, 10f);

            GUILayout.Space(10f);
            GUILayout.Label("Post-Wash Toning Baths", _titleStyle);

            GUILayout.Label($"Peroxide Oxidation: {Mathf.RoundToInt(_peroxideBoost * 100f)}%", _labelStyle);
            _peroxideBoost = GUILayout.HorizontalSlider(_peroxideBoost, 0f, 1f);

            GUILayout.Label($"Tea / Tannin Bath: {Mathf.RoundToInt(_tanninTone * 100f)}%", _labelStyle);
            _tanninTone = GUILayout.HorizontalSlider(_tanninTone, 0f, 1f);

            GUILayout.Space(6f);
            if (GUILayout.Button(_sodaBleachActive ? "Soda Ash Bleach: POURING" : "Bleach in Soda Ash Bath", _sodaBleachActive ? _activeBtnStyle : _btnStyle, GUILayout.Height(28f)))
            {
                _sodaBleachActive = !_sodaBleachActive;
            }

            GUILayout.EndArea();
        }

        // 3. Bottom Dock: Applicator & Wash Tools
        Rect bottomRect = new Rect((w - 820f) * 0.5f, h - 86f, 820f, 74f);
        GUI.Box(bottomRect, GUIContent.none, _panelStyle);

        GUILayout.BeginArea(new Rect(bottomRect.x + 12f, bottomRect.y + 8f, bottomRect.width - 24f, 58f));
        GUILayout.BeginHorizontal();

        var tool = fluidController != null ? fluidController.currentTool : CyanotypeFluidController.ToolMode.None;
        var app  = applicatorTools != null ? applicatorTools.activeApplicator : CyanotypeApplicatorTools.ApplicatorType.HakeBrush;

        bool isMove   = tool == CyanotypeFluidController.ToolMode.None;
        bool isBrush  = tool == CyanotypeFluidController.ToolMode.SensitizerBrush && app == CyanotypeApplicatorTools.ApplicatorType.HakeBrush;
        bool isRoller = tool == CyanotypeFluidController.ToolMode.SensitizerBrush && app == CyanotypeApplicatorTools.ApplicatorType.FoamRoller;
        bool isPour   = tool == CyanotypeFluidController.ToolMode.WaterPourWash;
        bool isTray   = tool == CyanotypeFluidController.ToolMode.SubmergeTrayWash;

        if (GUILayout.Button("Move Props/Paper [0]", isMove ? _activeBtnStyle : _btnStyle, GUILayout.Height(34f)))
            SetTool(CyanotypeFluidController.ToolMode.None, app);

        if (GUILayout.Button("Hake Brush [1]", isBrush ? _activeBtnStyle : _btnStyle, GUILayout.Height(34f)))
            SetTool(CyanotypeFluidController.ToolMode.SensitizerBrush, CyanotypeApplicatorTools.ApplicatorType.HakeBrush);

        if (GUILayout.Button("Foam Roller [4]", isRoller ? _activeBtnStyle : _btnStyle, GUILayout.Height(34f)))
            SetTool(CyanotypeFluidController.ToolMode.SensitizerBrush, CyanotypeApplicatorTools.ApplicatorType.FoamRoller);

        if (GUILayout.Button("Pour Water [2]", isPour ? _activeBtnStyle : _btnStyle, GUILayout.Height(34f)))
            SetTool(CyanotypeFluidController.ToolMode.WaterPourWash, app);

        if (GUILayout.Button("Tray Submerge [3]", isTray ? _activeBtnStyle : _btnStyle, GUILayout.Height(34f)))
            SetTool(CyanotypeFluidController.ToolMode.SubmergeTrayWash, app);

        if (fluidController != null)
        {
            GUILayout.Space(8f);
            GUILayout.BeginVertical(GUILayout.Width(115f));
            GUILayout.Label($"Size: {Mathf.RoundToInt(fluidController.brushRadius * 1000f)}mm", _titleStyle);
            fluidController.brushRadius = GUILayout.HorizontalSlider(fluidController.brushRadius, 0.02f, 0.14f);
            GUILayout.EndVertical();
        }

        GUILayout.EndHorizontal();

        GUI.Label(
            new Rect(0f, 38f, bottomRect.width - 24f, 18f),
            "Pour Water [2]: Localized puddle (hold to drench)   |   Tray [3] or Drop in Water Tray: Full-sheet wash   |   Hold Right-Click: Look",
            _hintStyle
        );
        GUILayout.EndArea();

        Vector2 guiMouse = Event.current.mousePosition;
        IsPointerOverHUD = topRect.Contains(guiMouse)
                        || bottomRect.Contains(guiMouse)
                        || (_labOpen && labRect.Contains(guiMouse));
    }

    private void ResetSheetAndToning()
    {
        _peroxideBoost = 0f;
        _tanninTone = 0f;
        _sodaBleachActive = false;
        SyncShaderParameters();

        if (fluidController != null) fluidController.ResetFluid();
        if (rig != null)
        {
            rig.coatingThickness = 0f;
            rig.ClearChemicalState();
        }
    }

    private void SetTool(CyanotypeFluidController.ToolMode mode, CyanotypeApplicatorTools.ApplicatorType appType)
    {
        if (fluidController != null)
            fluidController.currentTool = mode;
        if (applicatorTools != null)
            applicatorTools.SelectApplicator(appType);
    }
}