#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

public class CyanotypeStudioBuilder : EditorWindow
{
    private Texture2D _customStencilTexture;

    [MenuItem("Tools/Cyanotype/Build Complete Cyanotype Studio")]
    public static void BuildCompleteStudio()
    {
        EnsureDirectories();

        DestroyExisting("Cyanotype_StudioRoot");
        DestroyExisting("Cyanotype_StudioRig");
        DestroyExisting("Cyanotype_PlayerCam");

        GameObject studioRoot = new GameObject("Cyanotype_StudioRoot");
        Undo.RegisterCreatedObjectUndo(studioRoot, "Build Cyanotype Studio");

        Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        Material floorMat      = CreateLitMat(litShader, new Color(0.24f, 0.25f, 0.28f), 0.0f, 0.25f);
        Material wallMat       = CreateLitMat(litShader, new Color(0.76f, 0.74f, 0.70f), 0.0f, 0.15f);
        Material woodTableMat  = CreateLitMat(litShader, new Color(0.38f, 0.26f, 0.17f), 0.0f, 0.35f);
        Material patioStoneMat = CreateLitMat(litShader, new Color(0.68f, 0.66f, 0.62f), 0.0f, 0.20f);
        Material metalDarkMat  = CreateLitMat(litShader, new Color(0.16f, 0.17f, 0.20f), 0.85f, 0.65f);
        Material trayCeramicMat= CreateLitMat(litShader, new Color(0.88f, 0.90f, 0.92f), 0.0f, 0.75f);
        Material waterMat      = CreateTransparentMat(litShader, new Color(0.25f, 0.62f, 0.85f, 0.38f));
        Material uvGlowMat     = CreateEmissiveMat(litShader, new Color(0.45f, 0.22f, 0.95f), new Color(0.55f, 0.25f, 1.0f) * 2.4f);

        GameObject roomGroup = CreateChild(studioRoot, "Darkroom_Architecture");

        CreateBox(roomGroup, "StudioFloor", new Vector3(1.8f, -0.05f, -1.5f), new Vector3(9.5f, 0.10f, 6.5f), floorMat, true);
        CreateBox(roomGroup, "Wall_Back",  new Vector3(-0.2f, 1.35f,  1.2f), new Vector3(4.2f, 2.7f, 0.15f), wallMat, true);
        CreateBox(roomGroup, "Wall_Left",  new Vector3(-2.2f, 1.35f, -1.5f), new Vector3(0.15f, 2.7f, 5.5f), wallMat, true);
        CreateBox(roomGroup, "Wall_Front", new Vector3(-0.2f, 1.35f, -4.2f), new Vector3(4.2f, 2.7f, 0.15f), wallMat, true);

        GameObject ceiling = CreateBox(roomGroup, "StudioCeiling", new Vector3(-0.2f, 2.72f, -1.5f), new Vector3(4.3f, 0.12f, 5.6f), wallMat, true);

        GameObject furnitureGroup = CreateChild(studioRoot, "Workbenches");

        CreateBox(furnitureGroup, "IndoorWorkbench_Top",   new Vector3(0.0f, 0.775f, -1.5f),   new Vector3(2.8f, 0.05f, 1.3f),  woodTableMat, true);
        CreateBox(furnitureGroup, "IndoorWorkbench_LegFL", new Vector3(-1.25f, 0.375f, -2.05f), new Vector3(0.08f, 0.75f, 0.08f), metalDarkMat, true);
        CreateBox(furnitureGroup, "IndoorWorkbench_LegFR", new Vector3( 1.25f, 0.375f, -2.05f), new Vector3(0.08f, 0.75f, 0.08f), metalDarkMat, true);
        CreateBox(furnitureGroup, "IndoorWorkbench_LegBL", new Vector3(-1.25f, 0.375f, -0.95f), new Vector3(0.08f, 0.75f, 0.08f), metalDarkMat, true);
        CreateBox(furnitureGroup, "IndoorWorkbench_LegBR", new Vector3( 1.25f, 0.375f, -0.95f), new Vector3(0.08f, 0.75f, 0.08f), metalDarkMat, true);

        CreateBox(furnitureGroup, "OutdoorPatioTable_Top",      new Vector3(3.6f, 0.775f, -1.5f), new Vector3(2.4f, 0.05f, 1.3f),  patioStoneMat, true);
        CreateBox(furnitureGroup, "OutdoorPatioTable_Pedestal", new Vector3(3.6f, 0.375f, -1.5f), new Vector3(0.45f, 0.75f, 0.45f), patioStoneMat, true);

        GameObject paperGO = GameObject.CreatePrimitive(PrimitiveType.Quad);
        paperGO.name = "CyanotypePaper";
        paperGO.transform.SetParent(studioRoot.transform, false);
        paperGO.transform.position = new Vector3(0.0f, 0.806f, -1.5f);
        paperGO.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        paperGO.transform.localScale = new Vector3(0.63f, 0.89f, 1f);
        paperGO.layer = 0;

        MeshFilter paperMF = paperGO.GetComponent<MeshFilter>();
        MeshRenderer paperMR = paperGO.GetComponent<MeshRenderer>();

        Shader simShader     = Shader.Find("Cyanotype/PhotochemicalSimMultiSource");
        Shader displayShader = Shader.Find("Cyanotype/SurfaceDisplay");
        Shader fluidShader   = Shader.Find("Cyanotype/FluidTransport");

        Material simMat     = CreateAssetMaterial(simShader,     "Assets/Cyanotype/Materials/M_CyanotypeSim.mat");
        Material displayMat = CreateAssetMaterial(displayShader, "Assets/Cyanotype/Materials/M_CyanotypeDisplay.mat");
        Material fluidMat   = CreateAssetMaterial(fluidShader,   "Assets/Cyanotype/Materials/M_CyanotypeFluid.mat");

        if (displayMat != null)
            paperMR.sharedMaterial = displayMat;

        GameObject washTray = CreateChild(studioRoot, "WaterWashTray");
        washTray.transform.position = new Vector3(0.88f, 0.801f, -1.5f);

        GameObject trayBase = CreateBox(washTray, "Tray_Base", new Vector3(0.88f, 0.805f, -1.5f), new Vector3(0.76f, 0.010f, 1.02f), trayCeramicMat, true);
        trayBase.layer = 2;
        CreateBox(washTray, "Tray_WallN", new Vector3(0.88f, 0.825f, -0.99f), new Vector3(0.78f, 0.045f, 0.02f), trayCeramicMat, false).layer = 2;
        CreateBox(washTray, "Tray_WallS", new Vector3(0.88f, 0.825f, -2.01f), new Vector3(0.78f, 0.045f, 0.02f), trayCeramicMat, false).layer = 2;
        CreateBox(washTray, "Tray_WallE", new Vector3(1.26f, 0.825f, -1.50f), new Vector3(0.02f, 0.045f, 1.02f), trayCeramicMat, false).layer = 2;
        CreateBox(washTray, "Tray_WallW", new Vector3(0.50f, 0.825f, -1.50f), new Vector3(0.02f, 0.045f, 1.02f), trayCeramicMat, false).layer = 2;

        GameObject waterVol = CreateBox(washTray, "WaterVolume", new Vector3(0.88f, 0.820f, -1.5f), new Vector3(0.72f, 0.028f, 0.96f), waterMat, true);
        waterVol.layer = 2;
        BoxCollider waterCol = waterVol.GetComponent<BoxCollider>();
        waterCol.isTrigger = true;
        waterVol.AddComponent<WaterTrayTrigger>();

        GameObject propTrayGO = CreateChild(studioRoot, "PropTray");
        propTrayGO.transform.position = new Vector3(-0.78f, 0.801f, -1.5f);
        propTrayGO.layer = 2;

        CreateBox(propTrayGO, "PropTray_Base", new Vector3(-0.78f, 0.803f, -1.5f),  new Vector3(0.56f, 0.006f, 0.72f), trayCeramicMat, false).layer = 2;
        CreateBox(propTrayGO, "PropTray_RimN", new Vector3(-0.78f, 0.815f, -1.14f), new Vector3(0.58f, 0.024f, 0.015f), trayCeramicMat, false).layer = 2;
        CreateBox(propTrayGO, "PropTray_RimS", new Vector3(-0.78f, 0.815f, -1.86f), new Vector3(0.58f, 0.024f, 0.015f), trayCeramicMat, false).layer = 2;
        CreateBox(propTrayGO, "PropTray_RimE", new Vector3(-0.50f, 0.815f, -1.50f), new Vector3(0.015f, 0.024f, 0.72f), trayCeramicMat, false).layer = 2;
        CreateBox(propTrayGO, "PropTray_RimW", new Vector3(-1.06f, 0.815f, -1.50f), new Vector3(0.015f, 0.024f, 0.72f), trayCeramicMat, false).layer = 2;

        CyanotypePropTray propTray = propTrayGO.AddComponent<CyanotypePropTray>();
        propTray.trayOrigin = new Vector3(-0.78f, 0.808f, -1.5f);
        propTray.paperSurface = paperGO.transform;
        propTray.propLayer = 1 << 0;

        GameObject lampRoot = CreateChild(studioRoot, "UVLamp_Assembly");
        lampRoot.transform.position = new Vector3(0.0f, 1.58f, -1.5f);
        lampRoot.layer = 2;

        CreateBox(lampRoot, "Lamp_StandPole", new Vector3(0.0f, 1.25f, -1.02f), new Vector3(0.04f, 0.92f, 0.04f), metalDarkMat, false).layer = 2;
        CreateBox(lampRoot, "Lamp_Arm",       new Vector3(0.0f, 1.70f, -1.26f), new Vector3(0.04f, 0.04f, 0.52f), metalDarkMat, false).layer = 2;
        CreateBox(lampRoot, "Lamp_Hood",      new Vector3(0.0f, 1.64f, -1.50f), new Vector3(0.46f, 0.06f, 0.56f), metalDarkMat, false).layer = 2;
        CreateBox(lampRoot, "Lamp_UVTube",    new Vector3(0.0f, 1.60f, -1.50f), new Vector3(0.38f, 0.02f, 0.48f), uvGlowMat, false).layer = 2;

        GameObject uvLightGO = new GameObject("UV_SpotLight");
        uvLightGO.transform.SetParent(lampRoot.transform, false);
        uvLightGO.transform.position = new Vector3(0.0f, 1.56f, -1.50f);
        uvLightGO.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        Light uvSpot = uvLightGO.AddComponent<Light>();
        uvSpot.type = LightType.Spot;
        uvSpot.spotAngle = 78f;
        uvSpot.range = 4.0f;
        uvSpot.intensity = 3.2f;
        uvSpot.color = new Color(0.55f, 0.30f, 1.0f);
        uvSpot.shadows = LightShadows.Soft;

        Light sunLight = FindFirstObjectByType<Light>();
        if (sunLight == null || sunLight.type != LightType.Directional)
        {
            GameObject sunGO = new GameObject("Sun_DirectionalLight");
            sunGO.transform.SetParent(studioRoot.transform, false);
            sunLight = sunGO.AddComponent<Light>();
            sunLight.type = LightType.Directional;
        }
        sunLight.transform.rotation = Quaternion.Euler(55f, 25f, 0f);
        sunLight.intensity = 1.4f;
        sunLight.shadows = LightShadows.Soft;

        GameObject rigGO = new GameObject("Cyanotype_StudioRig");
        Undo.RegisterCreatedObjectUndo(rigGO, "Create Cyanotype Studio Rig");

        CyanotypeEnvironmentRig envRig = rigGO.AddComponent<CyanotypeEnvironmentRig>();
        envRig.paperMeshFilter = paperMF;
        envRig.paperRenderer = paperMR;
        envRig.shadowCasterLayers = 1 << 0;
        envRig.sunLight = sunLight;
        envRig.uvLampLight = uvSpot;
        envRig.lampIsOn = true;
        envRig.lampUVIntensity = 2.6f;
        envRig.peakSunUVIntensity = 0.0f;
        envRig.windowAmbientUV = 0.03f;
        envRig.coatingThickness = 0.0f;
        envRig.simMaterial = simMat;
        envRig.displayMaterial = displayMat;
        envRig.timeScale = 5.0f;

        CyanotypeFluidController fluidCtrl = rigGO.AddComponent<CyanotypeFluidController>();
        fluidCtrl.rig = envRig;
        fluidCtrl.currentTool = CyanotypeFluidController.ToolMode.SensitizerBrush;
        TryAssignSerializedProperty(fluidCtrl, "paperMeshFilter", paperMF);
        TryAssignSerializedProperty(fluidCtrl, "paperTransform", paperGO.transform);
        TryAssignSerializedProperty(fluidCtrl, "fluidSimMaterial", fluidMat);
        TryAssignSerializedProperty(fluidCtrl, "fluidMaterial", fluidMat);
        TryAssignSerializedProperty(fluidCtrl, "displayMaterial", displayMat);
        TryAssignSerializedProperty(fluidCtrl, "simMaterial", simMat);

        CyanotypeApplicatorTools appTools = rigGO.AddComponent<CyanotypeApplicatorTools>();
        appTools.fluidController = fluidCtrl;
        appTools.paperTransform = paperGO.transform;

        StudioEnvironmentToggle envToggle = rigGO.AddComponent<StudioEnvironmentToggle>();
        envToggle.rig = envRig;
        TryAssignSerializedProperty(envToggle, "ceilingObject", ceiling);
        TryAssignSerializedProperty(envToggle, "studioCeiling", ceiling);
        TryAssignSerializedProperty(envToggle, "ceiling", ceiling);
        TryAssignSerializedProperty(envToggle, "sunLight", sunLight);

        DraggableCyanotypePaper dragPaper = paperGO.AddComponent<DraggableCyanotypePaper>();
        dragPaper.fluidController = fluidCtrl;
        dragPaper.restingY = 0.806f;

        CyanotypeStudioHUD hud = rigGO.AddComponent<CyanotypeStudioHUD>();
        hud.rig = envRig;
        hud.fluidController = fluidCtrl;
        hud.applicatorTools = appTools;
        hud.envToggle = envToggle;
        hud.draggablePaper = dragPaper;

        Camera mainCam = Camera.main;
        if (mainCam == null)
        {
            GameObject camGO = new GameObject("Cyanotype_PlayerCam");
            camGO.tag = "MainCamera";
            mainCam = camGO.AddComponent<Camera>();
        }
        mainCam.transform.position = new Vector3(0.0f, 1.55f, -2.35f);
        mainCam.transform.rotation = Quaternion.Euler(48f, 0f, 0f);
        mainCam.nearClipPlane = 0.05f;

        if (mainCam.GetComponent<CyanotypePlayerController>() == null)
            mainCam.gameObject.AddComponent<CyanotypePlayerController>();

        Selection.activeGameObject = rigGO;
        Debug.Log("[Cyanotype] Complete Studio built! Coat paper [1]/[4], place props [0], expose with UV Lamp [L] or Open Roof [T], and wash in Water Tray [3].");
    }

    private static void TryAssignSerializedProperty(Object target, string propertyName, Object value)
    {
        if (target == null || value == null) return;
        SerializedObject so = new SerializedObject(target);
        SerializedProperty prop = so.FindProperty(propertyName);
        if (prop != null && prop.propertyType == SerializedPropertyType.ObjectReference)
        {
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    [MenuItem("Tools/Cyanotype/Make Selected Object a Stencil")]
    public static void ConvertSelectionToStencil()
    {
        if (Selection.activeGameObject == null)
        {
            Debug.LogWarning("[Cyanotype] Select a GameObject in the Hierarchy first.");
            return;
        }

        GameObject go = Selection.activeGameObject;
        Undo.RecordObject(go, "Convert to Cyanotype Stencil");

        var stencil = go.GetComponent<CyanotypeStencilProp>();
        if (stencil == null)
            stencil = Undo.AddComponent<CyanotypeStencilProp>(go);

        stencil.SetupPhysicsAndDragging();
        Debug.Log($"[Cyanotype] '{go.name}' configured as a draggable UV stencil!");
    }

    [MenuItem("Tools/Cyanotype/Create PNG Stencil Quad...")]
    public static void ShowStencilWindow()
    {
        GetWindow<CyanotypeStudioBuilder>("Create PNG Stencil");
    }

    void OnGUI()
    {
        GUILayout.Label("Create Photographic PNG / Negative Stencil", EditorStyles.boldLabel);
        _customStencilTexture = (Texture2D)EditorGUILayout.ObjectField("Stencil PNG Texture", _customStencilTexture, typeof(Texture2D), false);

        if (GUILayout.Button("Spawn Stencil Quad on Workbench", GUILayout.Height(30f)))
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = _customStencilTexture != null ? $"Stencil_{_customStencilTexture.name}" : "Stencil_CustomQuad";
            quad.transform.position = new Vector3(-0.45f, 0.812f, -1.5f);
            quad.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            quad.transform.localScale = new Vector3(0.32f, 0.32f, 1f);

            var stencil = quad.AddComponent<CyanotypeStencilProp>();
            stencil.stencilMaskTexture = _customStencilTexture;
            stencil.SetupPhysicsAndDragging();

            Selection.activeGameObject = quad;
            Undo.RegisterCreatedObjectUndo(quad, "Spawn Custom Stencil Quad");
        }
    }

    private static void EnsureDirectories()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Cyanotype"))
            AssetDatabase.CreateFolder("Assets", "Cyanotype");
        if (!AssetDatabase.IsValidFolder("Assets/Cyanotype/Materials"))
            AssetDatabase.CreateFolder("Assets/Cyanotype", "Materials");
    }

    private static void DestroyExisting(string name)
    {
        GameObject existing = GameObject.Find(name);
        if (existing != null)
            Undo.DestroyObjectImmediate(existing);
    }

    private static GameObject CreateChild(GameObject parent, string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        return go;
    }

    private static GameObject CreateBox(GameObject parent, string name, Vector3 worldPos, Vector3 scale, Material mat, bool keepCollider)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent.transform, false);
        go.transform.position = worldPos;
        go.transform.localScale = scale;

        var rend = go.GetComponent<Renderer>();
        if (rend != null && mat != null)
            rend.sharedMaterial = mat;

        if (!keepCollider)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
        }
        return go;
    }

    private static Material CreateLitMat(Shader shader, Color color, float metallic, float smoothness)
    {
        Material m = new Material(shader);
        m.color = color;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
        return m;
    }

    private static Material CreateTransparentMat(Shader shader, Color color)
    {
        Material m = CreateLitMat(shader, color, 0f, 0.9f);
        if (m.HasProperty("_Surface"))
        {
            m.SetFloat("_Surface", 1f);
            m.renderQueue = 3000;
        }
        return m;
    }

    private static Material CreateEmissiveMat(Shader shader, Color baseCol, Color emissionCol)
    {
        Material m = CreateLitMat(shader, baseCol, 0f, 0.5f);
        m.EnableKeyword("_EMISSION");
        if (m.HasProperty("_EmissionColor"))
            m.SetColor("_EmissionColor", emissionCol);
        return m;
    }

    private static Material CreateAssetMaterial(Shader shader, string path)
    {
        if (shader == null)
        {
            Debug.LogWarning($"[Cyanotype] Shader not found when creating {path}.");
            return null;
        }

        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
        {
            existing.shader = shader;
            EditorUtility.SetDirty(existing);
            return existing;
        }

        Material mat = new Material(shader);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }
}
#endif