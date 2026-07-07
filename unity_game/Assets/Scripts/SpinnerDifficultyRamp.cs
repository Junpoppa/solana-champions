using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// Spinner (Course) escalating difficulty. The green beam is now a FULL-DIAMETER bar in the scene (both
/// arms authored), so there's no runtime "extrude" — both beams just reverse and speed up over time.
/// Timeline starts at GO (IntroCountdown.OnGo) and runs in 10s "beats". Each beat:
///   • BOTH beams +<see cref="speedPct"/> (compounding, each capped at <see cref="speedCapMult"/>× its base)
///   • a direction event cycling [GREEN flip, BOTH flip, PURPLE flip, SAME-WAY sync] — so they spin opposite
///     most of the time but periodically snap to the SAME direction
///   • a beam color-flash + a small toast (with the live speed ×multiplier) so it all reads on screen
/// Tied to the current life: on respawn the beams reset to base speed/direction/rest-orientation and the
/// timeline restarts. The bean is parked on a SAFE diagonal spot between the rest bars; both hazards stay
/// off during the countdown + a short grace after GO so the bean can find its feet.
/// </summary>
public class SpinnerDifficultyRamp : MonoBehaviour
{
    [Header("Beams (auto-found by material if left empty)")]
    public SpinningBeamHazard greenBeam; // full-diameter low jump-over beam (Mat_Obstacle_Green)
    public SpinningBeamHazard bigBeam;   // full-diameter head-height beam (Mat_Obstacle_Purple)

    [Header("Beat timeline")]
    public float beatInterval = 10f;     // seconds per beat
    public float speedPct = 0.13f;       // BOTH beams +13% per beat, compounding
    public float speedCapMult = 3f;      // each beam capped at 3× its base speed
    public float beamStartDelay = 0.5f;  // grace after GO before the beams switch on
    public float reverseStartSeconds = 30f; // speed ramps from the start; reverse/sync only kicks in after this

    [Header("Safe spawn (diagonal gap between the rest bars)")]
    public Vector3 safeSpawn = new Vector3(187.8f, 13.1f, 27.8f);

    // base snapshot for per-life reset
    private float greenBaseSpeed, greenBaseSign, bigBaseSpeed, bigBaseSign;
    private Quaternion greenRestRot, bigRestRot;
    private Coroutine timeline;
    private CharacterControls player;

    private static readonly int ID_BaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int ID_Color = Shader.PropertyToID("_Color");
    private static readonly int ID_Emission = Shader.PropertyToID("_EmissionColor");

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void GameToast(string s);
#else
    private static void GameToast(string s) { Debug.Log("[GameToast] " + s); }
#endif

    void Start()
    {
        AutoFindBeams();
        if (greenBeam != null)
        {
            greenBaseSpeed = greenBeam.degreesPerSecond;
            greenBaseSign = greenBeam.spinSign;
            greenRestRot = greenBeam.transform.localRotation;
            greenBeam.enabled = false; // static during the countdown
        }
        if (bigBeam != null)
        {
            bigBaseSpeed = bigBeam.degreesPerSecond;
            bigBaseSign = bigBeam.spinSign;
            bigRestRot = bigBeam.transform.localRotation;
            bigBeam.enabled = false;
        }

        player = FindFirstObjectByType<CharacterControls>();
        if (player != null)
        {
            // Multiplayer: each player gets a distinct diagonal spawn; single-player keeps safeSpawn.
            Vector3 spawn = (WebBridge.Multiplayer && NetBridge.HasMatch)
                ? MultiplayerSpawns.SpinnerPoint(NetBridge.MySpawnIndex, NetBridge.PlayerCount)
                : safeSpawn;
            player.transform.position = spawn;
            player.checkPoint = spawn;
            var rb = player.GetComponent<Rigidbody>();
            if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            player.onRespawn += ResetAndRestart;
        }

        var countdown = FindFirstObjectByType<IntroCountdown>();
        if (countdown != null) countdown.OnGo += Begin;
    }

    void AutoFindBeams()
    {
        if (greenBeam != null && bigBeam != null) return;
        var beams = FindObjectsByType<SpinningBeamHazard>(FindObjectsSortMode.None);
        foreach (var b in beams)
        {
            bool isGreen = false;
            foreach (var rn in b.GetComponentsInChildren<Renderer>(true))
                if (rn.sharedMaterial != null && rn.sharedMaterial.name.IndexOf("Green", System.StringComparison.OrdinalIgnoreCase) >= 0)
                { isGreen = true; break; }
            if (isGreen && greenBeam == null) greenBeam = b;
            else if (!isGreen && bigBeam == null) bigBeam = b;
        }
        if (greenBeam == null || bigBeam == null)
        {
            if (beams.Length >= 1 && greenBeam == null) greenBeam = beams[0];
            if (beams.Length >= 2 && bigBeam == null) bigBeam = beams[1] == greenBeam ? beams[0] : beams[1];
        }
    }

    void Begin()
    {
        if (timeline != null) StopCoroutine(timeline);
        timeline = StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        // Spectator catch-up: a watcher joins MID-match, but their local OnGo just fired. Replay the
        // deterministic timeline (speed compounding + reversal cycle) silently up to "now" so the
        // beams match what the players see, then continue live from the fractional remainder.
        double elapsed = 0.0;
        if (WebBridge.Spectator && WebBridge.PendingGoAtMs > 0.0)
            elapsed = System.Math.Max(0.0, (System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - WebBridge.PendingGoAtMs) / 1000.0);

        // grace: bean is unfrozen at GO; hold the beams static a moment so it isn't swept instantly
        if (elapsed < beamStartDelay)
        {
            yield return new WaitForSeconds(beamStartDelay - (float)elapsed);
            elapsed = 0.0;
        }
        else elapsed -= beamStartDelay;
        if (greenBeam != null) greenBeam.enabled = true;
        if (bigBeam != null) bigBeam.enabled = true;

        int beat = 0;
        int syncDir = 1;
        float greenCap = greenBaseSpeed * speedCapMult;
        float bigCap = bigBaseSpeed * speedCapMult;
        int reverseStartBeat = Mathf.Max(1, Mathf.CeilToInt(reverseStartSeconds / beatInterval)); // reversals start here

        // fast-forward whole beats (spectator only; elapsed is 0 for players) — no flashes/toasts
        int ffBeats = (int)(elapsed / beatInterval);
        for (int i = 0; i < ffBeats; i++)
        {
            beat++;
            ApplyBeat(beat, ref syncDir, greenCap, bigCap, reverseStartBeat);
        }
        float nextWait = beatInterval - (float)(elapsed - ffBeats * (double)beatInterval);

        while (true)
        {
            yield return new WaitForSeconds(nextWait);
            nextWait = beatInterval;
            beat++;
            string dirLabel = ApplyBeat(beat, ref syncDir, greenCap, bigCap, reverseStartBeat);

            // feedback: flash both beams + toast with the live speed multiplier
            FlashBeam(greenBeam, new Color(0.55f, 1f, 0.8f));
            FlashBeam(bigBeam, new Color(0.8f, 0.5f, 1f));
            float mult = (greenBaseSpeed > 0f && greenBeam != null) ? greenBeam.degreesPerSecond / greenBaseSpeed : 1f;
            GameToast(dirLabel + "  ·  spd ×" + mult.ToString("0.0"));
        }
    }

    // One deterministic timeline beat: speed BOTH beams up (capped) + the direction event. Shared by
    // the live loop and the spectator fast-forward so both walk the exact same escalation.
    string ApplyBeat(int beat, ref int syncDir, float greenCap, float bigCap, int reverseStartBeat)
    {
        // 1) speed BOTH beams up (capped)
        if (greenBeam != null) greenBeam.degreesPerSecond = Mathf.Min(greenBeam.degreesPerSecond * (1f + speedPct), greenCap);
        if (bigBeam != null)   bigBeam.degreesPerSecond   = Mathf.Min(bigBeam.degreesPerSecond   * (1f + speedPct), bigCap);

        // 2) direction events — HELD BACK until reverseStartSeconds so the opening (speed-only) is survivable.
        //    From the gate on: GREEN flip → BOTH flip → PURPLE flip → SAME-WAY sync.
        if (beat < reverseStartBeat) return "FASTER";
        int f = (beat - reverseStartBeat) % 4;
        if (f == 0)      { if (greenBeam != null) greenBeam.spinSign *= -1f; return "GREEN REVERSE"; }
        else if (f == 1) { if (greenBeam != null) greenBeam.spinSign *= -1f; if (bigBeam != null) bigBeam.spinSign *= -1f; return "BOTH REVERSE"; }
        else if (f == 2) { if (bigBeam != null) bigBeam.spinSign *= -1f; return "PURPLE REVERSE"; }
        else             { syncDir *= -1; if (greenBeam != null) greenBeam.spinSign = syncDir; if (bigBeam != null) bigBeam.spinSign = syncDir; return "SAME WAY ⇉"; }
    }

    // --- beam color-flash (per-renderer MaterialPropertyBlock pop, no shared-mat mutation) ---
    void FlashBeam(SpinningBeamHazard b, Color flash)
    {
        if (b == null) return;
        StartCoroutine(FlashRoutine(b, flash));
    }

    IEnumerator FlashRoutine(SpinningBeamHazard b, Color flash)
    {
        var rends = new List<Renderer>();
        foreach (var rn in b.GetComponentsInChildren<Renderer>(true))
        {
            if (rn.name.IndexOf("Collar", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
            if (rn.name.IndexOf("Axle", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
            rends.Add(rn);
        }
        var mpb = new MaterialPropertyBlock();
        foreach (var rn in rends)
        {
            rn.GetPropertyBlock(mpb);
            mpb.SetColor(ID_BaseColor, flash);
            mpb.SetColor(ID_Color, flash);
            mpb.SetColor(ID_Emission, flash * 1.5f);
            rn.SetPropertyBlock(mpb);
        }
        yield return new WaitForSeconds(0.12f);
        foreach (var rn in rends)
        {
            if (rn == null) continue;
            mpb.Clear();
            rn.SetPropertyBlock(mpb);
        }
    }

    void ResetAndRestart()
    {
        if (timeline != null) { StopCoroutine(timeline); timeline = null; }
        StopAllCoroutines(); // also kill any in-flight flash routines
        if (greenBeam != null)
        {
            greenBeam.degreesPerSecond = greenBaseSpeed;
            greenBeam.spinSign = greenBaseSign;
            greenBeam.transform.localRotation = greenRestRot;
            greenBeam.enabled = false;
        }
        if (bigBeam != null)
        {
            bigBeam.degreesPerSecond = bigBaseSpeed;
            bigBeam.spinSign = bigBaseSign;
            bigBeam.transform.localRotation = bigRestRot;
            bigBeam.enabled = false;
        }
        timeline = StartCoroutine(Run()); // grace, re-enable, fresh beats
    }
}
