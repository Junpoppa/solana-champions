using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

/// <summary>
/// Last Man Standing: a trigger volume below the hex arena. When the player falls into it,
/// it's game over → tell the JS shell to return to the lobby (no respawn). Fires once.
/// Only used in LastManStanding.unity; the Spinner course keeps its KillZone respawn.
/// </summary>
public class EliminationZone : MonoBehaviour
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void LmsGameOver();
#endif

    private bool fired;

    void OnTriggerEnter(Collider other)
    {
        if (fired) return;
        // Robust against ragdoll bones (also tagged Player) — walk up to the controller.
        var cc = other.GetComponentInParent<CharacterControls>();
        if (cc == null) return;
        fired = true;
        Debug.Log("[EliminationZone] player fell — game over");
        if (WebBridge.Multiplayer)
        {
            // Multiplayer: report survival time (fall = death, not finished). The web shell reports it
            // to the lobby server, which ranks players and returns standings.
            MatchReporter.ReportResult(MatchReporter.CurrentMode(), MatchClock.ElapsedMs, false);
        }
        else
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            LmsGameOver(); // legacy single-player: straight back to the lobby
#endif
        }
    }
}
