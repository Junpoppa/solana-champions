using UnityEngine;
using UnityEditor;

/// <summary>
/// Editor helpers for building the obstacle course out of POLY STYLE meshes.
/// POLY STYLE prefabs ship with NO colliders, so every walkable mesh needs a MeshCollider.
/// </summary>
public static class CourseKit
{
    [MenuItem("Tools/Course/Add MeshColliders To Selection %#m")]
    public static void AddMeshCollidersToSelection()
    {
        int added = 0;
        foreach (var go in Selection.gameObjects)
            added += AddMeshColliders(go);
        Debug.Log($"[CourseKit] Added {added} MeshCollider(s) to selection.");
    }

    [MenuItem("Tools/Course/Add MeshColliders To 'Course'")]
    public static void AddMeshCollidersToCourse()
    {
        var course = GameObject.Find("Course");
        if (course == null) { Debug.LogWarning("[CourseKit] No 'Course' root found."); return; }
        int added = AddMeshColliders(course);
        Debug.Log($"[CourseKit] Added {added} MeshCollider(s) under 'Course'.");
    }

    /// <summary>
    /// Recursively adds a non-convex MeshCollider (sharedMesh from the MeshFilter) to every
    /// renderable mesh under <paramref name="root"/> that lacks any collider. Returns count added.
    /// Safe to re-run — skips objects that already have a collider.
    /// </summary>
    public static int AddMeshColliders(GameObject root)
    {
        int added = 0;
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            if (mf.GetComponent<Collider>() != null) continue;
            var mc = Undo.AddComponent<MeshCollider>(mf.gameObject);
            mc.sharedMesh = mf.sharedMesh;
            mc.convex = false;
            added++;
        }
        return added;
    }
}
