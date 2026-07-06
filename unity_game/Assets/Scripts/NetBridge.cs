using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

/// <summary>
/// Live multiplayer avatars. Lives on a plain "NetBridge" GameObject in every playable scene (Boot + each
/// mode), mirroring WebBridge. The JS shell:
///   • calls OnMatchInfo(json) right after LoadGameScene (cached in statics → survives the swap),
///   • calls OnSnapshot(json) ~15 Hz with every player's pose.
/// This component spawns a visual-only remote bean per OTHER player, interpolates them, replays real
/// physics ragdolls (RemoteRagdoll) from the synced downed+fling state, and streams the local bean's pose
/// back out via NetSend (→ window.__unityNetSend → server). Only active when WebBridge.Multiplayer.
/// </summary>
public class NetBridge : MonoBehaviour
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void NetSend(string s);
#else
    private static void NetSend(string s) { /* editor no-op */ }
#endif

    // ---- match info (cached statically so the gameplay scene reads it after the Boot→mode swap) ----
    [System.Serializable] private class RosterEntry { public string id; public string nick; public string look; public int spawnIndex; }
    [System.Serializable] private class MatchInfo { public int seed; public double startAtEpochMs; public string matchId; public string myId; public RosterEntry[] roster; }
    // Pose wire: position(x,y,z) + yaw(r) + planar speed(s) + airborne(a) + downed(d) + inSecondJump(j)
    // + fling velocity(fx,fy,fz) streamed while ragdolled so remotes replay the same knockdown.
    [System.Serializable] private class PoseQ { public float x, y, z, r, s, a, d, j, fx, fy, fz; }
    [System.Serializable] private class SnapPlayer { public string id; public PoseQ q; }
    [System.Serializable] private class Snapshot { public SnapPlayer[] players; }
    [System.Serializable] private class DroppedIds { public string[] ids; }

    private static MatchInfo s_info = null;

    // exposed to the mode spawn controllers (Spinner/LMS/RollOut)
    public static bool HasMatch => s_info != null && s_info.roster != null && s_info.roster.Length > 0;
    public static int PlayerCount => HasMatch ? s_info.roster.Length : 1;
    public static int MySpawnIndex
    {
        get
        {
            if (!HasMatch) return 0;
            foreach (var e in s_info.roster) if (e.id == s_info.myId) return e.spawnIndex;
            return 0;
        }
    }

    // Spawn indices of every OTHER player (LmsStartController re-derives each remote's start hex
    // from these + the shared seed, so their floating hexes render on every client).
    public static int[] RemoteSpawnIndices()
    {
        if (!HasMatch) return new int[0];
        var list = new List<int>();
        foreach (var e in s_info.roster) if (e.id != s_info.myId) list.Add(e.spawnIndex);
        return list.ToArray();
    }

    // ---- remote avatars ----
    private class Remote
    {
        public Transform root;
        public Transform model;
        public RemoteBeanAnim anim;
        public RemoteRagdoll rag;
        public Vector3 tPos;
        public float tYaw, tS;
        public bool tA;
        public bool tDowned;
        public bool tJump;
        public Vector3 tFling;
        public bool active;
        public bool wasDowned;
        // look (retry until the remote's "Character mesh" exists — insurance vs a not-yet-realized mesh)
        public string look;
        public bool lookApplied;
        public readonly List<GameObject> lookSpawned = new List<GameObject>();
    }
    private readonly Dictionary<string, Remote> remotes = new Dictionary<string, Remote>();

    // ---- local bean refs ----
    private CharacterControls local;
    private Rigidbody localRb;
    private RagdollController localRag;
    private CapsuleCollider localCapsule;
    private Animator localAnim;
    private Transform localModel; // the BeanModel (animator) transform = visible bean
    private bool spawnedRemotes;
    private float sendAccum;

    // ---- JS → Unity ----
    public void OnMatchInfo(string json)
    {
        try { s_info = JsonUtility.FromJson<MatchInfo>(json); }
        catch (System.Exception e) { Debug.LogWarning("[NetBridge] bad MatchInfo: " + e.Message); }
    }

    // Players who never loaded were dropped from the match by the server at begin time: remove their
    // remote avatars + roster entries (and their LMS start hex) so no frozen ghost stands at a spawn.
    public void OnPlayersDropped(string json)
    {
        DroppedIds d;
        try { d = JsonUtility.FromJson<DroppedIds>(json); }
        catch (System.Exception e) { Debug.LogWarning("[NetBridge] bad PlayersDropped: " + e.Message); return; }
        if (d == null || d.ids == null || s_info == null || s_info.roster == null) return;

        var lms = FindFirstObjectByType<LmsStartController>();
        var keep = new List<RosterEntry>(s_info.roster.Length);
        foreach (var e in s_info.roster)
        {
            bool dropped = System.Array.IndexOf(d.ids, e.id) >= 0;
            if (!dropped) { keep.Add(e); continue; }
            Debug.Log("[NetBridge] dropped player " + (e.nick ?? e.id) + " (spawnIndex " + e.spawnIndex + ")");
            if (remotes.TryGetValue(e.id, out var r))
            {
                if (r.root != null) Destroy(r.root.gameObject);
                remotes.Remove(e.id);
            }
            // If remotes aren't spawned yet, shrinking the roster is enough — TrySpawnRemotes never creates them.
            if (lms != null) lms.RemoveRemoteHex(e.spawnIndex);
        }
        s_info.roster = keep.ToArray();
    }

    public void OnSnapshot(string json)
    {
        Snapshot snap;
        try { snap = JsonUtility.FromJson<Snapshot>(json); }
        catch { return; }
        if (snap == null || snap.players == null) return;
        string myId = s_info != null ? s_info.myId : null;
        foreach (var p in snap.players)
        {
            if (p == null || p.q == null || p.id == myId) continue;
            if (!remotes.TryGetValue(p.id, out var r)) continue;
            r.tPos = new Vector3(p.q.x, p.q.y, p.q.z);
            r.tYaw = p.q.r;
            r.tS = p.q.s;
            r.tA = p.q.a > 0.5f;
            r.tDowned = p.q.d > 0.5f;
            r.tJump = p.q.j > 0.5f;
            r.tFling = new Vector3(p.q.fx, p.q.fy, p.q.fz);
            if (!r.active)
            {
                r.active = true;
                r.root.gameObject.SetActive(true);
                r.root.position = r.tPos;
                r.root.rotation = Quaternion.Euler(0f, r.tYaw, 0f);
            }
        }
    }

    // ---- lifecycle ----
    void Update()
    {
        if (!WebBridge.Multiplayer || !HasMatch) return;
        if (!spawnedRemotes) { TrySpawnRemotes(); return; }

        // stream local pose ~15 Hz
        sendAccum += Time.deltaTime;
        if (local != null && localModel != null && sendAccum >= 1f / 15f)
        {
            sendAccum = 0f;
            Vector3 p = localModel.position;
            float yaw = localModel.eulerAngles.y;
            Vector3 v = localRb != null ? localRb.linearVelocity : Vector3.zero;
            float s = new Vector2(v.x, v.z).magnitude;
            bool air = Mathf.Abs(v.y) > 2f;
            bool downed = local.IsRagdolled;
            bool secondJump = localAnim != null && localAnim.GetBool("InSecondJump");
            Vector3 fling = (downed && localRag != null) ? localRag.LastFlingVel : Vector3.zero;
            NetSend(
                "{\"x\":" + F(p.x) + ",\"y\":" + F(p.y) + ",\"z\":" + F(p.z) +
                ",\"r\":" + F(yaw) + ",\"s\":" + F(s) + ",\"a\":" + (air ? 1 : 0) +
                ",\"d\":" + (downed ? 1 : 0) + ",\"j\":" + (secondJump ? 1 : 0) +
                ",\"fx\":" + F(fling.x) + ",\"fy\":" + F(fling.y) + ",\"fz\":" + F(fling.z) + "}");
        }

        // interpolate + animate remotes
        float k = 1f - Mathf.Exp(-12f * Time.deltaTime);
        foreach (var r in remotes.Values)
        {
            if (r.root == null) continue;
            if (!r.lookApplied) TryApplyRemoteLook(r);
            if (!r.active) continue;

            // downed edge → replay/recover the real ragdoll (physics owns the pose while limp)
            if (r.tDowned && !r.wasDowned)
            {
                r.wasDowned = true;
                if (r.anim != null) r.anim.SetDowned(true);
                if (r.rag != null) r.rag.EnableRagdoll(r.tFling);
            }
            else if (!r.tDowned && r.wasDowned)
            {
                r.wasDowned = false;
                if (r.rag != null) r.rag.Recover();
                if (r.anim != null) r.anim.SetDowned(false);
                r.root.position = r.tPos; // snap the root back to the (recovered) network position
                r.root.rotation = Quaternion.Euler(0f, r.tYaw, 0f);
            }

            if (!r.tDowned)
            {
                r.root.position = Vector3.Lerp(r.root.position, r.tPos, k);
                r.root.rotation = Quaternion.Slerp(r.root.rotation, Quaternion.Euler(0f, r.tYaw, 0f), k);
                if (r.anim != null) { r.anim.SetMotion(r.tS, r.tA); r.anim.SetSecondJump(r.tJump); }
            }
            // while downed: leave the root alone — RemoteRagdoll's physics defines the bean's world pose.
        }
    }

    private static string F(float f) => f.ToString("0.###", CultureInfo.InvariantCulture);

    // Apply the remote's look once its "Character mesh"/"customize_objects" exist (usually the same frame
    // it's instantiated; retried each frame as insurance). Persists lookSpawned so it can be rebuilt.
    private void TryApplyRemoteLook(Remote r)
    {
        if (string.IsNullOrEmpty(r.look)) { r.lookApplied = true; return; } // nothing to apply
        if (r.model == null) return;
        if (FindChild(r.model, "Character mesh") == null || FindChild(r.model, "customize_objects") == null) return;
        WebBridge.ApplyLookToBean(r.model, r.look, r.lookSpawned, null); // no x-ray twins for remotes
        r.lookApplied = true;
    }

    private static Transform FindChild(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var c = FindChild(root.GetChild(i), name);
            if (c != null) return c;
        }
        return null;
    }

    private void TrySpawnRemotes()
    {
        // need the local player present (for its BeanLocomotion controller + pose source)
        local = Object.FindObjectOfType<CharacterControls>();
        if (local == null) return;
        localRb = local.GetComponent<Rigidbody>();
        localRag = local.GetComponent<RagdollController>();
        localCapsule = local.GetComponent<CapsuleCollider>();
        localAnim = local.GetComponentInChildren<Animator>();
        localModel = localAnim != null ? localAnim.transform : local.transform;
        RuntimeAnimatorController controller = localAnim != null ? localAnim.runtimeAnimatorController : null;

        // Ragdoll-capable remote bean (harvested from the player's tuned ragdoll). Falls back to the plain
        // physics-free prefab if the ragdoll variant is missing (remote just won't do the real fling).
        var src = Resources.Load<GameObject>("Prefabs/character_default_ragdoll");
        if (src == null) src = Resources.Load<GameObject>("Prefabs/character_default");
        if (src == null) { Debug.LogWarning("[NetBridge] no remote bean prefab in Resources"); spawnedRemotes = true; return; }

        string myId = s_info.myId;
        foreach (var e in s_info.roster)
        {
            if (e.id == myId || remotes.ContainsKey(e.id)) continue;
            var root = new GameObject("RemoteBean_" + (e.nick ?? e.id));
            // Trigger-only collider tagged "Player": lets a remote step VANISH hexes on this client (via
            // HexTile.OnTriggerEnter) while every overlap hazard (QueryTriggerInteraction.Ignore) + the
            // kill zones (null CharacterControls) safely ignore it. NO CharacterControls.
            root.tag = "Player";
            var rrb = root.AddComponent<Rigidbody>();
            rrb.isKinematic = true;
            rrb.useGravity = false;
            var rcol = root.AddComponent<CapsuleCollider>();
            rcol.isTrigger = true;
            rcol.radius = 0.5f;
            rcol.height = 2f;
            rcol.center = new Vector3(0f, 1f, 0f);

            var model = Object.Instantiate(src, root.transform);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one * 0.8014f;

            var anim = model.GetComponentInChildren<Animator>();
            RemoteBeanAnim ra = null;
            RemoteRagdoll rr = null;
            if (anim != null)
            {
                if (controller != null) anim.runtimeAnimatorController = controller;
                anim.applyRootMotion = false;
                ra = anim.gameObject.AddComponent<RemoteBeanAnim>();
                rr = anim.gameObject.AddComponent<RemoteRagdoll>(); // real physics knockdown replay
            }

            // Remote ragdoll bones are solid only while limp; stop them from shoving the LOCAL player when a
            // nearby remote gets knocked down (the ground still catches them so they tumble + rest normally).
            if (localCapsule != null)
                foreach (var bc in model.GetComponentsInChildren<Collider>(true))
                    if (bc != null) Physics.IgnoreCollision(bc, localCapsule, true);

            root.SetActive(false); // shown on first snapshot (avoids a frame at origin)
            remotes[e.id] = new Remote { root = root.transform, model = model.transform, anim = ra, rag = rr, look = e.look };
        }
        spawnedRemotes = true;
    }
}
