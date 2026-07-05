using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Physics ragdoll toggle for the player bean. Lives on the Player root (next to CharacterControls).
///
/// Animated state: bone ragdoll kinematic + colliders disabled, Animator/BeanWalkDriver drive the pose.
/// Beam hit (EnableRagdoll): Animator off, root Rigidbody kinematic + root capsule disabled, bones go
/// dynamic with a shaped/clamped knockback so the bean is knocked DOWN + a bit back and flops loosely.
///
/// While limp the bone colliders are tagged "Player" so the beam can keep DETECTING + RE-SHOVING the body
/// (Reshove) and KillZone still catches it — the root capsule (the normal trigger) is disabled during
/// ragdoll, so the standing/jumping hit path is unchanged.
///
/// Recovery is GROUNDED-gated: the bean only gets up once it has settled on the platform (low speed + ground
/// under the hips) after a minimum limp time. Thrown off / mid-air → stays ragdolled and falls → KillZone
/// respawn. On get-up it plays the matching standup clip (face-down vs back-down) and locks control until the
/// clip finishes. A fresh beam hit cancels an in-progress standup and re-ragdolls.
///
/// Self-wiring (Awake discovers everything) — just AddComponent it to the Player.
/// </summary>
[RequireComponent(typeof(CharacterControls))]
[RequireComponent(typeof(Rigidbody))]
public class RagdollController : MonoBehaviour
{
    [Header("Knockback feel (keep it loose/floppy, just don't fly away)")]
    [Tooltip("Multiplier on the beam's push. Lower = knocked down + a bit back instead of launched.")]
    public float knockbackScale = 0.3f;
    [Tooltip("Hard cap (m/s) on the launch speed so even a fast beam can't fling the bean off the map.")]
    public float maxLaunchSpeed = 4.0f;
    [Tooltip("Scales the beam's upward pop. Low = the bean drops instead of sailing.")]
    public float upMultiplier = 0.25f;
    [Tooltip("Re-shove (beam keeps hitting a limp bean): stronger than the initial knock so it's driven off the rim instead of slipping under.")]
    public float reshoveScale = 0.6f;
    public float reshoveMaxSpeed = 8.0f;

    [Header("Hit tumble (floppy in the direction struck) + jitter (all live-tunable)")]
    [Tooltip("Off-center impulse applied AT the beam contact point on the nearest bone, so a LOW hit sweeps the legs (bean pitches head-first) and a HIGH hit flings the head back. This is what makes it tumble instead of sliding like a rigid block. 0 = off.")]
    public float hitImpulse = 4f;
    [Tooltip("Small upward bias mixed into the hit impulse so the tumble lifts a touch (keep low for a subtle, non-flying feel).")]
    public float hitImpulseUp = 0.2f;
    [Tooltip("Caps how fast overlapping bones un-penetrate. Low = gentle, no exploding-into-jitter (the #1 vibration cause).")]
    public float ragdollMaxDepenetration = 3f;
    [Tooltip("Angular damping on limp bones — settles micro-vibration without killing the flop.")]
    public float ragdollAngularDamping = 0.12f;
    [Tooltip("Linear damping on limp bones — tiny, just to settle.")]
    public float ragdollLinearDamping = 0.06f;
    [Tooltip("Camera smooth time while limp: the cam follows a SMOOTHED proxy of the hips so bone jitter doesn't shake the view (the 'feels laggy' part). Higher = smoother/laggier.")]
    public float camSmoothTime = 0.08f;

    [Header("Standup / recovery")]
    [Tooltip("Spinner/Course only: recover faster after a beam hit. Halves the limp + bone-reset times and plays " +
             "the standup clip at this speed multiplier (1 = normal). Other scenes are unaffected.")]
    public float courseGetUpSpeed = 1.8f;
    [Tooltip("Minimum seconds the bean stays limp before it's allowed to get up (so it flops first).")]
    public float minLimpTime = 0.6f;
    [Tooltip("Hips must be slower than this (m/s) to count as 'settled'.")]
    public float restSpeedThreshold = 0.9f;
    [Tooltip("Ground-probe length below the hips. Ground within this = on the platform (can stand up).")]
    public float groundProbe = 0.7f;
    [Tooltip("Standup clip lengths (s) — control returns after this. Set to the imported clip durations.")]
    public float standFaceDuration = 4.0f;
    public float standBackDuration = 2.9f;
    [Tooltip("Animator triggers for the two standup clips.")]
    public string standFaceTrigger = "StandFace";
    public string standBackTrigger = "StandBack";
    [Tooltip("Animator STATE names (played directly for deterministic get-up).")]
    public string standFaceState = "StandUpFace";
    public string standBackState = "StandUpBack";
    public string idleState = "Idle";
    [Tooltip("Orientation calibration: if dot(chest.up, worldUp) < 0 means face-down. Flip if it's reversed.")]
    public bool faceDownWhenDotNegative = true;
    [Tooltip("Temporary: log get-up diagnostics to the console so we can pinpoint why a clip isn't showing.")]
    public bool debugStandup = false;

    [Tooltip("Grows the bone colliders so the chunky mesh rests ON the deck instead of sinking through it (the floor-clip / XRay outline). Raise if it still clips, lower if the bean looks like it floats.")]
    public float colliderInflate = 1.5f;
    [Tooltip("Friction on the limp ragdoll bones. Low = the spinning bar SWEEPS the bean smoothly instead of pinning+crushing it (the old shake). frictionCombine=Minimum so it stays slippery vs the deck too.")]
    public float ragdollFriction = 0.2f;
    [Tooltip("Escape hatch: if a beam-swept ragdoll STILL shakes, enable this to make the bones IGNORE the beam bars entirely (perfectly smooth, but the bar visibly passes through the fallen bean). Re-enter Play to apply.")]
    public bool beamPassThrough = false;

    [Header("Placement")]
    [Tooltip("Fallback root height above settled hips when no ground is found on get-up.")]
    public float standOffset = 0.6f;
    [Tooltip("Root capsule half-height — root is placed this far above the ground on get-up so feet land.")]
    public float capsuleHalfHeight = 1.0f;
    [Tooltip("Lift applied so the lowest bone sits just above the deck during the get-up (avoids sinking).")]
    public float groundSnapEpsilon = 0.05f;
    [Tooltip("Ankle-pivot-to-sole height: the get-up grounds the lower FOOT bone this far above the deck so the soles rest on it. Raise if the bean floats, lower if the feet clip through.")]
    public float soleOffset = 0.15f;
    [Header("Get-up bone reset (video 'reset bones' technique)")]
    [Tooltip("Seconds to lerp the landed ragdoll heap into the standup clip's first frame before the clip plays.")]
    public float timeToResetBones = 0.5f;
    [Tooltip("Clip ASSET names of the baked standup clips (NOT the animator state names) — sampled at frame 0 as the reset target.")]
    public string standFaceClipName = "StandUpFace_Bean";
    public string standBackClipName = "StandUpBack_Bean";
    [Tooltip("Local axis of the Hips bone that points toward the FEET (Mixamo rig = -up = (0,-1,0)). Flattened to the horizontal, this becomes the get-up facing direction. Calibrate per rig.")]
    public Vector3 hipsFacingAxis = new Vector3(0f, -1f, 0f);
    [Tooltip("If the bean ends up facing backwards when it gets up from the face-down side, flip the facing for that case.")]
    public bool flipFacingWhenFaceDown = false;

    [Header("End-settle (kills the get-up 'snap')")]
    [Tooltip("Over the LAST fraction of the standup clip the feet have stopped swinging (measured stable from ~70%), so we FULLY plant the soles on the deck every frame — no lift-off (levitate) and the hand-off needs no root teleport (the snap). 0.25 = last 25%.")]
    public float endSettleFraction = 0.25f;

    private CharacterControls controls;
    private Rigidbody rootRb;
    private CapsuleCollider rootCol;
    private Animator animator;
    private BeanWalkDriver walkDriver;
    private Transform hips;
    private Transform chest;
    private Transform footL, footR;   // for grounding the soles on the deck during the get-up
    private Rigidbody hipsBody;
    private CameraManager cam;
    private Transform camProxy;     // smoothed camera follow target during ragdoll (decouples cam from bone jitter)
    private Vector3 camProxyVel;

    private Rigidbody[] boneBodies = new Rigidbody[0];
    private Collider[] boneCols = new Collider[0];

    private bool ragdolled;
    private bool standingUp;
    private float timer;
    private Coroutine standRoutine;
    private bool fastGetUp;   // true in Course/Spinner — quicker recovery after a beam hit

    private Transform[] skeleton = new Transform[0];     // all bones the clips animate (Hips + beneath)
    private BoneTransform[] ragdollBones = new BoneTransform[0];     // landed heap snapshot (local pos+rot)
    private BoneTransform[] standBonesFace = new BoneTransform[0];   // face-down clip frame 0 (local)
    private BoneTransform[] standBonesBack = new BoneTransform[0];   // back-down clip frame 0 (local)
    private BoneTransform[] resetTarget = new BoneTransform[0];      // chosen target for the active get-up
    private bool faceTargetReady, backTargetReady;   // false if a clip wasn't found -> skip reset, snap into clip
    private bool resettingBones;
    private float resetT;
    private string curStandState;
    private float curStandDur;
    private float curStandNorm;   // live normalized time of the active standup clip (drives the end-settle)
    private float standFaceLen, standBackLen;   // ACTUAL clip lengths (read in Awake) — drives the hand-off
    private Vector2 landedHipsXZ;     // world XZ where the bean came to rest (anchor so the clip starts there)
    private SkinnedMeshRenderer smr;  // for the size diagnostic (debugStandup)

    [System.Serializable]
    private class BoneTransform { public Vector3 position; public Quaternion rotation; }

    public bool IsRagdolled { get { return ragdolled; } }

    // Multiplayer: the whole-body shove applied to the bones on the most recent hit (ShapeKnock result).
    // NetBridge streams this while ragdolled so each remote client can replay the same directional fling.
    public Vector3 LastFlingVel { get; private set; }

    void Awake()
    {
        controls = GetComponent<CharacterControls>();
        rootRb = GetComponent<Rigidbody>();
        rootCol = GetComponent<CapsuleCollider>();
        animator = GetComponentInChildren<Animator>(true);
        walkDriver = GetComponentInChildren<BeanWalkDriver>(true);
        smr = GetComponentInChildren<SkinnedMeshRenderer>(true);
        cam = Object.FindAnyObjectByType<CameraManager>();

        // Spinner/Course: recover faster after a beam hit — shorter limp + bone-reset (clip is sped up in BeginStandClip).
        fastGetUp = gameObject.scene.name == "Course";
        if (fastGetUp) { minLimpTime *= 0.5f; timeToResetBones *= 0.5f; }
        camProxy = new GameObject("RagdollCamTarget").transform; // standalone (NOT under the bean) so it never feeds back into the bones

        hips = transform.Find("BeanModel/Armature/Hips");
        if (hips == null)
        {
            Debug.LogWarning("[Ragdoll] Hips not found at BeanModel/Armature/Hips — ragdoll disabled.");
            enabled = false;
            return;
        }
        chest = hips.Find("Spine/Spine1");
        if (chest == null) chest = hips;
        hipsBody = hips.GetComponent<Rigidbody>();

        // All bones the clips animate (Hips + everything beneath) — used to snapshot the landed pose and
        // blend it into the get-up clip.
        skeleton = hips.GetComponentsInChildren<Transform>(true);
        ragdollBones = NewBoneArray(skeleton.Length);
        standBonesFace = NewBoneArray(skeleton.Length);
        standBonesBack = NewBoneArray(skeleton.Length);

        // Foot bones for grounding the soles on the deck during the get-up (they have no Rigidbody, so they're
        // not in boneBodies — look them up by name).
        for (int i = 0; i < skeleton.Length; i++)
        {
            if (skeleton[i] == null) continue;
            if (footL == null && skeleton[i].name == "LeftFoot") footL = skeleton[i];
            if (footR == null && skeleton[i].name == "RightFoot") footR = skeleton[i];
        }

        Rigidbody[] all = hips.GetComponentsInChildren<Rigidbody>(true);
        List<Rigidbody> bodies = new List<Rigidbody>();
        List<Collider> cols = new List<Collider>();
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] == null) continue;
            bodies.Add(all[i]);
            all[i].gameObject.tag = "Player"; // so the beam + KillZone can see the limp body
            Collider c = all[i].GetComponent<Collider>();
            if (c != null) cols.Add(c);
        }
        boneBodies = bodies.ToArray();
        boneCols = cols.ToArray();

        if (boneBodies.Length == 0)
        {
            Debug.LogWarning("[Ragdoll] No bone Rigidbodies under Hips — run Tools/Bean/Build Ragdoll first.");
            enabled = false;
            return;
        }

        // Grow colliders a touch so the chunky mesh rests on the deck instead of clipping through it.
        if (colliderInflate > 1f)
        {
            for (int i = 0; i < boneCols.Length; i++)
            {
                CapsuleCollider cap = boneCols[i] as CapsuleCollider;
                if (cap != null) { cap.radius *= colliderInflate; continue; }
                SphereCollider sph = boneCols[i] as SphereCollider;
                if (sph != null) sph.radius *= colliderInflate;
            }
        }

        // Stop the ragdoll from fighting itself (the #1 cause of the violent jitter): bone colliders must
        // not collide with each other. Joint-connected pairs already ignore each other, but non-adjacent
        // ones (arm vs leg, etc.) don't — and the inflated colliders overlap. Disable ALL self-collision.
        for (int i = 0; i < boneCols.Length; i++)
            for (int j = i + 1; j < boneCols.Length; j++)
                if (boneCols[i] != null && boneCols[j] != null)
                    Physics.IgnoreCollision(boneCols[i], boneCols[j], true);

        // The limp bean must SWEEP cleanly when the spinning bar hits it — NOT rattle (the old shake = grippy
        // bones pinned + crushed in place by the bar) and NOT pass through (zero contact = the bar clips the
        // bean). Fix: give the bones a SLIPPERY material so the bar slides/sweeps them along the deck instead of
        // crushing them. (Detection/knock is unaffected — that's an overlap query + impulse.)
        PhysicsMaterial slick = new PhysicsMaterial("RagdollSlick");
        slick.dynamicFriction = ragdollFriction;
        slick.staticFriction = ragdollFriction;
        slick.frictionCombine = PhysicsMaterialCombine.Minimum; // stays slippery regardless of the deck's material
        slick.bounciness = 0f;
        for (int i = 0; i < boneCols.Length; i++)
            if (boneCols[i] != null) boneCols[i].sharedMaterial = slick;

        // Escape hatch: if the swept contact STILL shakes, beamPassThrough re-applies the old "ignore the beam
        // bars entirely" behaviour (perfectly smooth, but the bar visibly passes through the fallen bean).
        if (beamPassThrough)
        {
            SpinningBeamHazard[] beams = Object.FindObjectsByType<SpinningBeamHazard>(FindObjectsSortMode.None);
            for (int b = 0; b < beams.Length; b++)
            {
                if (beams[b] == null) continue;
                Collider[] beamCols = beams[b].GetComponentsInChildren<Collider>(true);
                for (int k = 0; k < beamCols.Length; k++)
                    for (int i = 0; i < boneCols.Length; i++)
                        if (beamCols[k] != null && boneCols[i] != null)
                            Physics.IgnoreCollision(boneCols[i], beamCols[k], true);
            }
        }

        // Projection keeps the joints from being pulled apart (stretched limbs) under a fast-beam impact.
        CharacterJoint[] joints = hips.GetComponentsInChildren<CharacterJoint>(true);
        for (int i = 0; i < joints.Length; i++)
        {
            joints[i].enableProjection = true;
            joints[i].projectionDistance = 0.01f;
            joints[i].projectionAngle = 25f;
        }

        SetBonesDynamic(false); // start animated

        // Capture each standup clip's first (trimmed) frame in LOCAL space, so the get-up can lerp the landed
        // heap into the clip's start pose (the video's "reset bones"). SampleAnimation poses the bones; we
        // save/restore the animator-root transform and Rebind afterwards so nothing leaks into the live pose.
        faceTargetReady = SampleClipFirstFrame(standFaceClipName, standBonesFace);
        backTargetReady = SampleClipFirstFrame(standBackClipName, standBonesBack);
        if (animator != null) animator.Rebind();

        // Read the ACTUAL clip lengths so the get-up hand-off tracks the real clip (the serialized
        // standFace/BackDuration go stale every time the clips are re-baked/trimmed -> control returned
        // mid-clip and the still-rising animation floated the mesh up = the "levitation"). Fallback to the
        // serialized durations if a clip wasn't found.
        AnimationClip fc = FindClip(standFaceClipName);
        AnimationClip bc = FindClip(standBackClipName);
        standFaceLen = fc != null ? fc.length : standFaceDuration;
        standBackLen = bc != null ? bc.length : standBackDuration;
    }

    void Update()
    {
        if (!ragdolled) return;
        timer += Time.deltaTime;
        if (timer >= minLimpTime && IsSettledOnGround())
            BeginStandUp();
    }

    void LateUpdate()
    {
        // Smooth the camera target toward the hips so per-frame bone jitter doesn't shake the view
        // ("feels laggy"). The proxy is standalone, so moving it never feeds back into the bones.
        if (camProxy != null && ragdolled && hips != null)
            camProxy.position = Vector3.SmoothDamp(camProxy.position, hips.position, ref camProxyVel, camSmoothTime);

        if (resettingBones) { ResettingBonesStep(); return; }
        if (standingUp)
        {
            // Mid-rise: sink-only (the feet still SWING here — full grounding would chase them = the old mid-pop).
            // Final stretch (feet measured stable from ~70%): FULL grounding = plant the soles flat on the deck
            // so there's no lift-off (levitate) and the hand-off needs no root teleport (the snap).
            if (curStandNorm >= 1f - endSettleFraction) GroundLowestBone(false);
            else GroundLowestBone(true);
        }
    }

    private void GroundLowestBone() { GroundLowestBone(false); }

    // Shift the root so the LOWEST ragdoll bone/foot rests on the deck. Works for the lying start (torso/head
    // is lowest) through to standing (feet lowest) — self-correcting, no bake math.
    // sinkOnly: during the standup CLIP we must never LIFT the body to chase a foot that dips low on the
    // push-up/leg-swing (that was the "pop up into the air"). With sinkOnly we only LOWER the body to kill
    // float — the clip's own Hips height curve drives the rise.
    private void GroundLowestBone(bool sinkOnly)
    {
        float deckY;
        if (!FindGroundYUnderHips(out deckY)) return;
        // Ground by the actual FOOT bones (they track the animation precisely): move the root so the lower
        // foot's pivot sits soleOffset above the deck, putting the soles on it. Falls back to the lowest bone
        // pivot if the feet weren't found.
        if (footL != null || footR != null)
        {
            float footY = float.MaxValue;
            if (footL != null) footY = Mathf.Min(footY, footL.position.y);
            if (footR != null) footY = Mathf.Min(footY, footR.position.y);
            float delta = deckY + soleOffset - footY;
            if (sinkOnly && delta > 0f) return; // foot below the line -> don't lift (avoids the mid-standup pop)
            transform.position += Vector3.up * delta;
            return;
        }
        float minY = float.MaxValue;
        for (int i = 0; i < boneBodies.Length; i++)
        {
            if (boneBodies[i] == null) continue;
            float y = boneBodies[i].transform.position.y;
            if (y < minY) minY = y;
        }
        if (minY == float.MaxValue) return;
        float d = deckY + groundSnapEpsilon - minY;
        if (sinkOnly && d > 0f) return;
        transform.position += Vector3.up * d;
    }

    // ---- beam entry points ----

    public void EnableRagdoll(Vector3 inheritVel) { EnableRagdollCore(inheritVel, 1f, Vector3.zero, false); }
    // strength = per-beam knock multiplier (faster/bigger beams pass >1 to hit harder).
    public void EnableRagdoll(Vector3 inheritVel, float strength) { EnableRagdollCore(inheritVel, strength, Vector3.zero, false); }
    // hitPoint = world contact point from the beam, so the off-center impulse tumbles the bean in the
    // direction it was actually struck (low hit -> legs swept + head forward; high hit -> head back).
    public void EnableRagdoll(Vector3 inheritVel, float strength, Vector3 hitPoint) { EnableRagdollCore(inheritVel, strength, hitPoint, true); }

    private void EnableRagdollCore(Vector3 inheritVel, float strength, Vector3 hitPoint, bool hasHit)
    {
        if (boneBodies.Length == 0) return;

        // cancel any in-progress standup (a fresh hit always wins)
        if (standRoutine != null) { StopCoroutine(standRoutine); standRoutine = null; }
        standingUp = false;
        resettingBones = false;

        if (ragdolled) { ReshoveCore(inheritVel, strength, hitPoint, hasHit); return; } // already limp -> re-shove + reset

        ragdolled = true;
        timer = 0f;

        controls.SetRagdoll(true);
        if (animator != null) animator.enabled = false;
        if (walkDriver != null) walkDriver.enabled = false;

        rootRb.isKinematic = true;
        if (rootCol != null) rootCol.enabled = false; // no kinematic-capsule vs flying-bone fight

        SetBonesDynamic(true);
        Vector3 v = ShapeKnock(inheritVel, strength);
        LastFlingVel = v; // expose for multiplayer remote-ragdoll replication
        for (int i = 0; i < boneBodies.Length; i++)
        {
            if (boneBodies[i] == null) continue;
            boneBodies[i].linearVelocity = v;          // modest whole-body shove (subtle launch)
            boneBodies[i].angularVelocity = Vector3.zero;
        }
        ApplyHitImpulse(inheritVel, strength, hitPoint, hasHit); // off-center kick -> directional tumble

        if (cam != null)
        {
            if (camProxy != null && hips != null) { camProxy.position = hips.position; camProxyVel = Vector3.zero; cam.target = camProxy; }
            else if (hips != null) cam.target = hips;
        }
    }

    // Off-center impulse at the strike: pushes the nearest bone in the beam's horizontal direction (+ a small
    // up bias), which ROTATES the body about the hit point. A leg hit sweeps the legs and pitches the bean
    // head-first; a head hit flings the head back. This is the floppiness (vs a rigid block) without more launch.
    private void ApplyHitImpulse(Vector3 inheritVel, float strength, Vector3 hitPoint, bool hasHit)
    {
        if (!hasHit || hitImpulse <= 0f) return;
        Vector3 dir = inheritVel; dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f) return;
        dir = (dir.normalized + Vector3.up * hitImpulseUp).normalized;

        Rigidbody nearest = null; float best = float.MaxValue;
        for (int i = 0; i < boneBodies.Length; i++)
        {
            if (boneBodies[i] == null) continue;
            float d = (boneBodies[i].worldCenterOfMass - hitPoint).sqrMagnitude;
            if (d < best) { best = d; nearest = boneBodies[i]; }
        }
        if (nearest == null) return;
        nearest.AddForceAtPosition(dir * (hitImpulse * strength), hitPoint, ForceMode.Impulse);
    }

    // Beam keeps touching an already-limp bean: shove it further out and reset the get-up timer so it can't
    // pop up while still being hit. Additive (so it "continues" being pushed), clamped so it stays sane.
    public void Reshove(Vector3 inheritVel, float strength) { ReshoveCore(inheritVel, strength, Vector3.zero, false); }
    public void Reshove(Vector3 inheritVel, float strength, Vector3 hitPoint) { ReshoveCore(inheritVel, strength, hitPoint, true); }

    private void ReshoveCore(Vector3 inheritVel, float strength, Vector3 hitPoint, bool hasHit)
    {
        if (!ragdolled) return;
        timer = 0f;
        if (standRoutine != null) { StopCoroutine(standRoutine); standRoutine = null; }
        standingUp = false;
        resettingBones = false;

        // Stronger than the initial knock: a limp bean caught by the beam must be driven OFF the rim, not
        // passed over. Keep the up-pop low so it's a horizontal shove along the deck.
        Vector3 add = inheritVel;
        add.y *= upMultiplier;
        add *= reshoveScale * strength;
        LastFlingVel = Vector3.ClampMagnitude(add, reshoveMaxSpeed * strength); // refresh the streamed fling on a re-hit
        float cap = reshoveMaxSpeed * strength;
        for (int i = 0; i < boneBodies.Length; i++)
        {
            if (boneBodies[i] == null) continue;
            boneBodies[i].linearVelocity = Vector3.ClampMagnitude(boneBodies[i].linearVelocity + add, cap);
        }
        ApplyHitImpulse(inheritVel, strength, hitPoint, hasHit); // keep tumbling naturally on a re-hit
    }

    // Reshape the beam's push: cut the upward pop, shrink it, clamp — knock DOWN + a bit back, not launched.
    private Vector3 ShapeKnock(Vector3 inheritVel, float strength)
    {
        Vector3 v = inheritVel;
        v.y *= upMultiplier;
        v *= knockbackScale * strength;
        return Vector3.ClampMagnitude(v, maxLaunchSpeed * strength);
    }

    // ---- recovery ----

    private static readonly RaycastHit[] _groundHits = new RaycastHit[8];

    private bool IsSettledOnGround()
    {
        if (hipsBody != null && hipsBody.linearVelocity.magnitude > restSpeedThreshold) return false;
        // Cast from ABOVE the hips down (the hips bone sits inside the torso, often at/below the deck top,
        // so a ray starting at the hips would begin below the floor and miss it). Skip our own bones, require
        // an upward-facing floor that's near the hips — so "resting on the deck" is true but "falling high
        // above a far floor" (airborne / thrown off) is false.
        Vector3 origin = hips.position + Vector3.up * 1.5f;
        int n = Physics.RaycastNonAlloc(origin, Vector3.down, _groundHits, 3.0f, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            RaycastHit h = _groundHits[i];
            if (h.collider.transform.GetComponentInParent<RagdollController>() == this) continue; // our bones
            if (h.normal.y < 0.4f) continue;                       // a floor, not a wall/beam
            if (h.point.y <= hips.position.y + groundProbe) return true; // hips resting near/on it
        }
        return false;
    }

    // Deck Y directly under the hips, IGNORING our own ragdoll bones (else the placement self-hits a bone
    // sitting above the deck and the get-up floats). Returns the highest valid upward-facing floor.
    private bool FindGroundYUnderHips(out float groundY)
    {
        groundY = 0f;
        Vector3 origin = hips.position + Vector3.up * 2.0f;
        int n = Physics.RaycastNonAlloc(origin, Vector3.down, _groundHits, 5.0f, ~0, QueryTriggerInteraction.Ignore);
        bool found = false;
        for (int i = 0; i < n; i++)
        {
            RaycastHit h = _groundHits[i];
            if (h.collider.transform.GetComponentInParent<RagdollController>() == this) continue; // our bones
            if (h.normal.y < 0.4f) continue;                                                      // floor only
            if (!found || h.point.y > groundY) { groundY = h.point.y; found = true; }
        }
        return found;
    }

    // Settled on the platform: align facing, ease the landed heap into the standup clip's first frame, then
    // play the matching clip and lock control until it finishes. (NOT used for respawn — that uses
    // DisableRagdoll for an instant reset.)
    private void BeginStandUp()
    {
        bool faceDown = IsFaceDown();
        landedHipsXZ = new Vector2(hips.position.x, hips.position.z); // remember where it came to rest

        // Freeze the bones in their landed pose (kinematic), park the root, keep the Animator OFF — we
        // hand-lerp the bones during the reset. CharacterControls stays ragdolled so it writes nothing to
        // the kinematic root; walkDriver stays off so it can't trip the AnyState->Jump transition.
        SetBonesDynamic(false);
        if (animator != null) animator.enabled = false;
        rootRb.isKinematic = true;
        // Keep the root capsule LIVE during the get-up so a beam's overlap query still finds a "Player"-tagged
        // collider and can re-hit the bean mid-stand-up (bones colliders are off, root is kinematic -> no
        // physics fight). EnableRagdoll disables it again on a fresh hit; StandUpRoutine leaves it enabled.
        if (rootCol != null) rootCol.enabled = true;
        controls.SetRagdoll(true);
        if (walkDriver != null) walkDriver.enabled = false;
        ragdolled = false;
        standingUp = true;

        // Turn the root to face the bean's feet-direction so the clip doesn't have to rotate the body (a
        // source of slide). Restores the hips' world pose so the limp pose doesn't visibly jump.
        AlignRotationToHips(faceDown);

        // Snapshot the landed LOCAL pose AFTER aligning (align changes the hips' local rotation).
        PopulateBoneTransforms(ragdollBones);

        resetTarget = faceDown ? standBonesFace : standBonesBack;
        curStandState = faceDown ? standFaceState : standBackState;
        curStandDur = faceDown ? standFaceLen : standBackLen; // real clip length, not the stale serialized number

        if (debugStandup)
        {
            float bs = smr != null ? smr.bounds.size.magnitude : -1f;
            Debug.Log("[StandupDbg] begin faceDown=" + faceDown + " state='" + curStandState +
                      "' boundsSize=" + bs.ToString("F3") + " hipsScale=" + hips.lossyScale.x.ToString("F4") +
                      " rootY=" + transform.position.y.ToString("F2"));
        }

        bool ready = faceDown ? faceTargetReady : backTargetReady;
        if (ready) { resettingBones = true; resetT = 0f; } // ease into the clip over timeToResetBones
        else { BeginStandClip(); }                         // clip frame-0 not captured -> snap straight in
    }

    // Per-frame ease of the landed heap into the standup clip's first frame. Rotation for every bone; position
    // for the LIMBS only — the Hips' local position lives in the Armature's 0.01-scale space (lerping it
    // spikes to garbage), so the hips is snapped to the clip's local value and placed in the world by
    // AnchorHipsXZ + GroundLowestBone instead.
    private void ResettingBonesStep()
    {
        resetT += Time.deltaTime / Mathf.Max(0.0001f, timeToResetBones);
        float k = Mathf.Clamp01(resetT);
        for (int i = 0; i < skeleton.Length; i++)
        {
            Transform b = skeleton[i];
            if (b == null) continue;
            b.localRotation = Quaternion.Slerp(ragdollBones[i].rotation, resetTarget[i].rotation, k);
            if (b == hips) b.localPosition = resetTarget[i].position;                  // scale-safe: snap, don't lerp
            else b.localPosition = Vector3.Lerp(ragdollBones[i].position, resetTarget[i].position, k);
        }
        AnchorHipsXZ();      // keep the hips over the landing spot (no slide)
        GroundLowestBone();  // keep the lowest bone on the deck (no float)
        if (k >= 1f) { resettingBones = false; BeginStandClip(); }
    }

    // Reset finished (bones now at the clip's first frame): start the clip animating and hand to the timer.
    private void BeginStandClip()
    {
        curStandNorm = 0f; // reset so the end-settle only kicks in near the clip's end
        ForceState(curStandState); // animator ON, deterministically poses + plays clip frame 0 (Rebind resets speed)
        if (animator != null) { animator.SetBool("Airborne", false); animator.SetFloat("Speed", 0f); }
        // Spinner/Course: play the standup clip faster so the bean is back on its feet sooner. StandUpRoutine
        // waits on the clip's normalizedTime, so a higher Animator speed shortens recovery cleanly.
        if (fastGetUp && animator != null) animator.speed = Mathf.Max(0.1f, courseGetUpSpeed);
        AnchorHipsXZ();
        GroundLowestBone();
        if (cam != null) cam.target = transform; // frame the standing bean from the (grounded) root
        standRoutine = StartCoroutine(StandUpRoutine(curStandDur, curStandState));
    }

    // Rotate the root so its forward = the hips' feet-direction (flattened). Keeps the hips' world pose fixed
    // so the limp pose doesn't jump when the root turns.
    private void AlignRotationToHips(bool faceDown)
    {
        Vector3 dir = hips.rotation * hipsFacingAxis;
        if (faceDown && flipFacingWhenFaceDown) dir = -dir;
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-6f) return;
        dir.Normalize();
        Vector3 fwd = transform.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
        fwd.Normalize();
        Quaternion hr = hips.rotation; Vector3 hp = hips.position;
        transform.rotation = Quaternion.FromToRotation(fwd, dir) * transform.rotation;
        hips.rotation = hr; hips.position = hp; // restore the hips' world pose (only root + hips-local change)
    }

    // One-shot XZ correction: shift the root so the hips sit over the spot the bean landed (kills the slide).
    private void AnchorHipsXZ()
    {
        Vector3 hp = hips.position;
        transform.position += new Vector3(landedHipsXZ.x - hp.x, 0f, landedHipsXZ.y - hp.z);
    }

    private void PopulateBoneTransforms(BoneTransform[] arr)
    {
        for (int i = 0; i < skeleton.Length; i++)
        {
            if (skeleton[i] == null) continue;
            arr[i].position = skeleton[i].localPosition;
            arr[i].rotation = skeleton[i].localRotation;
        }
    }

    // Pose the bones at a clip's first frame and snapshot their LOCAL transforms. Sampling moves the animator
    // root, so save/restore it. Clip paths are relative to the Animator root (BeanModel), so sample there.
    private bool SampleClipFirstFrame(string clipName, BoneTransform[] arr)
    {
        AnimationClip clip = FindClip(clipName);
        if (clip == null) { Debug.LogWarning("[Ragdoll] standup clip '" + clipName + "' not found in controller — reset will snap."); return false; }
        Transform root = animator != null ? animator.transform : transform;
        Vector3 rp = root.position; Quaternion rr = root.rotation;
        clip.SampleAnimation(animator != null ? animator.gameObject : gameObject, 0f);
        PopulateBoneTransforms(arr);
        root.position = rp; root.rotation = rr;
        return true;
    }

    private AnimationClip FindClip(string clipName)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return null;
        AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;
        for (int i = 0; i < clips.Length; i++)
            if (clips[i] != null && clips[i].name == clipName) return clips[i];
        return null;
    }

    private static BoneTransform[] NewBoneArray(int n)
    {
        BoneTransform[] a = new BoneTransform[n];
        for (int i = 0; i < n; i++) a[i] = new BoneTransform();
        return a;
    }

    // Deterministically force an animator state even right after the Animator was re-enabled (a plain Play()
    // the same frame can be dropped before the rebind). Rebind -> Play -> Update(0) makes it stick + apply now.
    private void ForceState(string state)
    {
        if (animator == null) return;
        animator.enabled = true;
        animator.ResetTrigger(standFaceTrigger);
        animator.ResetTrigger(standBackTrigger);
        animator.Rebind();
        animator.Play(state, 0, 0f);
        animator.Update(0f);
    }

    private IEnumerator StandUpRoutine(float duration, string expectedState)
    {
        // Hand control back exactly when the standup clip FINISHES — not on a hardcoded duration that goes
        // stale on every re-bake. Returning early left the Animator still raising the Hips, floating the mesh
        // up while physics held the root = the "levitation". Poll the animator; cap as a safety net.
        float elapsed = 0f;
        float cap = duration + 0.5f;
        bool logged = false;
        while (elapsed < cap)
        {
            if (animator != null)
            {
                AnimatorStateInfo st = animator.GetCurrentAnimatorStateInfo(0);
                curStandNorm = st.IsName(expectedState) ? st.normalizedTime : 1f; // drives the end-settle in LateUpdate
                if (!st.IsName(expectedState) || st.normalizedTime >= 0.95f) break; // clip done / left the state
            }
            if (debugStandup && !logged && elapsed >= 0.3f && animator != null)
            {
                logged = true;
                float bs = smr != null ? smr.bounds.size.magnitude : -1f;
                Debug.Log("[StandupDbg] +0.3s inExpected=" + animator.GetCurrentAnimatorStateInfo(0).IsName(expectedState) +
                          " boundsSize=" + bs.ToString("F3") + " hipsScale=" + hips.lossyScale.x.ToString("F4"));
            }
            elapsed += Time.deltaTime;
            yield return null;
        }

        GroundLowestBone();                 // final frame: plant the soles flat on the deck (feet-based, full)
        if (animator != null) animator.speed = 1f; // restore normal playback after the (possibly sped-up) standup
        standRoutine = null;
        standingUp = false;

        // DELIBERATELY no capsule-based root reseat here. That reseat WAS the end-snap/levitate: it parked the
        // root so the capsule BOTTOM hit the deck, but the mesh FEET sit ~0.08 above the capsule bottom, so the
        // visible bean popped up off the floor at hand-off. Grounding by the FEET (above) already lands the soles
        // on the deck and leaves the capsule bottom a hair above it, so CharacterControls.IsGrounded() (a
        // downward ray of distToGround+0.1) is still true immediately — no teleport, no pop.

        if (rootCol != null) rootCol.enabled = true;
        rootRb.isKinematic = false;         // restore physics-driven root
        rootRb.linearVelocity = Vector3.zero;
        rootRb.angularVelocity = Vector3.zero;
        // Force the locomotion params to standing-still BEFORE the walk driver wakes, so its first-frame
        // ground raycast can't flip Airborne=true for a frame (which would fire AnyState->Jump = a 1-frame
        // twitch right at the standup->idle boundary).
        if (animator != null) { animator.SetBool("Airborne", false); animator.SetFloat("Speed", 0f); }
        if (walkDriver != null) walkDriver.enabled = true; // locomotion resumes
        controls.SetRagdoll(false); // clear ragdoll flag -> movement resumes
    }

    private bool IsFaceDown()
    {
        // chest.forward is the near-vertical axis when the bean lies down (chest.up stays horizontal).
        // Lying on its front -> forward points down (y<0). Flip faceDownWhenDotNegative if it's reversed.
        float d = Vector3.Dot(chest.forward, Vector3.up);
        return faceDownWhenDotNegative ? d < 0f : d > 0f;
    }

    // Hard reset to the animated/idle state (used by respawn). No standup clip.
    public void DisableRagdoll()
    {
        if (!ragdolled && !standingUp) return;
        ragdolled = false;
        standingUp = false;
        resettingBones = false;
        if (standRoutine != null) { StopCoroutine(standRoutine); standRoutine = null; }

        ExitRagdollToAnimated();
        controls.SetRagdoll(false);
        ForceState(idleState); // clean instant reset: no leftover get-up carrying into the respawn
        if (walkDriver != null) walkDriver.enabled = true; // normal locomotion resumes
    }

    // Shared: move the root over the settled hips, bones back to kinematic, animator/root physics back on.
    private void ExitRagdollToAnimated()
    {
        ragdolled = false;

        if (hips != null)
        {
            Vector3 p = hips.position;
            float deckY;
            float y = FindGroundYUnderHips(out deckY) ? deckY + capsuleHalfHeight : p.y + standOffset;
            transform.position = new Vector3(p.x, y, p.z);
            transform.rotation = Quaternion.identity;
        }

        SetBonesDynamic(false);

        rootRb.isKinematic = false;
        rootRb.linearVelocity = Vector3.zero;
        rootRb.angularVelocity = Vector3.zero;
        if (rootCol != null) rootCol.enabled = true;

        if (animator != null) animator.enabled = true;
        // NOTE: walkDriver is intentionally NOT re-enabled here. It sets Airborne=true for a frame, which
        // triggers the controller's AnyState->Jump transition and yanks the bean out of the get-up state.
        // Callers re-enable it when locomotion should resume (respawn now, or after the get-up clip).

        if (cam != null) cam.target = transform;
    }

    private void SetBonesDynamic(bool dynamic)
    {
        for (int i = 0; i < boneBodies.Length; i++)
        {
            Rigidbody b = boneBodies[i];
            if (b == null) continue;
            b.isKinematic = !dynamic;
            b.useGravity = dynamic;
            if (dynamic)
            {
                b.interpolation = RigidbodyInterpolation.Interpolate;
                // Discrete, NOT ContinuousDynamic: continuous sweeps on an 11-body jointed ragdoll resting on
                // the deck's mesh collider are the #1 cause of the shake/vibration (and far worse in WebGL).
                // The bones are slow + chunky, so tunneling through the thick deck is a non-issue.
                b.collisionDetectionMode = CollisionDetectionMode.Discrete;
                b.solverIterations = 16;             // steadier contacts -> less sinking into the deck + less WebGL jitter
                b.solverVelocityIterations = 8;
                b.maxAngularVelocity = 20f;          // tame frantic spin -> less vibration
                b.maxDepenetrationVelocity = ragdollMaxDepenetration; // un-penetrate gently, don't explode into jitter
                b.angularDamping = ragdollAngularDamping; // settle micro-vibration without killing the flop
                b.linearDamping = ragdollLinearDamping;
            }
            else
            {
                // CRITICAL for the get-up to be visible: a kinematic body that KEEPS Interpolate re-imposes
                // its last (fallen) physics pose over the Animator's output every rendered frame -> the
                // get-up clip can't move the bones. Reset so the Animator drives them cleanly.
                b.collisionDetectionMode = CollisionDetectionMode.Discrete;
                b.interpolation = RigidbodyInterpolation.None;
            }
        }
        for (int i = 0; i < boneCols.Length; i++)
            if (boneCols[i] != null) boneCols[i].enabled = dynamic;
    }
}
