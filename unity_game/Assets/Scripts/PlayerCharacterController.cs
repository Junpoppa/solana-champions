using UnityEngine;

/// <summary>
/// Camera-relative run + jump for the party character, via Unity CharacterController.
/// Faces the movement direction. Exposes movement state for the animator.
/// Uses legacy Input axes (Horizontal/Vertical/Jump) — needs Active Input Handling = Both.
///
/// Race hooks: speedMultiplier (boost/slow zones), ApplyKnockback + Stun (hazards / cards),
/// Respawn (checkpoints / fall recovery).
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerCharacterController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 10f;
    public float turnSpeed = 350f;     // deg/sec to face travel direction
    public float jumpHeight = 1.6f;
    public float gravity = -40f;
    [Tooltip("Extra gravity while descending — keeps the apex short and the fall snappy.")]
    public float fallMultiplier = 1.6f;

    [Tooltip("Camera used for camera-relative movement. Defaults to Camera.main.")]
    public Transform cameraTransform;

    [Tooltip("If false, input is ignored (e.g. before the race start).")]
    public bool controlEnabled = true;

    [Header("Race hooks")]
    [Tooltip("Scales moveSpeed. 1 = normal, >1 = boost, <1 = slow. Set by boost pads / slow zones.")]
    public float speedMultiplier = 1f;
    [Tooltip("How fast a knockback impulse decays (per second). Higher = snappier recovery.")]
    public float knockbackDamping = 6f;

    public bool IsMoving { get; private set; }
    public bool IsGrounded { get; private set; }
    public bool IsStunned => stunTimer > 0f;
    public System.Action OnJump;

    CharacterController cc;
    float vY;
    Vector3 externalVelocity;   // decaying knockback (world space, horizontal)
    float stunTimer;

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
    }

    void Update()
    {
        if (stunTimer > 0f) stunTimer -= Time.deltaTime;
        bool canControl = controlEnabled && stunTimer <= 0f;

        Vector3 move = Vector3.zero;
        if (canControl)
        {
            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");
            Vector3 input = new Vector3(h, 0f, v);
            if (input.sqrMagnitude > 0.01f)
            {
                Vector3 fwd = cameraTransform ? cameraTransform.forward : Vector3.forward; fwd.y = 0f; fwd.Normalize();
                Vector3 right = cameraTransform ? cameraTransform.right : Vector3.right; right.y = 0f; right.Normalize();
                move = (fwd * input.z + right * input.x).normalized;
                Quaternion target = Quaternion.LookRotation(move);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * Time.deltaTime);
            }
        }
        IsMoving = move.sqrMagnitude > 0.01f;

        IsGrounded = cc.isGrounded;
        if (IsGrounded)
        {
            if (vY < 0f) vY = -2f;
            if (canControl && Input.GetButtonDown("Jump"))
            {
                vY = Mathf.Sqrt(jumpHeight * -2f * gravity);
                OnJump?.Invoke();
            }
        }
        float g = (vY < 0f) ? gravity * fallMultiplier : gravity;
        vY += g * Time.deltaTime;

        // Decaying knockback (lets hazards / cards shove the bean around).
        externalVelocity = Vector3.Lerp(externalVelocity, Vector3.zero, knockbackDamping * Time.deltaTime);

        Vector3 velocity = move * (moveSpeed * Mathf.Max(0f, speedMultiplier));
        velocity += externalVelocity;
        velocity.y = vY;
        cc.Move(velocity * Time.deltaTime);
    }

    /// <summary>Shove the player (world-space impulse). Horizontal component knocks back; positive Y pops up.</summary>
    public void ApplyKnockback(Vector3 impulse)
    {
        externalVelocity += new Vector3(impulse.x, 0f, impulse.z);
        if (impulse.y > 0f) vY = Mathf.Max(vY, impulse.y);
    }

    /// <summary>Disable control for a moment (hazard hit). Movement input ignored; physics still applies.</summary>
    public void Stun(float seconds)
    {
        stunTimer = Mathf.Max(stunTimer, seconds);
    }

    /// <summary>Apply an external positional delta this frame (e.g. carried by a rotating platform).
    /// Goes through CharacterController.Move so collisions are respected.</summary>
    public void ExternalMove(Vector3 delta)
    {
        cc.Move(delta);
    }

    /// <summary>Teleport cleanly (CharacterController fights direct transform writes otherwise).</summary>
    public void Respawn(Vector3 position)
    {
        cc.enabled = false;
        transform.position = position;
        cc.enabled = true;
        vY = 0f;
        externalVelocity = Vector3.zero;
        stunTimer = 0f;
        speedMultiplier = 1f;
    }
}
