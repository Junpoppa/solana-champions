import type { StandingRow } from "./types.js";
import type { Player } from "./Player.js";

// All v1 modes are survival-based. Sort: finished first (future-proof; no finish
// lines yet), then longest survivalMs, then earliest report, then id for determinism.
export function rank(players: Player[]): StandingRow[] {
  const rows = players
    .filter((p) => p.result)
    .map((p) => ({ p, r: p.result! }));

  rows.sort((a, b) => {
    if (a.r.finished !== b.r.finished) return a.r.finished ? -1 : 1;
    if (b.r.survivalMs !== a.r.survivalMs) return b.r.survivalMs - a.r.survivalMs;
    const ta = a.r.reportedAt ?? Number.POSITIVE_INFINITY;
    const tb = b.r.reportedAt ?? Number.POSITIVE_INFINITY;
    if (ta !== tb) return ta - tb;
    return a.p.id < b.p.id ? -1 : 1;
  });

  return rows.map((x, i) => ({
    rank: i + 1,
    id: x.p.id,
    nick: x.p.nick,
    survivalMs: x.r.survivalMs,
    finished: x.r.finished,
  }));
}
