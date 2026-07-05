using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Periodically spawns physics-driven sphere "balls" that roll down a ramp toward the player.
/// Each ball is a solid Rigidbody sphere (rolls + collides with ramp/cover) plus a child trigger
/// carrying a Hazard, so contact knocks the player back. Balls self-destroy after `lifetime`.
/// </summary>
public class BallSpawner : MonoBehaviour
{
    [Header("Cadence")]
    public float interval = 2f;
    public int maxAlive = 6;
    [Tooltip("Seconds before a spawned ball is destroyed.")]
    public float lifetime = 9f;

    [Header("Ball")]
    public float ballRadius = 0.9f;
    public float radiusJitter = 0.4f;
    public float mass = 5f;
    [Tooltip("Initial velocity in local space (e.g. down the ramp = -Z).")]
    public Vector3 launchVelocity = new Vector3(0f, 0f, -4f);
    public Material[] ballMats;

    [Header("Hit response")]
    public float knockback = 16f;
    public float up = 5f;
    public float stun = 0.3f;
    public float hitRadiusPad = 0.3f;

    float timer;
    readonly List<GameObject> alive = new List<GameObject>();

    void Update()
    {
        alive.RemoveAll(b => b == null);
        timer -= Time.deltaTime;
        if (timer <= 0f)
        {
            timer = interval;
            if (alive.Count < maxAlive) Spawn();
        }
    }

    void Spawn()
    {
        float r = Mathf.Max(0.2f, ballRadius + Random.Range(-radiusJitter, radiusJitter));

        var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.name = "Ball";
        ball.transform.position = transform.position;
        ball.transform.localScale = Vector3.one * (r * 2f);

        if (ballMats != null && ballMats.Length > 0)
        {
            var m = ballMats[Random.Range(0, ballMats.Length)];
            if (m != null) ball.GetComponent<Renderer>().sharedMaterial = m;
        }

        var rb = ball.AddComponent<Rigidbody>();
        rb.mass = mass;
        rb.linearVelocity = transform.TransformDirection(launchVelocity);
        rb.angularDamping = 0.05f;

        // Child trigger that knocks the player back on contact.
        var hit = new GameObject("HitTrigger");
        hit.transform.SetParent(ball.transform, false);
        var sc = hit.AddComponent<SphereCollider>();
        sc.isTrigger = true;
        sc.radius = 0.5f + hitRadiusPad / Mathf.Max(0.2f, r * 2f); // local space (parent scaled)
        var hz = hit.AddComponent<Hazard>();
        hz.knockbackForce = knockback;
        hz.upForce = up;
        hz.stunDuration = stun;

        Object.Destroy(ball, lifetime);
        alive.Add(ball);
    }
}
