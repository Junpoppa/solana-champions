using UnityEngine;

/// <summary>
/// REAL physics ragdoll for a remote avatar — replays another player's knockdown on THIS client so a
/// beam/hammer hit shows the same directional fling on every screen (Fall-Guys model), not a canned tilt.
///
/// The remote bean prefab (Resources/Prefabs/character_default_ragdoll) carries the same tuned ragdoll
/// bones (Rigidbody + Collider + CharacterJoint) as the local player. This component is a lightweight
/// stand-in for RagdollController (which is coupled to CharacterControls/camera): it just toggles those
/// bones limp/kinematic and applies the network-synced fling. NetBridge drives Enable/Recover from the
/// synced `d` (downed) flag + `fx,fy,fz` (fling velocity).
///
/// While ragdolled, physics owns the bean's world pose — NetBridge stops lerping the root to the snapshot
/// (see NetBridge.Update). On recover the animator takes over and NetBridge re-syncs to the stream.
/// </summary>
public class RemoteRagdoll : MonoBehaviour
{
    private Animator anim;
    private Rigidbody[] boneBodies;
    private Collider[] boneCols;
    private bool ragdolled;

    // tumble tuning (mirrors the feel of RagdollController's off-center hit impulse, without a synced hit point)
    private const float TumbleImpulse = 3.2f; // scaled by fling speed
    private const float TumbleUpBias = 0.35f;

    public bool IsRagdolled => ragdolled;

    void Awake()
    {
        anim = GetComponent<Animator>();
        var bodies = GetComponentsInChildren<Rigidbody>(true);
        boneBodies = bodies;
        var cols = new System.Collections.Generic.List<Collider>();
        foreach (var b in boneBodies)
        {
            if (b == null) continue;
            var c = b.GetComponent<Collider>();
            if (c != null) cols.Add(c);
        }
        boneCols = cols.ToArray();
        SetDynamic(false); // bones start kinematic + colliders OFF so an idle remote never blocks the local player
    }

    /// Fling the bean limp in `flingVel` (world). Idempotent while already down (re-shoves for a fresh hit).
    public void EnableRagdoll(Vector3 flingVel)
    {
        if (boneBodies == null || boneBodies.Length == 0) return;

        if (!ragdolled)
        {
            ragdolled = true;
            if (anim != null) anim.enabled = false; // stop locomotion; physics drives the bones now
            SetDynamic(true);
        }

        // whole-body shove
        for (int i = 0; i < boneBodies.Length; i++)
        {
            if (boneBodies[i] == null) continue;
            boneBodies[i].linearVelocity = flingVel;
            boneBodies[i].angularVelocity = Vector3.zero;
        }

        // off-center kick → the bean pitches/tumbles in the fling direction (no synced hit point, so we
        // derive it from the fling: push the upper body forward-and-up about the hips).
        Vector3 dir = flingVel; dir.y = 0f;
        if (dir.sqrMagnitude > 1e-4f)
        {
            dir = (dir.normalized + Vector3.up * TumbleUpBias).normalized;
            float mag = Mathf.Clamp(flingVel.magnitude, 1.5f, 8f);
            Rigidbody upper = FindUpperBody();
            if (upper != null)
                upper.AddForceAtPosition(dir * (TumbleImpulse * mag), upper.worldCenterOfMass + Vector3.up * 0.4f, ForceMode.Impulse);
        }
    }

    /// The player stood back up → hand the bean back to the animator (NetBridge re-syncs the root next frame).
    public void Recover()
    {
        if (!ragdolled) return;
        ragdolled = false;
        SetDynamic(false);
        if (anim != null) anim.enabled = true;
    }

    /// Fastest-moving bone speed (m/s) — lets a caller detect when a ragdoll has come to rest.
    public float MaxBoneSpeed()
    {
        if (!ragdolled || boneBodies == null) return 0f;
        float max = 0f;
        foreach (var b in boneBodies)
        {
            if (b == null || b.isKinematic) continue;
            float s = b.linearVelocity.magnitude;
            if (s > max) max = s;
        }
        return max;
    }

    /// World position of the hips (heaviest bone) — where the bean actually came to rest, so a get-up
    /// can re-stand it there instead of snapping back to where it fell from.
    public Vector3 HipsPosition()
    {
        Rigidbody hips = null; float bestMass = -1f;
        if (boneBodies != null)
            foreach (var b in boneBodies)
            {
                if (b == null) continue;
                string n = b.name.ToLowerInvariant();
                if (n.Contains("hip") || n.Contains("pelvis")) return b.position;
                if (b.mass > bestMass) { bestMass = b.mass; hips = b; }
            }
        return hips != null ? hips.position : transform.position;
    }

    private Rigidbody FindUpperBody()
    {
        // Prefer a spine/head bone for a natural head-forward tumble; else the heaviest body (hips).
        Rigidbody best = null; float bestMass = -1f;
        foreach (var b in boneBodies)
        {
            if (b == null) continue;
            string n = b.name.ToLowerInvariant();
            if (n.Contains("spine") || n.Contains("head")) return b;
            if (b.mass > bestMass) { bestMass = b.mass; best = b; }
        }
        return best;
    }

    private void SetDynamic(bool dynamic)
    {
        if (boneBodies != null)
            foreach (var b in boneBodies)
            {
                if (b == null) continue;
                b.isKinematic = !dynamic;
                b.interpolation = dynamic ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
                if (dynamic) b.WakeUp();
            }
        if (boneCols != null)
            foreach (var c in boneCols) if (c != null) c.enabled = dynamic;
    }
}
