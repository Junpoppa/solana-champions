// Lobby screen — looped video bg + Solana branding + nav + JOIN GAME.
// Ported from the Unity LobbyMenu prototype. The 3D bean-on-pedestal renders in the
// (transparent) canvas behind this overlay; the video sits behind the canvas.
//
// Scope now = visual shell + navigation. Clothing opens the existing customizer.
// JOIN GAME / Settings / multiplayer / lobby-filling / Solana wallet = future to-dos (stubbed).

import { musicController } from "./musicController";

export type LobbyNav = "play" | "clothing" | "settings";
// Playable game modes. Each maps to a Unity scene in unityGame.ts; Platform Race is a future teaser.
export type GameMode = "spinner" | "lastman" | "rollout";

export interface LobbyHooks {
  onClothing: () => void; // open the customizer
  onJoin: (mode: GameMode) => void; // start a match in the chosen mode
  onSettings: () => void; // open settings (stub for now)
  onChat: (text: string) => void; // send a lobby chat line
}

const MAGENTA = "#ff2d9e";
// glossy candy fills (lighter top → brand base) for active / inactive nav pills
const ACTIVE_BG = "linear-gradient(180deg,#ff5cb5,#ff2d9e)";
const INACTIVE_BG = "linear-gradient(180deg,#46dada,#1ac7c7)";

// Hand-built nav icons (white line art, currentColor). Theme-matched, scalable, no asset deps.
const SVG_HEAD = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">';
const ICON_HOME = SVG_HEAD + '<path d="M3 11.5 12 4l9 7.5"/><path d="M5.5 10.5V20h13v-9.5"/><path d="M10 20v-5h4v5"/></svg>';
const ICON_SHIRT = SVG_HEAD + '<path d="M8.5 3 4 6l2.2 3.2L8 8.2V21h8V8.2l1.8 1L20 6l-4.5-3a3.6 3.6 0 0 1-7 0Z"/></svg>';
const ICON_SLIDERS =
  '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round">' +
  '<path d="M4 7h16M4 12h16M4 17h16"/>' +
  '<circle cx="15" cy="7" r="2.6" fill="#fff" stroke="none"/>' +
  '<circle cx="9" cy="12" r="2.6" fill="#fff" stroke="none"/>' +
  '<circle cx="16" cy="17" r="2.6" fill="#fff" stroke="none"/></svg>';
const ICON_SOUND_ON = SVG_HEAD +
  '<path d="M4 9v6h3.5L12 19V5L7.5 9H4z" fill="#fff" stroke="#fff" stroke-width="1.4"/>' +
  '<path d="M16 8.5a5 5 0 0 1 0 7"/><path d="M18.6 6a8.5 8.5 0 0 1 0 12"/></svg>';
const ICON_SOUND_OFF = SVG_HEAD +
  '<path d="M4 9v6h3.5L12 19V5L7.5 9H4z" fill="#fff" stroke="#fff" stroke-width="1.4"/>' +
  '<path d="M16.5 9.5l5 5M21.5 9.5l-5 5"/></svg>';
// music-toggle "off" pill fill (muted grey) vs the teal INACTIVE_BG when on
const MUTED_BG = "linear-gradient(180deg,#9aa1ad,#6b7280)";

export class Lobby {
  private video: HTMLVideoElement;
  private root: HTMLDivElement;
  private hooks: LobbyHooks;
  private navButtons: Partial<Record<LobbyNav, HTMLButtonElement>> = {};
  private modesEl!: HTMLDivElement;
  private logoEl!: HTMLImageElement;
  private chatLog!: HTMLDivElement;
  private chatSeeded = false;
  playerName = "Player"; // overwritten from the saved profile via setPlayerName()

  constructor(hooks: LobbyHooks) {
    this.hooks = hooks;
    this.injectStyles();
    this.video = this.buildVideo();
    this.root = this.build();
    document.body.appendChild(this.video);
    document.body.appendChild(this.root);
    window.addEventListener("resize", () => { if (this.root.classList.contains("show")) this.layoutModes(); });
  }

  // Seat the mode-card column just below the logo's ACTUAL rendered bottom (responsive to any screen size),
  // and cap its height so a short screen scrolls the cards instead of pushing them off-screen. No overlap.
  private layoutModes() {
    if (!this.logoEl || !this.modesEl) return;
    const r = this.logoEl.getBoundingClientRect();
    const logoBottom = r.height > 0 ? r.bottom : 28 + this.logoEl.offsetHeight; // fall back if not yet laid out
    const top = Math.max(150, logoBottom + 20);
    this.modesEl.style.top = top + "px";
    this.modesEl.style.maxHeight = Math.max(160, window.innerHeight - top - 24) + "px";
  }

  private injectStyles() {
    const css = `
    #lobbyVideo{position:fixed;inset:0;z-index:1;width:100%;height:100%;
      object-fit:cover;display:none;pointer-events:none}
    #lobbyVideo.show{display:block}
    #lobby{position:fixed;inset:0;z-index:25;display:none;pointer-events:none;
      font:600 14px/1 system-ui,sans-serif;color:#fff}
    #lobby.show{display:block}
    #lobby .lb-clickable{pointer-events:auto}
    #lobby .lb-logo{position:absolute;top:28px;left:36px;width:min(34vw,560px);height:auto;
      filter:drop-shadow(0 4px 14px rgba(0,0,0,.45))}
    #lobby .lb-card{position:absolute;top:15vh;left:50%;transform:translateX(-50%);
      text-align:center;min-width:200px;padding:11px 26px;border-radius:16px;
      background:rgba(20,12,38,.55);backdrop-filter:blur(6px);
      box-shadow:0 8px 22px rgba(0,0,0,.32),inset 0 1px 0 rgba(255,255,255,.12)}
    #lobby .lb-card .lb-label{font:600 11px/1 system-ui;letter-spacing:.12em;
      text-transform:uppercase;color:#cbb6ff;opacity:.8}
    #lobby .lb-card .lb-name{margin-top:7px;font:800 26px/1 system-ui;color:#fee64d;
      text-shadow:0 2px 6px rgba(0,0,0,.4)}
    #lobby .lb-nav{position:absolute;top:28px;right:36px;display:flex;gap:14px}
    #lobby .lb-nav button{position:relative;height:54px;padding:0 22px 0 18px;border:0;border-radius:27px;
      cursor:pointer;display:flex;align-items:center;gap:10px;color:#fff;overflow:hidden;
      font:800 15px/1 system-ui,sans-serif;letter-spacing:.01em;
      box-shadow:0 6px 16px rgba(0,0,0,.32),inset 0 2px 0 rgba(255,255,255,.5),inset 0 -3px 9px rgba(0,0,0,.2);
      transition:transform .14s cubic-bezier(.22,1,.36,1),filter .14s ease}
    #lobby .lb-nav button::before{content:"";position:absolute;inset:2px 2px 52% 2px;border-radius:25px 25px 40px 40px;
      background:linear-gradient(180deg,rgba(255,255,255,.5),rgba(255,255,255,0));pointer-events:none}
    #lobby .lb-nav button svg{width:22px;height:22px;flex:none;position:relative;filter:drop-shadow(0 1px 1px rgba(0,0,0,.25))}
    #lobby .lb-nav button span{position:relative;text-shadow:0 1px 2px rgba(0,0,0,.3)}
    #lobby .lb-nav button:hover{transform:translateY(-2px) scale(1.04);filter:brightness(1.07)}
    #lobby .lb-nav button:active{transform:translateY(0) scale(.99)}
    /* mode cards: a left vertical column beneath the logo, each a horizontal row with a map thumbnail.
       top + max-height are set from JS (layoutModes) against the logo's ACTUAL rendered height so the
       cards never overlap the logo on any screen size; this 220px is just a pre-JS fallback. */
    #lobby .lb-modes{position:absolute;left:36px;top:220px;display:flex;flex-direction:column;gap:13px;
      width:min(33vw,400px);overflow-y:auto;padding-right:4px}
    #lobby .lb-mode-card{display:flex;flex-direction:row;align-items:center;gap:14px;
      padding:12px 14px;border-radius:16px;text-align:left;
      background:rgba(20,12,38,.62);backdrop-filter:blur(8px);
      box-shadow:0 10px 26px rgba(0,0,0,.4),inset 0 1px 0 rgba(255,255,255,.14);
      transition:transform .14s ease,filter .14s ease}
    #lobby .lb-mode-card:not(.disabled):hover{transform:translateX(4px);filter:brightness(1.06)}
    #lobby .lb-mode-map{width:112px;height:80px;border-radius:11px;flex:none;
      background:#0b0716 center/cover no-repeat;
      box-shadow:0 3px 10px rgba(0,0,0,.4),inset 0 0 0 1px rgba(255,255,255,.08)}
    #lobby .lb-mode-info{display:flex;flex-direction:column;flex:1;min-width:0}
    #lobby .lb-mode-title{font:800 19px/1.05 system-ui,sans-serif;color:#fee64d;
      text-shadow:0 2px 5px rgba(0,0,0,.4)}
    #lobby .lb-mode-blurb{margin-top:5px;font:600 12px/1.3 system-ui,sans-serif;color:#cbb6ff}
    #lobby .lb-card-join{margin-top:9px;align-self:flex-start;border:0;border-radius:11px;cursor:pointer;color:#fff;
      padding:0 24px;height:38px;font:800 16px/1 system-ui,sans-serif;letter-spacing:.02em;
      background:${MAGENTA};text-shadow:0 2px 4px rgba(0,0,0,.5);
      box-shadow:0 5px 0 #b01b6e,0 8px 14px rgba(0,0,0,.4);
      transition:transform .12s ease,filter .12s ease}
    #lobby .lb-card-join:hover{transform:scale(1.05);filter:brightness(1.1)}
    #lobby .lb-card-join:active{transform:translateY(3px);box-shadow:0 2px 0 #b01b6e,0 5px 10px rgba(0,0,0,.4)}
    #lobby .lb-mode-card.disabled{filter:grayscale(.7) brightness(.66);opacity:.85}
    #lobby .lb-soon{margin-top:9px;align-self:flex-start;padding:0 16px;height:34px;border-radius:11px;
      display:flex;align-items:center;justify-content:center;
      font:800 12px/1 system-ui,sans-serif;letter-spacing:.07em;text-transform:uppercase;
      color:#fff;background:rgba(255,255,255,.12);border:2px dashed rgba(255,255,255,.32)}
    /* lobby chat — bottom-right, lobby-only */
    #lobby .lb-chat{position:absolute;right:36px;bottom:36px;width:min(32vw,360px);height:300px;
      display:flex;flex-direction:column;border-radius:16px;overflow:hidden;
      background:rgba(15,10,30,.72);backdrop-filter:blur(8px);
      box-shadow:0 10px 26px rgba(0,0,0,.42),inset 0 1px 0 rgba(255,255,255,.10)}
    #lobby .lb-chat-head{padding:9px 14px;font:800 11px/1 system-ui;letter-spacing:.1em;
      text-transform:uppercase;color:#c9b8ff;border-bottom:1px solid rgba(255,255,255,.10)}
    #lobby .lb-chat-log{flex:1;overflow-y:auto;padding:10px 14px;display:flex;flex-direction:column;gap:6px;
      font:500 13px/1.35 system-ui,sans-serif}
    #lobby .lb-chat-line{overflow-wrap:anywhere}
    #lobby .lb-chat-line .nk{font-weight:800;color:#5fd0ff}
    #lobby .lb-chat-line.me .nk{color:#fee64d}
    #lobby .lb-chat-empty{color:rgba(255,255,255,.4);font-style:italic}
    #lobby .lb-chat-input{display:flex;gap:8px;padding:10px;border-top:1px solid rgba(255,255,255,.10)}
    #lobby .lb-chat-input input{flex:1;min-width:0;border:0;border-radius:9px;padding:0 12px;height:38px;
      background:rgba(255,255,255,.12);color:#fff;font:500 13px/1 system-ui;outline:none}
    #lobby .lb-chat-input input::placeholder{color:rgba(255,255,255,.5)}
    #lobby .lb-chat-input button{border:0;border-radius:9px;cursor:pointer;height:38px;padding:0 16px;
      background:${INACTIVE_BG};color:#fff;font:800 13px/1 system-ui}
    `;
    const s = document.createElement("style");
    s.textContent = css;
    document.head.appendChild(s);
  }

  private buildVideo(): HTMLVideoElement {
    const v = document.createElement("video");
    v.id = "lobbyVideo";
    v.src = "/video/lobby_bg.webm";
    v.muted = true;
    v.loop = true;
    v.autoplay = true;
    v.playsInline = true;
    return v;
  }

  private build(): HTMLDivElement {
    const root = document.createElement("div");
    root.id = "lobby";

    // Solana Champions logo
    const logo = document.createElement("img");
    logo.className = "lb-logo";
    logo.src = "/ui/solana_champions_logo.png";
    logo.alt = "Solana Champions";
    logo.onload = () => this.layoutModes(); // once we know the logo's real height, seat the cards below it
    this.logoEl = logo;
    root.appendChild(logo);

    // player name card
    const card = document.createElement("div");
    card.className = "lb-card";
    const lab = document.createElement("div");
    lab.className = "lb-label";
    lab.textContent = "Player";
    const name = document.createElement("div");
    name.className = "lb-name";
    name.textContent = this.playerName;
    card.appendChild(lab);
    card.appendChild(name);
    root.appendChild(card);

    // nav (top-right): Menu / Customize / Settings — glossy pills with icon + label
    const nav = document.createElement("div");
    nav.className = "lb-nav lb-clickable";
    nav.appendChild(this.navButton("play", "Menu", ICON_HOME, () => this.setActive("play")));
    nav.appendChild(
      this.navButton("clothing", "Customize", ICON_SHIRT, () => {
        this.setActive("clothing");
        this.hooks.onClothing();
      })
    );
    nav.appendChild(
      this.navButton("settings", "Settings", ICON_SLIDERS, () => {
        this.setActive("settings");
        this.hooks.onSettings();
      })
    );
    nav.appendChild(this.buildMusicToggle()); // sound on/off (mutes BOTH tracks) — sits right of Settings
    root.appendChild(nav);

    // Game-mode cards — each enabled card launches its Unity scene; Platform Race is a future teaser.
    const modes = document.createElement("div");
    modes.className = "lb-modes lb-clickable";
    const cards: Array<{ mode: GameMode | null; title: string; blurb: string; map: string | null }> = [
      { mode: "spinner", title: "Spinner", blurb: "Survive the spinning arena — dodge the sweeping beams.", map: "/ui/map_spinner.png" },
      { mode: "lastman", title: "Last Man Standing", blurb: "Hexagons vanish as you step. Be the last one standing!", map: "/ui/map_lastman.png" },
      { mode: "rollout", title: "Roll Out", blurb: "Stay on the rolling log — dodge gaps, walls, cells and hammers.", map: "/ui/map_rollout.png" },
      { mode: null, title: "Platform Race", blurb: "A frantic dash to the finish. Launching in a future update.", map: null },
    ];
    for (const c of cards) {
      const card = document.createElement("div");
      card.className = "lb-mode-card" + (c.mode ? "" : " disabled");

      // map thumbnail (background-image so a missing/disabled map is a clean dark box, no broken-img icon)
      const map = document.createElement("div");
      map.className = "lb-mode-map";
      if (c.map) map.style.backgroundImage = `url('${c.map}')`;
      card.appendChild(map);

      const info = document.createElement("div");
      info.className = "lb-mode-info";
      const title = document.createElement("div");
      title.className = "lb-mode-title";
      title.textContent = c.title;
      const blurb = document.createElement("div");
      blurb.className = "lb-mode-blurb";
      blurb.textContent = c.blurb;
      info.appendChild(title);
      info.appendChild(blurb);
      if (c.mode) {
        const jb = document.createElement("button");
        jb.className = "lb-card-join";
        jb.textContent = "JOIN";
        const m = c.mode;
        jb.onclick = () => this.hooks.onJoin(m);
        info.appendChild(jb);
      } else {
        const soon = document.createElement("div");
        soon.className = "lb-soon";
        soon.textContent = "Coming Soon";
        info.appendChild(soon);
      }
      card.appendChild(info);
      modes.appendChild(card);
    }
    this.modesEl = modes;
    root.appendChild(modes);

    root.appendChild(this.buildChat());

    this.setActive("play");
    return root;
  }

  // Sound on/off pill. Mutes/unmutes BOTH music tracks globally (persisted via musicController), not a
  // nav selection — so it's NOT in navButtons and setActive() never recolors it.
  private buildMusicToggle(): HTMLButtonElement {
    const b = document.createElement("button");
    b.type = "button";
    const render = () => {
      const muted = musicController.isMuted();
      b.innerHTML = `${muted ? ICON_SOUND_OFF : ICON_SOUND_ON}<span>${muted ? "Music Off" : "Music On"}</span>`;
      b.style.background = muted ? MUTED_BG : INACTIVE_BG;
    };
    b.onclick = () => { b.blur(); musicController.toggle(); render(); };
    render();
    return b;
  }

  // Lobby chat panel (bottom-right). Input sends via hooks.onChat; incoming lines arrive via addChat().
  private buildChat(): HTMLDivElement {
    const wrap = document.createElement("div");
    wrap.className = "lb-chat lb-clickable";

    const head = document.createElement("div");
    head.className = "lb-chat-head";
    head.textContent = "Lobby Chat";

    const log = document.createElement("div");
    log.className = "lb-chat-log";
    const empty = document.createElement("div");
    empty.className = "lb-chat-empty";
    empty.textContent = "Say hi to other players…";
    log.appendChild(empty);
    this.chatLog = log;

    const inputRow = document.createElement("div");
    inputRow.className = "lb-chat-input";
    const input = document.createElement("input");
    input.type = "text";
    input.maxLength = 240;
    input.placeholder = "Type a message…";
    const sendBtn = document.createElement("button");
    sendBtn.type = "button";
    sendBtn.textContent = "Send";
    const send = () => {
      const text = input.value.trim();
      if (!text) return;
      this.hooks.onChat(text);
      input.value = "";
    };
    sendBtn.onclick = send;
    input.onkeydown = (e) => {
      e.stopPropagation(); // don't leak typing to the lobby camera / game key handlers
      if (e.key === "Enter") { e.preventDefault(); send(); }
    };
    inputRow.appendChild(input);
    inputRow.appendChild(sendBtn);

    wrap.appendChild(head);
    wrap.appendChild(log);
    wrap.appendChild(inputRow);
    return wrap;
  }

  // Append an incoming chat line. `mine` highlights the local player's own messages. Injection-safe (textContent).
  addChat(nick: string, text: string, mine: boolean) {
    if (!this.chatSeeded) { this.chatLog.innerHTML = ""; this.chatSeeded = true; } // drop the placeholder
    const line = document.createElement("div");
    line.className = "lb-chat-line" + (mine ? " me" : "");
    const nk = document.createElement("span");
    nk.className = "nk";
    nk.textContent = nick + ": ";
    line.appendChild(nk);
    line.appendChild(document.createTextNode(text));
    this.chatLog.appendChild(line);
    while (this.chatLog.childElementCount > 120 && this.chatLog.firstChild) this.chatLog.removeChild(this.chatLog.firstChild);
    this.chatLog.scrollTop = this.chatLog.scrollHeight;
  }

  private navButton(id: LobbyNav, label: string, iconSvg: string, onClick: () => void): HTMLButtonElement {
    const b = document.createElement("button");
    b.type = "button";
    b.innerHTML = `${iconSvg}<span>${label}</span>`;
    b.onclick = () => { b.blur(); onClick(); };
    this.navButtons[id] = b;
    return b;
  }

  // highlight the active nav (magenta glossy) vs inactive (teal glossy)
  setActive(nav: LobbyNav) {
    for (const key of Object.keys(this.navButtons) as LobbyNav[]) {
      const btn = this.navButtons[key];
      if (btn) btn.style.background = key === nav ? ACTIVE_BG : INACTIVE_BG;
    }
  }

  // call when the customizer closes so nav returns to Play
  resetNav() {
    this.setActive("play");
  }

  // hide the mode cards while the customizer is open
  showJoin(visible: boolean) {
    this.modesEl.style.display = visible ? "flex" : "none";
  }

  setPlayerName(n: string) {
    this.playerName = n;
    const el = this.root.querySelector(".lb-name");
    if (el) el.textContent = n;
  }

  show() {
    this.root.classList.add("show");
    this.video.classList.add("show");
    this.video.play().catch(() => {});
    // now that the overlay is visible (so measurements are real), seat the cards below the logo
    requestAnimationFrame(() => this.layoutModes());
  }

  hide() {
    this.root.classList.remove("show");
    this.video.classList.remove("show");
    this.video.pause();
  }
}
