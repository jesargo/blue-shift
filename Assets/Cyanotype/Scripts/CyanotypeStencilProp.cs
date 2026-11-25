using UnityEngine;

[DisallowMultipleComponent]
public class CyanotypeStencilProp : MonoBehaviour
{
    [Header("Optional PNG / Alpha Stencil Mask")]
    [Tooltip("Assign any black-and-white or transparent PNG texture to cast crisp photographic negative/stencil shadows.")]
    public Texture2D stencilMaskTexture;

    [Range(0f, 1f)]
    [Tooltip("Opacity of the stencil ink (1.0 = completely blocks UV, 0.5 = semi-translucent vellum).")]
    public float uvBlockingOpacity = 0.96f;

    public bool invertMask = false;

    void Awake()
    {
        SetupPhysicsAndDragging();
    }

    void OnValidate()
    {
        if (!Application.isPlaying)
            SetupPhysicsAndDragging();
    }

    public void SetupPhysicsAndDragging()
    {
        // 1. Ensure layer is Default (0) so shadowCasterLayers always detects it
        if (gameObject.layer == 2)
            gameObject.layer = 0;

        // 2. Ensure a solid non-trigger Collider exists with sufficient thickness for UV raycasts
        Collider col = GetComponentInChildren<Collider>();
        if (col == null)
        {
            MeshFilter mf = GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                var mc = gameObject.AddComponent<MeshCollider>();
                mc.convex = true;
            }
            else
            {
                var bc = gameObject.AddComponent<BoxCollider>();
                bc.size = new Vector3(1f, 1f, 0.05f);
            }
        }
        else
        {
            col.isTrigger = false;
            if (col is BoxCollider box && box.size.z < 0.03f)
                box.size = new Vector3(box.size.x, box.size.y, 0.05f);
        }

        // 3. Position slightly above the paper surface (0.806 + 0.006) so upward shadow rays never start inside the collider
        if (transform.position.y < 0.75f || transform.position.y > 0.90f)
        {
            Vector3 pos = transform.position;
            pos.y = 0.812f;
            transform.position = pos;
        }

        if (GetComponent<DraggableShadowProp>() == null)
        {
            gameObject.AddComponent<DraggableShadowProp>();
        }

        // 4. Apply stencil texture to renderer if assigned
        if (stencilMaskTexture != null)
        {
            var rend = GetComponent<Renderer>();
            if (rend != null)
            {
                Shader lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                Material mat = new Material(lit);
                mat.mainTexture = stencilMaskTexture;
                mat.color = new Color(0.12f, 0.12f, 0.14f, 0.88f);
                rend.sharedMaterial = mat;
            }
        }
    }
}