import type { GameMode, MatchResult, ModeStatus, Pose } from "./types.js";
import { GAME_MODES } from "./types.js";
import type { Player } from "./Player.js";
import { Room } from "./Room.js";
import { config } from "./config.js";

export class RoomManager {
  private rooms = new Map<GameMode, Room>();
  // Fired (debounced to the next tick) whenever any room's browsable state changes.
  onStatusChanged: (() => void) | null = null;
  private statusPending = false;

  constructor() {
    for (const m of GAME_MODES) {
      const room = new Room(m);
      room.onStatusChange = () => this.queueStatusBroadcast();
      this.rooms.set(m, room);
    }
  }

  // Coalesce bursts (a match start touches membership several times) into one broadcast.
  private queueStatusBroadcast(): void {
    if (this.statusPending) return;
    this.statusPending = true;
    setTimeout(() => {
      this.statusPending = false;
      this.onStatusChanged?.();
    }, 0);
  }

  status(): ModeStatus[] {
    return GAME_MODES.map((m) => {
      const r = this.get(m);
      return {
        mode: m,
        phase: r.phase,
        count: r.players.length,
        capacity: r.rules.capacity,
        watchers: r.watchers.length,
        watchCap: config.WATCH_CAP,
        watchable: r.watchable,
      };
    });
  }

  get(mode: GameMode): Room {
    return this.rooms.get(mode)!;
  }

  joinQueue(p: Player, mode: GameMode): void {
    this.stopWatching(p); // a watcher clicking JOIN leaves the spectate seat first
    if (p.roomMode === mode) {
      // already queued for this mode; nothing to do
      return;
    }
    if (p.roomMode) this.leave(p); // switch modes
    p.roomMode = mode;
    this.get(mode).add(p);
  }

  watch(p: Player, mode: GameMode): void {
    if (this.isInActiveMatch(p)) {
      p.send({ t: "error", code: "inmatch", message: "cannot watch while in a match" });
      return;
    }
    if (p.roomMode) this.leave(p); // queued player auto-leaves the waitlist
    if (p.watchingMode && p.watchingMode !== mode) this.stopWatching(p);
    this.get(mode).addWatcher(p);
  }

  stopWatching(p: Player): void {
    if (p.watchingMode) this.get(p.watchingMode).removeWatcher(p);
  }

  hexVanish(p: Player, idx: number): void {
    if (p.roomMode) this.get(p.roomMode).reportHexVanish(p, idx);
  }

  leave(p: Player): void {
    if (!p.roomMode) return;
    const room = this.get(p.roomMode);
    room.leave(p);
    p.roomMode = null;
  }

  disconnect(p: Player): void {
    this.stopWatching(p);
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
