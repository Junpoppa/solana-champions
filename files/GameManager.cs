using UnityEngine;

/// <summary>
/// Central brain of the game. ONE source of truth for world speed and game state.
/// Road, and later obstacles, read WorldSpeed from here so everything stays in sync.
///
/// Phase 0: exists as a stub (state stays Ready/Playing, speed ramps).
/// Phase 3+: GameOver() gets called by collision; Phase 4 wires the UI to states.
/// </summary>
public class GameManager : MonoBehaviour
{
    public enum State { Ready, Playing, GameOver }

    // ---- Singleton so any script can reach it without wiring references ----
    public static GameManager Instance { get; private set; }

    [Header("Speed")]
    [Tooltip("World units per second the road/obstacles move toward the player at the start.")]
    public float startSpeed = 12f;

    [Tooltip("Maximum world speed the ramp will reach.")]
    public float maxSpeed = 36f;

    [Tooltip("How many units/sec the speed gains per second of play.")]
    public float acceleration = 0.4f;

    /// <summary>Current world speed. Everything that moves reads this.</summary>
    public float WorldSpeed { get; private set; }

    /// <summary>Current game state. UI and spawners will react to this later.</summary>
    public State CurrentState { get; private set; } = State.Ready;

    /// <summary>Seconds survived this run. Basis for score + difficulty.</summary>
    public float RunTime { get; private set; }

    void Awake()
    {
        // Standard singleton guard.
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        // For Phase 0/2 we start playing immediately so you can test the feel.
        // Phase 4 will replace this with a Start screen that calls StartGame().
        StartGame();
    }

    void Update()
    {
        if (CurrentState != State.Playing) return;

        RunTime += Time.deltaTime;

        // Slow, steady ramp toward maxSpeed.
        WorldSpeed = Mathf.Min(maxSpeed, WorldSpeed + acceleration * Time.deltaTime);
    }

    public void StartGame()
    {
        RunTime = 0f;
        WorldSpeed = startSpeed;
        CurrentState = State.Playing;
    }

    public void GameOver()
    {
        if (CurrentState != State.Playing) return;
        CurrentState = State.GameOver;
        WorldSpeed = 0f;
        // Phase 4: show game-over UI here.
        Debug.Log($"[GameManager] Game Over. Survived {RunTime:0.0}s.");
    }

    public void Restart()
    {
        // Phase 4 will reload/reset cleanly. Stub for now.
        StartGame();
    }
}
