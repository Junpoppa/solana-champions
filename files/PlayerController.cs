using UnityEngine;

/// <summary>
/// Steers the player car left/right across the road. The car does NOT move
/// forward — the road moves (see RoadTreadmill). This only controls X (and a
/// little visual bank/tilt for feel).
///
/// INPUT NOTE: this uses Unity's legacy Input class so it works with zero setup.
/// In Unity 6, go to Project Settings > Player > Active Input Handling and set it
/// to "Both" (or "Input Manager (Old)"). If it's set to the new Input System
/// only, Input.GetAxis throws and you'll see errors — that's the fix.
///
/// Controls: A/D or Left/Right arrows (keyboard). Touch: drag left/right, or
/// hold on either half of the screen.
/// </summary>
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    [Tooltip("Sideways speed in world units per second at full input.")]
    public float strafeSpeed = 10f;

    [Tooltip("Half the drivable road width. The car is clamped to +/- this in X.")]
    public float roadHalfWidth = 4f;

    [Tooltip("How quickly steering input ramps up/down. Higher = snappier.")]
    public float responsiveness = 12f;

    [Header("Feel (visual only)")]
    [Tooltip("Max degrees the car banks into a turn.")]
    public float bankAngle = 12f;

    private float _currentInput;   // smoothed -1..1
    private float _touchStartX;
    private bool _touching;

    void Update()
    {
        // Don't steer unless we're actually playing.
        if (GameManager.Instance != null &&
            GameManager.Instance.CurrentState != GameManager.State.Playing)
            return;

        float target = ReadInput();

        // Smooth toward the target input for a less twitchy feel.
        _currentInput = Mathf.MoveTowards(_currentInput, target, responsiveness * Time.deltaTime);

        // Apply sideways movement.
        Vector3 pos = transform.position;
        pos.x += _currentInput * strafeSpeed * Time.deltaTime;
        pos.x = Mathf.Clamp(pos.x, -roadHalfWidth, roadHalfWidth);
        transform.position = pos;

        // Visual bank into the turn.
        Quaternion targetRot = Quaternion.Euler(0f, 0f, -_currentInput * bankAngle);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 10f * Time.deltaTime);
    }

    /// <summary>Returns steering input in range -1 (left) .. +1 (right).</summary>
    private float ReadInput()
    {
        // --- Keyboard ---
        float kb = Input.GetAxisRaw("Horizontal"); // A/D + arrows
        if (Mathf.Abs(kb) > 0.01f) return kb;

        // --- Touch (mobile / touchscreen) ---
        if (Input.touchCount > 0)
        {
            Touch t = Input.GetTouch(0);
            switch (t.phase)
            {
                case TouchPhase.Began:
                    _touchStartX = t.position.x;
                    _touching = true;
                    break;
                case TouchPhase.Moved:
                case TouchPhase.Stationary:
                    if (_touching)
                    {
                        // Simple: which side of the screen are they holding?
                        float half = Screen.width * 0.5f;
                        return t.position.x < half ? -1f : 1f;
                    }
                    break;
                case TouchPhase.Ended:
                case TouchPhase.Canceled:
                    _touching = false;
                    break;
            }
        }

        // --- Mouse (handy for testing in the editor) ---
        if (Input.GetMouseButton(0))
        {
            float half = Screen.width * 0.5f;
            return Input.mousePosition.x < half ? -1f : 1f;
        }

        return 0f;
    }

    // Visualize the road bounds in the Scene view for easy tuning.
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Vector3 c = transform.position;
        Gizmos.DrawLine(new Vector3(-roadHalfWidth, c.y, c.z), new Vector3(-roadHalfWidth, c.y, c.z + 5f));
        Gizmos.DrawLine(new Vector3(roadHalfWidth, c.y, c.z), new Vector3(roadHalfWidth, c.y, c.z + 5f));
    }
}
