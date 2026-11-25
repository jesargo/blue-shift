using UnityEngine;
using System.Collections.Generic;

public class CyanotypePropTray : MonoBehaviour
{
    [Header("Workbench Placement")]
    [Tooltip("Center of the prop tray on the left side of the workbench.")]
    public Vector3 trayOrigin = new Vector3(-0.78f, 0.808f, -1.5f);

    [Tooltip("Optional reference to the CyanotypePaper transform so props rest at its exact Y height.")]
    public Transform paperSurface;

    [Tooltip("Layer assigned to spawned props. Ensure this layer is included in CyanotypeEnvironmentRig.shadowCasterLayers.")]
    public LayerMask propLayer = 0;

    [Header("Prop Materials (Optional - Auto-created if null)")]
    public Material metalMaterial;
    public Material botanicalMaterial;
    public Material glassMaterial;

    private readonly List<GameObject> _spawnedProps = new List<GameObject>();

    void Start()
    {
        if (paperSurface == null)
        {
            var rig = FindFirstObjectByType<CyanotypeEnvironmentRig>();
            if (rig != null && rig.paperMeshFilter != null)
                paperSurface = rig.paperMeshFilter.transform;
        }

        foreach (var trayCol in GetComponents<Collider>())
        {
            trayCol.gameObject.layer = 2;
        }

        EnsureMaterials();
        SpawnAllProps();
    }

    private void EnsureMaterials()
    {
        Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
        if (litShader == null) litShader = Shader.Find("Standard");

        if (metalMaterial == null)
        {
            metalMaterial = new Material(litShader);
            metalMaterial.color = new Color(0.72f, 0.58f, 0.28f);
            metalMaterial.SetFloat("_Metallic", 0.85f);
            metalMaterial.SetFloat("_Smoothness", 0.65f);
        }

        if (botanicalMaterial == null)
        {
            botanicalMaterial = new Material(litShader);
            botanicalMaterial.color = new Color(0.24f, 0.45f, 0.22f);
            botanicalMaterial.SetFloat("_Smoothness", 0.25f);
        }

        if (glassMaterial == null)
        {
            glassMaterial = new Material(litShader);
            glassMaterial.color = new Color(0.75f, 0.90f, 0.95f, 0.45f);
            glassMaterial.SetFloat("_Smoothness", 0.92f);
        }
    }

    [ContextMenu("Spawn Photogram Props")]
    public void SpawnAllProps()
    {
        ClearProps();

        float paperTopY = paperSurface != null ? paperSurface.position.y : 0.806f;
        float surfaceY = Mathf.Max(paperTopY + 0.002f, trayOrigin.y);
        Vector3 origin = new Vector3(trayOrigin.x, surfaceY, trayOrigin.z);

        SpawnSkeletonKey(origin + new Vector3(-0.18f, 0f, 0.22f));
        SpawnFernFrond(origin + new Vector3(0.15f, 0f, 0.22f));

        SpawnCogwheel(origin + new Vector3(-0.18f, 0f, 0.0f));
        SpawnComb(origin + new Vector3(0.15f, 0f, 0.0f));

        SpawnMeshGrid(origin + new Vector3(-0.18f, 0f, -0.22f));
        SpawnGlassRing(origin + new Vector3(0.15f, 0f, -0.22f));
    }

    public void ClearProps()
    {
        foreach (var go in _spawnedProps)
        {
            if (go != null) Destroy(go);
        }
        _spawnedProps.Clear();
    }

    private GameObject CreatePropRoot(string propName, Vector3 pos)
    {
        GameObject root = new GameObject(propName);
        root.transform.SetParent(transform);
        root.transform.position = pos;
        root.layer = GetSingleLayerIndex(propLayer);

        BoxCollider rootCol = root.AddComponent<BoxCollider>();
        rootCol.isTrigger = true;
        rootCol.size = new Vector3(0.16f, 0.03f, 0.16f);
        rootCol.center = new Vector3(0f, 0.015f, 0f);

        DraggableShadowProp drag = root.AddComponent<DraggableShadowProp>();
        drag.matchInitialHeight = false;
        drag.tableY = pos.y;
        drag.minHeight = 0.0f;

        _spawnedProps.Add(root);
        return root;
    }

    private static int GetSingleLayerIndex(LayerMask mask)
    {
        int value = mask.value;
        if (value <= 0) return 0;

        for (int i = 0; i < 32; i++)
        {
            if ((value & (1 << i)) != 0)
                return i;
        }
        return 0;
    }

    private void AddPart(GameObject parent, PrimitiveType type, Vector3 localPos, Vector3 localEuler, Vector3 localScale, Material mat, float uvTransmittance = 0f)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        part.transform.SetParent(parent.transform, false);
        part.transform.localPosition = localPos;
        part.transform.localEulerAngles = localEuler;
        part.transform.localScale = localScale;
        part.layer = parent.layer;

        Collider col = part.GetComponent<Collider>();
        if (col != null)
            col.isTrigger = false;

        var rend = part.GetComponent<Renderer>();
        if (rend != null) rend.sharedMaterial = mat;

        if (uvTransmittance > 0f)
        {
            var opt = part.AddComponent<UVTransmittanceProp>();
            opt.uvTransmittance = uvTransmittance;
        }
    }

    private void SpawnSkeletonKey(Vector3 pos)
    {
        GameObject key = CreatePropRoot("Prop_SkeletonKey", pos);
        float h = 0.014f;

        int segs = 10;
        float radius = 0.032f;
        for (int i = 0; i < segs; i++)
        {
            float ang = i * (360f / segs);
            float rad = ang * Mathf.Deg2Rad;
            Vector3 p = new Vector3(Mathf.Cos(rad) * radius, h * 0.5f, Mathf.Sin(rad) * radius - 0.065f);
            AddPart(key, PrimitiveType.Cube, p, new Vector3(0f, -ang, 0f), new Vector3(0.012f, h, 0.024f), metalMaterial);
        }

        AddPart(key, PrimitiveType.Cylinder, new Vector3(0f, h * 0.5f, 0.01f), new Vector3(90f, 0f, 0f), new Vector3(0.012f, 0.065f, 0.012f), metalMaterial);

        AddPart(key, PrimitiveType.Cube, new Vector3(0.016f, h * 0.5f, 0.055f), Vector3.zero, new Vector3(0.024f, h, 0.012f), metalMaterial);
        AddPart(key, PrimitiveType.Cube, new Vector3(0.012f, h * 0.5f, 0.038f), Vector3.zero, new Vector3(0.016f, h, 0.010f), metalMaterial);
        AddPart(key, PrimitiveType.Cube, new Vector3(0.018f, h * 0.5f, 0.022f), Vector3.zero, new Vector3(0.026f, h, 0.011f), metalMaterial);
    }

    private void SpawnFernFrond(Vector3 pos)
    {
        GameObject fern = CreatePropRoot("Prop_BotanicalFern", pos);
        float h = 0.012f;

        AddPart(fern, PrimitiveType.Cube, new Vector3(0f, h * 0.5f, 0f), Vector3.zero, new Vector3(0.008f, h, 0.21f), botanicalMaterial, 0.15f);

        for (int i = -3; i <= 3; i++)
        {
            float zOff = i * 0.026f;
            float taper = 1f - Mathf.Abs(i) * 0.16f;
            float len = 0.055f * taper;

            AddPart(fern, PrimitiveType.Cube, new Vector3(-len * 0.5f, h * 0.5f, zOff + 0.005f), new Vector3(0f, -22f, 0f), new Vector3(len, h, 0.015f), botanicalMaterial, 0.28f);
            AddPart(fern, PrimitiveType.Cube, new Vector3(len * 0.5f, h * 0.5f, zOff - 0.005f), new Vector3(0f, 22f, 0f), new Vector3(len, h, 0.015f), botanicalMaterial, 0.28f);
        }
    }

    private void SpawnCogwheel(Vector3 pos)
    {
        GameObject gear = CreatePropRoot("Prop_BrassCogwheel", pos);
        float h = 0.014f;
        int teeth = 12;
        float innerR = 0.038f;

        for (int i = 0; i < teeth; i++)
        {
            float ang = i * (360f / teeth);
            float rad = ang * Mathf.Deg2Rad;

            Vector3 rimPos = new Vector3(Mathf.Cos(rad) * innerR, h * 0.5f, Mathf.Sin(rad) * innerR);
            AddPart(gear, PrimitiveType.Cube, rimPos, new Vector3(0f, -ang, 0f), new Vector3(0.014f, h, 0.026f), metalMaterial);

            Vector3 toothPos = new Vector3(Mathf.Cos(rad) * (innerR + 0.012f), h * 0.5f, Mathf.Sin(rad) * (innerR + 0.012f));
            AddPart(gear, PrimitiveType.Cube, toothPos, new Vector3(0f, -ang, 0f), new Vector3(0.016f, h, 0.012f), metalMaterial);
        }

        AddPart(gear, PrimitiveType.Cube, new Vector3(0f, h * 0.5f, 0f), Vector3.zero, new Vector3(innerR * 2f, h * 0.85f, 0.010f), metalMaterial);
        AddPart(gear, PrimitiveType.Cube, new Vector3(0f, h * 0.5f, 0f), new Vector3(0f, 90f, 0f), new Vector3(innerR * 2f, h * 0.85f, 0.010f), metalMaterial);
    }

    private void SpawnComb(Vector3 pos)
    {
        GameObject comb = CreatePropRoot("Prop_FineComb", pos);
        float h = 0.012f;

        AddPart(comb, PrimitiveType.Cube, new Vector3(0f, h * 0.5f, -0.022f), Vector3.zero, new Vector3(0.14f, h, 0.014f), metalMaterial);

        for (int i = 0; i < 14; i++)
        {
            float x = -0.065f + i * 0.010f;
            AddPart(comb, PrimitiveType.Cube, new Vector3(x, h * 0.5f, 0.006f), Vector3.zero, new Vector3(0.006f, h, 0.045f), metalMaterial);
        }
    }

    private void SpawnMeshGrid(Vector3 pos)
    {
        GameObject mesh = CreatePropRoot("Prop_LaceMeshGrid", pos);
        float h = 0.012f;

        for (int i = -2; i <= 2; i++)
        {
            float offset = i * 0.024f;
            AddPart(mesh, PrimitiveType.Cube, new Vector3(offset, h * 0.5f, 0f), Vector3.zero, new Vector3(0.008f, h, 0.12f), metalMaterial);
            AddPart(mesh, PrimitiveType.Cube, new Vector3(0f, h * 0.5f, offset), Vector3.zero, new Vector3(0.12f, h, 0.008f), metalMaterial);
        }
    }

    private void SpawnGlassRing(Vector3 pos)
    {
        GameObject glass = CreatePropRoot("Prop_GlassLensRing", pos);
        float h = 0.016f;
        int segs = 14;
        float radius = 0.042f;

        for (int i = 0; i < segs; i++)
        {
            float ang = i * (360f / segs);
            float rad = ang * Mathf.Deg2Rad;
            Vector3 p = new Vector3(Mathf.Cos(rad) * radius, h * 0.5f, Mathf.Sin(rad) * radius);
            AddPart(glass, PrimitiveType.Cube, p, new Vector3(0f, -ang, 0f), new Vector3(0.016f, h, 0.024f), glassMaterial, 0.55f);
        }
    }
}

public class UVTransmittanceProp : MonoBehaviour
{
    [Range(0f, 1f)]
    [Tooltip("0 = completely opaque (solid shadow), 1 = fully UV transparent")]
    public float uvTransmittance = 0.35f;
}