import { WebSocket } from "ws";
import type { GameMode, MatchResult, ServerMsg, Pose } from "./types.js";

export class Player {
  readonly id: string;
  readonly ws: WebSocket;
  nick = "";
  solAddress: string | null = null;
  look: string | null = null; // BeanLook+faceTex JSON, relayed to others for avatar rendering
  identified = false;
  connected = true;
  isAlive = true; // heartbeat flag
  lastChatMs = 0; // lobby-chat rate-limit timestamp

  // per-room / per-match state
  roomMode: GameMode | null = null;
  matchId: string | null = null;
  result: MatchResult | null = null;
  spawnIndex = 0; // assigned at match start
  pose: Pose | null = null; // latest live pose (for avatar snapshots)

  constructor(id: string, ws: WebSocket) {
    this.id = id;
    this.ws = ws;
  }

  send(msg: ServerMsg): void {
    if (this.ws.readyState === WebSocket.OPEN) {
      try {
        this.ws.send(JSON.stringify(msg));
      } catch {
        /* socket went away mid-send; ignore */
      }
    }
  }
}
