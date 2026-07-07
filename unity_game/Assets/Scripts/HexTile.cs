using System.Collections;
using UnityEngine;

/// <summary>
/// Hex-A-Gone disappearing tile. On first Player contact: highlight (via MaterialPropertyBlock),
/// dip down slightly, then disable collider+renderer after disappearDelay so the player drops through.
/// Disable (not Destroy) so the arena can be reset later if needed.
/// </summary>
[RequireComponent(typeof(Renderer))]
public class HexTile : MonoBehaviour
{
    public float disappearDelay = 1.2f;
    public float dipDepth = 0.12f;
    public float dipTime = 0.12f;
    public Color highlightColor = new Color(1f, 1f, 0.85f);

    private bool triggered;
    private Renderer rend;
    private MaterialPropertyBlock mpb;
    private Vector3 startPos;

    void Awake()
    {
        rend = GetComponent<Renderer>();
        mpb = new MaterialPropertyBlock();
        startPos = transform.position;
    }

    void OnCollisionEnter(Collision collision)
    {
        if (triggered) return;
        if (collision.gameObject.CompareTag("Player"))
        {
            triggered = true;
            HexNet.ReportLocalStep(this); // solid collision = the LOCAL bean → sync watchers
            StartCoroutine(Vanish());
        }
    }

    // Remote avatars are kinematic + trigger-only (no solid collision), so they vanish tiles via this
    // path — keeping hex disappearance consistent across all clients. The local player uses OnCollisionEnter.
    void OnTriggerEnter(Collider other)
    {
        if (triggered) return;
        if (other.CompareTag("Player"))
        {
            triggered = true;
            StartCoroutine(Vanish());
        }
    }

    IEnumerator Vanish()
    {
        // highlight
        rend.GetPropertyBlock(mpb);
        mpb.SetColor("_BaseColor", highlightColor);
        mpb.SetColor("_Color", highlightColor);
        rend.SetPropertyBlock(mpb);

        // dip down a hair
        float t = 0f;
        Vector3 down = startPos + Vector3.down * dipDepth;
        while (t < dipTime)
        {
            t += Time.deltaTime;
            transform.position = Vector3.Lerp(startPos, down, t / dipTime);
            yield return null;
        }

        float remain = disappearDelay - dipTime;
        if (remain > 0f) yield return new WaitForSeconds(remain);

        var col = GetComponent<Collider>();
        if (col != null) col.enabled = false;
        rend.enabled = false;
    }

    /// <summary>
    /// Vanish driven by the server's hex-state (spectators). Idempotent — a tile the local
    /// simulation already vanished (remote trigger) is left alone. instant = late-join backlog:
    /// kill collider+renderer with no animation so the arena matches the match state on entry.
    /// </summary>
    public void NetVanish(bool instant)
    {
        if (triggered) return;
        triggered = true;
        if (instant)
        {
            var col = GetComponent<Collider>();
            if (col != null) col.enabled = false;
            if (rend == null) rend = GetComponent<Renderer>();
            rend.enabled = false;
        }
        else StartCoroutine(Vanish());
    }

    /// <summary>Restore the tile to its starting state (for a future arena reset).</summary>
    public void ResetTile()
    {
        StopAllCoroutines();
        triggered = false;
        transform.position = startPos;
        var col = GetComponent<Collider>();
        if (col != null) col.enabled = true;
        rend.enabled = true;
        if (mpb == null) mpb = new MaterialPropertyBlock();
        rend.GetPropertyBlock(mpb);
        mpb.Clear();
        rend.SetPropertyBlock(mpb);
    }
}
