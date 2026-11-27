#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Cyanotype.Gameplay;
using Cyanotype.World;

namespace Cyanotype.EditorTools
{
    /// <summary>
    /// Builds a complete, canonical playable Cyanotype Simulator scene around any user-supplied surface Mesh.
    /// Preserves 1900s studio world units (0.63m x 0.89m sheet, 0.50m carbon-arc lamp height, base-10 OD 1.05 stencil,
    /// standard FAC 20g / KFC 8g stoichiometry) and raycasts all props, workbench, player, flora, and 2D NPCs onto the mesh.
    /// </summary>
    public class CyanotypeSurfaceSceneCreatorWindow : EditorWindow
    {
        // Canonical world-unit constants (must match CyanotypeCommon.cginc)
        private const float SheetWidthM = 0.63f;
        private const float SheetHeightM = 0.89f;
        private const float WorkbenchHeightM = 0.80f;
        private const float PlayerStandBackM = 0.92f;
        private const float CarbonArcLampHeightM = 0.50f;
        private const float BotanicalStencilBase10OD = 1.05f;
        private const float StandardFacGramsPer100mL = 20.0f;
        private const float StandardKfcGramsPer100mL = 8.0f;

        [SerializeField] private Mesh surfaceMesh;
        [SerializeField] private Vector3 surfaceScale = Vector3.one;
        [SerializeField] private Vector2 workbenchNormalizedXZ = Vector2.zero;
        [SerializeField] private int proceduralFloraCount = 6;
        [SerializeField] private int proceduralRockCount = 4;
        [SerializeField] private bool include2DNpc = true;
        [SerializeField] private bool createNewEmptyScene = true;
        [SerializeField] private bool saveSceneAsset = true;
        [SerializeField] private string outputFolder = "Assets/Cyanotype/Scenes";

        private Vector2 _scrollPos;

        [MenuItem("Cyanotype/Create Playable Scene From Surface Mesh", false, 10)]
        public static void ShowWindow()
        {
            var window = GetWindow<CyanotypeSurfaceSceneCreatorWindow>("Cyanotype Scene Creator");
            window.minSize = new Vector2(420f, 460f);
            window.TryCaptureSelection();
            window.Show();
        }

        [MenuItem("Assets/Create/Cyanotype/Playable Scene From Selected Mesh", true)]
        private static bool ValidateCreateFromSelectedMeshAsset()
        {
            return Selection.activeObject is Mesh;
        }

        [MenuItem("Assets/Create/Cyanotype/Playable Scene From Selected Mesh", false, 210)]
        private static void CreateFromSelectedMeshAsset()
        {
            var mesh = Selection.activeObject as Mesh;
            if (mesh == null) return;

            var window = CreateInstance<CyanotypeSurfaceSceneCreatorWindow>();
            window.surfaceMesh = mesh;
            window.BuildPlayableScene();
            DestroyImmediate(window);
        }

        [MenuItem("CONTEXT/MeshFilter/Create Playable Cyanotype Scene")]
        private static void CreateFromContextMeshFilter(MenuCommand command)
        {
            var mf = command.context as MeshFilter;
            if (mf == null || mf.sharedMesh == null)
            {
                EditorUtility.DisplayDialog("Cyanotype Scene Creator", "Selected MeshFilter has no valid Mesh.", "OK");
                return;
            }

            var window = CreateInstance<CyanotypeSurfaceSceneCreatorWindow>();
            window.surfaceMesh = mf.sharedMesh;
            window.surfaceScale = mf.transform.lossyScale;
            window.BuildPlayableScene();
            DestroyImmediate(window);
        }

        private void OnEnable()
        {
            TryCaptureSelection();
        }

        private void TryCaptureSelection()
        {
            if (surfaceMesh != null) return;

            if (Selection.activeObject is Mesh selectedMesh)
            {
                surfaceMesh = selectedMesh;
            }
            else if (Selection.activeGameObject != null)
            {
                var mf = Selection.activeGameObject.GetComponentInChildren<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    surfaceMesh = mf.sharedMesh;
                    surfaceScale = mf.transform.lossyScale;
                }
            }
        }

        private void OnGUI()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            EditorGUILayout.LabelField("Cyanotype Surface-to-Scene Builder", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Assign a ground/courtyard surface Mesh. The builder snaps the 1900s cyanotype studio workbench, " +
                "first-person hybrid rig, 0.63m x 0.89m sheet, 0.50m carbon-arc lamp, chemical trays, procedural botanicals, " +
                "and 2D NPC directly onto the mesh topology.",
                MessageType.Info);

            EditorGUILayout.Space(6f);
            surfaceMesh = (Mesh)EditorGUILayout.ObjectField("Surface Mesh", surfaceMesh, typeof(Mesh), false);
            surfaceScale = EditorGUILayout.Vector3Field("Surface Mesh Scale", surfaceScale);
            workbenchNormalizedXZ = EditorGUILayout.Slider("Workbench Offset X", workbenchNormalizedXZ.x, -0.4f, 0.4f) * Vector2.right
                                  + EditorGUILayout.Slider("Workbench Offset Z", workbenchNormalizedXZ.y, -0.4f, 0.4f) * Vector2.up;

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("World & Ecological Dressing", EditorStyles.boldLabel);
            proceduralFloraCount = EditorGUILayout.IntSlider("Procedural Botanicals", proceduralFloraCount, 0, 24);
            proceduralRockCount = EditorGUILayout.IntSlider("Procedural Rocks", proceduralRockCount, 0, 16);
            include2DNpc = EditorGUILayout.Toggle("Include 2D Studio NPC", include2DNpc);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Scene Output", EditorStyles.boldLabel);
            createNewEmptyScene = EditorGUILayout.Toggle("Create New Clean Scene", createNewEmptyScene);
            saveSceneAsset = EditorGUILayout.Toggle("Save & Register Scene", saveSceneAsset);
            using (new EditorGUI.DisabledScope(!saveSceneAsset))
            {
                outputFolder = EditorGUILayout.TextField("Output Folder", outputFolder);
            }

            if (PlayerSettings.colorSpace != ColorSpace.Linear)
            {
                EditorGUILayout.HelpBox(
                    "Project Color Space is currently Gamma. Cyanotype shaders are calibrated for Linear color space to avoid double-gamma encoding.",
                    MessageType.Warning);
            }

            EditorGUILayout.Space(12f);
            using (new EditorGUI.DisabledScope(surfaceMesh == null))
            {
                if (GUILayout.Button("Create Playable Cyanotype Scene", GUILayout.Height(36f)))
                {
                    BuildPlayableScene();
                }
            }

            EditorGUILayout.EndScrollView();
        }

        public void BuildPlayableScene()
        {
            if (surfaceMesh == null)
            {
                EditorUtility.DisplayDialog("Cyanotype Scene Creator", "Please assign a valid Surface Mesh first.", "OK");
                return;
            }

            if (createNewEmptyScene)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    return;

                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }

            // 1. Environment Root & Surface Mesh Ground
            GameObject envRoot = new GameObject("Cyanotype_EnvironmentRoot");
            Undo.RegisterCreatedObjectUndo(envRoot, "Create Cyanotype Playable Scene");

            Component recipeComp = AddComponentByName(envRoot, "CyanotypeRecipe");
            Component envRig = AddComponentByName(envRoot, "CyanotypeEnvironmentRig");
            Component fluidCtrl = AddComponentByName(envRoot, "CyanotypeFluidController");
            Component envToggle = AddComponentByName(envRoot, "StudioEnvironmentToggle");
            ConfigureStandardRecipe(recipeComp);

            GameObject groundGo = new GameObject("Courtyard_Ground");
            groundGo.transform.SetParent(envRoot.transform, false);
            groundGo.transform.localScale = new Vector3(
                Mathf.Max(0.01f, surfaceScale.x),
                Mathf.Max(0.01f, surfaceScale.y),
                Mathf.Max(0.01f, surfaceScale.z));

            var groundMf = groundGo.AddComponent<MeshFilter>();
            groundMf.sharedMesh = surfaceMesh;

            var groundMr = groundGo.AddComponent<MeshRenderer>();
            groundMr.sharedMaterial = CreateDefaultLitMaterial("CourtyardGround_Mat", new Color(0.46f, 0.44f, 0.40f));

            var groundCollider = groundGo.AddComponent<MeshCollider>();
            groundCollider.sharedMesh = surfaceMesh;
            groundCollider.convex = false;

            Physics.SyncTransforms();

            Bounds groundBounds = groundCollider.bounds;
            if (groundBounds.size.x < 2.5f || groundBounds.size.z < 2.5f)
            {
                Debug.LogWarning(
                    $"[CyanotypeSceneCreator] Surface mesh footprint ({groundBounds.size.x:F2}m x {groundBounds.size.z:F2}m) is tight for full courtyard exploration. " +
                    "Consider increasing Surface Mesh Scale while keeping studio instruments at 1:1 world scale.");
            }

            // 2. Determine Surface-Snapped Anchor & Orientation
            Vector3 desiredXZ = new Vector3(
                groundBounds.center.x + workbenchNormalizedXZ.x * groundBounds.size.x,
                groundBounds.max.y,
                groundBounds.center.z + workbenchNormalizedXZ.y * groundBounds.size.z);

            Vector3 tableGroundPos = SampleSurfacePoint(groundCollider, desiredXZ, out _);
            Vector3 tableForward = groundBounds.size.x >= groundBounds.size.z ? Vector3.right : Vector3.forward;
            tableForward = Vector3.ProjectOnPlane(tableForward, Vector3.up).normalized;
            if (tableForward.sqrMagnitude < 0.001f) tableForward = Vector3.forward;
            Quaternion tableRot = Quaternion.LookRotation(tableForward, Vector3.up);

            // 3. Daylight Sun & Studio Lighting
            Light sunLight = CreateDirectionalSun(envRoot.transform);

            // 4. Canonical Studio Workbench (0.80m table, 0.63x0.89m paper, 0.50m carbon-arc lamp, trays, 1900s tools)
            WorkbenchRefs wb = BuildStudioWorkbench(envRoot.transform, groundCollider, tableGroundPos, tableRot, fluidCtrl);

            // 5. Canonical First-Person Hybrid Player Rig
            PlayerRefs player = BuildFirstPersonPlayer(groundCollider, wb);

            // 6. Procedural 3D Flora & Rocks scattered across the surface mesh
            BuildProceduralFloraAndRocks(envRoot.transform, groundCollider, tableGroundPos);

            // 7. 2D Human NPC in 3D Courtyard Space
            if (include2DNpc)
            {
                Build2DStudioNpc(envRoot.transform, groundCollider, tableGroundPos, tableRot);
            }

            // 8. Screen-Space & World-Space Prompt Canvases (RectTransform-safe)
            CyanotypeWorldPromptBuilder.BuildPromptUI();

            // 9. Cross-Wire All Serialized Inspector References
            WireSimulationReferences(envRig, fluidCtrl, envToggle, recipeComp, sunLight, wb, player);

            // 10. Save Scene & Frame in SceneView
            string savedPath = null;
            if (saveSceneAsset)
            {
                savedPath = SaveAndRegisterScene(surfaceMesh.name);
            }

            Selection.activeGameObject = wb.WorkbenchRoot;
            if (SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.LookAt(wb.PaperSheet.transform.position, Quaternion.Euler(32f, -35f, 0f), 2.4f);
            }

            Debug.Log(
                $"[CyanotypeSceneCreator] Playable scene built on '{surfaceMesh.name}' " +
                $"(Anchor: {tableGroundPos}, Sheet: {SheetWidthM}m x {SheetHeightM}m, Lamp Height: {CarbonArcLampHeightM}m)" +
                (string.IsNullOrEmpty(savedPath) ? "." : $" -> Saved to {savedPath}"));
        }

        private struct WorkbenchRefs
        {
            public GameObject WorkbenchRoot;
            public GameObject TableTop;
            public CyanotypeWorkbenchStation Station;
            public Transform PlayerStandPoint;
            public GameObject PaperSheet;
            public MeshFilter PaperMeshFilter;
            public MeshRenderer PaperRenderer;
            public Component DraggablePaper;
            public GameObject HakeBrush;
            public GameObject GlassRod;
            public GameObject BotanicalStencil;
            public GameObject CarbonArcLamp;
            public Light LampLight;
            public List<GameObject> Trays;
        }

        private struct PlayerRefs
        {
            public GameObject PlayerRoot;
            public Camera MainCamera;
            public GameObject ArmsRig;
            public Transform ToolHoldAnchor;
            public Transform CarrySocket;
            public GameObject HeadGroup;
            public GameObject TorsoAndLegs;
            public Renderer[] HeadRenderers;
            public Renderer[] TorsoLegRenderers;
            public Renderer[] ArmRenderers;
        }

        private WorkbenchRefs BuildStudioWorkbench(
            Transform parent,
            MeshCollider groundCollider,
            Vector3 groundPos,
            Quaternion rot,
            Component fluidCtrl)
        {
            var refs = new WorkbenchRefs { Trays = new List<GameObject>() };

            GameObject wbRoot = new GameObject("Cyanotype_StudioWorkbench");
            wbRoot.transform.SetParent(parent, false);
            wbRoot.transform.SetPositionAndRotation(groundPos, rot);
            refs.WorkbenchRoot = wbRoot;

            // Workbench_TableTop (top surface at exactly 0.80m above groundPos)
            const float tableWidth = 1.90f;
            const float tableDepth = 1.15f;
            const float slabThickness = 0.06f;

            GameObject tableTop = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tableTop.name = "Workbench_TableTop";
            tableTop.transform.SetParent(wbRoot.transform, false);
            tableTop.transform.localPosition = new Vector3(0f, WorkbenchHeightM - slabThickness * 0.5f, 0f);
            tableTop.transform.localScale = new Vector3(tableWidth, slabThickness, tableDepth);
            tableTop.GetComponent<MeshRenderer>().sharedMaterial = CreateDefaultLitMaterial("WorkbenchWood_Mat", new Color(0.28f, 0.20f, 0.14f));
            refs.TableTop = tableTop;

            // Workbench_PlayerStandPoint (0.92m back from table center, snapped to surface mesh)
            GameObject standPoint = new GameObject("Workbench_PlayerStandPoint");
            standPoint.transform.SetParent(wbRoot.transform, false);
            Vector3 rawStandWorld = wbRoot.transform.TransformPoint(new Vector3(0f, 0f, -PlayerStandBackM));
            Vector3 snappedStandWorld = SampleSurfacePoint(groundCollider, rawStandWorld, out _);
            standPoint.transform.position = snappedStandWorld;
            Vector3 toBench = wbRoot.transform.position - snappedStandWorld;
            toBench.y = 0f;
            standPoint.transform.rotation = toBench.sqrMagnitude > 1e-4f
                ? Quaternion.LookRotation(toBench.normalized, Vector3.up)
                : rot;
            refs.PlayerStandPoint = standPoint.transform;

            var station = tableTop.AddComponent<CyanotypeWorkbenchStation>();
            var stationSO = new SerializedObject(station);
            stationSO.FindProperty("_benchStandPoint").objectReferenceValue = standPoint.transform;
            stationSO.ApplyModifiedPropertiesWithoutUndo();
            refs.Station = station;

            var benchInteractable = tableTop.AddComponent<WorldInteractableItem>();
            benchInteractable.ConfigureProcedural(WorldInteractableKind.CyanotypeWorkbench, "Use Cyanotype Workbench", false);
            var evtField = typeof(WorldInteractableItem).GetField("_onInteracted", BindingFlags.NonPublic | BindingFlags.Instance);
            if (evtField?.GetValue(benchInteractable) is UnityEngine.Events.UnityEvent<CyanotypeWorldInteractor> evt)
            {
                UnityEventTools.AddPersistentListener(evt, station.EnterWorkbench);
            }

            // Four table legs grounded to surface
            float legHeight = WorkbenchHeightM - slabThickness;
            Vector3[] legOffsets =
            {
                new Vector3(-tableWidth * 0.44f, legHeight * 0.5f, -tableDepth * 0.42f),
                new Vector3( tableWidth * 0.44f, legHeight * 0.5f, -tableDepth * 0.42f),
                new Vector3(-tableWidth * 0.44f, legHeight * 0.5f,  tableDepth * 0.42f),
                new Vector3( tableWidth * 0.44f, legHeight * 0.5f,  tableDepth * 0.42f)
            };
            Material legMat = tableTop.GetComponent<MeshRenderer>().sharedMaterial;
            for (int i = 0; i < legOffsets.Length; i++)
            {
                GameObject leg = GameObject.CreatePrimitive(PrimitiveType.Cube);
                leg.name = $"Workbench_Leg_{i + 1}";
                leg.transform.SetParent(wbRoot.transform, false);
                leg.transform.localPosition = legOffsets[i];
                leg.transform.localScale = new Vector3(0.07f, legHeight, 0.07f);
                leg.GetComponent<MeshRenderer>().sharedMaterial = legMat;
            }

            // Cyanotype_PaperSheet_089m (0.63m x 0.89m Quad lying flat at y = 0.802m)
            GameObject paper = GameObject.CreatePrimitive(PrimitiveType.Quad);
            paper.name = "Cyanotype_PaperSheet_089m";
            paper.transform.SetParent(wbRoot.transform, false);
            paper.transform.localPosition = new Vector3(0f, WorkbenchHeightM + 0.002f, -0.04f);
            paper.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            paper.transform.localScale = new Vector3(SheetWidthM, SheetHeightM, 1f);

            MeshFilter paperMf = paper.GetComponent<MeshFilter>();
            MeshRenderer paperMr = paper.GetComponent<MeshRenderer>();

            EnsureMaterialsDirectory();
            Shader displayShader = Shader.Find("Cyanotype/SurfaceDisplay");
            Material displayMat = CreateAssetMaterial(displayShader, "Assets/Cyanotype/Materials/M_CyanotypeDisplay.mat");
            if (displayMat != null)
                paperMr.sharedMaterial = displayMat;
            else
                paperMr.sharedMaterial = CreateDefaultLitMaterial("CyanotypePaperFallback_Mat", new Color(0.95f, 0.94f, 0.90f));

            refs.DraggablePaper = AddComponentByName(paper, "DraggableCyanotypePaper");
            refs.PaperSheet = paper;
            refs.PaperMeshFilter = paperMf;
            refs.PaperRenderer = paperMr;

            // Botanical_GlassStencil_OD105 (Base-10 OD = 1.05)
            GameObject stencil = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stencil.name = "Botanical_GlassStencil_OD105";
            stencil.transform.SetParent(wbRoot.transform, false);
            stencil.transform.localPosition = new Vector3(0f, WorkbenchHeightM + 0.035f, -0.04f);
            stencil.transform.localScale = new Vector3(0.24f, 0.004f, 0.32f);
            stencil.GetComponent<MeshRenderer>().sharedMaterial = CreateDefaultLitMaterial("BotanicalGlassStencil_Mat", new Color(0.22f, 0.34f, 0.24f, 0.75f));
            var stencilInteractable = stencil.AddComponent<CyanotypePhysicsInteractable>();
            stencilInteractable.ConfigureFromEditor(CyanotypeToolRole.ShadowOrStencilProp, 0.05f, 0f, BotanicalStencilBase10OD);
            AddComponentByName(stencil, "DraggableShadowProp");
            Component stencilComp = AddComponentByName(stencil, "CyanotypeStencilProp");
            TryAssignFieldOrProperty(stencilComp, BotanicalStencilBase10OD, "opticalDensity", "base10OD", "inkOD");
            refs.BotanicalStencil = stencil;

            // Period-accurate 1900s Applicators (Hake Brush & Glass Coating Rod)
            refs.HakeBrush = CreateToolProp(
                wbRoot.transform,
                "HakeBrush_1900s",
                new Vector3(0.56f, WorkbenchHeightM + 0.015f, -0.22f),
                new Vector3(0.07f, 0.016f, 0.24f),
                new Color(0.76f, 0.65f, 0.46f),
                0.035f,
                0.20f);

            refs.GlassRod = CreateToolProp(
                wbRoot.transform,
                "GlassCoatingRod_1900s",
                new Vector3(0.72f, WorkbenchHeightM + 0.012f, -0.22f),
                new Vector3(0.022f, 0.018f, 0.34f),
                new Color(0.78f, 0.90f, 0.92f),
                0.024f,
                0.16f);

            // Five Wet-Process & Stock Trays
            (string trayName, Vector3 localPos, Color tint, CyanotypeToolRole role)[] traySpecs =
            {
                ("Tray_SensitizerStock",      new Vector3(-0.66f, WorkbenchHeightM + 0.02f, -0.26f), new Color(0.76f, 0.78f, 0.22f), CyanotypeToolRole.SensitizerApplicator),
                ("Tray_WaterWash",            new Vector3(-0.66f, WorkbenchHeightM + 0.02f,  0.14f), new Color(0.32f, 0.58f, 0.82f), CyanotypeToolRole.WaterWashApplicator),
                ("Tray_AlkaliBleach_SodaAsh", new Vector3(-0.34f, WorkbenchHeightM + 0.02f,  0.42f), new Color(0.88f, 0.85f, 0.76f), CyanotypeToolRole.AlkaliBleachBath),
                ("Tray_TanninToner",          new Vector3( 0.02f, WorkbenchHeightM + 0.02f,  0.42f), new Color(0.36f, 0.20f, 0.12f), CyanotypeToolRole.TanninTonerBath),
                ("Tray_PeroxideOxidizer",     new Vector3( 0.38f, WorkbenchHeightM + 0.02f,  0.42f), new Color(0.72f, 0.86f, 0.94f), CyanotypeToolRole.PeroxideOxidizerBath)
            };

            foreach (var spec in traySpecs)
            {
                GameObject tray = GameObject.CreatePrimitive(PrimitiveType.Cube);
                tray.name = spec.trayName;
                tray.transform.SetParent(wbRoot.transform, false);
                tray.transform.localPosition = spec.localPos;
                tray.transform.localScale = new Vector3(0.26f, 0.04f, 0.22f);
                tray.GetComponent<MeshRenderer>().sharedMaterial = CreateDefaultLitMaterial(spec.trayName + "_Mat", spec.tint);

                var col = tray.GetComponent<BoxCollider>();
                col.isTrigger = true;

                var surfacePoint = new GameObject("LiquidSurfacePoint").transform;
                surfacePoint.SetParent(tray.transform, false);
                surfacePoint.localPosition = new Vector3(0f, 0.52f, 0f);

                var dipTray = tray.AddComponent<CyanotypeDipTray>();
                var dipSo = new SerializedObject(dipTray);
                dipSo.FindProperty("_traySolutionRole").enumValueIndex = (int)spec.role;
                dipSo.FindProperty("_liquidSurfacePoint").objectReferenceValue = surfacePoint;
                dipSo.ApplyModifiedPropertiesWithoutUndo();

                if (spec.role == CyanotypeToolRole.WaterWashApplicator)
                {
                    Component waterTrigger = AddComponentByName(tray, "WaterTrayTrigger");
                    TryAssignFieldOrProperty(waterTrigger, fluidCtrl, "fluidController");
                }
                else
                {
                    AddComponentByName(tray, "CyanotypePropTray");
                }
                refs.Trays.Add(tray);
            }

            // CarbonArcLamp_1900s (0.50m above paper center -> normalized 1.0 irradiance in CY_LampFalloff)
            Vector3 paperCenterLocal = paper.transform.localPosition;
            GameObject lampRoot = new GameObject("CarbonArcLamp_1900s");
            lampRoot.transform.SetParent(wbRoot.transform, false);
            lampRoot.transform.localPosition = paperCenterLocal + Vector3.up * CarbonArcLampHeightM;
            lampRoot.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            GameObject lampHousing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            lampHousing.name = "CarbonArc_ReflectorHood";
            lampHousing.transform.SetParent(lampRoot.transform, false);
            lampHousing.transform.localScale = new Vector3(0.18f, 0.07f, 0.18f);
            DestroyImmediate(lampHousing.GetComponent<Collider>());
            lampHousing.GetComponent<MeshRenderer>().sharedMaterial = CreateDefaultLitMaterial("BrassCarbonArc_Mat", new Color(0.55f, 0.44f, 0.22f));

            Light arcLight = lampRoot.AddComponent<Light>();
            arcLight.type = LightType.Spot;
            arcLight.range = 3.0f;
            arcLight.spotAngle = 82f;
            arcLight.intensity = 2.4f;
            arcLight.color = new Color(0.84f, 0.90f, 1.0f);
            arcLight.shadows = LightShadows.Soft;

            refs.CarbonArcLamp = lampRoot;
            refs.LampLight = arcLight;

            return refs;
        }

        private GameObject CreateToolProp(
            Transform parent,
            string name,
            Vector3 localPos,
            Vector3 localScale,
            Color color,
            float radiusWorld,
            float depositPerTick)
        {
            GameObject tool = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tool.name = name;
            tool.transform.SetParent(parent, false);
            tool.transform.localPosition = localPos;
            tool.transform.localScale = localScale;
            tool.GetComponent<MeshRenderer>().sharedMaterial = CreateDefaultLitMaterial(name + "_Mat", color);

            var tip = new GameObject("ContactTip").transform;
            tip.SetParent(tool.transform, false);
            tip.localPosition = new Vector3(0f, 0f, 0.5f);

            var rb = tool.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            var interactable = tool.AddComponent<CyanotypePhysicsInteractable>();
            interactable.ConfigureFromEditor(CyanotypeToolRole.SensitizerApplicator, radiusWorld, depositPerTick, 0f);
            var so = new SerializedObject(interactable);
            so.FindProperty("_contactTip").objectReferenceValue = tip;
            so.ApplyModifiedPropertiesWithoutUndo();

            AddComponentByName(tool, "CyanotypeDiegeticUXBinder");
            return tool;
        }

        private PlayerRefs BuildFirstPersonPlayer(MeshCollider groundCollider, WorkbenchRefs wb)
        {
            var refs = new PlayerRefs();

            GameObject playerRoot = new GameObject("Cyanotype_FirstPersonPlayer");
            Vector3 spawnPos = SampleSurfacePoint(groundCollider, wb.PlayerStandPoint.position, out _);
            playerRoot.transform.SetPositionAndRotation(spawnPos + Vector3.up * 0.02f, wb.PlayerStandPoint.rotation);
            refs.PlayerRoot = playerRoot;

            var cc = playerRoot.AddComponent<CharacterController>();
            cc.height = 1.75f;
            cc.radius = 0.32f;
            cc.center = new Vector3(0f, 0.875f, 0f);
            cc.stepOffset = 0.35f;
            cc.slopeLimit = 45f;

            // FirstPersonCamera (nearClipPlane = 0.03m)
            GameObject camGo = new GameObject("FirstPersonCamera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(playerRoot.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 1.62f, 0f);
            camGo.transform.localRotation = Quaternion.Euler(22f, 0f, 0f);

            Camera cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 250f;
            cam.fieldOfView = 62f;
            camGo.AddComponent<AudioListener>();
            refs.MainCamera = cam;

            Color coatColor = new Color(0.22f, 0.24f, 0.28f);
            Color skinColor = new Color(0.78f, 0.64f, 0.54f);
            Material coatMat = CreateDefaultLitMaterial("PlayerCoat_Mat", coatColor);
            Material skinMat = CreateDefaultLitMaterial("PlayerSkin_Mat", skinColor);

            // FirstPerson_ArmsRig + ToolHoldAnchor + World_CarrySocket
            GameObject armsRig = new GameObject("FirstPerson_ArmsRig");
            armsRig.transform.SetParent(camGo.transform, false);
            refs.ArmsRig = armsRig;

            var rightArm = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            rightArm.name = "Forearm_R";
            rightArm.transform.SetParent(armsRig.transform, false);
            rightArm.transform.localPosition = new Vector3(0.24f, -0.22f, 0.28f);
            rightArm.transform.localRotation = Quaternion.Euler(72f, -14f, 8f);
            rightArm.transform.localScale = new Vector3(0.075f, 0.18f, 0.075f);
            DestroyImmediate(rightArm.GetComponent<Collider>());
            rightArm.GetComponent<MeshRenderer>().sharedMaterial = coatMat;

            var rightHand = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            rightHand.name = "Hand_R";
            rightHand.transform.SetParent(armsRig.transform, false);
            rightHand.transform.localPosition = new Vector3(0.21f, -0.17f, 0.43f);
            rightHand.transform.localScale = new Vector3(0.075f, 0.055f, 0.09f);
            DestroyImmediate(rightHand.GetComponent<Collider>());
            rightHand.GetComponent<MeshRenderer>().sharedMaterial = skinMat;

            var leftArm = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            leftArm.name = "Forearm_L";
            leftArm.transform.SetParent(armsRig.transform, false);
            leftArm.transform.localPosition = new Vector3(-0.26f, -0.25f, 0.25f);
            leftArm.transform.localRotation = Quaternion.Euler(76f, 18f, -10f);
            leftArm.transform.localScale = new Vector3(0.075f, 0.18f, 0.075f);
            DestroyImmediate(leftArm.GetComponent<Collider>());
            leftArm.GetComponent<MeshRenderer>().sharedMaterial = coatMat;

            refs.ArmRenderers = new Renderer[]
            {
                rightArm.GetComponent<Renderer>(),
                rightHand.GetComponent<Renderer>(),
                leftArm.GetComponent<Renderer>()
            };

            GameObject toolAnchor = new GameObject("Cyanotype_ToolHoldAnchor");
            toolAnchor.transform.SetParent(camGo.transform, false);
            toolAnchor.transform.localPosition = new Vector3(0.22f, -0.16f, 0.42f);
            toolAnchor.transform.localRotation = Quaternion.Euler(28f, -18f, 12f);
            refs.ToolHoldAnchor = toolAnchor.transform;

            GameObject carrySocket = new GameObject("World_CarrySocket");
            carrySocket.transform.SetParent(camGo.transform, false);
            carrySocket.transform.localPosition = new Vector3(0.24f, -0.22f, 0.55f);
            refs.CarrySocket = carrySocket.transform;

            // Body_HeadGroup (ShadowsOnly in 1st-person to prevent clipping while casting shadows)
            GameObject headGroup = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            headGroup.name = "Body_HeadGroup";
            headGroup.transform.SetParent(playerRoot.transform, false);
            headGroup.transform.localPosition = new Vector3(0f, 1.62f, 0f);
            headGroup.transform.localScale = new Vector3(0.22f, 0.25f, 0.24f);
            DestroyImmediate(headGroup.GetComponent<Collider>());
            var headRend = headGroup.GetComponent<MeshRenderer>();
            headRend.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            refs.HeadGroup = headGroup;
            refs.HeadRenderers = new Renderer[] { headRend };

            // Body_TorsoAndLegs (visible in ExplorationFullBody; hidden in ActiveCyanotypeArmsOnly)
            GameObject torsoAndLegs = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            torsoAndLegs.name = "Body_TorsoAndLegs";
            torsoAndLegs.transform.SetParent(playerRoot.transform, false);
            torsoAndLegs.transform.localPosition = new Vector3(0f, 0.78f, 0f);
            torsoAndLegs.transform.localScale = new Vector3(0.42f, 0.72f, 0.30f);
            DestroyImmediate(torsoAndLegs.GetComponent<Collider>());
            var torsoRend = torsoAndLegs.GetComponent<MeshRenderer>();
            torsoRend.sharedMaterial = coatMat;
            refs.TorsoAndLegs = torsoAndLegs;
            refs.TorsoLegRenderers = new Renderer[] { torsoRend };

            // Canonical Hybrid Rig & New Input System Controllers
            AddComponentByName(playerRoot, "CyanotypeInputSystemCameraRig");
            AddComponentByName(playerRoot, "CyanotypeInputSystemPlayerController");
            AddComponentByName(playerRoot, "CyanotypePlayerController");
            AddComponentByName(playerRoot, "CyanotypeCameraRig");

            var worldInteractor = playerRoot.AddComponent<CyanotypeWorldInteractor>();
            var interactionRig = playerRoot.AddComponent<CyanotypePlayerInteractionRig>();
            var rigMode = playerRoot.AddComponent<CyanotypeFirstPersonRigMode>();
            AddComponentByName(playerRoot, "CyanotypeWorldPromptHUD");

            var worldSo = new SerializedObject(worldInteractor);
            worldSo.FindProperty("_playerCamera").objectReferenceValue = cam;
            worldSo.FindProperty("_carrySocket").objectReferenceValue = carrySocket.transform;
            worldSo.ApplyModifiedPropertiesWithoutUndo();

            var benchSo = new SerializedObject(interactionRig);
            benchSo.FindProperty("_playerCamera").objectReferenceValue = cam;
            benchSo.FindProperty("_holdAnchor").objectReferenceValue = toolAnchor.transform;
            benchSo.ApplyModifiedPropertiesWithoutUndo();

            var modeSo = new SerializedObject(rigMode);
            AssignRendererArray(modeSo.FindProperty("_headRenderers"), refs.HeadRenderers);
            AssignRendererArray(modeSo.FindProperty("_torsoAndLegRenderers"), refs.TorsoLegRenderers);
            AssignRendererArray(modeSo.FindProperty("_firstPersonArmRenderers"), refs.ArmRenderers);
            modeSo.FindProperty("_worldInteractor").objectReferenceValue = worldInteractor;
            modeSo.FindProperty("_cyanotypeBenchInteractor").objectReferenceValue = interactionRig;
            modeSo.ApplyModifiedPropertiesWithoutUndo();

            return refs;
        }

        private void BuildProceduralFloraAndRocks(Transform parent, MeshCollider groundCollider, Vector3 workbenchPos)
        {
            if (proceduralFloraCount <= 0 && proceduralRockCount <= 0) return;

            GameObject scatterRoot = new GameObject("Procedural_3D_FloraAndRocks");
            scatterRoot.transform.SetParent(parent, false);

            Bounds bounds = groundCollider.bounds;
            var rng = new System.Random(1904);

            Material floraMat = CreateDefaultLitMaterial("ProceduralBotanical_Mat", new Color(0.22f, 0.42f, 0.20f));
            Material rockMat = CreateDefaultLitMaterial("ProceduralRock_Mat", new Color(0.42f, 0.41f, 0.39f));

            Mesh fernMesh = BuildProceduralFernMesh();
            Mesh rockMesh = BuildProceduralRockMesh();

            for (int i = 0; i < proceduralFloraCount; i++)
            {
                Vector3 sampleXZ = PickClearSurfaceXZ(bounds, workbenchPos, 2.3f, rng);
                Vector3 hitPos = SampleSurfacePoint(groundCollider, sampleXZ, out Vector3 normal);

                GameObject flora = new GameObject($"ProceduralBotanical_{i + 1:00}");
                flora.transform.SetParent(scatterRoot.transform, false);
                flora.transform.position = hitPos;
                flora.transform.rotation = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                flora.transform.localScale = Vector3.one * Mathf.Lerp(0.75f, 1.35f, (float)rng.NextDouble());

                flora.AddComponent<MeshFilter>().sharedMesh = fernMesh;
                flora.AddComponent<MeshRenderer>().sharedMaterial = floraMat;

                var col = flora.AddComponent<MeshCollider>();
                col.sharedMesh = fernMesh;
                col.convex = true;

                var item = flora.AddComponent<WorldInteractableItem>();
                item.ConfigureProcedural(WorldInteractableKind.ProceduralBotanical, "Gather Botanical Frond", true);
            }

            for (int i = 0; i < proceduralRockCount; i++)
            {
                Vector3 sampleXZ = PickClearSurfaceXZ(bounds, workbenchPos, 2.5f, rng);
                Vector3 hitPos = SampleSurfacePoint(groundCollider, sampleXZ, out Vector3 normal);

                GameObject rock = new GameObject($"ProceduralRock_{i + 1:00}");
                rock.transform.SetParent(scatterRoot.transform, false);
                float scale = Mathf.Lerp(0.75f, 1.45f, (float)rng.NextDouble());
                rock.transform.position = hitPos;
                rock.transform.rotation = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                rock.transform.localScale = Vector3.one * scale;

                rock.AddComponent<MeshFilter>().sharedMesh = rockMesh;
                rock.AddComponent<MeshRenderer>().sharedMaterial = rockMat;

                var col = rock.AddComponent<MeshCollider>();
                col.sharedMesh = rockMesh;
                col.convex = true;

                var rb = rock.AddComponent<Rigidbody>();
                rb.mass = 1.4f;

                var item = rock.AddComponent<WorldInteractableItem>();
                item.ConfigureProcedural(WorldInteractableKind.ProceduralRock, "Pick Up Field Stone", false);
            }
        }

        private void Build2DStudioNpc(Transform parent, MeshCollider groundCollider, Vector3 workbenchPos, Quaternion workbenchRot)
        {
            Vector3 offset = workbenchRot * new Vector3(2.1f, 0f, 0.8f);
            Vector3 npcPos = SampleSurfacePoint(groundCollider, workbenchPos + offset, out _);

            GameObject npcRoot = new GameObject("NPC_2D_Assistant");
            npcRoot.transform.SetParent(parent, false);
            npcRoot.transform.position = npcPos;
            Vector3 look = workbenchPos - npcPos;
            look.y = 0f;
            if (look.sqrMagnitude > 1e-4f)
                npcRoot.transform.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);

            var capsule = npcRoot.AddComponent<CapsuleCollider>();
            capsule.height = 1.72f;
            capsule.radius = 0.28f;
            capsule.center = new Vector3(0f, 0.86f, 0f);

            npcRoot.AddComponent<Animator>();
            AddComponentByName(npcRoot, "Cyanotype2DNPCController");
            var interactable = npcRoot.AddComponent<WorldInteractableItem>();
            interactable.ConfigureNPC("Studio Assistant");

            GameObject billboard = new GameObject("SpriteBillboard");
            billboard.transform.SetParent(npcRoot.transform, false);
            billboard.transform.localPosition = new Vector3(0f, 0.86f, 0f);

            var sr = billboard.AddComponent<SpriteRenderer>();
            sr.sprite = CreatePlaceholderNpcSprite();
            sr.shadowCastingMode = ShadowCastingMode.TwoSided;
        }

        private void WireSimulationReferences(
            Component envRig,
            Component fluidCtrl,
            Component envToggle,
            Component recipeComp,
            Light sunLight,
            WorkbenchRefs wb,
            PlayerRefs player)
        {
            EnsureMaterialsDirectory();
            Shader simShader = Shader.Find("Cyanotype/SimMultiSource") ?? Shader.Find("Cyanotype/PhotochemicalSimMultiSource");
            Shader fluidShader = Shader.Find("Cyanotype/FluidTransport");
            Shader bathShader = Shader.Find("Cyanotype/BathTransport");

            Material simMat = CreateAssetMaterial(simShader, "Assets/Cyanotype/Materials/M_CyanotypeSim.mat");
            Material fluidMat = CreateAssetMaterial(fluidShader, "Assets/Cyanotype/Materials/M_CyanotypeFluid.mat");
            Material bathMat = CreateAssetMaterial(bathShader, "Assets/Cyanotype/Materials/M_CyanotypeBath.mat");

            // Canonical CyanotypeEnvironmentRig fields
            TryAssignFieldOrProperty(envRig, wb.PaperMeshFilter, "paperMeshFilter");
            TryAssignFieldOrProperty(envRig, wb.PaperRenderer, "paperRenderer");
            TryAssignFieldOrProperty(envRig, 1 << 0, "shadowCasterLayers");
            TryAssignFieldOrProperty(envRig, sunLight, "sunLight");
            TryAssignFieldOrProperty(envRig, wb.LampLight, "uvLampLight");
            TryAssignFieldOrProperty(envRig, true, "lampIsOn");
            TryAssignFieldOrProperty(envRig, 2.6f, "lampUVIntensity");
            TryAssignFieldOrProperty(envRig, 0.08f, "lampSize");
            TryAssignFieldOrProperty(envRig, 0.0047f, "sunPenumbra");
            TryAssignFieldOrProperty(envRig, 1.2f, "peakSunUVIntensity");
            TryAssignFieldOrProperty(envRig, 0.03f, "windowAmbientUV");
            TryAssignFieldOrProperty(envRig, 0.0f, "coatingThickness");
            TryAssignFieldOrProperty(envRig, simMat, "simMaterial");
            TryAssignFieldOrProperty(envRig, wb.PaperRenderer != null ? wb.PaperRenderer.sharedMaterial : null, "displayMaterial");
            TryAssignFieldOrProperty(envRig, recipeComp, "recipe");

            // Canonical CyanotypeFluidController fields
            TryAssignFieldOrProperty(fluidCtrl, envRig, "rig");
            TryAssignFieldOrProperty(fluidCtrl, wb.PaperSheet.transform, "paperTransform");
            TryAssignFieldOrProperty(fluidCtrl, fluidMat, "fluidMaterial");
            TryAssignFieldOrProperty(fluidCtrl, bathMat, "bathMaterial");

            // StudioEnvironmentToggle & DraggableCyanotypePaper
            TryAssignFieldOrProperty(envToggle, envRig, "rig");
            TryAssignFieldOrProperty(wb.DraggablePaper, fluidCtrl, "fluidController");
            TryAssignFieldOrProperty(wb.DraggablePaper, wb.PaperSheet.transform.position.y, "restingY");
        }

        private static void ConfigureStandardRecipe(Component recipeComp)
        {
            if (recipeComp == null) return;
            TryAssignFieldOrProperty(recipeComp, StandardFacGramsPer100mL, "facGramsPer100mL", "facConcentration");
            TryAssignFieldOrProperty(recipeComp, StandardKfcGramsPer100mL, "kfcGramsPer100mL", "kfcConcentration");
        }

        private static Light CreateDirectionalSun(Transform parent)
        {
            GameObject sunGo = new GameObject("Sun_DirectionalLight");
            sunGo.transform.SetParent(parent, false);
            sunGo.transform.rotation = Quaternion.Euler(52f, -38f, 0f);

            Light light = sunGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.6f;
            light.color = new Color(1.0f, 0.96f, 0.90f);
            light.shadows = LightShadows.Soft;
            return light;
        }

        private static Vector3 SampleSurfacePoint(MeshCollider collider, Vector3 approxPos, out Vector3 normal)
        {
            Bounds b = collider.bounds;
            Vector3 origin = new Vector3(approxPos.x, b.max.y + 10f, approxPos.z);
            Ray ray = new Ray(origin, Vector3.down);
            if (collider.Raycast(ray, out RaycastHit hit, b.size.y + 25f))
            {
                normal = hit.normal;
                return hit.point;
            }

            normal = Vector3.up;
            return new Vector3(approxPos.x, b.max.y, approxPos.z);
        }

        private static Vector3 PickClearSurfaceXZ(Bounds bounds, Vector3 avoidCenter, float minClearance, System.Random rng)
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                float rx = Mathf.Lerp(bounds.min.x + 0.5f, bounds.max.x - 0.5f, (float)rng.NextDouble());
                float rz = Mathf.Lerp(bounds.min.z + 0.5f, bounds.max.z - 0.5f, (float)rng.NextDouble());
                Vector3 candidate = new Vector3(rx, bounds.max.y, rz);
                if (Vector2.Distance(new Vector2(rx, rz), new Vector2(avoidCenter.x, avoidCenter.z)) >= minClearance)
                {
                    return candidate;
                }
            }
            return avoidCenter + new Vector3(minClearance, 0f, minClearance);
        }

        private static Mesh BuildProceduralFernMesh()
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            int fronds = 6;
            int pairs = 5;
            float length = 0.34f;

            for (int f = 0; f < fronds; f++)
            {
                Quaternion rot = Quaternion.Euler(0f, (360f / fronds) * f, 0f);
                for (int p = 0; p < pairs; p++)
                {
                    float t = (p + 0.5f) / pairs;
                    Vector3 spine = rot * new Vector3(0f, Mathf.Sin(t * Mathf.PI * 0.7f) * 0.11f, t * length);
                    float w = Mathf.Sin(t * Mathf.PI) * 0.045f + 0.01f;

                    int idx = verts.Count;
                    verts.Add(spine);
                    verts.Add(spine + rot * new Vector3(-w, 0.005f, 0.03f));
                    verts.Add(spine + rot * new Vector3(0f, -0.004f, 0.06f));
                    verts.Add(spine + rot * new Vector3(w, 0.005f, 0.03f));

                    tris.Add(idx); tris.Add(idx + 1); tris.Add(idx + 2);
                    tris.Add(idx); tris.Add(idx + 2); tris.Add(idx + 3);
                    tris.Add(idx); tris.Add(idx + 2); tris.Add(idx + 1);
                    tris.Add(idx); tris.Add(idx + 3); tris.Add(idx + 2);
                }
            }

            var mesh = new Mesh { name = "Procedural_Botanical_Mesh" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh BuildProceduralRockMesh()
        {
            var temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Mesh mesh = Instantiate(temp.GetComponent<MeshFilter>().sharedMesh);
            DestroyImmediate(temp);

            Vector3[] verts = mesh.vertices;
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 d = verts[i].normalized;
                float n = Mathf.PerlinNoise(d.x * 2.2f + 13.7f, d.z * 2.2f + d.y * 1.9f);
                Vector3 p = d * (0.12f * (0.82f + n * 0.38f));
                if (p.y < -0.05f) p.y = -0.05f;
                p.y += 0.05f;
                verts[i] = p;
            }
            mesh.vertices = verts;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void EnsureMaterialsDirectory()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Cyanotype"))
                AssetDatabase.CreateFolder("Assets", "Cyanotype");
            if (!AssetDatabase.IsValidFolder("Assets/Cyanotype/Materials"))
                AssetDatabase.CreateFolder("Assets/Cyanotype", "Materials");
        }

        private static Material CreateAssetMaterial(Shader shader, string path)
        {
            if (shader == null) return null;

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

        private static Material CreateDefaultLitMaterial(string name, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var mat = new Material(shader) { name = name };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            return mat;
        }

        private static Sprite CreatePlaceholderNpcSprite()
        {
            const int w = 64;
            const int h = 128;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                name = "NPC_2D_PlaceholderTex"
            };

            Color clear = new Color(0f, 0f, 0f, 0f);
            Color coat = new Color(0.18f, 0.25f, 0.36f, 1f);
            Color skin = new Color(0.86f, 0.72f, 0.60f, 1f);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool inBody = x >= 18 && x <= 46 && y >= 8 && y < 96;
                    bool inHead = Vector2.Distance(new Vector2(x, y), new Vector2(32f, 110f)) <= 11f;
                    tex.SetPixel(x, y, inHead ? skin : (inBody ? coat : clear));
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 74f);
        }

        private string SaveAndRegisterScene(string meshName)
        {
            if (!Directory.Exists(outputFolder))
            {
                Directory.CreateDirectory(outputFolder);
                AssetDatabase.Refresh();
            }

            string sanitized = string.Concat(meshName.Split(Path.GetInvalidFileNameChars()));
            string scenePath = AssetDatabase.GenerateUniqueAssetPath($"{outputFolder}/Cyanotype_{sanitized}_Playable.unity");

            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), scenePath);
            AssetDatabase.Refresh();

            var buildScenes = EditorBuildSettings.scenes.ToList();
            if (buildScenes.All(s => s.path != scenePath))
            {
                buildScenes.Add(new EditorBuildSettingsScene(scenePath, true));
                EditorBuildSettings.scenes = buildScenes.ToArray();
            }

            return scenePath;
        }

        private static Type ResolveProjectType(string typeName)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type exact = asm.GetType(typeName);
                if (exact != null) return exact;

                Type byShortName = asm.GetTypes().FirstOrDefault(t => t.Name == typeName);
                if (byShortName != null) return byShortName;
            }
            return null;
        }

        private static Component AddComponentByName(GameObject target, string typeName)
        {
            if (target == null) return null;
            Type t = ResolveProjectType(typeName);
            if (t == null || !typeof(Component).IsAssignableFrom(t)) return null;
            return target.GetComponent(t) ?? target.AddComponent(t);
        }

        private static void AssignRendererArray(SerializedProperty prop, Renderer[] renderers)
        {
            if (prop == null) return;
            prop.arraySize = renderers.Length;
            for (int i = 0; i < renderers.Length; i++)
            {
                prop.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
            }
        }

        private static void TryAssignFieldOrProperty(Component target, object value, params string[] candidateNames)
        {
            if (target == null || value == null) return;

            Type t = target.GetType();
            foreach (string name in candidateNames)
            {
                FieldInfo field = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null && field.FieldType.IsInstanceOfType(value))
                {
                    field.SetValue(target, value);
                    EditorUtility.SetDirty(target);
                    return;
                }
            }

            var so = new SerializedObject(target);
            foreach (string name in candidateNames)
            {
                SerializedProperty prop = so.FindProperty(name);
                if (prop == null) continue;

                switch (prop.propertyType)
                {
                    case SerializedPropertyType.ObjectReference when value is UnityEngine.Object unityObj:
                        prop.objectReferenceValue = unityObj;
                        so.ApplyModifiedPropertiesWithoutUndo();
                        return;
                    case SerializedPropertyType.Float when value is float f:
                        prop.floatValue = f;
                        so.ApplyModifiedPropertiesWithoutUndo();
                        return;
                    case SerializedPropertyType.Integer when value is int i:
                        prop.intValue = i;
                        so.ApplyModifiedPropertiesWithoutUndo();
                        return;
                    case SerializedPropertyType.Boolean when value is bool b:
                        prop.boolValue = b;
                        so.ApplyModifiedPropertiesWithoutUndo();
                        return;
                }
            }
        }
    }
}
#endif
