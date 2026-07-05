// Standings overlay — shown after a multiplayer match ends. Ranked list of players by
// survival time, winner highlighted with their payout address. No on-chain transfer yet.

import type { StandingsMsg } from "../netTypes";

function fmtTime(ms: number): string {
  const total = Math.max(0, Math.round(ms));
  const m = Math.floor(total / 60000);
  const s = Math.floor((total % 60000) / 1000);
  const cs = Math.floor((total % 1000) / 10);
  return `${m}:${String(s).padStart(2, "0")}.${String(cs).padStart(2, "0")}`;
}

export class Standings {
  private root: HTMLDivElement;
  private onBack: () => void;
  private listEl!: HTMLDivElement;
  private winnerEl!: HTMLDivElement;

  constructor(onBack: () => void) {
    this.onBack = onBack;
    this.injectStyles();
    this.root = this.build();
    document.body.appendChild(this.root);
  }

  private injectStyles() {
    const css = `
    #standings{position:fixed;inset:0;z-index:46;display:none;pointer-events:none;
      align-items:center;justify-content:center;background:rgba(8,4,18,.6);backdrop-filter:blur(4px);
      font:600 14px/1 system-ui,sans-serif;color:#fff}
    #standings.show{display:flex}
    #standings .st-panel{pointer-events:auto;width:min(94vw,480px);padding:26px 26px 22px;border-radius:22px;
      background:rgba(20,12,38,.9);box-shadow:0 18px 50px rgba(0,0,0,.6),inset 0 1px 0 rgba(255,255,255,.14)}
    #standings .st-title{text-align:center;font:800 clamp(24px,4vw,34px)/1 system-ui;color:#fee64d;
      text-shadow:0 2px 6px rgba(0,0,0,.45)}
    #standings .st-winner{margin-top:16px;padding:16px;border-radius:16px;text-align:center;
      background:linear-gradient(180deg,rgba(255,92,181,.28),rgba(255,45,158,.16));
      border:2px solid rgba(255,92,181,.5)}
    #standings .st-winner .wl-crown{font-size:30px}
    #standings .st-winner .wl-nick{margin-top:4px;font:800 24px/1 system-ui;color:#fff}
    #standings .st-winner .wl-addr{margin-top:8px;font:600 12px/1.3 ui-monospace,monospace;color:#cbb6ff;
      word-break:break-all}
    #standings .st-winner .wl-note{margin-top:8px;font:600 11px/1 system-ui;color:#8f83b8}
    #standings .st-list{margin-top:16px;max-height:40vh;overflow-y:auto;display:flex;flex-direction:column;gap:6px}
    #standings .st-row{display:flex;align-items:center;gap:12px;padding:10px 14px;border-radius:12px;
      background:rgba(255,255,255,.06)}
    #standings .st-row.me{background:rgba(26,199,199,.18);box-shadow:inset 0 0 0 1.5px rgba(26,199,199,.5)}
    #standings .st-rank{width:26px;font:800 16px/1 system-ui;color:#fee64d;text-align:center}
    #standings .st-nick{flex:1;font:700 15px/1 system-ui;color:#fff;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
    #standings .st-surv{font:700 14px/1 ui-monospace,monospace;color:#7fe7e7}
    #standings .st-back{margin-top:20px;width:100%;height:52px;border:0;border-radius:14px;cursor:pointer;color:#fff;
      font:800 19px/1 system-ui;letter-spacing:.02em;background:#1ac7c7;text-shadow:0 2px 4px rgba(0,0,0,.4);
      box-shadow:0 6px 0 #128f8f,0 9px 18px rgba(0,0,0,.4);transition:transform .12s ease,filter .12s ease}
    #standings .st-back:hover{transform:scale(1.03);filter:brightness(1.08)}
    #standings .st-back:active{transform:translateY(3px);box-shadow:0 3px 0 #128f8f}
    `;
    const s = document.createElement("style");
    s.textContent = css;
    document.head.appendChild(s);
  }

  private build(): HTMLDivElement {
    const root = document.createElement("div");
    root.id = "standings";
    const panel = document.createElement("div");
    panel.className = "st-panel";

    const title = document.createElement("div");
    title.className = "st-title";
    title.textContent = "Results";

    this.winnerEl = document.createElement("div");
    this.winnerEl.className = "st-winner";
    this.listEl = document.createElement("div");
    this.listEl.className = "st-list";

    const back = document.createElement("button");
    back.className = "st-back";
    back.type = "button";
    back.textContent = "Back to Lobby";
    back.onclick = () => this.onBack();

    panel.append(title, this.winnerEl, this.listEl, back);
    root.appendChild(panel);
    return root;
  }

  // Shown briefly after the local player finishes, while other players are still
  // playing and the server collects their results.
  showWaiting() {
    this.winnerEl.textContent = "";
    this.listEl.textContent = "";
    const wait = document.createElement("div");
    wait.className = "wl-note";
    wait.style.cssText = "text-align:center;padding:20px 0;font:700 15px/1.4 system-ui;color:#cbb6ff";
    wait.textContent = "Waiting for other players to finish…";
    this.listEl.appendChild(wait);
    this.root.classList.add("show");
  }

  show(m: StandingsMsg, myId: string | null) {
    // winner block
    this.winnerEl.textContent = "";
    if (m.winner) {
      const crown = document.createElement("div");
      crown.className = "wl-crown";
      crown.textContent = "👑";
      const nick = document.createElement("div");
      nick.className = "wl-nick";
      nick.textContent = m.winner.nick;
      this.winnerEl.append(crown, nick);
      if (m.winner.solAddress) {
        const addr = document.createElement("div");
        addr.className = "wl-addr";
        addr.textContent = m.winner.solAddress;
        const note = document.createElement("div");
        note.className = "wl-note";
        note.textContent = "Payout address on record — no on-chain transfer yet.";
        this.winnerEl.append(addr, note);
      } else {
        const note = document.createElement("div");
        note.className = "wl-note";
        note.textContent = "Winner has no payout address on file.";
        this.winnerEl.append(note);
      }
    }

    // ranked rows
    this.listEl.textContent = "";
    for (const row of m.ranked) {
      const el = document.createElement("div");
      el.className = "st-row" + (myId && row.id === myId ? " me" : "");
      const rank = document.createElement("div");
      rank.className = "st-rank";
      rank.textContent = "#" + row.rank;
      const nick = document.createElement("div");
      nick.className = "st-nick";
      nick.textContent = row.nick;
      const surv = document.createElement("div");
      surv.className = "st-surv";
      surv.textContent = fmtTime(row.survivalMs);
      el.append(rank, nick, surv);
      this.listEl.appendChild(el);
    }

    this.root.classList.add("show");
  }

  hide() {
    this.root.classList.remove("show");
  }
}
