import { useCallback, useEffect, useRef, useState } from 'react';
import {
  endAuction, getPassed, getResults, getRoom, getShortlist, nextRound,
  nominate, passPlayer, recordResult, startAuction, undoLast,
} from './api';
import PlayerPicker from './PlayerPicker';
import { PlayerCardBody, PlayerPhoto } from './player';
import './AuctionConsole.css';

/** Poll interval while the auction is running. The auction is a one-off event
 *  with <=10 viewers, so a short poll is cheaper than wiring up WebSockets. */
const POLL_MS = 4000;

/** Mirrors RoomLifecycleService.MaxParticipants — shown, not enforced, here. */
const MAX_PARTICIPANTS = 10;

/** The room's own status words are the server's; these are the room's. */
const PHASE = {
  Setup: 'Lobby',
  Auction: 'Auction',
  War: 'War',
  Completed: 'Completed',
};

export default function AuctionConsole({
  session, onExit, onViewStandings, onManageShortlist,
}) {
  const { roomCode, userId } = session;

  const [room, setRoom] = useState(null);
  const [results, setResults] = useState([]);
  const [shortlist, setShortlist] = useState(null);
  const [passed, setPassed] = useState(() => new Set());
  const [loadError, setLoadError] = useState(null);

  // Host-only form state
  const [player, setPlayer] = useState(null);
  const [winnerId, setWinnerId] = useState('');
  const [price, setPrice] = useState('');
  const [manual, setManual] = useState(false);
  const [exhausted, setExhausted] = useState(false);
  const [endWarnings, setEndWarnings] = useState(null);
  const [actionError, setActionError] = useState(null);
  const [busy, setBusy] = useState(false);

  const isHost = room?.hostId === userId;
  const isSetup = room?.status === 'Setup';
  const isAuction = room?.status === 'Auction';
  const isWar = room?.status === 'War';

  // Who the sale form is about. The draw is the normal case; a manual pick
  // overrides it without disturbing it, so the host can put someone up out of
  // turn and the drawn player is still on the block afterwards.
  const manualPick = isAuction ? player : null;
  const onBlock = manualPick ?? (isAuction ? room?.nomination ?? null : null);

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

  const loadPassed = useCallback(async () => {
    try {
      const p = await getPassed(roomCode);
      if (alive.current) setPassed(new Set(p.passedPlayerIds));
    } catch {
      // The Unsold badge is a nicety. Losing it must not stop an auction.
    }
  }, [roomCode]);

  // Only the host's own actions change this list, and it can run to a few
  // hundred ids late in an auction — so it is read once on entering the auction
  // and refreshed by the pass and round handlers, never polled.
  useEffect(() => { if (isAuction) loadPassed(); }, [isAuction, loadPassed]);

  // "Nothing left to draw" is a fact about one round, so advancing the round or
  // ending the auction retires it.
  useEffect(() => { setExhausted(false); }, [room?.auctionRound, room?.status]);

  const soldPlayerIds = new Set(results.map((r) => r.playerId));

  /** Every host action shares this shape: clear the error, block the UI, refresh. */
  async function act(fn) {
    setActionError(null);
    setBusy(true);
    try {
      await fn();
    } catch (err) {
      setActionError(err.message);
    } finally {
      if (alive.current) setBusy(false);
    }
  }

  async function submit(e) {
    e.preventDefault();
    if (!onBlock || !winnerId) return;
    await act(async () => {
      await recordResult(
        roomCode,
        { playerId: onBlock.id, participantId: winnerId, price: Number(price) },
        userId,
      );
      // The server clears the block when the sold player was the drawn one, so
      // the next poll empties the card on its own; this only drops a manual pick.
      setPlayer(null);
      setPrice('');
      // Leave the winner selected: the same buyer often takes several players.
      await refresh();
    });
  }

  const handleUndo = () => act(async () => {
    await undoLast(roomCode, userId);
    // The undone player is unowned again, so the round has something to draw
    // even if the last draw came up empty.
    setExhausted(false);
    await refresh();
  });

  const handleStart = () => act(async () => {
    await startAuction(roomCode, userId);
    await refresh();
  });

  const handleDraw = () => act(async () => {
    const drawn = await nominate(roomCode, userId);
    // A 204 arrives as null: the round has nobody left to draw. Not an error —
    // it is the cue to open the unsold round or end the auction.
    if (!drawn) setExhausted(true);
    await refresh();
  });

  const handlePass = () => act(async () => {
    await passPlayer(roomCode, userId);
    await Promise.all([refresh(), loadPassed()]);
  });

  const handleNextRound = () => act(async () => {
    await nextRound(roomCode, userId);
    setPlayer(null);
    await Promise.all([refresh(), loadPassed()]);
  });

  async function handleEnd(confirm) {
    setActionError(null);
    setBusy(true);
    try {
      await endAuction(roomCode, confirm, userId);
      setEndWarnings(null);
      setPlayer(null);
      await refresh();
    } catch (err) {
      // 409 is not a refusal — it is the same request asked back with its
      // consequences attached, so it renders as a confirm step, not an error.
      if (err.status === 409 && err.body?.warnings) {
        setEndWarnings({ message: err.message, warnings: err.body.warnings });
      } else {
        setActionError(err.message);
      }
    } finally {
      if (alive.current) setBusy(false);
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
  const roundLabel = room.auctionRound >= 2 ? 'unsold round' : 'main round';
  const joined = room.participants.length;

  return (
    <div className="console">
      <header className="console-head">
        <div>
          <h1>{room.name}</h1>
          <p className="room-meta">
            Room <strong>{room.code}</strong> · {PHASE[room.status] ?? room.status}
            {isAuction ? ` · ${roundLabel}` : null}
            {isHost
              ? <span className="badge host">Host</span>
              : <span className="badge">Read-only</span>}
          </p>
        </div>
        <div className="head-actions">
          {isHost && (isSetup || isAuction) && (
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

      {isSetup && (
        <section className="stage">
          <h2>Lobby</h2>
          {actionError && <p className="error" role="alert">{actionError}</p>}

          <p className="stage-note">
            Share code <strong>{room.code}</strong> so everyone can join. Once the
            auction starts the team list is fixed — nobody can join after that.
          </p>

          <dl className="config">
            <div><dt>Budget</dt><dd>{room.config?.budget}</dd></div>
            <div><dt>Squad</dt><dd>{squadSize}</dd></div>
            <div><dt>Scoring</dt><dd>Best {room.config?.bestN}</dd></div>
            <div><dt>Joined</dt><dd>{joined}/{MAX_PARTICIPANTS}</dd></div>
          </dl>

          {isHost ? (
            <>
              <button
                type="button"
                className="primary"
                onClick={handleStart}
                disabled={busy || joined < 2}
              >
                {busy ? 'Starting…' : 'Start auction'}
              </button>
              {joined < 2 && (
                <p className="empty">At least 2 teams are needed to start.</p>
              )}
            </>
          ) : (
            <p className="empty">Waiting for the host to start the auction.</p>
          )}
        </section>
      )}

      {isAuction && (
        <section className="stage">
          <h2>On the block <span className="count">({roundLabel})</span></h2>
          {actionError && <p className="error" role="alert">{actionError}</p>}

          {onBlock ? (
            <div className="chosen-card">
              <PlayerPhoto player={onBlock} size="lg" />

              <PlayerCardBody
                player={onBlock}
                badge={
                  <>
                    {manualPick && <span className="p-manual">Picked by hand</span>}
                    {passed.has(onBlock.id) && <span className="p-unsold">Unsold</span>}
                  </>
                }
              />

              {manualPick && isHost && (
                <button type="button" onClick={() => setPlayer(null)}>Change</button>
              )}
            </div>
          ) : (
            <p className="empty">
              {exhausted
                ? `Nothing left to draw in the ${roundLabel}.`
                : 'Nobody is on the block yet.'}
            </p>
          )}

          {isHost ? (
            <>
              {!onBlock && !exhausted && (
                <button type="button" className="primary draw" onClick={handleDraw} disabled={busy}>
                  {busy ? 'Drawing…' : 'Draw next player'}
                </button>
              )}

              {onBlock && (
                <form className="record" onSubmit={submit}>
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
                      disabled={busy || !winnerId || price === ''}
                    >
                      {busy ? 'Saving…' : 'Confirm sale'}
                    </button>

                    {/* Passing settles the drawn player, not a hand-picked one:
                        there is nothing to pass on somebody nobody drew. */}
                    {!manualPick && (
                      <button type="button" className="ghost" onClick={handlePass} disabled={busy}>
                        Nobody bid
                      </button>
                    )}
                  </div>
                </form>
              )}

              <div className="manual">
                <button
                  type="button"
                  className="ghost"
                  onClick={() => setManual(!manual)}
                  aria-expanded={manual}
                >
                  {manual ? 'Hide manual pick' : 'Pick manually'}
                </button>

                {manual && (
                  <>
                    <p className="empty">
                      Put someone up out of turn. Players passed over earlier are
                      here too, badged Unsold.
                    </p>
                    <PlayerPicker
                      roomCode={roomCode}
                      poolId={room.playerPoolId}
                      shortlisted={shortlist?.curated ?? false}
                      soldPlayerIds={soldPlayerIds}
                      passedPlayerIds={passed}
                      // Always null: the chosen player is shown once, on the
                      // block above, rather than twice in two different cards.
                      selected={null}
                      onSelect={setPlayer}
                    />
                  </>
                )}
              </div>

              <div className="stage-actions">
                <button
                  type="button"
                  className="undo"
                  onClick={handleUndo}
                  disabled={busy || results.length === 0}
                >
                  Undo last sale
                </button>

                <span className="spacer" />

                {room.auctionRound < 2 && (
                  <button type="button" className="ghost" onClick={handleNextRound} disabled={busy}>
                    End main round
                  </button>
                )}
                <button type="button" className="ghost" onClick={() => handleEnd(false)} disabled={busy}>
                  End auction
                </button>
              </div>

              {endWarnings && (
                <div className="confirm">
                  <p role="alert">{endWarnings.message}</p>
                  <ul>
                    {endWarnings.warnings.map((w) => (
                      <li key={w.participantId}>
                        <span className="team-name">{w.teamName}</span>
                        {w.squadCount} of {w.required}
                      </li>
                    ))}
                  </ul>
                  <div className="confirm-actions">
                    <button type="button" className="primary" onClick={() => handleEnd(true)} disabled={busy}>
                      End anyway
                    </button>
                    <button type="button" className="ghost" onClick={() => setEndWarnings(null)} disabled={busy}>
                      Keep auctioning
                    </button>
                  </div>
                </div>
              )}
            </>
          ) : (
            <p className="notice">
              Only the host records sales. This view updates on its own as players
              go up and are sold.
            </p>
          )}
        </section>
      )}

      {isWar && (
        <p className="notice">
          The auction is over and squads are set. Points update daily — open
          Standings to see where everyone is. Swaps run on weekends and are
          recorded by the host.
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

      {!isSetup && (
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
      )}
    </div>
  );
}
