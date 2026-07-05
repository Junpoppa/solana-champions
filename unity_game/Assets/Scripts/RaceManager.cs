using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central race controller: countdown → racing → finished. Locks player input during the
/// countdown, runs the timer, enforces checkpoint order, handles fall-recovery, and exposes the
/// current/remaining targets for the RouteGuide (beacons + path line). Minimal OnGUI HUD.
/// </summary>
public class RaceManager : MonoBehaviour
{
    public static RaceManager Instance { get; private set; }

    public enum State { Countdown, Racing, Finished }

    [Header("Refs")]
    public PlayerCharacterController player;
    public Transform startPoint;

    [Header("Countdown")]
    public float countdownSeconds = 3f;

    [Header("Fall recovery")]
    [Tooltip("If the player drops below this Y, respawn at the last checkpoint.")]
    public float fallY = -5f;

    [Header("Finish VFX")]
    public GameObject confettiVfx;
    public GameObject fireworkVfx;

    [Header("Auto-configure portals")]
    [Tooltip("On play, find all root objects whose name starts with this prefix, sort by copy number, " +
             "make all but the last into ordered checkpoints and the last into the finish.")]
    public bool autoConfigurePortals = true;
    public string portalNamePrefix = "Portal";
    [Tooltip("Trigger box size in the portal's LOCAL space (multiplied by the portal's scale).")]
    public Vector3 portalTriggerSize = new Vector3(3.2f, 2.2f, 3.2f);
    public Vector3 portalTriggerCenter = new Vector3(0f, -0.6f, 0f);
    public Color finishTint = new Color(1f, 0.84f, 0f, 1f); // gold

    public State CurrentState { get; private set; }
    public float ElapsedTime { get; private set; }
    public int Reached { get; private set; }
    public int CheckpointCount => checkpoints != null ? checkpoints.Length : 0;

    Checkpoint[] checkpoints;   // sorted by index (1-based indices)
    Transform finish;
    Vector3 lastCheckpointPos;
    Quaternion lastCheckpointRot = Quaternion.identity;
    float countdownTimer;
    float hintTimer;
    string hint = "";

    void Awake()
    {
        Instance = this;

        if (autoConfigurePortals) ConfigurePortals();

        var found = Object.FindObjectsByType<Checkpoint>(FindObjectsSortMode.None);
        System.Array.Sort(found, (a, b) => a.index.CompareTo(b.index));
        checkpoints = found;

        var fl = Object.FindFirstObjectByType<FinishLine>();
        if (fl != null) finish = fl.transform;
        if (player == null) player = Object.FindFirstObjectByType<PlayerCharacterController>();
    }

    void Start()
    {
        if (startPoint != null) { lastCheckpointPos = startPoint.position; lastCheckpointRot = startPoint.rotation; }
        else if (player != null) { lastCheckpointPos = player.transform.position; lastCheckpointRot = player.transform.rotation; }

        countdownTimer = countdownSeconds;
        CurrentState = State.Countdown;
        if (player != null) player.controlEnabled = false;
    }

    void Update()
    {
        if (hintTimer > 0f) hintTimer -= Time.deltaTime;

        if (CurrentState == State.Countdown)
        {
            countdownTimer -= Time.deltaTime;
            if (countdownTimer <= 0f)
            {
                CurrentState = State.Racing;
                if (player != null) player.controlEnabled = true;
            }
        }
        else if (CurrentState == State.Racing)
        {
            ElapsedTime += Time.deltaTime;
            if (player != null && player.transform.position.y < fallY) RespawnPlayer();
        }
    }

    // ---- targets (used by RouteGuide) ----
    public Vector3 CurrentTargetPosition
    {
        get
        {
            if (checkpoints != null && Reached < checkpoints.Length && checkpoints[Reached] != null)
                return checkpoints[Reached].transform.position;
            if (finish != null) return finish.position;
            return transform.position;
        }
    }

    /// <summary>Fills buffer with the remaining checkpoint positions (from Reached on) then the finish.</summary>
    public void GetRemainingTargets(List<Vector3> buffer)
    {
        buffer.Clear();
        if (checkpoints != null)
            for (int i = Reached; i < checkpoints.Length; i++)
                if (checkpoints[i] != null) buffer.Add(checkpoints[i].transform.position);
        if (finish != null) buffer.Add(finish.position);
    }

    /// <summary>Beacon position for slot i (0..N-1 = checkpoints, N = finish).</summary>
    public Vector3 GetBeaconPos(int i)
    {
        if (checkpoints != null && i < checkpoints.Length && checkpoints[i] != null) return checkpoints[i].transform.position;
        if (finish != null) return finish.position;
        return transform.position;
    }

    /// <summary>Checkpoint GameObject for slot i (0-based), used by RouteGuide for sequential reveal.</summary>
    public GameObject GetCheckpointGO(int i)
    {
        return (checkpoints != null && i < checkpoints.Length && checkpoints[i] != null) ? checkpoints[i].gameObject : null;
    }

    public GameObject FinishGO => finish != null ? finish.gameObject : null;

    /// <summary>Called by Checkpoint on entry. Advances only if it's the next one in sequence.</summary>
    public bool ReachCheckpoint(int index)
    {
        if (CurrentState != State.Racing || checkpoints == null) return false;
        if (index != Reached + 1) return false;   // out-of-order ignored
        Reached++;
        var cp = checkpoints[Reached - 1];
        lastCheckpointPos = cp.transform.position;
        lastCheckpointRot = cp.transform.rotation;
        return true;
    }

    public void RespawnPlayer()
    {
        if (player == null) return;
        player.Respawn(lastCheckpointPos + Vector3.up * 1.0f);
        player.transform.rotation = lastCheckpointRot;
    }

    /// <summary>Called by FinishLine. Only finishes once every checkpoint has been passed in order.</summary>
    public void TryFinish()
    {
        if (CurrentState == State.Finished) return;
        if (checkpoints != null && Reached < checkpoints.Length)
        {
            hint = "Reach all checkpoints!";
            hintTimer = 2f;
            return;
        }
        Finish();
    }

    void Finish()
    {
        CurrentState = State.Finished;
        if (player != null) player.controlEnabled = false;
        Vector3 at = player != null ? player.transform.position : transform.position;
        if (confettiVfx != null) Instantiate(confettiVfx, at + Vector3.up * 2f, Quaternion.identity);
        if (fireworkVfx != null) Instantiate(fireworkVfx, at + Vector3.up * 6f, Quaternion.identity);
    }

    // ---- auto-configure placed portals into ordered checkpoints + finish ----
    void ConfigurePortals()
    {
        var all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var portals = new List<Transform>();
        foreach (var t in all)
            if (t.parent == null && t.name.StartsWith(portalNamePrefix)) portals.Add(t);
        portals.Sort((a, b) => CopyNum(a.name).CompareTo(CopyNum(b.name)));
        if (portals.Count == 0) return;

        int last = portals.Count - 1;
        for (int i = 0; i < portals.Count; i++)
        {
            var go = portals[i].gameObject;
            go.SetActive(true);

            var box = go.GetComponent<BoxCollider>();
            if (box == null) box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = portalTriggerSize;
            box.center = portalTriggerCenter;

            if (i == last)
            {
                var c = go.GetComponent<Checkpoint>(); if (c != null) Destroy(c);
                if (go.GetComponent<FinishLine>() == null) go.AddComponent<FinishLine>();
                foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var m = ps.main;
                    m.startColor = new ParticleSystem.MinMaxGradient(finishTint);
                }
            }
            else
            {
                var f = go.GetComponent<FinishLine>(); if (f != null) Destroy(f);
                var c = go.GetComponent<Checkpoint>(); if (c == null) c = go.AddComponent<Checkpoint>();
                c.index = i + 1;
            }
        }
    }

    static int CopyNum(string n)
    {
        int op = n.IndexOf('('); if (op < 0) return 0;
        int cp = n.IndexOf(')', op); if (cp < 0) return 0;
        int v; return int.TryParse(n.Substring(op + 1, cp - op - 1).Trim(), out v) ? v : 0;
    }

    // ---- minimal HUD ----
    GUIStyle big, mid;
    void OnGUI()
    {
        if (big == null)
        {
            big = new GUIStyle(GUI.skin.label) { fontSize = 90, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            big.normal.textColor = Color.white;
            mid = new GUIStyle(GUI.skin.label) { fontSize = 30, alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold };
            mid.normal.textColor = Color.white;
        }
        float w = Screen.width, h = Screen.height;
        if (CurrentState == State.Countdown)
        {
            string t = countdownTimer > 0f ? Mathf.Ceil(countdownTimer).ToString() : "GO!";
            GUI.Label(new Rect(0, h * 0.32f, w, 140), t, big);
        }
        else if (CurrentState == State.Racing)
        {
            GUI.Label(new Rect(0, 18, w, 44), FormatTime(ElapsedTime), mid);
            int n = CheckpointCount;
            string cpText = (n > 0 && Reached < n) ? ("Checkpoint " + (Reached + 1) + "/" + n) : "Head to the FINISH!";
            float dist = player != null ? Vector3.Distance(player.transform.position, CurrentTargetPosition) : 0f;
            GUI.Label(new Rect(0, 56, w, 36), cpText + "    " + Mathf.RoundToInt(dist) + "m", mid);
            if (hintTimer > 0f) GUI.Label(new Rect(0, h * 0.5f, w, 40), hint, mid);
        }
        else
        {
            GUI.Label(new Rect(0, h * 0.28f, w, 140), "FINISH!", big);
            GUI.Label(new Rect(0, h * 0.28f + 120, w, 44), FormatTime(ElapsedTime), mid);
        }
    }

    static string FormatTime(float t)
    {
        int m = (int)(t / 60);
        float s = t - m * 60;
        return string.Format("{0:0}:{1:00.00}", m, s);
    }
}
