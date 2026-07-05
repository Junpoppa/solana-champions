# Bean Obstacle Course (web)

Web-native Fall-Guys-style obstacle course. Three.js + Rapier + Vite + TypeScript.

## Run
```
cd web
npm install
npm run dev
```
Opens http://localhost:5173. WASD/arrows to run, Space to jump, R to respawn.

## Status
v1 scaffold: placeholder capsule "bean", one platform-jumping course, timer, fall-respawn.

## Next
- Export FREE party bean from Unity → `public/models/bean.glb` (with animations), load in `player.ts`.
- Add hazards (rolling balls, spinning disks).
- Solana wallet + on-chain scores (deferred).

See `../files/plan.md` for the full plan.
