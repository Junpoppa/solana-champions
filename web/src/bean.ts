import * as THREE from "three";
import { GLTFLoader } from "three/examples/jsm/loaders/GLTFLoader.js";
import { FACE_OPTIONS, SLOTS, SlotId, Fit } from "./data/beanPalette";

// Visual bean character loaded from Unity-exported glb.
// - bean.glb : skinned "Character mesh" + skeleton + "customize_objects" (head attach) + foot bones.
// - hats.glb : original hats (party/chef/fedora/rabbit).
// - accessories.glb : the rest (hats/hair/glasses/face-acc/joker/shoes). Loaded if present.
// Accessory items are nodes in those glbs, attached to the head node or the foot bones with a Fit.

const TARGET_HEIGHT = 1.85;
const FEET_Y = -0.9;
const FACE_FORWARD = true;
const TUNE_KEY = "beanFits.v1"; // localStorage: in-browser tuning overrides

const deg = (d?: number) => ((d ?? 0) * Math.PI) / 180;

export class Bean {
  readonly group = new THREE.Group();
  body!: THREE.Mesh;
  face!: THREE.Mesh;
  customizeNode!: THREE.Object3D; // head attach (customize_objects)
  private leftFoot?: THREE.Object3D;
  private rightFoot?: THREE.Object3D;

  private faceTextures: THREE.Texture[] = [];
  private protos = new Map<string, THREE.Object3D>(); // node name -> prototype
  private equipped: Partial<Record<SlotId, THREE.Object3D[]>> = {};
  private equippedId: Partial<Record<SlotId, string | null>> = {};
  private tunedFits: Record<string, Fit> = {}; // key `${slot}:${id}` -> override

  // Animation
  private mixer?: THREE.AnimationMixer;
  private actions: { idle?: THREE.AnimationAction; run?: THREE.AnimationAction; jump?: THREE.AnimationAction } = {};
  private locoState: "idle" | "run" | "jump" = "idle";
  private paused = false; // freeze animation (customize mode) so the bean holds still
  private static RUN_REF_SPEED = 7;

  static async load(): Promise<Bean> {
    const loader = new GLTFLoader();
    const [beanGltf, hatsGltf] = await Promise.all([
      loader.loadAsync("/models/bean.glb"),
      loader.loadAsync("/models/hats.glb"),
    ]);
    // accessories.glb + sidekick.glb + headwear.glb are optional — load gracefully so a missing file never breaks the bean.
    const accGltf = await loader.loadAsync("/models/accessories.glb").catch(() => null);
    const sidekickGltf = await loader.loadAsync("/models/sidekick.glb").catch(() => null);
    const headwearGltf = await loader.loadAsync("/models/headwear.glb").catch(() => null);

    const bean = new Bean();
    bean.build(beanGltf.scene, hatsGltf.scene, accGltf?.scene ?? null, sidekickGltf?.scene ?? null, beanGltf.animations, headwearGltf?.scene ?? null);
    return bean;
  }

  private build(
    beanScene: THREE.Object3D,
    hatsScene: THREE.Object3D,
    accScene: THREE.Object3D | null,
    sidekickScene: THREE.Object3D | null,
    clips: THREE.AnimationClip[],
    headwearScene: THREE.Object3D | null = null
  ) {
    // classify body vs face mesh
    beanScene.traverse((o) => {
      const m = o as THREE.Mesh;
      if (!(m as any).isMesh) return;
      const mat = m.material as THREE.MeshStandardMaterial;
      m.castShadow = true;
      m.frustumCulled = false;
      const name = (mat?.name || "").toLowerCase();
      if (name.startsWith("face")) this.face = m;
      else this.body = m;
    });

    // attach points
    this.customizeNode = beanScene.getObjectByName("customize_objects") ?? beanScene.getObjectByName("Head") ?? beanScene;
    this.leftFoot = beanScene.getObjectByName("LeftFoot") ?? undefined;
    this.rightFoot = beanScene.getObjectByName("RightFoot") ?? undefined;

    // auto-fit to capsule
    const box = new THREE.Box3().setFromObject(beanScene);
    const size = new THREE.Vector3();
    box.getSize(size);
    const scale = size.y > 0 ? TARGET_HEIGHT / size.y : 1;
    beanScene.scale.setScalar(scale);
    const box2 = new THREE.Box3().setFromObject(beanScene);
    beanScene.position.y += FEET_Y - box2.min.y;
    if (!FACE_FORWARD) beanScene.rotation.y = Math.PI;
    this.group.add(beanScene);

    // face textures
    const texLoader = new THREE.TextureLoader();
    this.faceTextures = FACE_OPTIONS.map((url) => {
      const t = texLoader.load(url);
      t.flipY = false;
      t.colorSpace = THREE.SRGBColorSpace;
      return t;
    });

    // collect accessory prototypes by node name from both glbs
    const collect = (scene: THREE.Object3D | null) => {
      if (!scene) return;
      for (const slot of Object.values(SLOTS)) {
        for (const item of slot.items) {
          if (this.protos.has(item.node)) continue;
          const node = scene.getObjectByName(item.node);
          if (node) {
            node.traverse((o) => ((o as THREE.Mesh).castShadow = true));
            this.protos.set(item.node, node);
          }
        }
      }
    };
    collect(hatsScene);
    collect(accScene);
    collect(sidekickScene);
    collect(headwearScene);

    // load any tuning overrides saved in-browser
    try {
      const raw = localStorage.getItem(TUNE_KEY);
      if (raw) this.tunedFits = JSON.parse(raw);
    } catch {}

    // animations
    if (clips.length) {
      this.mixer = new THREE.AnimationMixer(beanScene);
      const find = (kw: string) => clips.find((c) => c.name.toLowerCase().includes(kw.toLowerCase()));
      const idle = find("idle");
      const run = find("running") ?? find("run");
      const jump = find("jump");
      if (idle) this.actions.idle = this.mixer.clipAction(idle);
      if (run) this.actions.run = this.mixer.clipAction(run);
      if (jump) {
        this.actions.jump = this.mixer.clipAction(jump);
        this.actions.jump.setLoop(THREE.LoopOnce, 1);
        this.actions.jump.clampWhenFinished = true;
      }
      this.actions.idle?.play();
    }
  }

  update(dt: number) {
    if (this.paused) return; // held still (customize mode)
    this.mixer?.update(dt);
  }

  // Freeze/unfreeze the animation. When freezing, settle into a calm idle stance and stop.
  setPaused(p: boolean) {
    this.paused = p;
    if (p && this.mixer) {
      this.actions.run?.stop();
      this.actions.jump?.stop();
      const idle = this.actions.idle;
      if (idle) {
        idle.reset();
        idle.play();
      }
      this.locoState = "idle";
      this.mixer.update(0.3); // pose one calm idle frame, then hold
    }
  }

  setMovement(speed: number, grounded: boolean) {
    if (!this.mixer) return;
    const target: "idle" | "run" | "jump" = !grounded ? "jump" : speed > 0.6 ? "run" : "idle";
    if (this.actions.run) this.actions.run.timeScale = Math.min(2.2, Math.max(0.6, speed / Bean.RUN_REF_SPEED));
    if (target !== this.locoState) {
      const next = this.actions[target];
      const prev = this.actions[this.locoState];
      if (next) {
        if (target === "jump") next.reset();
        next.enabled = true;
        next.fadeIn(0.15);
        next.play();
      }
      prev?.fadeOut(0.15);
      this.locoState = target;
    }
  }

  // --- body / face ---
  setBodyColor(hex: string) {
    const mat = this.body?.material as THREE.MeshStandardMaterial | undefined;
    if (mat) mat.color.set(hex);
  }

  setFace(index: number) {
    const mat = this.face?.material as THREE.MeshStandardMaterial | undefined;
    const tex = this.faceTextures[index];
    if (mat && tex) {
      mat.map = tex;
      mat.needsUpdate = true;
    }
  }

  // --- accessories ---
  private effectiveFit(slot: SlotId, id: string): Fit {
    return { ...(SLOTS[slot].fits[id] ?? {}), ...(this.tunedFits[`${slot}:${id}`] ?? {}) };
  }

  private applyFit(obj: THREE.Object3D, fit: Fit, mirror: boolean) {
    const s = fit.scale ?? 1;
    obj.position.set((mirror ? -1 : 1) * (fit.x ?? 0), fit.y ?? 0, fit.z ?? 0);
    obj.rotation.set(deg(fit.rotX), deg(fit.rotY), deg(fit.rotZ));
    obj.scale.set(mirror ? -s : s, s, s); // mirror right-foot shoe across X
  }

  setAccessory(slot: SlotId, id: string | null) {
    // remove current
    for (const inst of this.equipped[slot] ?? []) inst.parent?.remove(inst);
    this.equipped[slot] = [];
    this.equippedId[slot] = id;
    if (!id) return;

    const item = SLOTS[slot].items.find((it) => it.id === id);
    const proto = item ? this.protos.get(item.node) : undefined;
    if (!proto) {
      console.warn(`accessory "${slot}:${id}" mesh not loaded (node ${item?.node}) — export accessories.glb`);
      return;
    }
    const fit = this.effectiveFit(slot, id);
    const insts: THREE.Object3D[] = [];

    if (SLOTS[slot].attach === "feet") {
      if (this.leftFoot) {
        const l = proto.clone(true);
        this.applyFit(l, fit, false);
        this.leftFoot.add(l);
        insts.push(l);
      }
      if (this.rightFoot) {
        const r = proto.clone(true);
        this.applyFit(r, fit, true);
        this.rightFoot.add(r);
        insts.push(r);
      }
    } else {
      const c = proto.clone(true);
      this.applyFit(c, fit, false);
      this.customizeNode.add(c);
      insts.push(c);
    }
    this.equipped[slot] = insts;
  }

  // --- in-browser tuning ---
  // Merge a partial fit into the equipped item of `slot`, re-apply live, persist, return full fit.
  tune(slot: SlotId, partial: Fit): { id: string | null; fit: Fit } {
    const id = this.equippedId[slot] ?? null;
    if (!id) return { id: null, fit: {} };
    const key = `${slot}:${id}`;
    const merged: Fit = { ...this.effectiveFit(slot, id), ...partial };
    this.tunedFits[key] = merged;
    try {
      localStorage.setItem(TUNE_KEY, JSON.stringify(this.tunedFits));
    } catch {}
    // re-apply to live instances
    const insts = this.equipped[slot] ?? [];
    insts.forEach((inst, i) => this.applyFit(inst, merged, SLOTS[slot].attach === "feet" && i === 1));
    return { id, fit: merged };
  }

  getEquipped(slot: SlotId): { id: string | null; fit: Fit } {
    const id = this.equippedId[slot] ?? null;
    return { id, fit: id ? this.effectiveFit(slot, id) : {} };
  }

  // Dump all tuned fits (for "Copy ALL fits").
  allTunedFits(): Record<string, Fit> {
    return { ...this.tunedFits };
  }
}
