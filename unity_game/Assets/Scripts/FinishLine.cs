using UnityEngine;

/// <summary>Crossing this trigger ends the race.</summary>
[RequireComponent(typeof(Collider))]
public class FinishLine : MonoBehaviour
{
    void Reset()
    {
        var c = GetComponent<Collider>();
        if (c != null) c.isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        var p = other.GetComponentInParent<PlayerCharacterController>();
        if (p == null || RaceManager.Instance == null) return;
        RaceManager.Instance.TryFinish();   // only finishes once all checkpoints are passed
    }
}
