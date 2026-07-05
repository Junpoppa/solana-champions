using UnityEngine;

/// <summary>Bobs an object up and down on a sine wave (live stepping-stone pads).</summary>
public class Bobber : MonoBehaviour
{
    public float amplitude = 0.6f;
    public float speed = 2f;

    float baseY;
    float phase;

    void Start()
    {
        baseY = transform.position.y;
        phase = Random.value * Mathf.PI * 2f;
    }

    void Update()
    {
        Vector3 p = transform.position;
        p.y = baseY + Mathf.Sin(Time.time * speed + phase) * amplitude;
        transform.position = p;
    }
}
