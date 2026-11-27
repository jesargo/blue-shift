#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Cyanotype.Gameplay;

namespace Cyanotype.EditorTools
{
    public class CyanotypeInteractablePrefabWizard : EditorWindow
    {
        private GameObject _sourceObject;
        private AnimatorController _hybridController;
        private CyanotypeToolRole _role = CyanotypeToolRole.SensitizerApplicator;

        [Header("Base-10 Optical & World Scale Validation")]
        [Tooltip("Base-10 Optical Density (e.g. 1.05 for dark ink). Never use base-e (~2.40).")]
        [Range(0f, 3f)] private float _base10OpticalDensity = 1.05f;
        private float _contactRadiusWorld = 0.035f;
        private float _depositionRatePerTick = 0.18f;
        private string _prefabOutputDir = "Assets/Cyanotype/Prefabs/Interactables";

        // Sheet reference height in world units (~3x real A4 per project architecture)
        private const float ReferenceSheetHeightWorld = 0.89f;

        [MenuItem("Cyanotype/Convert Selection to Physics Interactable")]
        public static void OpenWizard()
        {
            var win = GetWindow<CyanotypeInteractablePrefabWizard>("Cyanotype Interactable Wizard");
            win._sourceObject = Selection.activeGameObject;
            win.minSize = new Vector2(420f, 380f);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Source Asset & Hybrid Controller", EditorStyles.boldLabel);
            _sourceObject = (GameObject)EditorGUILayout.ObjectField("Scene / Prefab Root", _sourceObject, typeof(GameObject), true);
            _hybridController = (AnimatorController)EditorGUILayout.ObjectField("Hybrid AnimatorController", _hybridController, typeof(AnimatorController), false);
            _role = (CyanotypeToolRole)EditorGUILayout.EnumPopup("Physics Interaction Role", _role);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Photochemistry & Scale Parameters", EditorStyles.boldLabel);
            _contactRadiusWorld = EditorGUILayout.Slider("Contact Footprint (World Units)", _contactRadiusWorld, 0.005f, 0.25f);
            _depositionRatePerTick = EditorGUILayout.Slider("Deposition / Animation Tick", _depositionRatePerTick, 0.01f, 1.0f);

            if (_role == CyanotypeToolRole.ShadowOrStencilProp)
            {
                _base10OpticalDensity = EditorGUILayout.Slider("Stencil Ink Base-10 OD", _base10OpticalDensity, 0f, 3f);
                float transmittance = Mathf.Pow(10f, -_base10OpticalDensity);
                EditorGUILayout.HelpBox($"Base-10 Transmittance tau = 10^(-OD) = {transmittance:P1}", MessageType.Info);
            }

            if (_sourceObject != null)
            {
                ValidateWorldScale(_sourceObject);
            }

            EditorGUILayout.Space(12);
            if (GUILayout.Button("Configure & Save Interactable Prefab", GUILayout.Height(34)))
            {
                ConvertAndSavePrefab();
            }
        }

        private void ValidateWorldScale(GameObject target)
        {
            var renderers = target.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;

            Bounds combined = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) combined.Encapsulate(renderers[i].bounds);

            if (combined.size.magnitude > ReferenceSheetHeightWorld * 1.5f)
            {
                EditorGUILayout.HelpBox(
                    $"Object bounds ({combined.size.magnitude:F2}m) exceed 1.5x sheet height ({ReferenceSheetHeightWorld}m). " +
                    "Verify blocker height against _OccMaxHeight so shadow penumbra scales accurately.",
                    MessageType.Warning);
            }
        }

        private void ConvertAndSavePrefab()
        {
            if (_sourceObject == null)
            {
                EditorUtility.DisplayDialog("Missing Target", "Assign a Scene GameObject or Model first.", "OK");
                return;
            }

            Undo.RegisterFullObjectHierarchyUndo(_sourceObject, "Convert to Cyanotype Interactable");

            // 1. Ensure Animator with hybrid 2D/3D controller
            var animator = _sourceObject.GetComponent<Animator>();
            if (animator == null) animator = Undo.AddComponent<Animator>(_sourceObject);
            if (_hybridController != null) animator.runtimeAnimatorController = _hybridController;

            // 2. Ensure Physics Collider & Kinematic-friendly Rigidbody
            if (_sourceObject.GetComponentInChildren<Collider>() == null)
            {
                var box = Undo.AddComponent<BoxCollider>(_sourceObject);
                box.isTrigger = (_role != CyanotypeToolRole.ShadowOrStencilProp);
            }

            var rb = _sourceObject.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = Undo.AddComponent<Rigidbody>(_sourceObject);
                rb.useGravity = (_role == CyanotypeToolRole.ShadowOrStencilProp);
                rb.isKinematic = (_role != CyanotypeToolRole.ShadowOrStencilProp);
            }

            // 3. Attach runtime physics bridge & diegetic UX binder (non-destructive)
            var interactable = _sourceObject.GetComponent<CyanotypePhysicsInteractable>();
            if (interactable == null) interactable = Undo.AddComponent<CyanotypePhysicsInteractable>(_sourceObject);

            interactable.ConfigureFromEditor(
                _role,
                _contactRadiusWorld,
                _depositionRatePerTick,
                _base10OpticalDensity
            );

            if (_role == CyanotypeToolRole.ShadowOrStencilProp)
            {
                TryAddComponentByName(_sourceObject, "DraggableShadowProp");
            }
            else
            {
                TryAddComponentByName(_sourceObject, "CyanotypeDiegeticUXBinder");
            }

            // 4. Save clean Prefab asset
            if (!AssetDatabase.IsValidFolder(_prefabOutputDir))
            {
                Directory.CreateDirectory(_prefabOutputDir);
                AssetDatabase.Refresh();
            }

            string prefabPath = Path.Combine(_prefabOutputDir, $"{_sourceObject.name}_Interactable.prefab").Replace("\\", "/");
            PrefabUtility.SaveAsPrefabAssetAndConnect(_sourceObject, prefabPath, InteractionMode.UserAction);
            Debug.Log($"[Cyanotype] Saved physics interactable prefab: {prefabPath}");
        }

        private static void TryAddComponentByName(GameObject target, string typeName)
        {
            Type found = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a =>
                {
                    try { return a.GetTypes(); }
                    catch { return Array.Empty<Type>(); }
                })
                .FirstOrDefault(t => t.Name == typeName && typeof(Component).IsAssignableFrom(t));

            if (found != null && target.GetComponent(found) == null)
            {
                Undo.AddComponent(target, found);
            }
        }
    }
}
#endif
