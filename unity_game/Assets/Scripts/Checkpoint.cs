using UnityEngine;

/// <summary>Ordered checkpoint trigger. Reaching it sets the respawn point in RaceManager.</summary>
[RequireComponent(typeof(Collider))]
public class Checkpoint : MonoBehaviour
{
    [Tooltip("Order along the track (0,1,2…). Only forward progress counts.")]
    public int index = 0;

    void Reset()
    {
        var c = GetComponent<Collider>();
        if (c != null) c.isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        var p = other.GetComponentInParent<PlayerCharacterController>();
        if (p == null || RaceManager.Instance == null) return;
        RaceManager.Instance.ReachCheckpoint(index);   // validates sequence internally
    }
}
