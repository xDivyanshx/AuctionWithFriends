import { useEffect, useState } from 'react';
import { getRoomPlayers } from './api';
import { POSITIONS, formatPrice } from './fpl';
import { PlayerFigures, PlayerIdent, PlayerPhoto, StatusBadge } from './player';

function Stat({ label, value }) {
  return (
    <div className="p-stat">
      <dt>{label}</dt>
      <dd>{value}</dd>
    </div>
  );
}

/**
 * Search the players this room can auction and pick who is on the block.
 *
 * The search hits the room's endpoint, not the pool's, so if the host curated a
 * shortlist only those players appear. An uncurated room sees the whole pool.
 *
 * Already-sold players are filtered out client-side: the endpoint has no
 * per-room notion of ownership, and the sold list is small enough (<= squad
 * size x participants) that filtering here is cheaper than a new endpoint.
 * The server still rejects a duplicate sale, so this is convenience, not
 * enforcement.
 *
 * Every stat shown comes straight from FPL. Before the season's first gameweek
 * they still hold last season's figures, which is exactly what a bidder wants
 * to judge a player on.
 */
export default function PlayerPicker({ roomCode, poolId, shortlisted, soldPlayerIds, selected, onSelect }) {
  const [search, setSearch] = useState('');
  const [position, setPosition] = useState('');
  const [players, setPlayers] = useState([]);
  const [error, setError] = useState(null);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    if (!poolId) return undefined;

    let cancelled = false;
    // Debounce so typing does not fire a request per keystroke.
    const t = setTimeout(async () => {
      setLoading(true);
      try {
        const found = await getRoomPlayers(roomCode, { search, position, take: 25 });
        if (!cancelled) {
          setPlayers(found);
          setError(null);
        }
      } catch (err) {
        if (!cancelled) setError(err.message);
      } finally {
        if (!cancelled) setLoading(false);
      }
    }, 250);

    return () => { cancelled = true; clearTimeout(t); };
  }, [roomCode, poolId, search, position]);

  if (!poolId) {
    return <p className="error">This room has no player pool linked.</p>;
  }

  const available = players.filter((p) => !soldPlayerIds.has(p.id));

  return (
    <div className="picker">
      <div className="picker-controls">
        <label className="grow">
          Player
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search name or club…"
          />
        </label>
        <label>
          Position
          <select value={position} onChange={(e) => setPosition(e.target.value)}>
            <option value="">All</option>
            {POSITIONS.map((p) => <option key={p} value={p}>{p}</option>)}
          </select>
        </label>
      </div>

      {error && <p className="error" role="alert">{error}</p>}

      {selected && (
        <div className="chosen-card">
          <PlayerPhoto player={selected} size="lg" />

          <div className="chosen-body">
            <p className="chosen-name">
              <strong>{selected.name}</strong>
              <StatusBadge player={selected} />
            </p>
            <p className="chosen-meta">
              {[
                selected.team,
                selected.position,
                selected.age != null ? `${selected.age} yrs` : null,
                formatPrice(selected.nowCost),
              ].filter(Boolean).join(' · ')}
            </p>

            <dl className="p-stats">
              <Stat label="Points" value={selected.totalPoints} />
              <Stat label="Per game" value={selected.pointsPerGame} />
              <Stat label="Minutes" value={selected.minutes} />
              <Stat label="Starts" value={selected.starts} />
              <Stat label="Goals" value={selected.goalsScored} />
              <Stat label="Assists" value={selected.assists} />
            </dl>
          </div>

          <button type="button" onClick={() => onSelect(null)}>
            Change
          </button>
        </div>
      )}

      {!selected && (
        <ul className="results" aria-busy={loading}>
          {available.length === 0 && !loading && (
            <li className="empty">
              {shortlisted
                ? 'No unsold players match, within this room’s shortlist.'
                : 'No unsold players match.'}
            </li>
          )}
          {available.map((p) => (
            <li key={p.id}>
              <button type="button" onClick={() => onSelect(p)}>
                <PlayerPhoto player={p} size="sm" />
                <PlayerIdent player={p} />
                <PlayerFigures player={p} />
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
