import * as THREE from "three";
import { GLTFLoader } from "three/examples/jsm/loaders/GLTFLoader.js";
import { Bean } from "./bean";
import { loadLook, applyLook } from "./customization";
import { ClothingUI } from "./ui/clothing";
import { StartMenu } from "./ui/startMenu";
import { Lobby, type GameMode } from "./ui/lobby";
import { SettingsUI, loadSettings } from "./ui/settings";
import { unityGame } from "./unityGame";
import { musicController } from "./ui/musicController";
import { IdentityScreen } from "./ui/identityScreen";
import { Waitlist } from "./ui/waitlist";
import { Standings } from "./ui/standings";
import { net } from "./net";
import { loadProfile } from "./profile";
import { setPlayerHudRoster } from "./ui/playerHud";
import { removeSpectatorPlayers } from "./ui/spectator";
import { setHowToStatus } from "./ui/howto";
import type { WatchStartMsg } from "./netTypes";

// HYBRID app: JS shell (identity + start menu + lobby + customizer, with a Three.js bean preview) wraps
// the real Unity WebGL game. Multiplayer v1: JOIN enters a per-mode waitlist on the server; when the
// room starts, the Unity build launches; on match end results are reported and standings shown.
type AppState = "identity" | "startmenu" | "lobby" | "unity" | "spectate";

interface MatchCtx { seed: number; startAtEpochMs: number; matchId: string; myId: string | null; matchInfoJson: string; }

const LOBBY_POS = new THREE.Vector3(0, 2.05, 0);
const clamp = (v: number, a: number, b: number) => (v < a ? a : v > b ? b : v);

async function main() {
  const bean = await Bean.load().catch((e) => {
    console.warn("Bean load failed, using placeholder:", e);
    return null;
  });

  const canvas = document.getElementById("app") as HTMLCanvasElement;
  const renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true });
  renderer.setSize(window.innerWidth, window.innerHeight);
  renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
  renderer.shadowMap.enabled = true;
  renderer.shadowMap.type = THREE.PCFSoftShadowMap;
  renderer.setClearColor(0x000000, 0);

  const scene = new THREE.Scene();
  // sky env gives the lobby bean nice reflections (background stays transparent for the video/art)
  const skyTex = new THREE.TextureLoader().load("/textures/sky/sky_equirect.png");
  skyTex.mapping = THREE.EquirectangularReflectionMapping;
  skyTex.colorSpace = THREE.SRGBColorSpace;
  scene.environment = skyTex;

  const sun = new THREE.DirectionalLight(0xfff4d6, 2.2);
  sun.position.set(-4, 8, 6);
  sun.castShadow = true;
  scene.add(sun);
  scene.add(new THREE.HemisphereLight(0xc9d1d1, 0x223066, 0.95));

  const camera = new THREE.PerspectiveCamera(50, window.innerWidth / window.innerHeight, 0.1, 100);

  // lobby/customize bean preview (visual only — no physics in the shell)
  const beanRoot = new THREE.Group();
  if (bean) beanRoot.add(bean.group);
  scene.add(beanRoot);
  if (bean) applyLook(bean, loadLook());

  const pedestal = await loadPedestal();
  pedestal.position.set(LOBBY_POS.x, 0, LOBBY_POS.z);
  scene.add(pedestal);
  const pedTop = new THREE.Box3().setFromObject(pedestal).max.y;
  beanRoot.position.set(LOBBY_POS.x, pedTop + 0.9, LOBBY_POS.z);

  // orbit camera (lobby idle + customize tuning)
  let customizing = false;
  let yaw = 0, pitch = 0.12, dist = 3.0;
  const keys = new Set<string>();
  window.addEventListener("keydown", (e) => keys.add(e.code));
  window.addEventListener("keyup", (e) => keys.delete(e.code));
  const canOrbit = () => appState === "lobby";
  let drag = false, lx = 0, ly = 0;
  canvas.addEventListener("pointerdown", (e) => { if (canOrbit()) { drag = true; lx = e.clientX; ly = e.clientY; } });
  window.addEventListener("pointerup", () => (drag = false));
  window.addEventListener("pointermove", (e) => {
    if (!drag || !canOrbit()) return;
    yaw -= (e.clientX - lx) * 0.01;
    pitch = clamp(pitch + (e.clientY - ly) * 0.01, -1.3, 1.3);
    lx = e.clientX; ly = e.clientY;
  });
  canvas.addEventListener("wheel", (e) => {
    if (!canOrbit()) return;
    dist = clamp(dist + e.deltaY * 0.002, 1.2, 8);
    e.preventDefault();
  }, { passive: false });

  // customizer (Clothing nav). Freezes the bean + hides JOIN while open.
  let clothingUI: ClothingUI | null = null;
  if (bean) {
    clothingUI = new ClothingUI(bean, {
      onOpen: () => { customizing = true; yaw = 0; pitch = 0.12; dist = 3.0; bean.setPaused(true); lobby.showJoin(false); },
      onClose: () => { customizing = false; bean.setPaused(false); lobby.setActive("play"); lobby.showJoin(true); },
      onSaved: (look) => {
        unityGame.applyLook(look); // push the outfit into an already-loaded game live
        net.updateLook(unityGame.lookPayload()); // + tell the server so remotes see the new outfit next match
      },
    });
    const cb = document.getElementById("customizeBtn") as HTMLElement | null;
    if (cb) cb.style.display = "none";
  }

  const settingsUI = new SettingsUI({
    onChange: (v) => unityGame.setMouseSensitivity(v), // live-update the running build
    onMusicChange: (v) => musicController.setMusicVolume(v), // master music volume (web Strudel)
    onSfxChange: (v) => unityGame.setSfxVolume(v), // bean SFX volume → Unity
    onClose: () => lobby.setActive("play"),
  });
  // Apply the saved music volume up front so it's active before the first track plays.
  musicController.setMusicVolume(loadSettings().musicVolume);

  const identityScreen = new IdentityScreen((profile) => {
    net.identify(profile.nick, profile.solAddress, unityGame.lookPayload());
    lobby.setPlayerName(profile.nick);
    setState("startmenu");
  });
  const startMenu = new StartMenu(() => setState("lobby"));
  const lobby = new Lobby({
    onClothing: () => clothingUI?.show(),
    onJoin: (mode) => enterQueue(mode),
    onWatch: (mode) => {
      // Watching replaces any queue intent (server auto-leaves too; mirror it client-side).
      net.leaveQueue();
      waitlist.hide();
      net.watchMatch(mode);
    },
    onSettings: () => settingsUI.show(),
    onChat: (text) => net.sendChat(text),
  });
  const waitlist = new Waitlist(() => {
    net.leaveQueue();
    waitlist.hide();
  });
  const standings = new Standings(() => {
    standings.hide();
    lobby.setActive("play");
  });

  // seed the lobby name card from any saved profile (replaces the old hardcoded name)
  const savedProfile = loadProfile();
  if (savedProfile) lobby.setPlayerName(savedProfile.nick);

  // ---- multiplayer session state ----
  let myId: string | null = null;
  let currentMode: GameMode = "spinner";
  let matchCtx: MatchCtx | null = null;
  let spectateCtx: WatchStartMsg | null = null; // set on watchStart, drives the spectate launch
  let standingsShown = false; // guards the finish/standings ordering race
  let matchRoster: { id: string; nick: string }[] = []; // current match roster (shrinks on playersDropped)

  net.setHandlers({
    onIdentified: (id) => { myId = id; },
    onQueueUpdate: (m) => { if (appState === "lobby") waitlist.update(m, myId); },
    onMatchStart: (m) => {
      currentMode = m.mode;
      const matchInfoJson = JSON.stringify({
        seed: m.seed, startAtEpochMs: m.startAtEpochMs, matchId: m.matchId, myId, roster: m.roster,
      });
      matchCtx = { seed: m.seed, startAtEpochMs: m.startAtEpochMs, matchId: m.matchId, myId, matchInfoJson };
      standingsShown = false;
      waitlist.hide();
      setState("unity", m.mode);
      // In-game player list (count + names, local player highlighted). setState launches Unity + mounts the HUD.
      matchRoster = m.roster.map((r) => ({ id: r.id, nick: r.nick }));
      setPlayerHudRoster(matchRoster, myId);
    },
    // Live avatars: forward each server snapshot straight to Unity.
    onSnapshot: (raw) => { unityGame.pushSnapshot(raw); },
    // Synchronized start: the server fixed an absolute GO instant — every client unfreezes at it.
    onBeginCountdown: (goAtEpochMs) => { unityGame.beginCountdown(goAtEpochMs); },
    // Loading progress while the room waits for everyone (slow first-visit downloads take a while).
    onReadyUpdate: (m) => {
      if (appState === "unity") setHowToStatus(`Waiting for players to load — ${m.ready}/${m.total} ready…`);
    },
    // Players were dropped from the match (never loaded, or AFK mid-match — hidden tab froze
    // their game): despawn their avatars + shrink the HUD.
    onPlayersDropped: (m) => {
      if (appState === "spectate") {
        removeSpectatorPlayers(m.ids);
        unityGame.pushPlayersDropped(JSON.stringify({ ids: m.ids }));
        return;
      }
      matchRoster = matchRoster.filter((r) => !m.ids.includes(r.id));
      setPlayerHudRoster(matchRoster, myId);
      // WE are one of the dropped (tab hidden mid-match → server AFK-eliminated us). Our result
      // is already recorded server-side as a loss — tear down the game and wait for standings.
      if (myId && m.ids.includes(myId)) {
        unityGame.hide();
        setState("lobby");
        if (!standingsShown) standings.showWaiting();
        return;
      }
      unityGame.pushPlayersDropped(JSON.stringify({ ids: m.ids }));
    },
    // A player's tab froze mid-match (poses stopped): every still-awake client takes their bean
    // over and simulates it as a normal idle player (falls through hexes / rolls off the log /
    // gets beamed) instead of leaving a frozen statue. Forward to Unity in both play + spectate.
    onPlayerStalled: (m) => {
      if (appState === "unity" || appState === "spectate") unityGame.pushPlayerStalled(JSON.stringify({ ids: m.ids }));
    },
    // The owner started streaming again before the AFK cutoff — hand their bean back to the
    // network stream (un-orphan it) so it follows their real pose again.
    onPlayerResumed: (m) => {
      if (appState === "unity" || appState === "spectate") unityGame.pushPlayerResumed(JSON.stringify({ ids: m.ids }));
    },
    // WE missed the start (tab hidden during load): server dropped + re-queued us. Leave the game
    // shell and show the waitlist for the next match.
    onMatchMissed: (m) => {
      currentMode = m.mode;
      unityGame.hide();
      setState("lobby");
      waitlist.show(m.mode);
      waitlist.setNotice("Missed the match start — you're in line for the next one");
    },
    // Match cancelled (<2 players ready) — we're back at the front of the queue.
    onMatchAborted: (m) => {
      currentMode = m.mode;
      unityGame.hide();
      setState("lobby");
      waitlist.show(m.mode);
      waitlist.setNotice("Not enough players were ready — waiting for more");
    },
    // Lobby chat line from the server (already scoped to lobby players) → render it.
    onChatMsg: (m) => { lobby.addChat(m.nick, m.text, m.id === myId); },
    // Standings always win: the server may end the match (grace/timeout) before our local bean falls,
    // so tear down Unity if it's still up and show the results.
    onStandings: (m) => {
      if (appState === "spectate") return; // watchers never get standings (defensive)
      standingsShown = true;
      unityGame.hide();
      if (appState !== "lobby") setState("lobby");
      standings.show(m, myId);
    },
    // Live server-browser state → lobby mode cards (players X/15, watch availability).
    onLobbyStatus: (m) => { lobby.updateStatus(m.modes); },
    // We're in as a spectator — launch the game shell in watch mode.
    onWatchStart: (m) => {
      spectateCtx = m;
      currentMode = m.mode;
      setState("spectate", m.mode);
    },
    // The watched match ended (or was aborted) — straight back to the lobby, no standings.
    onWatchEnd: () => { if (appState === "spectate") exitSpectate(false); },
    // LMS tiles vanished in the watched match — keep our world state in sync.
    onHexVanish: (m) => { if (appState === "spectate") unityGame.pushHexVanish(JSON.stringify(m)); },
    // Watch denials → a transient note on that mode's card (next lobbyStatus refreshes it).
    onError: (m) => {
      if (m.code === "watchfull") lobby.flashStatus(currentMode, "Watch slots are full — try again soon");
      else if (m.code === "notrunning") lobby.flashStatus(currentMode, "No live match to watch right now");
    },
    // Connection dropped while spectating: the server already freed our seat — exit cleanly.
    onConnChange: (up) => { if (!up && appState === "spectate") exitSpectate(false); },
  });
  net.connect();

  // Tab restored while in a match: put keyboard focus back on the Unity canvas so input works
  // immediately (the game state itself is message-driven and already reconciled while hidden).
  document.addEventListener("visibilitychange", () => {
    if (!document.hidden && appState === "unity") {
      document.getElementById("unity-canvas")?.focus();
    }
  });

  // JOIN a mode → enter its server-side waitlist (Unity launches only on matchStart).
  function enterQueue(mode: GameMode) {
    currentMode = mode;
    net.joinQueue(mode);
    waitlist.show(mode);
  }

  // Leave spectating (Exit button = tell the server; watchEnd/disconnect = seat already gone).
  function exitSpectate(userInitiated: boolean) {
    if (userInitiated) net.stopWatching();
    spectateCtx = null;
    unityGame.hide();
    setState("lobby");
  }

  // Local match ended (fall/elimination in Unity, or manual "← Menu" forfeit). Report to the
  // server, tear down Unity, and wait for standings.
  function finishMatch(survivalMs: number, finished: boolean) {
    net.reportResult(currentMode, survivalMs, finished);
    unityGame.hide();
    setState("lobby");
    // Don't clobber standings that already arrived (server ended the match before our local fall).
    if (!standingsShown) standings.showWaiting();
  }

  let appState: AppState = "identity";

  function setState(s: AppState, mode: GameMode = "spinner") {
    appState = s;
    const inUnity = s === "unity" || s === "spectate";
    canvas.style.display = inUnity ? "none" : "block"; // Three only renders the menu/lobby preview
    pedestal.visible = s === "lobby";
    beanRoot.visible = !inUnity;
    if (s === "identity") {
      identityScreen.show(); startMenu.hide(); lobby.hide(); unityGame.hide(); settingsUI.hide();
      musicController.stop();
    } else if (s === "startmenu") {
      identityScreen.hide(); startMenu.show(); lobby.hide(); unityGame.hide(); settingsUI.hide();
      musicController.stop();
    } else if (s === "lobby") {
      identityScreen.hide(); startMenu.hide(); lobby.show(); unityGame.hide(); settingsUI.hide(); lobby.setActive("play");
      customizing = false; clothingUI?.hide();
      yaw = 0; pitch = 0.12; dist = 3.0; bean?.setPaused(false);
      musicController.startLobby(); // the click that entered the lobby is the audio-unlocking gesture
    } else if (s === "spectate") {
      // watch a live match — same Unity shell, spectator config, DOM overlay controls.
      startMenu.hide(); lobby.hide(); clothingUI?.hide(); settingsUI.hide(); waitlist.hide();
      musicController.stop();
      if (spectateCtx) {
        const m = spectateCtx;
        const matchInfoJson = JSON.stringify({
          seed: m.seed, startAtEpochMs: m.startAtEpochMs, matchId: m.matchId,
          myId, // our id is NOT in the roster → every entry renders as a remote bean
          roster: m.roster,
          vanishedHexes: m.vanishedHexes,
        });
        unityGame
          .launchSpectate(
            m.mode,
            {
              seed: m.seed,
              startAtEpochMs: m.startAtEpochMs,
              goAtEpochMs: m.goAtEpochMs,
              matchId: m.matchId,
              matchInfoJson,
              roster: m.roster.map((r) => ({ id: r.id, nick: r.nick })),
            },
            () => exitSpectate(true),
          )
          .catch((e) => console.error("Spectate launch failed:", e));
      }
    } else {
      // unity gameplay — load on matchStart, swap to the chosen mode's scene with the shared match config.
      // Stop the lobby music the moment the game loads; the in-game track starts on the countdown's first tick.
      startMenu.hide(); lobby.hide(); clothingUI?.hide(); settingsUI.hide(); waitlist.hide();
      musicController.stop();
      unityGame
        .launch(
          () => finishMatch(0, false), // "← Menu" = forfeit
          mode,
          matchCtx ?? undefined,
          (surv, fin) => finishMatch(surv, fin), // Unity reported a real result
        )
        .catch((e) => console.error("Unity launch failed:", e));
    }
  }

  window.addEventListener("resize", () => {
    camera.aspect = window.innerWidth / window.innerHeight;
    camera.updateProjectionMatrix();
    renderer.setSize(window.innerWidth, window.innerHeight);
  });

  const clock = new THREE.Clock();
  function frame() {
    requestAnimationFrame(frame);
    const dt = Math.min(clock.getDelta(), 0.1);
    if (appState === "unity" || appState === "spectate") return; // Unity renders its own canvas

    if (appState === "lobby") {
      const r = dt * 1.8;
      if (customizing) {
        if (keys.has("ArrowLeft") || keys.has("KeyA")) yaw += r;
        if (keys.has("ArrowRight") || keys.has("KeyD")) yaw -= r;
        if (keys.has("ArrowUp") || keys.has("KeyW")) pitch = Math.min(1.3, pitch + r);
        if (keys.has("ArrowDown") || keys.has("KeyS")) pitch = Math.max(-1.3, pitch - r);
      }
      const p = beanRoot.position, ty = p.y + 0.45, cp = Math.cos(pitch);
      camera.position.set(p.x + dist * Math.sin(yaw) * cp, ty + dist * Math.sin(pitch), p.z + dist * Math.cos(yaw) * cp);
      camera.lookAt(p.x, ty, p.z);
      try { bean?.update(dt); } catch {}
    }
    renderer.render(scene, camera);
  }

  setState("identity");
  frame();
}

async function loadPedestal(): Promise<THREE.Object3D> {
  const fit = (o: THREE.Object3D): THREE.Object3D => {
    o.traverse((m) => { const mesh = m as THREE.Mesh; if ((mesh as any).isMesh) { mesh.castShadow = true; mesh.receiveShadow = true; } });
    const box = new THREE.Box3().setFromObject(o);
    const size = new THREE.Vector3(); box.getSize(size);
    const horiz = Math.max(size.x, size.z) || 1;
    o.scale.setScalar(2.6 / horiz);
    const box2 = new THREE.Box3().setFromObject(o);
    o.position.y -= box2.min.y;
    return o;
  };
  try {
    const gltf = await new GLTFLoader().loadAsync("/models/pedestal.glb");
    return fit(gltf.scene);
  } catch {
    const grp = new THREE.Group();
    const body = new THREE.Mesh(new THREE.CylinderGeometry(1.1, 1.35, 1.0, 48),
      new THREE.MeshStandardMaterial({ color: 0x9945ff, roughness: 0.25, metalness: 0.1 }));
    body.position.y = 0.5; body.castShadow = true; body.receiveShadow = true;
    const top = new THREE.Mesh(new THREE.CylinderGeometry(1.2, 1.2, 0.14, 48),
      new THREE.MeshStandardMaterial({ color: 0xc39bff, roughness: 0.12, metalness: 0.2 }));
    top.position.y = 1.07; top.castShadow = true; top.receiveShadow = true;
    grp.add(body, top);
    return grp;
  }
}

main().catch((err) => {
  console.error(err);
  document.body.innerHTML = `<pre style="color:#fff;padding:20px">${err}</pre>`;
});
