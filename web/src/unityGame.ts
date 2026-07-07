// Embeds the Unity WebGL build (the real gameplay) under the JS UI shell.
// Lazy-loads on first JOIN; show()/hide() toggle it; quit() unloads. A "← Menu" button returns to the lobby.
// The Unity build lives in web/public/unity/Build/ (served at /unity/Build/). Filenames follow the build
// folder name "unity": unity.{loader.js,data,framework.js,wasm}.

import { loadSettings } from "./ui/settings";
import { BeanLook, loadLook } from "./customization";
import { FACES } from "./data/beanPalette";
import type { GameMode } from "./ui/lobby";
import { initCountdown, hideCountdown } from "./ui/countdown";
import { showHowTo, hideHowTo } from "./ui/howto";
import { initPlayerHud, hidePlayerHud } from "./ui/playerHud";
import { showSpectatorHud, hideSpectatorHud, setSpectatorState } from "./ui/spectator";
import { net } from "./net";
import { musicController } from "./ui/musicController";

const BUILD = "/unity/Build";
const NAME = "unity";

// Game mode → Unity scene name (must match Assets/Scenes/*.unity in the build).
const SCENE_BY_MODE: Record<GameMode, string> = {
  spinner: "Course",
  lastman: "LastManStanding",
  rollout: "RollOut",
};

let instance: any = null;
let loading: Promise<any> | null = null;
let container: HTMLDivElement | null = null;
let onBackFn: (() => void) | null = null;

// Minimum time the per-mode "How to Play" card stays up before we report ready to the server.
// Fast loads would otherwise flash the card for under a second; slow loads already exceed this
// (load time counts toward it). The countdown stays server-synced — we just delay OUR ready.
const MIN_HOWTO_MS = 7000;
let howToShownAt = 0;
let readyTimer: number | null = null;
// Launch-session token: bumped on every launch()/hide(). A launch continuation that awaits the
// (slow) build download must not re-show the canvas if the match was missed/torn down meanwhile.
let session = 0;
// Spectating: no local bean, OS cursor stays free for the overlay (pointer-lock is skipped) —
// EXCEPT while the free-fly camera is active, which needs mouse-look (Unity locks the cursor;
// the gesture fallback below may re-lock after a browser Esc).
let spectating = false;
let specFreeCam = false;

function injectStyles() {
  if (document.getElementById("unityStyles")) return;
  const css = `
  #unityContainer{position:fixed;inset:0;z-index:35;display:none;background:#0b0716}
  #unityContainer.show{display:block}
  #unity-canvas{width:100%;height:100%;display:block}
  #unityLoad{position:fixed;inset:0;z-index:36;display:none;flex-direction:column;align-items:center;
    justify-content:center;background:#1a0f33;color:#fff;font:700 18px/1.4 system-ui,sans-serif;gap:16px}
  #unityLoad.show{display:flex}
  #unityLoad .bar{width:min(60vw,420px);height:14px;border-radius:8px;background:rgba(255,255,255,.15);overflow:hidden}
  #unityLoad .fill{height:100%;width:0;background:linear-gradient(90deg,#ff2d9e,#1ac7c7);transition:width .15s ease}
  `;
  const s = document.createElement("style");
  s.id = "unityStyles";
  s.textContent = css;
  document.head.appendChild(s);
}

function buildDom() {
  injectStyles();
  container = document.createElement("div");
  container.id = "unityContainer";
  const canvas = document.createElement("canvas");
  canvas.id = "unity-canvas";
  canvas.tabIndex = 0;
  container.appendChild(canvas);
  document.body.appendChild(container);

  const load = document.createElement("div");
  load.id = "unityLoad";
  load.innerHTML = `<div>Loading game…</div><div class="bar"><div class="fill"></div></div>`;
  document.body.appendChild(load);

  // (In-game "← Menu" button removed — exit is via elimination / match end only.)

  // Pointer-lock fallback: Unity (Cursor.lockState=Locked) only engages the browser lock from its own
  // mousedown handler — a player who pans without ever clicking would still edge-clamp. Request the lock
  // on ANY gesture (keydown counts as user activation in modern browsers) while the game is visible.
  const tryLock = () => {
    if (spectating && !specFreeCam) return; // spectator overlay needs the OS cursor (free cam doesn't)
    if (!container?.classList.contains("show")) return;
    if (document.pointerLockElement === canvas) return;
    try { (canvas.requestPointerLock() as any)?.catch?.(() => {}); } catch { /* gesture rejected — click will lock */ }
  };
  canvas.addEventListener("mousedown", tryLock);
  window.addEventListener("keydown", tryLock);
  // Spectator right-click cycles players — never open the browser context menu over the game.
  container.addEventListener("contextmenu", (e) => { if (spectating) e.preventDefault(); });
  // Spectator Tab = switch player cam ↔ free cam. Capture phase + preventDefault so it never
  // tab-navigates the DOM; handled here (not Unity) so it works regardless of canvas focus.
  window.addEventListener(
    "keydown",
    (e) => {
      if (!spectating || e.key !== "Tab") return;
      e.preventDefault();
      instance?.SendMessage("NetBridge", "SpectateFreeCam", "toggle");
    },
    true,
  );
  return canvas;
}

function loadScript(src: string): Promise<void> {
  return new Promise((resolve, reject) => {
    const s = document.createElement("script");
    s.src = src;
    s.onload = () => resolve();
    s.onerror = () => reject(new Error("failed to load " + src));
    document.body.appendChild(s);
  });
}

// Start loading the Unity build (idempotent). Resolves with the unity instance.
function ensureLoaded(): Promise<any> {
  if (loading) return loading;
  const canvas = buildDom();
  const loadEl = document.getElementById("unityLoad")!;
  const fill = loadEl.querySelector(".fill") as HTMLElement;
  loadEl.classList.add("show");
  loading = loadScript(`${BUILD}/${NAME}.loader.js`)
    .then(() =>
      (window as any).createUnityInstance(canvas, {
        dataUrl: `${BUILD}/${NAME}.data.unityweb`,
        frameworkUrl: `${BUILD}/${NAME}.framework.js.unityweb`,
        codeUrl: `${BUILD}/${NAME}.wasm.unityweb`,
        streamingAssetsUrl: "/unity/StreamingAssets",
        companyName: "DefaultCompany",
        productName: "unity_game",
        productVersion: "0.1.0",
        // Known-harmless Unity WebGL engine bug: its internal focus/blur handler throws
        // "str.charCodeAt is not a function" on window blur (Windows key / tab-away / page nav)
        // and pops a scary alert(). Swallow ONLY that one; everything else surfaces normally.
        errorHandler: (msg: any) => {
          if (typeof msg === "string" && msg.includes("str.charCodeAt is not a function")) {
            console.warn("[unity] suppressed known focus-handler engine bug:", msg);
            return true;
          }
          return false;
        },
      }, (p: number) => { fill.style.width = `${Math.round(p * 100)}%`; })
    )
    .then((inst: any) => {
      instance = inst;
      loadEl.classList.remove("show");
      return inst;
    })
    .catch((e) => {
      loadEl.innerHTML = `<div style="color:#ff6b6b;max-width:60vw;text-align:center">Game failed to load.<br><small>${e}</small></div>`;
      throw e;
    });
  return loading;
}

export const unityGame = {
  // Launch (load on first call) + show, then load the chosen mode's scene.
  //   onBack   = "← Menu" pressed (multiplayer: forfeit).
  //   matchCtx = shared match config from the server (seed + start time) for a synced/fair run.
  //   onResult = the Unity match ended and reported a result (survivalMs, finished).
  async launch(
    onBack: () => void,
    mode: GameMode = "spinner",
    matchCtx?: { seed: number; startAtEpochMs: number; matchId: string; myId?: string | null; matchInfoJson?: string },
    onResult?: (survivalMs: number, finished: boolean) => void,
  ) {
    onBackFn = onBack;
    // Multiplayer: Unity reports the match result (survival time / finish) via LmsBridge.jslib.
    (window as any).__unityMatchResult = (r: { survivalMs?: number; finished?: boolean }) => {
      onResult?.(Number(r?.survivalMs) || 0, !!r?.finished);
    };
    spectating = false;
    // Live avatars: Unity streams the local bean's pose (raw JSON) → server ~15 Hz.
    (window as any).__unityNetSend = (s: string) => net.sendState(s);
    // LMS spectator hex sync: our LOCAL bean stepped a hex — report the tile index.
    (window as any).__unityHexVanish = (idx: number) => net.sendHexVanish(idx);
    // Synchronized start: Unity's IntroCountdown tells us the scene is loaded + frozen → we report ready.
    // Hold the ready until the how-to card has been visible ≥ MIN_HOWTO_MS so players always get a
    // moment to read it (server starts 3·2·1 only once EVERY player reported ready).
    (window as any).__unityReady = () => {
      // Hidden tab: skip the how-to dwell entirely — nobody is reading it, background timers are
      // throttled, and a late ready gets this player DROPPED from the match by the server.
      const wait = document.hidden ? 0 : Math.max(0, MIN_HOWTO_MS - (Date.now() - howToShownAt));
      if (readyTimer != null) clearTimeout(readyTimer);
      readyTimer = window.setTimeout(() => { readyTimer = null; net.sendReady(); }, wait);
    };
    // Legacy single-player game-over (unused while all matches are multiplayer): return to lobby.
    (window as any).__unityGameOver = () => onBackFn?.();
    // Per-mode "How to Play" card — covers the load + waiting-for-players window on EVERY launch,
    // dropped on the countdown's first tick so it never hides 3·2·1·GO.
    showHowTo(mode);
    howToShownAt = Date.now();
    // 3·2·1·GO! overlay — Unity's IntroCountdown drives window.__unityCountdown via the jslib.
    // Music starts on the FIRST countdown tick ("3"), in sync across all players — not on the load screen.
    initCountdown(() => { musicController.startGame(mode); hideHowTo(); });
    initPlayerHud(); // in-game player list; roster/presence pushed from main.ts
    const mySession = ++session;
    await ensureLoaded();
    if (mySession !== session) return; // match missed/torn down while the build was downloading
    container?.classList.add("show");
    document.getElementById("unity-canvas")?.focus();
    // Boot scene is loaded first; switch to the gameplay scene for this mode.
    instance?.SendMessage("WebBridge", "LoadGameScene", SCENE_BY_MODE[mode] ?? "Course");
    // Push the shared match config (seed + start time + multiplayer flag). WebBridge caches this
    // statically, so the freshly loaded scene's bridge re-applies it in Start().
    instance?.SendMessage(
      "WebBridge",
      "SetMatchConfig",
      JSON.stringify({
        mode,
        multiplayer: true,
        seed: matchCtx?.seed ?? 0,
        startAtEpochMs: matchCtx?.startAtEpochMs ?? 0,
        matchId: matchCtx?.matchId ?? "",
      }),
    );
    // Live avatars: hand NetBridge the roster (each player's look + spawn slot) + my id. Cached
    // statically like SetMatchConfig so the gameplay scene reads it after the swap.
    if (matchCtx?.matchInfoJson) instance?.SendMessage("NetBridge", "OnMatchInfo", matchCtx.matchInfoJson);
    // Push the saved mouse sensitivity + outfit. WebBridge caches these statically, so the freshly
    // loaded scene's bridge re-applies them in Start() even though this hits the Boot bridge first.
    unityGame.setMouseSensitivity(loadSettings().mouseSensitivity);
    unityGame.setSfxVolume(loadSettings().sfxVolume);
    unityGame.applyLook(loadLook());
  },

  // Spectate a running match: same Unity build, but SetMatchConfig carries spectator:true —
  // the scene deactivates the local Player, renders the whole roster as remote beans and drives
  // a free overhead/chase camera. No how-to card, no countdown overlay, no ready handshake,
  // no result reporting. The DOM spectator HUD supplies focus/wide/exit controls.
  async launchSpectate(
    mode: GameMode,
    ctx: {
      seed: number;
      startAtEpochMs: number;
      goAtEpochMs: number;
      matchId: string;
      matchInfoJson: string;
      roster: { id: string; nick: string }[];
    },
    onExit: () => void,
  ) {
    spectating = true;
    specFreeCam = false;
    // Unity's SpectatorCamera reports every mode/focus change — keep the overlay + lock policy in sync.
    (window as any).__unitySpectateState = (s: { mode?: string; id?: string }) => {
      specFreeCam = s?.mode === "free";
      setSpectatorState(s || {});
    };
    const mySession = ++session;
    await ensureLoaded();
    if (mySession !== session) return; // match ended / user left while the build was downloading
    container?.classList.add("show");
    // Spectator keeps the OS cursor — release a stale lock from a previous played match.
    if (document.pointerLockElement) document.exitPointerLock();
    instance?.SendMessage("WebBridge", "LoadGameScene", SCENE_BY_MODE[mode] ?? "Course");
    instance?.SendMessage(
      "WebBridge",
      "SetMatchConfig",
      JSON.stringify({
        mode,
        multiplayer: true,
        spectator: true,
        seed: ctx.seed,
        startAtEpochMs: ctx.startAtEpochMs,
        matchId: ctx.matchId,
      }),
    );
    // Full roster (with looks + spawn slots + vanished hexes); our id is absent from it, so
    // NetBridge renders EVERY entry as a remote bean.
    instance?.SendMessage("NetBridge", "OnMatchInfo", ctx.matchInfoJson);
    // The match is long past (or inside) its countdown: hand Unity the absolute GO instant.
    // SetMatchConfig zeroed the pending GO, so this must come AFTER it. A past instant = instant GO.
    const goAtLocal = Math.round(net.serverEpochToLocal(ctx.goAtEpochMs));
    instance?.SendMessage("WebBridge", "BeginCountdown", String(goAtLocal));
    unityGame.setSfxVolume(loadSettings().sfxVolume);
    musicController.startGame(mode);
    // Keyboard (F toggle, free-fly WASD) needs the canvas focused.
    document.getElementById("unity-canvas")?.focus();
    showSpectatorHud(mode, ctx.roster, {
      onFocus: (id) => instance?.SendMessage("NetBridge", "SpectateFocus", id),
      onSetMode: (m) => {
        instance?.SendMessage("NetBridge", "SpectateFreeCam", m);
        document.getElementById("unity-canvas")?.focus(); // button stole focus — give it back for WASD
      },
      onExit,
    });
  },
  // JS → Unity: drives CameraManager.mouseSpeed via the WebBridge GameObject. No-op until loaded.
  // Guard: SendMessage with a non-number (undefined/NaN from corrupted settings) takes Unity's string
  // path and throws "str.charCodeAt is not a function" — send only finite numbers.
  setMouseSensitivity(v: number) {
    const n = Number(v);
    if (Number.isFinite(n)) instance?.SendMessage("WebBridge", "SetMouseSensitivity", n);
  },
  // JS → Unity: master volume for the bean jump SFX (WebBridge.SetSfxVolume → static, read by BeanWalkDriver).
  setSfxVolume(v: number) {
    const n = Number(v);
    if (Number.isFinite(n)) instance?.SendMessage("WebBridge", "SetSfxVolume", n);
  },
  // JS → Unity: applies the saved BeanLook (body color/face/accessories) to the in-game bean. No-op until loaded.
  // We attach `faceTex` (the Unity Resources texture basename for the chosen face) so the bridge can apply
  // any face by name — the stored `face` is just an index into the FACES catalog.
  applyLook(look: BeanLook) {
    const payload = { ...look, faceTex: FACES[look.face]?.tex ?? "" };
    instance?.SendMessage("WebBridge", "ApplyLook", JSON.stringify(payload));
  },
  // Serialize the current look to the exact JSON the server relays to other players.
  lookPayload(): string {
    const look = loadLook();
    return JSON.stringify({ ...look, faceTex: FACES[look.face]?.tex ?? "" });
  },
  // Live avatars: forward a server snapshot (raw JSON) to Unity's NetBridge.
  pushSnapshot(raw: string) {
    instance?.SendMessage("NetBridge", "OnSnapshot", raw);
  },
  // Synchronized start: the server fixed an absolute GO instant (server clock). Convert it to the
  // local clock and hand it to Unity — IntroCountdown derives every tick + the unfreeze from it, so
  // all players hit GO at the same wall-clock moment regardless of message latency or a hidden tab.
  beginCountdown(goAtEpochMs: number) {
    const goAtLocal = Math.round(net.serverEpochToLocal(goAtEpochMs));
    console.log("[net] beginCountdown → Unity, goAtLocal", goAtLocal, "in", goAtLocal - Date.now(), "ms");
    instance?.SendMessage("WebBridge", "BeginCountdown", String(goAtLocal));
  },
  // A player missed the start and was dropped by the server: remove their remote avatar
  // (and LMS start hex) from our running match.
  pushPlayersDropped(idsJson: string) {
    instance?.SendMessage("NetBridge", "OnPlayersDropped", idsJson);
  },
  // Watched match: LMS tiles vanished (server relay) — apply to our world state.
  pushHexVanish(json: string) {
    instance?.SendMessage("NetBridge", "OnHexVanish", json);
  },
  hide() {
    session++; // invalidate any launch() continuation still awaiting the build
    spectating = false;
    specFreeCam = false;
    (window as any).__unitySpectateState = null;
    container?.classList.remove("show");
    // Give the OS cursor back for the DOM lobby/standings (Unity's Boot swap also unlocks C#-side).
    if (document.pointerLockElement) document.exitPointerLock();
    if (readyTimer != null) { clearTimeout(readyTimer); readyTimer = null; } // don't report ready after leaving
    hideCountdown();
    hideHowTo();
    hidePlayerHud();
    hideSpectatorHud();
    (window as any).__unityGameOver = null;
    (window as any).__unityMatchResult = null;
    (window as any).__unityNetSend = null;
    (window as any).__unityReady = null;
    (window as any).__unityHexVanish = null;
    // Drop the gameplay scene back to the lightweight Boot scene so the next JOIN starts clean
    // (resets the hex arena / player) and a different mode can be selected.
    instance?.SendMessage("WebBridge", "LoadGameScene", "Boot");
  },
  isLoaded() {
    return !!instance;
  },
};
