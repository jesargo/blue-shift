using UnityEngine;

public class CyanotypeCameraRig : MonoBehaviour
{
    public Vector3 pivot = new Vector3(0f, 0.2f, 0f);
    public float distance = 6.5f;
    public float yaw = 0f;
    public float pitch = 52f;

    void LateUpdate()
    {
        // Right-mouse drag orbits the darkroom workbench
        if (Input.GetMouseButton(1))
        {
            yaw += Input.GetAxis("Mouse X") * 4.5f;
            pitch -= Input.GetAxis("Mouse Y") * 4.5f;
            pitch = Mathf.Clamp(pitch, 15f, 88f);
        }

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.001f)
        {
            distance = Mathf.Clamp(distance - scroll * 4.0f, 2.5f, 14.0f);
        }

        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        transform.position = pivot - (rot * Vector3.forward) * distance;
        transform.LookAt(pivot);
    }
}
