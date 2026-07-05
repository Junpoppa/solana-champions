using UnityEngine;

/// <summary>
/// Drives the party character's Animator (char_AC: trigger-based idle/run/jump/fall)
/// from the movement state on PlayerCharacterController. Only fires a trigger on change.
/// </summary>
public class CharacterLocomotion : MonoBehaviour
{
    public Animator animator;
    PlayerCharacterController pcc;
    string current = "";

    void Awake()
    {
        pcc = GetComponent<PlayerCharacterController>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (pcc != null) pcc.OnJump += () => Set("jump");
    }

    void Update()
    {
        if (animator == null || pcc == null) return;
        if (!pcc.IsGrounded)
        {
            Set("jump");   // hold the straight-up jump pose the whole time airborne (no forward-lean fall)
        }
        else Set(pcc.IsMoving ? "run" : "idle");
    }

    void Set(string trigger)
    {
        if (trigger == current) return;
        current = trigger;
        animator.ResetTrigger("idle"); animator.ResetTrigger("run");
        animator.ResetTrigger("jump"); animator.ResetTrigger("fall");
        animator.SetTrigger(trigger);
    }
}
