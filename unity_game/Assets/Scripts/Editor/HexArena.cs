using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

/// <summary>
/// Generates a Fall-Guys "Hex-A-Gone" arena: a hexagonal honeycomb of hex-prism tiles, stacked in
/// colored layers. Each tile carries a HexTile (vanish-on-step) component. Editor tool, Undo-aware.
/// </summary>
public static class HexArena
{
    [MenuItem("Tools/Course/Generate Hex Arena")]
    public static void GenerateDefault()
    {
        Color[] cols = new Color[]
        {
            new Color(0.96f, 0.36f, 0.42f), // coral
            new Color(0.98f, 0.70f, 0.25f), // amber
            new Color(0.25f, 0.72f, 0.70f), // teal
            new Color(0.58f, 0.46f, 0.88f), // violet
            new Color(0.45f, 0.80f, 0.42f), // green
        };
        Build(new Vector3(-180f, 12f, 20f), 8, 5, 1.265f, 0.35f, 15.0f, cols);
    }

    public static GameObject Build(Vector3 topCenter, int rings, int layers, float R, float thickness, float spacingY, Color[] colors)
    {
        Mesh hexMesh = GetHexMesh(R, thickness);

        var root = new GameObject("HexArena");
        Undo.RegisterCreatedObjectUndo(root, "Generate Hex Arena");
        root.transform.position = topCenter;

        int tileCount = 0;
        for (int k = 0; k < layers; k++)
        {
            Color baseCol = colors[k % colors.Length];
            Material mat = GetLayerMaterial(k, baseCol);
            Color hi = Color.Lerp(baseCol, Color.white, 0.6f);

            var layerGO = new GameObject("Layer_" + k);
            layerGO.transform.SetParent(root.transform, false);
            layerGO.transform.localPosition = new Vector3(0f, -k * spacingY, 0f);

            for (int q = -rings; q <= rings; q++)
            {
                int r1 = Mathf.Max(-rings, -q - rings);
                int r2 = Mathf.Min(rings, -q + rings);
                for (int r = r1; r <= r2; r++)
                {
                    float x = 1.5f * R * q;
                    float z = Mathf.Sqrt(3f) * R * (r + q / 2f);

                    var tile = new GameObject("Hex_" + q + "_" + r);
                    tile.transform.SetParent(layerGO.transform, false);
                    tile.transform.localPosition = new Vector3(x, 0f, z);

                    var mf = tile.AddComponent<MeshFilter>(); mf.sharedMesh = hexMesh;
                    var mr = tile.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
                    var mc = tile.AddComponent<MeshCollider>(); mc.sharedMesh = hexMesh; mc.convex = false;
                    var ht = tile.AddComponent<HexTile>(); ht.highlightColor = hi;
                    tileCount++;
                }
            }
        }

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);
        Debug.Log("[HexArena] Built " + tileCount + " tiles across " + layers + " layers at " + topCenter);
        Selection.activeGameObject = root;
        return root;
    }

    static Mesh GetHexMesh(float R, float thickness)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Meshes")) AssetDatabase.CreateFolder("Assets", "Meshes");
        if (!AssetDatabase.IsValidFolder("Assets/Meshes/Course")) AssetDatabase.CreateFolder("Assets/Meshes", "Course");
        string path = "Assets/Meshes/Course/HexTile.mesh";

        Mesh fresh = BuildHexMesh(R * 0.92f, thickness); // inset so tiles read separate
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(fresh, path);
        }
        else
        {
            existing.Clear();
            EditorUtility.CopySerialized(fresh, existing);
        }
        AssetDatabase.SaveAssets();
        return AssetDatabase.LoadAssetAtPath<Mesh>(path);
    }

    static Mesh BuildHexMesh(float radius, float thickness)
    {
        float h = thickness * 0.5f;
        var verts = new List<Vector3>();
        var tris = new List<int>();

        Vector3[] topC = new Vector3[6];
        Vector3[] botC = new Vector3[6];
        for (int i = 0; i < 6; i++)
        {
            float a = Mathf.Deg2Rad * (60f * i); // flat-top: corner on +X
            float cx = radius * Mathf.Cos(a);
            float cz = radius * Mathf.Sin(a);
            topC[i] = new Vector3(cx, h, cz);
            botC[i] = new Vector3(cx, -h, cz);
        }

        // top cap (normal +Y): wind center,b,a
        int topCenter = verts.Count; verts.Add(new Vector3(0, h, 0));
        int topStart = verts.Count; for (int i = 0; i < 6; i++) verts.Add(topC[i]);
        for (int i = 0; i < 6; i++)
        {
            int a = topStart + i;
            int b = topStart + (i + 1) % 6;
            tris.Add(topCenter); tris.Add(b); tris.Add(a);
        }

        // bottom cap (normal -Y): wind center,a,b
        int botCenter = verts.Count; verts.Add(new Vector3(0, -h, 0));
        int botStart = verts.Count; for (int i = 0; i < 6; i++) verts.Add(botC[i]);
        for (int i = 0; i < 6; i++)
        {
            int a = botStart + i;
            int b = botStart + (i + 1) % 6;
            tris.Add(botCenter); tris.Add(a); tris.Add(b);
        }

        // sides (outward)
        for (int i = 0; i < 6; i++)
        {
            int ni = (i + 1) % 6;
            int t0 = verts.Count; verts.Add(topC[i]);
            int t1 = verts.Count; verts.Add(topC[ni]);
            int b0 = verts.Count; verts.Add(botC[i]);
            int b1 = verts.Count; verts.Add(botC[ni]);
            tris.Add(t0); tris.Add(t1); tris.Add(b1);
            tris.Add(t0); tris.Add(b1); tris.Add(b0);
        }

        var mesh = new Mesh();
        mesh.name = "HexTile";
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    static Material GetLayerMaterial(int k, Color col)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
        if (!AssetDatabase.IsValidFolder("Assets/Materials/Course")) AssetDatabase.CreateFolder("Assets/Materials", "Course");
        string path = "Assets/Materials/Course/Hex_Layer_" + k + ".mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(m, path);
        }
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
        m.color = col;
        AssetDatabase.SaveAssets();
        return m;
    }
}
