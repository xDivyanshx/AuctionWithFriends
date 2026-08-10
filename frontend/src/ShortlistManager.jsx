import { useCallback, useEffect, useMemo, useState } from 'react';
import { getPoolClubs, getPoolPlayers, getShortlist, updateShortlist } from './api';
import { POSITIONS } from './fpl';
import { PlayerFigures, PlayerIdent } from './player';
import './ShortlistManager.css';

// The whole pool is ~573 players and the point of this screen is bulk curation,
// so every match is fetched rather than paged: "Add all shown" has to mean all
// of them, not the first 25.
const TAKE_ALL = 600;

/**
 * Host screen for choosing which slice of the pool a room auctions.
 *
 * Edits persist immediately — each toggle and each bulk action is its own
 * request — so there is no unsaved state to lose. The local set is updated
 * optimistically and reverted if the server refuses, which keeps ticking
 * through a long list responsive and avoids the out-of-order problem that
 * applying each response wholesale would create.
 *
 * An empty shortlist means the auction is unrestricted; see ShortlistEntry on
 * the server for why that is the fallback rather than an error state.
 */
export default function ShortlistManager({ session, poolId, onBack }) {
  const { roomCode, userId } = session;

  const [chosen, setChosen] = useState(null); // Set of player ids; null until loaded
  const [players, setPlayers] = useState([]);
  const [clubs, setClubs] = useState([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(null);
  const [saveError, setSaveError] = useState(null);

  const [search, setSearch] = useState('');
  const [club, setClub] = useState('');
  const [position, setPosition] = useState('');
  const [minPoints, setMinPoints] = useState('');

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const [list, clubNames] = await Promise.all([
          getShortlist(roomCode),
          poolId ? getPoolClubs(poolId) : Promise.resolve([]),
        ]);
        if (cancelled) return;
        setChosen(new Set(list.playerIds));
        setClubs(clubNames);
      } catch (err) {
        if (!cancelled) setError(err.message);
      }
    })();
    return () => { cancelled = true; };
  }, [roomCode, poolId]);

  useEffect(() => {
    if (!poolId) return undefined;

    let cancelled = false;
    const t = setTimeout(async () => {
      setLoading(true);
      try {
        const found = await getPoolPlayers(poolId, {
          search,
          club,
          position,
          minPoints: minPoints === '' ? undefined : Number(minPoints),
          take: TAKE_ALL,
        });
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
  }, [poolId, search, club, position, minPoints]);

  /**
   * Apply a delta locally, then persist it. A failure is undone by applying the
   * inverse delta rather than restoring a snapshot of the set: clicking down a
   * long list means several requests can be in flight at once, and a snapshot
   * taken before one of them would wipe out the others when it was restored.
   * The deltas are always disjoint from the current membership — an add only
   * ever carries ids that were absent — so the inverse is exact.
   */
  const apply = useCallback(async (add, remove) => {
    const patch = (addIds, removeIds) => setChosen((current) => {
      if (current === null) return current;
      const next = new Set(current);
      addIds.forEach((id) => next.add(id));
      removeIds.forEach((id) => next.delete(id));
      return next;
    });

    patch(add, remove);
    setSaveError(null);

    try {
      await updateShortlist(roomCode, { add, remove }, userId);
    } catch (err) {
      patch(remove, add);
      setSaveError(err.message);
    }
  }, [roomCode, userId]);

  const shownIds = useMemo(() => players.map((p) => p.id), [players]);
  const missing = useMemo(
    () => (chosen ? shownIds.filter((id) => !chosen.has(id)) : []),
    [shownIds, chosen],
  );
  const present = useMemo(
    () => (chosen ? shownIds.filter((id) => chosen.has(id)) : []),
    [shownIds, chosen],
  );

  if (!poolId) {
    return (
      <div className="shortlist">
        <p className="error">This room has no player pool linked.</p>
        <button type="button" className="ghost" onClick={onBack}>Back</button>
      </div>
    );
  }

  const total = chosen?.size ?? 0;
  const filtered = search !== '' || club !== '' || position !== '' || minPoints !== '';

  return (
    <div className="shortlist">
      <header className="console-head">
        <div>
          <h1>Auction shortlist</h1>
          <p className="room-meta">
            {chosen === null
              ? 'Loading…'
              : total === 0
                ? 'Empty — every player in the pool can be auctioned.'
                : `${total} player${total === 1 ? '' : 's'} will be auctioned.`}
          </p>
        </div>
        <div className="head-actions">
          <button type="button" className="ghost" onClick={onBack}>Done</button>
        </div>
      </header>

      {error && <p className="error" role="alert">{error}</p>}
      {saveError && <p className="error" role="alert">{saveError}</p>}

      <div className="sl-filters">
        <label className="grow">
          Search
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Name or club…"
          />
        </label>
        <label>
          Club
          <select value={club} onChange={(e) => setClub(e.target.value)}>
            <option value="">All clubs</option>
            {clubs.map((c) => <option key={c} value={c}>{c}</option>)}
          </select>
        </label>
        <label>
          Position
          <select value={position} onChange={(e) => setPosition(e.target.value)}>
            <option value="">All</option>
            {POSITIONS.map((p) => <option key={p} value={p}>{p}</option>)}
          </select>
        </label>
        <label className="sl-points">
          Min points
          <input
            type="number"
            min="0"
            value={minPoints}
            onChange={(e) => setMinPoints(e.target.value)}
            placeholder="0"
          />
        </label>
      </div>

      <div className="sl-bulk">
        <span className="sl-shown">
          {loading ? 'Searching…' : `${players.length} shown`}
          {filtered ? ' (filtered)' : ''}
        </span>
        <button
          type="button"
          onClick={() => apply(missing, [])}
          disabled={chosen === null || missing.length === 0}
        >
          Add all shown{missing.length > 0 ? ` (${missing.length})` : ''}
        </button>
        <button
          type="button"
          onClick={() => apply([], present)}
          disabled={chosen === null || present.length === 0}
        >
          Remove all shown{present.length > 0 ? ` (${present.length})` : ''}
        </button>
      </div>

      <ul className="sl-results">
        {players.length === 0 && !loading && (
          <li className="empty">No players match these filters.</li>
        )}
        {players.map((p) => {
          const on = chosen?.has(p.id) ?? false;
          return (
            <li key={p.id} className={on ? 'is-on' : undefined}>
              <label>
                <input
                  type="checkbox"
                  checked={on}
                  disabled={chosen === null}
                  onChange={() => apply(on ? [] : [p.id], on ? [p.id] : [])}
                />
                <PlayerIdent player={p} />
                <PlayerFigures player={p} />
              </label>
            </li>
          );
        })}
      </ul>
    </div>
  );
}
