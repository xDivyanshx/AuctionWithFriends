import { useEffect, useState } from 'react';
import { POSITION_SHORT, STATUS_LABEL, formatPrice } from './fpl';

/** Initials shown when a player has no photo, or the photo fails to load. */
function initials(name) {
  return name
    .split(' ')
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0].toUpperCase())
    .join('');
}

// The API stores the 110x140 asset (~90KB each). A search shows 25 rows at once,
// so list rows swap in the 40x40 crop — same image, ~14KB. If the URL ever stops
// matching this shape the replace is a no-op and the full-size image is used.
function sizedPhoto(url, variant) {
  return url.replace('/photos/players/110x140/', `/photos/players/${variant}/`);
}

export function PlayerPhoto({ player, size }) {
  const [failed, setFailed] = useState(false);

  // Reset when the row is reused for a different player.
  useEffect(() => { setFailed(false); }, [player.photoUrl]);

  if (!player.photoUrl || failed) {
    return <span className={`p-photo p-photo-${size} is-fallback`} aria-hidden="true">{initials(player.name)}</span>;
  }

  return (
    <img
      className={`p-photo p-photo-${size}`}
      src={size === 'sm' ? sizedPhoto(player.photoUrl, '40x40') : player.photoUrl}
      onError={() => setFailed(true)}
      alt=""
      loading="lazy"
    />
  );
}

export function StatusBadge({ player }) {
  if (!player.status || player.status === 'a') return null;

  const label = STATUS_LABEL[player.status] ?? 'Unavailable';
  // The news line carries the detail ("Groin injury - Expected back 22 Aug"),
  // so it becomes the accessible description rather than being hidden in a title.
  const detail = player.news ? `${label}: ${player.news}` : label;

  return (
    <span className={`p-status s-${player.status}`} title={detail}>
      <span className="sr-only">{detail}</span>
      <span aria-hidden="true">{label}</span>
    </span>
  );
}

/**
 * The identity block of a player row: name, availability, then club / position /
 * age. Shared so the auction picker and the shortlist screen cannot drift into
 * showing a player two different ways.
 */
export function PlayerIdent({ player }) {
  return (
    <span className="p-ident">
      <span className="p-name">
        {player.name}
        <StatusBadge player={player} />
      </span>
      <span className="p-meta">
        {[
          player.team,
          POSITION_SHORT[player.position] ?? player.position,
          player.age != null ? `${player.age}` : null,
        ].filter(Boolean).join(' · ')}
      </span>
    </span>
  );
}

/** Points and price, right-aligned. */
export function PlayerFigures({ player }) {
  return (
    <span className="p-figures">
      <span className="p-pts">{player.totalPoints} pts</span>
      <span className="p-price">{formatPrice(player.nowCost)}</span>
    </span>
  );
}
