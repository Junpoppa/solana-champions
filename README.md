# Solana Champions

Fall-Guys-style multiplayer obstacle-course browser game. Three live modes (Spinner, Last Man Standing, Roll Out), online lobbies (LMS up to 20 players, others 15), bean customization, and Solana-address collection for winner payouts (on-chain transfer not wired yet).

## Layout

| Folder | What it is |
|---|---|
| `web/` | JS front-end (Vite + TypeScript): start menu, lobby, customizer, waitlist/standings UI. Embeds the Unity WebGL build from `web/public/unity/`. |
| `server/` | Node lobby/match server (WebSocket) — **also serves the built website**, so ONE process/port covers everything. |
| `unity_game/` | Unity 6 (URP) project — the actual gameplay. **Only needed to modify the game.** The playable WebGL build is committed in `web/public/unity/`, so hosting requires no Unity at all. |
| `files/` | Project docs/plan. |

## Run locally (Windows, for development)

Double-click `start-multiplayer.bat` — starts the server on `:8787` plus a temporary public cloudflared tunnel and prints the link. Requires the web app to have been built once (see below).

## Host it online (the real deployment runbook)

Everything needed ships in this repo. On any box with Node 20+:

```bash
# 1. build the website (the Unity build is already in web/public/unity)
cd web && npm ci && npm run build

# 2. build + start the server (serves web/dist + websocket on one port)
cd ../server && npm ci && npm run build
PORT=8787 ALLOWED_ORIGIN=https://your-domain.example npm start
```

- `PORT` — listen port (default 8787).
- `ALLOWED_ORIGIN` — set to your site's public origin in production (empty = allow any, dev only).
- Point your host / reverse-proxy / load balancer at that one port (HTTP + WebSocket upgrade on the same port). TLS terminates at the proxy → the site is `https://` and the socket `wss://` automatically.

### Or with Docker

```bash
docker build -t solana-champions .
docker run -p 8787:8787 -e ALLOWED_ORIGIN=https://your-domain.example solana-champions
```

Works as-is on Railway / Render / Fly.io / any VPS.

### Lobby rules (production defaults)

- LMS capacity 20; Spinner / Roll Out capacity 15.
- Waitlist countdown (60s) arms once 6 players queue; at expiry the match starts with whoever is left (min 2). A full lobby starts instantly.
- Tunable via env: `MIN_TO_START`, `FILL_COUNTDOWN_MS`, `LMS_CAPACITY`, `SPINNER_CAPACITY`, `ROLLOUT_CAPACITY` (see `server/src/config.ts`).

## Changing the game itself

Open `unity_game/` in Unity 6 → edit → build WebGL to `web/public/unity` (scenes: Boot, Course, LastManStanding, RollOut — Boot must be index 0) → `cd web && npm run build` → restart the server.
