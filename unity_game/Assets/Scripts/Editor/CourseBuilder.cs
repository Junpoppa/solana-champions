using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Procedurally builds the Fall-Guys-style obstacle course from primitives and wires RaceManager +
/// ordered checkpoints + finish. Re-runnable (Tools/Build Obstacle Course): clears the prior
/// "Course"/"RaceManager", regenerates, repositions the player, wires the camera.
///
/// Layout uses a running cursor (z = forward edge, y = current surface height) so each section is
/// placed relative to the previous one — resize/replace one section and the rest auto-shift.
/// SCALE shrinks the whole course in one knob.
/// </summary>
public static class CourseBuilder
{
    const float COS = 0.97030f; // cos(14°)
    const float SIN = 0.24192f; // sin(14°)
    const float SCALE = 0.8f;   // overall size (1 = old size). Lower = smaller course.
    const float FALL_Y = -8f;

    static readonly Color BLUE = new Color(0.30f, 0.65f, 0.95f);
    static readonly Color CYAN = new Color(0.25f, 0.72f, 0.95f);
    static readonly Color YELLOW = new Color(0.98f, 0.85f, 0.20f);
    static readonly Color ORANGE = new Color(0.98f, 0.55f, 0.20f);
    static readonly Color PINKC = new Color(0.95f, 0.30f, 0.62f);
    static readonly Color PINK = new Color(0.95f, 0.35f, 0.65f);
    static readonly Color PURPLE = new Color(0.55f, 0.30f, 0.80f);
    static readonly Color TEAL = new Color(0.20f, 0.80f, 0.70f);
    static readonly Color WHITE = new Color(0.95f, 0.95f, 0.95f);

    class Cursor { public float z; public float y; }

    static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
    static Mesh s_prism;
    static Mesh s_torus;
    static int s_cp;

    [MenuItem("Tools/Build Obstacle Course")]
    public static void Build()
    {
        mats.Clear();
        s_cp = 0;

        foreach (var n in new[] { "Course", "RaceManager" })
        {
            var old = GameObject.Find(n);
            if (old != null) Object.DestroyImmediate(old);
        }
        foreach (var b in GameObject.FindObjectsByType<BallSpawner>(FindObjectsSortMode.None))
            if (b != null) Object.DestroyImmediate(b.gameObject);

        var root = new GameObject("Course").transform;
        var player = Object.FindFirstObjectByType<PlayerCharacterController>();

        var cur = new Cursor { z = 0f, y = 0f };
        BuildStart(root, player, cur);
        BuildDiskField(root, cur);
        BuildArchGate(root, cur);
        BuildBallRamp(root, cur);
        BuildSpinGauntlet(root, cur);
        BuildBridge(root, cur);
        BuildFinalRampAndFinish(root, cur);

        var rmGo = new GameObject("RaceManager");
        var rm = rmGo.AddComponent<RaceManager>();
        rm.autoConfigurePortals = false;
        rm.fallY = FALL_Y;
        rm.countdownSeconds = 3f;
        if (player != null) rm.player = player;
        var sp = GameObject.Find("StartPoint");
        if (sp != null) rm.startPoint = sp.transform;

        var cam = Object.FindFirstObjectByType<ThirdPersonCamera>();
        if (cam != null && player != null) cam.target = player.transform;

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log("[CourseBuilder] Obstacle course built (SCALE " + SCALE + ").");
    }

    // =========================================================================
    // Sections
    // =========================================================================

    static void BuildStart(Transform root, PlayerCharacterController player, Cursor cur)
    {
        var s = new GameObject("1_Start").transform; s.SetParent(root, false);

        float w = 22f, depth = 8f * SCALE;
        float cz = cur.z + depth * 0.5f;
        Box(s, "StartSlab", new Vector3(0, cur.y - 0.5f, cz), new Vector3(w, 1, depth), Vector3.zero, Mat("plat", BLUE));

        for (int i = 0; i < 3; i++)
        {
            var a = Box(s, "Arrow", new Vector3(0, cur.y + 0.06f, cur.z + 1f + i * 1.6f), new Vector3(0.5f, 0.08f, 1.6f), Vector3.zero, Mat("white", WHITE));
            StripCollider(a);
        }

        var spt = new GameObject("StartPoint").transform; spt.SetParent(s, false);
        spt.position = new Vector3(0, cur.y + 0.2f, cur.z + 2f);

        if (player != null)
        {
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.position = new Vector3(0, cur.y + 1.0f, cur.z + 2f);
            player.transform.rotation = Quaternion.identity;
            if (cc != null) cc.enabled = true;
        }

        Checkpoint(s, new Vector3(0, cur.y + 1f, cur.z + depth - 1f), new Vector3(w - 2, 4, 1.5f));
        cur.z += depth;
    }

    /// <summary>Section 2: packed honeycomb field of spinning disks with triangle gap-fillers and
    /// yellow pillow bumpers, on a solid blue base. Crossed while disks spin and carry you.</summary>
    static void BuildDiskField(Transform root, Cursor cur)
    {
        var s = new GameObject("2_DiskField").transform; s.SetParent(root, false);
        var rng = new System.Random(7777);

        float r = 4.0f;                   // disk radius (much bigger discs)
        float sInRow = 2f * r;            // touching disks across a row
        float rowSpacing = 1.732f * r;    // hex row pitch
        int[] counts = { 4, 5, 4, 3, 4, 5, 4 };
        float top = cur.y;
        float entryZ = cur.z;
        float startZ = cur.z + r - 0.8f;   // first disc overlaps the start slab edge (no base, no gap)

        // checkpoint at the field entrance (respawn anchor)
        var rowXs = new List<float[]>();
        var rowZ = new List<float>();
        float maxHalf = 0f;
        for (int i = 0; i < counts.Length; i++)
        {
            int n = counts[i];
            var xs = new float[n];
            for (int k = 0; k < n; k++) { xs[k] = (k - (n - 1) * 0.5f) * sInRow; maxHalf = Mathf.Max(maxHalf, Mathf.Abs(xs[k])); }
            rowXs.Add(xs);
            rowZ.Add(startZ + i * rowSpacing);
        }

        float fieldLen = (counts.Length - 1) * rowSpacing + 2f * (r + 0.8f);
        float baseW = (maxHalf + r) * 2f + 2f;
        // NO square base — the discs themselves are the floor; gaps are bridged by the triangles,
        // miss them and you fall (fallY respawns at the field-entry checkpoint).

        Checkpoint(s, new Vector3(0, top + 1f, entryZ + 0.6f), new Vector3(baseW - 1, 4, 1.5f));

        // disks
        for (int i = 0; i < counts.Length; i++)
        {
            float deg = (i % 2 == 0) ? 16f : -16f;   // bigger discs → ease spin so carry speed stays fair
            foreach (float x in rowXs[i])
            {
                var holder = Disk(s, "FDisk", new Vector3(x, top - 0.2f, rowZ[i]), 2f * r, deg, withBumpers: false);
                if (rng.NextDouble() < 0.32)
                {
                    float ox = (rng.Next(2) == 0 ? 1f : -1f) * r * 0.45f;
                    Box(holder.transform, "Bumper", new Vector3(ox, 0.5f, 0), new Vector3(r * 0.5f, 0.7f, r * 0.5f), Vector3.zero, Mat("pad", YELLOW));
                }
            }
        }

        // triangle wedges in the gaps between consecutive rows
        for (int i = 0; i < counts.Length - 1; i++)
        {
            float zc = (rowZ[i] + rowZ[i + 1]) * 0.5f;
            foreach (float a in rowXs[i])
                foreach (float b in rowXs[i + 1])
                {
                    if (Mathf.Abs(a - b) < sInRow * 0.75f)
                        Prism(s, new Vector3((a + b) * 0.5f, top - 0.02f, zc), r * 0.55f, (a < b) ? 0f : 180f, Mat("orange", ORANGE));
                }
        }

        cur.z = entryZ + fieldLen;
    }

    static void BuildArchGate(Transform root, Cursor cur)
    {
        var s = new GameObject("3_ArchGate").transform; s.SetParent(root, false);
        float Ld = 6f * SCALE, w = 9f;
        Box(s, "Landing1", new Vector3(0, cur.y - 0.5f, cur.z + Ld * 0.5f), new Vector3(w, 1, Ld), Vector3.zero, Mat("plat", BLUE));

        float az = cur.z + Ld * 0.5f;
        Box(s, "ArchPillarL", new Vector3(-4, cur.y + 3, az), new Vector3(1, 6, 1), Vector3.zero, Mat("purple", PURPLE));
        Box(s, "ArchPillarR", new Vector3(4, cur.y + 3, az), new Vector3(1, 6, 1), Vector3.zero, Mat("purple", PURPLE));
        Box(s, "ArchBar", new Vector3(0, cur.y + 6.3f, az), new Vector3(9, 1, 1), Vector3.zero, Mat("pink", PINK));

        Checkpoint(s, new Vector3(0, cur.y + 1f, cur.z + Ld - 1f), new Vector3(w, 4, 1.5f));
        cur.z += Ld;
    }

    static void BuildBallRamp(Transform root, Cursor cur)
    {
        var s = new GameObject("4_BallRamp").transform; s.SetParent(root, false);
        float L = 19f * SCALE, w = 9f;
        float cz = cur.z + (L * COS) * 0.5f;
        float cy = cur.y + (L * SIN) * 0.5f;

        Box(s, "Ramp", new Vector3(0, cy, cz), new Vector3(w, 1, L), new Vector3(-14, 0, 0), Mat("plat", BLUE));
        Box(s, "RailL", new Vector3(-4.25f, cy + 1f, cz), new Vector3(0.5f, 2, L), new Vector3(-14, 0, 0), Mat("pink", PINK));
        Box(s, "RailR", new Vector3(4.25f, cy + 1f, cz), new Vector3(0.5f, 2, L), new Vector3(-14, 0, 0), Mat("pink", PINK));

        float[,] spots = { { 0.20f, -3 }, { 0.30f, 3 }, { 0.42f, 0 }, { 0.52f, -3 }, { 0.63f, 3 }, { 0.74f, 0 }, { 0.85f, -3 } };
        for (int i = 0; i < spots.GetLength(0); i++)
        {
            float d = spots[i, 0] * L, x = spots[i, 1];
            Box(s, "Cover", new Vector3(x, cur.y + d * SIN + 1.0f, cur.z + d * COS), new Vector3(2, 2, 2), Vector3.zero, Mat("teal", TEAL));
        }

        float topZ = cur.z + L * COS, topY = cur.y + L * SIN;
        Spawner(s, "BallSpawnerA", new Vector3(0, topY + 1.2f, topZ - 1.5f), 1.8f, 5);

        cur.z = topZ; cur.y = topY;
    }

    /// <summary>A short dedicated disk-crossing gauntlet (disks WITH tall bumper pillars), over a
    /// gap — distinct challenge from the big field. Kept short; may be revised later.</summary>
    static void BuildSpinGauntlet(Transform root, Cursor cur)
    {
        var s = new GameObject("5_SpinGauntlet").transform; s.SetParent(root, false);

        float entryLen = 5f * SCALE;
        Box(s, "SpinEntry", new Vector3(0, cur.y - 0.5f, cur.z + entryLen * 0.5f), new Vector3(10, 1, 6), Vector3.zero, Mat("plat", BLUE));
        Checkpoint(s, new Vector3(0, cur.y + 1f, cur.z + 0.8f), new Vector3(10, 4, 1.5f));
        float z = cur.z + entryLen;

        float gap = 4.5f * SCALE;
        for (int i = 0; i < 3; i++)
        {
            float dz = z + gap * (i + 0.5f);
            Disk(s, "GDisk", new Vector3(0, cur.y - 0.2f, dz), 6f, (i % 2 == 0) ? 55f : -55f, withBumpers: true);
        }
        z += gap * 3f;

        float exitLen = 5f * SCALE;
        Box(s, "SpinExit", new Vector3(0, cur.y - 0.5f, z + exitLen * 0.5f), new Vector3(10, 1, 6), Vector3.zero, Mat("plat", BLUE));
        cur.z = z + exitLen;
    }

    static void BuildBridge(Transform root, Cursor cur)
    {
        var s = new GameObject("6_Bridge").transform; s.SetParent(root, false);
        Material[] stripe = { Mat("red", new Color(0.90f, 0.25f, 0.25f)), Mat("green", new Color(0.30f, 0.80f, 0.35f)), Mat("yellow", YELLOW) };

        float seg = 2f * SCALE;
        for (int i = 0; i < 5; i++)
            Box(s, "BridgeSeg", new Vector3(0, cur.y - 0.5f, cur.z + seg * (i + 0.5f)), new Vector3(6, 1, seg), Vector3.zero, stripe[i % 3]);
        float z = cur.z + seg * 5f;

        float gap = 4f * SCALE;
        Disk(s, "BDisk", new Vector3(0, cur.y - 0.2f, z + gap * 0.5f), 5f, -45f, withBumpers: true);
        Disk(s, "BDisk", new Vector3(0, cur.y - 0.2f, z + gap * 1.5f), 5f, 45f, withBumpers: true);
        z += gap * 2f;

        float platLen = 6f * SCALE;
        Box(s, "PlatformC", new Vector3(0, cur.y - 0.5f, z + platLen * 0.5f), new Vector3(10, 1, 6), Vector3.zero, Mat("plat", BLUE));
        Checkpoint(s, new Vector3(0, cur.y + 1f, z + platLen - 1f), new Vector3(10, 4, 1.5f));
        cur.z = z + platLen;
    }

    static void BuildFinalRampAndFinish(Transform root, Cursor cur)
    {
        var s = new GameObject("7_Finish").transform; s.SetParent(root, false);
        float L = 15f * SCALE, w = 9f;
        float cz = cur.z + (L * COS) * 0.5f;
        float cy = cur.y + (L * SIN) * 0.5f;

        Box(s, "FinalRamp", new Vector3(0, cy, cz), new Vector3(w, 1, L), new Vector3(-14, 0, 0), Mat("plat", BLUE));

        float topZ = cur.z + L * COS, topY = cur.y + L * SIN;
        Spawner(s, "BallSpawnerB", new Vector3(0, topY + 1.2f, topZ - 1.5f), 1.6f, 5);

        Box(s, "FinishLanding", new Vector3(0, topY - 0.5f, topZ + 4f), new Vector3(12, 1, 8), Vector3.zero, Mat("plat", BLUE));

        Material[] rainbow = { Mat("pink", PINK), Mat("purple", PURPLE), Mat("orange", ORANGE) };
        float az = topZ + 3f;
        Box(s, "FinPillarL", new Vector3(-5, topY + 3.5f, az), new Vector3(1, 7, 1), Vector3.zero, rainbow[1]);
        Box(s, "FinPillarR", new Vector3(5, topY + 3.5f, az), new Vector3(1, 7, 1), Vector3.zero, rainbow[1]);
        for (int i = 0; i < 3; i++)
            Box(s, "FinBar" + i, new Vector3(0, topY + 7f + i * 0.8f, az), new Vector3(12, 0.8f, 1), Vector3.zero, rainbow[i % 3]);

        Finish(s, "Finish", new Vector3(0, topY + 2f, az), new Vector3(12, 5, 1.5f));
        cur.z = topZ + 8f; cur.y = topY;
    }

    // =========================================================================
    // Primitive + component helpers
    // =========================================================================

    static GameObject Box(Transform parent, string name, Vector3 pos, Vector3 scale, Vector3 euler, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.transform.localEulerAngles = euler;
        if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    /// <summary>Disk = unscaled holder (flat BoxCollider + RotatingPlatform) with a scaled cylinder
    /// visual + pink center, and optional tall bumper pillars. Returns the holder.</summary>
    static GameObject Disk(Transform parent, string name, Vector3 pos, float diameter, float degPerSec, bool withBumpers)
    {
        var holder = new GameObject(name);
        holder.transform.SetParent(parent, false);
        holder.transform.localPosition = pos;

        var rp = holder.AddComponent<RotatingPlatform>();
        rp.degPerSec = degPerSec;
        rp.radius = diameter * 0.48f;

        var vis = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        vis.name = "DiskVisual";
        vis.transform.SetParent(holder.transform, false);
        vis.transform.localScale = new Vector3(diameter, 0.2f, diameter);
        // round standing collider (replaces the default rounded capsule) — scales with the disk
        var capsule = vis.GetComponent<Collider>();
        if (capsule != null) Object.DestroyImmediate(capsule);
        var mcv = vis.AddComponent<MeshCollider>();
        mcv.sharedMesh = vis.GetComponent<MeshFilter>().sharedMesh;
        mcv.convex = true;
        vis.GetComponent<Renderer>().sharedMaterial = Mat("disk", CYAN);

        // raised tube border ringing the rim
        var border = new GameObject("Border");
        border.transform.SetParent(holder.transform, false);
        border.transform.localPosition = new Vector3(0, 0.2f, 0);
        border.transform.localScale = Vector3.one * (diameter * 0.5f);
        border.AddComponent<MeshFilter>().sharedMesh = TorusMesh();
        border.AddComponent<MeshRenderer>().sharedMaterial = Mat("border", new Color(0.92f, 0.96f, 1f));

        var center = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        center.name = "Center";
        center.transform.SetParent(holder.transform, false);
        center.transform.localPosition = new Vector3(0, 0.22f, 0);
        center.transform.localScale = new Vector3(diameter * 0.22f, 0.18f, diameter * 0.22f);
        StripCollider(center);
        center.GetComponent<Renderer>().sharedMaterial = Mat("pinkc", PINKC);

        if (withBumpers)
        {
            float off = diameter * 0.28f;
            for (int i = 0; i < 2; i++)
            {
                float x = (i == 0) ? off : -off;
                var b = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                b.name = "Bumper";
                b.transform.SetParent(holder.transform, false);
                b.transform.localPosition = new Vector3(x, 1.0f, 0);
                b.transform.localScale = new Vector3(0.7f, 1.4f, 0.7f);
                b.GetComponent<Renderer>().sharedMaterial = Mat("bumper", PINK);
                var c = b.GetComponent<Collider>();
                if (c != null) c.isTrigger = true;
                var hz = b.AddComponent<Hazard>();
                hz.knockbackForce = 14f; hz.upForce = 4f; hz.stunDuration = 0.3f;
            }
        }

        return holder;
    }

    static void Prism(Transform parent, Vector3 pos, float size, float yRot, Material mat)
    {
        var go = new GameObject("Tri");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = new Vector3(size, 0.18f, size);
        go.transform.localEulerAngles = new Vector3(0, yRot, 0);
        go.AddComponent<MeshFilter>().sharedMesh = PrismMesh();
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        var mc = go.AddComponent<MeshCollider>();
        mc.sharedMesh = PrismMesh(); mc.convex = true;
    }

    static void Spawner(Transform parent, string name, Vector3 pos, float interval, int maxAlive)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        var bs = go.AddComponent<BallSpawner>();
        bs.interval = interval;
        bs.maxAlive = maxAlive;
        bs.launchVelocity = new Vector3(0, -2f, -6f);
        bs.ballMats = new[]
        {
            Mat("ballRed", new Color(0.90f, 0.30f, 0.45f)),
            Mat("ballTan", new Color(0.92f, 0.80f, 0.62f)),
            Mat("ballDark", new Color(0.25f, 0.25f, 0.30f)),
        };
    }

    static void Checkpoint(Transform parent, Vector3 pos, Vector3 size)
    {
        var go = new GameObject("CP" + (s_cp + 1));
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        var bc = go.AddComponent<BoxCollider>();
        bc.isTrigger = true; bc.size = size;
        go.AddComponent<global::Checkpoint>().index = ++s_cp;
    }

    static void Finish(Transform parent, string name, Vector3 pos, Vector3 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        var bc = go.AddComponent<BoxCollider>();
        bc.isTrigger = true; bc.size = size;
        go.AddComponent<FinishLine>();
    }

    static void StripCollider(GameObject go)
    {
        var c = go.GetComponent<Collider>();
        if (c != null) Object.DestroyImmediate(c);
    }

    // =========================================================================
    // Triangular-prism mesh (cached as asset so it survives play/reload)
    // =========================================================================

    static Mesh PrismMesh()
    {
        if (s_prism != null) return s_prism;

        const string dir = "Assets/Meshes/Course";
        if (!AssetDatabase.IsValidFolder("Assets/Meshes")) AssetDatabase.CreateFolder("Assets", "Meshes");
        if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets/Meshes", "Course");
        const string path = dir + "/PrismTri.asset";

        var m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (m == null)
        {
            m = new Mesh { name = "PrismTri" };
            const float a = 1f, h = 1f, s3 = 0.8660254f;
            Vector3 p0 = new Vector3(0, 0, a), p1 = new Vector3(-a * s3, 0, -a * 0.5f), p2 = new Vector3(a * s3, 0, -a * 0.5f);
            Vector3 up = new Vector3(0, h, 0);
            m.vertices = new[] { p0, p1, p2, p0 + up, p1 + up, p2 + up };
            m.triangles = new[]
            {
                0,2,1,          // bottom
                3,4,5,          // top
                0,1,4, 0,4,3,   // side p0-p1
                1,2,5, 1,5,4,   // side p1-p2
                2,0,3, 2,3,5,   // side p2-p0
            };
            m.RecalculateNormals();
            m.RecalculateBounds();
            AssetDatabase.CreateAsset(m, path);
        }
        s_prism = m;
        return m;
    }

    /// <summary>Torus (tube ring) in the XZ plane, major radius 1, tube radius ~0.07. Cached asset.</summary>
    static Mesh TorusMesh()
    {
        if (s_torus != null) return s_torus;

        const string dir = "Assets/Meshes/Course";
        if (!AssetDatabase.IsValidFolder("Assets/Meshes")) AssetDatabase.CreateFolder("Assets", "Meshes");
        if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets/Meshes", "Course");
        const string path = dir + "/Torus.asset";

        var m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (m == null)
        {
            const int N = 32, M = 12;     // major / minor segments
            const float R = 1f, t = 0.07f;
            var verts = new Vector3[N * M];
            for (int i = 0; i < N; i++)
            {
                float th = (Mathf.PI * 2f * i) / N;
                Vector3 ringDir = new Vector3(Mathf.Cos(th), 0f, Mathf.Sin(th));
                Vector3 ringCenter = ringDir * R;
                for (int j = 0; j < M; j++)
                {
                    float ph = (Mathf.PI * 2f * j) / M;
                    Vector3 off = ringDir * (Mathf.Cos(ph) * t) + Vector3.up * (Mathf.Sin(ph) * t);
                    verts[i * M + j] = ringCenter + off;
                }
            }
            var tris = new int[N * M * 6];
            int ti = 0;
            for (int i = 0; i < N; i++)
                for (int j = 0; j < M; j++)
                {
                    int i2 = (i + 1) % N, j2 = (j + 1) % M;
                    int a = i * M + j, b = i2 * M + j, c = i2 * M + j2, d = i * M + j2;
                    tris[ti++] = a; tris[ti++] = b; tris[ti++] = c;
                    tris[ti++] = a; tris[ti++] = c; tris[ti++] = d;
                }
            m = new Mesh { name = "Torus" };
            m.vertices = verts;
            m.triangles = tris;
            m.RecalculateNormals();
            m.RecalculateBounds();
            AssetDatabase.CreateAsset(m, path);
        }
        s_torus = m;
        return m;
    }

    // =========================================================================
    // Materials (URP Lit, cached + saved as assets)
    // =========================================================================

    static Material Mat(string key, Color c)
    {
        if (mats.TryGetValue(key, out var cached)) return cached;

        const string dir = "Assets/Materials/Course";
        if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
        if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets/Materials", "Course");

        string path = dir + "/" + key + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            m = new Material(sh);
            AssetDatabase.CreateAsset(m, path);
        }
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        m.color = c;
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.1f);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.1f);

        mats[key] = m;
        return m;
    }
}
