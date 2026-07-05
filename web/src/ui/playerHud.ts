// In-game player list HUD — a small overlay layered over the Unity WebGL canvas that shows how
// many players are in the current match and their names (the local player highlighted).
// Same pattern as ui/countdown.ts: fixed overlay, pointer-events:none, injected styles + lazy el.
// Data is pushed from main.ts on matchStart (roster) and, optionally, snapshots (presence).

interface HudPlayer { id: string; nick: string }

let el: HTMLDivElement | null = null;
let roster: HudPlayer[] = [];
let myId: string | null = null;
let presentIds: Set<string> | null = null; // ids seen in the latest snapshot (optional liveness dim)

function injectStyles() {
  if (document.getElementById("playerHudStyles")) return;
  const css = `
  #playerHud{position:fixed;top:14px;right:14px;z-index:39;display:none;pointer-events:none;
    min-width:150px;max-width:230px;
    background:rgba(20,12,38,.62);backdrop-filter:blur(6px);border:1px solid rgba(255,255,255,.10);
    border-radius:12px;padding:10px 12px;box-shadow:0 4px 14px rgba(0,0,0,.4);
    font:600 13px/1.35 system-ui,"Segoe UI",sans-serif;color:#fff}
  #playerHud.show{display:block}
  #playerHud .hd{font-weight:800;letter-spacing:.02em;color:#c9b8ff;text-transform:uppercase;
    font-size:11px;margin-bottom:6px;display:flex;align-items:center;gap:6px}
  #playerHud .hd .cnt{margin-left:auto;color:#fff;background:rgba(153,69,255,.5);
    border-radius:8px;padding:1px 8px;font-size:12px}
  #playerHud ul{list-style:none;margin:0;padding:0;max-height:40vh;overflow:hidden}
  #playerHud li{display:flex;align-items:center;gap:6px;padding:2px 0;
    white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
  #playerHud li .dot{width:7px;height:7px;border-radius:50%;background:#34f5a8;flex:0 0 auto;
    box-shadow:0 0 6px rgba(52,245,168,.6)}
  #playerHud li.me{color:#ffe14d}
  #playerHud li.me .dot{background:#ffe14d;box-shadow:0 0 6px rgba(255,225,77,.6)}
  #playerHud li.gone{opacity:.4}
  #playerHud li.gone .dot{background:#8a8a8a;box-shadow:none}
  `;
  const s = document.createElement("style");
  s.id = "playerHudStyles";
  s.textContent = css;
  document.head.appendChild(s);
}

function ensureEl(): HTMLDivElement {
  if (el) return el;
  injectStyles();
  el = document.createElement("div");
  el.id = "playerHud";
  document.body.appendChild(el);
  return el;
}

function render() {
  const root = ensureEl();
  if (!roster.length) {
    root.classList.remove("show");
    root.innerHTML = "";
    return;
  }
  root.innerHTML = "";
  const hd = document.createElement("div");
  hd.className = "hd";
  const label = document.createElement("span");
  label.textContent = "Players";
  const cnt = document.createElement("span");
  cnt.className = "cnt";
  cnt.textContent = String(roster.length);
  hd.appendChild(label);
  hd.appendChild(cnt);
  root.appendChild(hd);

  const ul = document.createElement("ul");
  for (const p of roster) {
    const li = document.createElement("li");
    const gone = presentIds != null && !presentIds.has(p.id) && p.id !== myId;
    li.className = (p.id === myId ? "me " : "") + (gone ? "gone" : "");
    const dot = document.createElement("span");
    dot.className = "dot";
    const name = document.createElement("span");
    name.textContent = p.nick + (p.id === myId ? " (you)" : ""); // textContent = injection-safe
    li.appendChild(dot);
    li.appendChild(name);
    ul.appendChild(li);
  }
  root.appendChild(ul);
  root.classList.add("show");
}

/// Mount the HUD (call when the game launches, alongside initCountdown). Idempotent.
export function initPlayerHud() {
  ensureEl();
}

/// Set the match roster (from matchStart) — count + names. Highlights `localId`.
export function setPlayerHudRoster(players: HudPlayer[], localId: string | null) {
  roster = players;
  myId = localId;
  presentIds = null;
  render();
}

/// Optional liveness: dim players not seen in the latest snapshot. Cheap, called per snapshot.
export function setPlayerHudPresence(ids: string[]) {
  if (!roster.length) return;
  presentIds = new Set(ids);
  render();
}

/// Tear down (call from unityGame.hide alongside hideCountdown).
export function hidePlayerHud() {
  roster = [];
  myId = null;
  presentIds = null;
  if (el) {
    el.classList.remove("show");
    el.innerHTML = "";
  }
}
