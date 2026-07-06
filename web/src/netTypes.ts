// Wire protocol — kept in sync with server/src/types.ts (copied, not imported, so the
// web build has no dependency on the server package).

export type GameMode = "spinner" | "lastman" | "rollout";

export interface RosterEntry {
  id: string;
  nick: string;
}

export interface MatchRosterEntry {
  id: string;
  nick: string;
  look: string | null;
  spawnIndex: number;
}

export interface Pose {
  x: number; y: number; z: number; r: number; s: number; a: number; d: number;
  j: number; fx: number; fy: number; fz: number; // second-jump flag + fling velocity (for remote ragdoll)
}

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

// ---- Client -> Server ----
export type ClientMsg =
  | { t: "identify"; nick: string; solAddress: string | null; look?: string | null }
  | { t: "updateLook"; look: string | null }
  | { t: "joinQueue"; mode: GameMode }
  | { t: "leaveQueue" }
  | { t: "reportResult"; mode: GameMode; survivalMs: number; finished: boolean; reason?: string }
  | { t: "state"; q: Pose }
  | { t: "ready" }
  | { t: "chat"; text: string }
  | { t: "timeSync"; t0: number }; // clock-sync probe; t0 = client Date.now() at send

// ---- Server -> Client ----
export interface IdentifiedMsg { t: "identified"; id: string; nick: string }
export interface QueueUpdateMsg { t: "queueUpdate"; mode: GameMode; count: number; capacity: number; minToStart: number; msRemaining: number; roster: RosterEntry[] }
export interface MatchStartMsg { t: "matchStart"; mode: GameMode; matchId: string; seed: number; startAtEpochMs: number; roster: MatchRosterEntry[] }
export interface SnapshotMsg { t: "snapshot"; players: { id: string; q: Pose }[] }
export interface BeginCountdownMsg { t: "beginCountdown"; goAtEpochMs: number } // GO fires at this SERVER-clock instant
export interface TimeSyncPongMsg { t: "timeSyncPong"; t0: number; serverNow: number }
export interface ReadyUpdateMsg { t: "readyUpdate"; mode: GameMode; ready: number; total: number } // loading progress while waiting for all players
export interface PlayersDroppedMsg { t: "playersDropped"; mode: GameMode; ids: string[] } // missed the start; despawn their avatars
export interface MatchMissedMsg { t: "matchMissed"; mode: GameMode; requeued: boolean } // we missed the start; re-queued for next match
export interface MatchAbortedMsg { t: "matchAborted"; mode: GameMode } // <2 ready players — match cancelled
export interface ChatMsg { t: "chatMsg"; id: string; nick: string; text: string; ts: number }
export interface StandingsMsg { t: "standings"; mode: GameMode; matchId: string; ranked: StandingRow[]; winner: WinnerInfo | null }
export interface ErrorMsg { t: "error"; code: string; message: string }

export type ServerMsg =
  | IdentifiedMsg | QueueUpdateMsg | MatchStartMsg | SnapshotMsg | BeginCountdownMsg
  | TimeSyncPongMsg | ReadyUpdateMsg | PlayersDroppedMsg | MatchMissedMsg | MatchAbortedMsg
  | ChatMsg | StandingsMsg | ErrorMsg;
