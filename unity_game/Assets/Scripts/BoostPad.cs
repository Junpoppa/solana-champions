using System.Collections;
using UnityEngine;

/// <summary>Speed-boost pad: temporarily raises the player's speedMultiplier.</summary>
[RequireComponent(typeof(Collider))]
public class BoostPad : MonoBehaviour
{
    public float boostMultiplier = 1.8f;
    public float duration = 2f;
    public GameObject activateVfx;

    float lastUse = -99f;

    void Reset()
    {
        var c = GetComponent<Collider>();
        if (c != null) c.isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        if (Time.time - lastUse < 0.3f) return;
        var p = other.GetComponentInParent<PlayerCharacterController>();
        if (p == null) return;
        lastUse = Time.time;
        StopAllCoroutines();
        StartCoroutine(Boost(p));
        if (activateVfx != null) Instantiate(activateVfx, p.transform.position + Vector3.up * 0.5f, Quaternion.identity);
    }

    IEnumerator Boost(PlayerCharacterController p)
    {
        p.speedMultiplier = boostMultiplier;
        yield return new WaitForSeconds(duration);
        // Only clear if nothing else changed it in the meantime (e.g. a slow zone).
        if (p != null && Mathf.Approximately(p.speedMultiplier, boostMultiplier)) p.speedMultiplier = 1f;
    }
}
