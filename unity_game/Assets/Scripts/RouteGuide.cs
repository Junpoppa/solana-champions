using UnityEngine;

/// <summary>
/// Sequential wayfinding: only the CURRENT target portal is visible/active. Reaching it reveals
/// the next; all others (passed + upcoming) are hidden. The portals themselves are the manually
/// placed checkpoint/finish GameObjects (found via RaceManager). Fires a burst on each reach.
/// </summary>
public class RouteGuide : MonoBehaviour
{
    [Header("Reached burst")]
    public GameObject reachedBurst;

    RaceManager rm;
    GameObject[] targets;   // [0..N-1] checkpoints in order, [N] finish
    int lastReached = 0;
    bool init;

    void Start() { TryInit(); }

    void TryInit()
    {
        rm = RaceManager.Instance;
        if (rm == null) return;
        int n = rm.CheckpointCount;
        targets = new GameObject[n + 1];
        for (int i = 0; i < n; i++) targets[i] = rm.GetCheckpointGO(i);
        targets[n] = rm.FinishGO;
        init = true;
        Apply(0);
    }

    void Update()
    {
        if (!init) { TryInit(); if (!init) return; }

        int reached = rm.Reached;
        if (reached > lastReached)
        {
            if (reachedBurst != null && targets != null && reached - 1 < targets.Length && targets[reached - 1] != null)
                Instantiate(reachedBurst, targets[reached - 1].transform.position + Vector3.up * 1.5f, Quaternion.identity);
            lastReached = reached;
        }
        Apply(reached);
    }

    /// <summary>Show only the active target (index == reached); hide everything else.</summary>
    void Apply(int active)
    {
        if (targets == null) return;
        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] == null) continue;
            bool on = (i == active);
            if (targets[i].activeSelf != on) targets[i].SetActive(on);
        }
    }
}
