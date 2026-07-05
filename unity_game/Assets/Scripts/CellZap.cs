using UnityEngine;

/// <summary>
/// Energy-Cell hazard for Roll Out. Replaces RollPusher on the battery cells: instead of plowing the
/// bean along, a touch gives a SLIGHT push-back (a small horizontal shove away from the cell, no
/// ragdoll), spawns a one-shot electric "zap" VFX on the bean, and flashes the cell's blue glass
/// brighter energy-blue for a moment.
///
/// Detection mirrors PendulumHazard/RollPusher: a deterministic Physics.OverlapBox over the cell's own
/// collider each FixedUpdate (a band-swung collider doesn't fire OnCollisionEnter reliably), gated by a
/// per-cell cooldown so one touch doesn't machine-gun the effect.
///
/// Emission flash uses a per-renderer MaterialPropertyBlock so the SHARED cell .mat assets are never
/// mutated (otherwise every cell in the project would stay bright). Put this on the cell object (the one
/// with the BoxCollider); the builder assigns zapVfx.
/// </summary>
public class CellZap : MonoBehaviour
{
    [Header("Push-back (no ragdoll)")]
    [Tooltip("Horizontal shove speed away from the cell, added to the bean's velocity on a hit.")]
    public float pushSpeed = 7f;
    [Tooltip("Small upward pop added on top of the horizontal shove so it reads as a 'bounce off'.")]
    public float upPop = 2.5f;
    [Tooltip("Seconds of immunity after a hit (per cell) so it doesn't re-fire every physics step.")]
    public float cooldown = 0.4f;
    [Tooltip("Extra padding around the cell collider for the contact test.")]
    public float contactSkin = 0.25f;
    public string playerTag = "Player";

    [Header("Slowdown debuff")]
    [Tooltip("Movement-speed multiplier applied to the bean on a zap (0.65 = 35% slower).")]
    public float slowMult = 0.65f;
    [Tooltip("Seconds the slowdown lasts.")]
    public float slowDuration = 2f;

    [Header("Zap VFX")]
    [Tooltip("One-shot electric particle prefab spawned on the bean (assigned by the builder).")]
    public GameObject zapVfx;
    [Tooltip("Seconds before the spawned VFX instance is destroyed.")]
    public float vfxLifetime = 0.8f;

    [Header("Glass flash")]
    [Tooltip("Energy-blue the cell emission flashes TO on a hit (multiplied by flashIntensity). Also tints the zap VFX.")]
    public Color flashColor = new Color(0.05f, 0.1f, 1.0f);
    [Tooltip("HDR multiplier on the flash colour.")]
    public float flashIntensity = 4f;
    [Tooltip("Seconds for the flash to fade back to the cell's base emission.")]
    public float flashFade = 0.5f;

    private Collider hazardCol;
    private float cdTimer;
    private float flash;                 // 1 right after a hit -> 0 as it fades
    private Renderer[] rends;
    private Color[] baseEmission;
    private MaterialPropertyBlock mpb;
    private static readonly Collider[] _hits = new Collider[16];
    private static readonly int EmissionID = Shader.PropertyToID("_EmissionColor");

    void Awake()
    {
        hazardCol = GetComponent<Collider>();

        rends = GetComponentsInChildren<Renderer>();
        baseEmission = new Color[rends.Length];
        for (int i = 0; i < rends.Length; i++)
        {
            var sm = rends[i].sharedMaterial;
            baseEmission[i] = (sm != null && sm.HasProperty(EmissionID)) ? sm.GetColor(EmissionID) : Color.black;
        }
        mpb = new MaterialPropertyBlock();
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        if (hazardCol == null || dt <= 0f) return;
        if (cdTimer > 0f) { cdTimer -= dt; return; }

        Bounds b = hazardCol.bounds;
        int n = Physics.OverlapBoxNonAlloc(b.center, b.extents + Vector3.one * contactSkin, _hits,
                                           Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        for (int h = 0; h < n; h++)
        {
            Collider col = _hits[h];
            if (col == null || !col.CompareTag(playerTag)) continue;
            CharacterControls cc = col.GetComponentInParent<CharacterControls>();
            if (cc == null) continue;
            Rigidbody brb = cc.GetComponent<Rigidbody>();
            if (brb == null || brb.isKinematic) continue; // ragdolling/teleport -> leave alone

            // SLIGHT PUSH-BACK: horizontal shove from the cell toward the bean, + a small up pop.
            Vector3 dir = col.bounds.center - b.center; dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) dir = -transform.forward; // degenerate fallback
            dir.Normalize();
            Vector3 v = brb.linearVelocity;
            v += dir * pushSpeed;
            if (v.y < upPop) v.y = upPop;
            brb.linearVelocity = v;

            // SLOWDOWN: makes it harder to walk back against the rolling log for a couple seconds.
            cc.ApplySlow(slowMult, slowDuration);

            // ZAP VFX on the bean's contact point
            if (zapVfx != null)
            {
                Vector3 at = hazardCol.ClosestPoint(col.bounds.center);
                var fx = Instantiate(zapVfx, at, Quaternion.LookRotation(dir));
                TintVfx(fx, flashColor); // force the spark color to match the (blue) flash
                Destroy(fx, vfxLifetime);
            }

            flash = 1f;       // kick the glass flash
            cdTimer = cooldown;
            break;
        }
    }

    // Recolor a spawned VFX instance to `tint`. The vfx_Electricity prefab bakes its own (non-blue) color,
    // so we override at runtime: ParticleSystem startColor + ColorOverLifetime, and the particle renderers'
    // tint/emission via a property block (covers additive "_TintColor" and Lit/Unlit "_EmissionColor"/"_BaseColor").
    static readonly int TintColorID = Shader.PropertyToID("_TintColor");
    static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
    void TintVfx(GameObject fx, Color tint)
    {
        var systems = fx.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < systems.Length; i++)
        {
            var main = systems[i].main;
            main.startColor = tint;
            var col = systems[i].colorOverLifetime;
            if (col.enabled)
            {
                var grad = new Gradient();
                grad.SetKeys(
                    new[] { new GradientColorKey(tint, 0f), new GradientColorKey(tint, 1f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
                col.color = new ParticleSystem.MinMaxGradient(grad);
            }
        }
        var prs = fx.GetComponentsInChildren<ParticleSystemRenderer>(true);
        var block = new MaterialPropertyBlock();
        for (int i = 0; i < prs.Length; i++)
        {
            prs[i].GetPropertyBlock(block);
            block.SetColor(TintColorID, tint);
            block.SetColor(EmissionID, tint);
            block.SetColor(BaseColorID, tint);
            prs[i].SetPropertyBlock(block);
        }
    }

    void Update()
    {
        if (rends == null) return;
        // fade the flash and push the (base + flash) emission via a property block (no shared-mat mutation)
        bool dirty = flash > 0f;
        if (flash > 0f) flash = Mathf.Max(0f, flash - Time.deltaTime / Mathf.Max(0.01f, flashFade));

        if (!dirty && flash <= 0f) return; // nothing to write once settled (one final write at flash==0)
        Color add = flashColor * (flashIntensity * flash);
        for (int i = 0; i < rends.Length; i++)
        {
            if (rends[i] == null) continue;
            rends[i].GetPropertyBlock(mpb);
            mpb.SetColor(EmissionID, baseEmission[i] + add);
            rends[i].SetPropertyBlock(mpb);
        }
    }
}
