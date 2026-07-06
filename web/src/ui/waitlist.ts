// Waitlist overlay — shown after JOIN while the server fills a per-mode room. Displays
// the live count, fill countdown, and roster of nicknames. Leave returns to the lobby.

import type { GameMode, QueueUpdateMsg } from "../netTypes";

const MODE_TITLE: Record<GameMode, string> = {
  spinner: "Spinner",
  lastman: "Last Man Standing",
  rollout: "Roll Out",
};

export class Waitlist {
  private root: HTMLDivElement;
  private onLeave: () => void;
  private titleEl!: HTMLDivElement;
  private countEl!: HTMLDivElement;
  private timerEl!: HTMLDivElement;
  private rosterEl!: HTMLDivElement;

  constructor(onLeave: () => void) {
    this.onLeave = onLeave;
    this.injectStyles();
    this.root = this.build();
    document.body.appendChild(this.root);
  }

  private injectStyles() {
    const css = `
    #waitlist{position:fixed;inset:0;z-index:30;display:none;pointer-events:none;
      align-items:center;justify-content:center;font:600 14px/1 system-ui,sans-serif;color:#fff}
    #waitlist.show{display:flex}
    #waitlist .wl-panel{pointer-events:auto;width:min(92vw,420px);padding:28px 28px 24px;border-radius:22px;
      background:rgba(20,12,38,.72);backdrop-filter:blur(10px);text-align:center;
      box-shadow:0 16px 44px rgba(0,0,0,.5),inset 0 1px 0 rgba(255,255,255,.14)}
    #waitlist .wl-title{font:800 clamp(22px,3.6vw,30px)/1.05 system-ui;color:#fee64d;text-shadow:0 2px 6px rgba(0,0,0,.45)}
    #waitlist .wl-count{margin-top:16px;font:800 48px/1 system-ui;color:#fff}
    #waitlist .wl-count small{font:700 20px/1 system-ui;color:#cbb6ff}
    #waitlist .wl-timer{margin-top:10px;font:700 14px/1 system-ui;color:#7fe7e7;min-height:16px}
    #waitlist .wl-roster{margin-top:18px;display:flex;flex-wrap:wrap;gap:8px;justify-content:center;
      max-height:172px;overflow-y:auto}
    #waitlist .wl-chip{padding:7px 12px;border-radius:20px;background:rgba(255,255,255,.1);
      font:700 13px/1 system-ui;color:#fff;box-shadow:inset 0 1px 0 rgba(255,255,255,.14)}
    #waitlist .wl-chip.me{background:linear-gradient(180deg,#ff5cb5,#ff2d9e)}
    #waitlist .wl-leave{margin-top:22px;height:48px;padding:0 26px;border:0;border-radius:13px;cursor:pointer;
      color:#fff;font:800 16px/1 system-ui;letter-spacing:.02em;background:rgba(255,255,255,.14);
      box-shadow:inset 0 1px 0 rgba(255,255,255,.18);transition:filter .12s ease,transform .12s ease}
    #waitlist .wl-leave:hover{filter:brightness(1.15);transform:translateY(-1px)}
    `;
    const s = document.createElement("style");
    s.textContent = css;
    document.head.appendChild(s);
  }

  private build(): HTMLDivElement {
    const root = document.createElement("div");
    root.id = "waitlist";
    const panel = document.createElement("div");
    panel.className = "wl-panel";

    this.titleEl = document.createElement("div");
    this.titleEl.className = "wl-title";
    this.countEl = document.createElement("div");
    this.countEl.className = "wl-count";
    this.timerEl = document.createElement("div");
    this.timerEl.className = "wl-timer";
    this.rosterEl = document.createElement("div");
    this.rosterEl.className = "wl-roster";

    const leave = document.createElement("button");
    leave.className = "wl-leave";
    leave.type = "button";
    leave.textContent = "Leave";
    leave.onclick = () => this.onLeave();

    panel.append(this.titleEl, this.countEl, this.timerEl, this.rosterEl, leave);
    root.appendChild(panel);
    return root;
  }

  show(mode: GameMode) {
    this.titleEl.textContent = MODE_TITLE[mode] ?? mode;
    this.countEl.innerHTML = `0 <small>/ 0</small>`;
    this.timerEl.textContent = "Connecting…";
    this.rosterEl.textContent = "";
    this.root.classList.add("show");
  }

  // myId: the local player's id so we can highlight our own chip.
  update(m: QueueUpdateMsg, myId: string | null) {
    this.titleEl.textContent = MODE_TITLE[m.mode] ?? m.mode;
    this.countEl.innerHTML = `${m.count} <small>/ ${m.capacity}</small>`;
    if (m.msRemaining < 0) {
      this.timerEl.textContent = "Waiting for the current match to finish…";
    } else if (m.count >= m.capacity) {
      this.timerEl.textContent = "Starting…";
    } else if (m.msRemaining === 0) {
      // no countdown armed yet — server arms it once minToStart players queue up
      this.timerEl.textContent = `Waiting for players — starts when ${m.minToStart} join (or lobby is full)`;
    } else {
      this.timerEl.textContent = `Starting in ${Math.ceil(m.msRemaining / 1000)}s (or when full)`;
    }
    this.rosterEl.textContent = "";
    for (const r of m.roster) {
      const chip = document.createElement("div");
      chip.className = "wl-chip" + (myId && r.id === myId ? " me" : "");
      chip.textContent = r.nick; // textContent — never innerHTML (injection-safe)
      this.rosterEl.appendChild(chip);
    }
  }

  // One-line status override (e.g. "Missed the match start — you're in line for the next one").
  // The next queueUpdate from the server repaints the timer line, which is the desired behavior.
  setNotice(text: string) {
    this.timerEl.textContent = text;
  }

  hide() {
    this.root.classList.remove("show");
  }
}
