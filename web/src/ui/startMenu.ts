// Start screen — full-screen art + pulsing "CLICK TO START". Any key / click advances.
// Ported from the Unity StartMenu prototype (Start_menu.png + animated TMP prompt).
export class StartMenu {
  private root: HTMLDivElement;
  private onStart: () => void;
  private armed = false;

  constructor(onStart: () => void) {
    this.onStart = onStart;
    this.injectStyles();
    this.root = this.build();
    document.body.appendChild(this.root);
  }

  private injectStyles() {
    const css = `
    #startMenu{position:fixed;inset:0;z-index:40;display:none;
      background:#261341 url('/ui/sol_champ_billboard_3.webp') center/cover no-repeat;
      align-items:flex-end;justify-content:center;cursor:pointer;
      transition:opacity .35s ease}
    #startMenu.show{display:flex}
    #startMenu.fading{opacity:0;pointer-events:none}
    #startMenu .sm-prompt{margin-bottom:9vh;text-align:center;user-select:none;pointer-events:none}
    #startMenu .sm-main{font:800 clamp(36px,7vw,96px)/1 system-ui,sans-serif;color:#fff;
      letter-spacing:.04em;text-shadow:0 4px 18px rgba(0,0,0,.55);
      animation:smPulse 0.909s ease-in-out infinite alternate}
    #startMenu .sm-sub{margin-top:14px;font:italic 500 clamp(14px,2.2vw,34px)/1 system-ui,sans-serif;
      color:#fff;opacity:.75;text-shadow:0 2px 8px rgba(0,0,0,.5)}
    @keyframes smPulse{from{transform:scale(.97);opacity:.55}to{transform:scale(1.03);opacity:1}}
    `;
    const s = document.createElement("style");
    s.textContent = css;
    document.head.appendChild(s);
  }

  private build(): HTMLDivElement {
    const root = document.createElement("div");
    root.id = "startMenu";
    const prompt = document.createElement("div");
    prompt.className = "sm-prompt";
    const main = document.createElement("div");
    main.className = "sm-main";
    main.textContent = "CLICK TO START";
    const sub = document.createElement("div");
    sub.className = "sm-sub";
    sub.textContent = "or press any key";
    prompt.appendChild(main);
    prompt.appendChild(sub);
    root.appendChild(prompt);

    const advance = () => this.trigger();
    root.addEventListener("pointerdown", advance);
    window.addEventListener("keydown", this.onKey);
    return root;
  }

  private onKey = () => {
    if (this.armed) this.trigger();
  };

  private trigger() {
    if (!this.armed) return;
    this.armed = false;
    this.root.classList.add("fading");
    window.removeEventListener("keydown", this.onKey);
    setTimeout(() => {
      this.root.classList.remove("show");
      this.onStart();
    }, 350);
  }

  show() {
    this.armed = true;
    this.root.classList.remove("fading");
    this.root.classList.add("show");
    window.addEventListener("keydown", this.onKey);
  }

  hide() {
    this.armed = false;
    this.root.classList.remove("show");
  }
}
