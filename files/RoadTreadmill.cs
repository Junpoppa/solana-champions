using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Creates the illusion of infinite road. The player car never actually moves
/// forward — instead a row of road tiles spawns ahead and slides toward the
/// camera at GameManager.WorldSpeed. When a tile passes behind the camera it is
/// recycled to the far end. This is cheap and WebGL-friendly (no GC churn).
///
/// SETUP (Phase 2):
///   - Create an empty GameObject named "Road", attach this script.
///   - Assign roadTilePrefab (the RoadTile prefab from Phase 1).
///   - Set tileLength to the EXACT length of one tile along Z (from Phase 1).
///   - Press Play. Tiles should stream toward you forever.
/// </summary>
public class RoadTreadmill : MonoBehaviour
{
    [Header("Tile setup")]
    [Tooltip("The road tile prefab to repeat.")]
    public GameObject roadTilePrefab;

    [Tooltip("Length of ONE tile along the travel (Z) axis, in world units. "
           + "Measure this in Phase 1 and enter it exactly, or tiles will gap/overlap.")]
    public float tileLength = 30f;

    [Tooltip("How many tiles to keep alive at once. Enough to fill from behind "
           + "the camera to past the far view distance.")]
    public int tileCount = 8;

    [Tooltip("How far behind the origin (negative Z) a tile may go before it "
           + "recycles to the front. Roughly the camera's Z minus a margin.")]
    public float recycleBehindZ = -20f;

    // Active tiles. Order doesn't matter; FurthestZ() finds the far edge.
    private readonly List<Transform> _tiles = new List<Transform>();

    void Start()
    {
        if (roadTilePrefab == null)
        {
            Debug.LogError("[RoadTreadmill] roadTilePrefab is not assigned.");
            enabled = false;
            return;
        }

        // Lay the initial run of tiles end-to-end starting a little behind us.
        float z = recycleBehindZ;
        for (int i = 0; i < tileCount; i++)
        {
            GameObject go = Instantiate(roadTilePrefab, transform);
            go.transform.localPosition = new Vector3(0f, 0f, z);
            _tiles.Add(go.transform);
            z += tileLength;
        }
    }

    void Update()
    {
        if (GameManager.Instance == null) return;

        float move = GameManager.Instance.WorldSpeed * Time.deltaTime;

        for (int i = 0; i < _tiles.Count; i++)
        {
            Transform t = _tiles[i];
            // Slide toward the camera (negative Z).
            t.localPosition += Vector3.back * move;

            // Recycle when fully behind the recycle line: snap to the far end.
            if (t.localPosition.z <= recycleBehindZ - tileLength)
            {
                float newZ = FurthestZ() + tileLength;
                t.localPosition = new Vector3(t.localPosition.x, t.localPosition.y, newZ);
            }
        }
    }

    // Helper: find the current furthest tile Z so recycling never gaps.
    private float FurthestZ()
    {
        float max = float.NegativeInfinity;
        for (int i = 0; i < _tiles.Count; i++)
            if (_tiles[i].localPosition.z > max) max = _tiles[i].localPosition.z;
        return max;
    }
}
