import { useEffect, useState } from 'react';
import { getRoomPlayers } from './api';
import { POSITIONS } from './fpl';
import { PlayerCardBody, PlayerFigures, PlayerIdent, PlayerPhoto } from './player';

/** Shared empty set so a caller that passes no passed-ids allocates nothing. */
const NONE = new Set();

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
 * Players passed over in an earlier round are badged <c>Unsold</c> rather than
 * hidden: the host may well want to put one back on the block out of order, and
 * a pass is not a sale.
 *
 * Every stat shown comes straight from FPL. Before the season's first gameweek
 * they still hold last season's figures, which is exactly what a bidder wants
 * to judge a player on.
 */
export default function PlayerPicker({
  roomCode, poolId, shortlisted, soldPlayerIds, passedPlayerIds = NONE, selected, onSelect,
}) {
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

          <PlayerCardBody
            player={selected}
            badge={passedPlayerIds.has(selected.id) ? <span className="p-unsold">Unsold</span> : null}
          />

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
                {passedPlayerIds.has(p.id) && <span className="p-unsold">Unsold</span>}
                <PlayerFigures player={p} />
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
