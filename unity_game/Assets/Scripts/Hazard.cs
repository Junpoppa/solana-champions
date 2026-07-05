using UnityEngine;

/// <summary>
/// Hits the player on contact: knockback + brief stun (or full respawn). Basis for the future
/// "card hits player" feature — call into PlayerCharacterController.ApplyKnockback / Stun.
/// </summary>
[RequireComponent(typeof(Collider))]
public class Hazard : MonoBehaviour
{
    [Header("Response")]
    public float knockbackForce = 14f;
    public float upForce = 4f;
    public float stunDuration = 0.4f;
    [Tooltip("If true, instead of knockback the player respawns at the last checkpoint.")]
    public bool respawnInstead = false;

    [Header("FX")]
    public GameObject hitVfx;
    public float cooldown = 0.5f;

    float lastHit = -99f;

    void OnTriggerEnter(Collider other) { TryHit(other); }
    void OnTriggerStay(Collider other) { TryHit(other); }

    void TryHit(Collider other)
    {
        if (Time.time - lastHit < cooldown) return;
        var p = other.GetComponentInParent<PlayerCharacterController>();
        if (p == null) return;
        lastHit = Time.time;

        if (respawnInstead && RaceManager.Instance != null)
        {
            RaceManager.Instance.RespawnPlayer();
        }
        else
        {
            Vector3 dir = p.transform.position - transform.position; dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = -p.transform.forward;
            dir.Normalize();
            p.ApplyKnockback(dir * knockbackForce + Vector3.up * upForce);
            p.Stun(stunDuration);
        }

        if (hitVfx != null) Instantiate(hitVfx, p.transform.position + Vector3.up * 1f, Quaternion.identity);
    }
}
