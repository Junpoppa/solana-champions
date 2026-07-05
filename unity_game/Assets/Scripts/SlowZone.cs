using UnityEngine;

/// <summary>Penalty zone: slows the player while inside, restores speed on exit.</summary>
[RequireComponent(typeof(Collider))]
public class SlowZone : MonoBehaviour
{
    public float slowMultiplier = 0.5f;
    public GameObject zoneVfx;

    void Reset()
    {
        var c = GetComponent<Collider>();
        if (c != null) c.isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        var p = other.GetComponentInParent<PlayerCharacterController>();
        if (p != null) p.speedMultiplier = slowMultiplier;
    }

    void OnTriggerExit(Collider other)
    {
        var p = other.GetComponentInParent<PlayerCharacterController>();
        if (p != null) p.speedMultiplier = 1f;
    }
}
