using UnityEngine;

public class CameraFollower : MonoBehaviour
{
    [SerializeField] private Transform target;

    [Header("Position")]
    [SerializeField] private Vector3 offset = new Vector3(0f, 3f, -6f);
    [SerializeField, Min(0f)] private float positionSmoothness = 5f;

    [Header("Aim")]
    [SerializeField] private Vector3 lookOffset = new Vector3(0f, 1f, 0f);
    [SerializeField, Min(0f)] private float rotationSmoothness = 8f;

    private void LateUpdate()
    {
        if (target == null)
        {
            return;
        }

        float positionBlend =
            1f - Mathf.Exp(-positionSmoothness * Time.deltaTime);

        Vector3 desiredPosition =
            target.position + target.rotation * offset;

        transform.position = Vector3.Lerp(
            transform.position,
            desiredPosition,
            positionBlend);

        Vector3 lookDirection =
            target.position + lookOffset - transform.position;

        if (lookDirection.sqrMagnitude < 0.0001f)
        {
            return;
        }

        float rotationBlend =
            1f - Mathf.Exp(-rotationSmoothness * Time.deltaTime);

        Quaternion desiredRotation =
            Quaternion.LookRotation(lookDirection, Vector3.up);

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            desiredRotation,
            rotationBlend);
    }
}