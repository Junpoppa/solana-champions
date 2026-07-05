using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

/// <summary>
/// One-shot tool that builds a physics ragdoll on the player bean's skeleton, then drops a
/// RagdollController on the Player. Builds it manually (Unity's internal RagdollBuilder wizard can't be
/// instantiated headless), computing every capsule/sphere/joint from world bone positions so it is robust
/// to the bean's tiny bone scale (~0.008) and Blender-imported bone rotations.
///
/// Bodies (11): Hips, Spine1, Head, L/R UpLeg, L/R Leg, L/R Arm, L/R ForeArm. Feet/hands are only used as
/// length references for the lower-leg / forearm capsules. Built kinematic + colliders disabled (the rest
/// "animated" state); RagdollController flips them dynamic on a beam hit.
///
/// Lives in Editor/, so it never ships in the WebGL player build. Menu: Tools/Bean/Build Ragdoll.
/// Re-run safe (aborts if a CharacterJoint already exists). SAVE THE SCENE after running.
/// </summary>
public static class BeanRagdollSetup
{
    [MenuItem("Tools/Bean/Build Ragdoll")]
    public static void Build()
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null) { Debug.LogError("[Ragdoll] No GameObject tagged 'Player' in the open scene."); return; }

        Transform hips = player.transform.Find("BeanModel/Armature/Hips");
        if (hips == null) { Debug.LogError("[Ragdoll] Hips not found at Player/BeanModel/Armature/Hips."); return; }

        if (hips.GetComponentInChildren<CharacterJoint>(true) != null)
        {
            Debug.LogWarning("[Ragdoll] Ragdoll already present (CharacterJoint found). Aborting to avoid duplicates.");
            EnsureController(player);
            return;
        }

        // resolve bones
        Transform spine1 = Find(hips, "Spine1"), head = Find(hips, "Head");
        Transform lUp = Find(hips, "LeftUpLeg"),  lLo = Find(hips, "LeftLeg"),  lFt = Find(hips, "LeftFoot");
        Transform rUp = Find(hips, "RightUpLeg"), rLo = Find(hips, "RightLeg"), rFt = Find(hips, "RightFoot");
        Transform lArm = Find(hips, "LeftArm"),  lFore = Find(hips, "LeftForeArm"),  lHand = Find(hips, "LeftHand");
        Transform rArm = Find(hips, "RightArm"), rFore = Find(hips, "RightForeArm"), rHand = Find(hips, "RightHand");

        Transform[] need = { spine1, head, lUp, lLo, lFt, rUp, rLo, rFt, lArm, lFore, lHand, rArm, rFore, rHand };
        foreach (Transform b in need) if (b == null) { Debug.LogError("[Ragdoll] A required bone is missing under Hips. Aborting."); return; }

        // 1) Rigidbodies first (joints need the parent body to exist)
        AddBody(hips,  2.5f);
        AddBody(spine1, 2.0f); AddBody(head, 1.2f);
        AddBody(lUp, 1.5f); AddBody(lLo, 1.0f); AddBody(rUp, 1.5f); AddBody(rLo, 1.0f);
        AddBody(lArm, 0.8f); AddBody(lFore, 0.6f); AddBody(rArm, 0.8f); AddBody(rFore, 0.6f);

        // 2) Colliders
        AddCapsule(hips,  spine1, 0.55f);
        AddCapsule(spine1, head,  0.45f);
        AddSphere(head, 0.5f * Vector3.Distance(head.position, spine1.position));
        AddCapsule(lUp, lLo, 0.30f); AddCapsule(lLo, lFt, 0.26f);
        AddCapsule(rUp, rLo, 0.30f); AddCapsule(rLo, rFt, 0.26f);
        AddCapsule(lArm, lFore, 0.26f); AddCapsule(lFore, lHand, 0.22f);
        AddCapsule(rArm, rFore, 0.26f); AddCapsule(rFore, rHand, 0.22f);

        // 3) Joints (child -> ragdoll parent body)
        AddJoint(spine1, hips,   20f, 25f, 25f);
        AddJoint(head,   spine1, 25f, 25f, 25f);
        AddJoint(lUp, hips,   20f, 45f, 30f); AddJoint(lLo, lUp, 10f, 45f, 10f);
        AddJoint(rUp, hips,   20f, 45f, 30f); AddJoint(rLo, rUp, 10f, 45f, 10f);
        AddJoint(lArm, spine1, 25f, 60f, 45f); AddJoint(lFore, lArm, 20f, 45f, 10f);
        AddJoint(rArm, spine1, 25f, 60f, 45f); AddJoint(rFore, rArm, 20f, 45f, 10f);

        // 4) Rest to "animated" state: kinematic + colliders off
        Rigidbody[] bodies = hips.GetComponentsInChildren<Rigidbody>(true);
        foreach (Rigidbody b in bodies)
        {
            b.isKinematic = true;
            b.interpolation = RigidbodyInterpolation.Interpolate;
            b.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            Collider c = b.GetComponent<Collider>();
            if (c != null) c.enabled = false;
        }

        EnsureController(player);
        EditorUtility.SetDirty(player);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(player.scene);
        Debug.Log("[Ragdoll] Built " + bodies.Length + " bodies + colliders + joints (kinematic). Controller attached. SAVE THE SCENE.");
    }

    private static void EnsureController(GameObject player)
    {
        if (player.GetComponent<RagdollController>() == null)
        {
            player.AddComponent<RagdollController>();
            Debug.Log("[Ragdoll] Added RagdollController to Player.");
        }
    }

    private static Transform Find(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform r = Find(root.GetChild(i), name);
            if (r != null) return r;
        }
        return null;
    }

    private static void AddBody(Transform bone, float mass)
    {
        Rigidbody rb = bone.gameObject.GetComponent<Rigidbody>();
        if (rb == null) rb = bone.gameObject.AddComponent<Rigidbody>();
        rb.mass = mass;
    }

    // Capsule running from bone origin toward 'end', sized in world units then converted to the bone's local
    // scale so the tiny bone lossyScale and odd rotation don't distort it.
    private static void AddCapsule(Transform bone, Transform end, float radiusRatio)
    {
        Vector3 worldDir = end.position - bone.position;
        float worldLen = worldDir.magnitude;
        if (worldLen < 1e-5f) return;

        Vector3 localDir = bone.InverseTransformDirection(worldDir.normalized);
        float ax = Mathf.Abs(localDir.x), ay = Mathf.Abs(localDir.y), az = Mathf.Abs(localDir.z);
        int dir = (ay >= ax && ay >= az) ? 1 : (az >= ax && az >= ay) ? 2 : 0;

        Vector3 ls = bone.lossyScale;
        float axisScale = Mathf.Abs(dir == 0 ? ls.x : dir == 1 ? ls.y : ls.z);
        float r0 = Mathf.Abs(dir == 0 ? ls.y : ls.x);
        float r1 = Mathf.Abs(dir == 2 ? ls.y : ls.z);
        float radScale = Mathf.Max(r0, r1);

        CapsuleCollider cap = bone.gameObject.AddComponent<CapsuleCollider>();
        cap.direction = dir;
        cap.height = worldLen / Mathf.Max(1e-6f, axisScale);
        cap.radius = (worldLen * radiusRatio) / Mathf.Max(1e-6f, radScale);
        cap.center = bone.InverseTransformPoint((bone.position + end.position) * 0.5f);
    }

    private static void AddSphere(Transform bone, float worldRadius)
    {
        Vector3 ls = bone.lossyScale;
        float radScale = Mathf.Max(Mathf.Abs(ls.x), Mathf.Max(Mathf.Abs(ls.y), Mathf.Abs(ls.z)));
        SphereCollider s = bone.gameObject.AddComponent<SphereCollider>();
        s.center = Vector3.zero;
        s.radius = worldRadius / Mathf.Max(1e-6f, radScale);
    }

    private static void AddJoint(Transform bone, Transform parentBone, float twist, float swing1, float swing2)
    {
        Rigidbody parent = parentBone.GetComponent<Rigidbody>();
        CharacterJoint j = bone.gameObject.AddComponent<CharacterJoint>();
        j.connectedBody = parent;
        j.anchor = Vector3.zero;           // bone pivot == joint location
        j.axis = new Vector3(0f, 0f, 1f);
        j.swingAxis = new Vector3(0f, 1f, 0f);
        j.enablePreprocessing = false;

        SoftJointLimit low = j.lowTwistLimit;   low.limit = -twist;  j.lowTwistLimit = low;
        SoftJointLimit high = j.highTwistLimit;  high.limit = twist;  j.highTwistLimit = high;
        SoftJointLimit s1 = j.swing1Limit;       s1.limit = swing1;   j.swing1Limit = s1;
        SoftJointLimit s2 = j.swing2Limit;       s2.limit = swing2;   j.swing2Limit = s2;
    }
}
