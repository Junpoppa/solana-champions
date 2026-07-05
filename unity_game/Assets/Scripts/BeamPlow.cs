using UnityEngine;

/// <summary>
/// For a low sweeping beam (e.g. the Draft 3 green arm): instead of a single knockback that the thin
/// kinematic bar then clips through, this continuously shoves the player along the sweep direction +
/// outward + up while in contact, so they get popped off the bar (and usually off the rim) cleanly.
/// </summary>
public class BeamPlow : MonoBehaviour
{
    public Transform pivot;            // the rotating parent (spins about its local Z)
    public float spinSign = 1f;        // +1 matches Rotator's +speed; flip if push goes the wrong way
    public float tangentialSpeed = 12f;
    public float outwardSpeed = 5f;
    public float upSpeed = 6f;
    public float stun = 0.25f;

    void OnCollisionEnter(Collision c) { Plow(c); }
    void OnCollisionStay(Collision c) { Plow(c); }

    void Plow(Collision c)
    {
        if (!c.collider.CompareTag("Player")) return;
        var cc = c.collider.GetComponent<CharacterControls>();
        if (cc == null) return;

        Transform pv = pivot != null ? pivot : transform.parent;
        Vector3 pivotPos = pv != null ? pv.position : transform.position;
        Vector3 p = c.GetContact(0).point;
        Vector3 r = p - pivotPos; r.y = 0f;
        if (r.sqrMagnitude < 0.01f) { r = transform.position - pivotPos; r.y = 0f; }

        Vector3 outward = r.sqrMagnitude > 0.0001f ? r.normalized : Vector3.right;
        Vector3 axis = (pv != null ? pv.TransformDirection(Vector3.forward) : Vector3.up).normalized; // Rotator spins about local Z
        Vector3 tangential = Vector3.Cross(axis * spinSign, r);
        tangential.y = 0f;
        tangential = tangential.sqrMagnitude > 0.0001f ? tangential.normalized : outward;

        Vector3 vel = tangential * tangentialSpeed + outward * outwardSpeed + Vector3.up * upSpeed;
        cc.HitPlayer(vel, stun);
    }
}
