using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Receiver for messages sent from the JS shell via unityInstance.SendMessage("WebBridge", ...).
// Lives on a GameObject named exactly "WebBridge" in every playable scene (Boot/Course/LastManStanding).
public class WebBridge : MonoBehaviour
{
    // ---- mouse sensitivity ----
    private float pendingSensitivity = -1f;

    // Survives scene swaps (LoadGameScene): the new scene's WebBridge re-applies these in Start so the
    // bean keeps its outfit/sensitivity across modes without depending on JS re-sending after the load.
    private static string s_lastLook = null;
    private static float s_lastSensitivity = -1f;

    // ---- SFX volume (bean jump sound) ----
    // Static so it survives scene swaps and is readable live by BeanWalkDriver. -1 = not yet set → 0.7 default.
    private static float s_lastSfx = -1f;
    public static float SfxVolume => s_lastSfx >= 0f ? s_lastSfx : 0.7f;

    // ---- match config (multiplayer v1) ----
    // Sent by JS right after LoadGameScene; statics survive the scene swap and are read directly by the
    // mode controllers / kill zones (no per-scene re-apply needed).
    [System.Serializable]
    private class MatchConfigData { public string mode; public bool multiplayer; public int seed; public double startAtEpochMs; public string matchId; }
    private static bool s_multiplayer = false;
    private static int s_seed = 0;
    private static string s_mode = null;
    private static string s_matchId = null;
    public static bool Multiplayer => s_multiplayer;
    public static int Seed => s_seed;
    public static string Mode => s_mode;
    public static string MatchId => s_matchId;

    // ---- bean look ----
    [System.Serializable]
    private class BeanLookData
    {
        public string bodyColor;
        public int face;        // index into the web FACES catalog (legacy fallback)
        public string faceTex;  // Unity Resources texture basename under "Materials/Face Images/"
        public string hat;
        public string hair;
        public string glasses;
        public string faceAcc;
    }

    // One catalog entry per web accessory id → how to build it from Resources + its placement on the head.
    private class AccDef
    {
        public string resource;   // Resources path (mesh or prefab)
        public bool isPrefab;     // true = Instantiate prefab; false = build MeshFilter/Renderer from a Mesh
        public string material;   // Resources material name for mesh items ("Color"/"Glass"); null for prefabs
        public Vector3 pos;
        public Vector3 euler;
        public float scale;
        public AccDef(string res, bool prefab, string mat)
        {
            resource = res; isPrefab = prefab; material = mat;
            pos = Vector3.zero; euler = Vector3.zero; scale = 1f;
        }
    }

    // Web item id → catalog entry. Placement (pos/euler/scale) is tuned in-editor; baked meshes start at identity.
    private static readonly Dictionary<string, AccDef> REG = new Dictionary<string, AccDef>
    {
        { "party_hat",     new AccDef("Prefabs/Hats/party hat",   true,  null) },
        { "chef_hat",      new AccDef("Prefabs/Hats/chef hat",    true,  null) },
        { "orange_fedora", new AccDef("Prefabs/Hats/orange fedora", true, null) },
        { "hat_010",       new AccDef("Accessories/Acc_hat_010",        false, "Color") },
        { "hat_single_013",new AccDef("Accessories/Acc_hat_single_013", false, "Color") },
        { "joker",         new AccDef("Accessories/Acc_joker",          false, "Color") },
        { "hair_001",      new AccDef("Accessories/Acc_hair_001",       false, "Color") },
        { "hair_005",      new AccDef("Accessories/Acc_hair_005",       false, "Color") },
        { "hair_006",      new AccDef("Accessories/Acc_hair_006",       false, "Color") },
        // Sidekick "Hair 4" — baked from SK_HUMN_BASE_01_02HAIR. Mesh is HEAD-LOCAL + single-submesh
        // (centered at the customize_objects origin like hair_001), so identity placement is scale-invariant
        // and works on the Course bean (0.8x) the same as the showroom bean. Flat HairColor material.
        { "hair_HUMN01",   new AccDef("Accessories/Acc_hair_HUMN01",    false, "HairColor") },
        // Hair 5..13 — batched from SK_HUMN_BASE_02..10_02HAIR via Tools/Bean/Bake Sidekick Hairs
        // (same head-local transform as Hair 4, so identity placement fits both bean scales).
        // pushed down slightly vs Hair 4 (identity) — matches the web fits in beanPalette.ts.
        { "hair_HUMN02",   new AccDef("Accessories/Acc_hair_HUMN02",    false, "HairColor") { pos = new Vector3(0f, -0.125f, 0f) } },
        { "hair_HUMN03",   new AccDef("Accessories/Acc_hair_HUMN03",    false, "HairColor") { pos = new Vector3(0f, -0.125f, 0f) } },
        { "hair_HUMN04",   new AccDef("Accessories/Acc_hair_HUMN04",    false, "HairColor") { pos = new Vector3(0f, -0.125f, 0f) } },
        { "hair_HUMN05",   new AccDef("Accessories/Acc_hair_HUMN05",    false, "HairColor") { pos = new Vector3(0f, -0.125f, 0f) } },
        { "hair_HUMN06",   new AccDef("Accessories/Acc_hair_HUMN06",    false, "HairColor") { pos = new Vector3(0f, -0.125f, 0f) } },
        { "hair_HUMN07",   new AccDef("Accessories/Acc_hair_HUMN07",    false, "HairColor") { pos = new Vector3(0f, -0.125f, 0f) } },
        { "hair_HUMN08",   new AccDef("Accessories/Acc_hair_HUMN08",    false, "HairColor") { pos = new Vector3(0f, -0.125f, 0f) } },
        { "hair_HUMN09",   new AccDef("Accessories/Acc_hair_HUMN09",    false, "HairColor") { pos = new Vector3(0f, -0.125f, 0f) } },
        { "hair_HUMN10",   new AccDef("Accessories/Acc_hair_HUMN10",    false, "HairColor") { pos = new Vector3(0f, -0.125f, 0f) } },
        // Sidekick HEADWEAR (hat slot) — batched from AHED FBX via Tools/Bean/Bake Sidekick Headwear.
        // Same head-local transform as the hairs (universal to head-bound Synty parts), pushed down -0.125
        // to match the hair batch + the web fits in beanPalette.ts. SidekickColor_<key> = per-item recolored
        // copy of the Synty ColorMap atlas sampled via the meshes' preserved UV0 (real hues, not flat brown).
        { "head_warrior",  new AccDef("Accessories/Acc_head_warrior",   false, "SidekickColor_warrior")  { pos = new Vector3(0f, -0.125f, 0f) } },
        { "head_pumpkin",  new AccDef("Accessories/Acc_head_pumpkin",   false, "SidekickColor_pumpkin")  { pos = new Vector3(0f, -0.125f, 0f) } },
        { "head_fox",      new AccDef("Accessories/Acc_head_fox",       false, "SidekickColor_fox")      { pos = new Vector3(0f, -0.125f, 0f) } },
        { "head_assassin", new AccDef("Accessories/Acc_head_assassin",  false, "SidekickColor_assassin") { pos = new Vector3(0f, -0.125f, 0f) } },
        { "glasses_006",   new AccDef("Accessories/Acc_glasses_006",    false, "Glass") },
        { "clown_nose",    new AccDef("Accessories/Acc_faceacc_clown_nose", false, "Color") },
        { "pacifier",      new AccDef("Accessories/Acc_faceacc_pacifier",   false, "Color") },
    };

    private string pendingLook = null;   // last JSON received; applied once the bean is found
    private bool lookApplied = false;
    private readonly List<GameObject> spawned = new List<GameObject>();
    private Material xrayMaskMat = null;  // Mat_BeanXRayMask, grabbed from the scene's BeanXRayMask renderer

    // ---- inbound messages from JS ----

    public void SetMouseSensitivity(float v)
    {
        pendingSensitivity = v;
        s_lastSensitivity = v;
        ApplySensitivity();
    }

    // Master volume for the bean's jump SFX (BeanWalkDriver reads WebBridge.SfxVolume on each PlayOneShot).
    public void SetSfxVolume(float v) { s_lastSfx = v; }

    public void ApplyLook(string json)
    {
        pendingLook = json;
        s_lastLook = json;
        lookApplied = false;
        TryApplyLook();
    }

    // Switch game modes. Single load swaps the per-scene WebBridge; the new one restores look in Start().
    public void LoadGameScene(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return;
        if (sceneName == "Boot")
        {
            // Back to the DOM lobby/standings — give the real cursor back (gameplay scenes lock it).
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        SceneManager.LoadScene(sceneName);
    }

    // Multiplayer match config (seed + start time + multiplayer flag). Cached statically so the gameplay
    // scene (loaded right after this) reads it via the static accessors.
    public void SetMatchConfig(string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        try
        {
            var d = JsonUtility.FromJson<MatchConfigData>(json);
            if (d != null)
            {
                s_multiplayer = d.multiplayer;
                s_seed = d.seed;
                s_mode = d.mode;
                s_matchId = d.matchId;
            }
        }
        catch (System.Exception e) { Debug.LogWarning("[WebBridge] bad MatchConfig json: " + e.Message); }
    }

    // Synchronized start: JS calls this (with a dummy arg) when the server says all players loaded.
    // Kicks off the (frozen, waiting) IntroCountdown so every client's 3·2·1·GO fires together.
    public void BeginCountdown(string _)
    {
        var ic = FindFirstObjectByType<IntroCountdown>();
        Debug.Log("[WebBridge] BeginCountdown received; IntroCountdown " + (ic != null ? "found" : "NULL"));
        if (ic != null) ic.BeginCountdown();
    }

    void Start()
    {
        // Restore look/sensitivity carried across a scene swap (this WebBridge spawned fresh in the new scene).
        if (pendingSensitivity < 0f && s_lastSensitivity >= 0f) pendingSensitivity = s_lastSensitivity;
        if (pendingLook == null && s_lastLook != null) { pendingLook = s_lastLook; lookApplied = false; }
        ApplySensitivity();
    }

    void Update()
    {
        if (pendingSensitivity >= 0f) ApplySensitivity();
        if (!lookApplied && pendingLook != null) TryApplyLook();
    }

    // ---- sensitivity ----
    void ApplySensitivity()
    {
        if (pendingSensitivity < 0f) return;
        if (CameraManager.singleton != null)
        {
            CameraManager.singleton.mouseSpeed = pendingSensitivity;
            pendingSensitivity = -1f;
        }
    }

    // ---- look ----
    private static Transform FindByName(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var r = FindByName(root.GetChild(i), name);
            if (r != null) return r;
        }
        return null;
    }

    void TryApplyLook()
    {
        var cc = Object.FindObjectOfType<CharacterControls>();
        if (cc == null) return; // player not spawned yet; retry next frame

        // Cache the bean's x-ray MASK material (accessories reuse it so the x-ray FILL won't paint
        // cyan on the body parts they cover). Remote avatars have none → their twins are skipped.
        if (xrayMaskMat == null)
        {
            Transform xm = FindByName(cc.transform, "BeanXRayMask");
            var xr = xm != null ? xm.GetComponent<Renderer>() : null;
            if (xr != null) xrayMaskMat = xr.sharedMaterial;
        }

        if (FindByName(cc.transform, "Character mesh") == null) return; // visual not ready yet
        ApplyLookToBean(cc.transform, pendingLook, spawned, xrayMaskMat);
        lookApplied = true;
    }

    // Apply a BeanLook (JSON) to ANY bean root — the local player OR a remote avatar. Anchors are the
    // "Character mesh" SkinnedMeshRenderer (body=mat0, face=mat1) and the "customize_objects" node.
    // `spawned` holds THIS bean's accessory instances (rebuilt each call); pass xrayMask=null for remotes.
    public static void ApplyLookToBean(Transform beanRoot, string json, List<GameObject> spawned, Material xrayMask = null)
    {
        if (beanRoot == null || string.IsNullOrEmpty(json)) return;
        Transform meshT = FindByName(beanRoot, "Character mesh");
        Transform customize = FindByName(beanRoot, "customize_objects");
        if (meshT == null || customize == null) return;
        var body = meshT.GetComponent<SkinnedMeshRenderer>();
        if (body == null) return;

        BeanLookData d;
        try { d = JsonUtility.FromJson<BeanLookData>(json); }
        catch (System.Exception e) { Debug.LogWarning("[WebBridge] bad BeanLook json: " + e.Message); return; }
        if (d == null) return;

        ApplyBodyAndFace(body, d);
        ApplyAccessories(customize, d, spawned, xrayMask);
    }

    static void ApplyBodyAndFace(SkinnedMeshRenderer body, BeanLookData d)
    {
        var mats = body.materials; // instanced copies
        if (mats.Length > 0 && !string.IsNullOrEmpty(d.bodyColor))
        {
            Color c;
            if (ColorUtility.TryParseHtmlString(d.bodyColor, out c))
            {
                mats[0].color = c;
                if (mats[0].HasProperty("_BaseColor")) mats[0].SetColor("_BaseColor", c);
            }
        }
        if (mats.Length > 1)
        {
            // Build the face from a transparent base material (correct shader/blend) + the chosen face texture.
            // faceTex covers ALL faces (named PNGs in Resources/Materials/Face Images/); fall back to the
            // legacy per-index "face N" material if no texture name was sent.
            var baseFace = Resources.Load<Material>("Materials/Face/face 1");
            Texture faceTex = null;
            if (!string.IsNullOrEmpty(d.faceTex))
                faceTex = Resources.Load<Texture>("Materials/Face Images/" + d.faceTex);

            if (faceTex != null && baseFace != null)
            {
                var faceMat = new Material(baseFace);
                if (faceMat.HasProperty("_BaseMap")) faceMat.SetTexture("_BaseMap", faceTex);
                if (faceMat.HasProperty("_MainTex")) faceMat.SetTexture("_MainTex", faceTex);
                mats[1] = faceMat;
            }
            else
            {
                var faceMat = Resources.Load<Material>("Materials/Face/face " + (d.face + 1));
                if (faceMat != null) mats[1] = faceMat;
            }
        }
        body.materials = mats;
    }

    static void ApplyAccessories(Transform customize, BeanLookData d, List<GameObject> spawned, Material xrayMaskMat)
    {
        for (int i = 0; i < spawned.Count; i++) if (spawned[i] != null) Object.Destroy(spawned[i]);
        spawned.Clear();

        SpawnAccessory(customize, d.hat, spawned, xrayMaskMat);
        SpawnAccessory(customize, d.hair, spawned, xrayMaskMat);
        SpawnAccessory(customize, d.glasses, spawned, xrayMaskMat);
        SpawnAccessory(customize, d.faceAcc, spawned, xrayMaskMat);
    }

    static void SpawnAccessory(Transform customize, string id, List<GameObject> spawned, Material xrayMaskMat)
    {
        if (string.IsNullOrEmpty(id)) return;
        AccDef def;
        if (!REG.TryGetValue(id, out def)) { Debug.LogWarning("[WebBridge] no catalog entry for '" + id + "'"); return; }

        GameObject go;
        if (def.isPrefab)
        {
            var prefab = Resources.Load<GameObject>(def.resource);
            if (prefab == null) { Debug.LogWarning("[WebBridge] missing prefab " + def.resource); return; }
            go = Object.Instantiate(prefab);
        }
        else
        {
            var mesh = Resources.Load<Mesh>(def.resource);
            if (mesh == null) { Debug.LogWarning("[WebBridge] missing mesh " + def.resource); return; }
            go = new GameObject(id);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            var mat = Resources.Load<Material>("Accessories/" + (def.material ?? "Color"));
            if (mat != null) mr.sharedMaterial = mat;
        }

        // Parent to the head node and place. Keep accessories NON-PHYSICAL (no collider, never tagged Player)
        // so beams only ever register the body capsule, and keep them out of the x-ray duplicate renderers.
        var t = go.transform;
        t.SetParent(customize, false);
        t.localPosition = def.pos;
        t.localEulerAngles = def.euler;
        t.localScale = Vector3.one * def.scale;
        go.tag = "Untagged";
        SetLayerRecursive(go, customize.gameObject.layer);
        foreach (var col in go.GetComponentsInChildren<Collider>()) Object.Destroy(col);

        AddXRayMaskTwins(go, xrayMaskMat);
        spawned.Add(go);
    }

    // For each accessory renderer, add an invisible twin that draws the SAME geometry with Mat_BeanXRayMask.
    // That material writes stencil bit 2 (ColorMask 0, queue Transparent+49, before the fill at +50), so the
    // bean's XRayFill skips the pixels the accessory covers — no cyan glow on the head-top/nose under a hat/etc.
    // The twin is a child of the accessory renderer, so it's destroyed with the accessory and inherits its pose.
    static void AddXRayMaskTwins(GameObject go, Material xrayMaskMat)
    {
        if (xrayMaskMat == null) return;
        var rends = go.GetComponentsInChildren<Renderer>(); // snapshot (added twins won't be re-processed)
        foreach (var r in rends)
        {
            if (r.GetComponent<MeshFilter>() == null && !(r is SkinnedMeshRenderer)) continue;
            var twin = new GameObject("xrayMask");
            twin.transform.SetParent(r.transform, false);
            twin.layer = r.gameObject.layer;
            Renderer tr;
            var srcSkinned = r as SkinnedMeshRenderer;
            if (srcSkinned != null)
            {
                var ms = twin.AddComponent<SkinnedMeshRenderer>();
                ms.sharedMesh = srcSkinned.sharedMesh;
                ms.bones = srcSkinned.bones;
                ms.rootBone = srcSkinned.rootBone;
                ms.localBounds = srcSkinned.localBounds;
                tr = ms;
            }
            else
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) { Object.Destroy(twin); continue; }
                twin.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                tr = twin.AddComponent<MeshRenderer>();
            }
            tr.sharedMaterial = xrayMaskMat;
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tr.receiveShadows = false;
        }
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform c in go.transform) SetLayerRecursive(c.gameObject, layer);
    }
}
