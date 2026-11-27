#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Rendering;
using Cyanotype.Gameplay;
using Cyanotype.World;

namespace Cyanotype.EditorTools
{
    public class CyanotypeSceneBootstrapWindow : EditorWindow
    {
        [Header("Optional Custom Prefabs (Leave Null to Auto-Generate)")]
        private GameObject _customPlayer3DModelPrefab;
        private GameObject _custom2DNPCPrefab;

        [Header("Environment & Procedural Scatter")]
        private int _worldSeed = 1904;
        private int _proceduralPlantCount = 12;
        private int _proceduralRockCount = 8;
        private float _courtyardRadius = 11f;
        private bool _include2DNPC = true;
        private bool _buildPromptHUD = true;

        // Project architectural constants (do not alter scale invariants)
        private const float SheetHeightWorld = 0.89f;
        private const float SheetWidthWorld = 0.63f; // ~3x A4 aspect ratio
        private const float CarbonArcNominalDist = 0.50f; // Normalized 1.0 irradiance at 0.5m
        private const float DefaultStencilOD10 = 1.05f; // Base-10 optical density

        [MenuItem("Cyanotype/Setup First-Person Player & Environment")]
        public static void OpenWindow()
        {
            var win = GetWindow<CyanotypeSceneBootstrapWindow>("Cyanotype Scene Setup");
            win.minSize = new Vector2(440f, 410f);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("First-Person 3D Player & Environment Bootstrapper", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Builds a complete scene with a 3D first-person player (Full-Body Exploration + Arms-Only Cyanotype mode), " +
                "1900s studio workbench (0.89m sheet, hake brush, glass rod, 5 bath trays, 0.50m carbon-arc lamp), " +
                "procedural 3D plants/rocks, and a 2D animated human NPC.",
                MessageType.Info);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("1. Optional Custom Models", EditorStyles.boldLabel);
            _customPlayer3DModelPrefab = (GameObject)EditorGUILayout.ObjectField(
                "3D Player Model Prefab (Opt)", _customPlayer3DModelPrefab, typeof(GameObject), false);
            _custom2DNPCPrefab = (GameObject)EditorGUILayout.ObjectField(
                "2D NPC Prefab (Opt)", _custom2DNPCPrefab, typeof(GameObject), false);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("2. Procedural World & NPC Settings", EditorStyles.boldLabel);
            _worldSeed = EditorGUILayout.IntField("Deterministic Seed", _worldSeed);
            _proceduralPlantCount = EditorGUILayout.IntSlider("Procedural 3D Plants", _proceduralPlantCount, 0, 40);
            _proceduralRockCount = EditorGUILayout.IntSlider("Procedural 3D Rocks", _proceduralRockCount, 0, 30);
            _courtyardRadius = EditorGUILayout.Slider("Courtyard Radius (m)", _courtyardRadius, 5f, 25f);
            _include2DNPC = EditorGUILayout.Toggle("Spawn 2D Animated Human NPC", _include2DNPC);
            _buildPromptHUD = EditorGUILayout.Toggle("Build Screen & World Prompt UI", _buildPromptHUD);

            EditorGUILayout.Space(14);
            if (GUILayout.Button("Build Working Scene & First-Person Rig", GUILayout.Height(38)))
            {
                BuildFullWorkingScene();
            }
        }

        private void BuildFullWorkingScene()
        {
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();

            // 1. Lighting & Base Studio Controllers
            Light sunLight = EnsureDirectionalSunLight();
            GameObject studioRoot = BuildStudioAndEnvironmentRoot();

            // 2. 1900s Cyanotype Workbench (0.89m paper, hake brush, glass rod, trays, carbon-arc lamp)
            Vector3 benchCenter = new Vector3(0f, 0f, 2.2f);
            BuildCyanotypeWorkbench(studioRoot, benchCenter, sunLight);

            // 3. First-Person 3D Player Rig (ExplorationFullBody <-> ActiveCyanotypeArmsOnly)
            GameObject playerRoot = BuildFirstPersonPlayerRig(new Vector3(0f, 0f, 0.6f));

            // 4. Procedural 3D Plants & Rocks around the courtyard
            ScatterProceduralEnvironment(studioRoot.transform, benchCenter);

            // 5. 2D Animated Human NPC in 3D space
            if (_include2DNPC)
            {
                Spawn2DAnimatedNPC(studioRoot.transform, new Vector3(2.4f, 0f, 3.1f));
            }

            // 6. Wire Screen-Space & World-Space Prompt HUD
            if (_buildPromptHUD)
            {
                CyanotypeWorldPromptBuilder.BuildPromptUI();
            }

            Undo.CollapseUndoOperations(undoGroup);
            Selection.activeGameObject = playerRoot;
            Debug.Log("[Cyanotype] Scene bootstrap complete: First-Person 3D Player, 1900s Workbench, Procedural Flora/Rocks, and 2D NPC ready.");
        }

        // -------------------------------------------------------------------------
        // 1. Studio Root & Existing Base Project Scripts
        // -------------------------------------------------------------------------
        private GameObject BuildStudioAndEnvironmentRoot()
        {
            var existing = GameObject.Find("Cyanotype_EnvironmentRoot");
            if (existing != null) Undo.DestroyObjectImmediate(existing);

            var root = new GameObject("Cyanotype_EnvironmentRoot");
            Undo.RegisterCreatedObjectUndo(root, "Create Cyanotype Environment");

            // Attach core base-project controllers if present in assembly
            TryAddComponentByName(root, "CyanotypeRecipe");
            TryAddComponentByName(root, "CyanotypeEnvironmentRig");
            TryAddComponentByName(root, "CyanotypeFluidController");
            TryAddComponentByName(root, "StudioEnvironmentToggle");

            // Courtyard ground plane
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Courtyard_Ground";
            ground.transform.SetParent(root.transform, false);
            ground.transform.localScale = new Vector3(_courtyardRadius * 0.25f, 1f, _courtyardRadius * 0.25f);
            ApplyTintMaterial(ground.GetComponent<Renderer>(), new Color(0.36f, 0.38f, 0.33f));

            return root;
        }

        private static Light EnsureDirectionalSunLight()
        {
#if UNITY_6000_3_OR_NEWER
            var lights = FindObjectsByType<Light>();
#else
            var lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
#endif
            Light sun = lights.FirstOrDefault(l => l.type == LightType.Directional);
            if (sun == null)
            {
                var sunGo = new GameObject("Sun_DirectionalLight");
                Undo.RegisterCreatedObjectUndo(sunGo, "Create Sun Light");
                sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional;
            }

            sun.transform.rotation = Quaternion.Euler(52f, -32f, 0f);
            sun.intensity = 1.25f;
            sun.shadows = LightShadows.Soft;
            return sun;
        }

        // -------------------------------------------------------------------------
        // 2. First-Person 3D Player Rig
        // -------------------------------------------------------------------------
        private GameObject BuildFirstPersonPlayerRig(Vector3 spawnPosition)
        {
            var existing = GameObject.Find("Cyanotype_FirstPersonPlayer");
            if (existing != null) Undo.DestroyObjectImmediate(existing);

            var playerRoot = new GameObject("Cyanotype_FirstPersonPlayer");
            Undo.RegisterCreatedObjectUndo(playerRoot, "Create First-Person Player");
            playerRoot.transform.position = spawnPosition;

            // CharacterController sized to 1.75m human scale
            var cc = playerRoot.AddComponent<CharacterController>();
            cc.height = 1.75f;
            cc.radius = 0.32f;
            cc.center = new Vector3(0f, 0.875f, 0f);

            // Attach New Input System drivers + base legacy drivers (auto-disabled if New Input System active)
            TryAddComponentByName(playerRoot, "CyanotypeInputSystemCameraRig");
            TryAddComponentByName(playerRoot, "CyanotypeInputSystemPlayerController");
            TryAddComponentByName(playerRoot, "CyanotypePlayerController");
            TryAddComponentByName(playerRoot, "CyanotypeCameraRig");

            // First-person Camera (nearClipPlane = 0.03m to prevent brush/paper clipping)
            Camera cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("FirstPersonCamera");
                camGo.tag = "MainCamera";
                cam = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }
            cam.transform.SetParent(playerRoot.transform, false);
            cam.transform.localPosition = new Vector3(0f, 1.62f, 0.05f);
            cam.transform.localRotation = Quaternion.identity;
            cam.nearClipPlane = 0.03f;

            // Build or instantiate 3D Player Model split into Head / Torso+Legs / FirstPersonArms
            Renderer[] headRenderers;
            Renderer[] torsoLegRenderers;
            Renderer[] armRenderers;

            if (_customPlayer3DModelPrefab != null)
            {
                var modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(_customPlayer3DModelPrefab, playerRoot.transform);
                modelInstance.name = "Player3D_CustomModel";
                modelInstance.transform.localPosition = Vector3.zero;
                CategorizeCustomModelRenderers(modelInstance, out headRenderers, out torsoLegRenderers, out armRenderers);
            }
            else
            {
                BuildArticulated3DPlayerPlaceholder(
                    playerRoot.transform,
                    cam.transform,
                    out headRenderers,
                    out torsoLegRenderers,
                    out armRenderers);
            }

            // Attach World & Workbench Interactors + Prompt HUD
            var worldInteractor = playerRoot.AddComponent<CyanotypeWorldInteractor>();
            var benchInteractor = playerRoot.AddComponent<CyanotypePlayerInteractionRig>();
            var rigMode = playerRoot.AddComponent<CyanotypeFirstPersonRigMode>();
            TryAddComponentByName(playerRoot, "CyanotypeWorldPromptHUD");

            // Wire private [SerializeField] arrays on CyanotypeFirstPersonRigMode via SerializedObject
            var so = new SerializedObject(rigMode);
            AssignRendererArray(so.FindProperty("_headRenderers"), headRenderers);
            AssignRendererArray(so.FindProperty("_torsoAndLegRenderers"), torsoLegRenderers);
            AssignRendererArray(so.FindProperty("_firstPersonArmRenderers"), armRenderers);
            so.FindProperty("_worldInteractor").objectReferenceValue = worldInteractor;
            so.FindProperty("_cyanotypeBenchInteractor").objectReferenceValue = benchInteractor;
            so.ApplyModifiedPropertiesWithoutUndo();

            return playerRoot;
        }

        private static void BuildArticulated3DPlayerPlaceholder(
            Transform playerRoot,
            Transform cameraTransform,
            out Renderer[] headRenderers,
            out Renderer[] torsoLegRenderers,
            out Renderer[] armRenderers)
        {
            Color coatColor = new Color(0.22f, 0.25f, 0.30f);
            Color skinColor = new Color(0.78f, 0.64f, 0.54f);
            Color trouserColor = new Color(0.16f, 0.17f, 0.20f);

            // Head group (ShadowsOnly in 1st person so player casts shadow without camera clipping)
            var headPivot = new GameObject("Body_HeadGroup");
            headPivot.transform.SetParent(playerRoot, false);
            headPivot.transform.localPosition = new Vector3(0f, 1.62f, 0f);
            var headMesh = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            headMesh.name = "Head_Mesh";
            headMesh.transform.SetParent(headPivot.transform, false);
            headMesh.transform.localScale = new Vector3(0.21f, 0.24f, 0.23f);
            DestroyImmediate(headMesh.GetComponent<Collider>());
            var headRend = headMesh.GetComponent<Renderer>();
            headRend.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            ApplyTintMaterial(headRend, skinColor);
            headRenderers = new[] { headRend };

            // Torso & Legs group (visible in ExplorationFullBody, hidden in ActiveCyanotypeArmsOnly)
            var bodyGroup = new GameObject("Body_TorsoAndLegs");
            bodyGroup.transform.SetParent(playerRoot, false);

            var torso = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            torso.name = "Torso_Coat";
            torso.transform.SetParent(bodyGroup.transform, false);
            torso.transform.localPosition = new Vector3(0f, 1.18f, -0.04f);
            torso.transform.localScale = new Vector3(0.38f, 0.34f, 0.24f);
            DestroyImmediate(torso.GetComponent<Collider>());
            ApplyTintMaterial(torso.GetComponent<Renderer>(), coatColor);

            var leftLeg = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            leftLeg.name = "Leg_L";
            leftLeg.transform.SetParent(bodyGroup.transform, false);
            leftLeg.transform.localPosition = new Vector3(-0.11f, 0.45f, -0.03f);
            leftLeg.transform.localScale = new Vector3(0.15f, 0.45f, 0.16f);
            DestroyImmediate(leftLeg.GetComponent<Collider>());
            ApplyTintMaterial(leftLeg.GetComponent<Renderer>(), trouserColor);

            var rightLeg = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            rightLeg.name = "Leg_R";
            rightLeg.transform.SetParent(bodyGroup.transform, false);
            rightLeg.transform.localPosition = new Vector3(0.11f, 0.45f, -0.03f);
            rightLeg.transform.localScale = new Vector3(0.15f, 0.45f, 0.16f);
            DestroyImmediate(rightLeg.GetComponent<Collider>());
            ApplyTintMaterial(rightLeg.GetComponent<Renderer>(), trouserColor);

            torsoLegRenderers = new[]
            {
                torso.GetComponent<Renderer>(),
                leftLeg.GetComponent<Renderer>(),
                rightLeg.GetComponent<Renderer>()
            };

            // First-Person Arms & Hands (parented to camera rig so visible in ActiveCyanotypeArmsOnly)
            var armsRoot = new GameObject("FirstPerson_ArmsRig");
            armsRoot.transform.SetParent(cameraTransform, false);

            var rightArm = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            rightArm.name = "Forearm_R";
            rightArm.transform.SetParent(armsRoot.transform, false);
            rightArm.transform.localPosition = new Vector3(0.24f, -0.22f, 0.28f);
            rightArm.transform.localRotation = Quaternion.Euler(72f, -14f, 8f);
            rightArm.transform.localScale = new Vector3(0.075f, 0.18f, 0.075f);
            DestroyImmediate(rightArm.GetComponent<Collider>());
            ApplyTintMaterial(rightArm.GetComponent<Renderer>(), coatColor);

            var rightHand = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            rightHand.name = "Hand_R";
            rightHand.transform.SetParent(armsRoot.transform, false);
            rightHand.transform.localPosition = new Vector3(0.21f, -0.17f, 0.43f);
            rightHand.transform.localScale = new Vector3(0.075f, 0.055f, 0.09f);
            DestroyImmediate(rightHand.GetComponent<Collider>());
            ApplyTintMaterial(rightHand.GetComponent<Renderer>(), skinColor);

            var leftArm = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            leftArm.name = "Forearm_L";
            leftArm.transform.SetParent(armsRoot.transform, false);
            leftArm.transform.localPosition = new Vector3(-0.26f, -0.25f, 0.25f);
            leftArm.transform.localRotation = Quaternion.Euler(76f, 18f, -10f);
            leftArm.transform.localScale = new Vector3(0.075f, 0.18f, 0.075f);
            DestroyImmediate(leftArm.GetComponent<Collider>());
            ApplyTintMaterial(leftArm.GetComponent<Renderer>(), coatColor);

            armRenderers = new[]
            {
                rightArm.GetComponent<Renderer>(),
                rightHand.GetComponent<Renderer>(),
                leftArm.GetComponent<Renderer>()
            };
        }

        private static void CategorizeCustomModelRenderers(
            GameObject modelRoot,
            out Renderer[] head,
            out Renderer[] torsoLegs,
            out Renderer[] arms)
        {
            var all = modelRoot.GetComponentsInChildren<Renderer>(true);
            var hList = new List<Renderer>();
            var aList = new List<Renderer>();
            var bList = new List<Renderer>();

            foreach (var r in all)
            {
                string n = r.name.ToLowerInvariant();
                if (n.Contains("head") || n.Contains("hair") || n.Contains("hat") || n.Contains("face"))
                {
                    r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                    hList.Add(r);
                }
                else if (n.Contains("arm") || n.Contains("hand") || n.Contains("sleeve") || n.Contains("glove"))
                {
                    aList.Add(r);
                }
                else
                {
                    bList.Add(r);
                }
            }

            head = hList.ToArray();
            arms = aList.ToArray();
            torsoLegs = bList.ToArray();
        }

        // -------------------------------------------------------------------------
        // 3. 1900s Studio Workbench (0.89m Sheet, Tools, 5 Trays, Carbon-Arc Lamp)
        // -------------------------------------------------------------------------
        private CyanotypeWorkbenchStation BuildCyanotypeWorkbench(GameObject studioRoot, Vector3 benchCenter, Light sunLight)
        {
            var benchRoot = new GameObject("Cyanotype_StudioWorkbench");
            benchRoot.transform.SetParent(studioRoot.transform, false);
            benchRoot.transform.position = benchCenter;

            // Wooden table top (height = 0.80m)
            var tableTop = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tableTop.name = "Workbench_TableTop";
            tableTop.transform.SetParent(benchRoot.transform, false);
            tableTop.transform.localPosition = new Vector3(0f, 0.40f, 0f);
            tableTop.transform.localScale = new Vector3(2.20f, 0.80f, 1.35f);
            ApplyTintMaterial(tableTop.GetComponent<Renderer>(), new Color(0.32f, 0.22f, 0.15f));

            // Stand point for player when entering ActiveCyanotypeArmsOnly mode
            var standPoint = new GameObject("Workbench_PlayerStandPoint").transform;
            standPoint.SetParent(benchRoot.transform, false);
            standPoint.localPosition = new Vector3(0f, 0f, -0.92f);
            standPoint.localRotation = Quaternion.identity;

            // Attach Workbench Station & WorldInteractableItem to table
            var station = tableTop.AddComponent<CyanotypeWorkbenchStation>();
            var stationSO = new SerializedObject(station);
            stationSO.FindProperty("_benchStandPoint").objectReferenceValue = standPoint;
            stationSO.ApplyModifiedPropertiesWithoutUndo();

            var benchInteractable = tableTop.AddComponent<WorldInteractableItem>();
            benchInteractable.ConfigureProcedural(WorldInteractableKind.CyanotypeWorkbench, "Use Cyanotype Workbench", false);

            // Register persistent UnityEvent listener -> CyanotypeWorkbenchStation.EnterWorkbench
            var field = typeof(WorldInteractableItem).GetField("_onInteracted",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field?.GetValue(benchInteractable) is UnityEngine.Events.UnityEvent<CyanotypeWorldInteractor> evt)
            {
                UnityEventTools.AddPersistentListener(evt, station.EnterWorkbench);
            }

            float tableSurfaceY = 0.802f;

            // 1. Cyanotype Sheet (exact 0.89 world-unit height per architecture rule)
            var sheet = GameObject.CreatePrimitive(PrimitiveType.Quad);
            sheet.name = "Cyanotype_PaperSheet_089m";
            sheet.transform.SetParent(benchRoot.transform, false);
            sheet.transform.localPosition = new Vector3(-0.15f, tableSurfaceY, 0.02f);
            sheet.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            sheet.transform.localScale = new Vector3(SheetWidthWorld, SheetHeightWorld, 1f);

            MeshFilter paperMF = sheet.GetComponent<MeshFilter>();
            MeshRenderer paperMR = sheet.GetComponent<MeshRenderer>();

            EnsureMaterialsDirectory();
            Shader simShader = Shader.Find("Cyanotype/SimMultiSource") ?? Shader.Find("Cyanotype/PhotochemicalSimMultiSource");
            Shader displayShader = Shader.Find("Cyanotype/SurfaceDisplay");
            Shader fluidShader = Shader.Find("Cyanotype/FluidTransport");
            Shader bathShader = Shader.Find("Cyanotype/BathTransport");

            Material simMat = CreateAssetMaterial(simShader, "Assets/Cyanotype/Materials/M_CyanotypeSim.mat");
            Material displayMat = CreateAssetMaterial(displayShader, "Assets/Cyanotype/Materials/M_CyanotypeDisplay.mat");
            Material fluidMat = CreateAssetMaterial(fluidShader, "Assets/Cyanotype/Materials/M_CyanotypeFluid.mat");
            Material bathMat = CreateAssetMaterial(bathShader, "Assets/Cyanotype/Materials/M_CyanotypeBath.mat");

            if (displayMat != null)
                paperMR.sharedMaterial = displayMat;
            else
                ApplyTintMaterial(paperMR, new Color(0.93f, 0.92f, 0.87f));

            Component dragPaper = TryAddComponentByName(sheet, "DraggableCyanotypePaper");

            // 2. Period-Accurate 1900s Tools: Hake Brush & Glass Coating Rod
            Build1900sApplicatorTool(
                benchRoot.transform,
                "HakeBrush_1900s",
                new Vector3(-0.68f, tableSurfaceY + 0.015f, -0.22f),
                CyanotypeToolRole.SensitizerApplicator,
                radiusWorld: 0.035f,
                depositPerTick: 0.20f,
                handleColor: new Color(0.68f, 0.54f, 0.34f),
                tipColor: new Color(0.86f, 0.82f, 0.72f));

            Build1900sApplicatorTool(
                benchRoot.transform,
                "GlassCoatingRod_1900s",
                new Vector3(-0.68f, tableSurfaceY + 0.012f, 0.06f),
                CyanotypeToolRole.SensitizerApplicator,
                radiusWorld: 0.024f,
                depositPerTick: 0.16f,
                handleColor: new Color(0.75f, 0.88f, 0.92f),
                tipColor: new Color(0.82f, 0.94f, 0.96f));

            // 3. Stencil / Botanical Shadow Prop (Base-10 OD = 1.05)
            var stencil = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stencil.name = "Botanical_GlassStencil_OD105";
            stencil.transform.SetParent(benchRoot.transform, false);
            stencil.transform.localPosition = new Vector3(-0.68f, tableSurfaceY + 0.01f, 0.34f);
            stencil.transform.localScale = new Vector3(0.22f, 0.008f, 0.28f);
            ApplyTintMaterial(stencil.GetComponent<Renderer>(), new Color(0.18f, 0.24f, 0.18f));
            var stencilComp = stencil.AddComponent<CyanotypePhysicsInteractable>();
            stencilComp.ConfigureFromEditor(CyanotypeToolRole.ShadowOrStencilProp, 0.05f, 0f, DefaultStencilOD10);
            TryAddComponentByName(stencil, "DraggableShadowProp");
            TryAddComponentByName(stencil, "CyanotypeStencilProp");

            // 4. Five Studio Trays (Sensitizer, Water Wash, Alkali Soda Ash, Tannin Toner, Peroxide)
            Component fluidCtrl = GetComponentByName(studioRoot, "CyanotypeFluidController");
            var trayConfigs = new (string name, CyanotypeToolRole role, Color liquidColor, float zOffset)[]
            {
                ("Tray_SensitizerStock", CyanotypeToolRole.SensitizerApplicator, new Color(0.74f, 0.82f, 0.22f), -0.44f),
                ("Tray_WaterWash", CyanotypeToolRole.WaterWashApplicator, new Color(0.14f, 0.38f, 0.62f), -0.22f),
                ("Tray_AlkaliBleach_SodaAsh", CyanotypeToolRole.AlkaliBleachBath, new Color(0.78f, 0.62f, 0.32f), 0.00f),
                ("Tray_TanninToner", CyanotypeToolRole.TanninTonerBath, new Color(0.32f, 0.20f, 0.14f), 0.22f),
                ("Tray_PeroxideOxidizer", CyanotypeToolRole.PeroxideOxidizerBath, new Color(0.68f, 0.84f, 0.90f), 0.44f)
            };

            foreach (var cfg in trayConfigs)
            {
                BuildStudioTray(benchRoot.transform, cfg.name, new Vector3(0.62f, tableSurfaceY, cfg.zOffset), cfg.role, cfg.liquidColor, fluidCtrl);
            }

            // 5. 1900s Carbon-Arc Lamp normalized at 0.50m above sheet
            var lampRoot = new GameObject("CarbonArcLamp_1900s");
            lampRoot.transform.SetParent(benchRoot.transform, false);
            lampRoot.transform.localPosition = new Vector3(-0.15f, tableSurfaceY + CarbonArcNominalDist, 0.02f);

            var hood = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            hood.name = "CarbonArc_ReflectorHood";
            hood.transform.SetParent(lampRoot.transform, false);
            hood.transform.localScale = new Vector3(0.24f, 0.09f, 0.24f);
            DestroyImmediate(hood.GetComponent<Collider>());
            ApplyTintMaterial(hood.GetComponent<Renderer>(), new Color(0.14f, 0.14f, 0.16f));

            var arcLight = lampRoot.AddComponent<Light>();
            arcLight.type = LightType.Spot;
            arcLight.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            arcLight.spotAngle = 78f;
            arcLight.range = 2.5f;
            arcLight.intensity = 2.4f;
            arcLight.color = new Color(0.84f, 0.90f, 1.0f);
            arcLight.shadows = LightShadows.Soft;

            // 6. Wire CyanotypeEnvironmentRig, CyanotypeFluidController & StudioEnvironmentToggle
            Component envRig = GetComponentByName(studioRoot, "CyanotypeEnvironmentRig");
            Component recipeComp = GetComponentByName(studioRoot, "CyanotypeRecipe");
            Component envToggle = GetComponentByName(studioRoot, "StudioEnvironmentToggle");

            AssignFieldIfPresent(envRig, "paperMeshFilter", paperMF);
            AssignFieldIfPresent(envRig, "paperRenderer", paperMR);
            AssignFieldIfPresent(envRig, "shadowCasterLayers", 1 << 0);
            AssignFieldIfPresent(envRig, "sunLight", sunLight);
            AssignFieldIfPresent(envRig, "uvLampLight", arcLight);
            AssignFieldIfPresent(envRig, "lampIsOn", true);
            AssignFieldIfPresent(envRig, "lampUVIntensity", 2.6f);
            AssignFieldIfPresent(envRig, "lampSize", 0.08f);
            AssignFieldIfPresent(envRig, "sunPenumbra", 0.0047f);
            AssignFieldIfPresent(envRig, "peakSunUVIntensity", 1.2f);
            AssignFieldIfPresent(envRig, "windowAmbientUV", 0.03f);
            AssignFieldIfPresent(envRig, "coatingThickness", 0.0f);
            AssignFieldIfPresent(envRig, "simMaterial", simMat);
            AssignFieldIfPresent(envRig, "displayMaterial", displayMat);
            AssignFieldIfPresent(envRig, "recipe", recipeComp);

            AssignFieldIfPresent(fluidCtrl, "rig", envRig);
            AssignFieldIfPresent(fluidCtrl, "paperTransform", sheet.transform);
            AssignFieldIfPresent(fluidCtrl, "fluidMaterial", fluidMat);
            AssignFieldIfPresent(fluidCtrl, "bathMaterial", bathMat);

            AssignFieldIfPresent(envToggle, "rig", envRig);
            AssignFieldIfPresent(dragPaper, "fluidController", fluidCtrl);
            AssignFieldIfPresent(dragPaper, "restingY", sheet.transform.position.y);

            return station;
        }

        private static void Build1900sApplicatorTool(
            Transform parent,
            string name,
            Vector3 localPos,
            CyanotypeToolRole role,
            float radiusWorld,
            float depositPerTick,
            Color handleColor,
            Color tipColor)
        {
            var tool = new GameObject(name);
            tool.transform.SetParent(parent, false);
            tool.transform.localPosition = localPos;

            var handle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            handle.name = "Handle";
            handle.transform.SetParent(tool.transform, false);
            handle.transform.localScale = new Vector3(0.022f, 0.012f, 0.22f);
            ApplyTintMaterial(handle.GetComponent<Renderer>(), handleColor);

            var tip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tip.name = "ContactTip";
            tip.transform.SetParent(tool.transform, false);
            tip.transform.localPosition = new Vector3(0f, -0.002f, 0.125f);
            tip.transform.localScale = new Vector3(radiusWorld * 2f, 0.014f, 0.035f);
            DestroyImmediate(tip.GetComponent<Collider>());
            ApplyTintMaterial(tip.GetComponent<Renderer>(), tipColor);

            var rb = tool.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            var interactable = tool.AddComponent<CyanotypePhysicsInteractable>();
            interactable.ConfigureFromEditor(role, radiusWorld, depositPerTick, 0f);
            TryAddComponentByName(tool, "CyanotypeDiegeticUXBinder");

            var so = new SerializedObject(interactable);
            so.FindProperty("_contactTip").objectReferenceValue = tip.transform;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BuildStudioTray(
            Transform parent,
            string name,
            Vector3 localPos,
            CyanotypeToolRole role,
            Color liquidColor,
            Component fluidCtrl)
        {
            var tray = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tray.name = name;
            tray.transform.SetParent(parent, false);
            tray.transform.localPosition = localPos + Vector3.up * 0.025f;
            tray.transform.localScale = new Vector3(0.46f, 0.05f, 0.19f);
            ApplyTintMaterial(tray.GetComponent<Renderer>(), new Color(0.88f, 0.88f, 0.86f));

            var liquid = GameObject.CreatePrimitive(PrimitiveType.Quad);
            liquid.name = "LiquidMeniscus";
            liquid.transform.SetParent(tray.transform, false);
            liquid.transform.localPosition = new Vector3(0f, 0.52f, 0f);
            liquid.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            liquid.transform.localScale = new Vector3(0.92f, 0.88f, 1f);
            DestroyImmediate(liquid.GetComponent<Collider>());
            ApplyTintMaterial(liquid.GetComponent<Renderer>(), liquidColor);

            var dipTray = tray.AddComponent<CyanotypeDipTray>();
            var so = new SerializedObject(dipTray);
            so.FindProperty("_traySolutionRole").enumValueIndex = (int)role;
            so.FindProperty("_liquidSurfacePoint").objectReferenceValue = liquid.transform;
            so.ApplyModifiedPropertiesWithoutUndo();

            if (role == CyanotypeToolRole.WaterWashApplicator)
            {
                Component waterTrigger = TryAddComponentByName(tray, "WaterTrayTrigger");
                AssignFieldIfPresent(waterTrigger, "fluidController", fluidCtrl);
            }
            TryAddComponentByName(tray, "CyanotypePropTray");
        }

        // -------------------------------------------------------------------------
        // 4. Deterministic Procedural 3D Plants & Rocks Scatter
        // -------------------------------------------------------------------------
        private void ScatterProceduralEnvironment(Transform parent, Vector3 benchCenter)
        {
            var natureRoot = new GameObject("Procedural_3D_FloraAndRocks");
            natureRoot.transform.SetParent(parent, false);

            UnityEngine.Random.InitState(_worldSeed);

            Mesh plantMesh = BuildProceduralFernMesh();
            Mesh rockMesh = BuildProceduralRockMesh();

            for (int i = 0; i < _proceduralPlantCount; i++)
            {
                Vector3 pos = SampleClearCourtyardPoint(benchCenter, 2.4f, _courtyardRadius);
                var plant = new GameObject($"Procedural_Botanical_{i:00}");
                plant.transform.SetParent(natureRoot.transform, false);
                plant.transform.position = pos;
                plant.transform.rotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
                plant.transform.localScale = Vector3.one * UnityEngine.Random.Range(0.75f, 1.35f);

                plant.AddComponent<MeshFilter>().sharedMesh = plantMesh;
                var mr = plant.AddComponent<MeshRenderer>();
                ApplyTintMaterial(mr, new Color(0.20f, 0.42f, 0.22f));

                var col = plant.AddComponent<MeshCollider>();
                col.sharedMesh = plantMesh;
                col.convex = true;

                var item = plant.AddComponent<WorldInteractableItem>();
                item.ConfigureProcedural(WorldInteractableKind.ProceduralBotanical, "Gather Botanical Frond", true);
            }

            for (int i = 0; i < _proceduralRockCount; i++)
            {
                Vector3 pos = SampleClearCourtyardPoint(benchCenter, 2.4f, _courtyardRadius);
                var rock = new GameObject($"Procedural_FieldRock_{i:00}");
                rock.transform.SetParent(natureRoot.transform, false);
                rock.transform.position = pos;
                rock.transform.rotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
                rock.transform.localScale = Vector3.one * UnityEngine.Random.Range(0.7f, 1.45f);

                rock.AddComponent<MeshFilter>().sharedMesh = rockMesh;
                var mr = rock.AddComponent<MeshRenderer>();
                ApplyTintMaterial(mr, new Color(0.48f, 0.47f, 0.44f));

                var col = rock.AddComponent<MeshCollider>();
                col.sharedMesh = rockMesh;
                col.convex = true;

                var rb = rock.AddComponent<Rigidbody>();
                rb.mass = 1.4f;

                var item = rock.AddComponent<WorldInteractableItem>();
                item.ConfigureProcedural(WorldInteractableKind.ProceduralRock, "Pick Up Field Stone", false);
            }
        }

        private static Vector3 SampleClearCourtyardPoint(Vector3 avoidCenter, float minClearance, float maxRadius)
        {
            for (int attempt = 0; attempt < 24; attempt++)
            {
                Vector2 circle = UnityEngine.Random.insideUnitCircle * maxRadius;
                Vector3 candidate = new Vector3(circle.x, 0f, circle.y);
                if (Vector3.Distance(candidate, avoidCenter) > minClearance && candidate.magnitude > 1.5f)
                {
                    return candidate;
                }
            }
            return new Vector3(maxRadius * 0.65f, 0f, maxRadius * 0.65f);
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

        // -------------------------------------------------------------------------
        // 5. 2D Animated Human NPC in 3D Space
        // -------------------------------------------------------------------------
        private void Spawn2DAnimatedNPC(Transform parent, Vector3 worldPos)
        {
            if (_custom2DNPCPrefab != null)
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(_custom2DNPCPrefab, parent);
                inst.transform.position = worldPos;
                return;
            }

            var npcRoot = new GameObject("NPC_2D_Botanist");
            npcRoot.transform.SetParent(parent, false);
            npcRoot.transform.position = worldPos;

            var capsule = npcRoot.AddComponent<CapsuleCollider>();
            capsule.height = 1.72f;
            capsule.radius = 0.28f;
            capsule.center = new Vector3(0f, 0.86f, 0f);

            npcRoot.AddComponent<Animator>();

            var spriteChild = new GameObject("SpriteBillboard");
            spriteChild.transform.SetParent(npcRoot.transform, false);
            spriteChild.transform.localPosition = new Vector3(0f, 0.86f, 0f);

            var sr = spriteChild.AddComponent<SpriteRenderer>();
            sr.sprite = CreatePlaceholder2DHumanSprite();
            sr.shadowCastingMode = ShadowCastingMode.TwoSided;

            npcRoot.AddComponent<Cyanotype2DNPCController>();
            var interactable = npcRoot.AddComponent<WorldInteractableItem>();
            interactable.ConfigureNPC("Town Botanist");
        }

        private static Sprite CreatePlaceholder2DHumanSprite()
        {
            const int w = 64;
            const int h = 128;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            Color clear = new Color(0f, 0f, 0f, 0f);
            Color coat = new Color(0.24f, 0.30f, 0.38f, 1f);
            Color skin = new Color(0.84f, 0.68f, 0.56f, 1f);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float dx = (x - 32f) / 14f;
                    float dyHead = (y - 104f) / 14f;
                    bool inHead = (dx * dx + dyHead * dyHead) <= 1f;
                    bool inBody = (y >= 16 && y <= 88 && Mathf.Abs(x - 32) <= 13);
                    tex.SetPixel(x, y, inHead ? skin : (inBody ? coat : clear));
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 74f);
        }

        // -------------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------------
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

        private static Type ResolveType(string typeName)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a =>
                {
                    try { return a.GetTypes(); }
                    catch { return Array.Empty<Type>(); }
                })
                .FirstOrDefault(t => t.Name == typeName);
        }

        private static Component TryAddComponentByName(GameObject target, string typeName)
        {
            Type found = ResolveType(typeName);
            if (found != null && typeof(Component).IsAssignableFrom(found))
            {
                return target.GetComponent(found) ?? Undo.AddComponent(target, found);
            }
            return null;
        }

        private static Component GetComponentByName(GameObject target, string typeName)
        {
            Type found = ResolveType(typeName);
            return found != null ? target.GetComponent(found) : null;
        }

        private static void AssignFieldIfPresent(Component target, string fieldName, object value)
        {
            if (target == null || value == null) return;
            var field = target.GetType().GetField(fieldName,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null && field.FieldType.IsInstanceOfType(value))
            {
                field.SetValue(target, value);
                EditorUtility.SetDirty(target);
            }
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

        private static void ApplyTintMaterial(Renderer r, Color color)
        {
            if (r == null) return;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { color = color };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            r.sharedMaterial = mat;
        }
    }
}
#endif
