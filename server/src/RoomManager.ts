import type { GameMode, MatchResult, Pose } from "./types.js";
import { GAME_MODES } from "./types.js";
import type { Player } from "./Player.js";
import { Room } from "./Room.js";

export class RoomManager {
  private rooms = new Map<GameMode, Room>();

  constructor() {
    for (const m of GAME_MODES) this.rooms.set(m, new Room(m));
  }

  get(mode: GameMode): Room {
    return this.rooms.get(mode)!;
  }

  joinQueue(p: Player, mode: GameMode): void {
    if (p.roomMode === mode) {
      // already queued for this mode; nothing to do
      return;
    }
    if (p.roomMode) this.leave(p); // switch modes
    p.roomMode = mode;
    this.get(mode).add(p);
  }

  leave(p: Player): void {
    if (!p.roomMode) return;
    const room = this.get(p.roomMode);
    room.leave(p);
    p.roomMode = null;
  }

  disconnect(p: Player): void {
    if (!p.roomMode) return;
    // Room.disconnect may keep the player in an active match (synth result); it
    // does not clear roomMode itself, so we clear our view here.
    this.get(p.roomMode).disconnect(p);
    p.roomMode = null;
  }

  reportResult(p: Player, mode: GameMode, result: MatchResult): void {
    this.get(mode).reportResult(p, result);
  }

  setPose(p: Player, pose: Pose): void {
    if (p.roomMode) this.get(p.roomMode).setPose(p, pose);
  }

  markReady(p: Player): void {
    if (p.roomMode) this.get(p.roomMode).markReady(p);
  }

  // True only while the player is actually INSIDE a starting/running match. Queued and
  // pending players are not "in a match" — they still get lobby chat, for example.
  isInActiveMatch(p: Player): boolean {
    if (!p.roomMode) return false;
    const room = this.get(p.roomMode);
    return (room.phase === "starting" || room.phase === "running") && room.players.includes(p);
  }
}
