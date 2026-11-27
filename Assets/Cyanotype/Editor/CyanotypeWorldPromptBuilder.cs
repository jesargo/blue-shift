#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Cyanotype.World;

namespace Cyanotype.EditorTools
{
    public static class CyanotypeWorldPromptBuilder
    {
        private const string IconFolder = "Assets/Cyanotype/UI/PromptIcons";
        private const string ScreenCanvasName = "Cyanotype_ScreenPromptCanvas";
        private const string WorldCanvasName = "Cyanotype_WorldPromptCanvas";

        [MenuItem("Cyanotype/Build World & Screen Prompt UI")]
        public static void BuildPromptUI()
        {
            var interactor = Object.FindAnyObjectByType<CyanotypeWorldInteractor>();
            if (interactor == null)
            {
                EditorUtility.DisplayDialog(
                    "Missing Interactor",
                    "Add CyanotypeWorldInteractor to your Player GameObject first.",
                    "OK");
                return;
            }

            if (!AssetDatabase.IsValidFolder(IconFolder))
            {
                Directory.CreateDirectory(IconFolder);
                AssetDatabase.Refresh();
            }

            DestroyExistingCanvas(ScreenCanvasName);
            DestroyExistingCanvas(WorldCanvasName);

            Font uiFont = ResolveBuiltinUIFont();

            // 1. Generate clean 64x64 procedural 2D icons
            Sprite gatherIcon = CreateOrLoadIcon("Icon_GatherLeaf.png", DrawLeafPattern);
            Sprite rockIcon = CreateOrLoadIcon("Icon_FieldRock.png", DrawRockPattern);
            Sprite talkIcon = CreateOrLoadIcon("Icon_TalkBubble.png", DrawBubblePattern);
            Sprite benchIcon = CreateOrLoadIcon("Icon_CyanotypeSun.png", DrawSunPattern);

            // 2. Build Screen-Space Overlay Canvas
            var screenCanvasGo = new GameObject(ScreenCanvasName, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(screenCanvasGo, "Create Cyanotype Prompt UI");

            var screenCanvas = screenCanvasGo.AddComponent<Canvas>();
            screenCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            screenCanvas.sortingOrder = 25;
            var scaler = screenCanvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            // Center Reticle Dot
            var dotGo = new GameObject("CenterReticleDot", typeof(RectTransform));
            dotGo.transform.SetParent(screenCanvasGo.transform, false);
            var dotImg = dotGo.AddComponent<Image>();
            var dotRect = dotGo.GetComponent<RectTransform>();
            dotRect.sizeDelta = new Vector2(6f, 6f);

            // Screen-Space Prompt Pill below reticle
            var pillGo = new GameObject("ScreenPromptPill", typeof(RectTransform));
            pillGo.transform.SetParent(screenCanvasGo.transform, false);
            var pillRect = pillGo.GetComponent<RectTransform>();
            pillRect.anchoredPosition = new Vector2(0f, -44f);
            pillRect.sizeDelta = new Vector2(280f, 38f);

            var pillBg = pillGo.AddComponent<Image>();
            pillBg.color = new Color(0.08f, 0.10f, 0.14f, 0.78f);
            var screenGroup = pillGo.AddComponent<CanvasGroup>();

            var layout = pillGo.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(10, 12, 5, 5);
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;

            // Key Badge [E]
            var keyGo = new GameObject("KeyBadge", typeof(RectTransform));
            keyGo.transform.SetParent(pillGo.transform, false);
            keyGo.GetComponent<RectTransform>().sizeDelta = new Vector2(24f, 24f);
            var keyBg = keyGo.AddComponent<Image>();
            keyBg.color = new Color(1f, 1f, 1f, 0.16f);

            var keyTextGo = new GameObject("KeyText", typeof(RectTransform));
            keyTextGo.transform.SetParent(keyGo.transform, false);
            keyTextGo.GetComponent<RectTransform>().sizeDelta = new Vector2(24f, 24f);
            var keyText = keyTextGo.AddComponent<Text>();
            keyText.font = uiFont;
            keyText.fontSize = 14;
            keyText.fontStyle = FontStyle.Bold;
            keyText.alignment = TextAnchor.MiddleCenter;
            keyText.color = Color.white;
            keyText.text = "E";

            // Context Icon
            var iconGo = new GameObject("ContextIcon", typeof(RectTransform));
            iconGo.transform.SetParent(pillGo.transform, false);
            iconGo.GetComponent<RectTransform>().sizeDelta = new Vector2(22f, 22f);
            var screenIconImg = iconGo.AddComponent<Image>();
            screenIconImg.sprite = gatherIcon;

            // Action Label
            var labelGo = new GameObject("ActionLabel", typeof(RectTransform));
            labelGo.transform.SetParent(pillGo.transform, false);
            labelGo.GetComponent<RectTransform>().sizeDelta = new Vector2(185f, 26f);
            var actionText = labelGo.AddComponent<Text>();
            actionText.font = uiFont;
            actionText.fontSize = 15;
            actionText.alignment = TextAnchor.MiddleLeft;
            actionText.color = Color.white;
            actionText.text = "Gather Specimen";

            // 3. Build World-Space Floating Marker Canvas
            var worldCanvasGo = new GameObject(WorldCanvasName, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(worldCanvasGo, "Create World Prompt Marker");

            var worldCanvas = worldCanvasGo.AddComponent<Canvas>();
            worldCanvas.renderMode = RenderMode.WorldSpace;
            var worldRect = worldCanvasGo.GetComponent<RectTransform>();
            worldRect.sizeDelta = new Vector2(64f, 64f);
            worldRect.localScale = Vector3.one * 0.003f;

            var worldBg = worldCanvasGo.AddComponent<Image>();
            worldBg.color = new Color(0.06f, 0.08f, 0.12f, 0.72f);
            var worldGroup = worldCanvasGo.AddComponent<CanvasGroup>();

            var worldIconGo = new GameObject("WorldIcon", typeof(RectTransform));
            worldIconGo.transform.SetParent(worldCanvasGo.transform, false);
            worldIconGo.GetComponent<RectTransform>().sizeDelta = new Vector2(44f, 44f);
            var worldIconImg = worldIconGo.AddComponent<Image>();
            worldIconImg.sprite = gatherIcon;

            // 4. Attach and wire CyanotypeWorldPromptHUD on Player
            var hud = interactor.GetComponent<CyanotypeWorldPromptHUD>();
            if (hud == null) hud = Undo.AddComponent<CyanotypeWorldPromptHUD>(interactor.gameObject);

            hud.AssignGeneratedHierarchy(
                screenGroup, dotImg, screenIconImg, actionText, keyText,
                worldCanvasGo.transform, worldGroup, worldIconImg,
                gatherIcon, rockIcon, talkIcon, benchIcon);

            EditorUtility.SetDirty(hud);
            Selection.activeGameObject = interactor.gameObject;
            Debug.Log("[Cyanotype] Built and wired Screen-Space + World-Space Prompt UI.");
        }

        private static void DestroyExistingCanvas(string objectName)
        {
            var existing = GameObject.Find(objectName);
            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing);
            }
        }

        private static Font ResolveBuiltinUIFont()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
            {
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }
            return font;
        }

        private static Sprite CreateOrLoadIcon(string fileName, System.Func<int, int, float> sdfEvaluator)
        {
            string assetPath = Path.Combine(IconFolder, fileName).Replace("\\", "/");
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float alpha = Mathf.Clamp01(sdfEvaluator(x, y));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            tex.Apply();
            File.WriteAllBytes(assetPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        private static float DrawLeafPattern(int x, int y)
        {
            float u = (x - 32f) / 24f;
            float v = (y - 32f) / 24f;
            float rx = (u + v) * 0.707f;
            float ry = (v - u) * 0.707f;
            float d = Mathf.Sqrt((rx * rx) / 0.32f + (ry * ry) / 0.85f);
            return 1f - Mathf.SmoothStep(0.88f, 1.0f, d);
        }

        private static float DrawRockPattern(int x, int y)
        {
            float u = (x - 32f) / 24f;
            float v = (y - 28f) / 20f;
            if (v < -0.55f) return 0f;
            float d = Mathf.Sqrt(u * u * 0.85f + v * v * 1.15f);
            return 1f - Mathf.SmoothStep(0.85f, 0.98f, d);
        }

        private static float DrawBubblePattern(int x, int y)
        {
            float u = (x - 32f) / 24f;
            float v = (y - 35f) / 18f;
            float body = 1f - Mathf.SmoothStep(0.88f, 1.0f, Mathf.Sqrt(u * u * 0.78f + v * v * 1.1f));
            bool tail = (x >= 18 && x <= 28 && y >= 12 && y <= 22 && (x - 18) <= (y - 12));
            return Mathf.Max(body, tail ? 1f : 0f);
        }

        private static float DrawSunPattern(int x, int y)
        {
            float dx = x - 32f;
            float dy = y - 32f;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            float core = 1f - Mathf.SmoothStep(10f, 12f, r);
            float angle = Mathf.Atan2(dy, dx);
            float rays = (r > 15f && r < 26f && Mathf.Cos(angle * 8f) > 0.78f) ? 1f : 0f;
            return Mathf.Max(core, rays);
        }
    }
}
#endif