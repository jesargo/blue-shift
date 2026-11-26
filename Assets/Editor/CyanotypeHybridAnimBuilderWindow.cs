#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.UI;

namespace Cyanotype.EditorTools
{
    public class CyanotypeHybridAnimBuilderWindow : EditorWindow
    {
        private string _assetBaseName = "HakeBrush_Hybrid";
        private string _outputFolder = "Assets/Cyanotype/Animations";

        [Header("3D Model Clips")]
        private AnimationClip _idle3DClip;
        private AnimationClip _activeLoop3DClip;
        private AnimationClip _actionOneShot3DClip;

        [Header("2D Overlay / Flipbook Sprites")]
        private bool _targetUIImageInsteadOfSpriteRenderer = false;
        private string _overlayChildPath = "UX_2D_Overlay";
        private float _spriteFrameRate = 12f;
        private List<Sprite> _overlaySprites = new List<Sprite>();

        [Header("Physics Event Injection (Normalized 0..1)")]
        private bool _injectPhysicsEvents = true;
        [Range(0f, 1f)] private float _contactStartNormalized = 0.15f;
        [Range(0f, 1f)] private float _contactEndNormalized = 0.85f;
        private int _depositTicksPerCycle = 6;

        private Vector2 _scroll;

        [MenuItem("Cyanotype/Hybrid 2D-3D Animation Builder")]
        public static void ShowWindow()
        {
            var win = GetWindow<CyanotypeHybridAnimBuilderWindow>("Cyanotype Hybrid Anim");
            win.minSize = new Vector2(430f, 540f);
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("Output Configuration", EditorStyles.boldLabel);
            _assetBaseName = EditorGUILayout.TextField("Controller Base Name", _assetBaseName);
            _outputFolder = EditorGUILayout.TextField("Output Folder", _outputFolder);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Layer 0: 3D Rig Animation Clips", EditorStyles.boldLabel);
            _idle3DClip = (AnimationClip)EditorGUILayout.ObjectField("Idle 3D Clip", _idle3DClip, typeof(AnimationClip), false);
            _activeLoop3DClip = (AnimationClip)EditorGUILayout.ObjectField("Active Stroke/Hold 3D Clip", _activeLoop3DClip, typeof(AnimationClip), false);
            _actionOneShot3DClip = (AnimationClip)EditorGUILayout.ObjectField("One-Shot Dip/Shutter 3D Clip", _actionOneShot3DClip, typeof(AnimationClip), false);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Layer 1: 2D Sprite / UX Cycle", EditorStyles.boldLabel);
            _targetUIImageInsteadOfSpriteRenderer = EditorGUILayout.Toggle("Target UI Image (vs SpriteRenderer)", _targetUIImageInsteadOfSpriteRenderer);
            _overlayChildPath = EditorGUILayout.TextField("Child Transform Path", _overlayChildPath);
            _spriteFrameRate = EditorGUILayout.FloatField("2D Flipbook FPS", _spriteFrameRate);

            if (GUILayout.Button("Load Selected Sprites from Project Window"))
            {
                _overlaySprites = Selection.GetFiltered<Sprite>(SelectionMode.DeepAssets)
                    .OrderBy(s => s.name)
                    .ToList();
            }

            EditorGUILayout.LabelField($"Loaded 2D Frames: {_overlaySprites.Count}");
            for (int i = 0; i < _overlaySprites.Count; i++)
            {
                _overlaySprites[i] = (Sprite)EditorGUILayout.ObjectField($"Frame {i}", _overlaySprites[i], typeof(Sprite), false);
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Physics Contact AnimationEvents", EditorStyles.boldLabel);
            _injectPhysicsEvents = EditorGUILayout.Toggle("Inject Deposition Events", _injectPhysicsEvents);
            if (_injectPhysicsEvents)
            {
                _contactStartNormalized = EditorGUILayout.Slider("Contact Begin (Norm)", _contactStartNormalized, 0f, 0.9f);
                _contactEndNormalized = EditorGUILayout.Slider("Contact End (Norm)", _contactEndNormalized, _contactStartNormalized + 0.05f, 1f);
                _depositTicksPerCycle = EditorGUILayout.IntSlider("Deposit Ticks / Cycle", _depositTicksPerCycle, 1, 24);
            }

            EditorGUILayout.Space(14);
            if (GUILayout.Button("Generate Hybrid AnimatorController & Clips", GUILayout.Height(34)))
            {
                BuildHybridAnimator();
            }

            EditorGUILayout.EndScrollView();
        }

        private void BuildHybridAnimator()
        {
            if (!AssetDatabase.IsValidFolder(_outputFolder))
            {
                Directory.CreateDirectory(_outputFolder);
                AssetDatabase.Refresh();
            }

            string controllerPath = Path.Combine(_outputFolder, $"{_assetBaseName}_Controller.controller").Replace("\\", "/");
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);

            // Standard Cyanotype gameplay parameters
            controller.AddParameter("IsHeld", AnimatorControllerParameterType.Bool);
            controller.AddParameter("IsActive", AnimatorControllerParameterType.Bool);
            controller.AddParameter("WetLoad", AnimatorControllerParameterType.Float);
            controller.AddParameter("ProcessStage", AnimatorControllerParameterType.Int);
            controller.AddParameter("ActionTrigger", AnimatorControllerParameterType.Trigger);

            // Layer 0: 3D Rig Motion
            var baseLayer = controller.layers[0];
            baseLayer.name = "Base_3D_Cycle";
            var rootSM = baseLayer.stateMachine;

            var idleState = rootSM.AddState("Idle", new Vector3(260, 40, 0));
            idleState.motion = _idle3DClip;
            rootSM.defaultState = idleState;

            var activeState = rootSM.AddState("ActiveLoop", new Vector3(540, 40, 0));
            activeState.motion = _activeLoop3DClip;

            var actionState = rootSM.AddState("ActionOneShot", new Vector3(400, 170, 0));
            actionState.motion = _actionOneShot3DClip;

            // Transitions for 3D states
            var toActive = idleState.AddTransition(activeState);
            toActive.AddCondition(AnimatorConditionMode.If, 0, "IsActive");
            toActive.hasExitTime = false;
            toActive.duration = 0.08f;

            var toIdle = activeState.AddTransition(idleState);
            toIdle.AddCondition(AnimatorConditionMode.IfNot, 0, "IsActive");
            toIdle.hasExitTime = false;
            toIdle.duration = 0.12f;

            var anyToAction = rootSM.AddAnyStateTransition(actionState);
            anyToAction.AddCondition(AnimatorConditionMode.If, 0, "ActionTrigger");
            anyToAction.hasExitTime = false;
            anyToAction.duration = 0.05f;

            var actionToIdle = actionState.AddTransition(idleState);
            actionToIdle.hasExitTime = true;
            actionToIdle.exitTime = 0.95f;
            actionToIdle.duration = 0.1f;

            // Inject physics contact AnimationEvents onto ActiveLoop 3D clip
            if (_injectPhysicsEvents && _activeLoop3DClip != null)
            {
                InjectPhysicsEventsIntoClip(_activeLoop3DClip);
            }

            // Layer 1: 2D Sprite / Diegetic UX Cycle
            if (_overlaySprites != null && _overlaySprites.Count > 0)
            {
                AnimationClip clip2D = Create2DSpriteFlipbookClip();
                controller.AddLayer("Overlay_2D_UX");
                var layers = controller.layers;
                layers[1].defaultWeight = 1f;
                controller.layers = layers;

                var sm2D = controller.layers[1].stateMachine;
                var state2D = sm2D.AddState("UX_2D_Loop", new Vector3(280, 60, 0));
                state2D.motion = clip2D;
                state2D.speedParameterActive = true;
                state2D.speedParameter = "WetLoad";
                sm2D.defaultState = state2D;
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Selection.activeObject = controller;
            Debug.Log($"[Cyanotype] Built hybrid AnimatorController at {controllerPath}");
        }

        private AnimationClip Create2DSpriteFlipbookClip()
        {
            var clip = new AnimationClip { frameRate = _spriteFrameRate };
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            var binding = new EditorCurveBinding
            {
                path = _overlayChildPath,
                type = _targetUIImageInsteadOfSpriteRenderer ? typeof(Image) : typeof(SpriteRenderer),
                propertyName = "m_Sprite"
            };

            var keyframes = new ObjectReferenceKeyframe[_overlaySprites.Count];
            for (int i = 0; i < _overlaySprites.Count; i++)
            {
                keyframes[i] = new ObjectReferenceKeyframe
                {
                    time = i / _spriteFrameRate,
                    value = _overlaySprites[i]
                };
            }

            AnimationUtility.SetObjectReferenceCurve(clip, binding, keyframes);

            string clipPath = Path.Combine(_outputFolder, $"{_assetBaseName}_2DOverlay.anim").Replace("\\", "/");
            AssetDatabase.CreateAsset(clip, clipPath);
            return clip;
        }

        private void InjectPhysicsEventsIntoClip(AnimationClip clip)
        {
            float len = Mathf.Max(clip.length, 0.1f);
            var events = new List<AnimationEvent>
            {
                new AnimationEvent { time = _contactStartNormalized * len, functionName = "OnPhysicsContactBegin" },
                new AnimationEvent { time = _contactEndNormalized * len, functionName = "OnPhysicsContactEnd" }
            };

            for (int i = 0; i < _depositTicksPerCycle; i++)
            {
                float tNorm = Mathf.Lerp(_contactStartNormalized, _contactEndNormalized, (i + 0.5f) / _depositTicksPerCycle);
                events.Add(new AnimationEvent
                {
                    time = tNorm * len,
                    functionName = "OnPhysicsDepositTick",
                    floatParameter = 1f / _depositTicksPerCycle
                });
            }

            AnimationUtility.SetAnimationEvents(clip, events.OrderBy(e => e.time).ToArray());
            EditorUtility.SetDirty(clip);
        }
    }
}
#endif