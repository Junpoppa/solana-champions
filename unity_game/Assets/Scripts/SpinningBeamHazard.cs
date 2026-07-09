using UnityEngine;

/// <summary>
/// Reliable spinning-beam hazard. Replaces Rotator + Bounce/BeamPlow on a kinematic rotating beam.
///
/// Why: Rotator spins via transform.Rotate in Update(), so a kinematic bar carries no PhysX
/// velocity and OnCollisionEnter/Stay barely fires + thin/fast bars tunnel between physics steps,
/// letting the player pass straight through. This drives the spin with Rigidbody.MoveRotation in
/// FixedUpdate (PhysX-aware) and detects the player every FixedUpdate with a deterministic
/// Physics.OverlapCapsule over the bar's own collider(s), then launches them via the existing
/// CharacterControls.HitPlayer. 50 Hz sampling is tunnel-proof and needs no collision events.
///
/// Put this on the SPINNING object (it owns the kinematic Rigidbody). Capsule colliders may live on
/// this object or its children; they are switched to triggers so PhysX never shoves/blocks the player.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class SpinningBeamHazard : MonoBehaviour
{
    [Header("Spin (preserves Rotator feel: degPerSec = Rotator.speed * 100)")]
    public float degreesPerSecond = 65f;
    public Vector3 localSpinAxis = new Vector3(0f, 0f, 1f); // local Z, matches Rotator's Space.Self
    public float spinSign = 1f;

    [Header("Knockback")]
    public float tangentialSpeed = 12f;
    public float outwardSpeed = 6f;
    public float upSpeed = 6f;
    public float stun = 0.25f;
    [Tooltip("0 = continuous plow (re-hit every step). >0 = one launch, then immune for this many seconds.")]
    public float cooldown = 0f;
    [Tooltip("Per-beam ragdoll knock multiplier. >1 hits harder (faster/bigger beams). Only used when the player has a RagdollController.")]
    public float ragdollStrength = 1f;
    [Tooltip("Push the player without ragdolling them (brief stun + launch only). Used by the Spinner's violet upper beam.")]
    public bool knockbackOnly = false;

    [Header("Detection")]
    public string playerTag = "Player";
    [Tooltip("Extra padding added to the detection capsule radius.")]
    public float radiusPadding = 0.05f;
    [Tooltip("Force bar colliders to triggers. Off (default) = solid colliders that physically block/push the player.")]
    public bool makeCollidersTriggers = false;

    private Rigidbody rb;
    private CapsuleCollider[] caps;
    private float cooldownTimer;
    private static readonly Collider[] _hits = new Collider[16];

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        RefreshColliders();
    }

    /// <summary>Re-scan child capsule colliders. Call after adding/removing beam arms at runtime
    /// (e.g. the Spinner's full-diameter "extrude" stage) so the new arm is detected.</summary>
    public void RefreshColliders()
    {
        caps = GetComponentsInChildren<CapsuleCollider>(true);
        if (makeCollidersTriggers)
            for (int i = 0; i < caps.Length; i++) caps[i].isTrigger = true;
    }

    void FixedUpdate()
    {
        // 1) PhysX-aware spin (mirrors transform.Rotate(0,0,deg,Space.Self)).
        // spinSign controls direction so flipping it at runtime actually reverses the sweep (and stays
        // consistent with Hit()'s knockback tangent, which also uses spinSign). Default spinSign=1 = unchanged.
        float dir = spinSign < 0f ? -1f : 1f;
        Quaternion delta = Quaternion.AngleAxis(degreesPerSecond * dir * Time.fixedDeltaTime, localSpinAxis.normalized);
        rb.MoveRotation(rb.rotation * delta);

        if (cooldownTimer > 0f) { cooldownTimer -= Time.fixedDeltaTime; return; }
        if (caps == null) return;

        // 2) deterministic overlap detection
        for (int i = 0; i < caps.Length; i++)
        {
            CapsuleCollider cap = caps[i];
            if (cap == null) continue;
            Vector3 p0, p1; float wr;
            WorldCapsule(cap, out p0, out p1, out wr);
            int n = Physics.OverlapCapsuleNonAlloc(p0, p1, wr + radiusPadding, _hits, ~0, QueryTriggerInteraction.Ignore);
            for (int h = 0; h < n; h++)
            {
                Collider col = _hits[h];
                if (col == null || !col.CompareTag(playerTag)) continue;
                // GetComponentInParent so a ragdoll bone collider (also tagged Player while limp) resolves
                // to the player's CharacterControls. Standing/jumping hits resolve via the root capsule as before.
                CharacterControls cc = col.GetComponentInParent<CharacterControls>();
                if (cc == null)
                {
                    // Ownerless (frozen-tab) bean — no CharacterControls, but fling it like a real player.
                    var ob = col.GetComponentInParent<OrphanBean>();
                    if (ob == null) continue;
                    ob.BeamHit(OrphanFlingVel(col.transform.position));
                    cooldownTimer = cooldown;
                    return;
                }
                // Closest point on the BAR to the bean = the strike point. Its HEIGHT tells the ragdoll whether
                // this is an upper-beam (head) or lower-beam (legs) hit so it can tumble accordingly.
                Vector3 hitPoint = cap.ClosestPoint(col.bounds.center);
                Hit(cc, col.transform.position, hitPoint);
                cooldownTimer = cooldown;
                return;
            }
        }
    }

    void Hit(CharacterControls cc, Vector3 playerPos, Vector3 hitPoint)
    {
        Vector3 pivotPos = transform.position;
        Vector3 r = playerPos - pivotPos; r.y = 0f;
        if (r.sqrMagnitude < 0.0001f) r = transform.right;
        Vector3 outward = r.normalized;
        Vector3 axis = transform.TransformDirection(localSpinAxis).normalized; // ~ world up
        Vector3 tangential = Vector3.Cross(axis * spinSign, r); tangential.y = 0f;
        tangential = tangential.sqrMagnitude > 0.0001f ? tangential.normalized : outward;
        Vector3 vel = tangential * tangentialSpeed + outward * outwardSpeed + Vector3.up * upSpeed;
        RagdollController rag = knockbackOnly ? null : cc.GetComponent<RagdollController>();
        if (rag != null && rag.enabled)
            rag.EnableRagdoll(vel, ragdollStrength, hitPoint); // beam touch -> limp + tumble at the strike point
        else
            cc.HitPlayer(vel, stun); // fallback: original launch-knockback
    }

    // Same fling velocity Hit() applies, minus the ragdoll strength/hit-point (an orphan has no
    // CharacterControls/RagdollController — NetBridge's RemoteRagdoll takes the raw velocity).
    Vector3 OrphanFlingVel(Vector3 playerPos)
    {
        Vector3 pivotPos = transform.position;
        Vector3 r = playerPos - pivotPos; r.y = 0f;
        if (r.sqrMagnitude < 0.0001f) r = transform.right;
        Vector3 outward = r.normalized;
        Vector3 axis = transform.TransformDirection(localSpinAxis).normalized;
        Vector3 tangential = Vector3.Cross(axis * spinSign, r); tangential.y = 0f;
        tangential = tangential.sqrMagnitude > 0.0001f ? tangential.normalized : outward;
        return tangential * tangentialSpeed + outward * outwardSpeed + Vector3.up * upSpeed;
    }

    // Standard CapsuleCollider -> world segment endpoints + scaled radius.
    static void WorldCapsule(CapsuleCollider cap, out Vector3 p0, out Vector3 p1, out float worldRadius)
    {
        Transform t = cap.transform;
        Vector3 ls = t.lossyScale;
        Vector3 dir;
        float radScaleA, radScaleB, heightScale;
        if (cap.direction == 0) { dir = Vector3.right;   heightScale = Mathf.Abs(ls.x); radScaleA = Mathf.Abs(ls.y); radScaleB = Mathf.Abs(ls.z); }
        else if (cap.direction == 1) { dir = Vector3.up; heightScale = Mathf.Abs(ls.y); radScaleA = Mathf.Abs(ls.x); radScaleB = Mathf.Abs(ls.z); }
        else { dir = Vector3.forward;                    heightScale = Mathf.Abs(ls.z); radScaleA = Mathf.Abs(ls.x); radScaleB = Mathf.Abs(ls.y); }
        worldRadius = cap.radius * Mathf.Max(radScaleA, radScaleB);
        float half = Mathf.Max(0f, (cap.height * 0.5f * heightScale) - worldRadius);
        Vector3 centerWorld = t.TransformPoint(cap.center);
        Vector3 axisWorld = t.TransformDirection(dir).normalized;
        p0 = centerWorld + axisWorld * half;
        p1 = centerWorld - axisWorld * half;
    }
}
