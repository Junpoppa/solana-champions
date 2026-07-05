// Player identity, persisted locally. Nickname is shown in the lobby/roster/standings;
// solAddress is where a payout would go if the player wins (no on-chain transfer yet).

export interface PlayerProfile {
  nick: string;
  solAddress: string | null;
}

const STORAGE_KEY = "playerProfile.v1";
export const NICK_MAX = 16;

// Base58 shape check only (matches the server). Solana pubkeys are 32-44 base58 chars.
const BASE58_ADDR = /^[1-9A-HJ-NP-Za-km-z]{32,44}$/;
export function isValidSolAddress(s: string): boolean {
  return BASE58_ADDR.test(s.trim());
}

export function sanitizeNick(raw: string): string {
  // strip ASCII control chars, collapse to <= NICK_MAX
  const cleaned = raw.replace(new RegExp("[\\u0000-\\u001f\\u007f]", "g"), "").trim();
  return cleaned.slice(0, NICK_MAX);
}

export function randomGuestNick(): string {
  return "Bean" + Math.floor(1000 + Math.random() * 9000);
}

export function loadProfile(): PlayerProfile | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return null;
    const p = JSON.parse(raw) as Partial<PlayerProfile>;
    const nick = sanitizeNick(typeof p.nick === "string" ? p.nick : "");
    if (!nick) return null;
    const addr = typeof p.solAddress === "string" && isValidSolAddress(p.solAddress) ? p.solAddress.trim() : null;
    return { nick, solAddress: addr };
  } catch {
    return null;
  }
}

export function saveProfile(p: PlayerProfile): void {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(p));
  } catch (e) {
    console.warn("saveProfile failed:", e);
  }
}
