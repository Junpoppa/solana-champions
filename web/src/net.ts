// WebSocket client for the multiplayer lobby/room server (server/). Singleton, mirrors
// the style of unityGame / musicController. Auto-reconnects and re-identifies + re-joins
// the current queue on reconnect so a transient drop in the lobby is invisible.

import type {
  ClientMsg,
  ServerMsg,
  GameMode,
  QueueUpdateMsg,
  MatchStartMsg,
  StandingsMsg,
  ChatMsg,
  ErrorMsg,
  PlayersDroppedMsg,
  MatchMissedMsg,
  MatchAbortedMsg,
} from "./netTypes";

// WS address resolution:
//  - explicit VITE_WS_URL wins.
//  - vite dev (page on :5173): the server is a separate process on :8787.
//  - otherwise (combined server / tunnel / prod): SAME ORIGIN — one URL covers web + ws.
const WS_URL: string =
  (import.meta as any).env?.VITE_WS_URL ||
  (location.port === "5173"
    ? `ws://${location.hostname || "localhost"}:8787`
    : `${location.protocol === "https:" ? "wss" : "ws"}://${location.host}`);

interface Handlers {
  onIdentified?: (id: string, nick: string) => void;
  onQueueUpdate?: (m: QueueUpdateMsg) => void;
  onMatchStart?: (m: MatchStartMsg) => void;
  onSnapshot?: (raw: string) => void; // high-freq avatar poses; raw JSON forwarded to Unity
  onBeginCountdown?: (goAtEpochMs: number) => void; // GO fires at this server-clock instant, same for all players
  onPlayersDropped?: (m: PlayersDroppedMsg) => void; // players who missed the start — despawn their avatars
  onMatchMissed?: (m: MatchMissedMsg) => void; // we missed the start; server re-queued us
  onMatchAborted?: (m: MatchAbortedMsg) => void; // match cancelled (<2 ready) — back to the queue
  onChatMsg?: (m: ChatMsg) => void; // a lobby chat line from another player
  onStandings?: (m: StandingsMsg) => void;
  onError?: (m: ErrorMsg) => void;
  onConnChange?: (connected: boolean) => void;
}

let ws: WebSocket | null = null;
let connected = false;
let wantOpen = false; // whether we should keep (re)connecting
let backoff = 500;
let reconnectTimer: ReturnType<typeof setTimeout> | null = null;

// cached identity + intent, replayed on (re)connect
let profile: { nick: string; solAddress: string | null; look: string | null } | null = null;
let queuedMode: GameMode | null = null;

// ---- clock sync (offset = serverClock - clientClock) ----------------------------
// Measured NTP-style: 5 probes, keep the lowest-RTT sample (most symmetric path).
// Used to convert the server's goAtEpochMs into local time for the synced countdown.
let clockOffset = 0;
let clockSynced = false;
let bestSyncRtt = Infinity;
let syncTimer: ReturnType<typeof setTimeout> | null = null;

function runTimeSync() {
  if (syncTimer) return; // a burst is already in flight
  bestSyncRtt = Infinity;
  let sent = 0;
  const probe = () => {
    syncTimer = null;
    if (!send({ t: "timeSync", t0: Date.now() })) return;
    if (++sent < 5) syncTimer = setTimeout(probe, 150);
  };
  probe();
}

function onTimeSyncPong(t0: number, serverNow: number) {
  const rtt = Date.now() - t0;
  if (rtt < 0 || rtt > 1000) return; // discard outliers
  if (rtt < bestSyncRtt) {
    bestSyncRtt = rtt;
    clockOffset = serverNow - (t0 + rtt / 2);
    clockSynced = true;
  }
}

const handlers: Handlers = {};

function send(msg: ClientMsg): boolean {
  if (ws && ws.readyState === WebSocket.OPEN) {
    ws.send(JSON.stringify(msg));
    return true;
  }
  return false;
}

function scheduleReconnect() {
  if (!wantOpen || reconnectTimer) return;
  reconnectTimer = setTimeout(() => {
    reconnectTimer = null;
    open();
  }, backoff);
  backoff = Math.min(backoff * 2, 5000);
}

function open() {
  if (ws && (ws.readyState === WebSocket.OPEN || ws.readyState === WebSocket.CONNECTING)) return;
  try {
    ws = new WebSocket(WS_URL);
  } catch (e) {
    console.warn("net: WebSocket ctor failed", e);
    scheduleReconnect();
    return;
  }

  ws.onopen = () => {
    connected = true;
    backoff = 500;
    handlers.onConnChange?.(true);
    // replay identity + queue intent
    if (profile) send({ t: "identify", nick: profile.nick, solAddress: profile.solAddress, look: profile.look });
    if (queuedMode) send({ t: "joinQueue", mode: queuedMode });
    runTimeSync();
  };

  ws.onclose = () => {
    connected = false;
    handlers.onConnChange?.(false);
    scheduleReconnect();
  };

  ws.onerror = () => {
    // onclose will follow and drive reconnect
  };

  ws.onmessage = (ev) => {
    // Fast path for high-freq snapshots: forward the raw JSON straight to Unity without re-stringifying.
    const raw = ev.data as string;
    if (raw.startsWith('{"t":"snapshot"')) {
      handlers.onSnapshot?.(raw);
      return;
    }
    let msg: ServerMsg;
    try {
      msg = JSON.parse(raw);
    } catch {
      return;
    }
    dispatch(msg);
  };
}

function dispatch(msg: ServerMsg) {
  switch (msg.t) {
    case "identified":
      handlers.onIdentified?.(msg.id, msg.nick);
      break;
    case "queueUpdate":
      handlers.onQueueUpdate?.(msg);
      break;
    case "matchStart":
      handlers.onMatchStart?.(msg);
      break;
    case "beginCountdown":
      console.log("[net] received beginCountdown from server, goAt", msg.goAtEpochMs, "offset", Math.round(clockOffset));
      handlers.onBeginCountdown?.(msg.goAtEpochMs);
      break;
    case "timeSyncPong":
      onTimeSyncPong(msg.t0, msg.serverNow);
      break;
    case "playersDropped":
      handlers.onPlayersDropped?.(msg);
      break;
    case "matchMissed":
      handlers.onMatchMissed?.(msg);
      break;
    case "matchAborted":
      handlers.onMatchAborted?.(msg);
      break;
    case "chatMsg":
      handlers.onChatMsg?.(msg);
      break;
    case "standings":
      handlers.onStandings?.(msg);
      break;
    case "error":
      console.warn("net error:", msg.code, msg.message);
      handlers.onError?.(msg);
      break;
  }
}

export const net = {
  setHandlers(h: Handlers) {
    Object.assign(handlers, h);
  },

  connect() {
    wantOpen = true;
    open();
  },

  isConnected() {
    return connected;
  },

  identify(nick: string, solAddress: string | null, look: string | null = null) {
    profile = { nick, solAddress, look };
    send({ t: "identify", nick, solAddress, look });
  },

  // Push a new outfit to the server (after the player customizes + saves in the lobby) so the
  // NEXT match's roster ships the current look, not the stale identify-time one. Also updates the
  // cached profile so a reconnect replays the current look.
  updateLook(look: string | null) {
    if (profile) profile.look = look;
    send({ t: "updateLook", look });
  },

  // High-freq local pose → server. `poseJson` is a raw compact JSON object (from Unity),
  // wrapped without re-parsing to keep GC low.
  sendState(poseJson: string) {
    if (ws && ws.readyState === WebSocket.OPEN) ws.send(`{"t":"state","q":${poseJson}}`);
  },

  // Gameplay scene loaded + frozen → tell the server we're ready for the synchronized countdown.
  sendReady() {
    send({ t: "ready" });
  },

  // Lobby chat: send a text line (server stamps nick/id and broadcasts to other lobby players).
  sendChat(text: string) {
    send({ t: "chat", text });
  },

  joinQueue(mode: GameMode) {
    queuedMode = mode;
    send({ t: "joinQueue", mode });
    runTimeSync(); // refresh the clock offset before the upcoming match
  },

  // Convert a server-clock epoch ms into local-clock epoch ms (identity if sync failed —
  // then the countdown is merely un-offset, never frozen).
  serverEpochToLocal(serverMs: number): number {
    return serverMs - clockOffset;
  },

  isClockSynced(): boolean {
    return clockSynced;
  },

  leaveQueue() {
    queuedMode = null;
    send({ t: "leaveQueue" });
  },

  reportResult(mode: GameMode, survivalMs: number, finished: boolean) {
    // match is over for this client either way; clear queue intent so a reconnect
    // during the standings wait doesn't silently re-queue us.
    queuedMode = null;
    send({ t: "reportResult", mode, survivalMs, finished });
  },
};
