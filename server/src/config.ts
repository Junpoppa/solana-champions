function num(name: string, def: number): number {
  const v = process.env[name];
  const n = v !== undefined ? Number(v) : NaN;
  return Number.isFinite(n) ? n : def;
}

export const config = {
  PORT: num("PORT", 8787),
  START_DELAY_MS: num("START_DELAY_MS", 3_500),
  RESULT_GRACE_MS: num("RESULT_GRACE_MS", 8_000),
  MATCH_MAX_MS: num("MATCH_MAX_MS", 180_000),
  SNAPSHOT_MS: num("SNAPSHOT_MS", 66), // ~15 Hz avatar pose fan-out
  BEGIN_TIMEOUT_MS: num("BEGIN_TIMEOUT_MS", 12_000), // max wait for all clients to load before starting the countdown
  ALLOWED_ORIGIN: process.env.ALLOWED_ORIGIN || "", // empty = allow any (dev)
};

// Per-mode queue rules (production defaults). The waitlist countdown only ARMS once
// `minToStart` players are queued; at expiry the match starts with whoever is left (≥2).
// A full lobby always starts instantly. Env overrides kept for load-testing
// (MIN_TO_START / FILL_COUNTDOWN_MS apply to all modes; *_CAPACITY per mode).
export interface ModeRules {
  capacity: number;
  minToStart: number;
  fillCountdownMs: number;
}
const MIN_TO_START = num("MIN_TO_START", 6);
const FILL_MS = num("FILL_COUNTDOWN_MS", 60_000);
export const MODE_RULES: Record<string, ModeRules> = {
  lastman: { capacity: num("LMS_CAPACITY", 20), minToStart: MIN_TO_START, fillCountdownMs: FILL_MS },
  spinner: { capacity: num("SPINNER_CAPACITY", 15), minToStart: MIN_TO_START, fillCountdownMs: FILL_MS },
  rollout: { capacity: num("ROLLOUT_CAPACITY", 15), minToStart: MIN_TO_START, fillCountdownMs: FILL_MS },
};

export function clamp(v: number, lo: number, hi: number): number {
  if (!Number.isFinite(v)) return lo;
  return Math.min(hi, Math.max(lo, v));
}
