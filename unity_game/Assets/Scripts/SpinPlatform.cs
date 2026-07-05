using UnityEngine;

/// <summary>
/// Spins a platform around the world vertical (Y) axis. Negative direction = counter-clockwise
/// viewed from above. Used by the mode-3 obstacle course "spinning platform" disks.
/// </summary>
public class SpinPlatform : MonoBehaviour
{
    [Tooltip("Degrees per second. Positive value, direction set by counterClockwise.")]
    public float degreesPerSecond = 45f;

    [Tooltip("If true, spin counter-clockwise when viewed from above.")]
    public bool counterClockwise = true;

    void Update()
    {
        float dir = counterClockwise ? -1f : 1f;
        transform.Rotate(0f, dir * degreesPerSecond * Time.deltaTime, 0f, Space.World);
    }
}
