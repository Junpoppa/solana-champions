using UnityEngine;

/// <summary>
/// Cosmetic walk animation driver for the bean model. Movement stays 100% driven by the player's
/// CharacterControls/Rigidbody — this NEVER moves the character (applyRootMotion forced off). It only
/// scales Animator playback speed by the player's planar velocity, so the walk cadence matches the
/// actual move speed and freezes when standing still. Single "walk" clip only.
/// Put on the bean model object (the one with the Animator). Reads the Rigidbody from a parent.
/// </summary>
[RequireComponent(typeof(Animator))]
public class BeanWalkDriver : MonoBehaviour
{
    [Tooltip("Planar speed at which the walk clip plays at 1x. Lower = legs move faster for the same speed.")]
    public float walkSpeedRef = 8f;
    [Tooltip("Max playback multiplier so fast knockbacks don't spin the legs absurdly.")]
    public float maxSpeedMul = 1.6f;
    [Tooltip("Below this planar speed the walk freezes (treated as idle/standing).")]
    public float idleThreshold = 0.3f;
    [Tooltip("Down-ray length from the player center for the grounded check (~capsule half-height + margin).")]
    public float groundCheckDist = 1.15f;
    [Tooltip("Last Man Standing only: stay 'grounded' this long after losing ground contact, so running " +
             "over the gappy, slightly-dipping hex tiles doesn't flicker the jump (hands-up) pose. Cosmetic.")]
    public float coyoteTime = 0.15f;

    private Animator anim;
    private Rigidbody body;
    private CapsuleCollider capsule;
    private bool smoothGrounding;       // true only in the LastManStanding (hex) scene
    private float groundProbeRadius = 0.3f;
    private float lastGroundedTime = -999f;
    private float secondJumpStartTime = -999f; // when TriggerDoubleJump last fired
    [Tooltip("Hold InSecondJump true this long after a second jump so the SecondJump roll isn't cut by the " +
             "brief not-yet-airborne launch transient (must outlast coyoteTime). Cosmetic.")]
    public float secondJumpHold = 0.2f;

    [Header("SFX (loaded from Assets/Audio/Resources)")]
    [Tooltip("Base jump sound volume (every ground jump AND double jump). Scaled by WebBridge.SfxVolume.")]
    public float jumpVolume = 1.0f;
    private AudioSource jumpSfx;  // 2D one-shot source for jumps
    private AudioClip jumpClip;

    void Awake()
    {
        anim = GetComponent<Animator>();
        if (anim != null) anim.applyRootMotion = false; // never let animation move the character
        body = GetComponentInParent<Rigidbody>();
        capsule = GetComponentInParent<CapsuleCollider>();
        if (capsule != null) groundProbeRadius = Mathf.Max(0.15f, capsule.radius * 0.9f);
        // Smoothed grounding for the hex arena (LMS) AND the Roll Out log — both have a surface a single
        // center ray slips off for a frame (hex seams / the curved rolling top), flickering the hands-up
        // Jump pose. Course/Spinner keeps its exact original single-ray behavior. The object's scene = mode.
        string sn = gameObject.scene.name;
        smoothGrounding = sn == "LastManStanding" || sn == "RollOut";

        // --- jump SFX: a 2D one-shot AudioSource on the bean (footsteps were removed — felt weird) ---
        jumpClip = Resources.Load<AudioClip>("jumping");
        jumpSfx = gameObject.AddComponent<AudioSource>();
        jumpSfx.playOnAwake = false;
        jumpSfx.spatialBlend = 0f; // 2D — always audible regardless of distance to the camera's AudioListener
        jumpSfx.loop = false;
    }

    void Update()
    {
        if (anim == null) return;
        anim.speed = 1f; // cadence handled via WalkMul on the Walk state
        float planar = 0f;
        if (body != null)
        {
            Vector3 v = body.linearVelocity; v.y = 0f;
            planar = v.magnitude;
        }
        anim.SetFloat("Speed", planar); // Idle<->Walk blend
        float mul = planar < idleThreshold
            ? 1f
            : Mathf.Clamp(planar / Mathf.Max(0.01f, walkSpeedRef), 0f, maxSpeedMul);
        anim.SetFloat("WalkMul", mul); // run cadence matches move speed

        bool airborne;
        if (smoothGrounding)
        {
            // Hex arena: robust multi-point probe + coyote time. The single center ray used to slip
            // through inter-hex seams / over freshly-dipped tiles for a frame, flickering Airborne and
            // snapping the bean into the hands-up Jump pose. Only a SUSTAINED loss of ground (a real
            // fall off the arena) now reads as airborne.
            if (GroundedMultiRay()) lastGroundedTime = Time.time;
            airborne = (Time.time - lastGroundedTime) > coyoteTime;
        }
        else
        {
            // Original behavior (Course / Spinner) — unchanged: single center ray.
            bool grounded = body != null && Physics.Raycast(body.position, Vector3.down, groundCheckDist);
            airborne = !grounded;
        }
        anim.SetBool("Airborne", airborne);
        // Once we touch ground again the second jump is over — clear the gate so the next ground jump can
        // re-enter the normal Jump state. (Harmless no-op in scenes without double jump.) The hold window
        // keeps InSecondJump true through the brief not-yet-airborne launch transient right after an instant
        // double-tap, so the SecondJump roll isn't cut to Idle before it shows.
        if (!airborne && Time.time - secondJumpStartTime > secondJumpHold) anim.SetBool("InSecondJump", false);
    }

    // Play the jump sound. Called for EVERY jump: ground jumps (from CharacterControls) and the air/double
    // jump (from TriggerDoubleJump below). Uses a dedicated one-shot source so it never cuts the footstep loop.
    public void PlayJumpSfx()
    {
        if (jumpSfx != null && jumpClip != null) jumpSfx.PlayOneShot(jumpClip, jumpVolume * WebBridge.SfxVolume);
    }

    // Fired by CharacterControls when the player triggers a mid-air (second) jump in Roll Out. Plays the
    // baked SecondJump_Bean clip via the SecondJump state. Cosmetic only — the lift is physics-driven.
    public void TriggerDoubleJump()
    {
        if (anim == null) return;
        anim.SetBool("InSecondJump", true); // block AnyState->Jump from overriding the second-jump anim
        anim.SetTrigger("DoubleJump");
        secondJumpStartTime = Time.time;    // start the hold window (see Update's InSecondJump clear)
        PlayJumpSfx();                      // double jump sounds too
    }

    // Grounded if a down-ray from the body center OR any of 4 capsule-radius offsets hits within range.
    // A seam under the center is covered by an offset ray landing on the neighbouring tile.
    private bool GroundedMultiRay()
    {
        if (body == null) return true;
        float dist = groundCheckDist + 0.2f; // tolerate the 0.12 tile dip + small inter-tile drops
        Vector3 c = body.position;
        float r = groundProbeRadius;
        if (Physics.Raycast(c, Vector3.down, dist)) return true;
        if (Physics.Raycast(c + new Vector3(r, 0f, 0f), Vector3.down, dist)) return true;
        if (Physics.Raycast(c + new Vector3(-r, 0f, 0f), Vector3.down, dist)) return true;
        if (Physics.Raycast(c + new Vector3(0f, 0f, r), Vector3.down, dist)) return true;
        if (Physics.Raycast(c + new Vector3(0f, 0f, -r), Vector3.down, dist)) return true;
        return false;
    }
}
