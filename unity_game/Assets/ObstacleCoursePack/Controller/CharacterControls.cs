using UnityEngine;
using System.Collections;

[RequireComponent (typeof (Rigidbody))]
[RequireComponent (typeof (CapsuleCollider))]

public class CharacterControls : MonoBehaviour {
	
	public float speed = 10.0f;
	public float airVelocity = 8f;
	public float gravity = 10.0f;
	public float maxVelocityChange = 10.0f;
	public float jumpHeight = 2.0f;
	public float maxFallSpeed = 20.0f;
	public float rotateSpeed = 25f; //Speed the player rotate

	[Header("Double Jump (Roll Out only)")]
	public bool doubleJumpEnabled = false; // auto-set true in the RollOut scene (see Awake)
	public float secondJumpHeight = 2.3f;  // slightly higher than jumpHeight (~15%); tunable
	public float jumpLockout = 0.18f;      // after any jump, ignore the grounded re-check for this long so a
	                                       // fast double-tap routes to the AIR jump (not a re-grounded ground jump)
	private int jumpsUsed;                  // 0 grounded, 1 after first jump, 2 after the air (second) jump
	private float lastJumpTime = -999f;     // time of the last jump (ground or air); gates the lockout above
	private bool jumpQueued;                // Jump button-down edge captured in Update (FixedUpdate misses GetButtonDown)
	private BeanWalkDriver beanDriver;      // cosmetic animator driver (fires the DoubleJump trigger)

	private Vector3 moveDir;
	public GameObject cam;
	private Rigidbody rb;

	private float distToGround;

	private bool canMove = true; //If player is not hitted
	private bool isStuned = false;
	private bool wasStuned = false; //If player was stunned before get stunned another time
	private bool ragdolled = false; //While ragdolling, physics on the bones drives the bean; root is frozen
	private float pushForce;
	private Vector3 pushDir;

	// Temporary movement slowdown (e.g. Roll Out energy-cell zap). Multiplies the player's TARGET
	// velocity only, so walking against the conveyor is harder without touching RollDrum's carry.
	// It ALSO scales the per-step acceleration clamp (see FixedUpdate), which is what makes a zap feel
	// HEAVY — mushy to get going, mushy to stop — rather than just capped at a lower top speed.
	private float slowMult = 1f;
	private float slowTimer = 0f;
	public void ApplySlow(float mult, float duration){ slowMult = mult; slowTimer = Mathf.Max(slowTimer, duration); }
	private float SlowAccel { get { return maxVelocityChange * slowMult; } }

	public Vector3 checkPoint;
	private bool slide = false;

	// Fired at the end of LoadCheckPoint() (KillZone respawn). SpinnerDifficultyRamp uses this to reset
	// its escalation timeline to t=0 for the new life. Null in modes that don't subscribe.
	public System.Action onRespawn;

	void  Start (){
		// get the distance to ground
		distToGround = GetComponent<Collider>().bounds.extents.y;
		// Double jump is enabled in Roll Out, Spinner (Course) and Last Man Standing (same scene-gate idiom
		// as BeanWalkDriver). The SecondJump anim state lives in the shared BeanLocomotion.controller, so all
		// three get it for free. ObstacleCourse (race, WIP) stays on single jump.
		string djScene = gameObject.scene.name;
		doubleJumpEnabled = djScene == "RollOut" || djScene == "Course" || djScene == "LastManStanding";
		beanDriver = GetComponentInChildren<BeanWalkDriver>();
	}
	
	bool IsGrounded (){
		return Physics.Raycast(transform.position, -Vector3.up, distToGround + 0.1f);
	}
	
	void Awake () {
		rb = GetComponent<Rigidbody>();
		rb.freezeRotation = true;
		rb.useGravity = false;

		checkPoint = transform.position;
		Cursor.visible = false;
		//WebGL never engages the browser's pointer lock unless lockState is Locked — without it the
		//(hidden) OS cursor hits the window edge and Input.GetAxis("Mouse X") deltas die (camera "sticks").
		//Browsers defer the actual lock to the next user gesture; JS side has a matching fallback.
		Cursor.lockState = CursorLockMode.Locked;
	}
	
	void FixedUpdate () {
		if (ragdolled) return; // root is kinematic; bone ragdoll is in control
		if (canMove)
		{
			if (moveDir.x != 0 || moveDir.z != 0)
			{
				Vector3 targetDir = moveDir; //Direction of the character

				targetDir.y = 0;
				if (targetDir == Vector3.zero)
					targetDir = transform.forward;
				Quaternion tr = Quaternion.LookRotation(targetDir); //Rotation of the character to where it moves
				Quaternion targetRotation = Quaternion.Slerp(transform.rotation, tr, Time.deltaTime * rotateSpeed); //Rotate the character little by little
				transform.rotation = targetRotation;
			}

			if (IsGrounded())
			{
			 // Calculate how fast we should be moving
				Vector3 targetVelocity = moveDir;
				targetVelocity *= speed * slowMult;

				// Apply a force that attempts to reach our target velocity
				Vector3 velocity = rb.linearVelocity;
				if (targetVelocity.magnitude < velocity.magnitude) //If I'm slowing down the character
				{
					targetVelocity = velocity;
					rb.linearVelocity /= 1.1f;
				}
				Vector3 velocityChange = (targetVelocity - velocity);
				velocityChange.x = Mathf.Clamp(velocityChange.x, -SlowAccel, SlowAccel);
				velocityChange.z = Mathf.Clamp(velocityChange.z, -SlowAccel, SlowAccel);
				velocityChange.y = 0;
				if (!slide)
				{
					if (Mathf.Abs(rb.linearVelocity.magnitude) < speed * 1.0f)
						rb.AddForce(velocityChange, ForceMode.VelocityChange);
				}
				else if (Mathf.Abs(rb.linearVelocity.magnitude) < speed * 1.0f)
				{
					rb.AddForce(moveDir * 0.15f, ForceMode.VelocityChange);
					//Debug.Log(rb.velocity.magnitude);
				}
			}
			else
			{
				if (!slide)
				{
					Vector3 targetVelocity = new Vector3(moveDir.x * airVelocity * slowMult, rb.linearVelocity.y, moveDir.z * airVelocity * slowMult);
					Vector3 velocity = rb.linearVelocity;
					Vector3 velocityChange = (targetVelocity - velocity);
					velocityChange.x = Mathf.Clamp(velocityChange.x, -SlowAccel, SlowAccel);
					velocityChange.z = Mathf.Clamp(velocityChange.z, -SlowAccel, SlowAccel);
					rb.AddForce(velocityChange, ForceMode.VelocityChange);
					if (velocity.y < -maxFallSpeed)
						rb.linearVelocity = new Vector3(velocity.x, -maxFallSpeed, velocity.z);
				}
				else if (Mathf.Abs(rb.linearVelocity.magnitude) < speed * 1.0f)
				{
					rb.AddForce(moveDir * 0.15f, ForceMode.VelocityChange);
				}
			}

			// --- Unified jump handling (edge-triggered, works in BOTH grounded and air branches) ---
			// Reset the jump count only on a REAL landing: grounded AND past the post-jump lockout. Without
			// the lockout the bean still reads as grounded for a frame right after a jump, which would reset
			// the count and let a fast second tap re-fire a ground jump instead of the air (double) jump.
			bool justJumped = Time.time - lastJumpTime < jumpLockout;
			if (IsGrounded() && !justJumped) jumpsUsed = 0;

			if (jumpQueued)
			{
				if (jumpsUsed == 0 && IsGrounded() && !justJumped)
				{
					// Ground jump
					Vector3 v = rb.linearVelocity;
					rb.linearVelocity = new Vector3(v.x, CalculateJumpVerticalSpeed(jumpHeight), v.z);
					jumpsUsed = 1;
					lastJumpTime = Time.time;
					if (beanDriver != null) beanDriver.PlayJumpSfx();
				}
				else if (doubleJumpEnabled && jumpsUsed == 1)
				{
					// Air (second) jump — fires immediately, even on a fast double-tap (we may still be
					// inside the ground-ray range; the lockout above keeps jumpsUsed==1 so we land here).
					Vector3 v = rb.linearVelocity;
					rb.linearVelocity = new Vector3(v.x, CalculateJumpVerticalSpeed(secondJumpHeight), v.z);
					jumpsUsed = 2;
					lastJumpTime = Time.time;
					if (beanDriver != null) beanDriver.TriggerDoubleJump();
				}
			}
		}
		else
		{
			rb.linearVelocity = pushDir * pushForce;
		}
		jumpQueued = false; // consume the buffered Jump press each physics step
		// We apply gravity manually for more tuning control
		rb.AddForce(new Vector3(0, -gravity * GetComponent<Rigidbody>().mass, 0));
	}

	private void Update()
	{
		// Edge-detect the Jump press here (FixedUpdate can miss GetButtonDown). Buffered for the next physics step.
		if (Input.GetButtonDown("Jump")) jumpQueued = true;

		// Decay any temporary slowdown back to full speed.
		if (slowTimer > 0f) { slowTimer -= Time.deltaTime; if (slowTimer <= 0f) slowMult = 1f; }

		float h = Input.GetAxis("Horizontal");
		float v = Input.GetAxis("Vertical");

		Vector3 v2 = v * cam.transform.forward; //Vertical axis to which I want to move with respect to the camera
		Vector3 h2 = h * cam.transform.right; //Horizontal axis to which I want to move with respect to the camera
		moveDir = (v2 + h2).normalized; //Global position to which I want to move in magnitude 1

		RaycastHit hit;
		if (Physics.Raycast(transform.position, -Vector3.up, out hit, distToGround + 0.1f))
		{
			if (hit.transform.tag == "Slide")
			{
				slide = true;
			}
			else
			{
				slide = false;
			}
		}
	}

	float CalculateJumpVerticalSpeed () {
		return CalculateJumpVerticalSpeed(jumpHeight);
	}

	float CalculateJumpVerticalSpeed (float height) {
		// From the jump height and gravity we deduce the upwards speed
		// for the character to reach at the apex.
		return Mathf.Sqrt(2 * height * gravity);
	}

	public void HitPlayer(Vector3 velocityF, float time)
	{
		rb.linearVelocity = velocityF;

		pushForce = velocityF.magnitude;
		pushDir = Vector3.Normalize(velocityF);
		StartCoroutine(Decrease(velocityF.magnitude, time));
	}

	// Public read of the ragdoll flag (true through the limp + get-up). NetBridge syncs it as a "downed"
	// flag so remote avatars mirror the flop instead of gliding upright through hazards.
	public bool IsRagdolled => ragdolled;

	// Called by the spinning beam (via RagdollController). Gates input + stops the root from fighting the bones.
	public void SetRagdoll(bool on)
	{
		ragdolled = on;
		canMove = !on;
		StopAllCoroutines(); // kill any in-flight stun so it can't re-enable movement mid-ragdoll
		isStuned = false;
		wasStuned = false;
	}

	// Lock/unlock input WITHOUT entering ragdoll (used while the get-up animation plays). Root physics
	// still runs (gravity holds the bean on the ground); only player input is ignored.
	public void LockControl(bool locked)
	{
		canMove = !locked;
	}

	public void LoadCheckPoint()
	{
		RagdollController rc = GetComponent<RagdollController>();
		if (rc != null) rc.DisableRagdoll(); // restore animated state (no-op if already animated) before teleporting
		transform.position = checkPoint;
		rb.linearVelocity = Vector3.zero;
		rb.angularVelocity = Vector3.zero;
		pushForce = 0f;
		StopAllCoroutines();
		isStuned = false;
		wasStuned = false;
		canMove = true;
		ragdolled = false;
		onRespawn?.Invoke();
	}

	private IEnumerator Decrease(float value, float duration)
	{
		if (isStuned)
			wasStuned = true;
		isStuned = true;
		canMove = false;

		float delta = 0;
		delta = value / duration;

		for (float t = 0; t < duration; t += Time.deltaTime)
		{
			yield return null;
			if (!slide) //Reduce the force if the ground isnt slide
			{
				pushForce = pushForce - Time.deltaTime * delta;
				pushForce = pushForce < 0 ? 0 : pushForce;
				//Debug.Log(pushForce);
			}
			rb.AddForce(new Vector3(0, -gravity * GetComponent<Rigidbody>().mass, 0)); //Add gravity
		}

		if (wasStuned)
		{
			wasStuned = false;
		}
		else
		{
			isStuned = false;
			canMove = true;
		}
	}
}
