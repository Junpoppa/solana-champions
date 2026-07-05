using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

/// <summary>
/// Bakes the Mixamo "second jump" FBX onto the bean's GENERIC skeleton (transform-curve .anim, paths
/// "Armature/Hips/..."), the same artifact type as Jump_Bean / StandUp*_Bean, then wires a SecondJump
/// state + DoubleJump trigger into BeanLocomotion.controller. The bean stays Generic at runtime; Humanoid
/// retargeting is used ONLY here at bake time (mirrors StandupBaker — see that file for the full rationale).
///
/// Menu: Tools/Bean/Bake Second Jump. Run once. Idempotent: re-running re-bakes the clip and leaves the
/// controller param/state/transitions in place (won't duplicate them).
/// </summary>
public static class SecondJumpBaker
{
    const string PartyChar = "Assets/FREE/Pack_FREE_PartyCharacters/Models/party_character.fbx";
    const string SrcFbx = "Assets/Mixamo/second_jump.fbx";
    const string OutClip = "Assets/Animations/SecondJump_Bean.anim";
    const string Controller = "Assets/Animations/BeanLocomotion.controller";

    // Begin the baked clip at the very start of the source (we want the whole jump, unlike the standups
    // which trim the flat-on-the-ground intro).
    const float StartSeconds = 0f;

    // Bones whose retargeted ROTATION mangles the stubby bean (feet twist, thighs splay, head tilts). EMPTY
    // to start: a jump genuinely uses its leg/foot motion, so we keep it. If a playtest shows the bean
    // splaying/twisting in the air, add the offending leaves here (e.g. "LeftUpLeg","RightUpLeg",...) and
    // re-bake — same mechanism StandupBaker uses.
    static readonly string[] StripRotationLeaves = { };

    // Bones whose retargeted POSITION must be stripped so the clip plays IN PLACE. The Mixamo front-roll
    // travels the Hips forward/around; without stripping it the mesh drifts during the roll then SNAPS back
    // to the capsule origin when the clip ends. We want physics (preserved x/z velocity) to carry the bean,
    // not the animation — so kill the Hips (and the Armature root) translation. Rotation/pose is kept.
    static readonly string[] StripPositionLeaves = { "Hips", "Armature" };

    [MenuItem("Tools/Bean/Bake Second Jump")]
    public static void Bake()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(SrcFbx) == null)
        {
            Debug.LogError("[SecondJump] source FBX not found at " + SrcFbx + " (import it first).");
            return;
        }

        // 1) source FBX must be Humanoid so it retargets onto the bean's humanoid avatar
        SetRig(SrcFbx, ModelImporterAnimationType.Human);

        // 2) temporarily make the bean Humanoid to get a humanoid avatar for retargeting
        var beanImp = (ModelImporter)AssetImporter.GetAtPath(PartyChar);
        var originalRig = beanImp.animationType;
        GameObject inst = null;
        try
        {
            SetRig(PartyChar, ModelImporterAnimationType.Human);
            Avatar beanAvatar = null;
            foreach (var a in AssetDatabase.LoadAllAssetsAtPath(PartyChar))
                if (a is Avatar) beanAvatar = (Avatar)a;
            if (beanAvatar == null || !beanAvatar.isValid) { Debug.LogError("[SecondJump] bean humanoid avatar invalid"); return; }

            var beanModel = AssetDatabase.LoadAssetAtPath<GameObject>(PartyChar);
            inst = (GameObject)Object.Instantiate(beanModel);
            inst.transform.position = Vector3.zero;
            var animr = inst.GetComponent<Animator>();
            if (animr == null) animr = inst.AddComponent<Animator>();
            animr.avatar = beanAvatar;
            animr.applyRootMotion = false;

            BakeOne(inst, SrcFbx, OutClip, StartSeconds);
        }
        finally
        {
            if (inst != null) Object.DestroyImmediate(inst);
            // 3) ALWAYS revert the bean to its original (Generic) rig so runtime locomotion is unaffected
            SetRig(PartyChar, originalRig);
        }

        // 4) wire SecondJump state + DoubleJump trigger into the controller
        WireController();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[SecondJump] Done. SecondJump_Bean.anim baked (Generic) + controller wired (DoubleJump trigger, SecondJump state).");
    }

    static void BakeOne(GameObject beanInstance, string srcFbx, string outPath, float startTime)
    {
        AnimationClip src = null;
        foreach (var a in AssetDatabase.LoadAllAssetsAtPath(srcFbx))
            if (a is AnimationClip && !a.name.StartsWith("__preview")) src = (AnimationClip)a;
        if (src == null) { Debug.LogError("[SecondJump] no clip in " + srcFbx); return; }

        var recorder = new GameObjectRecorder(beanInstance);
        recorder.BindComponentsOfType<Transform>(beanInstance, true);

        float fps = 30f;
        float dt = 1f / fps;
        float start = Mathf.Clamp(startTime, 0f, Mathf.Max(0f, src.length - dt));
        AnimationMode.StartAnimationMode();
        for (float t = start; t <= src.length + 1e-4f; t += dt)
        {
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(beanInstance, src, Mathf.Min(t, src.length)); // retargets onto bean
            AnimationMode.EndSampling();
            recorder.TakeSnapshot(dt);
        }
        AnimationMode.StopAnimationMode();

        var outClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(outPath);
        if (outClip == null) { outClip = new AnimationClip(); AssetDatabase.CreateAsset(outClip, outPath); }
        else outClip.ClearCurves();
        outClip.frameRate = fps;
        recorder.SaveToClip(outClip);

        // Strip SCALE channels always (retarget bakes non-unit bone scales that deform the bean), plus the
        // ROTATION of any bone in StripRotationLeaves (empty by default for the jump — see field comment).
        int stripped = 0;
        foreach (var b in AnimationUtility.GetCurveBindings(outClip))
        {
            bool isScale = b.propertyName.StartsWith("m_LocalScale");
            bool isStripRot = false;
            if (b.propertyName.StartsWith("m_LocalRotation"))
                for (int i = 0; i < StripRotationLeaves.Length; i++)
                    if (b.path.EndsWith(StripRotationLeaves[i])) { isStripRot = true; break; }
            bool isStripPos = false;
            if (b.propertyName.StartsWith("m_LocalPosition"))
                for (int i = 0; i < StripPositionLeaves.Length; i++)
                    if (b.path.Length == 0 || b.path.EndsWith(StripPositionLeaves[i])) { isStripPos = true; break; }
            if (isScale || isStripRot || isStripPos)
            {
                AnimationUtility.SetEditorCurve(outClip, b, null);
                stripped++;
            }
        }

        var settings = AnimationUtility.GetAnimationClipSettings(outClip);
        settings.loopTime = false;
        AnimationUtility.SetAnimationClipSettings(outClip, settings);
        EditorUtility.SetDirty(outClip);
        Debug.Log("[SecondJump] " + outPath + " len=" + outClip.length.ToString("F2") + "s (stripped " + stripped + " scale" + (StripRotationLeaves.Length > 0 ? "+rotation" : "") + " curves)");
    }

    // Adds the DoubleJump trigger, the SecondJump state (with the baked clip), and its two transitions.
    // Idempotent — checks for existing param/state/transition before adding.
    static void WireController()
    {
        var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(Controller);
        if (ac == null) { Debug.LogError("[SecondJump] controller not found at " + Controller); return; }
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(OutClip);

        // Parameter: DoubleJump (trigger)
        bool hasParam = false;
        foreach (var p in ac.parameters) if (p.name == "DoubleJump") { hasParam = true; break; }
        if (!hasParam) ac.AddParameter("DoubleJump", AnimatorControllerParameterType.Trigger);

        // Parameter: InSecondJump (bool) — gates the AnyState->Jump transition so it can't yank us out of
        // the SecondJump state while still airborne (Airborne stays true through the whole second jump).
        bool hasInSecond = false;
        foreach (var p in ac.parameters) if (p.name == "InSecondJump") { hasInSecond = true; break; }
        if (!hasInSecond) ac.AddParameter("InSecondJump", AnimatorControllerParameterType.Bool);

        var sm = ac.layers[0].stateMachine;

        // Guard the existing AnyState->Jump (Airborne==true) transition with InSecondJump==false, so the
        // mid-air second jump isn't immediately overridden back to the Jump state.
        AnimatorState jumpState = null;
        foreach (var cs in sm.states) if (cs.state.name == "Jump") { jumpState = cs.state; break; }
        foreach (var t in sm.anyStateTransitions)
        {
            if (t.destinationState != jumpState) continue;
            bool hasGuard = false;
            foreach (var c in t.conditions) if (c.parameter == "InSecondJump") { hasGuard = true; break; }
            if (!hasGuard) t.AddCondition(AnimatorConditionMode.IfNot, 0f, "InSecondJump");
        }

        // State: SecondJump
        AnimatorState secondJump = null;
        foreach (var cs in sm.states) if (cs.state.name == "SecondJump") { secondJump = cs.state; break; }
        if (secondJump == null)
        {
            secondJump = sm.AddState("SecondJump");
            secondJump.speed = 1f;
        }
        secondJump.motion = clip;

        // Resolve Idle (for the land-exit transition target)
        AnimatorState idle = null;
        foreach (var cs in sm.states) if (cs.state.name == "Idle") { idle = cs.state; break; }

        // AnyState -> SecondJump on DoubleJump trigger
        bool hasAnyToSecond = false;
        foreach (var t in sm.anyStateTransitions)
            if (t.destinationState == secondJump) { hasAnyToSecond = true; break; }
        if (!hasAnyToSecond)
        {
            var t = sm.AddAnyStateTransition(secondJump);
            t.hasExitTime = false;
            t.duration = 0.08f;
            t.canTransitionToSelf = false;
            t.AddCondition(AnimatorConditionMode.If, 0f, "DoubleJump");
        }

        // SecondJump -> Idle when Airborne == false (landed)
        if (idle != null)
        {
            bool hasSecondToIdle = false;
            foreach (var t in secondJump.transitions)
                if (t.destinationState == idle) { hasSecondToIdle = true; break; }
            if (!hasSecondToIdle)
            {
                var t = secondJump.AddTransition(idle);
                t.hasExitTime = false;
                t.duration = 0.12f;
                t.AddCondition(AnimatorConditionMode.IfNot, 0f, "Airborne");
                // Also require InSecondJump==false so an instant double-tap (second jump fires ~1 frame before
                // the bean is airborne) doesn't immediately bail the freshly-entered SecondJump state to Idle.
                t.AddCondition(AnimatorConditionMode.IfNot, 0f, "InSecondJump");
            }
        }

        // SecondJump -> Jump when the roll clip FINISHES but we're still in the air: drop into the normal
        // airborne/fall pose (same as a single jump) instead of freezing on the clip's last frame. The
        // ->Idle transition above (no exit time) still wins if we land mid-roll.
        if (jumpState != null)
        {
            bool hasSecondToJump = false;
            foreach (var t in secondJump.transitions)
                if (t.destinationState == jumpState) { hasSecondToJump = true; break; }
            if (!hasSecondToJump)
            {
                var t = secondJump.AddTransition(jumpState);
                t.hasExitTime = true;
                t.exitTime = 0.85f;
                t.duration = 0.1f;
                t.AddCondition(AnimatorConditionMode.If, 0f, "Airborne");
            }
        }

        EditorUtility.SetDirty(ac);
    }

    static void SetRig(string path, ModelImporterAnimationType type)
    {
        var imp = (ModelImporter)AssetImporter.GetAtPath(path);
        if (imp.animationType != type)
        {
            imp.animationType = type;
            if (type == ModelImporterAnimationType.Human)
                imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.SaveAndReimport();
        }
    }
}
