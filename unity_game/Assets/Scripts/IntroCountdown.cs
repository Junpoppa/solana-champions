using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// Shared "3 · 2 · 1 · GO!" match-start intro, driven by an ABSOLUTE wall-clock GO instant:
///  - freezes the bean (CharacterControls.LockControl) for the whole count,
///  - emits each label ("3","2","1","GO","") to the web DOM overlay via the CountdownTick jslib,
///  - on GO unfreezes the bean and fires <see cref="OnGo"/>.
/// Multiplayer: the server fixes goAtEpochMs (one shared instant for the whole room) and JS hands it
/// in via WebBridge.BeginCountdown → <see cref="BeginCountdownAt"/>. Every tick and the unfreeze are
/// re-derived from that instant each Update, so all players hit GO at the same wall-clock moment —
/// and a tab that was hidden (WebGL pauses the loop) catches up INSTANTLY on resume instead of
/// replaying a stale 3·2·1 (remaining &lt;= 0 → straight to GO, never a frozen bean).
/// Mode controllers (LmsStartController, SpinnerDifficultyRamp) subscribe to OnGo as before.
/// In the editor the jslib is compiled out, so ticks just Debug.Log — the freeze/GO logic still runs.
/// </summary>
public class IntroCountdown : MonoBehaviour
{
    [Tooltip("Count starts from this number down to 1, then shows GO.")]
    public int countFrom = 3;
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
    private bool cleared;
    private double goAtLocalMs;        // absolute local-clock (JS Date.now) GO instant
    private double fallbackDeadlineMs; // absolute rescue deadline if beginCountdown never arrives
    private int lastShownDigit = int.MinValue;

    // Wall clock in epoch ms — on WebGL this is backed by JS Date.now(), i.e. exactly the clock the
    // web shell measured its server offset against. Keeps running while the tab is hidden.
    private static double NowMs() => System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    void Start()
    {
        MatchReporter.BeginMatch(); // fresh match: allow one result report
        player = FindFirstObjectByType<CharacterControls>();
        if (player != null) player.LockControl(true); // freeze until the synchronized GO

        // Multiplayer: wait for the server's shared GO instant so every player's 3·2·1·GO (and the
        // LMS drop) fire together. Single-player/editor: start now.
        bool waitForServer = WebBridge.Multiplayer;
#if UNITY_EDITOR
        waitForServer = false;
#endif
        Debug.Log("[IntroCountdown] Start; multiplayer=" + WebBridge.Multiplayer + " waitForServer=" + waitForServer);
        if (waitForServer)
        {
            // The begin message may have raced the scene load (hidden tab / slow swap) — WebBridge
            // caches the GO instant statically, so pick it up here instead of waiting forever.
            if (WebBridge.PendingGoAtMs > 0.0)
            {
                BeginCountdownAt(WebBridge.PendingGoAtMs);
                return;
            }
            NetReady(); // → JS reports ready; server broadcasts beginCountdown → BeginCountdownAt()
            // Safety net: if the synchronized begin never arrives, start locally so the match can NEVER
            // hard-freeze. 15 s > the server's 12 s begin-timeout, so this only fires on genuine failure.
            // Absolute-time based: a hidden tab can't fire it early (Update doesn't run while hidden),
            // and on resume the real begin — if it arrived meanwhile — wins via `begun`.
            fallbackDeadlineMs = NowMs() + 15000.0;
        }
        else BeginCountdown();
    }

    /// <summary>Start the countdown toward an absolute local-clock GO instant (epoch ms).</summary>
    public void BeginCountdownAt(double goAtMs)
    {
        if (begun) return;
        begun = true;
        goAtLocalMs = goAtMs;
        double remaining = goAtLocalMs - NowMs();
        Debug.Log("[IntroCountdown] BeginCountdownAt goAt=" + goAtMs.ToString("F0") + " remaining=" + remaining.ToString("F0") + "ms");
        if (remaining < 0.0) Debug.LogWarning("[IntroCountdown] GO instant already passed — instant GO (late/hidden client catch-up)");
    }

    // Local start (single-player/editor, legacy payload, or fallback): full count from now.
    public void BeginCountdown()
    {
        BeginCountdownAt(NowMs() + countFrom * 1000.0 + 100.0);
    }

    void Update()
    {
        if (!begun)
        {
            if (fallbackDeadlineMs > 0.0 && NowMs() > fallbackDeadlineMs)
            {
                Debug.LogWarning("[IntroCountdown] begin signal never arrived — starting locally (fallback)");
                BeginCountdown();
            }
            return;
        }

        double remaining = goAtLocalMs - NowMs();

        if (!HasFired)
        {
            if (remaining > 0.0)
            {
                // Edge-triggered digits: each fires exactly once, derived from absolute time so every
                // client shows the same digit at the same wall-clock moment.
                int digit = Mathf.Min(countFrom, (int)System.Math.Ceiling(remaining / 1000.0));
                if (digit != lastShownDigit)
                {
                    lastShownDigit = digit;
                    CountdownTick(digit.ToString());
                }
            }
            else
            {
                CountdownTick("GO");
                if (player != null) player.LockControl(false);
                MatchClock.StartNow(); // survival timing starts at GO
                HasFired = true;
                OnGo?.Invoke();
            }
        }
        else if (!cleared && remaining <= -goHold * 1000.0)
        {
            cleared = true;
            CountdownTick(""); // clear the overlay
            enabled = false;   // done — no more work for this component
        }
    }
}
