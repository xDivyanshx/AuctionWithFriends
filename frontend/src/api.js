const BASE = (import.meta.env.VITE_API_BASE ?? '').replace(/\/$/, '');

/**
 * Single fetch wrapper. The API reports failures as { error: "..." } with a
 * 4xx status, so surface that message rather than a bare status code.
 */
async function get(path) {
  let res;
  try {
    res = await fetch(`${BASE}${path}`);
  } catch {
    // Network-level failure: server down, wrong port, CORS rejection.
    throw new Error('Cannot reach the API. Is it running?');
  }

  if (!res.ok) {
    let message = `Request failed (${res.status}).`;
    try {
      const body = await res.json();
      if (body?.error) message = body.error;
    } catch {
      // Non-JSON error body — keep the status-based message.
    }
    throw new Error(message);
  }

  return res.json();
}

/**
 * Same contract as get(), plus the caller identity the API uses to authorize
 * host-only actions. Sending no userId is valid — the server then answers 403,
 * which is the behaviour we want surfaced rather than hidden.
 */
async function post(path, body, userId) {
  let res;
  try {
    res = await fetch(`${BASE}${path}`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...(userId ? { 'X-User-Id': userId } : {}),
      },
      body: JSON.stringify(body ?? {}),
    });
  } catch {
    throw new Error('Cannot reach the API. Is it running?');
  }

  if (!res.ok) {
    let message = `Request failed (${res.status}).`;
    try {
      const parsed = await res.json();
      if (parsed?.error) message = parsed.error;
    } catch {
      // Non-JSON error body — keep the status-based message.
    }
    throw new Error(message);
  }

  // Every current endpoint returns JSON, but tolerate an empty 204 body.
  return res.status === 204 ? null : res.json();
}

const room = (code) => `/api/rooms/${encodeURIComponent(code)}`;

/** GET /api/rooms/{code}/standings → StandingRow[] */
export const getStandings = (roomCode) => get(`${room(roomCode)}/standings`);

/** GET /api/rooms/{code}/participants/{id} → ParticipantSquad */
export const getSquad = (roomCode, participantId) =>
  get(`${room(roomCode)}/participants/${participantId}`);

/** GET /api/rooms/{code} → RoomResponse */
export const getRoom = (roomCode) => get(room(roomCode));

/** POST /api/rooms → RoomResponse (hostId doubles as the host's caller id) */
export const createRoom = (payload) => post('/api/rooms', payload);

/** POST /api/rooms/{code}/join → JoinResponse */
export const joinRoom = (roomCode, payload) => post(`${room(roomCode)}/join`, payload);

/** GET /api/rooms/{code}/auction/results → AuctionResultResponse[] */
export const getResults = (roomCode) => get(`${room(roomCode)}/auction/results`);

/** POST /api/rooms/{code}/auction/record → AuctionResultResponse (host only) */
export const recordResult = (roomCode, payload, userId) =>
  post(`${room(roomCode)}/auction/record`, payload, userId);

/** POST /api/rooms/{code}/auction/undo (host only) */
export const undoLast = (roomCode, userId) =>
  post(`${room(roomCode)}/auction/undo`, {}, userId);

/** GET /api/pools/{id}/players → PlayerResponse[] */
export function getPoolPlayers(poolId, filters = {}) {
  return get(`/api/pools/${poolId}/players?${playerQuery(filters)}`);
}

/** GET /api/pools/{id}/clubs → string[] */
export const getPoolClubs = (poolId) => get(`/api/pools/${poolId}/clubs`);

/**
 * GET /api/rooms/{code}/shortlist/players → PlayerResponse[]
 *
 * Same shape as the pool endpoint, but limited to what this room may auction.
 * The picker uses this one so it can never offer a player the sale would reject.
 */
export function getRoomPlayers(roomCode, filters = {}) {
  return get(`${room(roomCode)}/shortlist/players?${playerQuery(filters)}`);
}

/** GET /api/rooms/{code}/shortlist → ShortlistResponse */
export const getShortlist = (roomCode) => get(`${room(roomCode)}/shortlist`);

/** POST /api/rooms/{code}/shortlist → ShortlistResponse (host only) */
export const updateShortlist = (roomCode, payload, userId) =>
  post(`${room(roomCode)}/shortlist`, payload, userId);

/** Shared query string for the two player-search endpoints. */
function playerQuery({ search, position, club, minPoints, take = 25 } = {}) {
  const qs = new URLSearchParams({ take: String(take) });
  if (search) qs.set('search', search);
  if (position) qs.set('position', position);
  if (club) qs.set('club', club);
  // 0 is the default floor, so only send a real filter.
  if (minPoints) qs.set('minPoints', String(minPoints));
  return qs;
}
