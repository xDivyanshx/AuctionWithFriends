/**
 * Drill-down for one participant: every squad slot with its contribution, and
 * which slots fall inside the best-N that make up the standings score.
 *
 * Slots arrive already sorted by contribution (highest first) from the API.
 */
export default function SquadPanel({ squad, error, onClose }) {
  return (
    <section className="squad" aria-live="polite">
      <div className="squad-head">
        <h2>{squad ? squad.teamName : 'Squad'}</h2>
        <button type="button" className="close" onClick={onClose} aria-label="Close squad">
          ×
        </button>
      </div>

      {error && <p className="error" role="alert">{error}</p>}
      {!squad && !error && <p className="empty">Loading squad…</p>}

      {squad && (
        <>
          <dl className="squad-stats">
            <div>
              <dt>Score</dt>
              <dd>{squad.score}</dd>
            </div>
            <div>
              <dt>All slots</dt>
              <dd>{squad.totalPoints}</dd>
            </div>
            <div>
              <dt>Budget left</dt>
              <dd>{squad.budgetRemaining}</dd>
            </div>
          </dl>

          {squad.squad.length === 0 ? (
            <p className="empty">No players owned yet.</p>
          ) : (
            <table className="slots">
              <thead>
                <tr>
                  <th scope="col">Player</th>
                  <th scope="col">Pos</th>
                  <th scope="col" className="num">Contribution</th>
                </tr>
              </thead>
              <tbody>
                {squad.squad.map((s) => (
                  <tr key={s.playerId} className={s.countsTowardScore ? 'counts' : 'benched'}>
                    <td>
                      <span className="player-name">{s.playerName}</span>
                      {s.team && <span className="player-team">{s.team}</span>}
                    </td>
                    <td className="pos">{s.position}</td>
                    <td className="num">
                      {s.contribution}
                      {s.inheritedPoints > 0 && (
                        <span
                          className="inherited"
                          title={`Includes ${s.inheritedPoints} points inherited from a swapped-out player`}
                        >
                          incl. {s.inheritedPoints} inherited
                        </span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}

          <p className="legend">
            Highlighted rows are the best {squad.squad.filter((s) => s.countsTowardScore).length} that
            make up the score.
          </p>
        </>
      )}
    </section>
  );
}
