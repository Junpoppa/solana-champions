using UnityEngine;

/// <summary>
/// Tiny match timer. Started at GO (by IntroCountdown) so every mode measures survival from the same
/// moment. Read by the kill/elimination zones to report how long the local player lasted.
/// Static so it survives without a scene object; reset each match by StartNow().
/// </summary>
public static class MatchClock
{
    private static float s_startTime = -1f;

    public static void StartNow() { s_startTime = Time.time; }

    /// <summary>Milliseconds since GO (0 if the match hasn't started).</summary>
    public static float ElapsedMs => s_startTime < 0f ? 0f : Mathf.Max(0f, (Time.time - s_startTime) * 1000f);
}
