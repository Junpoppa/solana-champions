// Identity screen — shown BEFORE the start menu. Player picks a nickname and (optionally)
// a Solana address for winner payouts. Persisted to localStorage (playerProfile.v1).

import {
  PlayerProfile,
  NICK_MAX,
  sanitizeNick,
  isValidSolAddress,
  randomGuestNick,
  loadProfile,
  saveProfile,
} from "../profile";

export class IdentityScreen {
  private root: HTMLDivElement;
  private onSubmit: (p: PlayerProfile) => void;
  private nickEl!: HTMLInputElement;
  private addrEl!: HTMLInputElement;
  private warnEl!: HTMLDivElement;

  constructor(onSubmit: (p: PlayerProfile) => void) {
    this.onSubmit = onSubmit;
    this.injectStyles();
    this.root = this.build();
    document.body.appendChild(this.root);
  }

  private injectStyles() {
    const css = `
    #identity{position:fixed;inset:0;z-index:45;display:none;
      background:#261341 url('/ui/sol_champ_billboard_3.png') center/cover no-repeat;
      align-items:center;justify-content:center;
      transition:opacity .35s ease}
    #identity.show{display:flex}
    #identity.fading{opacity:0;pointer-events:none}
    #identity .id-panel{width:min(92vw,440px);padding:30px 30px 26px;border-radius:22px;
      background:rgba(20,12,38,.72);backdrop-filter:blur(10px);
      box-shadow:0 16px 44px rgba(0,0,0,.5),inset 0 1px 0 rgba(255,255,255,.14);
      font:600 14px/1 system-ui,sans-serif;color:#fff}
    #identity .id-title{font:800 clamp(24px,4vw,34px)/1.05 system-ui,sans-serif;color:#fee64d;
      text-align:center;text-shadow:0 2px 6px rgba(0,0,0,.45)}
    #identity .id-sub{margin-top:8px;text-align:center;color:#cbb6ff;font-weight:600;font-size:13px;opacity:.9}
    #identity label{display:block;margin-top:20px;margin-bottom:7px;
      font:700 11px/1 system-ui;letter-spacing:.12em;text-transform:uppercase;color:#cbb6ff}
    #identity .id-opt{color:#8f83b8;font-weight:600;text-transform:none;letter-spacing:0}
    #identity input{width:100%;box-sizing:border-box;height:48px;padding:0 14px;border-radius:12px;
      border:2px solid rgba(255,255,255,.16);background:rgba(255,255,255,.08);color:#fff;
      font:700 17px/1 system-ui,sans-serif;outline:none;transition:border-color .12s ease}
    #identity input:focus{border-color:#ff2d9e}
    #identity input::placeholder{color:rgba(255,255,255,.4);font-weight:600}
    #identity .id-warn{margin-top:8px;min-height:16px;font-size:12px;font-weight:700;color:#ffb64d}
    #identity .id-go{margin-top:22px;width:100%;height:56px;border:0;border-radius:14px;cursor:pointer;color:#fff;
      font:800 22px/1 system-ui,sans-serif;letter-spacing:.02em;background:#ff2d9e;
      text-shadow:0 2px 4px rgba(0,0,0,.5);box-shadow:0 6px 0 #b01b6e,0 9px 18px rgba(0,0,0,.4);
      transition:transform .12s ease,filter .12s ease}
    #identity .id-go:hover{transform:scale(1.03);filter:brightness(1.1)}
    #identity .id-go:active{transform:translateY(3px);box-shadow:0 3px 0 #b01b6e,0 6px 12px rgba(0,0,0,.4)}
    `;
    const s = document.createElement("style");
    s.textContent = css;
    document.head.appendChild(s);
  }

  private build(): HTMLDivElement {
    const root = document.createElement("div");
    root.id = "identity";

    const panel = document.createElement("div");
    panel.className = "id-panel";

    const title = document.createElement("div");
    title.className = "id-title";
    title.textContent = "Enter the Arena";
    const sub = document.createElement("div");
    sub.className = "id-sub";
    sub.textContent = "Pick a name. Add a Solana address to claim winnings.";

    const nickLabel = document.createElement("label");
    nickLabel.textContent = "Nickname";
    this.nickEl = document.createElement("input");
    this.nickEl.type = "text";
    this.nickEl.maxLength = NICK_MAX;
    this.nickEl.placeholder = "e.g. BeanKing";
    this.nickEl.autocomplete = "off";
    this.nickEl.spellcheck = false;

    const addrLabel = document.createElement("label");
    addrLabel.innerHTML = 'Solana Address <span class="id-opt">(optional)</span>';
    this.addrEl = document.createElement("input");
    this.addrEl.type = "text";
    this.addrEl.placeholder = "paste your wallet address";
    this.addrEl.autocomplete = "off";
    this.addrEl.spellcheck = false;

    this.warnEl = document.createElement("div");
    this.warnEl.className = "id-warn";

    const go = document.createElement("button");
    go.className = "id-go";
    go.type = "button";
    go.textContent = "CONTINUE";
    go.onclick = () => this.submit();

    // prefill from a saved profile
    const saved = loadProfile();
    if (saved) {
      this.nickEl.value = saved.nick;
      if (saved.solAddress) this.addrEl.value = saved.solAddress;
    }

    this.addrEl.addEventListener("input", () => this.validateAddr());
    this.nickEl.addEventListener("keydown", (e) => { if (e.key === "Enter") this.submit(); });
    this.addrEl.addEventListener("keydown", (e) => { if (e.key === "Enter") this.submit(); });

    panel.append(title, sub, nickLabel, this.nickEl, addrLabel, this.addrEl, this.warnEl, go);
    root.appendChild(panel);
    return root;
  }

  private validateAddr(): boolean {
    const raw = this.addrEl.value.trim();
    if (raw && !isValidSolAddress(raw)) {
      this.warnEl.textContent = "That doesn't look like a valid Solana address — it won't be saved.";
      return false;
    }
    this.warnEl.textContent = "";
    return true;
  }

  private submit() {
    const nick = sanitizeNick(this.nickEl.value) || randomGuestNick();
    const rawAddr = this.addrEl.value.trim();
    const solAddress = rawAddr && isValidSolAddress(rawAddr) ? rawAddr : null;
    if (rawAddr && !solAddress) this.validateAddr(); // surface the warning but proceed
    const profile: PlayerProfile = { nick, solAddress };
    saveProfile(profile);

    this.root.classList.add("fading");
    setTimeout(() => {
      this.root.classList.remove("show");
      this.onSubmit(profile);
    }, 350);
  }

  show() {
    this.root.classList.remove("fading");
    this.root.classList.add("show");
    setTimeout(() => this.nickEl.focus(), 60);
  }

  hide() {
    this.root.classList.remove("show");
  }
}
