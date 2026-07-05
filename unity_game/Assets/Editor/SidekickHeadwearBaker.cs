using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityGLTF;

// Batches Synty Sidekick HEADWEAR (AHED) FBXs into head-local single-submesh meshes (exactly like the
// hairs in SidekickHairBaker), saves them to Resources/Accessories, and exports web/public/models/headwear.glb.
//
// The head-local transform M is DERIVED from the already-shipped Acc_hair_HUMN01.mesh (same as the hair baker):
//   M = the exact affine that turned HUMN01's raw bake into its shipped head-local mesh.
// M depends only on the shared skeleton bind pose, NOT the mesh, so it applies to ANY head-bound Synty part
// (headwear is skinned to the same head bone as the hairs). Same numbers => "same position" as the hairs.
public static class SidekickHeadwearBaker
{
    const string HairFbxDir = "Assets/Synty/SidekickCharacters/Meshes/Species/Humans/"; // moved OUT of Resources so 157 FBX stop shipping in the WebGL build
    const string HeadFbxDir = "Assets/Synty/SidekickCharacters/Meshes/Outfits/Starter/";
    const string ResDir = "Assets/Resources/Accessories/";
    const string GlbDir = "C:/Users/Junius/Desktop/unit_game/web/public/models";

    // (FBX basename in HeadFbxDir, output key) — key drives Acc_head_<key>.mesh + glb node head_<key>.
    static readonly (string fbx, string key)[] Items = new (string, string)[]
    {
        ("SK_FANT_KNGT_17_22AHED_HU01", "warrior"),
        ("SK_HORR_VILN_01_22AHED_HU01", "pumpkin"),
        ("SK_SCFI_CIVL_09_22AHED_HU01", "fox"),
        ("SK_SCFI_CIVL_10_22AHED_HU01", "assassin"),
    };

    [MenuItem("Tools/Bean/Bake Sidekick Headwear")]
    public static void Bake()
    {
        // 1) derive M from HUMN01 hair (raw bake -> shipped head-local mesh) — identical to the hair baker.
        Vector3[] raw01 = BakeRawMesh(HairFbxDir + "SK_HUMN_BASE_01_02HAIR_HU01.fbx")?.vertices;
        Mesh good01 = AssetDatabase.LoadAssetAtPath<Mesh>(ResDir + "Acc_hair_HUMN01.mesh");
        if (raw01 == null || good01 == null) { Debug.LogError("[HeadBake] missing HUMN01 raw or shipped mesh"); return; }
        Vector3[] g01 = good01.vertices;
        if (raw01.Length != g01.Length)
        {
            Debug.LogError("[HeadBake] vertex count mismatch raw=" + raw01.Length + " good=" + g01.Length + " (cannot derive transform)");
            return;
        }
        Matrix4x4 M = SolveAffine(raw01, g01);
        float maxRes = 0f;
        for (int i = 0; i < g01.Length; i++)
            maxRes = Mathf.Max(maxRes, (M.MultiplyPoint3x4(raw01[i]) - g01[i]).magnitude);
        Debug.Log("[HeadBake] derived M, max residual = " + maxRes.ToString("F5"));
        if (maxRes > 0.01f) { Debug.LogError("[HeadBake] residual too large, aborting (correspondence failed)"); return; }

        // Per-item atlas materials: URP/Lit sampling T_Sidekick_<key>.png — a copy of the Synty Starter
        // ColorMap with THAT item's UV cells recolored (pumpkin orange, warrior steel + red plume, ...).
        // Per-item copies because different headwear items share atlas pixels (fox/pumpkin overlap).

        // 2) bake each headwear with M, save head-local single-submesh meshes.
        foreach (var it in Items)
        {
            Mesh m = BakeHeadLocal(HeadFbxDir + it.fbx + ".fbx", M);
            if (m == null) { Debug.LogError("[HeadBake] failed " + it.fbx); continue; }
            string outPath = ResDir + "Acc_head_" + it.key + ".mesh";
            AssetDatabase.DeleteAsset(outPath);
            AssetDatabase.CreateAsset(m, outPath);
            Debug.Log("[HeadBake] saved " + outPath + " verts=" + m.vertexCount + " tris=" + (m.triangles.Length / 3));
        }
        AssetDatabase.SaveAssets();

        // 3) build a root with head_<key> children (identity, head-local) and export headwear.glb.
        GameObject root = new GameObject("SidekickHeadwear");
        foreach (var it in Items)
        {
            Mesh m = AssetDatabase.LoadAssetAtPath<Mesh>(ResDir + "Acc_head_" + it.key + ".mesh");
            if (m == null) continue;
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(ResDir + "SidekickColor_" + it.key + ".mat");
            if (mat == null) { Debug.LogError("[HeadBake] missing " + ResDir + "SidekickColor_" + it.key + ".mat"); continue; }
            GameObject go = new GameObject("head_" + it.key);
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }
        var settings = GLTFSettings.GetOrCreateSettings();
        var ctx = new ExportContext(settings);
        var exporter = new GLTFSceneExporter(new Transform[] { root.transform }, ctx);
        exporter.SaveGLB(GlbDir, "headwear");
        Debug.Log("[HeadBake] exported " + GlbDir + "/headwear.glb with " + Items.Length + " headwear");

        Object.DestroyImmediate(root);
        AssetDatabase.Refresh();
        Debug.Log("[HeadBake] DONE");
    }

    // Instantiate the FBX, bake its skinned mesh (BakeMesh applies renderer scale), carry submeshes/topology.
    static Mesh BakeRawMesh(string fbxPath)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (prefab == null) { Debug.LogError("[HeadBake] FBX not found: " + fbxPath); return null; }
        GameObject inst = Object.Instantiate(prefab);
        SkinnedMeshRenderer smr = inst.GetComponentInChildren<SkinnedMeshRenderer>();
        Mesh raw = null;
        if (smr != null && smr.sharedMesh != null)
        {
            raw = new Mesh();
            smr.BakeMesh(raw);
            raw.name = "raw";
            int sc = smr.sharedMesh.subMeshCount;
            raw.subMeshCount = sc;
            for (int s = 0; s < sc; s++)
                raw.SetIndices(smr.sharedMesh.GetIndices(s), smr.sharedMesh.GetTopology(s), s);
            // Synty colors live in the shared ColorMap atlas sampled via UV0 — carry the UVs through
            // (BakeMesh preserves vertex order, so sharedMesh.uv maps 1:1).
            raw.uv = smr.sharedMesh.uv;
        }
        else Debug.LogError("[HeadBake] no SkinnedMeshRenderer in " + fbxPath);
        Object.DestroyImmediate(inst);
        return raw;
    }

    static Mesh BakeHeadLocal(string fbxPath, Matrix4x4 M)
    {
        Mesh raw = BakeRawMesh(fbxPath);
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
        m.indexFormat = nv.Length > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        m.vertices = nv;
        m.uv = raw.uv; // keep the Synty ColorMap atlas UVs (position affine M doesn't touch them)
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
