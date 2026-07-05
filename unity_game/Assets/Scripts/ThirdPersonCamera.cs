using UnityEngine;

/// <summary>
/// Smooth chase camera that sits behind the target's facing and looks slightly down at it.
/// </summary>
public class ThirdPersonCamera : MonoBehaviour
{
    public Transform target;
    [Tooltip("Offset in the target's local space (behind = negative Z, above = positive Y).")]
    public Vector3 offset = new Vector3(0f, 4f, -7f);
    public float followLerp = 10f;
    public float lookHeight = 1f;

    void LateUpdate()
    {
        if (target == null) return;
        Vector3 desired = target.position + target.rotation * offset;
        transform.position = Vector3.Lerp(transform.position, desired, followLerp * Time.deltaTime);
        transform.LookAt(target.position + Vector3.up * lookHeight);
    }
}
