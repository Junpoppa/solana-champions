import { Bean } from "../bean";
import { BeanLook, applyLook, loadLook, saveLook } from "../customization";
import { BODY_PALETTE, FACES, FACE_OPTIONS, SLOTS, SLOT_IDS, SlotId } from "../data/beanPalette";

// DOM overlay clothing/customization screen.
export interface ClothingUIHooks {
  onOpen: () => void;
  onClose: () => void;
  onSaved?: (look: BeanLook) => void; // pushed when the user hits Save & Equip (e.g. → Unity)
}

export class ClothingUI {
  private bean: Bean;
  private hooks: ClothingUIHooks;
  private look: BeanLook;
  private panel!: HTMLDivElement;
  private swatches: HTMLButtonElement[] = [];
  private faceLabel!: HTMLSpanElement;
  private slotLabels: Partial<Record<SlotId, HTMLSpanElement>> = {};
  private slotSteppers: Partial<Record<SlotId, HTMLDivElement>> = {};

  open = false;

  constructor(bean: Bean, hooks: ClothingUIHooks) {
    this.bean = bean;
    this.hooks = hooks;
    this.look = loadLook();
    this.injectStyles();
    this.buildButton();
    this.buildPanel();
    applyLook(this.bean, this.look);
    this.refreshAll();
  }

  private injectStyles() {
    const css = `
    #customizeBtn{position:fixed;top:14px;left:14px;z-index:20;cursor:pointer;border:0;
      font:700 14px/1 system-ui,sans-serif;color:#fff;background:#8b3cff;padding:10px 16px;
      border-radius:10px;box-shadow:0 3px 10px rgba(0,0,0,.3)}
    #customizeBtn:hover{background:#9d57ff}
    #clothingPanel{position:fixed;top:0;right:0;height:100%;width:340px;z-index:30;
      background:rgba(20,18,30,.94);backdrop-filter:blur(8px);color:#fff;
      font:400 13px/1.3 system-ui,sans-serif;display:none;flex-direction:column;
      box-shadow:-6px 0 24px rgba(0,0,0,.4);overflow-y:auto}
    #clothingPanel.show{display:flex}
    #clothingPanel h2{font-size:18px;margin:0;padding:16px 18px 6px}
    .cl-sec{padding:10px 18px;border-top:1px solid rgba(255,255,255,.08)}
    .cl-sec h3{font-size:11px;text-transform:uppercase;letter-spacing:.08em;color:#b9aee0;margin:0 0 8px}
    .cl-sec.cl-disabled{opacity:.4;pointer-events:none}
    .cl-sec.cl-disabled h3::after{content:" — off (other equipped)";color:#8a7fb0;font-weight:400;text-transform:none;letter-spacing:0}
    .cl-swatches{display:grid;grid-template-columns:repeat(6,1fr);gap:6px}
    .cl-swatch{width:100%;aspect-ratio:1;border-radius:6px;border:2px solid transparent;cursor:pointer;padding:0}
    .cl-swatch.sel{border-color:#fff;box-shadow:0 0 0 2px #8b3cff}
    .cl-step{display:flex;align-items:center;justify-content:space-between;gap:8px}
    .cl-step button{width:34px;height:30px;border:0;border-radius:7px;cursor:pointer;background:#3a3450;color:#fff;font:700 16px/1 system-ui}
    .cl-step button:hover{background:#4a4366}
    .cl-step span{flex:1;text-align:center;font-weight:600;font-size:12px}
    .cl-row{display:flex;gap:10px;padding:12px 18px}
    .cl-row button{flex:1;height:40px;border:0;border-radius:10px;cursor:pointer;font:700 13px/1 system-ui;color:#fff}
    #clRandom{background:#3a3450}#clRandom:hover{background:#4a4366}
    #clSave{background:#27c08a}#clSave:hover{background:#2ad79b}
    #clClose{position:absolute;top:14px;right:16px;background:0;border:0;color:#fff;font-size:22px;cursor:pointer;line-height:1}
    `;
    const s = document.createElement("style");
    s.textContent = css;
    document.head.appendChild(s);
  }

  private buildButton() {
    const btn = document.createElement("button");
    btn.id = "customizeBtn";
    btn.textContent = "👕 Customize";
    btn.onclick = () => this.show();
    document.body.appendChild(btn);
  }

  private buildPanel() {
    const p = document.createElement("div");
    p.id = "clothingPanel";

    const close = document.createElement("button");
    close.id = "clClose";
    close.innerHTML = "&times;";
    close.onclick = () => this.hide();
    p.appendChild(close);

    const h = document.createElement("h2");
    h.textContent = "Customize Bean";
    p.appendChild(h);

    // body color
    const body = section("Body Color");
    const grid = document.createElement("div");
    grid.className = "cl-swatches";
    BODY_PALETTE.forEach((sw) => {
      const b = document.createElement("button");
      b.className = "cl-swatch";
      b.style.background = sw.hex;
      b.title = sw.name;
      b.onclick = () => {
        this.look.bodyColor = sw.hex;
        this.bean.setBodyColor(sw.hex);
        this.refreshSwatches();
      };
      this.swatches.push(b);
      grid.appendChild(b);
    });
    body.appendChild(grid);
    p.appendChild(body);

    // face stepper
    const face = section("Face");
    this.faceLabel = document.createElement("span");
    face.appendChild(stepper(() => this.cycleFace(-1), () => this.cycleFace(1), this.faceLabel));
    p.appendChild(face);

    // a stepper per accessory slot
    for (const slot of SLOT_IDS) {
      const sec = section(SLOTS[slot].label);
      const label = document.createElement("span");
      this.slotLabels[slot] = label;
      sec.appendChild(stepper(() => this.cycleSlot(slot, -1), () => this.cycleSlot(slot, 1), label));
      this.slotSteppers[slot] = sec;
      p.appendChild(sec);
    }

    // actions
    const row = document.createElement("div");
    row.className = "cl-row";
    const rand = document.createElement("button");
    rand.id = "clRandom";
    rand.textContent = "Randomize";
    rand.onclick = () => this.randomize();
    const save = document.createElement("button");
    save.id = "clSave";
    save.textContent = "Save & Equip";
    save.onclick = () => {
      saveLook(this.look);
      this.hooks.onSaved?.(this.look);
      this.hide(); // Save & Equip now closes the customizer (no separate X press). hide() fires onClose → back to lobby.
    };
    row.appendChild(rand);
    row.appendChild(save);
    p.appendChild(row);

    this.panel = p;
    document.body.appendChild(p);
  }

  private cycleFace(dir: number) {
    const n = FACE_OPTIONS.length;
    this.look.face = (this.look.face + dir + n) % n;
    this.bean.setFace(this.look.face);
    this.refreshFaceLabel();
  }

  private cycleSlot(slot: SlotId, dir: number) {
    const opts: (string | null)[] = [null, ...SLOTS[slot].items.map((i) => i.id)];
    let i = opts.indexOf(this.look[slot]);
    if (i < 0) i = 0;
    i = (i + dir + opts.length) % opts.length;
    this.look[slot] = opts[i];
    this.bean.setAccessory(slot, this.look[slot]);
    this.refreshSlotLabel(slot);
    this.syncHeadwear(slot);
  }

  private randomize() {
    this.look.bodyColor = BODY_PALETTE[(Math.random() * BODY_PALETTE.length) | 0].hex;
    this.look.face = (Math.random() * FACE_OPTIONS.length) | 0;
    for (const slot of SLOT_IDS) {
      const opts: (string | null)[] = [null, ...SLOTS[slot].items.map((i) => i.id)];
      this.look[slot] = opts[(Math.random() * opts.length) | 0];
    }
    applyLook(this.bean, this.look);
    this.refreshAll();
  }

  private refreshAll() {
    this.refreshSwatches();
    this.refreshFaceLabel();
    for (const slot of SLOT_IDS) this.refreshSlotLabel(slot);
    this.syncHeadwear();
  }

  // Hats and hair share the head: only one at a time. The slot just changed wins;
  // the other is removed and its picker greyed out. Works both directions.
  private syncHeadwear(changed?: SlotId) {
    if (this.look.hat && this.look.hair) {
      const drop: SlotId = changed === "hat" ? "hair" : "hat";
      this.look[drop] = null;
      this.bean.setAccessory(drop, null);
      this.refreshSlotLabel(drop);
    }
    this.slotSteppers.hat?.classList.toggle("cl-disabled", !!this.look.hair);
    this.slotSteppers.hair?.classList.toggle("cl-disabled", !!this.look.hat);
  }

  private refreshSwatches() {
    BODY_PALETTE.forEach((sw, i) => this.swatches[i].classList.toggle("sel", sw.hex === this.look.bodyColor));
  }

  private refreshFaceLabel() {
    this.faceLabel.textContent = FACES[this.look.face]?.label ?? `Face ${this.look.face + 1}`;
  }

  private refreshSlotLabel(slot: SlotId) {
    const id = this.look[slot];
    const item = id ? SLOTS[slot].items.find((it) => it.id === id) : null;
    const span = this.slotLabels[slot];
    if (span) span.textContent = item ? item.label : "None";
  }

  show() {
    if (this.open) return;
    this.open = true;
    this.panel.classList.add("show");
    this.hooks.onOpen();
  }

  hide() {
    if (!this.open) return;
    this.open = false;
    this.panel.classList.remove("show");
    this.hooks.onClose();
  }
}

function section(title: string): HTMLDivElement {
  const d = document.createElement("div");
  d.className = "cl-sec";
  if (title) {
    const h = document.createElement("h3");
    h.textContent = title;
    d.appendChild(h);
  }
  return d;
}

function stepper(onPrev: () => void, onNext: () => void, label: HTMLSpanElement): HTMLDivElement {
  const d = document.createElement("div");
  d.className = "cl-step";
  const prev = document.createElement("button");
  prev.innerHTML = "&#9664;";
  prev.onclick = onPrev;
  const next = document.createElement("button");
  next.innerHTML = "&#9654;";
  next.onclick = onNext;
  d.appendChild(prev);
  d.appendChild(label);
  d.appendChild(next);
  return d;
}
