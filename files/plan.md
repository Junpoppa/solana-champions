# PLAN — Solana Champions (Fall-Guys-style obstacle course, browser game)

Resume = read this + memory.md. Unity = `unity_game/` (Unity 6, URP). Web = `web/` (Vite+TS+Three.js).

> ★★ **STANDING RULE (s29): repo is on GitHub — `github.com/Junpoppa/solana-champions` (PRIVATE).** After ANY session's changes: `git add -A && git commit && git push`. Once Railway is connected, every push auto-deploys the live site. Root `.gitignore` filters Unity Library/junk/media; the WebGL build in `web/public/unity` IS committed on purpose (hosts never run Unity).

## Architecture = HYBRID (shipping setup)
- **Gameplay = Unity.** `unity_game/Assets/Scenes/Course.unity` → WebGL build → `web/public/unity/`, embedded. IS the game (movement/camera/obstacle/materials/FX). NEVER hand-port gameplay to Three.js (see memory.md #1).
- **Front-end = JS (`web/`).** Flow: Identity → Start menu → Lobby → JOIN → Unity game → standings → Lobby.
  - `web/src/main.ts` — state machine identity|startmenu|lobby|unity (`setState`).
  - `web/src/ui/{startMenu,lobby,clothing,identityScreen,waitlist,standings,howto,playerHud,countdown}.ts` — DOM overlays.
  - `web/src/unityGame.ts` — loads/shows/hides Unity WebGL build; `applyLook` → Unity; how-to card per launch.
  - `web/src/{bean,customization}.ts` + `data/beanPalette.ts` — Three.js bean PREVIEW (lobby+customizer). `BeanLook` v2 JSON in `localStorage beanLook.v2` (NFT-shaped).
  - `web/src/{net,profile,netTypes}.ts` + `server/` — multiplayer (see [[multiplayer-v1]]).
  - Assets: `web/public/models/{bean,hats,accessories,sidekick,headwear,pedestal}.glb`, `web/public/{ui,video,textures}`.
- Look (body color/face/hat/hair/glasses/faceAcc) loads on BOTH lobby preview AND in-match Unity bean — `unityGame.applyLook` → `WebBridge.ApplyLook` on JOIN + Save.

## Course.unity
- Char: ObstacleCoursePack `CharacterControls` capsule + `CameraManager` (mouse-look, dist 10) + `KillZone` (fall→respawn). Bean visual = FREE Party Characters `character_default` (Generic rig); anims `BeanLocomotion.controller` (idle/walk/jump/StandUp*) via `BeanWalkDriver.cs`. Spawn ≈(190,13,16).
- Obstacle: `Draft_3` spinning arena — `RoundArenaDeck`+2 bumper rings, `RotatingHub_CenterDrum`, `SweepArm_Upper` (violet full beam, head height, PUSH-only s27) + `SweepArm_Lower` (green half beam, jump-over, ragdoll), both `SpinningBeamHazard.cs`. Mats `Mat_Obstacle_*`/`Arena_*`.
- X-ray: `XRayFill`/`XRayMask` shaders + `Mat_BeanXRay` = cyan silhouette through occluders.
- Env: lava disk (AQUIS), `Cliff_Layout` (Sky Den), Quaternius trees, `PolygonCity_Billboards` (Solana signs). Sky = Polyverse cubemap (BOXOPHOBIC).

## Asset packages
FREE Party Characters (bean) · ObstacleCoursePack (controller+hazards) · POLY STYLE Platformer · ithappy Creative_Characters_FREE (accessories) · Synty Sidekick FREE (hairs/headwear) · Skyden_Games (cliffs) · Palmov Island (trees) · PolygonCity (billboards) · BOXOPHOBIC skybox · AquisWater · Quaternius Platformer (decor) · GabrielAguiar VFX · Barmetler Road System · Stylish Cannon Pack · Mixamo (anim-bake source). MCPForUnity = bridge.

## DONE (1-liners; how-to lives in memory)
- **Double jump** (s19–s20) — mid-air front-roll 2nd jump, Spinner/LMS/RollOut. [[double-jump]]
- **Look-bridge** — saved outfit loads in-match (body hex, face tex, accessories on `customize_objects`); REG in `WebBridge`.
- **Ragdoll on beam hit** + get-up; faster Spinner get-up. Water/lava, mouse-sensitivity slider, XRay glow fixes.
- **Faces** — 12 named expressions, texture-name based.
- **Music + SFX** — lobby + per-mode Strudel tracks, volume sliders, jump SFX. [[web-strudel-music]]
- **Match intro** — shared 3·2·1·GO! all modes, LMS drop-in start. [[match-intro-countdown]]
- **Spinner difficulty ramp** — beams escalate/reverse; violet beam PUSH-only (s27). [[spinner-difficulty-ramp]]
- **Game modes** — lobby = 4 cards (Spinner/LMS/Roll Out live, Platform Race "Coming Soon"); `LoadGameScene` swap; build scenes `[Boot, Course, LastManStanding, RollOut]`.
- **LMS scene** — hex arena 1655 tiles, vanish-on-step, `EliminationZone`→lobby, meteor-rain ring, night skybox.
- **Asset packs + VFX** (s12) — Quaternius decor, sakura swap, water→lava, meteor rain. [[asset-packs-added]] (★scene-switch gotcha: [[unity-mcp-scene-switch-unsaved]])
- **Scene separation** (s11) — ONE scene per mode (Spinner=`Course`, LMS=`LastManStanding`, Race=`ObstacleCourse`, RollOut=`RollOut`, loader=`Boot` idx0); `Tools/Scenes` menu opens EXCLUSIVELY — NEVER open mode scenes additively. [[scene-separation]]
- **Sidekick hairs 1–13 + headwear ×4** — bakers `SidekickHairBaker`/`SidekickHeadwearBaker`; headwear = REAL atlas colors (s27: orange pumpkin, kitsune fox, steel+plume warrior, charcoal assassin; ★UnityGLTF-JPEG-smears-1px-cells gotcha). [[sidekick-showroom]]
- **Roll Out mode** (s15–s22) — 4th mode, 5 rolling bands + walls/gaps/cells/hammers, difficulty ramp, Earth backdrop, JOIN-able. ★DON'T rerun `Generate Roll Out` (cells hand-edited); arrows = additive `Tools/Course/Add Roll Out Arrows` (s27: 150). [[rollout-mode]]
- **s27 (2026-07-05)** — Roll Out blurb de-candied; per-mode **How-to-Play card** (`web/src/ui/howto.ts`, every launch, hides on first countdown tick); Roll Out arrows 90→150; **mouse-look stuck FIXED** (`CameraManager`: deltas accumulated in Update not FixedUpdate + yaw wrapped); violet beam push-only; headwear real colors.
- **s28b (2026-07-05)** — **LMS remote start hexes** (every client now renders EVERY player's floating countdown hex: `NetBridge.RemoteSpawnIndices()` + `LmsStartController` clones per remote spawnIndex — re-seed `Random.InitState(seed)` before each `LmsTileIndex` call; remote hexes collider-off; all vanish on synced GO). **PRODUCTION QUEUE RULES**: per-mode `MODE_RULES` in `server/src/config.ts` — LMS cap 20, Spinner/RollOut cap 15, countdown arms at **6** queued → **60s** → starts with whoever's left (≥2; solo cancels+rewaits), full lobby = instant; `queueUpdate` carries `minToStart`; waitlist UI 4th state "Waiting for players — starts when 6 join"; **dev overrides REMOVED from `start-multiplayer.bat` + `_spin.bat`** (env `MIN_TO_START`/`FILL_COUNTDOWN_MS`/`*_CAPACITY` still work for load tests). Verified 8/8 bot-driven scenario checks + real 20-player LMS match (20/20 ready). Bot fleet scripts live in scratchpad (NOT repo): `lms-bots.mjs`, `queue-rules-test.mjs`.
- **s28 (2026-07-05)** — **pointer lock FIXED** (mouse no longer edge-sticks in browser: `Cursor.lockState=Locked` in `CharacterControls.Awake` + click re-lock in `CameraManager` + unlock on Boot in `WebBridge.LoadGameScene` + JS gesture fallback/`exitPointerLock` in `unityGame.ts`); **cell zap punish-up** (push 7, upPop 2.5, slow 0.65×2s — CellZap.cs + 30 RollOut instances + builder); **how-to card ≥7s** (`MIN_HOWTO_MS` delays `net.sendReady`; server BEGIN_TIMEOUT=12s is the ceiling); **charCodeAt alert KILLED** (= Unity engine focus-handler bug, suppressed via loader `errorHandler` + numeric SendMessage guards); **−7.3MB WebGL** (Synty `Resources/Meshes` → `Meshes`, baker paths updated; data 29.7→22.4MB).
- **MULTIPLAYER v1 LIVE** (s23–s26) — identity → per-mode waitlist → synced start → LIVE avatars (pose sync 15Hz, remote ragdoll, remote double-jump, look sync) → standings; player HUD; lobby chat. Cross-network 2-machine verified. Runs via `start-multiplayer.bat` (Node server + cloudflared quick-tunnel — NOT a real deploy). ★RESTART server after editing `server/src` (stale-dist gotcha). Fling-feel knobs: `RemoteRagdoll.TumbleImpulse`/`TumbleUpBias`, `RagdollController.LastFlingVel`. [[multiplayer-v1]]

## TODO (priority)

> **NEXT PRIORITIES (s27, 2026-07-05):** (1) **real server deploy** (off cloudflared quick-tunnel — Railway/Fly/Render, `wss://` + `VITE_WS_URL` + `ALLOWED_ORIGIN`); (2) **Solana on-chain payout** (item 2 — address collected, no transfer yet); (3) **Mode 3 Obstacle Course Race** — LAST priority per user.

★★★ #1 (within mode 3) — **FINISH THE ZIGZAG / SERPENTINE TRACK (match reference image).** Built with **Barmetler Road System** ([[road-system-asset]]): `SerpentineRoad` = 4-lane road snake (smooth 180° U-turns, ascending +9u, walkable MeshCollider) in `ObstacleCourse.unity`, built from script (Clear→RefreshEndPoints→AppendSegment→AutoSetControlPoints→GenerateRoadMesh). Still TODO: **START section** (flat pad + chevrons, curve-connects to LOW end) + **FINISH section** (checkered strip + orange arch, HIGH end); **recolor** grey asphalt → candy/bright (tint `RS_Road_mat`); wire finish line; decide replace-vs-join with rest of course. Then delete OLD hand-mesh `SerpentineTrack` + `SerpentineTrackBuilder.cs` (kept as fallback). User can hand-extend via `Tools/RoadSystem/` (Extrude = Ctrl+Shift+E).

★★ CURB-WALL HAZARDS — 3 telescoping walls (`SHOW_MovableWall`, `(2)`, `(3)`) on `SerpentineRoad` (`CurbWallHazard.cs`). **REMAINING: anchor extrude-out/retract to each wall's CURB-RAIL EDGE** (per-wall `clipPoint`/`moveDir` vs actual curb edge — current anchors look off). Optional: taller. ★cuts/backing baked to wall pos+scale at edit time → user moves wall = misalign; need auto-fit helper reading wall bounds. [[curb-wall-hazards]]

★★ **MODE 3: Obstacle Course Race (IN PROGRESS, `Scenes/ObstacleCourse.unity`).**
   - Done so far: bean transplanted (★`CharacterControls.cam` must point at rig's camera), 16 spinning disks, StepLane section, Quaternius decor, pendulums→hammers (s14, [[pendulum-hazards]]). CAD level GLBs at `Assets/CAD_Level/` (imported, unplaced).
   - TODO: remove leftover OLD ball-course clutter (`Course` root `1_Start`…`7_Finish`); keep building sections; wire finish line + Build Settings (`[Boot, Course, LastManStanding, RollOut, ObstacleCourse]`) + lobby card + `SCENE_BY_MODE` entry when it ships.
   - ★ **RACE OBSTACLE TODO (user-dictated; track order: Hammers → Cannons → Hexes → Orbs → last 2 curves):**
     - **A. CURB-RAIL CUT EDGES — cap open ends (all sections).** Everywhere rails are cut, cap the exposed cross-section (quad faces across open profile). Do NOT re-fill the gaps.
     - **B. Movable walls** — see ★★ CURB-WALL above (anchor to curb edge, all 3).
     - **D. CANNON SECTION (after hammers, before hexes):** cannons from Stylish Cannon Pack (16 in `Cannon_Showroom`, USER picks/arranges — DON'T touch per [[dont-revert-user-edits]]). Fire spheres that push + ragdoll (beam-style); spheres despawn within section. Config: fire rate, start timing, aim/arc, sphere speed + lifetime.
     - **E. HEX SECTION (`HexBridge_MCurb4`, `HexTile.cs`):** better alignment; slightly smaller hexes; longer `disappearDelay`; LATER auto-respawn (`ResetTile()` after delay — method exists). (User hand-editing layout — coordinate first.)
     - **F. MOVABLE ORBS (after hexes):** orb hit = ragdoll + push (beam-style); cut curb-rails this section too (+ cap per A).
     - **G. LAST TWO CURVES:** obstacles TBD.

~~0b/0c/1b/1e dropped by user (s28); 1c+1d DONE s28.~~
2. **Solana payout + wallet:** identity screen already collects address (`playerProfile.v1`, base58-validated), winner's address shows in standings — NO on-chain transfer yet. Needs funded payer wallet + `@solana/web3.js` server-side + key mgmt + net choice + security review. Later: wallet connect (Phantom), NFT skin from `BeanLook`, on-chain leaderboard.
3. **Multiplayer remaining:** (a) DEPLOY = **Railway via GitHub (IN PROGRESS s29)** — repo pushed + README runbook + Dockerfile committed; user connects repo on railway.app → Generate Domain → set `ALLOWED_ORIGIN=<domain>`; NO `VITE_WS_URL` needed (net.ts auto same-origin wss); (b) optional private room codes. Queue rules are production-set (s28b). Core is live — see DONE + [[multiplayer-v1]].
3b. Platform Race mode — PARKED (user: "no need for now", s28). Seed scene `Scenes/PlatformRace.unity` exists if revived.
4. More course content (sections/obstacles/checkpoints); tune feel.
5. Prod hosting: serve `.unityweb` with `Content-Encoding: br` (or rebuild uncompressed).
6. Polish: Settings live; transitions.

## Rebuild WebGL (after editing any scene)
`manage_build` target=webgl, scenes=`[Boot.unity, Course.unity, LastManStanding.unity, RollOut.unity]` (Boot MUST be index 0), out=`web/public/unity`. ~7–15min IL2CPP; MCP call times out but build CONTINUES — poll `web/public/unity/Build/` mtimes (ALL 4 files fresh + wasm/data >5MB; partial mid-write looks done but isn't). Then `npm run build` in `web/` (dist!) + restart `start-multiplayer.bat`. Build is SYNCHRONOUS → can't cancel mid-build.
