# Sol Champions — multiplayer lobby server

Authoritative WebSocket server for the **lobby only** (identity, per-mode waitlist rooms,
synchronized match start, result collection, winner/standings). It never sees gameplay — each
player runs their own local Unity WebGL instance and reports one result per match.

## Run (local dev)

```bash
cd server
npm install
npm run dev          # tsx watch, reads .env if present
```

For **solo testing** (start a match with a single tab), create `server/.env`:

```
MIN_TO_START=1
FILL_COUNTDOWN_MS=6000
```

Then run the web app (`cd web && npm run dev`) and open one or more tabs at http://localhost:5173.
The web client connects to `ws://<hostname>:8787` by default (override with `VITE_WS_URL`).

## Run (production)

```bash
npm run build && npm start
```

Set env: `PORT` (host-provided), `ALLOWED_ORIGIN=https://your-web-origin` (rejects other origins),
and any tuning (`CAPACITY`, `FILL_COUNTDOWN_MS`, …) — see `.env.example`. Expose over `wss://`
(the host terminates TLS). Any long-lived-WebSocket host works (Railway / Fly.io / Render).

## Protocol

Envelope `{ "t": <type>, ... }`. See `src/types.ts` (the web client mirrors it in
`web/src/netTypes.ts`).

- **client→server:** `identify` · `joinQueue` · `leaveQueue` · `reportResult`
- **server→client:** `identified` · `queueUpdate` · `matchStart` · `standings` · `error`

Winner's Solana address is exposed only in `standings.winner` (never in rosters). Results are
client-reported and clamped `[0, MATCH_MAX_MS]` — trust-based, acceptable for v1 since there is no
on-chain transfer yet. Add server-side validation before attaching real payouts.
