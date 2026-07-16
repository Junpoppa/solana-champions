using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Procedurally builds the Fall-Guys "Roll Out" log into the open RollOut.unity scene.
/// ONE big horizontal cylinder lying along world X, split into 5 COAXIAL bands (segments along its
/// length), each band an independently-rotating RollDrum with its own candy colour and its own MIX of
/// obstacles (walls / gaps / poles) per BandConfig. The band mesh is a THICK-WALLED tube so gap
/// cut-outs reveal real wall thickness (not a paper sheet). Walls/poles are children of the band, so
/// they orbit up over the top; they PLOW the bean (RollPusher, no ragdoll). Gaps are holes through the
/// mesh (collider matches) the bean falls through. Falling off the rolling edge -> elimination.
/// Menu: Tools/Course/Generate Roll Out.
/// </summary>
public static class RollOutBuilder
{
    const int   BANDS    = 5;
    const float R        = 22f;    // log radius (FATTER — more surface to run/dodge on)
    const float SEG_LEN  = 14f;    // band length along X
    const float CENTER_Y = 14f;    // log axis height (top surface = CENTER_Y + R = 36)
    const int   SEG      = 64;     // radial segments
    const float WALL_THK = 4.5f;   // tube wall thickness (chunky rim at gaps + solid inner wall)

    static readonly float[] SPINS = { 14f, -17f, 15f, -12f, 16f };

    // Per-band angular phase: shifts ALL of a band's obstacles so the authored sections no longer share
    // angles across adjacent bands (distinct, non-multiple values). Spawn band (index 2) = 0 so its
    // authored angles stay where they are and the top stays clear.
    static readonly float[] BAND_PHASE = { 23f, 31f, 0f, 17f, 48f };
    const int   SPAWN_BAND        = 2;
    const float FILL_MIN_SPACING  = 30f;  // min deg between two obstacles in the SAME band
    const float ANTI_ALIGN        = 14f;  // min deg a fill must keep from a NEIGHBOUR band's obstacle
    const float SPAWN_CLEAR       = 28f;  // spawn band: keep this wedge around the top (0deg) empty
    const int   WANT_GAPS         = 2;    // fill each band up to at least this many of each type
    const int   WANT_WALLS        = 1;    // fewer walls
    const int   WANT_CELLS        = 2;
    const int   WANT_HAMMERS      = 1;    // more rolling hammers (>=1 per band, incl. band 0)

    // Explicit, UNEVEN per-band layout. Each obstacle has its own angle, so types interleave around the
    // drum however we author them (gap -> wall -> cells -> gap ...). Not evenly spread.
    enum Kind { Gap, Wall, Cells, Hammer }
    struct Obs { public Kind kind; public float angle; public int count;
        public Obs(Kind k, float a, int c){ kind=k; angle=a; count=c; } }
    static Obs G(float a)        { return new Obs(Kind.Gap, a, 0); }
    static Obs W(float a)        { return new Obs(Kind.Wall, a, 0); }
    static Obs C(float a, int n) { return new Obs(Kind.Cells, a, n); }
    static Obs H(float a)        { return new Obs(Kind.Hammer, a, 0); }
    static readonly Obs[][] PROFILES = {
        new[]{ G(25f), W(70f),  C(130f,3), G(185f), W(255f) },           // Section 1 (Band 0)
        new[]{ G(40f), G(85f),  W(150f),   H(215f), C(300f,3) },         // Section 2 (Band 1)
        new[]{ C(60f,3), W(135f), G(200f), H(290f) },                    // Section 3 (Band 2, spawn: top clear)
        new[]{ G(50f), H(120f), W(175f),   G(250f), C(310f,3) },         // Section 4 (Band 3)
        new[]{ W(45f), G(100f), C(160f,3), G(235f), H(300f) },           // Section 5 (Band 4)
    };

    const float GAP_WIDTH_DEG = 7f;   // jumpable, slightly wider than the first pass (~2.7m arc at R=22)
    const float STRIPE_TOP_DEG = 2.5f; // thin hazard-stripe border painted on the top surface at each gap edge
    const float GAP_OFFSET_DEG = 40f; // keep the top (angle 0 = spawn) clear at build time

    // wall visual = this POLY STYLE prop panel (flat ~2 x 0.23 x 2 mesh) scaled to the wall footprint
    const string PROPS11_PATH = "Assets/POLY STYLE - Platformer Starter Pack/Platformer Starter Pack_URP/Prefabs/Environments/Props_11.prefab";
    // pole obstacles are replaced by this Energy Cell, scaled up from native by CELL_SCALE (bigger cells)
    const string ENERGYCELL_PATH = "Assets/Energy/Type 2/EnergyCell_2/Prefabs/EnergyCell_2_1.prefab";
    const float  CELL_SCALE      = 3.70f; // user-set enlarged cells (baked in so regen doesn't revert them)
    // 360°-revolving hammer (built from the user's reference; Pendulum swapped for Rotator)
    const string HAMMER_PATH = "Assets/Prefabs/RollHammer.prefab";
    // one-shot electric "zap" VFX spawned on the bean when it hits an energy cell
    const string ZAP_VFX_PATH = "Assets/GabrielAguiarProductions/FreeQuickEffectsVol1/Prefabs/vfx_Electricity_01.prefab";
    const float WALL_W = SEG_LEN * 0.85f;
    const float WALL_H = 5.5f;
    const float WALL_T = 0.8f;

    static readonly Color BLUE    = new Color(0.30f, 0.74f, 0.95f);
    static readonly Color MAGENTA = new Color(0.92f, 0.26f, 0.80f);
    static readonly Color YELLOW  = new Color(0.98f, 0.78f, 0.12f);
    static readonly Color[] BANDCOLS = { BLUE, MAGENTA, YELLOW, MAGENTA, BLUE };
    static readonly Color WALLCOL = new Color(0.95f, 0.35f, 0.45f);
    static readonly Color POLECOL = new Color(0.96f, 0.96f, 0.98f);
    static readonly Color OBSCOL  = new Color(0.55f, 0.95f, 0.45f);

    [MenuItem("Tools/Course/Generate Roll Out")]
    public static void Generate()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.name != "RollOut")
        {
            EditorUtility.DisplayDialog("Roll Out", "Open RollOut.unity first (Tools/Scenes / Open Roll Out).", "OK");
            return;
        }

        var old = GameObject.Find("RollOut_Field");
        if (old != null) Object.DestroyImmediate(old);
        var strayProp = GameObject.Find("Props_11"); // the loose reference copy in the scene
        if (strayProp != null && strayProp.transform.parent == null) Object.DestroyImmediate(strayProp);
        var strayHammer = GameObject.Find("Hammer"); // loose reference hammer (now captured as a prefab)
        if (strayHammer != null && strayHammer.transform.parent == null) Object.DestroyImmediate(strayHammer);
        var root = new GameObject("RollOut_Field");

        float halfLen = SEG_LEN * 0.5f;

        Material[] mats = new Material[BANDS];
        for (int i = 0; i < BANDS; i++)
            mats[i] = GetOrCreateFabricMat("RollOut_Band" + i, BANDCOLS[i]);
        Material obsMat = GetOrCreateMat("RollOut_Obstacle", OBSCOL, 0.25f);
        Material hazardMat = GetOrCreateHazardMat(); // yellow/black caution stripes for the gap rim edges

        // Slippery physics material for obstacles (harmless extra; thin poles are the real anti-climb fix).
        var slip = new PhysicsMaterial("RollOut_Slippery")
        { dynamicFriction = 0f, staticFriction = 0f, frictionCombine = PhysicsMaterialCombine.Minimum };

        // expand authored sections -> phased + filled + anti-aligned plans (one list per band)
        List<Obs>[] plans = ComputePlans();

        float totalLen = BANDS * SEG_LEN;
        float x0 = -totalLen * 0.5f + halfLen;
        for (int i = 0; i < BANDS; i++)
        {
            float x = x0 + i * SEG_LEN;
            List<Obs> specs = plans[i];

            // gap arcs come from this band's Gap specs (each GAP_WIDTH_DEG wide at its own angle)
            var gapArcs = new List<Vector2>();
            foreach (var o in specs)
                if (o.kind == Kind.Gap) gapArcs.Add(new Vector2(o.angle - GAP_WIDTH_DEG * 0.5f, o.angle + GAP_WIDTH_DEG * 0.5f));
            Mesh mesh = BuildBandMesh(R, halfLen, SEG, gapArcs.Count > 0 ? gapArcs : null, WALL_THK);
            SaveAsset(mesh, "Assets/Meshes/Course/RollDrumBand_" + i + ".mesh");

            var band = new GameObject("Band_" + i);
            band.transform.SetParent(root.transform, false);
            band.transform.position = new Vector3(x, CENTER_Y, 0f);

            var mf = band.AddComponent<MeshFilter>(); mf.sharedMesh = mesh;
            // submesh 0 = band fabric surface, submesh 1 = gap-rim hazard stripes
            var mr = band.AddComponent<MeshRenderer>(); mr.sharedMaterials = new Material[] { mats[i], hazardMat };
            var mc = band.AddComponent<MeshCollider>(); mc.sharedMesh = mesh; mc.convex = false;
            var rb = band.AddComponent<Rigidbody>(); rb.isKinematic = true;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var rd = band.AddComponent<RollDrum>();
            rd.radius = R; rd.halfLength = halfLen;
            rd.degreesPerSecond = Mathf.Abs(SPINS[i]);
            rd.spinWorldAxis = new Vector3(1f, 0f, 0f);
            rd.spinSign = Mathf.Sign(SPINS[i]);
            rd.carryStrength = 1f;

            // place each obstacle at its own authored angle (Gaps are already cut into the mesh above)
            foreach (var o in specs)
            {
                switch (o.kind)
                {
                    case Kind.Wall:   BuildWall(band.transform, o.angle, obsMat, slip); break;
                    case Kind.Cells:  BuildPoleRow(band.transform, o.angle, o.count, obsMat, slip); break;
                    case Kind.Hammer: BuildHammer(band.transform, o.angle); break;
                }
            }
        }

        // Interior kill volume: a bean that falls THROUGH a gap drops into the hollow tube and would
        // otherwise land on the inner surface (~y=-3.5) and walk around inside. This static trigger fills
        // the hollow interior so falling in = the same elimination as sliding off the edge. Its top (y~28)
        // stays well below the inner-top (31.5) and outer top deck (36), so top-walkers never touch it; and
        // its Z half-width (16 < inner radius 17.5) keeps it inside the pipe. Not a child of a rolling band.
        var killZone = new GameObject("GapKillZone");
        killZone.transform.SetParent(root.transform, false);
        killZone.transform.localPosition = new Vector3(0f, 6f, 0f); // spans y -9 .. 21
        var kbc = killZone.AddComponent<BoxCollider>();
        kbc.isTrigger = true;
        kbc.center = Vector3.zero;
        // z half = 18 > inner radius 17.5 so a bean resting anywhere on the inner wall (incl. the sides) is
        // caught; top (y=21) stays below inner-top 31.5 / outer deck 36 so top-walkers are never triggered.
        kbc.size = new Vector3(totalLen + 4f, 30f, 36f);
        killZone.AddComponent<EliminationZone>();

        Material wallMat = GetOrCreateMat("RollOut_EndWall", WALLCOL, 0.10f);
        Material poleMat = GetOrCreateMat("RollOut_Pole", POLECOL, 0.12f);
        BuildEndWall(root.transform,  totalLen * 0.5f + 0.6f, wallMat, poleMat);
        BuildEndWall(root.transform, -(totalLen * 0.5f + 0.6f), wallMat, poleMat);

        var player = GameObject.Find("Player");
        if (player != null)
            player.transform.position = new Vector3(0f, CENTER_Y + R + 1.6f, 0f);

        var cam = GameObject.Find("Camera Holder");
        if (cam != null && player != null)
            cam.transform.position = player.transform.position + new Vector3(0f, 5f, -12f);

        var elim = GameObject.Find("EliminationZone");
        if (elim != null)
        {
            elim.transform.position = new Vector3(0f, -25f, 0f);
            elim.transform.localScale = Vector3.one;
            var bc = elim.GetComponent<BoxCollider>();
            if (bc != null) { bc.center = Vector3.zero; bc.size = new Vector3(260f, 4f, 260f); }
        }

        Selection.activeGameObject = root;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[RollOutBuilder] Built thick log: 5 bands, mixed obstacles. Saved RollOut.unity.");
    }

    // ---- emerging wall at a given angle: a Props_11 panel scaled to the wall footprint (plow, no ragdoll) ----
    static void BuildWall(Transform band, float angleDeg, Material mat, PhysicsMaterial slip)
    {
        float th = angleDeg * Mathf.Deg2Rad;
        Vector3 radial = new Vector3(0f, Mathf.Cos(th), Mathf.Sin(th)); // band-local = world at build

        // unscaled container holds the SAME collider + plow logic as before (so size/physics are unchanged)
        var wall = new GameObject("Wall_" + Mathf.RoundToInt(angleDeg));
        wall.transform.SetParent(band, false);
        wall.transform.localPosition = radial * (R + WALL_H * 0.5f);
        wall.transform.localRotation = Quaternion.FromToRotation(Vector3.up, radial); // local Y = radial out
        var bc = wall.AddComponent<BoxCollider>();
        bc.size = new Vector3(WALL_W, WALL_H, WALL_T); // X width, Y radial height, Z thin
        bc.sharedMaterial = slip;
        var haz = wall.AddComponent<RollPusher>();
        haz.carryStrength = 1f; haz.contactSkin = 0.6f; haz.blockOver = true; // can't be jumped/run over

        // visual = Props_11 panel (mesh local ~2 x 0.23 x 2) scaled to fill the wall box. Rotate 90° about X
        // so mesh-Z(2)->height, mesh-Y(0.23 thin)->thickness; scale each axis to the wall dims.
        var pf = AssetDatabase.LoadAssetAtPath<GameObject>(PROPS11_PATH);
        if (pf != null)
        {
            var vis = (GameObject)PrefabUtility.InstantiatePrefab(pf);
            vis.name = "Props_11_Visual";
            vis.transform.SetParent(wall.transform, false);
            vis.transform.localPosition = Vector3.zero;
            vis.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            vis.transform.localScale = new Vector3(WALL_W / 2f, WALL_T / 0.23f, WALL_H / 2f);
            foreach (var c in vis.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c); // wall box does physics
        }
        else
        {
            // fallback: plain cube if the prop is missing
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(wall.transform, false);
            cube.transform.localScale = new Vector3(WALL_W, WALL_H, WALL_T);
            cube.GetComponent<MeshRenderer>().sharedMaterial = mat;
            var cc = cube.GetComponent<Collider>(); if (cc != null) Object.DestroyImmediate(cc);
        }
    }

    // ---- a ROW of Energy Cells (NATIVE size) at ONE angle, spread evenly across the band width ----
    static void BuildPoleRow(Transform band, float angleDeg, int count, Material mat, PhysicsMaterial slip)
    {
        float th = angleDeg * Mathf.Deg2Rad;
        Vector3 radial = new Vector3(0f, Mathf.Cos(th), Mathf.Sin(th));
        Quaternion rot = Quaternion.FromToRotation(Vector3.up, radial); // cell stands up on the surface
        float hw = SEG_LEN * 0.4f;

        var pf = AssetDatabase.LoadAssetAtPath<GameObject>(ENERGYCELL_PATH);
        var zapVfx = AssetDatabase.LoadAssetAtPath<GameObject>(ZAP_VFX_PATH);
        // native mesh bounds -> seat the cell base on the surface + size the collider natively
        Bounds mb = new Bounds(new Vector3(0f, 0.475f, 0f), new Vector3(0.25f, 0.95f, 0.26f));
        if (pf != null) { var mf = pf.GetComponentInChildren<MeshFilter>(); if (mf != null && mf.sharedMesh != null) mb = mf.sharedMesh.bounds; }
        float lowestY = (mb.center.y - mb.size.y * 0.5f) * CELL_SCALE;
        float radialDist = R - lowestY; // so the (scaled) cell's base sits on the surface

        for (int k = 0; k < count; k++)
        {
            float t = (count <= 1) ? 0.5f : (float)k / (count - 1);
            float x = Mathf.Lerp(-hw, hw, t);
            GameObject cell = (pf != null) ? (GameObject)PrefabUtility.InstantiatePrefab(pf)
                                           : GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cell.name = "EnergyCell_" + Mathf.RoundToInt(angleDeg) + "_" + k;
            cell.transform.SetParent(band, false);
            cell.transform.localPosition = new Vector3(x, radial.y * radialDist, radial.z * radialDist);
            cell.transform.localRotation = rot;
            cell.transform.localScale *= CELL_SCALE; // bigger cells (collider scales with the transform)

            foreach (var oldc in cell.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(oldc);
            var bc = cell.AddComponent<BoxCollider>();
            bc.center = mb.center; bc.size = mb.size; bc.sharedMaterial = slip; // native-size barrier
            // cells ZAP instead of plow: slight push-back + electric VFX on the bean + glass flash
            var zap = cell.AddComponent<CellZap>();
            zap.zapVfx = zapVfx; zap.contactSkin = 0.2f;
            zap.flashColor = new Color(0.05f, 0.1f, 1f); // deep pure blue (glass flash + VFX tint)
            // punish tuning (explicit so a re-bake keeps it; scene instances serialize these values)
            zap.pushSpeed = 7f; zap.upPop = 2.5f;
            zap.slowMult = 0.6f; zap.slowDuration = 5f;
        }
    }

    // ---- 360°-revolving hammer (rides with the band), seated on the surface at a given angle ----
    static void BuildHammer(Transform band, float angleDeg)
    {
        var pf = AssetDatabase.LoadAssetAtPath<GameObject>(HAMMER_PATH);
        if (pf == null) { Debug.LogWarning("[RollOutBuilder] Hammer prefab missing: " + HAMMER_PATH); return; }
        float th = angleDeg * Mathf.Deg2Rad;
        Vector3 radial = new Vector3(0f, Mathf.Cos(th), Mathf.Sin(th));
        var h = (GameObject)PrefabUtility.InstantiatePrefab(pf);
        h.name = "Hammer_" + Mathf.RoundToInt(angleDeg);
        h.transform.SetParent(band, false);
        // RollOut-only: drop the decorative Gear (user removed it). Prefab is RollOut-exclusive, so safe.
        var gear = h.transform.Find("Gear");
        if (gear != null) Object.DestroyImmediate(gear.gameObject);
        // pivot on the surface; keep the prefab's authored orientation (R_ref) rotated onto the surface
        // normal so it sits "the way it was set" at any angle. Native prefab scale (3.3) is preserved.
        Quaternion rRef = pf.transform.localRotation;
        h.transform.localPosition = radial * R;
        h.transform.localRotation = Quaternion.FromToRotation(Vector3.up, radial) * rRef;
    }

    // ---- candy end-wall + cane poles ----
    static void BuildEndWall(Transform parent, float xPos, Material wallMat, Material poleMat)
    {
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "EndWall_" + (xPos > 0 ? "PosX" : "NegX");
        wall.transform.SetParent(parent, false);
        wall.transform.position = new Vector3(xPos, CENTER_Y + R - 2f, 0f);
        wall.transform.localScale = new Vector3(0.8f, 8f, 2f * R + 2f);
        wall.GetComponent<MeshRenderer>().sharedMaterial = wallMat;

        for (int s = -1; s <= 1; s += 2)
        {
            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pole.name = "Pole_" + (xPos > 0 ? "PosX" : "NegX") + (s > 0 ? "_PosZ" : "_NegZ");
            pole.transform.SetParent(parent, false);
            pole.transform.position = new Vector3(xPos, CENTER_Y + R + 1.5f, s * (R + 1.5f));
            pole.transform.localScale = new Vector3(0.7f, 4f, 0.7f);
            pole.GetComponent<MeshRenderer>().sharedMaterial = poleMat;
            var col = pole.GetComponent<Collider>(); if (col != null) Object.DestroyImmediate(col);
        }
    }

    // ---- build the final per-band obstacle plans: phased authored sections + fills, anti-aligned ----
    static List<Obs>[] ComputePlans()
    {
        var plans = new List<Obs>[BANDS];

        // 1. phase each band's authored obstacles so matching angles don't coincide across bands
        for (int i = 0; i < BANDS; i++)
        {
            plans[i] = new List<Obs>();
            foreach (var o in PROFILES[i])
                plans[i].Add(new Obs(o.kind, Mathf.Repeat(o.angle + BAND_PHASE[i], 360f), o.count));
        }

        // 2. fill the empty arcs so EVERY band ends with a balanced mix: top up each type to its WANT_*
        //    minimum. Wanted obstacles are interleaved (G,W,C,G,W,C...) so the fill stays varied.
        for (int i = 0; i < BANDS; i++)
        {
            var rng = new System.Random(1000 + i);

            int haveG = 0, haveW = 0, haveC = 0, haveH = 0;
            foreach (var o in plans[i])
            {
                if (o.kind == Kind.Wall) haveW++;
                else if (o.kind == Kind.Gap) haveG++;
                else if (o.kind == Kind.Cells) haveC++;
                else if (o.kind == Kind.Hammer) haveH++;
            }
            int needG = Mathf.Max(0, WANT_GAPS - haveG);
            int needW = Mathf.Max(0, WANT_WALLS - haveW);
            int needC = Mathf.Max(0, WANT_CELLS - haveC);
            int needH = Mathf.Max(0, WANT_HAMMERS - haveH);
            var wanted = new List<Kind>();
            int rounds = Mathf.Max(Mathf.Max(needG, needW), Mathf.Max(needC, needH));
            for (int r = 0; r < rounds; r++)
            {
                if (r < needH) wanted.Add(Kind.Hammer);
                if (r < needG) wanted.Add(Kind.Gap);
                if (r < needW) wanted.Add(Kind.Wall);
                if (r < needC) wanted.Add(Kind.Cells);
            }

            // candidate angles every 5deg, shuffled (deterministic per band)
            var cands = new List<float>();
            for (float a = 0f; a < 360f; a += 5f) cands.Add(a);
            for (int k = cands.Count - 1; k > 0; k--) { int j = rng.Next(k + 1); var t = cands[k]; cands[k] = cands[j]; cands[j] = t; }

            foreach (var kind in wanted)
            {
                float chosen = float.NaN;
                foreach (var a in cands) { if (AngleOk(a, i, plans)) { chosen = a; break; } }
                if (float.IsNaN(chosen)) continue;                       // band too full for this kind
                plans[i].Add(new Obs(kind, chosen, kind == Kind.Cells ? 3 : 0));
            }
        }
        return plans;
    }

    // an angle is placeable if it keeps spacing within its band, clears the spawn top, and isn't aligned
    // with a neighbour band's obstacle
    static bool AngleOk(float a, int band, List<Obs>[] plans)
    {
        if (band == SPAWN_BAND && Mathf.Abs(Mathf.DeltaAngle(a, 0f)) < SPAWN_CLEAR) return false;
        foreach (var o in plans[band])
            if (Mathf.Abs(Mathf.DeltaAngle(a, o.angle)) < FILL_MIN_SPACING) return false;
        for (int nb = band - 1; nb <= band + 1; nb += 2)
        {
            if (nb < 0 || nb >= BANDS) continue;
            foreach (var o in plans[nb])
                if (Mathf.Abs(Mathf.DeltaAngle(a, o.angle)) < ANTI_ALIGN) return false;
        }
        return true;
    }

    // ---- parametric gaps: N small arcs spread around the circle, offset to keep the top clear ----
    static List<Vector2> MakeGaps(int count, float widthDeg, float offsetDeg)
    {
        if (count <= 0) return null;
        var list = new List<Vector2>();
        float step = 360f / count;
        for (int i = 0; i < count; i++)
        {
            float center = offsetDeg + i * step;
            list.Add(new Vector2(center - widthDeg * 0.5f, center + widthDeg * 0.5f));
        }
        return list;
    }

    static bool InGap(float deg, List<Vector2> gapArcs)
    {
        if (gapArcs == null) return false;
        for (int i = 0; i < gapArcs.Count; i++)
            if (deg >= gapArcs[i].x && deg <= gapArcs[i].y) return true;
        return false;
    }

    // ---- THICK-WALLED tube along X: outer surface (skip gaps) + rim lips at gap boundaries ----
    static Mesh BuildBandMesh(float radius, float halfLen, int seg, List<Vector2> gapArcs, float wallThk)
    {
        float rIn = radius - wallThk;
        var verts = new List<Vector3>();
        var uvs   = new List<Vector2>();
        // 4 verts per angular step: outer(-x), outer(+x), inner(-x), inner(+x)
        for (int s = 0; s <= seg; s++)
        {
            float ang = (float)s / seg * Mathf.PI * 2f;
            float c = Mathf.Cos(ang), sn = Mathf.Sin(ang);
            verts.Add(new Vector3(-halfLen, c * radius, sn * radius)); uvs.Add(new Vector2((float)s / seg, 0f));
            verts.Add(new Vector3( halfLen, c * radius, sn * radius)); uvs.Add(new Vector2((float)s / seg, 1f));
            verts.Add(new Vector3(-halfLen, c * rIn,   sn * rIn));    uvs.Add(new Vector2((float)s / seg, 0f));
            verts.Add(new Vector3( halfLen, c * rIn,   sn * rIn));    uvs.Add(new Vector2((float)s / seg, 1f));
        }
        var tris = new List<int>();
        for (int s = 0; s < seg; s++)
        {
            float midDeg = (s + 0.5f) / seg * 360f;
            if (InGap(midDeg, gapArcs)) continue; // hole in surface + collider
            int b = s * 4, b2 = (s + 1) * 4;
            int oA = b, oB = b + 1, oA2 = b2, oB2 = b2 + 1;
            Vector3 mid = (verts[oA] + verts[oB] + verts[oA2] + verts[oB2]) * 0.25f;
            Vector3 outward = new Vector3(0f, mid.y, mid.z).normalized;
            AddQuad(verts, tris, oA, oB, oB2, oA2, outward);
            // INNER surface on the same solid span (reuse inner-ring verts), facing inward -> the tube
            // reads as a real thick pipe instead of a hollow shell when seen through a gap.
            int iA = b + 2, iB = b + 3, iA2 = b2 + 2, iB2 = b2 + 3;
            AddQuad(verts, tris, iA, iB, iB2, iA2, -outward);
        }
        // rim lips: at every boundary ring where solid meets gap, connect outer edge -> inner edge.
        // These go into a SEPARATE submesh (1) with their OWN duplicated verts + UVs so the gap edges can
        // wear a distinct caution-stripe material (the ring verts are shared by submesh 0, so they can't
        // carry independent UVs). UV: U tiles along the band width (X), V spans outer(0)->inner(1).
        var rimTris = new List<int>();
        float uMax = (2f * halfLen) / 3f; // ~4-5 stripe repeats across the band width
        for (int s = 0; s <= seg; s++)
        {
            bool gapBefore = (s > 0)   && InGap((s - 0.5f) / seg * 360f, gapArcs);
            bool gapAfter  = (s < seg) && InGap((s + 0.5f) / seg * 360f, gapArcs);
            if (gapBefore == gapAfter) continue;
            int b = s * 4; int oA = b, oB = b + 1, iA = b + 2, iB = b + 3;
            float ang = (float)s / seg * Mathf.PI * 2f;
            Vector3 tangent = new Vector3(0f, -Mathf.Sin(ang), Mathf.Cos(ang)); // along circle
            Vector3 hint = gapAfter ? tangent : -tangent;                       // face into the opening
            AddRimQuad(verts, uvs, rimTris,
                verts[oA], verts[oB], verts[iB], verts[iA],
                new Vector2(0f, 0f), new Vector2(uMax, 0f), new Vector2(uMax, 1f), new Vector2(0f, 1f),
                hint);
        }
        // top-surface hazard border: a thin striped strip flanking BOTH edges of every gap, lying on the
        // outer surface (raised 0.04 to avoid z-fighting the fabric). Same hazard submesh + tiling stripe UVs.
        if (gapArcs != null)
        {
            float rOut = radius + 0.04f;
            foreach (var arc in gapArcs)
            {
                AddTopStripe(verts, uvs, rimTris, halfLen, rOut, arc.x - STRIPE_TOP_DEG, arc.x, 3, uMax);
                AddTopStripe(verts, uvs, rimTris, halfLen, rOut, arc.y, arc.y + STRIPE_TOP_DEG, 3, uMax);
            }
        }
        var m = new Mesh { name = "RollDrumBand" };
        m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt16;
        m.SetVertices(verts); m.SetUVs(0, uvs);
        m.subMeshCount = 2;
        m.SetTriangles(tris, 0);
        m.SetTriangles(rimTris, 1);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    // add a quad with its OWN four (duplicated) vertices + UVs, triangulated into rimTris (submesh 1),
    // auto-oriented so the face normal agrees with hint. Used for the gap-rim hazard-stripe submesh.
    static void AddRimQuad(List<Vector3> verts, List<Vector2> uvs, List<int> rimTris,
                           Vector3 pa, Vector3 pb, Vector3 pc, Vector3 pd,
                           Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, Vector3 hint)
    {
        int a = verts.Count; verts.Add(pa); uvs.Add(ua);
        int b = verts.Count; verts.Add(pb); uvs.Add(ub);
        int c = verts.Count; verts.Add(pc); uvs.Add(uc);
        int d = verts.Count; verts.Add(pd); uvs.Add(ud);
        Vector3 n = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
        if (Vector3.Dot(n, hint) >= 0f)
        { rimTris.Add(a); rimTris.Add(b); rimTris.Add(c); rimTris.Add(a); rimTris.Add(c); rimTris.Add(d); }
        else
        { rimTris.Add(a); rimTris.Add(c); rimTris.Add(b); rimTris.Add(a); rimTris.Add(d); rimTris.Add(c); }
    }

    // thin striped strip lying on the outer surface from startDeg..endDeg (subdivided to follow the curve),
    // emitted into the hazard submesh. U tiles along the band width (X); V spans 0..1 across the strip.
    static void AddTopStripe(List<Vector3> verts, List<Vector2> uvs, List<int> rimTris,
                             float halfLen, float r, float startDeg, float endDeg, int sub, float uMax)
    {
        for (int k = 0; k < sub; k++)
        {
            float t0 = (float)k / sub, t1 = (float)(k + 1) / sub;
            float aL = Mathf.Lerp(startDeg, endDeg, t0) * Mathf.Deg2Rad;
            float aR = Mathf.Lerp(startDeg, endDeg, t1) * Mathf.Deg2Rad;
            float cL = Mathf.Cos(aL), sL = Mathf.Sin(aL), cR = Mathf.Cos(aR), sR = Mathf.Sin(aR);
            Vector3 pa = new Vector3(-halfLen, cL * r, sL * r);
            Vector3 pb = new Vector3( halfLen, cL * r, sL * r);
            Vector3 pc = new Vector3( halfLen, cR * r, sR * r);
            Vector3 pd = new Vector3(-halfLen, cR * r, sR * r);
            float am = Mathf.Lerp(startDeg, endDeg, (t0 + t1) * 0.5f) * Mathf.Deg2Rad;
            Vector3 hint = new Vector3(0f, Mathf.Cos(am), Mathf.Sin(am)); // outward = face up off the surface
            AddRimQuad(verts, uvs, rimTris, pa, pb, pc, pd,
                new Vector2(0f, t0), new Vector2(uMax, t0), new Vector2(uMax, t1), new Vector2(0f, t1), hint);
        }
    }

    // add a quad (corners in ring order a-b-c-d), auto-oriented so its face normal agrees with hint
    static void AddQuad(List<Vector3> v, List<int> tris, int a, int b, int c, int d, Vector3 hint)
    {
        Vector3 n = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
        if (Vector3.Dot(n, hint) >= 0f)
        { tris.Add(a); tris.Add(b); tris.Add(c); tris.Add(a); tris.Add(c); tris.Add(d); }
        else
        { tris.Add(a); tris.Add(c); tris.Add(b); tris.Add(a); tris.Add(d); tris.Add(c); }
    }

    // ---- bean-bag fabric look: matte, no emission, slightly deepened candy colour (soft cloth read) ----
    static Material GetOrCreateFabricMat(string name, Color c)
    {
        string dir = "Assets/RollOut_Materials";
        EnsureFolder(dir);
        string path = dir + "/" + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        Color deep = c * 0.85f; deep.a = 1f;            // deepen candy tones a touch
        mat.SetColor("_BaseColor", deep);
        mat.color = deep;
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_Smoothness", 0.08f);             // matte -> soft bean-bag cloth, not glossy plastic
        mat.DisableKeyword("_EMISSION");
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        mat.SetColor("_EmissionColor", Color.black);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // ---- asset helpers ----
    static Material GetOrCreateMat(string name, Color c, float emissive)
    {
        string dir = "Assets/RollOut_Materials";
        EnsureFolder(dir);
        string path = dir + "/" + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetColor("_BaseColor", c);
        mat.color = c;
        if (emissive > 0f)
        {
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            mat.SetColor("_EmissionColor", c * emissive);
        }
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // ---- caution hazard-stripe material for the gap rim edges (diagonal yellow/black, generated texture) ----
    static Material GetOrCreateHazardMat()
    {
        string dir = "Assets/RollOut_Materials";
        EnsureFolder(dir);
        string texPath = dir + "/HazardStripe.asset";
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        if (tex == null)
        {
            tex = MakeHazardTex();
            AssetDatabase.CreateAsset(tex, texPath);
        }
        string path = dir + "/RollOut_HazardRim.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetTexture("_BaseMap", tex);
        mat.mainTexture = tex;
        mat.SetColor("_BaseColor", Color.white);
        mat.color = Color.white;
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_Smoothness", 0.18f);
        // faint emission so the danger edge reads even in shadow (uses the same stripe map)
        mat.EnableKeyword("_EMISSION");
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        mat.SetTexture("_EmissionMap", tex);
        mat.SetColor("_EmissionColor", new Color(0.35f, 0.30f, 0f));
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // 64x64 diagonal yellow/black caution stripes, tiling (Repeat) — driven by the rim UVs.
    static Texture2D MakeHazardTex()
    {
        const int N = 64;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        tex.name = "HazardStripe";
        Color yellow = new Color(0.98f, 0.82f, 0.05f, 1f);
        Color black  = new Color(0.06f, 0.06f, 0.06f, 1f);
        int band = N / 4; // stripe period in pixels
        var px = new Color[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
                px[y * N + x] = (((x + y) / band) % 2 == 0) ? yellow : black;
        tex.SetPixels(px);
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;
        tex.Apply();
        return tex;
    }

    static void SaveAsset(Object asset, string path)
    {
        EnsureFolder(System.IO.Path.GetDirectoryName(path).Replace("\\", "/"));
        var existing = AssetDatabase.LoadAssetAtPath<Object>(path);
        if (existing != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
    }

    static void EnsureFolder(string dir)
    {
        if (AssetDatabase.IsValidFolder(dir)) return;
        string parent = System.IO.Path.GetDirectoryName(dir).Replace("\\", "/");
        string leaf = System.IO.Path.GetFileName(dir);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }

    // ================= CELL THIN-OUT (additive; NEVER regenerates the level) =================
    // The log carried 30 energy cells (10 rows x 3 across) — cells dominated the obstacle mix. This swaps
    // six of those rows for walls / gaps, leaving 4 rows (12 cells) so a zap reads as a rare punish.
    //
    // Why a separate additive tool instead of re-authoring PROFILES and re-running Generate(): Generate()
    // does DestroyImmediate(RollOut_Field) + SaveScene() with no prompt, which would wipe the user's
    // hand-enlarged cells and all 150 arrows irreversibly (MCP edits bypass Undo). So this operates in
    // place: it deletes ONLY the rows being converted, and rebuilds ONLY the meshes of gap-converted bands.
    //
    // Gaps are holes in the band mesh (not objects), so a gap conversion re-cuts that band's mesh with one
    // extra arc. Everything else on the band (obstacles, arrows) survives a mesh swap untouched.
    // Idempotent: a row that's already been converted is simply reported as done and skipped.
    struct CellSwap
    {
        public int band; public int angle; public Kind to;
        public CellSwap(int b, int a, Kind k) { band = b; angle = a; to = k; }
    }

    // Angles are the LIVE row angles (authored angle + BAND_PHASE, i.e. what ComputePlans emits).
    // Chosen to respect the builder's own placement rules: >= FILL_MIN_SPACING(30) from obstacles in the
    // same band, >= ANTI_ALIGN(14) from neighbour-band obstacles, and SPAWN_CLEAR(28) around band 2's top.
    // Result: 10 cell rows -> 4 kept; walls 5 -> 8; gaps 10 -> 13.
    static readonly CellSwap[] CELL_SWAPS = {
        new CellSwap(0, 153, Kind.Gap),   // band 0 keeps its 310 row
        new CellSwap(1,  20, Kind.Wall),  // band 1 keeps its 331 row
        new CellSwap(2,  90, Kind.Wall),  // band 2 (spawn) keeps its authored 60 row
        new CellSwap(3,  35, Kind.Gap),   // band 3 keeps its 327 row
        new CellSwap(4,  50, Kind.Wall),  // band 4 loses both rows
        new CellSwap(4, 208, Kind.Gap),
    };

    [MenuItem("Tools/Course/Roll Out — Thin Out Cells")]
    public static void ThinOutCells()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.name != "RollOut")
        {
            Debug.LogWarning("[RollOutBuilder] Active scene is '" + scene.name + "', not RollOut — open RollOut first.");
            return;
        }
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[RollOutBuilder] Exit Play mode first — scene edits made in Play are discarded on stop.");
            return;
        }
        var root = GameObject.Find("RollOut_Field");
        if (root == null) { Debug.LogWarning("[RollOutBuilder] no RollOut_Field in the scene"); return; }

        // SAFETY: the band meshes were cut from ComputePlans(), so a gap rebuild is only trustworthy if the
        // plan still describes the live scene. Verify every planned wall/cell/hammer is actually present
        // before touching a single mesh; bail out loudly rather than cutting holes from a stale plan.
        var plans = ComputePlans();
        if (!VerifyPlanMatchesScene(root.transform, plans)) return;

        int cellsRemoved = 0, wallsAdded = 0, gapsAdded = 0;
        var slip = new PhysicsMaterial("RollOut_Slippery")
        { dynamicFriction = 0f, staticFriction = 0f, frictionCombine = PhysicsMaterialCombine.Minimum };
        Material obsMat = GetOrCreateMat("RollOut_Obstacle", OBSCOL, 0.25f);

        // group the requested gap cuts per band so each band's mesh is rebuilt exactly once
        var gapAnglesByBand = new Dictionary<int, List<float>>();

        foreach (var sw in CELL_SWAPS)
        {
            Transform band = root.transform.Find("Band_" + sw.band);
            if (band == null) { Debug.LogWarning("[RollOutBuilder] Band_" + sw.band + " missing — skipped"); continue; }

            int removed = RemoveCellRow(band, sw.angle);
            if (removed == 0)
            {
                Debug.Log("[RollOutBuilder] Band_" + sw.band + " angle " + sw.angle
                          + ": no cell row (already converted?) — skipping");
                continue;
            }
            cellsRemoved += removed;

            if (sw.to == Kind.Wall)
            {
                BuildWall(band, sw.angle, obsMat, slip);
                wallsAdded++;
            }
            else // Kind.Gap — collected, cut below
            {
                if (!gapAnglesByBand.ContainsKey(sw.band)) gapAnglesByBand[sw.band] = new List<float>();
                gapAnglesByBand[sw.band].Add(sw.angle);
                gapsAdded++;
            }
        }

        // re-cut the mesh of every band that gained a gap: existing (verified) arcs + the new ones
        float halfLen = SEG_LEN * 0.5f;
        foreach (var kv in gapAnglesByBand)
        {
            int i = kv.Key;
            Transform band = root.transform.Find("Band_" + i);
            var arcs = GapArcsFor(i, plans);
            foreach (float a in kv.Value)
                arcs.Add(new Vector2(a - GAP_WIDTH_DEG * 0.5f, a + GAP_WIDTH_DEG * 0.5f));

            Mesh mesh = BuildBandMesh(R, halfLen, SEG, arcs, WALL_THK);
            SaveAsset(mesh, "Assets/Meshes/Course/RollDrumBand_" + i + ".mesh");
            // re-point BOTH the visual and the collider at the re-cut mesh (children are untouched)
            var mf = band.GetComponent<MeshFilter>(); if (mf != null) mf.sharedMesh = mesh;
            var mc = band.GetComponent<MeshCollider>(); if (mc != null) mc.sharedMesh = mesh;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[RollOutBuilder] Thin-out done: removed " + cellsRemoved + " cells, added " + wallsAdded
                  + " walls + " + gapsAdded + " gaps (meshes re-cut on " + gapAnglesByBand.Count
                  + " bands). Re-run 'Add Roll Out Arrows' next. Saved RollOut.unity.");
    }

    // Every planned Wall/Cells/Hammer must exist in the scene at its planned angle. Gaps aren't objects, so
    // they can't be checked directly — but if the solid obstacles all line up, the plan that cut the current
    // holes is still valid, which is what a mesh re-cut depends on.
    static bool VerifyPlanMatchesScene(Transform root, List<Obs>[] plans)
    {
        var problems = new List<string>();
        for (int i = 0; i < plans.Length; i++)
        {
            Transform band = root.Find("Band_" + i);
            if (band == null) { problems.Add("Band_" + i + " missing"); continue; }
            foreach (var o in plans[i])
            {
                int a = Mathf.RoundToInt(o.angle);
                if (o.kind == Kind.Wall)
                {
                    if (band.Find("Wall_" + a) == null) problems.Add("Band_" + i + ": expected Wall_" + a);
                }
                else if (o.kind == Kind.Hammer)
                {
                    if (band.Find("Hammer_" + a) == null) problems.Add("Band_" + i + ": expected Hammer_" + a);
                }
                else if (o.kind == Kind.Cells)
                {
                    if (CountCellRow(band, a) == 0) problems.Add("Band_" + i + ": expected EnergyCell row at " + a);
                }
            }
        }
        if (problems.Count > 0)
        {
            Debug.LogError("[RollOutBuilder] ABORTED — the live scene no longer matches ComputePlans(), so the "
                + "existing gap arcs can't be trusted for a mesh re-cut. Fix or re-derive before retrying:\n  "
                + string.Join("\n  ", problems.ToArray()));
            return false;
        }
        return true;
    }

    static int CountCellRow(Transform band, int angle)
    {
        int n = 0;
        string prefix = "EnergyCell_" + angle + "_";
        for (int c = 0; c < band.childCount; c++)
            if (band.GetChild(c).name.StartsWith(prefix)) n++;
        return n;
    }

    static int RemoveCellRow(Transform band, int angle)
    {
        int n = 0;
        string prefix = "EnergyCell_" + angle + "_";
        for (int c = band.childCount - 1; c >= 0; c--)
        {
            var ch = band.GetChild(c);
            if (!ch.name.StartsWith(prefix)) continue;
            Undo.DestroyObjectImmediate(ch.gameObject);
            n++;
        }
        return n;
    }

    // The gap arcs currently cut into band `i`'s mesh, straight from the plan that cut them.
    static List<Vector2> GapArcsFor(int band, List<Obs>[] plans)
    {
        var arcs = new List<Vector2>();
        if (band < 0 || band >= plans.Length) return arcs;
        foreach (var o in plans[band])
            if (o.kind == Kind.Gap) arcs.Add(new Vector2(o.angle - GAP_WIDTH_DEG * 0.5f, o.angle + GAP_WIDTH_DEG * 0.5f));
        // gaps this tool added are already baked into the mesh but not into PROFILES — fold them back in so
        // a second run (or the arrow pass) sees the true hole set.
        foreach (var sw in CELL_SWAPS)
            if (sw.band == band && sw.to == Kind.Gap)
                arcs.Add(new Vector2(sw.angle - GAP_WIDTH_DEG * 0.5f, sw.angle + GAP_WIDTH_DEG * 0.5f));
        return arcs;
    }

    // BuildBandMesh only drops a sector when its MIDPOINT falls inside a gap arc, so a 7° arc becomes a
    // 1- or 2-sector hole (5.6°–11.25°). These are the real hole spans — use them, not the nominal arc.
    static List<Vector2> HoleSpans(List<Vector2> gapArcs)
    {
        var spans = new List<Vector2>();
        for (int s = 0; s < SEG; s++)
        {
            float mid = (s + 0.5f) / SEG * 360f;
            if (InGap(mid, gapArcs)) spans.Add(new Vector2((float)s / SEG * 360f, (float)(s + 1) / SEG * 360f));
        }
        return spans;
    }

    static bool InHole(float deg, List<Vector2> holes)
    {
        float d = Mathf.Repeat(deg, 360f);
        for (int i = 0; i < holes.Count; i++)
            if (d >= holes[i].x && d <= holes[i].y) return true;
        return false;
    }

    static int BandIndexOf(Transform band)
    {
        int idx;
        if (band.name.StartsWith("Band_") && int.TryParse(band.name.Substring(5), out idx)) return idx;
        return -1;
    }

    // ---- roll-direction arrows (additive; run on the EXISTING RollOut scene, does NOT regenerate it) ----
    // Small subtle white chevrons seated flat on each band's surface, pointing along that band's roll
    // direction (so players read which way the roller carries them). They are CHILDREN of the band so they
    // orbit with the roll. Idempotent: clears prior "RollArrow*" children first. Leaves the fabric mat alone.
    [MenuItem("Tools/Course/Add Roll Out Arrows")]
    public static void AddRollOutArrows()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.name != "RollOut")
        {
            Debug.LogWarning("[RollOutBuilder] Active scene is '" + scene.name + "', not RollOut — open RollOut first.");
            return;
        }
        var drums = Object.FindObjectsByType<RollDrum>(FindObjectsSortMode.None);
        if (drums == null || drums.Length == 0) { Debug.LogWarning("[RollOutBuilder] no RollDrum bands found"); return; }

        Material arrowMat = GetOrCreateArrowMat();
        Mesh quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");

        // 10 angles around the drum, 3 chevrons across the width at each → direction reads continuously as the log turns.
        float[] angles = { 18f, 54f, 90f, 126f, 162f, 198f, 234f, 270f, 306f, 342f };
        float[] cols = { -4.2f, 0f, 4.2f };
        int total = 0, skipped = 0;
        var plansForGaps = ComputePlans();
        foreach (var rd in drums)
        {
            Transform band = rd.transform;
            for (int c = band.childCount - 1; c >= 0; c--)
                if (band.GetChild(c).name.StartsWith("RollArrow")) Object.DestroyImmediate(band.GetChild(c).gameObject);

            // An arrow seated over a gap would float above a hole. Skip those: the holes are quantized to
            // whole mesh sectors, so test against the ACTUAL hole spans, not the nominal gap arc.
            int bandIdx = BandIndexOf(band);
            var holes = (bandIdx >= 0) ? HoleSpans(GapArcsFor(bandIdx, plansForGaps)) : new List<Vector2>();

            float rad = rd.radius > 0f ? rd.radius : R;
            float sign = rd.spinSign >= 0f ? 1f : -1f;
            foreach (float a in angles)
            {
                if (InHole(a, holes)) { skipped += cols.Length; continue; }
                float th = a * Mathf.Deg2Rad;
                Vector3 radial = new Vector3(0f, Mathf.Cos(th), Mathf.Sin(th));            // surface normal (out)
                Vector3 tangent = sign * new Vector3(0f, -Mathf.Sin(th), Mathf.Cos(th));   // roll/drag direction
                Quaternion rot = Quaternion.LookRotation(radial, tangent);                 // +Z=out, +Y(arrow up)=roll dir
                foreach (float ox in cols)
                {
                    var go = new GameObject("RollArrow_" + Mathf.RoundToInt(a) + "_" + Mathf.RoundToInt(ox));
                    go.transform.SetParent(band, false);
                    go.transform.localPosition = new Vector3(ox, radial.y * (rad + 0.06f), radial.z * (rad + 0.06f));
                    go.transform.localRotation = rot;
                    go.transform.localScale = new Vector3(1.25f, 2.0f, 1f); // X = width across band, Y = length along the roll
                    var mf = go.AddComponent<MeshFilter>(); mf.sharedMesh = quad;
                    var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = arrowMat;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                    total++;
                }
            }
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[RollOutBuilder] Added " + total + " roll-direction arrows across " + drums.Length + " bands ("
                  + skipped + " skipped over gaps). Saved RollOut.unity.");
    }

    static Material GetOrCreateArrowMat()
    {
        const string matPath = "Assets/Mode3_Materials/M_RollArrow.mat";
        const string texPath = "Assets/Mode3_Materials/T_RollArrow.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (existing != null) return existing;
        EnsureFolder("Assets/Mode3_Materials");

        var tex = MakeArrowTex();
        var oldTex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        if (oldTex != null) AssetDatabase.DeleteAsset(texPath);
        AssetDatabase.CreateAsset(tex, texPath);

        var sh = Shader.Find("Universal Render Pipeline/Unlit");
        var m = new Material(sh);
        m.SetTexture("_BaseMap", tex);
        m.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.5f)); // subtle white
        m.SetFloat("_Surface", 1f);   // Transparent
        m.SetFloat("_Cull", 0f);      // double-sided
        m.SetFloat("_ZWrite", 0f);
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        AssetDatabase.CreateAsset(m, matPath);
        AssetDatabase.SaveAssets();
        return m;
    }

    // White up-pointing arrow glyph (triangle head + shaft) on transparent — drawn procedurally.
    static Texture2D MakeArrowTex()
    {
        const int S = 64;
        var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { name = "T_RollArrow", wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = (x + 0.5f) / S, v = (y + 0.5f) / S; // v points up
                bool on = false;
                if (v >= 0.5f && v <= 0.92f) { float k = (0.92f - v) / 0.42f; if (Mathf.Abs(u - 0.5f) <= 0.34f * k) on = true; } // head
                if (v >= 0.12f && v < 0.5f && Mathf.Abs(u - 0.5f) <= 0.12f) on = true; // shaft
                px[y * S + x] = on ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
            }
        t.SetPixels32(px);
        t.Apply();
        return t;
    }
}
