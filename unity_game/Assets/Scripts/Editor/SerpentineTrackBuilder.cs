using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Generates a Fall-Guys-style serpentine "zigzag" track ribbon: straights joined by rounded 180°
/// U-turns (boustrophedon), gently ascending start→finish, with a colored top deck, colored side
/// rim, and purple support legs underneath. Bare track only — no obstacles. Lives beside the mode-3
/// race track in ObstacleCourse.unity. Re-runnable: deletes the prior "SerpentineTrack" root first.
///
/// Procedural mesh (one mesh, two submeshes: deck + rim/caps/bottom), explicit normals, cached as an
/// asset. Matches the project pattern from HexArena.cs / CourseBuilder.cs. URP Lit materials reused
/// from Assets/CAD_Level/ (CourseDeckBlue / CourseRimCoral / CourseLegMagenta).
/// </summary>
public static class SerpentineTrackBuilder
{
    // ---- Placement (world space of the root) ---------------------------------------------------
    // Existing race track occupies X[-136,-32], Z[-14,190]. We sit parallel on the open +X side.
    static readonly Vector3 ORIGIN = new Vector3(8f, 4f, 40f);

    // ---- Shape knobs ---------------------------------------------------------------------------
    const float WIDE_WIDTH   = 10f;   // wide straights (~10 beans abreast)
    const float NARROW_WIDTH = 3f;    // pinch sections
    const float STRAIGHT_LEN = 26f;   // length of each straight run
    const float UTURN_RADIUS = 8f;    // centerline radius of each 180° U-turn (advance per row = 2*R)
    const int   NUM_STRAIGHTS = 6;    // straight runs (NUM-1 U-turns between them)
    const float TIER_RISE    = 9f;    // total ascent over the whole path (continuous gentle ramp)
    const float DECK_THICK   = 1.2f;  // slab thickness (top deck → bottom)
    const float SAMPLE_STEP  = 1.0f;  // centerline sample spacing (mesh resolution)

    // ---- Legs ----------------------------------------------------------------------------------
    const float LEG_SPACING  = 9f;    // arc-length between leg rows
    const float LEG_RADIUS   = 0.6f;
    const float LEG_BOTTOM_Y = -10f;  // world Y where purple posts end

    // Straights (0-based) that get a mid-run pinch to NARROW_WIDTH.
    static readonly HashSet<int> PINCH_STRAIGHTS = new HashSet<int> { 1, 3 };

    const bool INVERT_WINDING = false; // flip all faces in one knob if they render inside-out

    const string MESH_PATH = "Assets/Meshes/Course/SerpentineTrack.asset";

    // A centerline sample: world-XZ position (y filled in pass 2), width here, cumulative arc length.
    struct Sample { public Vector3 p; public float width; public float arc; }

    [MenuItem("Tools/Course/Generate Serpentine Track")]
    public static void Generate()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.name != "ObstacleCourse")
        {
            EditorUtility.DisplayDialog("Serpentine Track",
                "Active scene is '" + scene.name + "'.\nOpen ObstacleCourse first (Tools/Scenes/Open Obstacle Course Race), then run this.",
                "OK");
            return;
        }

        var old = GameObject.Find("SerpentineTrack");
        if (old != null) Undo.DestroyObjectImmediate(old);

        // -------- Pass 1: build the centerline (XZ) with per-sample width + arc length -----------
        var samples = BuildCenterline();

        // -------- Pass 2: assign ascending Y from arc fraction ----------------------------------
        float total = samples[samples.Count - 1].arc;
        for (int i = 0; i < samples.Count; i++)
        {
            var s = samples[i];
            s.p.y = (total > 0f) ? (s.arc / total) * TIER_RISE : 0f;
            samples[i] = s;
        }

        // -------- Build the ribbon mesh ---------------------------------------------------------
        Mesh mesh = BuildRibbonMesh(samples);
        Mesh asset = SaveMesh(mesh);

        // -------- Assemble GameObjects ----------------------------------------------------------
        var root = new GameObject("SerpentineTrack");
        Undo.RegisterCreatedObjectUndo(root, "Generate Serpentine Track");
        root.transform.position = ORIGIN;

        var ribbon = new GameObject("Ribbon");
        ribbon.transform.SetParent(root.transform, false);
        ribbon.AddComponent<MeshFilter>().sharedMesh = asset;
        var mr = ribbon.AddComponent<MeshRenderer>();
        mr.sharedMaterials = new[] { LoadMat("Assets/CAD_Level/CourseDeckBlue.mat", new Color(0.30f, 0.65f, 0.95f)),
                                     LoadMat("Assets/CAD_Level/CourseRimCoral.mat", new Color(0.96f, 0.40f, 0.45f)) };
        var mc = ribbon.AddComponent<MeshCollider>();
        mc.sharedMesh = asset; mc.convex = false;

        BuildLegs(root.transform, samples);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = root;
        Debug.Log("[SerpentineTrack] Built ribbon (" + samples.Count + " samples, arc " + total.ToString("F1") +
                  "u, rise " + TIER_RISE + "u) at " + ORIGIN + " and saved scene.");
    }

    // ============================================================================================
    // Centerline
    // ============================================================================================
    static List<Sample> BuildCenterline()
    {
        var pts = new List<Vector3>();   // raw XZ points (local, y=0)
        var widths = new List<float>();

        // cursor in local XZ. Heading starts +X. Each straight runs, then a 180° U-turn advances +Z.
        Vector2 pos = Vector2.zero;
        Vector2 dir = new Vector2(1f, 0f);

        for (int sIdx = 0; sIdx < NUM_STRAIGHTS; sIdx++)
        {
            // ---- straight ----
            bool pinch = PINCH_STRAIGHTS.Contains(sIdx);
            int steps = Mathf.Max(1, Mathf.RoundToInt(STRAIGHT_LEN / SAMPLE_STEP));
            for (int k = 0; k <= steps; k++)
            {
                float f = k / (float)steps;                 // 0..1 along this straight
                Vector2 p = pos + dir * (f * STRAIGHT_LEN);
                pts.Add(new Vector3(p.x, 0f, p.y));
                widths.Add(WidthFor(pinch, f));
            }
            pos += dir * STRAIGHT_LEN;

            if (sIdx == NUM_STRAIGHTS - 1) break;           // no U-turn after the last straight

            // ---- 180° U-turn, turning toward +Z (alternating left/right keeps advancing +Z) ----
            // leftNormal of dir = (-dir.y, dir.x). For +X that's +Z, for -X that's -Z. So straights
            // running +X turn LEFT, straights running -X turn RIGHT — both advance +Z.
            bool turnLeft = dir.x > 0f;
            Vector2 nrm = turnLeft ? new Vector2(-dir.y, dir.x) : new Vector2(dir.y, -dir.x);
            Vector2 center = pos + nrm * UTURN_RADIUS;
            Vector2 radial = pos - center;                  // points from center back to current pos
            int arcSteps = Mathf.Max(4, Mathf.RoundToInt((Mathf.PI * UTURN_RADIUS) / SAMPLE_STEP));
            for (int k = 1; k <= arcSteps; k++)
            {
                float ang = (k / (float)arcSteps) * Mathf.PI;       // 0..180°
                float a = turnLeft ? ang : -ang;
                Vector2 r = Rotate(radial, a);
                Vector2 p = center + r;
                pts.Add(new Vector3(p.x, 0f, p.y));
                widths.Add(WIDE_WIDTH);
            }
            pos = center - radial;                          // exit point
            dir = -dir;                                     // heading reversed after 180°
        }

        // cumulative arc length
        var outp = new List<Sample>(pts.Count);
        float arc = 0f;
        for (int i = 0; i < pts.Count; i++)
        {
            if (i > 0) arc += Vector3.Distance(pts[i], pts[i - 1]);
            outp.Add(new Sample { p = pts[i], width = widths[i], arc = arc });
        }
        return outp;
    }

    static float WidthFor(bool pinch, float f)
    {
        if (!pinch) return WIDE_WIDTH;
        // smooth dip centered at the straight's middle
        float pulse = Mathf.Clamp01(1f - Mathf.Abs(f - 0.5f) / 0.22f);
        pulse = Mathf.SmoothStep(0f, 1f, pulse);
        return Mathf.Lerp(WIDE_WIDTH, NARROW_WIDTH, pulse);
    }

    static Vector2 Rotate(Vector2 v, float rad)
    {
        float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }

    // ============================================================================================
    // Ribbon mesh
    // ============================================================================================
    static Mesh BuildRibbonMesh(List<Sample> s)
    {
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var deck = new List<int>();   // submesh 0
        var rim = new List<int>();    // submesh 1 (side walls + end caps + bottom)

        int n = s.Count;
        var L = new Vector3[n]; var R = new Vector3[n];     // top edges
        var Lb = new Vector3[n]; var Rb = new Vector3[n];   // bottom edges
        var tan = new Vector3[n];

        for (int i = 0; i < n; i++)
        {
            Vector3 a = s[Mathf.Max(0, i - 1)].p;
            Vector3 b = s[Mathf.Min(n - 1, i + 1)].p;
            Vector3 t = (b - a); t.y = 0f;
            if (t.sqrMagnitude < 1e-6f) t = Vector3.forward;
            t.Normalize();
            tan[i] = t;
            Vector3 left = new Vector3(-t.z, 0f, t.x);      // in-plane left normal
            float hw = s[i].width * 0.5f;
            Vector3 top = s[i].p;                            // y already set
            L[i] = top + left * hw;
            R[i] = top - left * hw;
            Lb[i] = L[i] + Vector3.down * DECK_THICK;
            Rb[i] = R[i] + Vector3.down * DECK_THICK;
        }

        for (int i = 0; i < n - 1; i++)
        {
            // deck (up)
            AddQuad(verts, norms, deck, L[i], L[i + 1], R[i + 1], R[i], Vector3.up);
            // left wall (outward)
            Vector3 leftOut = (L[i] - s[i].p); leftOut.y = 0f; leftOut.Normalize();
            AddQuad(verts, norms, rim, L[i], L[i + 1], Lb[i + 1], Lb[i], leftOut);
            // right wall (outward)
            Vector3 rightOut = (R[i] - s[i].p); rightOut.y = 0f; rightOut.Normalize();
            AddQuad(verts, norms, rim, R[i], R[i + 1], Rb[i + 1], Rb[i], rightOut);
            // bottom (down)
            AddQuad(verts, norms, rim, Lb[i], Lb[i + 1], Rb[i + 1], Rb[i], Vector3.down);
        }
        // end caps
        AddQuad(verts, norms, rim, L[0], R[0], Rb[0], Lb[0], -tan[0]);
        AddQuad(verts, norms, rim, L[n - 1], R[n - 1], Rb[n - 1], Lb[n - 1], tan[n - 1]);

        var mesh = new Mesh { name = "SerpentineTrack" };
        mesh.indexFormat = (verts.Count > 65000) ? UnityEngine.Rendering.IndexFormat.UInt32
                                                  : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(deck, 0);
        mesh.SetTriangles(rim, 1);
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>Adds a quad a→b→c→d with shading normal n, winding so its front face points to n.</summary>
    static void AddQuad(List<Vector3> v, List<Vector3> nm, List<int> tris,
                        Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n)
    {
        Vector3 rh = Vector3.Cross(b - a, c - a);
        bool flip = Vector3.Dot(rh, n) > 0f;
        if (INVERT_WINDING) flip = !flip;

        int i0 = v.Count;
        v.Add(a); v.Add(b); v.Add(c); v.Add(d);
        nm.Add(n); nm.Add(n); nm.Add(n); nm.Add(n);
        if (!flip)
        {
            tris.Add(i0); tris.Add(i0 + 1); tris.Add(i0 + 2);
            tris.Add(i0); tris.Add(i0 + 2); tris.Add(i0 + 3);
        }
        else
        {
            tris.Add(i0); tris.Add(i0 + 2); tris.Add(i0 + 1);
            tris.Add(i0); tris.Add(i0 + 3); tris.Add(i0 + 2);
        }
    }

    // ============================================================================================
    // Legs
    // ============================================================================================
    static void BuildLegs(Transform root, List<Sample> s)
    {
        var legRoot = new GameObject("Legs");
        legRoot.transform.SetParent(root, false);
        Material mag = LoadMat("Assets/CAD_Level/CourseLegMagenta.mat", new Color(0.62f, 0.30f, 0.78f));

        float nextArc = LEG_SPACING * 0.5f;
        for (int i = 0; i < s.Count; i++)
        {
            if (s[i].arc < nextArc) continue;
            nextArc += LEG_SPACING;

            // perpendicular for the leg pair
            Vector3 a = s[Mathf.Max(0, i - 1)].p, b = s[Mathf.Min(s.Count - 1, i + 1)].p;
            Vector3 t = b - a; t.y = 0f; if (t.sqrMagnitude < 1e-6f) t = Vector3.forward; t.Normalize();
            Vector3 left = new Vector3(-t.z, 0f, t.x);
            float off = s[i].width * 0.3f;

            foreach (float sgn in new[] { 1f, -1f })
            {
                Vector3 footTop = s[i].p + left * (off * sgn) + Vector3.down * DECK_THICK;
                MakeLeg(legRoot.transform, footTop, mag);
            }
        }
    }

    static void MakeLeg(Transform parent, Vector3 topLocal, Material mat)
    {
        // topLocal is in root-local space (root sits at ORIGIN). Leg bottom is world LEG_BOTTOM_Y.
        float worldTopY = ORIGIN.y + topLocal.y;
        float height = Mathf.Max(0.5f, worldTopY - LEG_BOTTOM_Y);
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "Leg";
        go.transform.SetParent(parent, false);
        float midLocalY = topLocal.y - height * 0.5f;
        go.transform.localPosition = new Vector3(topLocal.x, midLocalY, topLocal.z);
        go.transform.localScale = new Vector3(LEG_RADIUS * 2f, height * 0.5f, LEG_RADIUS * 2f); // cyl default h=2
        var col = go.GetComponent<Collider>(); if (col != null) Object.DestroyImmediate(col);
        go.GetComponent<Renderer>().sharedMaterial = mat;
    }

    // ============================================================================================
    // Assets
    // ============================================================================================
    static Mesh SaveMesh(Mesh fresh)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Meshes")) AssetDatabase.CreateFolder("Assets", "Meshes");
        if (!AssetDatabase.IsValidFolder("Assets/Meshes/Course")) AssetDatabase.CreateFolder("Assets/Meshes", "Course");
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(MESH_PATH);
        if (existing == null) { AssetDatabase.CreateAsset(fresh, MESH_PATH); }
        else { existing.Clear(); EditorUtility.CopySerialized(fresh, existing); }
        AssetDatabase.SaveAssets();
        return AssetDatabase.LoadAssetAtPath<Mesh>(MESH_PATH);
    }

    static Material LoadMat(string path, Color fallback)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        m = new Material(sh);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", fallback);
        m.color = fallback;
        Debug.LogWarning("[SerpentineTrack] Material not found at " + path + " — using fallback color.");
        return m;
    }
}
