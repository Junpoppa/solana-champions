using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityGLTF;

// Batches Synty Sidekick hair FBXs into head-local single-submesh meshes (like the shipped "Hair 4"),
// saves them to Resources/Accessories, and re-exports web/public/models/sidekick.glb with all hairs.
//
// The head-local transform M is DERIVED from the already-shipped Acc_hair_HUMN01.mesh:
//   M = (the exact transform that produced Hair 4 from its raw bake).
// Since all Synty human hair FBXs share the same rig/origin (user confirmed HUMN02 fits with HUMN01's
// numbers), the same M applied to each raw bake yields a correctly-fitted head-local mesh.
public static class SidekickHairBaker
{
    const string FbxDir = "Assets/Synty/SidekickCharacters/Meshes/Species/Humans/"; // moved OUT of Resources so 157 FBX stop shipping in the WebGL build
    const string ResDir = "Assets/Resources/Accessories/";
    const string GlbDir = "C:/Users/Junius/Desktop/unit_game/web/public/models";

    [MenuItem("Tools/Bean/Bake Sidekick Hairs")]
    public static void Bake()
    {
        // 1) derive M from HUMN01 (raw bake of its FBX  ->  shipped head-local mesh)
        Vector3[] raw01 = BakeRawVerts("SK_HUMN_BASE_01_02HAIR_HU01");
        Mesh good01 = AssetDatabase.LoadAssetAtPath<Mesh>(ResDir + "Acc_hair_HUMN01.mesh");
        if (raw01 == null || good01 == null) { Debug.LogError("[HairBake] missing HUMN01 raw or shipped mesh"); return; }
        Vector3[] g01 = good01.vertices;
        if (raw01.Length != g01.Length)
        {
            Debug.LogError("[HairBake] vertex count mismatch raw=" + raw01.Length + " good=" + g01.Length + " (cannot derive transform)");
            return;
        }
        Matrix4x4 M = SolveAffine(raw01, g01);
        float maxRes = 0f;
        for (int i = 0; i < g01.Length; i++)
            maxRes = Mathf.Max(maxRes, (M.MultiplyPoint3x4(raw01[i]) - g01[i]).magnitude);
        Debug.Log("[HairBake] derived M, max residual = " + maxRes.ToString("F5"));
        if (maxRes > 0.01f) { Debug.LogError("[HairBake] residual too large, aborting (correspondence failed)"); return; }

        Material mat = AssetDatabase.LoadAssetAtPath<Material>(ResDir + "HairColor.mat");

        // 2) bake HUMN02..10 with M, save head-local single-submesh meshes
        var ids = new List<int> { 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        foreach (int n in ids)
        {
            string nn = n.ToString("D2");
            string fbx = "SK_HUMN_BASE_" + nn + "_02HAIR_HU01";
            Mesh m = BakeHeadLocal(fbx, M);
            if (m == null) { Debug.LogError("[HairBake] failed " + fbx); continue; }
            string outPath = ResDir + "Acc_hair_HUMN" + nn + ".mesh";
            AssetDatabase.DeleteAsset(outPath);
            AssetDatabase.CreateAsset(m, outPath);
            Debug.Log("[HairBake] saved " + outPath + " verts=" + m.vertexCount + " tris=" + (m.triangles.Length / 3));
        }
        AssetDatabase.SaveAssets();

        // 3) build a root with hair_HUMN01..10 (identity, head-local) and export sidekick.glb
        GameObject root = new GameObject("SidekickHairs");
        for (int n = 1; n <= 10; n++)
        {
            string nn = n.ToString("D2");
            Mesh m = AssetDatabase.LoadAssetAtPath<Mesh>(ResDir + "Acc_hair_HUMN" + nn + ".mesh");
            if (m == null) continue;
            GameObject go = new GameObject("hair_HUMN" + nn);
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }
        var settings = GLTFSettings.GetOrCreateSettings();
        var ctx = new ExportContext(settings);
        var exporter = new GLTFSceneExporter(new Transform[] { root.transform }, ctx);
        exporter.SaveGLB(GlbDir, "sidekick");
        Debug.Log("[HairBake] exported " + GlbDir + "/sidekick.glb with 10 hairs");

        Object.DestroyImmediate(root);
        AssetDatabase.Refresh();
        Debug.Log("[HairBake] DONE");
    }

    // Instantiate the FBX, bake its skinned mesh (BakeMesh applies renderer scale), return raw verts.
    static Vector3[] BakeRawVerts(string fbxName)
    {
        Mesh raw = BakeRawMesh(fbxName);
        return raw == null ? null : raw.vertices;
    }

    static Mesh BakeRawMesh(string fbxName)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FbxDir + fbxName + ".fbx");
        if (prefab == null) { Debug.LogError("[HairBake] FBX not found: " + fbxName); return null; }
        GameObject inst = Object.Instantiate(prefab);
        SkinnedMeshRenderer smr = inst.GetComponentInChildren<SkinnedMeshRenderer>();
        Mesh raw = null;
        if (smr != null && smr.sharedMesh != null)
        {
            raw = new Mesh();
            smr.BakeMesh(raw);
            // carry topology/submeshes for the caller
            raw.name = fbxName + "_raw";
            int sc = smr.sharedMesh.subMeshCount;
            raw.subMeshCount = sc;
            for (int s = 0; s < sc; s++)
                raw.SetIndices(smr.sharedMesh.GetIndices(s), smr.sharedMesh.GetTopology(s), s);
        }
        else Debug.LogError("[HairBake] no SkinnedMeshRenderer in " + fbxName);
        Object.DestroyImmediate(inst);
        return raw;
    }

    static Mesh BakeHeadLocal(string fbxName, Matrix4x4 M)
    {
        Mesh raw = BakeRawMesh(fbxName);
        if (raw == null) return null;
        Vector3[] rv = raw.vertices;
        var nv = new Vector3[rv.Length];
        for (int i = 0; i < rv.Length; i++) nv[i] = M.MultiplyPoint3x4(rv[i]);

        // merge submeshes + triangulate quads into one triangle list
        var tris = new List<int>();
        for (int s = 0; s < raw.subMeshCount; s++)
        {
            int[] idx = raw.GetIndices(s);
            MeshTopology topo = raw.GetTopology(s);
            if (topo == MeshTopology.Quads)
            {
                for (int q = 0; q + 3 < idx.Length; q += 4)
                {
                    int a = idx[q], b = idx[q + 1], c = idx[q + 2], d = idx[q + 3];
                    tris.Add(a); tris.Add(b); tris.Add(c);
                    tris.Add(a); tris.Add(c); tris.Add(d);
                }
            }
            else
            {
                tris.AddRange(idx);
            }
        }

        Mesh m = new Mesh();
        m.name = "Acc_hair_" + fbxName;
        m.indexFormat = nv.Length > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        m.vertices = nv;
        m.subMeshCount = 1;
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    // Least-squares affine fit: find 3x4 M mapping src -> dst (M includes translation).
    static Matrix4x4 SolveAffine(Vector3[] src, Vector3[] dst)
    {
        double[,] ata = new double[4, 4];
        double[] atbx = new double[4], atby = new double[4], atbz = new double[4];
        for (int i = 0; i < src.Length; i++)
        {
            double[] p = { src[i].x, src[i].y, src[i].z, 1.0 };
            for (int r = 0; r < 4; r++)
            {
                for (int c = 0; c < 4; c++) ata[r, c] += p[r] * p[c];
                atbx[r] += p[r] * dst[i].x;
                atby[r] += p[r] * dst[i].y;
                atbz[r] += p[r] * dst[i].z;
            }
        }
        double[] mx = Solve4(ata, atbx);
        double[] my = Solve4(ata, atby);
        double[] mz = Solve4(ata, atbz);
        Matrix4x4 M = Matrix4x4.identity;
        M.m00 = (float)mx[0]; M.m01 = (float)mx[1]; M.m02 = (float)mx[2]; M.m03 = (float)mx[3];
        M.m10 = (float)my[0]; M.m11 = (float)my[1]; M.m12 = (float)my[2]; M.m13 = (float)my[3];
        M.m20 = (float)mz[0]; M.m21 = (float)mz[1]; M.m22 = (float)mz[2]; M.m23 = (float)mz[3];
        return M;
    }

    // Gaussian elimination with partial pivoting on a 4x4 system (copies inputs).
    static double[] Solve4(double[,] aIn, double[] bIn)
    {
        double[,] a = (double[,])aIn.Clone();
        double[] b = (double[])bIn.Clone();
        for (int col = 0; col < 4; col++)
        {
            int piv = col;
            for (int r = col + 1; r < 4; r++)
                if (System.Math.Abs(a[r, col]) > System.Math.Abs(a[piv, col])) piv = r;
            if (piv != col)
            {
                for (int c = 0; c < 4; c++) { double t = a[col, c]; a[col, c] = a[piv, c]; a[piv, c] = t; }
                double tb = b[col]; b[col] = b[piv]; b[piv] = tb;
            }
            double d = a[col, col];
            for (int r = 0; r < 4; r++)
            {
                if (r == col) continue;
                double f = a[r, col] / d;
                for (int c = 0; c < 4; c++) a[r, c] -= f * a[col, c];
                b[r] -= f * b[col];
            }
        }
        double[] x = new double[4];
        for (int i = 0; i < 4; i++) x[i] = b[i] / a[i, i];
        return x;
    }
}
