using UnityEngine;

/// <summary>
/// Fall-Guys "Roll Out" drum. A big horizontal cylinder that spins about a world axis and
/// DRAGS any bean standing on its top surface (conveyor), so the player drifts toward the
/// rolling edge and must keep walking to stay on. Adjacent drums alternate spinSign.
///
/// Why a custom carry: the bean uses CharacterControls (impulse movement + manual gravity,
/// velocity overwritten each FixedUpdate). PhysX friction from a rotating MeshCollider does
/// NOT reliably carry such a controller. So we detect standing beans every FixedUpdate
/// (same 50 Hz overlap approach as SpinningBeamHazard) and add the surface's tangential
/// velocity as a small positional drift (rb.position +=), decoupled from the velocity solve
/// so it neither gets damped nor fights the controller. carryStrength dials difficulty.
///
/// Put this on the spinning drum object (owns the kinematic Rigidbody + cylinder MeshCollider).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class RollDrum : MonoBehaviour
{
    [Header("Spin")]
    public float degreesPerSecond = 40f;
    public Vector3 spinWorldAxis = new Vector3(1f, 0f, 0f); // drums lie along world X -> roll about X
    public float spinSign = 1f;                              // +1 / -1, alternated per drum by the builder

    [Header("Drum geometry (must match the built mesh)")]
    public float radius = 6f;
    public float halfLength = 14f;

    [Header("Carry")]
    [Tooltip("0 = no drag, 1 = full surface speed. >1 throws harder.")]
    public float carryStrength = 1f;
    [Tooltip("Lower up-dot bound: below this the bean is treated as off the drum (under the equator).")]
    public float topDot = 0.05f;
    [Tooltip("Tolerance band around the surface radius for 'resting on the drum'.")]
    public float surfaceBand = 1.4f;

    [Header("Fall-off (up-dot d = how high on the curve the bean is)")]
    [Tooltip("d >= holdDot = safe TOP zone (free movement). Below it = strong down-slope slide-off " +
             "with no recovery. RAISE holdDot = stricter (you slide off higher up / sooner).")]
    public float holdDot = 0.72f;
    [Tooltip("Down-slope slide speed (u/s) below the safe zone — high enough to overpower walking back up.")]
    public float slideOffSpeed = 11f;

    [Header("Detection")]
    public string playerTag = "Player";

    private Rigidbody rb;
    private static readonly Collider[] _hits = new Collider[16];

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        Vector3 axis = spinWorldAxis.sqrMagnitude > 0.0001f ? spinWorldAxis.normalized : Vector3.right;

        // 1) PhysX-aware roll about the world axis.
        float angle = degreesPerSecond * spinSign * dt;
        rb.MoveRotation(Quaternion.AngleAxis(angle, axis) * rb.rotation);

        // 2) Carry beans resting on the top surface.
        Vector3 center = transform.position;
        Vector3 halfExtents = new Vector3(
            Mathf.Abs(axis.x) > 0.5f ? halfLength : radius + surfaceBand,
            radius + surfaceBand * 2f,
            Mathf.Abs(axis.z) > 0.5f ? halfLength : radius + surfaceBand);
        int n = Physics.OverlapBoxNonAlloc(center, halfExtents, _hits, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);

        Vector3 omega = axis * (degreesPerSecond * spinSign * Mathf.Deg2Rad);

        for (int i = 0; i < n; i++)
        {
            Collider col = _hits[i];
            if (col == null || !col.CompareTag(playerTag)) continue;
            CharacterControls cc = col.GetComponentInParent<CharacterControls>();
            if (cc == null) continue;
            Rigidbody brb = cc.GetComponent<Rigidbody>();
            if (brb == null || brb.isKinematic) continue; // ragdolling -> root kinematic, leave it alone

            Vector3 beanPos = brb.position;
            float t = Mathf.Clamp(Vector3.Dot(beanPos - center, axis), -halfLength, halfLength);
            Vector3 axisPoint = center + axis * t;
            Vector3 r = beanPos - axisPoint;
            float dist = r.magnitude;
            if (dist < 0.001f) continue;
            Vector3 rn = r / dist;
            float d = Vector3.Dot(rn, Vector3.up);
            if (d < topDot) continue;                                          // under the equator -> off the drum
            if (dist < radius - surfaceBand || dist > radius + surfaceBand * 2f) continue; // not resting on surface

            // roll carry (everywhere we still have contact)
            Vector3 vSurf = Vector3.Cross(omega, r);
            vSurf.y = 0f; // horizontal drag only; ground + gravity handle vertical
            brb.position += vSurf * carryStrength * dt;

            // Below the (strict) safe zone: a strong steepest-descent slide-off, no recovery, no ragdoll.
            if (d < holdDot)
            {
                Vector3 slideDir = Vector3.down - Vector3.Dot(Vector3.down, rn) * rn; // gravity along the surface
                if (slideDir.sqrMagnitude > 1e-6f)
                    brb.position += slideDir.normalized * (slideOffSpeed * dt);
            }
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Vector3 axis = spinWorldAxis.sqrMagnitude > 0.0001f ? spinWorldAxis.normalized : Vector3.right;
        Vector3 a = transform.position + axis * halfLength;
        Vector3 b = transform.position - axis * halfLength;
        Gizmos.DrawLine(a, b);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
