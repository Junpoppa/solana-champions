using System.Collections;
using UnityEngine;

/// <summary>
/// Roll Out difficulty escalation — the candy logs roll FASTER over time, mirroring the Spinner's
/// SpinnerDifficultyRamp but stripped of reversals, flashes and toasts (user wants "faster only, no cues").
///
/// Also owns the match-start freeze: every RollDrum is disabled at Start() so the logs sit still during
/// the 3·2·1 countdown, then re-enabled when IntroCountdown fires OnGo (the bean is unfrozen at the same
/// instant by IntroCountdown). After GO, each drum's degreesPerSecond is multiplied by (1+speedPct) every
/// beatInterval seconds, capped at its own base speed × speedCapMult.
///
/// Put this on a manager GameObject in RollOut.unity (can share the IntroCountdown object). RollOut has no
/// respawn (fall = game-over → lobby), so no per-life reset is needed.
/// </summary>
public class RollOutDifficultyRamp : MonoBehaviour
{
    [Header("Ramp")]
    [Tooltip("Seconds between each speed-up step.")]
    public float beatInterval = 20f;
    [Tooltip("Fractional speed increase per step (0.08 = +8%).")]
    public float speedPct = 0.08f;
    [Tooltip("Hard ceiling: each drum never exceeds base speed × this.")]
    public float speedCapMult = 2f;
    [Tooltip("Grace after GO before the logs start rolling.")]
    public float startDelay = 0.5f;

    private RollDrum[] drums;
    private float[] baseSpeeds;
    private Coroutine timeline;

    void Start()
    {
        drums = FindObjectsByType<RollDrum>(FindObjectsSortMode.None);
        baseSpeeds = new float[drums.Length];
        for (int i = 0; i < drums.Length; i++)
        {
            baseSpeeds[i] = drums[i].degreesPerSecond;
            drums[i].enabled = false; // frozen until GO
        }

        // Multiplayer: spread players along the top of the spawn band (single-player keeps the baked spot).
        if (WebBridge.Multiplayer && NetBridge.HasMatch)
        {
            var pl = FindFirstObjectByType<CharacterControls>();
            if (pl != null)
            {
                Vector3 spawn = MultiplayerSpawns.RollOutPoint(NetBridge.MySpawnIndex, NetBridge.PlayerCount);
                pl.transform.position = spawn;
                pl.checkPoint = spawn;
                var rb = pl.GetComponent<Rigidbody>();
                if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            }
        }

        var countdown = FindFirstObjectByType<IntroCountdown>();
        if (countdown != null) countdown.OnGo += Begin;
        else Begin(); // no countdown in the scene -> just start (editor fallback)
    }

    void Begin()
    {
        if (timeline != null) StopCoroutine(timeline);
        timeline = StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        if (startDelay > 0f) yield return new WaitForSeconds(startDelay);
        for (int i = 0; i < drums.Length; i++)
            if (drums[i] != null) drums[i].enabled = true; // logs start rolling on GO

        while (true)
        {
            yield return new WaitForSeconds(beatInterval);
            for (int i = 0; i < drums.Length; i++)
            {
                if (drums[i] == null) continue;
                float cap = baseSpeeds[i] * speedCapMult;
                drums[i].degreesPerSecond = Mathf.Min(drums[i].degreesPerSecond * (1f + speedPct), cap);
            }
        }
    }
}
