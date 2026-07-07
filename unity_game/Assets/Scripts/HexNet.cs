using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

/// <summary>
/// LMS hex-state sync for spectators. Every client shares the same baked LastManStanding scene, so a
/// tile's position in a depth-first walk of the HexArena hierarchy is a stable cross-client index.
///   • Players: <see cref="ReportLocalStep"/> fires when the LOCAL bean steps a tile
///     (HexTile.OnCollisionEnter) → jslib HexVanish → server records + relays to watchers.
///   • Spectators: <see cref="ApplyVanished"/> applies the server's tile list — the whole backlog
///     instantly on join, then live relays with the normal vanish animation.
/// </summary>
public static class HexNet
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void HexVanish(int idx);
#else
    private static void HexVanish(int idx) { Debug.Log("[HexNet] local step tile " + idx); }
#endif

    private static List<HexTile> tiles;
    private static Dictionary<HexTile, int> indexOf;
    private static Scene builtFor;

    // Depth-first component order under HexArena — deterministic for the same baked scene, i.e.
    // identical on every client. Rebuilt when the active scene changes.
    private static bool EnsureIndex()
    {
        var scene = SceneManager.GetActiveScene();
        if (tiles != null && builtFor == scene && (tiles.Count == 0 || tiles[0] != null)) return tiles.Count > 0;
        tiles = new List<HexTile>();
        indexOf = new Dictionary<HexTile, int>();
        builtFor = scene;
        var arena = GameObject.Find("HexArena");
        if (arena == null) return false;
        foreach (var t in arena.GetComponentsInChildren<HexTile>(true))
        {
            indexOf[t] = tiles.Count;
            tiles.Add(t);
        }
        return tiles.Count > 0;
    }

    /// <summary>The LOCAL bean stepped this tile — report its index to the server (via JS).</summary>
    public static void ReportLocalStep(HexTile tile)
    {
        if (!WebBridge.Multiplayer || WebBridge.Spectator) return;
        if (WebBridge.Mode != "lastman") return;
        if (!EnsureIndex()) return;
        int idx;
        if (indexOf.TryGetValue(tile, out idx)) HexVanish(idx);
    }

    /// <summary>Vanish these tiles (server hex-state). instant = late-join backlog (no animation).</summary>
    public static void ApplyVanished(int[] idxs, bool instant)
    {
        if (idxs == null || idxs.Length == 0) return;
        if (!EnsureIndex()) return;
        foreach (int idx in idxs)
        {
            if (idx < 0 || idx >= tiles.Count) continue;
            var t = tiles[idx];
            if (t != null) t.NetVanish(instant);
        }
    }
}
