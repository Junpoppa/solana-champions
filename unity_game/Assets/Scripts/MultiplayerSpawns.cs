using UnityEngine;

/// <summary>
/// Per-mode spread spawn points so multiple players don't start stacked on one spot.
/// The local player uses NetBridge.MySpawnIndex; remote beans are positioned from their snapshots.
/// </summary>
public static class MultiplayerSpawns
{
    // Spinner/Course: arc(s) in the diagonal safe quadrants of the deck (center world XZ = (180,20),
    // bean rest Y = 13.1). Beams rest along the axes at GO, so the 45°-offset diagonals are clear.
    public static Vector3 SpinnerPoint(int index, int count)
    {
        float baseAng = 45f + 90f * (index % 4);          // one of the 4 diagonal directions
        int ring = index / 4;
        float r = Mathf.Min(9f + ring * 1.2f, 12f);       // step outward, stay inside the deck
        float ang = (baseAng + (ring % 2 == 0 ? -7f : 7f)) * Mathf.Deg2Rad;
        return new Vector3(180f + r * Mathf.Cos(ang), 13.1f, 20f + r * Mathf.Sin(ang));
    }

    // RollOut: TWO staggered rows across the (clear) top of the spawn band — a single line packs
    // 15 players ~0.9u apart. Rows sit at z=±2.2 on the drum top (R=22 → surface only ~0.11 lower
    // there), X spread to ±6.5 inside the 14-wide band → ~2u neighbour spacing at full capacity.
    public static Vector3 RollOutPoint(int index, int count)
    {
        int row = index % 2;                              // alternate front/back row
        int k = index / 2;                                // slot within the row
        int rowCount = (count + (row == 0 ? 1 : 0)) / 2;  // row 0 takes the odd extra
        float x = rowCount <= 1 ? 0f : Mathf.Lerp(-6.5f, 6.5f, k / (float)(rowCount - 1));
        float z = row == 0 ? 2.2f : -2.2f;
        return new Vector3(x, 37.6f, z);
    }

    // LMS: deterministic shuffle of the top-layer tile indices (all clients share WebBridge.Seed),
    // then each player takes its own slot. Assumes UnityEngine.Random was seeded by the caller.
    public static int LmsTileIndex(int spawnIndex, int childCount)
    {
        if (childCount <= 0) return 0;
        int[] idx = new int[childCount];
        for (int i = 0; i < childCount; i++) idx[i] = i;
        for (int i = childCount - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (idx[i], idx[j]) = (idx[j], idx[i]);
        }
        return idx[Mathf.Clamp(spawnIndex, 0, childCount - 1)];
    }
}
