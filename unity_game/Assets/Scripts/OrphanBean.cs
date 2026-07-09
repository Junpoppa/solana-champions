using UnityEngine;

/// <summary>
/// Marker put on a remote bean whose owner's tab froze (see NetBridge orphan-takeover). The bean is
/// flipped from a visual-only ghost into a real, gravity-driven idle body so the scene's hazards act
/// on it exactly like a motionless player. Hazards gate on CharacterControls (which an orphan lacks),
/// so RollDrum and SpinningBeamHazard look up THIS component instead to carry / fling the ownerless
/// bean. NetBridge owns the get-up + despawn lifecycle; this is just the hazard-facing handle.
/// </summary>
public class OrphanBean : MonoBehaviour
{
    public Rigidbody body;          // root dynamic body — RollDrum drifts this like a standing player
    public CapsuleCollider bodyCol; // root solid collider — disabled while ragdolled so bones own the pose
    public RemoteRagdoll ragdoll;   // child ragdoll — the beam flings this

    public bool Downed => ragdoll != null && ragdoll.IsRagdolled;

    /// A beam sweep hit this ownerless bean: drop its standing body and ragdoll the bones with `fling`.
    /// Idempotent while already down (a second sweep re-shoves). NetBridge stands it back up after it settles.
    public void BeamHit(Vector3 fling)
    {
        if (ragdoll == null) return;
        if (!Downed)
        {
            if (bodyCol != null) bodyCol.enabled = false; // bones take over collision
            if (body != null) body.isKinematic = true;    // stop the standing body fighting the ragdoll
        }
        ragdoll.EnableRagdoll(fling);
    }
}
