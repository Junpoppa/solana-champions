using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

/// <summary>
/// Spectator camera — created at runtime by NetBridge when WebBridge.Spectator (CameraManager is
/// disabled first; there is no local bean). Two modes:
///   • Player: reproduces the FOCUSED player's exact third-person view by driving the scene's
///     existing CameraManager rig (root follows the bean, yaw/pitch = the player's streamed
///     camera angles cy/cp, camera child pulled in on wall hits — same chain the player runs).
///     Left click = next player, right click = previous (roster order).
///   • Free: fly anywhere — mouse-look (pointer locked) + WASD, Space/E up, C/Q down, Shift fast.
/// Toggled by the F key or the DOM overlay's FREE CAM button (NetBridge.SpectateFreeCam).
/// Every mode/focus change is reported to the DOM via the SpectateState jslib so the roster
/// highlight and hint text track the camera.
/// </summary>
public class SpectatorCamera : MonoBehaviour
{
    [Header("Player view")]
    public float viewAngleLerp = 10f; // catch-up rate toward the streamed camera angles

    [Header("Free fly")]
    public float freeSpeed = 12f;     // u/s
    public float freeFastMult = 3f;   // Shift multiplier
    public float freeLookSpeed = 2.4f;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void SpectateState(string s);
#else
    private static void SpectateState(string s) { Debug.Log("[SpectateState] " + s); }
#endif

    private enum Mode { Player, Free }

    private NetBridge net;
    private Mode mode = Mode.Player;
    private string focusedId;

    // the CameraManager rig chain (root → pivot → camera), reused for both modes
    private Transform rig;
    private Transform pivot;
    private Transform camT;
    private float camDist = 3f;
    private float followSpeed = 3f;

    private float yaw, pitch;         // smoothed player-view angles
    private float freeYaw, freePitch; // free-fly look angles

    public void Init(NetBridge bridge)
    {
        net = bridge;
        var cm = CameraManager.singleton;
        if (cm != null && cm.pivot != null && cm.camTrans != null)
        {
            rig = cm.transform;
            pivot = cm.pivot;
            camT = cm.camTrans;
            camDist = cm.cameraDist;
            followSpeed = cm.followSpeed;
        }
        else if (Camera.main != null)
        {
            // Fallback rig (no CameraManager in the scene): root → pivot → camera, same shape.
            var rootGo = new GameObject("SpecRig");
            var pivotGo = new GameObject("SpecPivot");
            pivotGo.transform.SetParent(rootGo.transform, false);
            Camera.main.transform.SetParent(pivotGo.transform, true);
            rig = rootGo.transform;
            pivot = pivotGo.transform;
            camT = Camera.main.transform;
        }
        else
        {
            Debug.LogWarning("[SpectatorCamera] no camera rig");
            enabled = false;
            return;
        }

        // Default focus: first roster player, camera snapped straight to them.
        var ids = net.SpectateIds();
        focusedId = ids.Count > 0 ? ids[0] : null;
        Transform root; float cy, cp;
        if (net.TryGetSpectateTarget(focusedId, out root, out cy, out cp))
        {
            rig.position = root.position;
            yaw = cy; pitch = cp;
        }
        PushState();
    }

    // DOM roster click: focus this player (always returns to Player mode).
    public void Focus(string id)
    {
        focusedId = id;
        if (mode == Mode.Free) ExitFree(false);
        PushState();
    }

    public void ToggleFreeCam()
    {
        if (mode == Mode.Free) ExitFree(true);
        else EnterFree();
    }

    // Explicit mode select (the HUD's PLAYER CAM / FREE CAM buttons).
    public void SetFreeCam(bool free)
    {
        if (free && mode != Mode.Free) EnterFree();
        else if (!free && mode == Mode.Free) ExitFree(true);
    }

    void EnterFree()
    {
        mode = Mode.Free;
        // Seamless: keep flying from the camera's current world pose. The camera collapses onto
        // the pivot (localPosition zero) and the RIG becomes the fly position.
        Vector3 camPos = camT.position;
        Vector3 e = camT.rotation.eulerAngles;
        freeYaw = e.y;
        freePitch = e.x > 180f ? e.x - 360f : e.x;
        rig.position = camPos;
        camT.localPosition = Vector3.zero;
        camT.localRotation = Quaternion.identity;
        Cursor.lockState = CursorLockMode.Locked; // mouse-look; Esc frees, click re-locks
        Cursor.visible = false;
        PushState();
    }

    void ExitFree(bool notify)
    {
        mode = Mode.Player;
        yaw = freeYaw; pitch = Mathf.Clamp(freePitch, -35f, 35f); // ease back toward the streamed view
        Cursor.lockState = CursorLockMode.None; // DOM overlay needs the OS cursor again
        Cursor.visible = true;
        if (notify) PushState();
    }

    void Update()
    {
        if (!WebBridge.Spectator) return;

        if (Input.GetKeyDown(KeyCode.F)) { ToggleFreeCam(); return; }

        if (mode == Mode.Player)
        {
            // LMB/RMB cycle players. DOM overlay islands sit above the canvas, so these only fire
            // for clicks on the game view itself.
            if (Input.GetMouseButtonDown(0)) Cycle(1);
            else if (Input.GetMouseButtonDown(1)) Cycle(-1);
        }
        else if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked)
        {
            Cursor.lockState = CursorLockMode.Locked; // browser Esc dropped the lock — re-engage
            Cursor.visible = false;
        }
    }

    void Cycle(int dir)
    {
        var ids = net.SpectateIds();
        if (ids.Count == 0) return;
        int i = ids.IndexOf(focusedId);
        i = i < 0 ? 0 : (i + dir + ids.Count) % ids.Count;
        focusedId = ids[i];
        PushState();
    }

    void LateUpdate()
    {
        if (rig == null || pivot == null || camT == null) return;
        if (mode == Mode.Free) { FreeFly(); return; }

        Transform root; float cy, cp;
        if (!net.TryGetSpectateTarget(focusedId, out root, out cy, out cp))
        {
            // focused player despawned (dropped) — advance to the next live one
            var ids = net.SpectateIds();
            if (ids.Count == 0) return;
            focusedId = ids[0];
            PushState();
            if (!net.TryGetSpectateTarget(focusedId, out root, out cy, out cp)) return;
        }

        // Exact CameraManager chain: root lerps to the bean, yaw on the root, pitch on the pivot,
        // camera child at -dist with the wall pull-in. Angles ease toward the 15 Hz streamed values.
        rig.position = Vector3.Lerp(rig.position, root.position, Time.deltaTime * followSpeed);
        float k = 1f - Mathf.Exp(-viewAngleLerp * Time.deltaTime);
        yaw = Mathf.LerpAngle(yaw, cy, k);
        pitch = Mathf.LerpAngle(pitch, cp, k);
        rig.rotation = Quaternion.Euler(0f, yaw, 0f);
        pivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);

        float dist = camDist + 1.0f;
        Ray ray = new Ray(pivot.position, camT.position - pivot.position);
        RaycastHit hit;
        if (Physics.Raycast(ray, out hit, dist) && hit.transform.CompareTag("Wall"))
            dist = hit.distance - 0.25f;
        if (dist > camDist) dist = camDist;
        camT.localPosition = new Vector3(0f, 0f, -dist);
        camT.localRotation = Quaternion.identity;
    }

    void FreeFly()
    {
        // mouse-look (only while the pointer is locked — a freed cursor is browsing the overlay)
        if (Cursor.lockState == CursorLockMode.Locked)
        {
            freeYaw = Mathf.Repeat(freeYaw + Input.GetAxis("Mouse X") * freeLookSpeed, 360f);
            freePitch = Mathf.Clamp(freePitch - Input.GetAxis("Mouse Y") * freeLookSpeed, -89f, 89f);
        }
        rig.rotation = Quaternion.Euler(0f, freeYaw, 0f);
        pivot.localRotation = Quaternion.Euler(freePitch, 0f, 0f);

        Vector3 move = Vector3.zero;
        Vector3 fwd = pivot.rotation * Vector3.forward;
        Vector3 right = rig.rotation * Vector3.right;
        if (Input.GetKey(KeyCode.W)) move += fwd;
        if (Input.GetKey(KeyCode.S)) move -= fwd;
        if (Input.GetKey(KeyCode.D)) move += right;
        if (Input.GetKey(KeyCode.A)) move -= right;
        if (Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.E)) move += Vector3.up;
        if (Input.GetKey(KeyCode.C) || Input.GetKey(KeyCode.Q)) move -= Vector3.up;
        if (move.sqrMagnitude > 1f) move.Normalize();
        float sp = freeSpeed * (Input.GetKey(KeyCode.LeftShift) ? freeFastMult : 1f);
        rig.position += move * sp * Time.deltaTime;

        camT.localPosition = Vector3.zero;
        camT.localRotation = Quaternion.identity;
    }

    // Tell the DOM overlay what the camera is doing (roster highlight + hint text + button state).
    void PushState()
    {
        string id = focusedId ?? "";
        SpectateState("{\"mode\":\"" + (mode == Mode.Free ? "free" : "player") + "\",\"id\":\"" + id + "\"}");
    }
}
