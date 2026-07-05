using UnityEngine;

/// <summary>
/// Ping-pongs a hazard object back and forth across the track (e.g. a slime/turtle patrol).
/// Pair with a Hazard component + trigger collider so contact knocks the player back.
/// </summary>
public class MovingHazard : MonoBehaviour
{
    [Tooltip("Local direction to oscillate along (will be normalized).")]
    public Vector3 axis = Vector3.right;
    [Tooltip("Half-travel distance from the start position.")]
    public float distance = 6f;
    public float speed = 3f;
    [Tooltip("Face the direction of travel.")]
    public bool faceTravel = true;

    Vector3 center;
    float phase;

    void Start()
    {
        center = transform.position;
    }

    void Update()
    {
        phase += speed * Time.deltaTime;
        Vector3 dir = axis.sqrMagnitude > 0.0001f ? axis.normalized : Vector3.right;
        transform.position = center + dir * Mathf.Sin(phase) * distance;
        if (faceTravel)
        {
            float vel = Mathf.Cos(phase);
            Vector3 look = dir * vel; look.y = 0f;
            if (look.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(look);
        }
    }
}
