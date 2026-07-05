// Per-mode "How to Play" card shown after JOIN while the Unity scene loads / waits for the synced
// countdown. Same overlay pattern as countdown.ts (injected <style> + .show toggle). Shown on EVERY
// launch (not just the first load), hidden on the countdown's first tick so it never covers 3·2·1·GO.

import type { GameMode } from "./lobby";

type Rule = { dot: string; text: string };
const CONTENT: Record<GameMode, { title: string; img: string; accent: string; rules: Rule[] }> = {
  spinner: {
    title: "Spinner",
    img: "/ui/howto_spinner.png",
    accent: "#b970ff",
    rules: [
      { dot: "#39d98a", text: "The GREEN beam knocks you flying — jump over it!" },
      { dot: "#b970ff", text: "The VIOLET beam shoves you around — hold your ground." },
      { dot: "#ffb020", text: "Beams speed up and reverse over time. Outlast everyone!" },
    ],
  },
  lastman: {
    title: "Last Man Standing",
    img: "/ui/howto_lastman.png",
    accent: "#ff7a59",
    rules: [
      { dot: "#ff7a59", text: "Hex tiles VANISH shortly after you step on them." },
      { dot: "#1ac7c7", text: "Keep moving — never stand still." },
      { dot: "#ffb020", text: "Fall through and you're out. Be the last one standing!" },
    ],
  },
  rollout: {
    title: "Roll Out",
    img: "/ui/howto_rollout.png",
    accent: "#ff2d9e",
    rules: [
      { dot: "#ffffff", text: "The log ROLLS — the arrows show which way it drags you." },
      { dot: "#ff7a59", text: "Walls push you along · electric cells zap you back · hammers FLING you." },
      { dot: "#1ac7c7", text: "Gaps drop you straight through. Don't fall off!" },
    ],
  },
};

let el: HTMLDivElement | null = null;
let hideTimer: number | null = null;

function injectStyles() {
  if (document.getElementById("howtoStyles")) return;
  const css = `
  #howtoCard{position:fixed;inset:0;z-index:38;display:none;align-items:center;justify-content:center;
    background:rgba(16,8,38,.92);font-family:system-ui,sans-serif;opacity:0;transition:opacity .35s ease}
  #howtoCard.show{display:flex;opacity:1}
  #howtoCard.fade{opacity:0}
  #howtoCard .ht-box{width:min(92vw,560px);max-height:92vh;overflow:auto;background:#241245;border-radius:18px;
    box-shadow:0 18px 60px rgba(0,0,0,.55);border:1px solid rgba(255,255,255,.12);color:#fff}
  #howtoCard .ht-img{width:100%;aspect-ratio:16/9;object-fit:cover;display:block;border-radius:18px 18px 0 0}
  #howtoCard .ht-body{padding:16px 22px 20px}
  #howtoCard .ht-title{font:800 26px/1.2 system-ui,sans-serif;margin:0 0 2px;letter-spacing:.5px}
  #howtoCard .ht-sub{font:700 12px/1 system-ui,sans-serif;letter-spacing:2.5px;opacity:.65;text-transform:uppercase;margin-bottom:12px}
  #howtoCard .ht-keys{display:flex;flex-wrap:wrap;gap:14px;margin:0 0 14px;align-items:center}
  #howtoCard .ht-keygroup{display:flex;align-items:center;gap:7px;font:600 13px/1 system-ui,sans-serif;opacity:.95}
  #howtoCard kbd{display:inline-block;min-width:22px;padding:5px 7px;text-align:center;border-radius:6px;
    background:linear-gradient(#3a2766,#2a1a4e);border:1px solid rgba(255,255,255,.28);border-bottom-width:3px;
    font:700 12px/1 system-ui,sans-serif;color:#fff;box-shadow:0 2px 4px rgba(0,0,0,.35)}
  #howtoCard .ht-rules{list-style:none;margin:0;padding:0;display:flex;flex-direction:column;gap:9px}
  #howtoCard .ht-rules li{display:flex;gap:10px;align-items:flex-start;font:500 14px/1.45 system-ui,sans-serif}
  #howtoCard .ht-dot{flex:0 0 10px;width:10px;height:10px;border-radius:50%;margin-top:5px;box-shadow:0 0 8px currentColor}
  #howtoCard .ht-wait{margin-top:16px;text-align:center;font:600 13px/1 system-ui,sans-serif;opacity:.6;
    animation:htPulse 1.6s ease-in-out infinite}
  @keyframes htPulse{0%,100%{opacity:.35}50%{opacity:.8}}
  @media (max-height:560px){#howtoCard .ht-img{display:none}}
  `;
  const s = document.createElement("style");
  s.id = "howtoStyles";
  s.textContent = css;
  document.head.appendChild(s);
}

function ensureEl(): HTMLDivElement {
  if (el) return el;
  injectStyles();
  el = document.createElement("div");
  el.id = "howtoCard";
  document.body.appendChild(el);
  return el;
}

export function showHowTo(mode: GameMode) {
  const c = CONTENT[mode] ?? CONTENT.spinner;
  const box = ensureEl();
  box.innerHTML = `
    <div class="ht-box" style="outline:2px solid ${c.accent}55">
      <img class="ht-img" src="${c.img}" alt="">
      <div class="ht-body">
        <div class="ht-sub">How to play</div>
        <h2 class="ht-title" style="color:${c.accent}">${c.title}</h2>
        <div class="ht-keys">
          <span class="ht-keygroup"><kbd>W</kbd><kbd>A</kbd><kbd>S</kbd><kbd>D</kbd> Move</span>
          <span class="ht-keygroup"><kbd>SPACE</kbd> Jump</span>
          <span class="ht-keygroup"><kbd>SPACE</kbd><kbd>SPACE</kbd> Double-jump</span>
          <span class="ht-keygroup"><kbd>MOUSE</kbd> Camera</span>
        </div>
        <ul class="ht-rules">
          ${c.rules.map((r) => `<li><span class="ht-dot" style="background:${r.dot};color:${r.dot}"></span><span>${r.text}</span></li>`).join("")}
        </ul>
        <div class="ht-wait">Get ready — the match starts right after the countdown…</div>
      </div>
    </div>`;
  box.classList.remove("fade");
  box.classList.add("show");
  // Safety net: never stick around forever if the countdown signal is lost.
  if (hideTimer) window.clearTimeout(hideTimer);
  hideTimer = window.setTimeout(hideHowTo, 30000);
}

export function hideHowTo() {
  if (hideTimer) { window.clearTimeout(hideTimer); hideTimer = null; }
  const box = el;
  if (!box || !box.classList.contains("show")) return;
  box.classList.add("fade");
  window.setTimeout(() => { box.classList.remove("show", "fade"); }, 380);
}
