using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spins around world Y and carries any PlayerCharacterController standing on its top surface,
/// so the player drifts with the spin and has to keep re-centering (the "balance" disk).
/// Detection is an OverlapBox sitting just above the platform's top, sized by `radius`.
/// </summary>
public class RotatingPlatform : MonoBehaviour
{
    [Tooltip("Spin speed in degrees/second. Negative = other direction.")]
    public float degPerSec = 40f;
    [Tooltip("Horizontal half-extent of the carry detection box (≈ disk radius).")]
    public float radius = 3f;
    [Tooltip("How far above the top surface a player still counts as 'standing on it'.")]
    public float topThickness = 1.2f;

    Collider col;
    readonly HashSet<PlayerCharacterController> carried = new HashSet<PlayerCharacterController>();

    void Awake()
    {
        col = GetComponent<Collider>();
        if (col == null)
        {
            var dv = transform.Find("DiskVisual");
            if (dv != null) col = dv.GetComponent<Collider>();
        }
        if (col == null) col = GetComponentInChildren<Collider>();
    }

    void Update()
    {
        float d = degPerSec * Time.deltaTime;
        Vector3 center = transform.position;
        float topY = col != null ? col.bounds.max.y : center.y;

        Vector3 boxCenter = new Vector3(center.x, topY + topThickness * 0.5f, center.z);
        Vector3 half = new Vector3(radius, topThickness * 0.5f + 0.05f, radius);

        carried.Clear();
        var hits = Physics.OverlapBox(boxCenter, half, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        foreach (var h in hits)
        {
            var p = h.GetComponentInParent<PlayerCharacterController>();
            if (p == null || !carried.Add(p)) continue;
            Vector3 rel = p.transform.position - center; rel.y = 0f;
            Vector3 rotated = Quaternion.Euler(0f, d, 0f) * rel;
            p.ExternalMove(rotated - rel);
        }

        transform.Rotate(0f, d, 0f, Space.World);
    }
}
