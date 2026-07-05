using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// Shared "3 · 2 · 1 · GO!" match-start intro. Unity owns the authoritative timer:
///  - freezes the bean (CharacterControls.LockControl) for the whole count,
///  - emits each label ("3","2","1","GO","") to the web DOM overlay via the CountdownTick jslib,
///  - on GO unfreezes the bean and fires <see cref="OnGo"/>.
/// Mode controllers (LmsStartController, SpinnerDifficultyRamp) subscribe to OnGo to kick off their logic.
/// In the editor the jslib is compiled out, so ticks just Debug.Log — the freeze/GO logic still runs.
/// </summary>
public class IntroCountdown : MonoBehaviour
{
    [Tooltip("Count starts from this number down to 1, then shows GO.")]
    public int countFrom = 3;
    [Tooltip("Seconds each number stays on screen.")]
    public float stepSeconds = 1f;
    [Tooltip("How long GO! stays up before the overlay clears.")]
    public float goHold = 0.6f;

    /// <summary>Fired on GO, the instant the bean is unfrozen.</summary>
    public event System.Action OnGo;
    public bool HasFired { get; private set; }

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void CountdownTick(string s);
    [DllImport("__Internal")] private static extern void NetReady();
#else
    private static void CountdownTick(string s) { Debug.Log("[Countdown] " + (s == "" ? "(clear)" : s)); }
    private static void NetReady() { }
#endif

    private CharacterControls player;
    private bool begun;

    void Start()
    {
        MatchReporter.BeginMatch(); // fresh match: allow one result report
        player = FindFirstObjectByType<CharacterControls>();
        if (player != null) player.LockControl(true); // freeze until the synchronized GO

        // Multiplayer: wait for the server's synchronized "begin" (all clients loaded) before counting,
        // so every player's 3·2·1·GO (and the LMS drop) fire together. Single-player/editor: start now.
        bool waitForServer = WebBridge.Multiplayer;
#if UNITY_EDITOR
        waitForServer = false;
#endif
        Debug.Log("[IntroCountdown] Start; multiplayer=" + WebBridge.Multiplayer + " waitForServer=" + waitForServer);
        if (waitForServer)
        {
            NetReady(); // → JS reports ready; server broadcasts beginCountdown → BeginCountdown()
            StartCoroutine(BeginFallback());
        }
        else BeginCountdown();
    }

    // Safety net: if the server's synchronized "begin" never arrives (any handshake link failed), start
    // the countdown locally so the match can NEVER hard-freeze. 15 s > the server's 12 s begin-timeout,
    // so this only fires on a genuine failure — not while waiting for a slow-loading peer.
    private IEnumerator BeginFallback()
    {
        yield return new WaitForSeconds(15f);
        if (!begun)
        {
            Debug.LogWarning("[IntroCountdown] begin signal never arrived — starting locally (fallback)");
            BeginCountdown();
        }
    }

    // Started by WebBridge.BeginCountdown() on the server's go-ahead (or directly in single-player/editor).
    public void BeginCountdown()
    {
        if (begun) return;
        begun = true;
        Debug.Log("[IntroCountdown] BeginCountdown → running 3·2·1");
        StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        // one frame so mode controllers can park the bean / position things before the first tick
        yield return null;

        for (int n = countFrom; n >= 1; n--)
        {
            CountdownTick(n.ToString());
            yield return new WaitForSeconds(stepSeconds);
        }

        CountdownTick("GO");
        if (player != null) player.LockControl(false);
        MatchClock.StartNow(); // survival timing starts at GO
        HasFired = true;
        OnGo?.Invoke();

        if (goHold > 0f) yield return new WaitForSeconds(goHold);
        CountdownTick(""); // clear the overlay
    }
}
