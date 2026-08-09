import { useEffect, useRef, useState } from 'react';
import { getStandings, getSquad } from './api';
import SquadPanel from './SquadPanel';
import './Standings.css';

/**
 * The daily-use page: enter a room code, see the leaderboard, click a row to
 * drill into that participant's squad and per-player contributions.
 *
 * Works standalone (type a code) or seeded from an active session via
 * initialCode, in which case it loads on mount.
 *
 * Shapes come from the API's StandingRow / ParticipantSquad records, serialized
 * camelCase by ASP.NET Core.
 */
export default function Standings({ initialCode = '', onBack, backLabel = 'Back' }) {
  const [roomCode, setRoomCode] = useState(initialCode);
  const [rows, setRows] = useState(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(null);

  const [openId, setOpenId] = useState(null);
  const [squad, setSquad] = useState(null);
  const [squadError, setSquadError] = useState(null);

  const code = roomCode.trim().toUpperCase();

  // Auto-load when arriving from a session, but only once: after that the
  // room-code form stays in charge so a manual lookup is not overridden.
  const autoLoaded = useRef(false);
  useEffect(() => {
    if (initialCode && !autoLoaded.current) {
      autoLoaded.current = true;
      loadStandings();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [initialCode]);

  async function loadStandings(e) {
    e?.preventDefault();
    if (!code) {
      setError('Enter a room code.');
      return;
    }
    setLoading(true);
    setError(null);
    closeSquad();
    try {
      setRows(await getStandings(code));
    } catch (err) {
      setError(err.message);
      setRows(null);
    } finally {
      setLoading(false);
    }
  }

  async function openSquad(participantId) {
    if (openId === participantId) {
      closeSquad();
      return;
    }
    setOpenId(participantId);
    setSquad(null);
    setSquadError(null);
    try {
      setSquad(await getSquad(code, participantId));
    } catch (err) {
      setSquadError(err.message);
    }
  }

  function closeSquad() {
    setOpenId(null);
    setSquad(null);
    setSquadError(null);
  }

  return (
    <main className="standings">
      <div className="standings-head">
        <h1>Standings</h1>
        {onBack && (
          <button type="button" className="ghost" onClick={onBack}>
            {backLabel}
          </button>
        )}
      </div>

      <form className="room-form" onSubmit={loadStandings}>
        <label className="sr-only" htmlFor="room-code">Room code</label>
        <input
          id="room-code"
          name="roomCode"
          placeholder="Room code"
          value={roomCode}
          autoComplete="off"
          onChange={(ev) => setRoomCode(ev.target.value.toUpperCase())}
        />
        <button type="submit" disabled={loading}>
          {loading ? 'Loading…' : 'View'}
        </button>
      </form>

      {error && <p className="error" role="alert">{error}</p>}

      {rows?.length === 0 && !error && (
        <p className="empty">No participants in this room yet.</p>
      )}

      {rows?.length > 0 && (
        <table className="board">
          <caption className="sr-only">
            Leaderboard by best-eleven score. Select a team to see its squad.
          </caption>
          <thead>
            <tr>
              <th scope="col" className="num">#</th>
              <th scope="col">Team</th>
              <th scope="col" className="num">Score</th>
              <th scope="col" className="num">Squad</th>
              <th scope="col" className="num">Budget</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => {
              const open = openId === r.participantId;
              return (
                <tr key={r.participantId} className={open ? 'open' : undefined}>
                  <td className="num rank">{r.rank}</td>
                  <td>
                    <button
                      type="button"
                      className="team-btn"
                      aria-expanded={open}
                      onClick={() => openSquad(r.participantId)}
                    >
                      <span className="team-name">{r.teamName}</span>
                      <span className="user-name">{r.userName}</span>
                    </button>
                  </td>
                  <td className="num score">{r.score}</td>
                  <td className="num">{r.squadCount}</td>
                  <td className="num">{r.budgetRemaining}</td>
                </tr>
              );
            })}
          </tbody>
        </table>
      )}

      {openId && (
        <SquadPanel
          squad={squad}
          error={squadError}
          onClose={closeSquad}
        />
      )}
    </main>
  );
}
