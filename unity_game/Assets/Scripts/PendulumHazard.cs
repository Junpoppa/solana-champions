using UnityEngine;

/// <summary>
/// Ragdoll-on-hit for a swinging Pendulum ball. Put this on the Ball (the SphereCollider object).
///
/// Mirrors SpinningBeamHazard/CurbWallHazard: a deterministic Physics.OverlapSphere over the ball's
/// own collider every FixedUpdate (a transform-swung collider doesn't fire OnCollisionEnter reliably
/// and thin/fast contacts tunnel), then RagdollController.EnableRagdoll(vel, strength, hitPoint) for
/// the identical limp+tumble. Knockback points along the ball's own swing velocity so the bean is
/// flung the way the ball is moving (across the road, off the open edge once the curb is cut).
///
/// Replaces the pack's legacy Bounce.cs on these balls (remove/disable Bounce so it can't double-fire).
/// </summary>
public class PendulumHazard : MonoBehaviour
{
    [Header("Knockback (beam-style ragdoll)")]
    [Tooltip("Horizontal push speed along the ball's swing direction.")]
    public float pushForce = 11f;
    [Tooltip("Upward launch added on top of the horizontal push.")]
    public float upBias = 5f;
    [Tooltip("Per-ball ragdoll knock multiplier (passed to RagdollController).")]
    public float strength = 1f;
    [Tooltip("Seconds of immunity after a hit so one swing doesn't re-ragdoll every step.")]
    public float cooldown = 0.5f;
    [Tooltip("Extra padding added to the detection sphere radius.")]
    public float radiusPadding = 0.1f;
    public string playerTag = "Player";

    private Collider hazardCol;   // any solid collider (SphereCollider ball, or the hammer head's collider)
    private Vector3 lastPos;
    private Vector3 swingVel;
    private float cdTimer;
    private static readonly Collider[] _hits = new Collider[16];

    void Awake()
    {
        hazardCol = GetComponent<Collider>();
        lastPos = hazardCol != null ? hazardCol.bounds.center : transform.position;
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        if (hazardCol == null) return;

        // use the collider's world bounds so this works for any shape (sphere ball or hammer head),
        // and so swing velocity is tracked at the collider (the head sits offset from its pivot origin)
        Bounds b = hazardCol.bounds;
        Vector3 center = b.center;
        float worldRadius = Mathf.Max(b.extents.x, Mathf.Max(b.extents.y, b.extents.z));

        // track the hazard's own world velocity (it's swung by Pendulum via transform, not physics)
        if (dt > 0f) swingVel = (center - lastPos) / dt;
        lastPos = center;

        if (cdTimer > 0f) { cdTimer -= dt; return; }

        int n = Physics.OverlapSphereNonAlloc(center, worldRadius + radiusPadding, _hits, ~0, QueryTriggerInteraction.Ignore);
        for (int h = 0; h < n; h++)
        {
            Collider col = _hits[h];
            if (col == null || !col.CompareTag(playerTag)) continue;
            CharacterControls cc = col.GetComponentInParent<CharacterControls>();
            if (cc == null) continue;

            // fling along the ball's swing direction (horizontal), with an upward pop
            Vector3 dir = swingVel; dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f)
            {
                dir = col.transform.position - center; dir.y = 0f;       // fallback: away from ball
                if (dir.sqrMagnitude < 0.0001f) dir = transform.forward;
            }
            dir.Normalize();
            Vector3 vel = dir * pushForce + Vector3.up * upBias;
            Vector3 hitPoint = hazardCol.ClosestPoint(col.bounds.center);

            RagdollController rag = cc.GetComponent<RagdollController>();
            if (rag != null && rag.enabled)
                rag.EnableRagdoll(vel, strength, hitPoint);
            else
                cc.HitPlayer(vel, 0.25f);

            cdTimer = cooldown;
            return;
        }
    }
}
