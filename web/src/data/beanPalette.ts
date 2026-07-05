// Bean body-color palette — extracted from the FREE Party Characters pack
// (Assets/.../Resources/Materials/Body/*.mat _BaseColor). 12 hues x 3 shades.

export interface BodySwatch {
  name: string;
  hex: string;
}

// Default the pack ships on character_default = "Blue 2 Base".
export const DEFAULT_BODY_HEX = "#0000FF";

export const BODY_PALETTE: BodySwatch[] = [
  { name: "Red 1 Dark", hex: "#660000" },
  { name: "Red 2 Base", hex: "#FF0000" },
  { name: "Red 3 Light", hex: "#FF9999" },
  { name: "Orange 1 Dark", hex: "#663300" },
  { name: "Orange 2 Base", hex: "#FF8000" },
  { name: "Orange 3 Light", hex: "#FFCC99" },
  { name: "Yellow 1 Dark", hex: "#666600" },
  { name: "Yellow 2 Base", hex: "#FFFF00" },
  { name: "Yellow 3 Light", hex: "#FFFF99" },
  { name: "Green 1 Dark", hex: "#006600" },
  { name: "Green 2 Base", hex: "#00FF00" },
  { name: "Green 3 Light", hex: "#99FF99" },
  { name: "Turquoise 1 Dark", hex: "#195C46" },
  { name: "Turquoise 2 Base", hex: "#40E6B1" },
  { name: "Turquoise 3 Light", hex: "#B3F5E0" },
  { name: "Cyan 1 Dark", hex: "#006666" },
  { name: "Cyan 2 Base", hex: "#00FFFF" },
  { name: "Cyan 3 Light", hex: "#99FFFF" },
  { name: "Blue 1 Dark", hex: "#000066" },
  { name: "Blue 2 Base", hex: "#0000FF" },
  { name: "Blue 3 Light", hex: "#9999FF" },
  { name: "Purple 1 Dark", hex: "#330066" },
  { name: "Purple 2 Base", hex: "#8000FF" },
  { name: "Purple 3 Light", hex: "#CC99FF" },
  { name: "Pink 1 Dark", hex: "#662947" },
  { name: "Pink 2 Base", hex: "#FF66B2" },
  { name: "Pink 3 Light", hex: "#FFC2E0" },
  { name: "Brown 1 Dark", hex: "#33190A" },
  { name: "Brown 2 Base", hex: "#804019" },
  { name: "Brown 3 Light", hex: "#CCB3A3" },
  { name: "Cream 1 Dark", hex: "#827251" },
  { name: "Cream 2 Base", hex: "#C5AD7E" },
  { name: "Cream 3 Light", hex: "#FAE4B7" },
  { name: "Grey 1 Dark", hex: "#1D1D1D" },
  { name: "Grey 2 Base", hex: "#808080" },
  { name: "Grey 3 Light", hex: "#F1F1F1" },
];

// Face options — one named expression per entry. `file` = the web texture (public/textures/faces/);
// `tex` = the matching Unity Resources texture basename ("Materials/Face Images/<tex>") applied in-match.
// look.face is the INDEX into this list (kept index-based for the stored BeanLook / NFT payload).
export interface FaceOption {
  id: string;
  label: string;
  file: string; // web texture url
  tex: string;  // Unity Resources basename (under "Materials/Face Images/")
}

export const FACES: FaceOption[] = [
  { id: "happy", label: "Happy", file: "/textures/faces/face1.png", tex: "face 1" },
  { id: "mad", label: "Mad", file: "/textures/faces/face2.png", tex: "face 2" },
  { id: "sad", label: "Sad", file: "/textures/faces/face3.png", tex: "face 3" },
  { id: "angry", label: "Angry", file: "/textures/faces/Angry.png", tex: "Angry" },
  { id: "bored", label: "Bored", file: "/textures/faces/Bored.png", tex: "Bored" },
  { id: "confused", label: "Confused", file: "/textures/faces/Confused.png", tex: "Confused" },
  { id: "delighted", label: "Delighted", file: "/textures/faces/Delighted.png", tex: "Delighted" },
  { id: "flirty", label: "Flirty", file: "/textures/faces/Flirty.png", tex: "Flirty" },
  { id: "shocked", label: "Shocked", file: "/textures/faces/Shocked.png", tex: "Shocked" },
  { id: "tricky", label: "Tricky", file: "/textures/faces/Tricky.png", tex: "Tricky" },
  { id: "worried", label: "Worried", file: "/textures/faces/Worried.png", tex: "Worried" },
  { id: "terrified", label: "Terrified", file: "/textures/faces/terrified.png", tex: "terrified" },
];

// Back-compat: plain url list (bean.ts face-texture loader + length/index checks).
export const FACE_OPTIONS: string[] = FACES.map((f) => f.file);

// ---------------------------------------------------------------------------
// Accessory slots — generalized from the single hat slot.
// Each item's mesh is a node inside hats.glb or accessories.glb (by `node` name).
// `fit` positions/scales/rotates the item on its attach bone (customize_objects for
// head items; both foot bones for shoes). Fits start ~identity and are tuned in-browser.
// ---------------------------------------------------------------------------

export type SlotId = "hat" | "hair" | "glasses" | "faceAcc";

export interface Fit {
  x?: number;
  y?: number;
  z?: number;
  scale?: number;
  rotX?: number;
  rotY?: number;
  rotZ?: number;
}

export interface AccItem {
  id: string;
  label: string;
  node: string; // node name inside hats.glb / accessories.glb
}

export interface SlotDef {
  label: string;
  attach: "head" | "feet"; // head = customize_objects node; feet = Left/Right Foot bones
  items: AccItem[];
  fits: Record<string, Fit>; // per-item committed placement
}

export const SLOT_IDS: SlotId[] = ["hat", "hair", "glasses", "faceAcc"];

export const SLOTS: Record<SlotId, SlotDef> = {
  hat: {
    label: "Hat",
    attach: "head",
    items: [
      { id: "party_hat", label: "Party Hat", node: "party_hat" },
      { id: "chef_hat", label: "Chef Hat", node: "chef_hat" },
      { id: "orange_fedora", label: "Fedora", node: "orange_fedora" },
      { id: "hat_010", label: "Straw Hat", node: "hat_010" },
      { id: "hat_single_013", label: "Aviator", node: "hat_single_013" },
      { id: "joker", label: "Joker Hat", node: "joker" },
      // Sidekick headwear — baked from AHED FBX into headwear.glb (head-local, identity-fit like the hairs).
      // ids match the WebBridge REG keys; pushed down -0.125 like the hair batch.
      { id: "head_warrior", label: "Warrior Headgear", node: "head_warrior" },
      { id: "head_pumpkin", label: "Pumpkin", node: "head_pumpkin" },
      { id: "head_fox", label: "fox", node: "head_fox" },
      { id: "head_assassin", label: "Assassin", node: "head_assassin" },
    ],
    // Per-item placement, tuned in-browser then committed here.
    fits: {
      hat_010: { x: 0.005, y: -0.1, z: 0.035, scale: 0.96, rotX: 0, rotY: 0, rotZ: 2 },
      hat_single_013: { x: 0.02, y: -0.085, z: -0.055, scale: 0.96, rotX: -15, rotY: -5, rotZ: 3 },
      party_hat: { x: 0, y: -0.025, z: 0, scale: 1, rotX: 0, rotY: 0, rotZ: 0 },
      head_warrior: { y: -0.125, scale: 0.93 },
      head_pumpkin: { y: -0.125, scale: 0.93 },
      head_fox: { y: -0.125, scale: 0.93 },
      head_assassin: { y: -0.125, scale: 0.93 },
    },
  },
  hair: {
    label: "Hair",
    attach: "head",
    items: [
      { id: "hair_001", label: "Hair 1", node: "hair_001" },
      { id: "hair_005", label: "Hair 2", node: "hair_005" },
      { id: "hair_006", label: "Hair 3", node: "hair_006" },
      // Sidekick "Hair 4" — baked from SK_HUMN_BASE_01_02HAIR, exported in sidekick.glb (head-local,
      // so identity fit sits it on the head like Hair 1/2/3). In-game id matches WebBridge REG "hair_HUMN01".
      { id: "hair_HUMN01", label: "Hair 4", node: "hair_HUMN01" },
      // Hair 5..13 — batched from SK_HUMN_BASE_02..10_02HAIR, all in sidekick.glb (head-local, identity fit).
      { id: "hair_HUMN02", label: "Hair 5", node: "hair_HUMN02" },
      { id: "hair_HUMN03", label: "Hair 6", node: "hair_HUMN03" },
      { id: "hair_HUMN04", label: "Hair 7", node: "hair_HUMN04" },
      { id: "hair_HUMN05", label: "Hair 8", node: "hair_HUMN05" },
      { id: "hair_HUMN06", label: "Hair 9", node: "hair_HUMN06" },
      { id: "hair_HUMN07", label: "Hair 10", node: "hair_HUMN07" },
      { id: "hair_HUMN08", label: "Hair 11", node: "hair_HUMN08" },
      { id: "hair_HUMN09", label: "Hair 12", node: "hair_HUMN09" },
      { id: "hair_HUMN10", label: "Hair 13", node: "hair_HUMN10" },
    ],
    // Hair 5..13 sit a touch high at identity — nudge down slightly (Hair 4 = HUMN01 stays at identity).
    fits: {
      hair_HUMN02: { y: -0.125 },
      hair_HUMN03: { y: -0.125 },
      hair_HUMN04: { y: -0.125 },
      hair_HUMN05: { y: -0.125 },
      hair_HUMN06: { y: -0.125 },
      hair_HUMN07: { y: -0.125 },
      hair_HUMN08: { y: -0.125 },
      hair_HUMN09: { y: -0.125 },
      hair_HUMN10: { y: -0.125 },
    },
  },
  glasses: {
    label: "Glasses",
    attach: "head",
    items: [
      { id: "glasses_006", label: "Glasses 006", node: "glasses_006" },
    ],
    fits: {
      glasses_006: { x: -0.025, y: 0.095, z: -0.18, scale: 1.16, rotX: 0, rotY: 3, rotZ: 0 },
    },
  },
  faceAcc: {
    label: "Face Accessory",
    attach: "head",
    items: [
      { id: "clown_nose", label: "Clown Nose", node: "faceacc_clown_nose" },
      { id: "pacifier", label: "Pacifier", node: "faceacc_pacifier" },
    ],
    fits: {
      clown_nose: { x: -0.025, y: 0.08, z: 0.02, scale: 0.87, rotX: 0, rotY: 3, rotZ: 0 },
      pacifier: { x: 0.065, y: 0.08, z: -0.18, scale: 1.34, rotX: -11, rotY: -6, rotZ: 3 },
    },
  },
};
