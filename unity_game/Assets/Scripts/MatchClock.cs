using UnityEngine;

/// <summary>
/// Tiny match timer. Started at GO (by IntroCountdown) so every mode measures survival from the same
/// moment. Read by the kill/elimination zones to report how long the local player lasted.
/// Wall-clock based (NOT Time.time, which freezes while a WebGL tab is hidden — a tabbed-away player
/// would otherwise report an undercounted survival). Server-side clamping bounds any abuse.
/// Static so it survives without a scene object; reset each match by StartNow().
/// </summary>
public static class MatchClock
{
    private static double s_startMs = -1.0;

    private static double NowMs() => System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public static void StartNow() { s_startMs = NowMs(); }

    /// <summary>Milliseconds since GO (0 if the match hasn't started).</summary>
    public static float ElapsedMs => s_startMs < 0.0 ? 0f : Mathf.Max(0f, (float)(NowMs() - s_startMs));
}
