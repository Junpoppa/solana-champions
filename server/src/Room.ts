import { randomUUID, randomInt } from "node:crypto";
import type { GameMode, RosterEntry, MatchRosterEntry, RoomPhase, MatchResult, ServerMsg, Pose } from "./types.js";
import type { Player } from "./Player.js";
import { config, clamp, MODE_RULES, type ModeRules } from "./config.js";
import { rank } from "./ranking.js";
import { log } from "./log.js";

// One Room per game mode. Owns the CURRENT forming/active match plus a `pending`
// list of players who arrived while a match was starting/running (promoted to the
// next batch on reset).
export class Room {
  readonly mode: GameMode;
  readonly rules: ModeRules;
  phase: RoomPhase = "filling";
  players: Player[] = []; // active batch
  pending: Player[] = []; // waiting for next batch

  matchId = "";
  seed = 0;
  startAtEpochMs = 0;
  private goAtEpochMs = 0; // server-clock GO instant, set at fireBegin (0 until then)

  private fillDeadline = 0;
  private fillTimer: NodeJS.Timeout | null = null;
  private tickTimer: NodeJS.Timeout | null = null;
  private matchTimer: NodeJS.Timeout | null = null;
  private graceTimer: NodeJS.Timeout | null = null;
  private snapTimer: NodeJS.Timeout | null = null;
  private beginTimer: NodeJS.Timeout | null = null;
  private readyIds = new Set<string>();
  private beginFired = false;

  constructor(mode: GameMode) {
    this.mode = mode;
    this.rules = MODE_RULES[mode];
  }

  // ---- membership ------------------------------------------------------------

  add(p: Player): void {
    if (this.phase === "filling") {
      if (!this.players.includes(p)) this.players.push(p);
      if (this.players.length >= this.rules.capacity) {
        this.startMatch();
        return;
      }
      // The countdown only arms once minToStart players are queued; before that the
      // waitlist just shows "waiting for players".
      if (this.players.length >= this.rules.minToStart && !this.fillTimer) this.armFillTimer();
      this.broadcastQueue();
    } else {
      if (!this.pending.includes(p)) this.pending.push(p);
      this.sendPendingUpdate(p);
    }
  }

  leave(p: Player): void {
    const i = this.players.indexOf(p);
    if (i >= 0) {
      if (this.phase === "filling") {
        this.players.splice(i, 1);
        // An armed countdown keeps running even if the queue drops back under minToStart —
        // the match starts with whoever is left (see onFillExpire). Empty queue = full reset.
        if (this.players.length === 0) this.clearFillTimers();
        else this.broadcastQueue();
      }
      // during starting/running a "leave" is ignored (client already in-match)
      return;
    }
    const j = this.pending.indexOf(p);
    if (j >= 0) this.pending.splice(j, 1);
  }

  disconnect(p: Player): void {
    const i = this.players.indexOf(p);
    if (i >= 0 && (this.phase === "running" || this.phase === "starting")) {
      p.connected = false;
      if (!p.result) {
        p.result = { survivalMs: this.elapsedSinceGo(), finished: false, reason: "disconnect", reportedAt: Date.now() };
      }
      this.maybeEnd();
      return;
    }
    this.leave(p);
  }

  // Match time elapsed since GO (falls back to the matchStart reference before GO fires).
  private elapsedSinceGo(): number {
    return Math.max(0, Date.now() - (this.goAtEpochMs || this.startAtEpochMs));
  }

  // Live pose from a client while in a match — stored, fanned out on the snapshot tick.
  setPose(p: Player, pose: Pose): void {
    if (this.phase !== "running") return;
    if (!this.players.includes(p)) return;
    p.pose = pose;
  }

  // A client's gameplay scene is loaded and frozen, waiting for the synchronized countdown.
  // When everyone (or the begin-timeout) is in, tell all clients to start 3·2·1 together.
  markReady(p: Player): void {
    if (this.phase !== "running" || this.beginFired) return;
    if (!this.players.includes(p)) return;
    this.readyIds.add(p.id);
    if (this.readyIds.size >= this.players.length) {
      this.fireBegin();
      return;
    }
    // Loading progress for the players already in — their how-to card shows "X/Y loaded".
    const msg: ServerMsg = { t: "readyUpdate", mode: this.mode, ready: this.readyIds.size, total: this.players.length };
    for (const q of this.players) if (q.connected) q.send(msg);
  }

  private fireBegin(): void {
    if (this.beginFired) return;
    this.beginFired = true;
    if (this.beginTimer) { clearTimeout(this.beginTimer); this.beginTimer = null; }

    // Drop players who never got ready (tab hidden / stalled load) instead of starting
    // them as frozen ghosts. Connected ones are auto-requeued for the next match.
    // Surviving spawnIndex values are NOT reindexed — clients already placed by them.
    const dropped = this.players.filter((p) => !this.readyIds.has(p.id));
    if (dropped.length > 0) {
      this.players = this.players.filter((p) => this.readyIds.has(p.id));
      for (const p of dropped) {
        p.matchId = null;
        p.result = null;
        p.pose = null;
        if (p.connected) {
          this.pending.push(p);
          p.send({ t: "matchMissed", mode: this.mode, requeued: true });
          this.sendPendingUpdate(p);
        } else {
          p.roomMode = null;
        }
      }
      log(`[${this.mode}] match ${this.matchId} dropped ${dropped.length} not-ready player(s)`);
    }

    // Below 2 ready players a match is pointless (solo = guaranteed win, exploitable once
    // payouts exist) — abort and refill instead.
    if (this.players.length < 2) {
      this.abortMatch();
      return;
    }

    if (dropped.length > 0) {
      const droppedMsg: ServerMsg = { t: "playersDropped", mode: this.mode, ids: dropped.map((p) => p.id) };
      for (const p of this.players) if (p.connected) p.send(droppedMsg);
    }

    this.goAtEpochMs = Date.now() + config.GO_LEAD_MS;
    const msg: ServerMsg = { t: "beginCountdown", goAtEpochMs: this.goAtEpochMs };
    for (const p of this.players) if (p.connected) p.send(msg);
    log(`[${this.mode}] match ${this.matchId} BEGIN (${this.players.length} ready) goAt=${this.goAtEpochMs}`);
  }

  // Not enough ready players at begin time: cancel the match, put the remainder at the
  // front of the queue, and refill.
  private abortMatch(): void {
    log(`[${this.mode}] match ${this.matchId} ABORTED (${this.players.length} ready)`);
    this.clearMatchTimers();
    const remainder = this.players;
    this.players = [];
    this.phase = "filling";
    for (const p of remainder.reverse()) {
      p.matchId = null;
      p.result = null;
      p.pose = null;
      if (p.connected) {
        this.pending.unshift(p);
        p.send({ t: "matchAborted", mode: this.mode });
      } else {
        p.roomMode = null;
      }
    }
    this.promotePending();
  }

  reportResult(p: Player, result: MatchResult): void {
    if (this.phase !== "running") return;
    if (!this.players.includes(p)) return;
    if (p.result) return; // duplicate
    p.result = {
      // Anti-inflation: a report can never claim more time than has actually elapsed.
      survivalMs: clamp(result.survivalMs, 0, this.elapsedSinceGo()),
      finished: !!result.finished,
      reason: result.reason ?? "died",
      reportedAt: Date.now(),
    };
    this.maybeEnd();
  }

  // ---- lifecycle -------------------------------------------------------------

  private startMatch(): void {
    this.phase = "starting";
    this.clearFillTimers();
    this.matchId = randomUUID();
    this.seed = randomInt(1, 2 ** 31 - 1);
    this.startAtEpochMs = Date.now() + config.START_DELAY_MS;

    // assign spawn slots + reset per-match state
    this.players.forEach((p, i) => {
      p.matchId = this.matchId;
      p.result = null;
      p.pose = null;
      p.spawnIndex = i;
    });

    const roster: MatchRosterEntry[] = this.players.map((p) => ({
      id: p.id,
      nick: p.nick,
      look: p.look,
      spawnIndex: p.spawnIndex,
    }));
    const msg: ServerMsg = {
      t: "matchStart",
      mode: this.mode,
      matchId: this.matchId,
      seed: this.seed,
      startAtEpochMs: this.startAtEpochMs,
      roster,
    };
    for (const p of this.players) p.send(msg);

    this.phase = "running";
    // synchronized-countdown handshake: wait for all clients to report `ready`, or a timeout.
    this.readyIds.clear();
    this.beginFired = false;
    this.beginTimer = setTimeout(() => this.fireBegin(), config.BEGIN_TIMEOUT_MS);
    // No gameplay time limit — matches end when one player remains. This is only the
    // zombie-room watchdog (invisible in normal play).
    this.matchTimer = setTimeout(() => this.endMatch(), config.START_DELAY_MS + config.MATCH_WATCHDOG_MS);
    this.snapTimer = setInterval(() => this.broadcastSnapshot(), config.SNAPSHOT_MS);
    log(`[${this.mode}] match ${this.matchId} START; ${this.players.length} players seed=${this.seed}`);
  }

  private maybeEnd(): void {
    const reported = this.players.filter((p) => p.result).length;
    const total = this.players.length;
    if (total === 0) {
      this.endMatch();
      return;
    }
    if (reported >= total) {
      this.endMatch();
      return;
    }
    // One player left standing: they're the winner. The short grace only catches a
    // near-simultaneous death report that's still in flight.
    if (reported >= total - 1 && total > 1 && !this.graceTimer) {
      this.graceTimer = setTimeout(() => {
        const survivor = this.players.find((p) => !p.result);
        this.endMatch(survivor);
      }, config.RESULT_GRACE_MS);
    }
  }

  private endMatch(survivor?: Player): void {
    if (this.phase !== "running") return;
    this.phase = "finished";
    this.clearMatchTimers();

    const now = Date.now();
    if (survivor && !survivor.result) {
      // Last player standing: synthesized survival is strictly above every reported time
      // (reports are clamped to elapsedSinceGo at report time), so ranking puts them 1st.
      survivor.result = {
        survivalMs: this.elapsedSinceGo() + 1,
        finished: false,
        reason: "winner",
        reportedAt: now,
      };
    }
    for (const p of this.players) {
      if (!p.result) {
        p.result = {
          survivalMs: this.elapsedSinceGo(),
          finished: false,
          reason: "timeout",
          reportedAt: now,
        };
      }
    }

    const ranked = rank(this.players);
    const winnerRow = ranked[0];
    const winnerPlayer = winnerRow ? this.players.find((p) => p.id === winnerRow.id) ?? null : null;
    const winner = winnerPlayer
      ? { id: winnerPlayer.id, nick: winnerPlayer.nick, solAddress: winnerPlayer.solAddress }
      : null;

    const msg: ServerMsg = { t: "standings", mode: this.mode, matchId: this.matchId, ranked, winner };
    for (const p of this.players) p.send(msg);
    log(
      `[${this.mode}] match ${this.matchId} DONE; winner=${winner?.nick ?? "-"}` +
        `${winner?.solAddress ? " addr=" + winner.solAddress : ""} (${ranked.length} players)`,
    );

    this.reset();
  }

  private reset(): void {
    this.clearFillTimers();
    this.clearMatchTimers();
    for (const p of this.players) {
      p.roomMode = null;
      p.matchId = null;
      p.result = null;
    }
    this.players = [];
    this.phase = "filling";
    this.goAtEpochMs = 0;
    this.promotePending();
  }

  // Promote pending players into the (empty) forming batch. Shared by reset() and abortMatch().
  private promotePending(): void {
    if (!this.pending.length) return;
    this.players = this.pending.splice(0, this.rules.capacity);
    for (const p of this.pending) this.sendPendingUpdate(p);
    if (this.players.length >= this.rules.capacity) {
      this.startMatch();
      return;
    }
    if (this.players.length >= this.rules.minToStart) this.armFillTimer();
    this.broadcastQueue();
  }

  // ---- timers ----------------------------------------------------------------

  private armFillTimer(): void {
    this.clearFillTimers();
    this.fillDeadline = Date.now() + this.rules.fillCountdownMs;
    this.fillTimer = setTimeout(() => this.onFillExpire(), this.rules.fillCountdownMs);
    this.tickTimer = setInterval(() => this.broadcastQueue(), 1000);
    log(`[${this.mode}] countdown armed (${this.players.length} queued, ${this.rules.fillCountdownMs / 1000}s)`);
  }

  private onFillExpire(): void {
    // Start with whoever is still queued. Below 2 we cancel instead — a solo match is a
    // guaranteed win (exploitable once payouts exist) — and wait for the minToStart trigger again.
    if (this.players.length >= 2) this.startMatch();
    else {
      this.clearFillTimers();
      log(`[${this.mode}] countdown expired with ${this.players.length} queued — cancelled, waiting for players`);
      this.broadcastQueue();
    }
  }

  private clearFillTimers(): void {
    this.fillDeadline = 0;
    if (this.fillTimer) {
      clearTimeout(this.fillTimer);
      this.fillTimer = null;
    }
    if (this.tickTimer) {
      clearInterval(this.tickTimer);
      this.tickTimer = null;
    }
  }

  private clearMatchTimers(): void {
    if (this.matchTimer) {
      clearTimeout(this.matchTimer);
      this.matchTimer = null;
    }
    if (this.graceTimer) {
      clearTimeout(this.graceTimer);
      this.graceTimer = null;
    }
    if (this.snapTimer) {
      clearInterval(this.snapTimer);
      this.snapTimer = null;
    }
    if (this.beginTimer) {
      clearTimeout(this.beginTimer);
      this.beginTimer = null;
    }
  }

  // Fan out every player's latest pose to everyone in the match (~15 Hz). Each client
  // ignores its own id and interpolates the rest.
  private broadcastSnapshot(): void {
    const poses: { id: string; q: Pose }[] = [];
    for (const p of this.players) if (p.pose) poses.push({ id: p.id, q: p.pose });
    if (poses.length === 0) return;
    const msg: ServerMsg = { t: "snapshot", players: poses };
    for (const p of this.players) if (p.connected) p.send(msg);
  }

  // ---- broadcasts ------------------------------------------------------------

  private roster(): RosterEntry[] {
    return this.players.map((p) => ({ id: p.id, nick: p.nick }));
  }

  private broadcastQueue(): void {
    // msRemaining: >0 counting down, 0 = no countdown armed yet (waiting for minToStart),
    // -1 (sendPendingUpdate) = a match is running. A live countdown hitting 0 starts the
    // match immediately, so 0 is unambiguous as the "waiting" sentinel.
    const msRemaining = this.fillDeadline ? Math.max(1, this.fillDeadline - Date.now()) : 0;
    const msg: ServerMsg = {
      t: "queueUpdate",
      mode: this.mode,
      count: this.players.length,
      capacity: this.rules.capacity,
      minToStart: this.rules.minToStart,
      msRemaining,
      roster: this.roster(),
    };
    for (const p of this.players) p.send(msg);
  }

  // A pending player (arrived mid-match) sees msRemaining = -1 => "waiting for
  // the current match to finish".
  private sendPendingUpdate(p: Player): void {
    p.send({
      t: "queueUpdate",
      mode: this.mode,
      count: this.pending.length,
      capacity: this.rules.capacity,
      minToStart: this.rules.minToStart,
      msRemaining: -1,
      roster: this.pending.map((x) => ({ id: x.id, nick: x.nick })),
    });
  }
}
