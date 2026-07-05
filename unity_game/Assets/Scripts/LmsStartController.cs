using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Last Man Standing drop-in start. Spawns a single disappearing "start hex" a couple of hex-layers
/// above a RANDOM tile of the top layer (Layer_0), parks the bean on it during the 3·2·1 countdown,
/// then vanishes it on GO so the bean free-falls onto the top layer and normal Hex-A-Gone play begins.
/// Pairs with IntroCountdown (subscribes to its OnGo event).
/// </summary>
public class LmsStartController : MonoBehaviour
{
    [Tooltip("Height above the top layer to spawn the start hex (~2 hex-layers).")]
    public float dropHeight = 30f;
    [Tooltip("Clearance above the start-hex surface so the bean capsule rests on it.")]
    public float standLift = 1.2f;
    [Tooltip("HexArena root object name.")]
    public string arenaName = "HexArena";
    [Tooltip("Top layer child name under the arena.")]
    public string topLayerName = "Layer_0";

    private GameObject startHex;
    private readonly List<GameObject> remoteHexes = new List<GameObject>();

    void Start()
    {
        var arena = GameObject.Find(arenaName);
        Transform topLayer = arena ? arena.transform.Find(topLayerName) : null;
        if (topLayer == null || topLayer.childCount == 0)
        {
            Debug.LogWarning("[LmsStart] " + arenaName + "/" + topLayerName + " not found; skipping drop-in start.");
            return;
        }

        // Pick the tile we drop onto. Multiplayer: a deterministic shuffle (all clients share
        // WebBridge.Seed) gives each player a DISTINCT start hex; single-player = random.
        Transform tile;
        if (WebBridge.Multiplayer && NetBridge.HasMatch)
        {
            Random.InitState(WebBridge.Seed);
            int ti = MultiplayerSpawns.LmsTileIndex(NetBridge.MySpawnIndex, topLayer.childCount);
            tile = topLayer.GetChild(ti);
        }
        else
        {
            tile = topLayer.GetChild(Random.Range(0, topLayer.childCount));
        }
        Vector3 tileWpos = tile.position;
        Vector3 startPos = new Vector3(tileWpos.x, tileWpos.y + dropHeight, tileWpos.z);

        // Clone the chosen tile for the start hex (same mesh/material/collider), but strip its HexTile so
        // it does NOT vanish on player-contact — WE control when it disappears (on GO).
        startHex = CloneStartHex(tile, startPos, "StartHex", true);

        // Multiplayer: also build every REMOTE player's start hex. Each client derives the identical
        // tile the remote's own client picked: same shared seed + same shuffle + that player's
        // spawnIndex (re-seed before EVERY call — LmsTileIndex consumes the random stream).
        if (WebBridge.Multiplayer && NetBridge.HasMatch)
        {
            foreach (int idx in NetBridge.RemoteSpawnIndices())
            {
                Random.InitState(WebBridge.Seed);
                int rti = MultiplayerSpawns.LmsTileIndex(idx, topLayer.childCount);
                Transform rTile = topLayer.GetChild(rti);
                Vector3 rPos = new Vector3(rTile.position.x, rTile.position.y + dropHeight, rTile.position.z);
                // collider OFF: remote beans are snapshot-driven (their own client does the standing);
                // a solid floating hex could snag the local bean mid-match if flung through its spot.
                remoteHexes.Add(CloneStartHex(rTile, rPos, "StartHex_Remote_" + idx, false));
            }
        }

        // Park the bean on top of the start hex (frozen by IntroCountdown until GO).
        var player = FindFirstObjectByType<CharacterControls>();
        if (player != null)
        {
            Vector3 standPos = startPos + Vector3.up * standLift;
            player.transform.position = standPos;
            player.checkPoint = standPos;
            var rb = player.GetComponent<Rigidbody>();
            if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        }

        var countdown = FindFirstObjectByType<IntroCountdown>();
        if (countdown != null) countdown.OnGo += Vanish;
    }

    GameObject CloneStartHex(Transform tile, Vector3 pos, string name, bool solid)
    {
        var hex = Instantiate(tile.gameObject, pos, tile.rotation);
        hex.name = name;
        hex.transform.SetParent(null, true); // root object, doesn't ride a layer
        var ht = hex.GetComponent<HexTile>();
        if (ht != null) Destroy(ht);
        var col = hex.GetComponent<Collider>();
        if (col != null) col.enabled = solid;
        return hex;
    }

    void Vanish()
    {
        // GO is server-synchronized, so hiding the remote hexes here matches the moment each remote's
        // own client drops their bean.
        HideHex(startHex);
        startHex = null;
        foreach (var h in remoteHexes) HideHex(h);
        remoteHexes.Clear();
    }

    static void HideHex(GameObject hex)
    {
        if (hex == null) return;
        var col = hex.GetComponent<Collider>();
        if (col != null) col.enabled = false;
        var rend = hex.GetComponent<Renderer>();
        if (rend != null) rend.enabled = false;
        Destroy(hex, 0.5f);
    }
}
