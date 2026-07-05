import { randomInt } from "node:crypto";
import type { ClientMsg, GameMode } from "./types.js";
import { GAME_MODES } from "./types.js";

export function safeParse(raw: string): ClientMsg | null {
  let obj: unknown;
  try {
    obj = JSON.parse(raw);
  } catch {
    return null;
  }
  if (!obj || typeof obj !== "object" || typeof (obj as { t?: unknown }).t !== "string") {
    return null;
  }
  return obj as ClientMsg;
}

// Strip ASCII control chars (0x00-0x1F and 0x7F). Built via RegExp(string) so the
// source file stays plain-ASCII (no literal control bytes).
const CONTROL_CHARS = new RegExp("[\\u0000-\\u001f\\u007f]", "g");

export function sanitizeNick(raw: unknown): string {
  let s = typeof raw === "string" ? raw : "";
  s = s.replace(CONTROL_CHARS, "").trim();
  if (s.length > 16) s = s.slice(0, 16);
  return s;
}

export function randomGuestNick(): string {
  return "Bean" + String(randomInt(1000, 10000));
}

// Lobby chat text: strip control chars, collapse to a single line, trim, cap length. Empty → "".
export function sanitizeChatText(raw: unknown): string {
  let s = typeof raw === "string" ? raw : "";
  s = s.replace(CONTROL_CHARS, "").trim();
  if (s.length > 240) s = s.slice(0, 240);
  return s;
}

// Base58 shape check only (not on-chain existence). Solana pubkeys are 32-44 base58 chars.
const BASE58_ADDR = /^[1-9A-HJ-NP-Za-km-z]{32,44}$/;
export function sanitizeAddress(raw: unknown): string | null {
  if (typeof raw !== "string") return null;
  const s = raw.trim();
  return BASE58_ADDR.test(s) ? s : null;
}

export function isGameMode(m: unknown): m is GameMode {
  return typeof m === "string" && (GAME_MODES as string[]).includes(m);
}
