using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

/// <summary>
/// Reports the local player's match result to the JS shell (multiplayer v1). One report per match:
/// the kill/elimination zones call ReportResult when the player falls. The web side forwards it to the
/// lobby server, which ranks all players and returns standings.
/// Centralizes the WebGL extern so callers don't each need the #if guard.
/// </summary>
public static class MatchReporter
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void MatchResult(string json);
#endif

    private static bool s_reported;

    /// <summary>Call at match start (fresh scene/countdown) so a single result can be reported.</summary>
    public static void BeginMatch() { s_reported = false; }

    public static void ReportResult(string mode, float survivalMs, bool finished)
    {
        if (s_reported) return;
        s_reported = true;
        if (string.IsNullOrEmpty(mode)) mode = CurrentMode();
        long ms = (long)Mathf.Max(0f, survivalMs);
        string json = "{\"mode\":\"" + mode + "\",\"survivalMs\":" + ms +
                      ",\"finished\":" + (finished ? "true" : "false") + "}";
#if UNITY_WEBGL && !UNITY_EDITOR
        MatchResult(json);
#else
        Debug.Log("[MatchReporter] " + json);
#endif
    }

    /// <summary>Mode the JS shell configured, else derived from the active scene name.</summary>
    public static string CurrentMode()
    {
        string m = WebBridge.Mode;
        if (!string.IsNullOrEmpty(m)) return m;
        switch (SceneManager.GetActiveScene().name)
        {
            case "Course": return "spinner";
            case "LastManStanding": return "lastman";
            case "RollOut": return "rollout";
            default: return "spinner";
        }
    }
}
