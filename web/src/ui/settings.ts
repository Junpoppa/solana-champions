// DOM overlay Settings screen: mouse sensitivity (Unity camera), game music volume (web Strudel),
// and game SFX volume (Unity bean jump sound). Values persist to localStorage and are pushed live to
// the game via the hooks (see main.ts wiring → unityGame / musicController).

export interface SettingsUIHooks {
  onChange: (mouseSensitivity: number) => void; // push live to Unity (camera)
  onMusicChange: (volume0to1: number) => void;  // master music volume (web Strudel)
  onSfxChange: (volume0to1: number) => void;     // SFX volume → Unity WebBridge
  onClose: () => void; // return lobby nav to Play
}

export type Settings = { mouseSensitivity: number; musicVolume: number; sfxVolume: number };

const STORAGE_KEY = "settings.v1";
export const SENS_MIN = 0.5;
export const SENS_MAX = 6;
const DEFAULTS: Settings = { mouseSensitivity: 2, musicVolume: 0.7, sfxVolume: 0.7 };

const clampSens = (v: number) =>
  Number.isFinite(v) ? Math.min(SENS_MAX, Math.max(SENS_MIN, v)) : DEFAULTS.mouseSensitivity;
const clamp01 = (v: number, dflt: number) =>
  Number.isFinite(v) ? Math.min(1, Math.max(0, v)) : dflt;

export function loadSettings(): Settings {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (raw) {
      const p = JSON.parse(raw);
      return {
        mouseSensitivity: clampSens(p.mouseSensitivity),
        musicVolume: clamp01(p.musicVolume, DEFAULTS.musicVolume),
        sfxVolume: clamp01(p.sfxVolume, DEFAULTS.sfxVolume),
      };
    }
  } catch (e) {
    console.warn("loadSettings failed:", e);
  }
  return { ...DEFAULTS };
}

export function saveSettings(s: Settings) {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify({
      mouseSensitivity: clampSens(s.mouseSensitivity),
      musicVolume: clamp01(s.musicVolume, DEFAULTS.musicVolume),
      sfxVolume: clamp01(s.sfxVolume, DEFAULTS.sfxVolume),
    }));
  } catch (e) {
    console.warn("saveSettings failed:", e);
  }
}

export class SettingsUI {
  private hooks: SettingsUIHooks;
  private settings: Settings;
  private panel!: HTMLDivElement;
  private refreshers: Array<() => void> = [];
  open = false;

  constructor(hooks: SettingsUIHooks) {
    this.hooks = hooks;
    this.settings = loadSettings();
    this.injectStyles();
    this.buildPanel();
    this.refresh();
  }

  private injectStyles() {
    const css = `
    #settingsPanel{position:fixed;top:0;right:0;height:100%;width:340px;z-index:30;
      background:rgba(20,18,30,.94);backdrop-filter:blur(8px);color:#fff;
      font:400 13px/1.3 system-ui,sans-serif;display:none;flex-direction:column;
      box-shadow:-6px 0 24px rgba(0,0,0,.4);overflow-y:auto}
    #settingsPanel.show{display:flex}
    #settingsPanel h2{font-size:18px;margin:0;padding:16px 18px 6px}
    .st-sec{padding:14px 18px;border-top:1px solid rgba(255,255,255,.08)}
    .st-sec h3{font-size:11px;text-transform:uppercase;letter-spacing:.08em;color:#b9aee0;margin:0 0 10px}
    .st-row{display:flex;align-items:center;gap:12px}
    .st-row input[type=range]{flex:1;accent-color:#ff2d9e;height:4px}
    .st-val{min-width:42px;text-align:right;font-weight:700;color:#fee64d}
    .st-hint{margin-top:8px;color:#8a7fb0;font-size:11px}
    #stClose{position:absolute;top:14px;right:16px;background:0;border:0;color:#fff;font-size:22px;cursor:pointer;line-height:1}
    `;
    const s = document.createElement("style");
    s.textContent = css;
    document.head.appendChild(s);
  }

  // Generic slider section. `live` fires onInput; `commit` (optional) fires onChange (release) instead/also.
  private makeSlider(opts: {
    title: string; hint: string; min: number; max: number; step: number;
    get: () => number; set: (v: number) => void; fmt: (v: number) => string;
    onLive?: (v: number) => void; onCommit?: (v: number) => void;
  }) {
    const sec = document.createElement("div");
    sec.className = "st-sec";
    const title = document.createElement("h3");
    title.textContent = opts.title;
    sec.appendChild(title);

    const row = document.createElement("div");
    row.className = "st-row";
    const slider = document.createElement("input");
    slider.type = "range";
    slider.min = String(opts.min);
    slider.max = String(opts.max);
    slider.step = String(opts.step);
    const val = document.createElement("span");
    val.className = "st-val";

    const refreshVal = () => { val.textContent = opts.fmt(opts.get()); };
    slider.oninput = () => {
      const v = parseFloat(slider.value);
      opts.set(v);
      saveSettings(this.settings);
      refreshVal();
      opts.onLive?.(v);
    };
    if (opts.onCommit) slider.onchange = () => opts.onCommit!(parseFloat(slider.value));

    row.appendChild(slider);
    row.appendChild(val);
    sec.appendChild(row);
    const hint = document.createElement("div");
    hint.className = "st-hint";
    hint.textContent = opts.hint;
    sec.appendChild(hint);

    this.refreshers.push(() => { slider.value = String(opts.get()); refreshVal(); });
    return sec;
  }

  private buildPanel() {
    const p = document.createElement("div");
    p.id = "settingsPanel";

    const close = document.createElement("button");
    close.id = "stClose";
    close.innerHTML = "&times;";
    close.onclick = () => this.hide();
    p.appendChild(close);

    const h = document.createElement("h2");
    h.textContent = "Settings";
    p.appendChild(h);

    const pct = (v: number) => Math.round(v * 100) + "%";

    p.appendChild(this.makeSlider({
      title: "Mouse Sensitivity", hint: "Controls how fast the camera turns in-game.",
      min: SENS_MIN, max: SENS_MAX, step: 0.1,
      get: () => this.settings.mouseSensitivity,
      set: (v) => { this.settings.mouseSensitivity = v; },
      fmt: (v) => v.toFixed(1),
      onLive: (v) => this.hooks.onChange(v),
    }));

    // Music: update label/save live, but only re-eval the track on release (onCommit) to avoid choppiness.
    p.appendChild(this.makeSlider({
      title: "Game Music", hint: "Background music volume.",
      min: 0, max: 1, step: 0.01,
      get: () => this.settings.musicVolume,
      set: (v) => { this.settings.musicVolume = v; },
      fmt: pct,
      onCommit: (v) => this.hooks.onMusicChange(v),
    }));

    p.appendChild(this.makeSlider({
      title: "Game SFX", hint: "Jump / movement sound effects.",
      min: 0, max: 1, step: 0.01,
      get: () => this.settings.sfxVolume,
      set: (v) => { this.settings.sfxVolume = v; },
      fmt: pct,
      onLive: (v) => this.hooks.onSfxChange(v),
    }));

    this.panel = p;
    document.body.appendChild(p);
  }

  private refresh() {
    for (const r of this.refreshers) r();
  }

  show() {
    if (this.open) return;
    this.open = true;
    this.settings = loadSettings();
    this.refresh();
    this.panel.classList.add("show");
  }

  hide() {
    if (!this.open) return;
    this.open = false;
    this.panel.classList.remove("show");
    this.hooks.onClose();
  }
}
