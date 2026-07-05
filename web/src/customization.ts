import { Bean } from "./bean";
import { DEFAULT_BODY_HEX, FACE_OPTIONS, SLOTS, SLOT_IDS, SlotId } from "./data/beanPalette";

// The player's saved "look" — flat serializable JSON (localStorage now, NFT-skin payload later).
// The accessory slots (hat/hair/glasses/faceAcc/joker/shoes) hold an item id or null.
export type BeanLook = {
  bodyColor: string; // hex, e.g. "#0000FF"
  face: number; // index into FACE_OPTIONS
} & Record<SlotId, string | null>;

const STORAGE_KEY = "beanLook.v2";

export const DEFAULT_LOOK: BeanLook = {
  bodyColor: DEFAULT_BODY_HEX,
  face: 0,
  hat: null,
  hair: null,
  glasses: null,
  faceAcc: null,
};

export function loadLook(): BeanLook {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (raw) return sanitize({ ...DEFAULT_LOOK, ...JSON.parse(raw) });
  } catch (e) {
    console.warn("loadLook failed:", e);
  }
  return { ...DEFAULT_LOOK };
}

export function saveLook(look: BeanLook) {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(look));
  } catch (e) {
    console.warn("saveLook failed:", e);
  }
}

// Push a look onto the live bean.
export function applyLook(bean: Bean, look: BeanLook) {
  bean.setBodyColor(look.bodyColor);
  bean.setFace(look.face);
  for (const slot of SLOT_IDS) bean.setAccessory(slot, look[slot]);
}

// Clamp/repair a loaded look so bad data can't break rendering.
function sanitize(look: BeanLook): BeanLook {
  const out: BeanLook = { ...DEFAULT_LOOK };
  out.bodyColor = /^#[0-9a-fA-F]{6}$/.test(look.bodyColor) ? look.bodyColor : DEFAULT_BODY_HEX;
  out.face = Number.isInteger(look.face) && look.face >= 0 && look.face < FACE_OPTIONS.length ? look.face : 0;
  for (const slot of SLOT_IDS) {
    const id = look[slot];
    out[slot] = id && SLOTS[slot].items.some((it) => it.id === id) ? id : null;
  }
  return out;
}
