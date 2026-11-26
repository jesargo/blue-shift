#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Cyanotype.World;

namespace Cyanotype.EditorTools
{
    public class CyanotypeProceduralEnvBuilder : EditorWindow
    {
        private enum ProceduralArchetype { BotanicalPlant, FieldRock }

        private ProceduralArchetype _archetype = ProceduralArchetype.BotanicalPlant;
        private int _seed = 1904;
        private Material _targetMaterial;
        private string _outputDir = "Assets/Cyanotype/ProceduralEnv";

        [Header("Rock Settings (World Units)")]
        private float _rockRadius = 0.12f;
        private float _rockRoughness = 0.32f;

        [Header("Botanical Plant Settings (World Units)")]
        private int _frondCount = 7;
        private int _leafPairsPerFrond = 6;
        private float _frondLength = 0.34f;

        [MenuItem("Cyanotype/Procedural 3D Plant & Rock Builder")]
        public static void Open()
        {
            var win = GetWindow<CyanotypeProceduralEnvBuilder>("Procedural Env Builder");
            win.minSize = new Vector2(400f, 340f);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Procedural 3D Environment Prop Generator", EditorStyles.boldLabel);
            _archetype = (ProceduralArchetype)EditorGUILayout.EnumPopup("Archetype", _archetype);
            _seed = EditorGUILayout.IntField("Deterministic Seed", _seed);
            _targetMaterial = (Material)EditorGUILayout.ObjectField("Surface Material", _targetMaterial, typeof(Material), false);
            _outputDir = EditorGUILayout.TextField("Output Folder", _outputDir);

            EditorGUILayout.Space(8);
            if (_archetype == ProceduralArchetype.FieldRock)
            {
                _rockRadius = EditorGUILayout.Slider("Rock Radius (m)", _rockRadius, 0.04f, 0.45f);
                _rockRoughness = EditorGUILayout.Slider("Facet Roughness", _rockRoughness, 0.05f, 0.65f);
            }
            else
            {
                _frondCount = EditorGUILayout.IntSlider("Frond Count", _frondCount, 3, 14);
                _leafPairsPerFrond = EditorGUILayout.IntSlider("Leaflet Pairs / Frond", _leafPairsPerFrond, 3, 12);
                _frondLength = EditorGUILayout.Slider("Frond Length (m)", _frondLength, 0.12f, 0.65f);
            }

            EditorGUILayout.Space(12);
            if (GUILayout.Button("Generate Mesh Asset & Interactable Prefab", GUILayout.Height(34)))
            {
                BuildProceduralAsset();
            }
        }

        private void BuildProceduralAsset()
        {
            if (!AssetDatabase.IsValidFolder(_outputDir))
            {
                Directory.CreateDirectory(_outputDir);
                AssetDatabase.Refresh();
            }

            Random.InitState(_seed);
            string baseName = $"{_archetype}_{_seed}";
            Mesh mesh = _archetype == ProceduralArchetype.FieldRock
                ? GenerateRockMesh(_rockRadius, _rockRoughness, _seed)
                : GeneratePlantMesh(_frondCount, _leafPairsPerFrond, _frondLength, _seed);

            string meshPath = Path.Combine(_outputDir, $"{baseName}_Mesh.asset").Replace("\\", "/");
            AssetDatabase.CreateAsset(mesh, meshPath);

            var go = new GameObject(baseName);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _targetMaterial != null
                ? _targetMaterial
                : AssetDatabase.GetBuiltinExtraResource<Material>("Default-Diffuse.mat");

            var col = go.AddComponent<MeshCollider>();
            col.sharedMesh = mesh;
            col.convex = true;

            var interactable = go.AddComponent<WorldInteractableItem>();
            if (_archetype == ProceduralArchetype.FieldRock)
            {
                var rb = go.AddComponent<Rigidbody>();
                rb.mass = Mathf.Max(0.2f, _rockRadius * 12f);
                interactable.ConfigureProcedural(WorldInteractableKind.ProceduralRock, "Pick Up Field Stone", false);
            }
            else
            {
                interactable.ConfigureProcedural(WorldInteractableKind.ProceduralBotanical, "Gather Botanical Frond", true);
            }

            string prefabPath = Path.Combine(_outputDir, $"{baseName}.prefab").Replace("\\", "/");
            PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            DestroyImmediate(go);

            AssetDatabase.SaveAssets();
            Debug.Log($"[Cyanotype] Built procedural interactable prefab: {prefabPath}");
        }

        private static Mesh GenerateRockMesh(float radius, float roughness, int seed)
        {
            var temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Mesh src = temp.GetComponent<MeshFilter>().sharedMesh;
            Mesh mesh = Instantiate(src);
            DestroyImmediate(temp);

            Vector3[] verts = mesh.vertices;
            float offset = seed * 0.137f;

            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 dir = verts[i].normalized;
                float n = Mathf.PerlinNoise(dir.x * 2.1f + offset, dir.z * 2.1f + dir.y * 1.7f);
                float r = radius * (1f + (n - 0.5f) * 2f * roughness);
                Vector3 p = dir * r;

                // Flatten bottom contact patch so rocks sit stably on terrain or paper corners
                if (p.y < -radius * 0.45f) p.y = -radius * 0.45f;
                p.y += radius * 0.45f;
                verts[i] = p;
            }

            mesh.vertices = verts;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh GeneratePlantMesh(int fronds, int pairs, float length, int seed)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            for (int f = 0; f < fronds; f++)
            {
                float yaw = (360f / fronds) * f + Random.Range(-14f, 14f);
                float arch = Random.Range(28f, 52f);
                Quaternion frondRot = Quaternion.Euler(0f, yaw, 0f);

                for (int p = 0; p < pairs; p++)
                {
                    float t = (p + 0.5f) / pairs;
                    float dist = t * length;
                    float droop = Mathf.Sin(t * Mathf.PI * 0.7f) * (length * 0.35f) * (arch / 45f);
                    Vector3 spinePos = frondRot * new Vector3(0f, droop, dist);

                    float leafLen = Mathf.Sin(Mathf.Clamp01(t * 1.15f) * Mathf.PI) * (length * 0.24f) + 0.015f;
                    float leafWid = leafLen * 0.34f;

                    for (int side = -1; side <= 1; side += 2)
                    {
                        Quaternion leafRot = frondRot * Quaternion.Euler(10f, side * 62f, side * -12f);
                        int baseIdx = verts.Count;

                        verts.Add(spinePos);
                        verts.Add(spinePos + leafRot * new Vector3(-leafWid * 0.5f, 0.004f, leafLen * 0.5f));
                        verts.Add(spinePos + leafRot * new Vector3(0f, -0.004f, leafLen));
                        verts.Add(spinePos + leafRot * new Vector3(leafWid * 0.5f, 0.004f, leafLen * 0.5f));

                        uvs.Add(new Vector2(0.5f, 0f));
                        uvs.Add(new Vector2(0f, 0.5f));
                        uvs.Add(new Vector2(0.5f, 1f));
                        uvs.Add(new Vector2(1f, 0.5f));

                        // Double-sided triangles so foliage renders from above and below
                        tris.Add(baseIdx); tris.Add(baseIdx + 1); tris.Add(baseIdx + 2);
                        tris.Add(baseIdx); tris.Add(baseIdx + 2); tris.Add(baseIdx + 3);
                        tris.Add(baseIdx); tris.Add(baseIdx + 2); tris.Add(baseIdx + 1);
                        tris.Add(baseIdx); tris.Add(baseIdx + 3); tris.Add(baseIdx + 2);
                    }
                }
            }

            var mesh = new Mesh { name = $"Botanical_{seed}" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
#endif