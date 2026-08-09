/**
 * Who the browser is acting as. The API identifies callers with an `X-User-Id`
 * header and enforces host-only actions server-side, so this is convenience
 * state — clearing it hides the host controls but does not grant anyone rights
 * they did not already have.
 */
const KEY = 'auctionroom.session';

/** @returns {{roomCode: string, userId: string, participantId: string} | null} */
export function loadSession() {
  try {
    const raw = localStorage.getItem(KEY);
    if (!raw) return null;
    const s = JSON.parse(raw);
    return s?.roomCode && s?.userId ? s : null;
  } catch {
    // Corrupt or unavailable storage (private mode) — behave as signed out.
    return null;
  }
}

export function saveSession(session) {
  try {
    localStorage.setItem(KEY, JSON.stringify(session));
  } catch {
    // Storage full or blocked: the session stays in React state for this tab.
  }
}

export function clearSession() {
  try {
    localStorage.removeItem(KEY);
  } catch {
    // Nothing to do — a failed clear still drops the in-memory session.
  }
}
