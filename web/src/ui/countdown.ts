// Beefy "3 · 2 · 1 · GO!" countdown overlay, layered over the Unity WebGL canvas.
// Unity owns the timer: IntroCountdown.cs → CountdownTick jslib → window.__unityCountdown(label).
// We just paint the label. Empty string ("") clears the overlay. Numbers are white, GO! is green.

let el: HTMLDivElement | null = null;
let toastEl: HTMLDivElement | null = null;
let onFirstTick: (() => void) | null = null;
let firstTickDone = false;

function injectStyles() {
  if (document.getElementById("countdownStyles")) return;
  const css = `
  #countdown{position:fixed;inset:0;z-index:40;display:none;align-items:center;justify-content:center;
    pointer-events:none}
  #countdown.show{display:flex}
  #countdown .num{
    font:900 clamp(120px,26vw,340px)/1 system-ui,"Segoe UI",sans-serif;
    letter-spacing:.02em;color:#fff;
    -webkit-text-stroke:8px #16092e;
    text-shadow:0 0 24px rgba(0,0,0,.55), 0 10px 0 #2a1656, 0 14px 26px rgba(0,0,0,.55);
    transform:scale(.6);opacity:0;
  }
  #countdown .num.go{color:#34f5a8;-webkit-text-stroke:9px #0a2e1f;
    text-shadow:0 0 32px rgba(52,245,168,.5), 0 12px 0 #0c5b3c, 0 16px 30px rgba(0,0,0,.55)}
  #countdown .num.pop{animation:cdPop .9s cubic-bezier(.2,1.4,.35,1) both}
  #countdown .num.popGo{animation:cdGo 1s cubic-bezier(.2,1.5,.3,1) both}
  /* small in-game event toast — sits HIGH so it never covers the (centered) bean */
  #gameToast{position:fixed;top:15%;left:0;right:0;z-index:41;display:flex;justify-content:center;
    pointer-events:none}
  #gameToast .toast{
    font:900 clamp(22px,4.5vw,56px)/1 system-ui,"Segoe UI",sans-serif;letter-spacing:.03em;color:#ffe14d;
    -webkit-text-stroke:5px #1a0d2e;
    text-shadow:0 0 16px rgba(0,0,0,.5), 0 6px 0 #2a1656, 0 9px 18px rgba(0,0,0,.5);
    animation:toastPop 1.1s cubic-bezier(.2,1.3,.35,1) both}
  @keyframes toastPop{
    0%{transform:translateY(-12px) scale(.7);opacity:0}
    16%{transform:translateY(0) scale(1.06);opacity:1}
    32%{transform:scale(1)}
    70%{opacity:1}
    100%{transform:translateY(-8px) scale(.96);opacity:0}
  }
  @keyframes cdPop{
    0%{transform:scale(.4);opacity:0}
    18%{transform:scale(1.12);opacity:1}
    34%{transform:scale(1)}
    78%{transform:scale(1);opacity:1}
    100%{transform:scale(.78);opacity:0}
  }
  @keyframes cdGo{
    0%{transform:scale(.5) rotate(-6deg);opacity:0}
    20%{transform:scale(1.3) rotate(3deg);opacity:1}
    40%{transform:scale(1.05) rotate(0)}
    85%{transform:scale(1.05);opacity:1}
    100%{transform:scale(1.5);opacity:0}
  }`;
  const s = document.createElement("style");
  s.id = "countdownStyles";
  s.textContent = css;
  document.head.appendChild(s);
}

function ensureEl(): HTMLDivElement {
  if (el) return el;
  injectStyles();
  el = document.createElement("div");
  el.id = "countdown";
  document.body.appendChild(el);
  return el;
}

function show(label: string) {
  const root = ensureEl();
  if (label === "" || label == null) {
    root.classList.remove("show");
    root.innerHTML = "";
    return;
  }
  const isGo = label.toUpperCase() === "GO";
  // rebuild the span each tick so the CSS animation restarts cleanly
  root.innerHTML = "";
  const span = document.createElement("div");
  span.className = "num " + (isGo ? "go popGo" : "pop");
  span.textContent = isGo ? "GO!" : label;
  root.appendChild(span);
  root.classList.add("show");
  // force reflow so the animation always (re)plays
  void span.offsetWidth;
}

function ensureToast(): HTMLDivElement {
  if (toastEl) return toastEl;
  injectStyles();
  toastEl = document.createElement("div");
  toastEl.id = "gameToast";
  document.body.appendChild(toastEl);
  return toastEl;
}

function showToast(label: string) {
  const root = ensureToast();
  if (label === "" || label == null) {
    root.innerHTML = "";
    return;
  }
  // rebuild each call so the CSS animation restarts cleanly
  root.innerHTML = "";
  const span = document.createElement("div");
  span.className = "toast";
  span.textContent = label;
  root.appendChild(span);
  void span.offsetWidth;
}

/// Wire window.__unityCountdown + window.__unityToast so the Unity jslib can drive both overlays.
/// `firstTick` fires once, on the first countdown number (used to start in-game music in sync with "3").
export function initCountdown(firstTick?: () => void) {
  ensureEl();
  ensureToast();
  onFirstTick = firstTick ?? null;
  firstTickDone = false;
  (window as any).__unityCountdown = (label: string) => {
    if (label && !firstTickDone) {
      firstTickDone = true;
      onFirstTick?.();
    }
    show(label);
  };
  (window as any).__unityToast = (label: string) => showToast(label);
}

/// Tear down (e.g. when leaving the game) — clears any lingering number/toast.
export function hideCountdown() {
  show("");
  showToast("");
  firstTickDone = false;
}
