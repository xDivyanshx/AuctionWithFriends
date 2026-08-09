import { useState } from 'react';
import { createRoom, joinRoom } from './api';
import './Home.css';

/**
 * Entry point: create a room (you become host) or join an existing one.
 * On success it hands the caller identity up to App, which stores it.
 */
export default function Home({ onSession, onViewStandings }) {
  const [mode, setMode] = useState('join');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);

  // Join fields
  const [code, setCode] = useState('');
  const [name, setName] = useState('');
  const [teamName, setTeamName] = useState('');

  // Create-only fields
  const [roomName, setRoomName] = useState('');
  const [budget, setBudget] = useState(100);
  const [squadSize, setSquadSize] = useState(15);
  const [bestN, setBestN] = useState(11);

  async function submit(e) {
    e.preventDefault();
    setError(null);
    setBusy(true);
    try {
      if (mode === 'create') {
        const room = await createRoom({
          roomName: roomName.trim(),
          hostName: name.trim(),
          hostTeamName: teamName.trim(),
          budget: Number(budget),
          squadSize: Number(squadSize),
          bestN: Number(bestN),
        });
        // The host's participant row is created alongside the room; hostId is
        // the caller id the API checks for host-only actions.
        const me = room.participants[0];
        onSession({
          roomCode: room.code,
          userId: room.hostId,
          participantId: me?.id ?? null,
        });
      } else {
        const joined = await joinRoom(code.trim().toUpperCase(), {
          name: name.trim(),
          teamName: teamName.trim(),
        });
        onSession({
          roomCode: joined.room.code,
          userId: joined.userId,
          participantId: joined.participantId,
        });
      }
    } catch (err) {
      setError(err.message);
    } finally {
      setBusy(false);
    }
  }

  const creating = mode === 'create';

  return (
    <div className="home">
      <h1>AuctionRoom</h1>

      <div className="tabs" role="tablist" aria-label="Create or join a room">
        <button
          type="button"
          role="tab"
          aria-selected={!creating}
          className={!creating ? 'active' : ''}
          onClick={() => { setMode('join'); setError(null); }}
        >
          Join a room
        </button>
        <button
          type="button"
          role="tab"
          aria-selected={creating}
          className={creating ? 'active' : ''}
          onClick={() => { setMode('create'); setError(null); }}
        >
          Create a room
        </button>
      </div>

      <form className="home-form" onSubmit={submit}>
        {error && <p className="error" role="alert">{error}</p>}

        {creating ? (
          <label>
            Room name
            <input
              value={roomName}
              onChange={(e) => setRoomName(e.target.value)}
              placeholder="Sunday League"
              required
            />
          </label>
        ) : (
          <label>
            Room code
            <input
              className="code-input"
              value={code}
              onChange={(e) => setCode(e.target.value)}
              placeholder="ABC123"
              maxLength={6}
              required
            />
          </label>
        )}

        <label>
          Your name
          <input value={name} onChange={(e) => setName(e.target.value)} required />
        </label>

        <label>
          Team name
          <input
            value={teamName}
            onChange={(e) => setTeamName(e.target.value)}
            placeholder="Real Madrid FC"
            required
          />
        </label>

        {creating && (
          <fieldset className="config">
            <legend>Tournament settings</legend>
            <div className="config-row">
              <label>
                Budget
                <input
                  type="number"
                  min="1"
                  value={budget}
                  onChange={(e) => setBudget(e.target.value)}
                />
              </label>
              <label>
                Squad size
                <input
                  type="number"
                  min="1"
                  value={squadSize}
                  onChange={(e) => setSquadSize(e.target.value)}
                />
              </label>
              <label>
                Best N
                <input
                  type="number"
                  min="1"
                  value={bestN}
                  onChange={(e) => setBestN(e.target.value)}
                />
              </label>
            </div>
          </fieldset>
        )}

        <button type="submit" className="primary" disabled={busy}>
          {busy ? 'Working…' : creating ? 'Create room' : 'Join room'}
        </button>
      </form>

      {onViewStandings && (
        <p className="alt-action">
          Just here for the table?{' '}
          <button type="button" onClick={onViewStandings}>View standings</button>
        </p>
      )}
    </div>
  );
}
