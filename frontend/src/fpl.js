/**
 * How FPL's raw values read to a human.
 *
 * Kept apart from player.jsx so that file exports only components — a module
 * mixing the two breaks Vite's fast refresh, and this vocabulary is wanted by
 * screens that render no player row at all (the shortlist filters, for one).
 */

export const POSITIONS = ['Goalkeeper', 'Defender', 'Midfielder', 'Forward'];

export const POSITION_SHORT = {
  Goalkeeper: 'GKP',
  Defender: 'DEF',
  Midfielder: 'MID',
  Forward: 'FWD',
};

// FPL availability codes. Anything other than 'a' is worth flagging before
// someone spends half a budget on a player who is not going to play.
export const STATUS_LABEL = {
  i: 'Injured',
  d: 'Doubtful',
  s: 'Suspended',
  u: 'Unavailable',
  n: 'On loan',
};

/** FPL prices arrive in tenths of a million: 155 → £15.5m. */
export function formatPrice(tenths) {
  return `£${(tenths / 10).toFixed(1)}m`;
}
