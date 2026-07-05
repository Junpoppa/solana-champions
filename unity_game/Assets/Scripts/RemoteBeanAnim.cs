using UnityEngine;

/// <summary>
/// Drives a REMOTE avatar's BeanLocomotion animator from network-synced state (no Rigidbody of its own):
/// locomotion (Speed/WalkMul/Airborne) + the double-jump front-roll (InSecondJump/DoubleJump). Root motion
/// is off so animation never moves the bean — NetBridge positions the root.
///
/// The knocked-down reaction is NO LONGER a canned tilt here: a real physics fling is replayed by
/// RemoteRagdoll (which disables this Animator while limp). SetDowned just gates locomotion writes so we
/// don't fight the ragdoll.
/// </summary>
public class RemoteBeanAnim : MonoBehaviour
{
    private Animator anim;
    private bool downed;
    private bool inSecondJump;

    void Awake()
    {
        anim = GetComponent<Animator>();
        if (anim != null) { anim.applyRootMotion = false; anim.speed = 1f; }
    }

    public void SetMotion(float speed, bool airborne)
    {
        if (anim == null || downed || !anim.enabled) return; // frozen while ragdolled
        anim.SetFloat("Speed", speed);
        anim.SetFloat("WalkMul", speed < 0.3f ? 1f : Mathf.Clamp(speed / 8f, 0f, 1.6f));
        anim.SetBool("Airborne", airborne);
    }

    // Double-jump (front-roll): drive the same animator params the local BeanWalkDriver.TriggerDoubleJump uses.
    // Fire the DoubleJump trigger on the rising edge; hold InSecondJump so the roll clip isn't cut short.
    public void SetSecondJump(bool active)
    {
        if (anim == null || downed || !anim.enabled) return;
        anim.SetBool("InSecondJump", active); // hold the gate first (matches BeanWalkDriver ordering)
        if (active && !inSecondJump)
        {
            anim.SetTrigger("DoubleJump");
            // Force the roll directly: the one-shot DoubleJump trigger loses to the higher-priority
            // AnyState->Jump transition and the 15Hz sampling misses its edge, so drive the state ourselves.
            anim.CrossFadeInFixedTime("SecondJump", 0.08f);
        }
        inSecondJump = active;
    }

    // Gate only — RemoteRagdoll performs the actual physics knockdown + toggles the Animator.
    public void SetDowned(bool d)
    {
        downed = d;
    }
}
