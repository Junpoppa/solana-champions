// Spectator overlay — layered over the Unity canvas while watching a live match.
// Same pattern as ui/playerHud.ts: injected styles, lazy root, pointer-events:none shell
// with clickable islands (roster rows + FREE CAM / EXIT buttons).
// The camera itself lives in Unity (SpectatorCamera): player-view mode (LMB/RMB cycles players)
// or free-fly mode. Unity reports every mode/focus change back via window.__unitySpectateState
// → setSpectatorState, so the highlight/hints here always track the real camera state.

interface SpecPlayer { id: string; nick: string }

export interface SpectatorHooks {
  onFocus: (id: string) => void; // roster click → player-view this player
  onSetMode: (mode: "player" | "free") => void; // PLAYER CAM / FREE CAM buttons
  onExit: () => void; // leave spectating → lobby
}

let el: HTMLDivElement | null = null;
let roster: SpecPlayer[] = [];
let hooks: SpectatorHooks | null = null;
let focusedId: string | null = null;
let freeCam = false;
let modeTitle = "";

const MODE_TITLES: Record<string, string> = {
  spinner: "Spinner",
  lastman: "Last Man Standing",
  rollout: "Roll Out",
};

const HINT_PLAYER = "Click: next player · Right-click: previous · Tab: free cam";
const HINT_FREE = "WASD fly · Space/C up/down · Shift fast · Tab: player cam · Esc frees cursor";

function injectStyles() {
  if (document.getElementById("spectatorStyles")) return;
  const css = `
  #specHud{position:fixed;inset:0;z-index:40;display:none;pointer-events:none;
    font:600 13px/1.35 system-ui,"Segoe UI",sans-serif;color:#fff}
  #specHud.show{display:block}
  #specHud .sp-top{position:absolute;top:14px;left:50%;transform:translateX(-50%);
    display:flex;flex-direction:column;align-items:center;gap:6px}
  #specHud .sp-pill{display:flex;align-items:center;gap:8px;padding:8px 18px;border-radius:20px;
    background:rgba(20,12,38,.72);backdrop-filter:blur(6px);border:1px solid rgba(255,255,255,.12);
    box-shadow:0 4px 14px rgba(0,0,0,.4);font:800 13px/1 system-ui;letter-spacing:.04em}
  #specHud .sp-dot{width:9px;height:9px;border-radius:50%;background:#ff4b4b;
    animation:spLivePulse 1.1s ease-in-out infinite}
  @keyframes spLivePulse{0%,100%{opacity:1;transform:scale(1)}50%{opacity:.45;transform:scale(.8)}}
  #specHud .sp-hint{padding:5px 14px;border-radius:12px;font:600 11.5px/1 system-ui;color:#cbb6ff;
    background:rgba(20,12,38,.55);backdrop-filter:blur(4px)}
  #specHud .sp-roster{position:absolute;top:70px;right:14px;pointer-events:auto;
    min-width:160px;max-width:240px;
    background:rgba(20,12,38,.68);backdrop-filter:blur(6px);border:1px solid rgba(255,255,255,.10);
    border-radius:12px;padding:10px 8px;box-shadow:0 4px 14px rgba(0,0,0,.4)}
  #specHud .sp-roster .hd{font:800 11px/1 system-ui;letter-spacing:.08em;text-transform:uppercase;
    color:#c9b8ff;margin:0 6px 8px}
  #specHud .sp-roster ul{list-style:none;margin:0;padding:0;max-height:46vh;overflow-y:auto}
  #specHud .sp-roster li{display:flex;align-items:center;gap:7px;padding:6px 8px;border-radius:8px;
    cursor:pointer;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;
    transition:background .12s ease}
  #specHud .sp-roster li:hover{background:rgba(255,255,255,.10)}
  #specHud .sp-roster li.focused{background:rgba(153,69,255,.45)}
  #specHud .sp-roster li .dot{width:7px;height:7px;border-radius:50%;background:#34f5a8;flex:0 0 auto;
    box-shadow:0 0 6px rgba(52,245,168,.6)}
  #specHud .sp-btns{position:absolute;bottom:22px;left:50%;transform:translateX(-50%);
    display:flex;gap:12px;pointer-events:auto}
  #specHud .sp-btns button{border:0;border-radius:12px;cursor:pointer;color:#fff;height:42px;padding:0 22px;
    font:800 14px/1 system-ui;letter-spacing:.03em;text-shadow:0 2px 4px rgba(0,0,0,.5);
    box-shadow:0 5px 0 rgba(0,0,0,.35),0 8px 14px rgba(0,0,0,.4);
    transition:transform .12s ease,filter .12s ease}
  #specHud .sp-btns button:hover{transform:scale(1.05);filter:brightness(1.1)}
  #specHud .sp-btns button:active{transform:translateY(3px);box-shadow:0 2px 0 rgba(0,0,0,.35)}
  #specHud .sp-mode{background:linear-gradient(180deg,#46dada,#1ac7c7);opacity:.75}
  #specHud .sp-mode.active{background:linear-gradient(180deg,#ff5cb5,#ff2d9e);opacity:1}
  #specHud .sp-exit{background:linear-gradient(180deg,#9aa1ad,#6b7280)}
  `;
  const s = document.createElement("style");
  s.id = "spectatorStyles";
  s.textContent = css;
  document.head.appendChild(s);
}

function ensureEl(): HTMLDivElement {
  if (el) return el;
  injectStyles();
  el = document.createElement("div");
  el.id = "specHud";
  document.body.appendChild(el);
  return el;
}

function render() {
  const root = ensureEl();
  root.innerHTML = "";

  const top = document.createElement("div");
  top.className = "sp-top";
  const pill = document.createElement("div");
  pill.className = "sp-pill";
  const dot = document.createElement("span");
  dot.className = "sp-dot";
  pill.appendChild(dot);
  pill.appendChild(document.createTextNode(`WATCHING LIVE — ${modeTitle}`));
  const hint = document.createElement("div");
  hint.className = "sp-hint";
  hint.textContent = freeCam ? HINT_FREE : HINT_PLAYER;
  top.appendChild(pill);
  top.appendChild(hint);
  root.appendChild(top);

  const panel = document.createElement("div");
  panel.className = "sp-roster";
  const hd = document.createElement("div");
  hd.className = "hd";
  hd.textContent = `Players (${roster.length})`;
  panel.appendChild(hd);
  const ul = document.createElement("ul");
  for (const p of roster) {
    const li = document.createElement("li");
    li.className = !freeCam && p.id === focusedId ? "focused" : "";
    const d = document.createElement("span");
    d.className = "dot";
    const name = document.createElement("span");
    name.textContent = p.nick; // textContent = injection-safe
    li.appendChild(d);
    li.appendChild(name);
    li.onclick = () => hooks?.onFocus(p.id); // Unity confirms via __unitySpectateState
    ul.appendChild(li);
  }
  panel.appendChild(ul);
  root.appendChild(panel);

  // Both camera modes as buttons — the active one is lit, so it's always clear which is on.
  const btns = document.createElement("div");
  btns.className = "sp-btns";
  const player = document.createElement("button");
  player.className = "sp-mode" + (freeCam ? "" : " active");
  player.textContent = "PLAYER CAM";
  player.onclick = () => hooks?.onSetMode("player");
  const free = document.createElement("button");
  free.className = "sp-mode" + (freeCam ? " active" : "");
  free.textContent = "FREE CAM";
  free.onclick = () => hooks?.onSetMode("free");
  const exit = document.createElement("button");
  exit.className = "sp-exit";
  exit.textContent = "EXIT";
  exit.onclick = () => hooks?.onExit();
  btns.appendChild(player);
  btns.appendChild(free);
  btns.appendChild(exit);
  root.appendChild(btns);

  root.classList.add("show");
}

/// Mount the overlay for a spectate session.
export function showSpectatorHud(mode: string, players: SpecPlayer[], h: SpectatorHooks) {
  roster = players;
  hooks = h;
  focusedId = players.length > 0 ? players[0].id : null; // Unity defaults to roster[0] too
  freeCam = false;
  modeTitle = MODE_TITLES[mode] || mode;
  render();
}

/// Unity reported a camera state change: {mode:"player"|"free", id:<focused player id>}.
export function setSpectatorState(s: { mode?: string; id?: string }) {
  if (!hooks) return; // not spectating (stale callback)
  freeCam = s.mode === "free";
  focusedId = s.id || null;
  render();
}

/// Remove players who dropped mid-match (playersDropped relay).
export function removeSpectatorPlayers(ids: string[]) {
  if (!roster.length) return;
  const gone = new Set(ids);
  roster = roster.filter((p) => !gone.has(p.id));
  if (focusedId && gone.has(focusedId)) focusedId = null;
  render();
}

/// Tear down (call from unityGame.hide).
export function hideSpectatorHud() {
  roster = [];
  hooks = null;
  focusedId = null;
  freeCam = false;
  if (el) {
    el.classList.remove("show");
    el.innerHTML = "";
  }
}
