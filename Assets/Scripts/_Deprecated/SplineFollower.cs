using UnityEngine;
using UnityEngine.Splines;
using UnityEngine.InputSystem;

public class SplineFollower : MonoBehaviour
{
    public SplineContainer spline;
    public float speed = 0.05f;

    [Header("Lane Settings")]
    public int totalLanes = 7;
    public float laneSpacing = 1.5f;   // distance between lane centers
    public float laneChangeSpeed = 5f; // how fast you glide between lanes

    float t = 0f;
    int targetLane = 3;        // start in the middle lane (0-6, center = 3)
    float currentOffset = 0f;  // smoothed sideways offset

    void Update()
    {
        // --- Forward speed control (up/down) ---
        if (Keyboard.current.upArrowKey.isPressed)
            speed += 0.1f * Time.deltaTime;
        if (Keyboard.current.downArrowKey.isPressed)
            speed -= 0.1f * Time.deltaTime;
        speed = Mathf.Clamp(speed, 0.01f, 2f);

        // --- Lane changing (left/right) ---
        if (Keyboard.current.leftArrowKey.wasPressedThisFrame)
            targetLane -= 1;
        if (Keyboard.current.rightArrowKey.wasPressedThisFrame)
            targetLane += 1;
        targetLane = Mathf.Clamp(targetLane, 0, totalLanes - 1);

        // Convert lane number to a target sideways offset
        // (center lane = 0 offset)
        float centerLane = (totalLanes - 1) / 2f;
        float targetOffset = (targetLane - centerLane) * laneSpacing;

        // Smoothly glide toward the target lane
        currentOffset = Mathf.Lerp(currentOffset, targetOffset, laneChangeSpeed * Time.deltaTime);

        // --- Move forward along the spline ---
        t += speed * Time.deltaTime;
        if (t > 1f) t -= 1f;

        // Get the spline position and direction at this point
        Vector3 pos = spline.EvaluatePosition(t);
        Vector3 tangent = spline.EvaluateTangent(t);

        // Sideways direction perpendicular to the road
        Vector3 sideways = Vector3.Cross(Vector3.up, tangent).normalized;

        // Apply the lane offset
        Vector3 finalPos = pos + sideways * currentOffset;

        if (!float.IsNaN(finalPos.x) && !float.IsNaN(finalPos.y) && !float.IsNaN(finalPos.z))
            transform.position = finalPos;

        // --- Face direction of travel (down its own lane) ---
        float nextT = t + 0.01f;
        if (nextT > 1f) nextT -= 1f;

        Vector3 nextPosCenter = spline.EvaluatePosition(nextT);
        Vector3 nextTangent = spline.EvaluateTangent(nextT);
        Vector3 nextSideways = Vector3.Cross(Vector3.up, nextTangent).normalized;

        // Apply the SAME lane offset to the look-ahead point
        Vector3 nextPos = nextPosCenter + nextSideways * currentOffset;

        if (!float.IsNaN(nextPos.x))
            transform.LookAt(nextPos);
    }
}