import { useState } from 'react';
import AuctionConsole from './AuctionConsole';
import Home from './Home';
import ShortlistManager from './ShortlistManager';
import Standings from './Standings';
import { clearSession, loadSession, saveSession } from './session';

/**
 * Four views, no router: the app has one entry point and the stored session
 * decides where you land. A routing library would cost more than it saves here,
 * and there are no shareable URLs yet.
 *
 * Standings is reachable without a session because it is the daily-use page —
 * checking the table should not require rejoining the room.
 */
export default function App() {
  const [session, setSession] = useState(loadSession);
  const [view, setView] = useState(session ? 'room' : 'home');
  // Handed up by the console rather than re-fetched: the console already has
  // the room loaded, and the shortlist screen is only ever opened from it.
  const [poolId, setPoolId] = useState(null);

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

  if (view === 'shortlist' && session) {
    return (
      <ShortlistManager
        session={session}
        poolId={poolId}
        onBack={() => setView('room')}
      />
    );
  }

  if (session) {
    return (
      <AuctionConsole
        session={session}
        onExit={leave}
        onViewStandings={() => setView('standings')}
        onManageShortlist={(id) => { setPoolId(id); setView('shortlist'); }}
      />
    );
  }

  return (
    <Home onSession={startSession} onViewStandings={() => setView('standings')} />
  );
}
