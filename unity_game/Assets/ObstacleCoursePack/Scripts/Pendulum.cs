using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Pendulum : MonoBehaviour
{
	public float speed = 1.5f;
	public float limit = 75f; //Limit in degrees of the movement
	public bool randomStart = false; //If you want to modify the start position
	// Local axis the arm rotates about. Default (0,0,1) == old Euler(0,0,angle) (swing in X-Y plane,
	// ball travels along local X). Set (1,0,0) to swing in the Y-Z plane (ball travels along local Z) —
	// e.g. perpendicular to a road running along X.
	public Vector3 swingAxisLocal = new Vector3(0, 0, 1);
	// When true the arm is driven by a kinematic Rigidbody (MoveRotation in FixedUpdate) so its solid
	// child colliders PHYSICALLY PUSH the player (like SHOW_MovableWall) instead of passing through.
	// Off = legacy transform-only spin (colliders don't push / can tunnel).
	public bool physicalPush = true;
	private float random = 0;
	private Rigidbody rb;

	// Start is called before the first frame update
	void Awake()
    {
		if(randomStart)
			random = Random.Range(0f, 1f);

		if (physicalPush)
		{
			rb = GetComponent<Rigidbody>();
			if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
			rb.isKinematic = true;
			rb.useGravity = false;
			rb.interpolation = RigidbodyInterpolation.Interpolate;
			rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
		}
	}

	Quaternion TargetLocalRotation()
	{
		float angle = limit * Mathf.Sin(Time.time + random * speed);
		Vector3 axis = swingAxisLocal.sqrMagnitude < 1e-6f ? Vector3.forward : swingAxisLocal.normalized;
		return Quaternion.AngleAxis(angle, axis);
	}

	// Update is called once per frame
	void Update()
	{
		if (rb != null) return; // physics path drives rotation in FixedUpdate
		transform.localRotation = TargetLocalRotation();
	}

	void FixedUpdate()
	{
		if (rb == null) return;
		// kinematic MoveRotation so the swinging collider shoves dynamic bodies it sweeps into
		Quaternion local = TargetLocalRotation();
		Quaternion world = transform.parent != null ? transform.parent.rotation * local : local;
		rb.MoveRotation(world);
	}
}
