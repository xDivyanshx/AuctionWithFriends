import { useState } from 'react';
import AuctionConsole from './AuctionConsole';
import Home from './Home';
import Standings from './Standings';
import { clearSession, loadSession, saveSession } from './session';

/**
 * Three views, no router: the app has one entry point and the stored session
 * decides where you land. A routing library would cost more than it saves here,
 * and there are no shareable URLs yet.
 *
 * Standings is reachable without a session because it is the daily-use page —
 * checking the table should not require rejoining the room.
 */
export default function App() {
  const [session, setSession] = useState(loadSession);
  const [view, setView] = useState(session ? 'room' : 'home');

  function startSession(next) {
    saveSession(next);
    setSession(next);
    setView('room');
  }

  function leave() {
    clearSession();
    setSession(null);
    setView('home');
  }

  if (view === 'standings') {
    return (
      <Standings
        initialCode={session?.roomCode ?? ''}
        backLabel={session ? 'Back to room' : 'Back'}
        onBack={() => setView(session ? 'room' : 'home')}
      />
    );
  }

  if (session) {
    return (
      <AuctionConsole
        session={session}
        onExit={leave}
        onViewStandings={() => setView('standings')}
      />
    );
  }

  return (
    <Home onSession={startSession} onViewStandings={() => setView('standings')} />
  );
}
