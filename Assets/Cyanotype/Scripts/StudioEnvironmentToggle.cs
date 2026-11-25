using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class StudioEnvironmentToggle : MonoBehaviour
{
    [Header("References")]
    public CyanotypeEnvironmentRig rig;
    public GameObject studioCeiling;

    [Header("UV Exposure Profiles")]
    public float indoorWindowUV = 0.06f;
    public float indoorSunUV = 0.0f;
    public float outdoorSunUV = 1.45f;
    public float outdoorSkyUV = 0.25f;

    public bool ceilingRemoved = false;

    void Start()
    {
        if (rig == null)
            rig = GetComponent<CyanotypeEnvironmentRig>();

        ApplyState();
    }

    void Update()
    {
        bool togglePressed = false;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame)
            togglePressed = true;
#else
        if (Input.GetKeyDown(KeyCode.T))
            togglePressed = true;
#endif
        if (togglePressed)
        {
            ToggleCeiling();
        }
    }

    public void ToggleCeiling()
    {
        ceilingRemoved = !ceilingRemoved;
        ApplyState();
    }

    public void ApplyState()
    {
        if (studioCeiling != null)
            studioCeiling.SetActive(!ceilingRemoved);

        if (rig != null)
        {
            rig.peakSunUVIntensity = ceilingRemoved ? outdoorSunUV : indoorSunUV;
            rig.windowAmbientUV    = ceilingRemoved ? outdoorSkyUV : indoorWindowUV;
        }
    }

  
}