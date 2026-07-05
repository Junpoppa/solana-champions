using UnityEngine;

/// <summary>
/// A curb wall that TELESCOPES out of the origin curb and back: its back face is pinned at the curb
/// (the clip point) and the wall GROWS along moveDir into the lane, then SHRINKS back in. Because the
/// box never extends behind the curb edge, the bean never sees its back end. Ragdolls the bean on
/// contact exactly like SpinningBeamHazard/PendulumHazard.
///
/// Motion axis (moveDir) is baked at setup time as the road's local "right" at this wall's spot (so it
/// follows the curve). The wall is anchored at <see cref="clipPoint"/> (the origin-curb edge) and its
/// length is animated between minLen and maxLen with the same asymmetric fast-out / slow-in timing as
/// before. Knockback is ragdoll-only (a kinematic SCALE doesn't physically shove — the OverlapBox →
/// RagdollController.EnableRagdoll handles the hit).
/// </summary>
public class CurbWallHazard : MonoBehaviour
{
    [Header("Motion (perpendicular to road)")]
    [Tooltip("Baked slide direction (road perpendicular). Zero = use transform.right.")]
    public Vector3 moveDir = Vector3.zero;
    [Tooltip("How far the wall's FRONT travels out from the curb (drives the out/in timing).")]
    public float moveDistance = 24f;
    [Tooltip("Speed coming OUT (fast).")]
    public float outSpeed = 22f;
    [Tooltip("Speed going back IN (slow).")]
    public float inSpeed = 5.5f;
    [Tooltip("Time offset (sec) so walls are out of sync.")]
    public float phaseOffset = 0f;

    [Header("Telescoping extend (anchored at curb)")]
    [Tooltip("Length (world units along moveDir) when fully RETRACTED — keep small so it tucks into the curb.")]
    public float minLen = 1f;
    [Tooltip("Length (world units along moveDir) when fully EXTENDED. <= 0 = auto (moveDistance + base wall depth).")]
    public float maxLen = 0f;

    [Header("Knockback (beam-style ragdoll)")]
    public float pushForce = 10f;
    public float upBias = 5f;
    [Tooltip("Per-wall ragdoll knock multiplier (passed to RagdollController).")]
    public float strength = 1f;
    public float cooldown = 0.4f;
    public string playerTag = "Player";

    [Header("Emerge-from-curb clip plane (world space)")]
    [Tooltip("Point on the origin-curb edge = the wall's ANCHOR (back face sits here). Also the clip plane.")]
    public Vector3 clipPoint = Vector3.zero;
    [Tooltip("Plane normal (toward the road/destination). Usually == moveDir.")]
    public Vector3 clipNormal = Vector3.zero;

    private Vector3 startPos;
    private Vector3 anchor;            // world back-face point (curb edge)
    private BoxCollider box;
    private Rigidbody rb;
    private float cdTimer;

    private int extendAxis;            // local axis index (0/1/2) aligned with moveDir
    private float worldLenPerLocalUnit; // world length along moveDir per 1.0 of localScale[extendAxis]
    private Vector3 baseLocalScale;

    private static readonly Collider[] _hits = new Collider[16];

    void Awake()
    {
        startPos = transform.position;
        box = GetComponent<BoxCollider>();
        moveDir.y = 0f;
        if (moveDir.sqrMagnitude < 0.0001f) moveDir = transform.right;
        moveDir.y = 0f;
        moveDir.Normalize();

        baseLocalScale = transform.localScale;

        // which LOCAL axis is most aligned with moveDir (so we scale the right component)
        float dR = Mathf.Abs(Vector3.Dot(transform.right, moveDir));
        float dU = Mathf.Abs(Vector3.Dot(transform.up, moveDir));
        float dF = Mathf.Abs(Vector3.Dot(transform.forward, moveDir));
        extendAxis = (dR >= dU && dR >= dF) ? 0 : (dU >= dF ? 1 : 2);

        Vector3 lossy = transform.lossyScale;
        float boxSizeLocal = (box != null) ? box.size[extendAxis] : 1f;
        float baseWorldLen = boxSizeLocal * Mathf.Abs(lossy[extendAxis]);
        float baseScaleAxis = Mathf.Abs(baseLocalScale[extendAxis]);
        worldLenPerLocalUnit = (baseScaleAxis > 1e-5f) ? (baseWorldLen / baseScaleAxis) : 1f;

        // anchor = curb edge (clipPoint). Fallback: current back face along -moveDir.
        anchor = (clipNormal.sqrMagnitude > 0.0001f || clipPoint.sqrMagnitude > 0.0001f)
            ? clipPoint
            : startPos - moveDir * (baseWorldLen * 0.5f);
        anchor.y = startPos.y; // keep height; only extend horizontally

        if (maxLen <= 0f) maxLen = moveDistance + baseWorldLen;

        // kinematic rigidbody for clean interpolated MovePosition of the growing box
        rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        // feed the world-space clip plane (anchor) to the wall material — safety so nothing renders
        // behind the curb even for a frame
        var rend = GetComponent<Renderer>();
        if (rend != null)
        {
            Vector3 cn = clipNormal.sqrMagnitude > 0.0001f ? clipNormal.normalized : moveDir;
            var mpb = new MaterialPropertyBlock();
            rend.GetPropertyBlock(mpb);
            mpb.SetVector("_ClipPoint", anchor);
            mpb.SetVector("_ClipNormal", cn);
            rend.SetPropertyBlock(mpb);
        }
    }

    // 0..1 fraction of extension, fast out / slow in (same timing model as before)
    float CurrentFrac()
    {
        float outDur = moveDistance / Mathf.Max(0.01f, outSpeed);
        float inDur  = moveDistance / Mathf.Max(0.01f, inSpeed);
        float period = outDur + inDur;
        float tc = Mathf.Repeat(Time.time + phaseOffset, period);
        return tc < outDur
            ? (tc / outDur)                 // fast out
            : (1f - (tc - outDur) / inDur); // slow in
    }

    void FixedUpdate()
    {
        float len = Mathf.Lerp(minLen, maxLen, CurrentFrac());

        // grow/shrink along the extend axis only (height & thickness unchanged)
        Vector3 ls = baseLocalScale;
        ls[extendAxis] = len / Mathf.Max(1e-5f, worldLenPerLocalUnit);
        transform.localScale = ls; // sets lossyScale immediately for the overlap below

        // pin the BACK face at the anchor: center sits half a length out from the curb
        Vector3 center = anchor + moveDir * (len * 0.5f);
        if (rb != null) rb.MovePosition(center);

        if (cdTimer > 0f) { cdTimer -= Time.fixedDeltaTime; return; }
        if (box == null) return;

        Vector3 lossy = transform.lossyScale;
        Vector3 half = Vector3.Scale(box.size * 0.5f, new Vector3(Mathf.Abs(lossy.x), Mathf.Abs(lossy.y), Mathf.Abs(lossy.z)));
        int n = Physics.OverlapBoxNonAlloc(center, half, _hits, transform.rotation, ~0, QueryTriggerInteraction.Ignore);
        for (int h = 0; h < n; h++)
        {
            Collider col = _hits[h];
            if (col == null || !col.CompareTag(playerTag)) continue;
            CharacterControls cc = col.GetComponentInParent<CharacterControls>();
            if (cc == null) continue;

            Vector3 outward = col.transform.position - center; outward.y = 0f;
            if (outward.sqrMagnitude < 0.0001f) outward = moveDir;
            outward.Normalize();
            // bias knockback along the wall's travel dir so the bean is driven toward the cut curb
            Vector3 dir = (outward + moveDir).normalized;
            Vector3 vel = dir * pushForce + Vector3.up * upBias;

            Vector3 hitPoint = box.ClosestPoint(col.bounds.center);
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
