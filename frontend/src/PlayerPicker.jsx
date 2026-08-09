import { useEffect, useState } from 'react';
import { getPoolPlayers } from './api';

const POSITIONS = ['Goalkeeper', 'Defender', 'Midfielder', 'Forward'];

/**
 * Search the room's player pool and pick who is being auctioned.
 *
 * Already-sold players are filtered out client-side: the pool endpoint has no
 * per-room notion of ownership, and the sold list is small enough (<= squad
 * size x participants) that filtering here is cheaper than a new endpoint.
 * The server still rejects a duplicate sale, so this is convenience, not
 * enforcement.
 */
export default function PlayerPicker({ poolId, soldPlayerIds, selected, onSelect }) {
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
        const found = await getPoolPlayers(poolId, { search, position, take: 25 });
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
  }, [poolId, search, position]);

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
        <p className="chosen">
          Auctioning: <strong>{selected.name}</strong>
          <span className="chosen-meta">{selected.team} · {selected.position}</span>
          <button type="button" onClick={() => onSelect(null)} aria-label="Clear selection">
            Change
          </button>
        </p>
      )}

      {!selected && (
        <ul className="results" aria-busy={loading}>
          {available.length === 0 && !loading && (
            <li className="empty">No unsold players match.</li>
          )}
          {available.map((p) => (
            <li key={p.id}>
              <button type="button" onClick={() => onSelect(p)}>
                <span className="p-name">{p.name}</span>
                <span className="p-meta">{p.team} · {p.position}</span>
                <span className="p-pts">{p.totalPoints} pts</span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
