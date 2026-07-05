# MEMORY — gotchas & procedures (so I don't repeat mistakes)

## #1 rule
NO hand-porting Unity gameplay to Three.js. Tried; every system (beams/xray/water/mountains/materials/movement/camera) broke separately. Ship = Unity WebGL build wrapped by JS shell. Old port deleted (`web/src/{course,player,physics}.ts`).

## Diagnosis methods (can't see/tick the game directly)
- Editor Play does NOT tick physics/anim/coroutines while MCP-driven/unfocused (velocity frozen between calls) → user MUST playtest gameplay. Sim physics manually: `Physics.simulationMode=Script` → loop `Physics.Simulate(0.02f)` → restore.
- Edit script while IN Play = OLD code runs until you EXIT+RE-ENTER Play.
- Pose/anim defect (can't see 3D): render baked clip to PNG — `clip.SampleAnimation(inst,t)` (EDIT mode, no Play) at t={0,.25,.5,.9,1}, temp Camera→RenderTexture→`EncodeToPNG`→`_diag/`, then `Read` PNG. Front=splay, side=incline.
- Browser: Playwright screenshot readable ONLY via ABSOLUTE path (`C:/Users/Junius/Desktop/unit_game/_diag/x.png`)→`Read`. Headless Chromium runs WebGL (harmless GPU warns INVALID_ENUM/FSR).
- In-game look bugs: headless Playwright → JOIN → `browser_console_messages` (grep "WebBridge"/"not found"/"[WebBridge]") + screenshot. DO BEFORE telling user done.

## Ragdoll — rig + feel + jitter + beam sweep
Scope = `SpinningBeamHazard` only. Beam → limp tumble → auto get-up. Files: `RagdollController.cs` (NEW, self-wiring), `Assets/Editor/BeanRagdollSetup.cs`. Edits: `CharacterControls.cs` (`ragdolled` flag + `SetRagdoll` + FixedUpdate early-return + LoadCheckPoint reset), `SpinningBeamHazard.Hit`.
- Player hierarchy (tag Player): root = Rigidbody+CapsuleCollider+`CharacterControls`; child `BeanModel` (Animator+`BeanWalkDriver`) → `Armature/Hips` (20-bone, 0.01 scale).
- Rig = 11 bone bodies (Hips,Spine1,Head,L/R UpLeg+Leg,L/R Arm+ForeArm): RB+Collider+CharacterJoint. Built by menu **Tools/Bean/Build Ragdoll** (editor-only, re-run safe).
  - GOTCHA scale: bone lossyScale≈0.008 → collider dims in WORLD units ÷ lossyScale.
  - GOTCHA: `UnityEditor.RagdollBuilder` can't instantiate headless → built MANUALLY (capsules/sphere/joints from world bone positions).
- `EnableRagdoll(vel,strength,hitPoint)`: animator+walkdriver off, root rb kinematic + root capsule OFF (stops kinematic-vs-flying-bone fight), bones dynamic+inherit vel. Re-entry guard (`if ragdolled return`) → routes to `Reshove` when already limp (re-push+reset timer; fixes 2nd-beam instant-getup + re-hit during standup).
- FEEL (GTA-floppy = knock DOWN+a bit back, not fly): reshape push — `v.y*=upMultiplier(0.25)`, `v*=knockbackScale(0.3)`, `ClampMagnitude(maxLaunchSpeed=4)`. Limbs LOOSE (don't crank damping). ~2m slide-back.
- Directional tumble: `SpinningBeamHazard` passes CONTACT POINT (`cap.ClosestPoint`, height=upper/lower) → `AddForceAtPosition` on NEAREST bone (`ApplyHitImpulse`, `hitImpulse`=4, `hitImpulseUp`=0.2) → off-center = rotates about strike. Low beam=legs swept/head fwd, high=head back. Flop is from rotation, not launch.
- PER-BEAM knock: `SpinningBeamHazard.ragdollStrength` — `SweepArm_Lower`=1.4, `SweepArm_Upper`=1.2, base 1.0. Editor distance unreliable → feel-test in Play.
- Beam keeps shoving limp bean: bone colliders tagged "Player" at runtime (off while animated). `SpinningBeamHazard.Hit` uses `GetComponentInParent<CharacterControls>`. `SweepArm_Lower.radiusPadding`=0.4 to reach the lying bean.
- JITTER (3 causes): (1) bones **Discrete** not ContinuousDynamic (continuous sweeps on jointed ragdoll = #1 jitter, worse in WebGL) + `maxDepenetrationVelocity`=3 + small damping. (2) physics solver **TGS**: `DynamicsManager.m_SolverType` 0→1 (iters→16, velIters→8; set via SerializedObject + Save Project, persists to build) — THE pro ragdoll-smoothness fix. (3) camera follows SMOOTHED standalone `camProxy` (`SmoothDamp`, `camSmoothTime`=0.08), NOT raw hips bone (bones are children of root → moving root to chase hips = feedback loop).
- Self-collision violent vibration → `Physics.IgnoreCollision` every bone-collider pair in Awake (non-adjacent didn't auto-ignore).
- Stretched limbs → `CharacterJoint.enableProjection=true`, `projectionDistance`=0.01; `maxAngularVelocity`=20.
- Beam shake-vs-pass-through tradeoff: keep contact + SLIPPERY bone `PhysicsMaterial` (`ragdollFriction`=0.2, frictionCombine=Minimum) → bar SWEEPS bean along deck (not crush/pin). Escape: `beamPassThrough` re-applies IgnoreCollision (but then bar clips through).
- Floor clip (bones thinner than mesh; feet/hands have NO colliders, rig length-refs only): `colliderInflate` (1.5 WebGL) + `soleOffset` 0.15. If still clips, add foot/hand sphere colliders in `BeanRagdollSetup` + re-run Build Ragdoll.
- Camera retarget: `CameraManager.target`=Hips (proxy) while limp, →Player on recover. KillZone respawn: `CharacterControls.LoadCheckPoint()` calls `DisableRagdoll()` + zero velocity.
- WebGL physics jitterier than editor → `DynamicsManager.m_DefaultContactOffset`=0.02 (earlier/softer contacts, helps jitter AND clip).
- Live-tune fields (no recompile): hitImpulse, hitImpulseUp, ragdollFriction, beamPassThrough, colliderInflate (re-play), soleOffset, camSmoothTime, ragdoll Max/Angular/Linear damping.

## Ragdoll GET-UP = "reset bones" technique (DONE, shipped clean)
Pipeline (`RagdollController.cs`): limp heap → settle-gate → align → **ResettingBones** (lerp bones into standup clip frame-0, `timeToResetBones` 0.5s) → play Mixamo standup clip → walk.
- Grounded-gate (no mid-air spring-up): stand only after `minLimpTime` AND settled (hips speed low + upward floor near hips). Ground ray from ABOVE hips, SKIPS own bone colliders (else self-hit reads grounded mid-air), normal.y>0.4, within `groundProbe`. Airborne→stays limp→falls→KillZone.
- Orientation: `dot(chest.forward,up)<0` = face-down (chest.up stays horizontal when lying). `faceDownWhenDotNegative` flips. Face→`StandUpFace`, back→`StandUpBack`.
- **Scale trap (0.01 Armature)**: NEVER lerp hips localPOSITION (lerps to garbage → "disappear/slide"). Lerp ROTATION all bones + POSITION limbs only; hips SNAP to clip-local then placed in world by `AlignRotationToHips` (root faces hips feet-dir `hipsFacingAxis`(0,-1,0), restore hips world pose) + `AnchorHipsXZ` (world-space, hips over landing spot = no slide) + foot grounding. Sample clips' frame-0 in Awake (`SampleClipFirstFrame` on `animator.gameObject`=BeanModel; clip paths "Armature/Hips/..").
- Force get-up state: `animator.enabled=true → Rebind() → Play(state,0,0) → Update(0)` (plain Play the same frame Animator re-enables gets dropped). Deterministic Play, not triggers. Respawn forces `Play("Idle",0,0)`.
- **DON'T enable `BeanWalkDriver` during get-up**: it sets Airborne=true 1 frame → controller `AnyState→Jump` (Airborne==true, no exit) yanks out of standup into Jump. Keep walkDriver DISABLED + force Airborne=false/Speed=0 until control returns (`BeanWalkDriver.cs:49-50` sets Airborne from own raycast).
- **Frozen after get-up (big one)**: `BeginStandUp` must `controls.SetRagdoll(false)` BEFORE `LockControl` else `CharacterControls.FixedUpdate` keeps `if(ragdolled)return` = frozen forever. BUT during the get-up keep `SetRagdoll(true)` so FixedUpdate doesn't write velocity on the kinematic root (kills error spam); clear at StandUpRoutine end.
- Grounding during get-up: `GroundLowestBone()` by FOOT bones (LeftFoot/RightFoot world Y, `soleOffset` 0.1). NOT mesh-bounds (don't track animated pose → float). Mid-rise = **SINK-ONLY** (`GroundLowestBone(true)` — only lower, never lift to chase a foot dipping on the push-up = the "pop up"). Clip's own Hips.Y drives the rise. Final `endSettleFraction`(0.25) of clip = ground by FEET FULLY (feet settled, no chase). `curStandNorm` picks the branch.
- **LEVITATION at get-up END (big bug)**: serialized `standFaceDuration`/`standBackDuration` go STALE every re-bake (clip len changes, number doesn't) → hand-off MID-clip → animator keeps raising Hips.Y while physics holds root = float ~1s. FIX: read REAL clip length at runtime (`FindClip(name).length` in Awake), StandUpRoutine POLLS animator (`normalizedTime>=0.95` or left state) → hand off at real clip end.
- **NO capsule-reseat at hand-off** (was the levitate/snap): feet sit ~0.08 ABOVE capsule bottom, so reseating root by the capsule pops the visible bean up. Instead ground by FEET in final 25% + delete reseat; `IsGrounded` still true via ray tolerance (deck y=12.0, capsule h=2 center=0 → distToGround=1.0, ray len +0.1).
- Beam re-hit during get-up: keep `rootCol` ENABLED (root tagged Player) so OverlapCapsule re-detects → re-ragdoll. Bone cols off + root kinematic = no fight.
- Deck = `Platform_RoundArena`, collider top y=12.00.
- Live knobs: `soleOffset`(0.1), `endSettleFraction`(0.25), controller StandUp*→Idle `exitTime`(0.80)/`duration`(0.35).

## STANDUP CLIP BAKE (`StandupBaker.cs`, menu Tools/Bean/Bake Standup Clips)
DON'T make the bean permanently Humanoid — idle/walk/jump are GENERIC transform clips; a Humanoid avatar overrides them and the body SINKS ~0.8m through the floor. Bake Mixamo standups to Generic .anim via Humanoid retarget AT BAKE TIME only: temporarily set `party_character` Humanoid, `GameObjectRecorder`+`AnimationMode.SampleAnimationClip` retarget onto bean → `StandUpFace_Bean.anim`/`StandUpBack_Bean.anim` (paths Armature/Hips/..), then REVERT to Generic. (Same trick made idle/jump.)
- After bake, STRIP curves:
  - STRIP all `m_LocalScale` — extreme-pose retarget bakes non-unit bone scales = "bigger/deformed" (proof: good idle has `m_ScaleCurves: []`).
  - KEEP all `m_LocalPosition` — stripping → CROOKED legs.
  - STRIP rotation on `StripRotationLeaves[]` = feet + L/R UpLeg + L/R Leg + Head. Retarget twists feet sideways, abducts thighs ("A-splay"), tilts head up; holding bind → feet forward, legs straight/parallel, head level. Spine/arm rotations KEPT (drive the rise). (Splay straightening at hand-off WAS the "snap" → fixing splay killed the snap.)
  - TRIM start `FaceStartSeconds`/`BackStartSeconds`(~1.0) so clip begins partway-up.
- Clip lens now: Face 3.15s, Back 2.0s. Controller `StandUp*→Idle` exitTime 0.80 / dur 0.35 (pose stable from ~70%, blend melts into idle before control returns = zero boundary).

## Faces (12 named expressions)
Flat transparent 512² PNGs on bean face material mat[1] (URP Lit transparent). Add a face:
1. PNG → `unity_game/Assets/FREE/Pack_FREE_PartyCharacters/Resources/Materials/Face Images/` (Resources → auto-bundled).
2. Copy PNG → `web/public/textures/faces/`.
3. `FACES[]` entry in `web/src/data/beanPalette.ts`: `{id,label,file,tex}` — `file`=`/textures/faces/<n>.png`, `tex`=Resources basename (no path/ext). `look.face` = INDEX into FACES (sanitize clamps).
- WEB: `clothing.ts refreshFaceLabel`→`FACES[i].label`; `bean.ts` `FACE_OPTIONS`=FACES.map(file).
- IN-GAME: `unityGame.applyLook` sends `faceTex`. `WebBridge.ApplyBodyAndFace` loads `Resources.Load<Texture>("Materials/Face Images/"+faceTex)` onto a CLONE of `Materials/Face/face 1.mat` (`_BaseMap`+`_MainTex`)→mat[1]. Texture-name based = ONE path all faces (legacy per-index `face N.mat` fallback). Import sRGB+alpha default OK.
- Rebuild WebGL for in-game; customizer updates on dev reload alone.

## Music = Strudel live-coded, NO wav
- Autoplay law: no web audio before a user gesture (all browsers) → music lives in LOBBY (lobby-enter click = unlock), not silent start screen. `hush()` on JOIN.
- `@strudel/web` v1.3.0, singleton `web/src/ui/musicController.ts` holds pattern as a code STRING. `start`: lazy `initStrudel()` → `getAudioContext().resume()` → `evaluate(code)` (last expr `arrange(...)` loops). `stop`=`hush()`. Synths pure WebAudio; TR808/909 drums STREAM from `dough-*` repos. No TS types → `src/strudel-web.d.ts` shim. Exports: initStrudel, evaluate, hush, getAudioContext, samples.
- Two tracks: `LOBBY_PATTERN`(105/4), `GAME_PATTERN`(134/4). To change: edit the string (last expr plays+loops).
- BUG drums silent: `initStrudel()` default prebake doesn't load Roland machines → after init `await samples("https://raw.githubusercontent.com/felixroos/dough-samples/main/tidal-drum-machines.json")` (felixroos; Bubobubobubobubo fork = 404).
- BUG track didn't swap: `evaluate()` doesn't stop prior pattern → `hush()` BEFORE `evaluate()`.
- Diagnostic: `window.getCps()` tells live track (lobby 0.4375 vs game 0.5583) — confirm swap without listening.
- Model: `request(code, delayMs)` — set desired, ++token (cancels in-flight), `hush()` immediately, evaluate after delayMs (token-guarded). `startLobby`=request(LOBBY,0); `startGame`=request(GAME,**2000**) (2s in-game delay); `stop`=request(null,0). Wired in `main.ts setState`.
- Mute toggle: 4th lobby nav pill (`lobby.ts buildMusicToggle`, NOT in navButtons so setActive won't recolor; teal on / `MUTED_BG` off), `localStorage["musicMuted"]`, `musicController.setMuted/toggle/isMuted`.

## Unity WebGL build
- Settings = `Course.unity` only. Output ≈23MB Brotli → `web/public/unity/Build/unity.{loader.js,data.unityweb,framework.js.unityweb,wasm.unityweb}`. ~7–15min IL2CPP; MCP call times out but build CONTINUES — poll `Build/` mtimes.
- `BuildPipeline.BuildPlayer` is SYNCHRONOUS → `manage_build cancel` REJECTED mid-build ("blocks editor"). Don't start a build on a value you're unsure of (tune in web first). In-flight build on a now-stale value = throwaway, just rebuild after it ends.
- GOTCHA: asset EDITOR scripts in PLAYER assembly break the build (ithappy FREE asmdef put 51 editor scripts there → "scripts had compiler errors", UnityEditor stripped). FIX: Editor folder gets own editor-only asmdef (`includePlatforms:["Editor"]`, refs runtime asmdef). Any package doing this → same fix.
- GOTCHA: Brotli `.unityweb` needs `Content-Encoding: br`. Dev = `web/vite.config.ts` plugin `unity-webgl-serve`. PROD = host must send br (or rebuild uncompressed). `WebGL.compressionFormat=Disabled` did NOT take in Unity 6.

## Web↔Unity bridge + water + xray
- `WebBridge` GO in Course.unity = JS→Unity receiver: `SetMouseSensitivity(float)`, `ApplyLook(json)`, `ApplyBodyAndFace`. Player bean: `Player`→`BeanModel`→`Character mesh` (SMR mat[0]=body,[1]=face)+XRayFill/Mask+`customize_objects` (head, scale100, **lossyScale 0.8014 on Course**).
- `SpawnAccessory(customize,id)`: reads `REG` AccDef → `Resources.Load` mesh+material (or prefab), parents to `customize` at `def.pos/euler/scale` (localPosition/Euler/scale), strips colliders, Untagged, adds XRay-mask twin. REG entry placement defaults identity; tune in web → bake numbers into REG.
- Accessory mesh/material/prefab MUST be under a `Resources/` folder for WebGL `Resources.Load` → `Assets/Resources/Accessories/` (Acc_*.mesh + Color/Glass/HairColor.mat; hat prefabs `Resources/Prefabs/Hats`).
- ithappy/Synty accessory FBX are SKINNED → can't instantiate raw; use baked `Acc_*.mesh`.
- Water: WebGL runs "Mobile" quality → `Mobile_RPAsset` needs RequireDepth+OpaqueTexture ON (PC asset had them → editor fine, browser flat).
- XRay glow: fill paints cyan on body an opaque accessory covers (occlude → bean XRayMask LEqual fails). FIX = each accessory gets a `Mat_BeanXRayMask` twin (stencil bit 2), AUTO in `SpawnAccessory.AddXRayMaskTwins`. Same bug = "cyan clown nose" (was fill, not its color).

## Unity MCP (execute_code) + glb export
- C# 6 CodeDom: NO `using`; fully-qualify (`UnityEditor.`, `System.IO.`). No local functions (Stack/loops). File writes / `AssetDatabase.DeleteAsset` need `safety_checks:false`. (For multi-method work prefer a real `Assets/Editor/*.cs` via create_script + a menu item — no CodeDom limits.)
- Scene edits SILENTLY REVERT in Play → `manage_editor stop` first. User also hand-edits → re-check state; save Course + warn before scene switch.
- glb export: UnityGLTF `new GLTFSceneExporter(new[]{root}, new ExportContext(GLTFSettings.GetOrCreateSettings())).SaveGLB(dir,name)` (sync; appends `.glb`). glTFast can't export anims, UnityGLTF can.
  - UnityGLTF ships NO asmdef → its types live in Assembly-CSharp → an `Assets/Editor` script can `using UnityGLTF;` directly (no reflection, no asmdef ref).
  - UnityGLTF MIRRORS X: exported (x,y,z) → web (−x,y,z).
- Mesh surgery / `BakeMesh` needs FBX `isReadable=true`.

## Sidekick hair → customizer + in-game (recipe + scripted baker + 3 traps)
WORKING RECIPE (Synty Sidekick part → "Hair N"):
- Source: `Assets/Synty/SidekickCharacters/Resources/Meshes/Species/Humans/SK_HUMN_BASE_0N_02HAIR_HU01.fbx` (+ `Outfits/Starter/SK_SCFI_CIVL_09_02HAIR_HU01.fbx`).
- `BakeMesh` (Unity 6) bakes WITH renderer scale.
- Synty mesh = `MeshTopology.Quads` → triangulate (abcd → abc+acd). Hair = 2 submeshes but SpawnAccessory assigns ONE material → MERGE submeshes into 1 triangle list (`SetTriangles(all,0)`, subMeshCount=1).
- In-game mesh MUST be **HEAD-LOCAL + IDENTITY** (like `Acc_hair_001`): verts = `customize.worldToLocalMatrix * TRS(smr.pos,smr.rot,1) * v`. Scale-invariant → works on Course bean (0.8x) AND showroom (1.0). NOT geometry+offset-fit.
- Flat color: `Resources/Accessories/HairColor.mat` (URP/Lit `#5A3E28`), REG `material="HairColor"`.
- Files per hair: `Resources/Accessories/Acc_hair_HUMN0N.mesh`; `WebBridge` REG entry; node in `web/public/models/sidekick.glb` (loaded by `bean.ts` `load()`+`collect(sidekickScene)`); `beanPalette.ts` SLOTS.hair `{id,label,node}` (id MUST match REG key). Collider-strip + XRay twin AUTO.

SCRIPTED BAKER (`Assets/Editor/SidekickHairBaker.cs`, menu **Tools/Bean/Bake Sidekick Hairs**): batched Hairs 5–13 (`SK_HUMN_BASE_02..10`) in ONE run. Add more = add FBX basename to `ids` list + REG/beanPalette entries + rebuild.
- KEY TRICK: DERIVE the head-local transform M from the already-shipped `Acc_hair_HUMN01.mesh` — least-squares affine mapping raw BakeMesh verts → shipped verts. Residual 0.00000 because triangulate+merge are INDEX-ONLY (preserve vertex array order/count, NOT `CombineMeshes`) → exact correspondence. Apply same M to every hair's raw bake (all Synty human hairs share rig/origin; user confirmed HUMN02 fits HUMN01's numbers). Baker ABORTS if count mismatch or residual>0.01 (self-check, no garbage shipped).
- Per hair: BakeMesh → `M.MultiplyPoint3x4(v)` → triangulate+merge → `RecalculateNormals/Bounds` → save mesh.
- Web glb: baker builds temp root with `hair_HUMN01..10` children (identity, head-local) → `SaveGLB(dir,"sidekick")` overwrites with all 10.
- DOWN-NUDGE workflow (no wasted rebuilds): drop = a Y offset in the SHARED head-local mesh space → SAME number in web (`beanPalette` SLOTS.hair `fits[id].y`; `applyFit` uses `fit.y ?? 0`; down=neg) AND Unity (`WebBridge` REG `AccDef.pos.y` → localPosition; down=neg). TUNE LIVE IN WEB FIRST (Vite HMR, instant) until user happy, THEN bake into REG + ONE rebuild. Hairs 5–13 = **-0.125** (Hair 4/HUMN01 stays identity). `AccDef.pos` via object-init: `new AccDef(...,"HairColor"){ pos = new Vector3(0,-0.125f,0) }`.

3 TRAPS (s6, cost HOURS — read before touching):
- TRAP 1 hair invisible in-game: Course `customize_objects` lossyScale=0.8014 vs showroom 1.0 → non-identity showroom-calibrated fit doesn't transfer. ALWAYS head-local+identity (scale-invariant). VERIFY before any 15-min rebuild: sim spawn on the REAL Course bean (`Resources.Load` mesh+mat → child of Course `customize_objects` at identity → render PNG). Don't trust the showroom.
- TRAP 2 camera flipped: `GameObject.Find("Main Camera")` with Course loaded additively returns COURSE's camera (Find ignores active scene) → moving it for renders + build saving Course = flipped camera shipped. `CameraManager.LateUpdate` resets localPosition but NOT localRotation → bad rotation survives. RULE: diag renders use a uniquely-named TEMP camera (`TMPCAM_diag`), destroy after. NEVER touch `Find("Main Camera")` while Course is loaded. Restore = Course Main Camera (child of `Pivot` under `Camera Holder`): localPos (0,0,-10), localRot identity, fov 60.
- TRAP 3 nothing applies in-game: `WebBridge` GO went MISSING from Course (saved out while dirty) → `SendMessage("WebBridge",...)` logs "object WebBridge not found!" → EVERY look message dropped (body/face/hats too). RULE: before saving Course confirm a `WebBridge` GO exists+active. Restore = `new GameObject("WebBridge")` → `SceneManager.MoveGameObjectToScene(go,course)` → `AddComponent<WebBridge>()` → save (no serialized wiring, REG static).
- s7 avoided all 3: didn't touch Course/Main Camera/showroom; baker only spawns+destroys temp instances + a temp export root.

HEADWEAR (hat slot) = same recipe, parallel baker `SidekickHeadwearBaker.cs` (menu **Tools/Bean/Bake Sidekick Headwear**): 4 AHED FBX from `.../Meshes/Outfits/Starter/` (`SK_FANT_KNGT_17_22AHED`=Warrior, `SK_HORR_VILN_01_22AHED`=Pumpkin, `SK_SCFI_CIVL_09_22AHED`=fox, `SK_SCFI_CIVL_10_22AHED`=Assassin). DERIVES SAME M from HUMN01 hair (M = bind-pose frame, mesh-independent → works on ANY head-bound Synty part, residual 0.00000). Output `Acc_head_<key>.mesh` + own `headwear.glb` (NOT sidekick.glb — separate, bean.ts loads it: `loadAsync("/models/headwear.glb")`+`collect(headwearScene)`). REG keys `head_warrior/pumpkin/fox/assassin` (HairColor mat, pos.y=-0.125). beanPalette hat items + fits `{y:-0.125, scale:0.93}`. Flat brown (color = future TODO 1f). Verify in WEB preview (drive `#clothingPanel` rows via evaluate — head slots mutually exclusive, set Hat auto-clears Hair).
- Showroom = `Sidekick_Showroom.unity` (editor-only, NOT in build) — pick parts visually. Web hairs relabeled "Hair 1/2/3" (the non-Sidekick ones).

## Billboard (PolygonCity signs)
Sign mesh UVs map the board face to a SUB-RECT (`SM_Prop_Billboard_Sign_01` uv u[0.001,0.512] v[0.003,0.254]), NOT full 0..1. Full image needs material tiling scale=1/(uMax-uMin)≈(1.96,3.98), offset=-uMin·scale, to stretch UV-rect over the whole image; tiling (1,1) = cropped corner. Per-instance image = duplicate the mat (signs share). New PNGs land at PROJECT ROOT → copy into Assets/ first. Sign (4) = `Mat_SolChamp_Billboard3` (full "Sol Champ billboard 3.png").

## Game modes + Last Man Standing scene (s8)
- 3 build scenes `[Boot(0), Course(1), LastManStanding(2)]`. Boot = tiny loader (Camera+Light+WebBridge) so the web picks the mode BEFORE a heavy gameplay scene loads. Web: `unityGame.launch(onBack,mode)` → after `ensureLoaded` `SendMessage("WebBridge","LoadGameScene",scene)` (spinner→Course, lastman→LastManStanding); `hide()`→LoadGameScene("Boot"). `WebBridge.LoadGameScene(name)`=`SceneManager.LoadScene`. Each scene has its OWN `WebBridge` GO (REG static). Look/sensitivity survive the swap via WebBridge STATIC cache (`s_lastLook`/`s_lastSensitivity`) re-applied in `Start()` — so ApplyLook sent to Boot's bridge still lands in the gameplay scene.
- Unity→JS: `Assets/Plugins/WebGL/LmsBridge.jslib` exposes `LmsGameOver()`→`window.__unityGameOver()`. `EliminationZone.cs` (trigger box, LMS only) calls it on player fall → web returns to lobby (game-over, NO respawn). Editor-stubbed via `#if UNITY_WEBGL && !UNITY_EDITOR`.
- Music Spinner-only: `musicController.startGame(mode)` plays GAME_PATTERN only for `mode==="spinner"`, else `hush()`. (TODO: own LMS track.)
- LMS scene built by DUPLICATING Course (`AssetDatabase.CopyAsset`) then DestroyImmediate the 7 non-essential roots (KillZone, Draft_3, Background_SetDressing, ASSET_PALETTE, Cliff_Layout, Forest, PolygonCity_Billboards) — keeps Player/Camera/ragdoll/WebBridge wiring intact. Player moved to (-180,14,20) (`CharacterControls.Awake` sets checkPoint=transform.position). Hex via `Tools/Course/Generate Hex Arena` (R 1.455, rings 10 = 1655 tiles).
- **TRAP (cost the user's rails+billboards once)**: scene edits in PLAY MODE silently revert; and OpenScene(Single) DISCARDS unsaved changes in the closing scene with NO prompt. The user had manually added poles+billboards to the (dirty) Course in-memory; opening LMS single dumped them. Lesson: before switching/closing a scene the user has been hand-editing, SAVE it or move+save the wanted objects first. Recovered by copying the on-disk Course originals (`PolygonCity_Billboards`, and `Hex_Corner_Posts`/`Hex_Triple_Rails` poles) into LMS via atomic open-additive→Instantiate→MoveGameObjectToScene→close (Δx -360 = hex center vs Course arena center). MoveGameObjectToScene needs a root → SetParent(null,true) first.
- **Hands-up flicker on hexes** = `BeanWalkDriver` single center ray slipping through inter-hex seams / over freshly-dipped tiles → `Airborne` flickers → `AnyState→Jump` (hands up). FIX (LMS-scoped via `gameObject.scene.name=="LastManStanding"` in Awake; Course path byte-identical): 5-point down-ray (center + ±capsuleRadius x/z) + coyote-time 0.15s; Airborne only after sustained ground loss. Cosmetic flag only (never touches physics). `Airborne` true=Jump pose, so keep coyote short enough that real jumps still pose.

## Deeper detail
`.claude` auto-memories hold long history: web-native-pivot, unity-to-web-glb-export, bean-customization-web, sidekick-showroom. This file = quick "don't repeat" list.
