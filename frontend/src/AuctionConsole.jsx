import { useCallback, useEffect, useRef, useState } from 'react';
import { getResults, getRoom, getShortlist, recordResult, undoLast } from './api';
import PlayerPicker from './PlayerPicker';
import './AuctionConsole.css';

/** Poll interval while the auction is running. The auction is a one-off event
 *  with <=10 viewers, so a short poll is cheaper than wiring up WebSockets. */
const POLL_MS = 4000;

export default function AuctionConsole({
  session, onExit, onViewStandings, onManageShortlist,
}) {
  const { roomCode, userId } = session;

  const [room, setRoom] = useState(null);
  const [results, setResults] = useState([]);
  const [shortlist, setShortlist] = useState(null);
  const [loadError, setLoadError] = useState(null);

  // Host-only form state
  const [player, setPlayer] = useState(null);
  const [winnerId, setWinnerId] = useState('');
  const [price, setPrice] = useState('');
  const [actionError, setActionError] = useState(null);
  const [busy, setBusy] = useState(false);

  const isHost = room?.hostId === userId;

  // Skip a poll-driven repaint if the component unmounted mid-flight.
  const alive = useRef(true);
  useEffect(() => () => { alive.current = false; }, []);

  const refresh = useCallback(async () => {
    try {
      const [r, res] = await Promise.all([getRoom(roomCode), getResults(roomCode)]);
      if (!alive.current) return;
      setRoom(r);
      setResults(res);
      setLoadError(null);
    } catch (err) {
      if (alive.current) setLoadError(err.message);
    }
  }, [roomCode]);

  useEffect(() => {
    refresh();
    const id = setInterval(refresh, POLL_MS);
    return () => clearInterval(id);
  }, [refresh]);

  // Deliberately outside the poll: only the host changes the shortlist, and
  // leaving this screen to do so unmounts the console, so a mount-time read is
  // always current. A failure here is not surfaced — the shortlist only affects
  // the picker's empty-state wording, and the server enforces it regardless.
  useEffect(() => {
    let cancelled = false;
    getShortlist(roomCode)
      .then((s) => { if (!cancelled) setShortlist(s); })
      .catch(() => {});
    return () => { cancelled = true; };
  }, [roomCode]);

  const soldPlayerIds = new Set(results.map((r) => r.playerId));

  async function submit(e) {
    e.preventDefault();
    if (!player || !winnerId) return;
    setActionError(null);
    setBusy(true);
    try {
      await recordResult(
        roomCode,
        { playerId: player.id, participantId: winnerId, price: Number(price) },
        userId,
      );
      setPlayer(null);
      setPrice('');
      // Leave the winner selected: the same buyer often takes several players.
      await refresh();
    } catch (err) {
      setActionError(err.message);
    } finally {
      setBusy(false);
    }
  }

  async function handleUndo() {
    setActionError(null);
    setBusy(true);
    try {
      await undoLast(roomCode, userId);
      await refresh();
    } catch (err) {
      setActionError(err.message);
    } finally {
      setBusy(false);
    }
  }

  if (!room) {
    return (
      <div className="console">
        {loadError
          ? <p className="error" role="alert">{loadError}</p>
          : <p className="empty">Loading room…</p>}
      </div>
    );
  }

  const squadSize = room.config?.squadSize ?? 15;
  const recent = [...results].reverse().slice(0, 12);

  return (
    <div className="console">
      <header className="console-head">
        <div>
          <h1>{room.name}</h1>
          <p className="room-meta">
            Room <strong>{room.code}</strong> · {room.status}
            {isHost
              ? <span className="badge host">Host</span>
              : <span className="badge">Read-only</span>}
          </p>
        </div>
        <div className="head-actions">
          {isHost && (
            <button type="button" className="ghost" onClick={() => onManageShortlist(room.playerPoolId)}>
              Shortlist{shortlist?.curated ? ` (${shortlist.count})` : ''}
            </button>
          )}
          <button type="button" className="ghost" onClick={onViewStandings}>
            Standings
          </button>
          <button type="button" className="ghost" onClick={onExit}>Leave</button>
        </div>
      </header>

      {loadError && <p className="error" role="alert">{loadError}</p>}

      {isHost ? (
        <form className="record" onSubmit={submit}>
          <h2>Record a sale</h2>
          {actionError && <p className="error" role="alert">{actionError}</p>}

          <PlayerPicker
            roomCode={roomCode}
            poolId={room.playerPoolId}
            shortlisted={shortlist?.curated ?? false}
            soldPlayerIds={soldPlayerIds}
            selected={player}
            onSelect={setPlayer}
          />

          <div className="record-row">
            <label>
              Winner
              <select
                value={winnerId}
                onChange={(e) => setWinnerId(e.target.value)}
                required
              >
                <option value="">Select a team…</option>
                {room.participants.map((p) => (
                  <option
                    key={p.id}
                    value={p.id}
                    disabled={p.squadCount >= squadSize}
                  >
                    {p.teamName} — {p.budgetRemaining} left
                    {p.squadCount >= squadSize ? ' (squad full)' : ''}
                  </option>
                ))}
              </select>
            </label>

            <label className="price-field">
              Price
              <input
                type="number"
                min="0"
                value={price}
                onChange={(e) => setPrice(e.target.value)}
                required
              />
            </label>

            <button
              type="submit"
              className="primary"
              disabled={busy || !player || !winnerId || price === ''}
            >
              {busy ? 'Saving…' : 'Confirm sale'}
            </button>
          </div>

          <button
            type="button"
            className="undo"
            onClick={handleUndo}
            disabled={busy || results.length === 0}
          >
            Undo last sale
          </button>
        </form>
      ) : (
        <p className="notice">
          Only the host records sales. This view updates on its own as players
          are sold.
        </p>
      )}

      <section className="teams">
        <h2>Teams</h2>
        <table className="board">
          <thead>
            <tr>
              <th scope="col">Team</th>
              <th scope="col" className="num">Squad</th>
              <th scope="col" className="num">Budget</th>
            </tr>
          </thead>
          <tbody>
            {room.participants.map((p) => (
              <tr key={p.id}>
                <td>
                  <span className="team-name">{p.teamName}</span>
                  <span className="user-name">{p.userName}</span>
                </td>
                <td className="num">{p.squadCount}/{squadSize}</td>
                <td className="num">{p.budgetRemaining}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>

      <section className="sold">
        <h2>Recent sales <span className="count">({results.length} total)</span></h2>
        {recent.length === 0 ? (
          <p className="empty">Nothing sold yet.</p>
        ) : (
          <table className="board">
            <thead>
              <tr>
                <th scope="col" className="num">#</th>
                <th scope="col">Player</th>
                <th scope="col">Bought by</th>
                <th scope="col" className="num">Price</th>
              </tr>
            </thead>
            <tbody>
              {recent.map((r) => (
                <tr key={r.id}>
                  <td className="num seq">{r.sequenceNumber}</td>
                  <td>{r.playerName}</td>
                  <td>{r.teamName}</td>
                  <td className="num">{r.purchasePrice}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>
    </div>
  );
}
