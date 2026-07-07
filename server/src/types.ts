// Shared wire protocol types. The web client keeps a copy in web/src/netTypes.ts.

export type GameMode = "spinner" | "lastman" | "rollout";
export const GAME_MODES: GameMode[] = ["spinner", "lastman", "rollout"];

export interface RosterEntry {
  id: string;
  nick: string;
}

// Roster entry sent at match start — carries each player's look + spawn slot so
// clients can render remote avatars and place the local player.
export interface MatchRosterEntry {
  id: string;
  nick: string;
  look: string | null; // BeanLook+faceTex JSON (opaque to the server)
  spawnIndex: number;
}

// Compact live pose streamed ~15 Hz: position, yaw, planar speed, airborne flag, downed flag,
// second-jump flag, and the fling velocity (only meaningful while downed — lets remotes replay the
// same physics knockdown). All opaque numbers to the server (validated finite, otherwise passed through).
export interface Pose {
  x: number;
  y: number;
  z: number;
  r: number; // yaw degrees
  s: number; // planar speed
  a: number; // airborne 0/1
  d: number; // downed/ragdolled 0/1
  j: number; // inSecondJump 0/1 (double-jump front-roll)
  fx: number; // fling velocity x (world)
  fy: number; // fling velocity y
  fz: number; // fling velocity z
  cy: number; // camera yaw (CameraManager.lookAngle) — spectator player-view replication
  cp: number; // camera pitch (CameraManager.tiltAngle)
}

export type ResultReason = "died" | "finished" | "timeout" | "disconnect" | "winner";

export interface MatchResult {
  survivalMs: number;
  finished: boolean;
  reason?: ResultReason;
  reportedAt?: number; // server-stamped
}

export type RoomPhase = "filling" | "starting" | "running" | "finished";

// ---- Client -> Server ----
export type ClientMsg =
  | { t: "identify"; nick: string; solAddress: string | null; look?: string | null }
  | { t: "updateLook"; look: string | null } // outfit changed after identify (customizer save)
  | { t: "joinQueue"; mode: GameMode }
  | { t: "leaveQueue" }
  | { t: "reportResult"; mode: GameMode; survivalMs: number; finished: boolean; reason?: string }
  | { t: "state"; q: Pose } // live pose while in a match
  | { t: "ready" } // gameplay scene loaded; waiting for the synchronized countdown start
  | { t: "chat"; text: string } // lobby text chat (broadcast to other lobby players)
  | { t: "timeSync"; t0: number } // clock-sync probe; t0 = client Date.now() at send
  | { t: "watchMatch"; mode: GameMode } // spectate the running match in this mode
  | { t: "stopWatching" } // leave spectating (back to lobby)
  | { t: "hexVanish"; idx: number }; // my LOCAL bean stepped LMS hex tile idx (spectator hex sync)

// ---- Server -> Client ----
export interface StandingRow {
  rank: number;
  id: string;
  nick: string;
  survivalMs: number;
  finished: boolean;
}

export interface WinnerInfo {
  id: string;
  nick: string;
  solAddress: string | null;
}

// Per-mode live status for the lobby server-browser cards.
export interface ModeStatus {
  mode: GameMode;
  phase: RoomPhase;
  count: number; // queued while filling, match roster size while starting/running
  capacity: number;
  watchers: number;
  watchCap: number;
  watchable: boolean; // running, past GO, and a watcher slot is free
}

export type ServerMsg =
  | { t: "identified"; id: string; nick: string }
  | { t: "queueUpdate"; mode: GameMode; count: number; capacity: number; minToStart: number; msRemaining: number; roster: RosterEntry[] }
  | { t: "matchStart"; mode: GameMode; matchId: string; seed: number; startAtEpochMs: number; roster: MatchRosterEntry[] }
  | { t: "beginCountdown"; goAtEpochMs: number } // GO fires at this SERVER-clock instant on every client
  | { t: "timeSyncPong"; t0: number; serverNow: number } // reply to timeSync (echoes t0 for RTT)
  | { t: "readyUpdate"; mode: GameMode; ready: number; total: number } // loading progress while waiting for all players
  | { t: "playersDropped"; mode: GameMode; ids: string[] } // these players missed the start; despawn their avatars
  | { t: "matchMissed"; mode: GameMode; requeued: boolean } // you missed the start; you're re-queued for the next match
  | { t: "matchAborted"; mode: GameMode } // <2 players were ready — match cancelled, back to the queue
  | { t: "chatMsg"; id: string; nick: string; text: string; ts: number } // a lobby chat line (server-stamped nick/id)
  | { t: "snapshot"; players: { id: string; q: Pose }[] } // ~15 Hz live poses of all players in the match
  | { t: "standings"; mode: GameMode; matchId: string; ranked: StandingRow[]; winner: WinnerInfo | null }
  | { t: "lobbyStatus"; modes: ModeStatus[] } // live server-browser state for the lobby cards
  | { t: "watchStart"; mode: GameMode; matchId: string; seed: number; startAtEpochMs: number; goAtEpochMs: number; roster: MatchRosterEntry[]; vanishedHexes: number[] } // you're now spectating
  | { t: "watchEnd"; mode: GameMode; reason: "finished" | "aborted" } // spectated match over — back to lobby
  | { t: "hexVanish"; idxs: number[] } // relay to watchers: these LMS tiles vanished
  | { t: "error"; code: string; message: string };
