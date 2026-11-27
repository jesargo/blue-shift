#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;
using Cyanotype.World;

namespace Cyanotype.EditorTools
{
    public class Cyanotype2DNPCBuilderWindow : EditorWindow
    {
        private string _npcName = "NPC_Botanist";
        private float _npcHeightWorldUnits = 1.72f;
        private float _frameRate = 10f;
        private string _outputDir = "Assets/Cyanotype/Prefabs/NPCs";

        private List<Sprite> _idleFrontSprites = new List<Sprite>();
        private List<Sprite> _idleSideSprites = new List<Sprite>();
        private List<Sprite> _idleBackSprites = new List<Sprite>();
        private List<Sprite> _talkFrontSprites = new List<Sprite>();

        [MenuItem("Cyanotype/2D Human NPC Builder")]
        public static void Open()
        {
            var win = GetWindow<Cyanotype2DNPCBuilderWindow>("2D Human NPC Builder");
            win.minSize = new Vector2(420f, 440f);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("2D-in-3D Human NPC Configuration", EditorStyles.boldLabel);
            _npcName = EditorGUILayout.TextField("NPC Asset Name", _npcName);
            _npcHeightWorldUnits = EditorGUILayout.Slider("NPC Height (World Units)", _npcHeightWorldUnits, 1.4f, 2.0f);
            _frameRate = EditorGUILayout.FloatField("Sprite Animation FPS", _frameRate);
            _outputDir = EditorGUILayout.TextField("Output Directory", _outputDir);

            EditorGUILayout.Space(8);
            DrawSpriteListButton("Idle Front Sprites", _idleFrontSprites);
            DrawSpriteListButton("Idle Side Sprites (Optional)", _idleSideSprites);
            DrawSpriteListButton("Idle Back Sprites (Optional)", _idleBackSprites);
            DrawSpriteListButton("Talk Front Sprites (Optional)", _talkFrontSprites);

            EditorGUILayout.Space(14);
            if (GUILayout.Button("Build 2D Animated NPC Prefab", GUILayout.Height(34)))
            {
                BuildNPCPrefab();
            }
        }

        private void DrawSpriteListButton(string label, List<Sprite> targetList)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"{label}: {targetList.Count} frames");
            if (GUILayout.Button("Assign Selected Sprites", GUILayout.Width(175)))
            {
                targetList.Clear();
                targetList.AddRange(Selection.GetFiltered<Sprite>(SelectionMode.DeepAssets).OrderBy(s => s.name));
            }
            EditorGUILayout.EndHorizontal();
        }

        private void BuildNPCPrefab()
        {
            if (_idleFrontSprites.Count == 0)
            {
                EditorUtility.DisplayDialog("Missing Sprites", "Assign at least one Idle Front sprite.", "OK");
                return;
            }

            if (!AssetDatabase.IsValidFolder(_outputDir))
            {
                Directory.CreateDirectory(_outputDir);
                AssetDatabase.Refresh();
            }

            // 1. Build AnimatorController with ViewDir (0=Front, 1=Side, 2=Back) & IsTalking
            string ctrlPath = Path.Combine(_outputDir, $"{_npcName}_Anim.controller").Replace("\\", "/");
            var controller = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
            controller.AddParameter("MoveSpeed", AnimatorControllerParameterType.Float);
            controller.AddParameter("ViewDir", AnimatorControllerParameterType.Int);
            controller.AddParameter("IsTalking", AnimatorControllerParameterType.Bool);
            controller.AddParameter("InteractTrigger", AnimatorControllerParameterType.Trigger);

            var sm = controller.layers[0].stateMachine;
            var frontClip = CreateSpriteClip($"{_npcName}_IdleFront", _idleFrontSprites);
            var sideClip = CreateSpriteClip($"{_npcName}_IdleSide", _idleSideSprites.Count > 0 ? _idleSideSprites : _idleFrontSprites);
            var backClip = CreateSpriteClip($"{_npcName}_IdleBack", _idleBackSprites.Count > 0 ? _idleBackSprites : _idleFrontSprites);
            var talkClip = CreateSpriteClip($"{_npcName}_TalkFront", _talkFrontSprites.Count > 0 ? _talkFrontSprites : _idleFrontSprites);

            var frontState = sm.AddState("Idle_Front", new Vector3(260, 40, 0));
            frontState.motion = frontClip;
            sm.defaultState = frontState;

            var sideState = sm.AddState("Idle_Side", new Vector3(260, 120, 0));
            sideState.motion = sideClip;

            var backState = sm.AddState("Idle_Back", new Vector3(260, 200, 0));
            backState.motion = backClip;

            var talkState = sm.AddState("Talk", new Vector3(520, 40, 0));
            talkState.motion = talkClip;

            AddAnyTransition(sm, frontState, "ViewDir", AnimatorConditionMode.Equals, 0, false);
            AddAnyTransition(sm, sideState, "ViewDir", AnimatorConditionMode.Equals, 1, false);
            AddAnyTransition(sm, backState, "ViewDir", AnimatorConditionMode.Equals, 2, false);

            var anyToTalk = sm.AddAnyStateTransition(talkState);
            anyToTalk.AddCondition(AnimatorConditionMode.If, 0, "IsTalking");
            anyToTalk.hasExitTime = false;
            anyToTalk.canTransitionToSelf = false;
            anyToTalk.duration = 0.05f;

            var endTalk = talkState.AddTransition(frontState);
            endTalk.AddCondition(AnimatorConditionMode.IfNot, 0, "IsTalking");
            endTalk.hasExitTime = false;
            endTalk.duration = 0.05f;

            // 2. Assemble 3D Root + 2D Sprite Child + CapsuleCollider
            var root = new GameObject(_npcName);
            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.height = _npcHeightWorldUnits;
            capsule.radius = 0.28f;
            capsule.center = new Vector3(0f, _npcHeightWorldUnits * 0.5f, 0f);

            var animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;

            var visualChild = new GameObject("SpriteBillboard");
            visualChild.transform.SetParent(root.transform, false);

            var sr = visualChild.AddComponent<SpriteRenderer>();
            sr.sprite = _idleFrontSprites[0];
            sr.shadowCastingMode = ShadowCastingMode.TwoSided;
            sr.receiveShadows = true;

            // Auto-scale sprite height to match realistic 1.72m world scale
            float rawHeight = Mathf.Max(sr.sprite.bounds.size.y, 0.01f);
            float scale = _npcHeightWorldUnits / rawHeight;
            visualChild.transform.localScale = Vector3.one * scale;
            visualChild.transform.localPosition = new Vector3(0f, _npcHeightWorldUnits * 0.5f, 0f);

            var npcCtrl = root.AddComponent<Cyanotype2DNPCController>();
            var interactable = root.AddComponent<WorldInteractableItem>();
            interactable.ConfigureNPC(_npcName);

            string prefabPath = Path.Combine(_outputDir, $"{_npcName}.prefab").Replace("\\", "/");
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            DestroyImmediate(root);

            AssetDatabase.SaveAssets();
            Debug.Log($"[Cyanotype] Created 2D-in-3D NPC prefab at {prefabPath}");
        }

        private AnimationClip CreateSpriteClip(string clipName, List<Sprite> sprites)
        {
            var clip = new AnimationClip { frameRate = _frameRate };
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            var binding = new EditorCurveBinding
            {
                path = "SpriteBillboard",
                type = typeof(SpriteRenderer),
                propertyName = "m_Sprite"
            };

            var keys = new ObjectReferenceKeyframe[sprites.Count];
            for (int i = 0; i < sprites.Count; i++)
            {
                keys[i] = new ObjectReferenceKeyframe { time = i / _frameRate, value = sprites[i] };
            }

            AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);
            string path = Path.Combine(_outputDir, $"{clipName}.anim").Replace("\\", "/");
            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }

        private static void AddAnyTransition(AnimatorStateMachine sm, AnimatorState dest, string param, AnimatorConditionMode mode, float threshold, bool isTalking)
        {
            var t = sm.AddAnyStateTransition(dest);
            t.AddCondition(mode, threshold, param);
            t.AddCondition(isTalking ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, "IsTalking");
            t.hasExitTime = false;
            t.canTransitionToSelf = false;
            t.duration = 0.05f;
        }
    }
}
#endif
