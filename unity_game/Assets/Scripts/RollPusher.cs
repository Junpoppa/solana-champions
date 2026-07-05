using UnityEngine;

/// <summary>
/// PLOW pusher for Roll Out wall/pole obstacles (children of a rolling band). NOT a knockback —
/// while the bean is pressed against the moving obstacle on its LEADING face, the bean is carried
/// ALONG WITH the obstacle at the obstacle's own speed (like a snowplow). The bean keeps full
/// control: walk sideways out of the way, or hop to another band section, and it slips free;
/// otherwise it rides the wall until swept off the rolling edge. No impulse, no stun, no ragdoll.
///
/// Gates so it only plows when it should:
///   - TIGHT contact: Collider.ClosestPoint within contactSkin (only when actually touching).
///   - DIRECTIONAL: only beans on the leading face the obstacle sweeps toward (Dot(toBean, sweep) > 0);
///     a bean BEHIND the wall (the side it's moving away from) is never carried.
///   - moving: ignored when the obstacle isn't sweeping sideways (e.g. straight overhead).
///
/// The obstacle is moved by its parent band's transform rotation, so its world velocity is read from
/// the collider's bounds-center delta. Same position-carry technique as RollDrum's surface drag.
/// Put this on the obstacle object (the one with the BoxCollider / CapsuleCollider).
/// </summary>
public class RollPusher : MonoBehaviour
{
    [Header("Plow")]
    [Tooltip("Fraction of the obstacle's own sweep speed transferred to the bean. 1 = rides exactly " +
             "with the wall; >1 shoves ahead of it; <1 lets the bean lag/slip.")]
    public float carryStrength = 1f;

    [Tooltip("Wall-only: while pressed on the leading face, kill the bean's upward climb and stop it " +
             "overtaking the obstacle, so it can't run/jump OVER it (poles leave this off).")]
    public bool blockOver = false;

    [Header("Gates")]
    [Tooltip("How close (world units) the bean must be to the obstacle surface to count as touching.")]
    public float contactSkin = 0.6f;
    [Tooltip("Min Dot(toBean, sweepDir): >0 = leading side only. Raise toward 1 for a narrower front.")]
    public float leadingDot = 0.0f;
    [Tooltip("Below this sweep speed (u/s) the obstacle is treated as stationary -> no plow.")]
    public float minSweepSpeed = 0.5f;
    public string playerTag = "Player";

    private Collider hazardCol;
    private Vector3 lastCenter;
    private bool hasLast;
    private static readonly Collider[] _hits = new Collider[16];

    void Awake()
    {
        hazardCol = GetComponent<Collider>();
        if (hazardCol != null) { lastCenter = hazardCol.bounds.center; hasLast = true; }
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        if (hazardCol == null || dt <= 0f) return;

        Vector3 center = hazardCol.bounds.center;
        if (!hasLast) { lastCenter = center; hasLast = true; return; }
        Vector3 sweepVel = (center - lastCenter) / dt;
        lastCenter = center;

        Vector3 sweepHoriz = sweepVel; sweepHoriz.y = 0f;
        float sweepSpeed = sweepHoriz.magnitude;
        if (sweepSpeed < minSweepSpeed) return; // not sweeping sideways -> nothing to plow
        Vector3 sweepDir = sweepHoriz / sweepSpeed;

        Bounds b = hazardCol.bounds;
        int n = Physics.OverlapBoxNonAlloc(b.center, b.extents + Vector3.one * contactSkin, _hits,
                                           Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        for (int h = 0; h < n; h++)
        {
            Collider col = _hits[h];
            if (col == null || !col.CompareTag(playerTag)) continue;
            CharacterControls cc = col.GetComponentInParent<CharacterControls>();
            if (cc == null) continue;
            Rigidbody brb = cc.GetComponent<Rigidbody>();
            if (brb == null || brb.isKinematic) continue; // ragdolling/teleport -> leave alone

            Vector3 beanCenter = col.bounds.center;

            // TIGHT contact: bean must be within the skin of this obstacle's surface.
            Vector3 cp = hazardCol.ClosestPoint(beanCenter);
            if ((beanCenter - cp).sqrMagnitude > contactSkin * contactSkin) continue;

            // DIRECTIONAL: only carry beans on the leading face the obstacle sweeps toward.
            Vector3 toBean = beanCenter - center; toBean.y = 0f;
            if (toBean.sqrMagnitude < 1e-6f || Vector3.Dot(toBean.normalized, sweepDir) <= leadingDot) continue;

            // PLOW: move the bean WITH the obstacle (no impulse, no stun). Player input still applies on top,
            // so walking aside / changing band section lets it slip free.
            brb.position += sweepDir * (sweepSpeed * carryStrength * dt);

            // WALL: stop the bean going OVER it — kill upward climb + don't let it overtake the wall.
            // Sideways / backward motion stays free, so stepping aside still escapes.
            if (blockOver)
            {
                Vector3 v = brb.linearVelocity;
                if (v.y > 0f) v.y = 0f;                                  // no jumping/climbing UP the face
                float along = Vector3.Dot(v, sweepDir);
                if (along > sweepSpeed) v -= sweepDir * (along - sweepSpeed); // no punching past the wall
                brb.linearVelocity = v;
            }
        }
    }
}
