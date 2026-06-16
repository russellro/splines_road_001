using UnityEngine;

public class CameraFollower : MonoBehaviour
{
    [SerializeField] private Transform target;

    [Header("Position")]
    [SerializeField] private Vector3 offset = new Vector3(0f, 4f, -12f);
    [Tooltip("Lower = smoother but more trailing. ~0.3 is a good start.")]
    [SerializeField, Min(0.01f)] private float followSmoothTime = 0.3f;
    [Tooltip("How quickly the camera's orbit follows the rider. Lower = smoother turns/hills.")]
    [SerializeField, Min(0f)] private float orbitSmoothness = 5f;

    [Header("Aim")]
    [SerializeField] private float lookHeight = 1.5f;
    [SerializeField, Min(0f)] private float rotationSmoothness = 6f;

    private Vector3 smoothedPoint;
    private Vector3 pointVelocity;
    private Quaternion smoothedOrbit;
    private bool initialized;

    private void LateUpdate()
    {
        if (target == null)
        {
            return;
        }

        if (!initialized)
        {
            smoothedPoint = target.position;
            smoothedOrbit = target.rotation;
            initialized = true;
        }

        // 1) Low-pass the point we follow. Filters high-frequency path wobble
        //    (worst in turns on hills) before it can reach the camera.
        smoothedPoint = Vector3.SmoothDamp(
            smoothedPoint, target.position, ref pointVelocity, followSmoothTime);

        // 2) Smooth the orbit toward the rider's rotation. Keeping (smoothed) pitch
        //    lifts the camera to stay above the road on slopes, while the smoothing
        //    removes the per-bump facets that caused the jitter.
        float orbitBlend = 1f - Mathf.Exp(-orbitSmoothness * Time.deltaTime);
        smoothedOrbit = Quaternion.Slerp(smoothedOrbit, target.rotation, orbitBlend);

        transform.position = smoothedPoint + smoothedOrbit * offset;

        Vector3 lookTarget = smoothedPoint + Vector3.up * lookHeight;
        Vector3 lookDirection = lookTarget - transform.position;
        if (lookDirection.sqrMagnitude < 0.0001f)
        {
            return;
        }

        float rotationBlend = 1f - Mathf.Exp(-rotationSmoothness * Time.deltaTime);
        Quaternion desiredRotation = Quaternion.LookRotation(lookDirection, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, rotationBlend);
    }
}