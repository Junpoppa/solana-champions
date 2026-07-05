using UnityEngine;

/// <summary>
/// Keeps the camera behind and above the player car, looking slightly down the
/// road — the PILL RUNNER style "chase" view. Follows the car's X gently so the
/// world feels alive when you steer, without making the player chase the camera.
///
/// SETUP (Phase 2): attach to the Main Camera, assign target = PlayerCar.
/// </summary>
public class CameraRig : MonoBehaviour
{
    [Header("Target")]
    public Transform target;

    [Header("Offset (relative to target)")]
    [Tooltip("How far behind the car (negative Z) and above (positive Y).")]
    public Vector3 offset = new Vector3(0f, 6f, -9f);

    [Tooltip("How much of the car's sideways movement the camera mirrors. "
           + "0 = camera stays centered, 1 = fully tracks. ~0.4 feels good.")]
    [Range(0f, 1f)]
    public float horizontalFollow = 0.4f;

    [Header("Smoothing")]
    public float followSmooth = 8f;

    [Header("Look")]
    [Tooltip("How far ahead of the car the camera aims.")]
    public float lookAheadZ = 8f;

    [Tooltip("Height of the look-at point.")]
    public float lookHeight = 1.5f;

    void LateUpdate()
    {
        if (target == null) return;

        // Desired position: fixed offset, but only partially tracking X.
        Vector3 desired = new Vector3(
            target.position.x * horizontalFollow + offset.x,
            offset.y,
            offset.z
        );

        transform.position = Vector3.Lerp(transform.position, desired, followSmooth * Time.deltaTime);

        // Look slightly ahead down the road, tracking the car's X a touch.
        Vector3 lookAt = new Vector3(
            target.position.x * horizontalFollow,
            lookHeight,
            target.position.z + lookAheadZ
        );
        transform.LookAt(lookAt);
    }
}
