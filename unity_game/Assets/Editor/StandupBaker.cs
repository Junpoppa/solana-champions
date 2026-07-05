using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

/// <summary>
/// Bakes the Mixamo standup FBX clips onto the bean's GENERIC skeleton, producing transform-curve .anim
/// files (paths "Armature/Hips/...") — the same artifact type as HappyIdle_Bean/Jump_Bean. The bean stays
/// Generic at runtime (so the existing locomotion clips keep working); Humanoid retargeting is used ONLY here
/// at bake time to pose the bean correctly, then we revert party_character back to Generic.
///
/// Why: the standup FBX use Mixamo bone names, so they can't drive the bean as Generic directly, and making
/// the bean Humanoid breaks the Generic locomotion clips (the body sinks). Baking sidesteps both.
///
/// Menu: Tools/Bean/Bake Standup Clips. Run once; it writes StandUpFace_Bean.anim + StandUpBack_Bean.anim and
/// assigns them to the StandUpFace/StandUpBack states in BeanLocomotion.controller.
/// </summary>
public static class StandupBaker
{
    const string PartyChar = "Assets/FREE/Pack_FREE_PartyCharacters/Models/party_character.fbx";
    const string FaceFbx = "Assets/Animations/face_Standing Up.fbx";
    const string BackFbx = "Assets/Animations/back_Standing Up.fbx";
    const string FaceOut = "Assets/Animations/StandUpFace_Bean.anim";
    const string BackOut = "Assets/Animations/StandUpBack_Bean.anim";
    const string Controller = "Assets/Animations/BeanLocomotion.controller";

    // Trim the start of each standup so the baked clip BEGINS already sitting up (the flat-on-the-ground
    // frames are what made the bean look briefly 'bigger'/spread as it started to rise). The reset-bones lerp
    // then eases the heap into this sitting-up pose instead of a flat one. Tune per clip (seconds), re-bake,
    // playtest — and set RagdollController.standFaceDuration/standBackDuration to the new (shorter) lengths
    // printed in the [Bake] log.
    const float FaceStartSeconds = 1.0f;
    const float BackStartSeconds = 1.0f;

    // Bones whose ROTATION the retarget mangles on the stubby bean — we strip the rotation curve so the bone
    // holds its (clean) bind rotation through the whole clip instead of the Mixamo-retargeted pose:
    //   LeftFoot/RightFoot   — retarget twists the soles sideways (feet point forward at bind).
    //   *UpLeg / *Leg        — retarget abducts the thighs -> the bean stands with legs SPLAYED out to the
    //                          sides in an "A" the whole rise; bind = legs straight + parallel.
    //   Head                 — retarget tilts the head up so the bean "looks forward and up"; bind = level.
    // (Positions are KEPT — the known-good idle clip keeps them and stripping them made the legs crooked.)
    static readonly string[] StripRotationLeaves = {
        "LeftFoot", "RightFoot",
        "LeftUpLeg", "RightUpLeg", "LeftLeg", "RightLeg",
        "Head",
    };

    [MenuItem("Tools/Bean/Bake Standup Clips")]
    public static void Bake()
    {
        // 1) standup FBX must be Humanoid (so they retarget onto the bean's humanoid avatar)
        SetRig(FaceFbx, ModelImporterAnimationType.Human);
        SetRig(BackFbx, ModelImporterAnimationType.Human);

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
            if (beanAvatar == null || !beanAvatar.isValid) { Debug.LogError("[Bake] bean humanoid avatar invalid"); return; }

            var beanModel = AssetDatabase.LoadAssetAtPath<GameObject>(PartyChar);
            inst = (GameObject)Object.Instantiate(beanModel);
            inst.transform.position = Vector3.zero;
            var animr = inst.GetComponent<Animator>();
            if (animr == null) animr = inst.AddComponent<Animator>();
            animr.avatar = beanAvatar;
            animr.applyRootMotion = false;

            BakeOne(inst, FaceFbx, FaceOut, FaceStartSeconds);
            BakeOne(inst, BackFbx, BackOut, BackStartSeconds);
        }
        finally
        {
            if (inst != null) Object.DestroyImmediate(inst);
            // 3) ALWAYS revert the bean to its original (Generic) rig so runtime locomotion is unaffected
            SetRig(PartyChar, originalRig);
        }

        // 4) assign baked clips to the controller states
        AssignToController();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Bake] Done. StandUpFace_Bean.anim + StandUpBack_Bean.anim baked (Generic) and assigned.");
    }

    static void BakeOne(GameObject beanInstance, string srcFbx, string outPath, float startTime)
    {
        AnimationClip src = null;
        foreach (var a in AssetDatabase.LoadAllAssetsAtPath(srcFbx))
            if (a is AnimationClip && !a.name.StartsWith("__preview")) src = (AnimationClip)a;
        if (src == null) { Debug.LogError("[Bake] no clip in " + srcFbx); return; }

        var recorder = new GameObjectRecorder(beanInstance);
        recorder.BindComponentsOfType<Transform>(beanInstance, true);

        float fps = 30f;
        float dt = 1f / fps;
        // Sampling starts at startTime but recorder.TakeSnapshot(dt) accumulates from 0, so the OUTPUT clip
        // begins at frame 0 = src@startTime (i.e. the standup already underway). startTime clamped sane.
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

        // Strip the SCALE channels (the extreme-pose Humanoid->bean retarget bakes non-unit bone SCALES that
        // deform the bean once the clip plays — "bigger"; scale must never animate on a skeleton), AND the
        // ROTATION of the bones in StripRotationLeaves (feet, legs, head — the retarget mangles those: feet
        // twist sideways, thighs splay out into an "A", head tilts up). Holding their clean bind rotation keeps
        // the bean upright with straight, parallel legs through the whole rise.
        // Everything else (all positions + the spine/arm rotations that drive the get-up) is KEPT.
        int stripped = 0;
        foreach (var b in AnimationUtility.GetCurveBindings(outClip))
        {
            bool isScale = b.propertyName.StartsWith("m_LocalScale");
            bool isStripRot = false;
            if (b.propertyName.StartsWith("m_LocalRotation"))
                for (int i = 0; i < StripRotationLeaves.Length; i++)
                    if (b.path.EndsWith(StripRotationLeaves[i])) { isStripRot = true; break; }
            if (isScale || isStripRot)
            {
                AnimationUtility.SetEditorCurve(outClip, b, null);
                stripped++;
            }
        }

        var settings = AnimationUtility.GetAnimationClipSettings(outClip);
        settings.loopTime = false;
        AnimationUtility.SetAnimationClipSettings(outClip, settings);
        EditorUtility.SetDirty(outClip);
        Debug.Log("[Bake] " + outPath + " len=" + outClip.length.ToString("F2") + "s (kept positions, stripped " + stripped + " scale + foot-rotation curves)");
    }

    static void AssignToController()
    {
        var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(Controller);
        var face = AssetDatabase.LoadAssetAtPath<AnimationClip>(FaceOut);
        var back = AssetDatabase.LoadAssetAtPath<AnimationClip>(BackOut);
        foreach (var cs in ac.layers[0].stateMachine.states)
        {
            if (cs.state.name == "StandUpFace") cs.state.motion = face;
            if (cs.state.name == "StandUpBack") cs.state.motion = back;
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
