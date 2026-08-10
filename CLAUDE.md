# AuctionRoom — Fantasy Football Auction & Standings Platform

> This file is the single source of truth for architecture and decisions.
> Keep it updated as decisions change. Last updated: 2026-08-11.

## 1. Product Overview

A **fantasy football management platform** for a group of friends (up to 10).
The live auction happens **externally** (WhatsApp, in person). The platform's job:

1. **Record auction results** — host sequentially assigns each player to a
   participant at a sale price. All participants see this live.
2. **Team ownership** — each participant owns a squad (e.g. 15 players).
3. **Standings** — updated ~daily from real fantasy points. A participant's
   score = **sum of their best 11 players** (auto-computed, not user-chosen).

Sport: **Premier League football now.** IPL cricket later as a separate room type.

## 2. Current Status

- **Phase:** post-MVP feature work. The MVP is feature-complete, deployed and
  syncing daily (Phases 4–7: backend, standings page, auction console, deployment,
  cron). Now working through a 5-phase enhancement plan: Phase 1 (player detail)
  and Phase 2 (auction shortlist) are done; Phases 3–5 remain.
- **What exists:**
  - Solution scaffold: Api (.NET 10), Domain, Infrastructure, Tests
  - All domain entities with updated transaction model (1-for-1 swaps, refund+acquire money)
  - EF Core DbContext + 5 migrations applied to Neon: InitialCreate, AddHoldings,
    AddHoldingAcquisitionPrice, AddPlayerDetailFields, AddShortlistEntries
  - **RoomService**: create room, join room, get room state (with squad counts loaded)
  - **AuctionService**: record result (budget/squad/player-exists/pool validation), undo last, get results
  - **SwapService**: 1-for-1 unsold-pool and P2P swaps with frozen-points inheritance, weekend-only
    (IST), monthly per-participant cap, 1-month per-player cooldown, full budget validation
  - **CallerContext**: lightweight host-only enforcement via `X-User-Id` header
  - **RoomsController**: POST create, POST join, GET room state
  - **AuctionController**: POST record, POST undo, GET results — host-only enforced
  - **SwapsController**: POST unsold, POST p2p, GET all swaps — host-only enforced
  - **Holding** entity: live squad ownership with `AcquisitionPoints`, `InheritedPoints`, and
    `AcquisitionPrice` for frozen-at-swap scoring and correct refunds. Record opens a Holding;
    undo closes it; swaps repoint slots with inheritance.
  - **FplService**: typed HttpClient importing full FPL pool from `bootstrap-static/`
    (~573 as of 2026-08-07; drifts as FPL adds players), idempotent upsert on
    `ExternalId`; shared public pool per season.
  - **StandingsService**: best-N standings via frozen-points formula
    (`InheritedPoints + current − AcquisitionPoints`), ranks, persists denormalized
    figures; per-participant squad drill-down.
  - **AdminController**: POST import-fpl (synchronous — blocks until the import
    finishes, for manual runs), POST sync (202 Accepted + background import, for the
    timeout-capped cron). Both gated by the same `X-Sync-Secret` check
    (`SYNC_SECRET` env var) via a shared `CheckSyncSecret` helper, and both behind
    one single-flight gate so two imports cannot overlap.
  - **PoolsController**: GET pools, GET pool players (search/club/position/
    minPoints/take), GET pool clubs.
  - **ShortlistController**: GET shortlist, GET shortlist players, POST shortlist
    (host-only batch add/remove). See the Phase 2 bullet below.
  - **StandingsController**: GET standings, GET participant squad.
  - New rooms auto-link to the shared FPL pool.
  - **Tests (AuctionRoom.Tests)**: 19 xUnit tests against real Neon Postgres with fake clock
    covering swap money (refund+acquire), frozen-points inheritance, weekend gate (IST timezone),
    monthly cap, per-player cooldown, budget validation, ownership checks, and P2P unique-index
    handling. All pass. Test cleanup verified.
  - Clean build, zero warnings
- **Verified against Neon:** Full auction→standings→swaps chain with real FPL players and live DB.
  create room → host auto-join → second player joins → record auction buys → Holdings created →
  budgets decremented → GET standings ranks correctly → best-N excludes surplus slots →
  drill-down shows per-player contributions. Frozen-points proven by simulating a sync bump
  (Haaland 239→254 = +15, Bruno 235→255 = +20): standings re-ranked and only post-acquisition
  deltas counted. Swaps: refund+acquire budget math correct, frozen value inheritance confirmed,
  weekend/monthly-cap/cooldown gates enforced, P2P player exchange works (fixed unique-index bug).
  - **Frontend (`frontend/`)**: Vite 8 + React 19, plain CSS, `fetch` only — no
    extra runtime deps. `api.js` (`get`/`post` wrappers, `VITE_API_BASE`,
    `X-User-Id` sent from the stored session), `session.js` (localStorage
    identity: userId, roomCode, participantId, isHost), `App.jsx` (session-based
    routing: Home → console ⇄ standings ⇄ shortlist), `Home.jsx` (create or join
    a room), `AuctionConsole.jsx` (host records/undoes; everyone sees live
    budgets and squads, 4s poll; host gets a Shortlist button showing the count),
    `PlayerPicker.jsx` (debounced search over the room's auctionable slice, sold
    players filtered out, photo + age + club + position + price + last-season
    stats per player, injury badge, initials fallback via `PlayerPhoto`),
    `ShortlistManager.jsx` (host curates the auction pool: club/position/
    min-points filters, per-row toggle, bulk add/remove all shown),
    `Standings.jsx` (leaderboard, drill-down; accepts
    `initialCode`/`onBack` so a session flows into it), `SquadPanel.jsx`
    (per-slot contributions, best-N rows highlighted). Shared player-row
    primitives (`PlayerPhoto`, `StatusBadge`, `PlayerIdent`, `PlayerFigures`) in
    `player.jsx`, FPL vocabulary and formatting in `fpl.js`. Shared CSS
    (`.board`/`.slots` tables, `.error`, `.empty`, `.sr-only`, `.team-name`)
    lives in `index.css`; per-screen rules in `Home.css`, `AuctionConsole.css`,
    `ShortlistManager.css`, `Standings.css`. Keyboard reachable, `.sr-only`
    labels, `role="alert"` errors. Lint (oxlint) and production build both clean.
  - **CORS** in `Program.cs`: any loopback origin in Development, configured
    `Cors:AllowedOrigins` in production. `UseHttpsRedirection` is dev-exempt —
    a 307 breaks CORS preflight from the Vite dev server.
  - **Deploy scaffolding**: `Dockerfile` (multi-stage SDK 10 → aspnet 10, binds
    `$PORT`), `.dockerignore`, `render.yaml` (Docker web service, `/health`
    probe, secrets as `sync: false`), `.gitignore`, `frontend/.env.example`,
    `frontend/vercel.json`, and `DEPLOY.md` (step-by-step, env-var reference,
    known gaps). Not yet deployed — no Docker locally, so the image build is
    unverified.
- **Verified (standings page):** API on :5109 + Vite on :5173 against live Neon,
  in a two-participant demo room (since deleted). Standings JSON shape matched
  the components field-for-field; a simulated points bump (+122 across one squad)
  re-ranked the page and moved two benched slots into the best-11. Best-N
  boundary confirmed: 11 highlighted rows, 2 benched, counted contributions
  summing exactly to the score. Components server-rendered against the live
  payload (live squad / loading / error / empty-squad) with no crashes.
- **Verified (auction console):** Every DTO field path used by `AuctionConsole`,
  `PlayerPicker`, `Standings`, and `SquadPanel` checked against live API
  responses by a throwaway contract script — all passed (this guards the
  recurring failure mode in §12, 2026-08-07). Host/read-only split confirmed at
  both layers: the API returns 403 + `{error}` to a non-host on record *and*
  undo, and the UI gates the record form on `room.hostId === userId` (exercised
  as host, as viewer, and with no identity). Read-only GETs succeed with no
  `X-User-Id`; preflight allows `x-user-id`; success and error responses both
  carry `Access-Control-Allow-Origin`. Participant order stable across 6
  consecutive polls after the ordering fix. Undo returns 200 with clean JSON,
  applies the refund, empties `results`, then 400 "Nothing to undo."
  Backend suite 19/19 green; frontend lint and production build clean.
- **Deployed (2026-08-09).** API on Render at `https://auctionroom-api.onrender.com`
  (Docker, `development` branch — both hosts default to `main`, which does not exist
  here); frontend on Vercel at `https://auction-with-friends.vercel.app` with root
  dir `frontend` and `VITE_API_BASE` inlined at build time; `Cors__AllowedOrigins`
  set to the Vercel origin. Verified live: `/health` 200, `/api/pools` returns the
  573-player pool from Neon, admin endpoints 401 without the secret, GET returns 200
  rather than a 307, and preflight from the Vercel origin (including `x-user-id`)
  is allowed while a foreign origin is refused.
- **Cron jobs live (2026-08-09).** Both created at cron-job.org: the 22:00 IST sync
  (POST `/api/admin/sync`, `X-Sync-Secret`) and the 21:55 IST `/health` warm-up.
  Verified against the live deploy — `lastSyncedAt` advanced from a capped caller,
  which the old blocking endpoint could not have produced.
- **Phase 1 done (2026-08-10): player detail in the auction picker.** `Player` gained
  `BirthDate`, `PointsPerGame`, `Minutes`, `Starts`, `GoalsScored`, `Assists`,
  `NowCost`, `Status`, `News`; migration `AddPlayerDetailFields` applied to Neon and
  the pool re-imported to populate them. `PlayerPicker` now shows photo, club, age,
  position, price and last-season stats, with an initials fallback and an injury
  badge. Verified: all 573 rows populated (ages 16–40, the 17 known null birth dates,
  status split 514 a / 35 i / 15 d / 6 u / 3 s), every DTO field the component reads
  present in the live response, six render cases clean (including null age, missing
  photo and zero stats), 19/19 backend tests, lint and production build clean.
- **Phase 2 done (2026-08-11): room-scoped auction shortlist.** The host curates
  which slice of the pool a room auctions — 573 down to ~200 — without touching
  the shared pool or the swap market.
  - `ShortlistEntry` (RoomId, PlayerId, AddedAt) with a unique index on
    `(RoomId, PlayerId)`; migration `AddShortlistEntries` applied to Neon.
  - **ShortlistService**: `GetPlayerIdsAsync`, batch `ApplyAsync(add, remove)`
    (idempotent both ways, off-pool ids rejected, one audit event per edit),
    `IsAuctionableAsync` used as the gate in `AuctionService.RecordResultAsync`.
  - **ShortlistController**: GET `/api/rooms/{code}/shortlist` (public — the
    console reads it to label its button), GET `/shortlist/players` (the room's
    auctionable slice, filterable), POST `/shortlist` (host-only).
  - **PlayerQuery**: the search/filter/order expression shared by the pool
    endpoint and the room endpoint, so the picker and the shortlist screen can
    never drift apart. `GET /api/pools/{id}/clubs` added for the club dropdown.
  - **Frontend**: `ShortlistManager.jsx` + `.css` (club / position / min-points
    filters, per-row toggle, "Add all shown" / "Remove all shown", optimistic
    with inverse-delta revert), a fourth view in `App.jsx`, a Shortlist button
    in `AuctionConsole` carrying the live count, and `PlayerPicker` switched to
    the room-scoped endpoint. `fpl.js` split out of `player.jsx`.
- **Verified (shortlist):** three harnesses outside the repo, all green.
  46/46 API contract + behaviour assertions (filters, empty-means-unrestricted,
  host-only 403s that change nothing, curation round-trip, record gated off-list,
  atomic rejected batches, add+remove of the same id resolving to removed, bulk
  club add/remove, wipe re-opening the pool, 404s). 21/21 static server-renders
  against live payloads including 9 degenerate rows. 28/28 jsdom assertions with
  effects actually running — the shortlist fetch, the button it drives, the
  room-scoped picker search, bulk add/remove persisting all 577, and a refused
  viewer edit surfacing an error, reverting the optimistic toggle and leaving
  server state untouched. 19/19 backend tests, lint and production build clean.
  All fixtures were tagged `season: "test"` and torn down; the shared pool
  (1 pool, 577 players) and the 2 real rooms were verified intact afterwards.
- **Next:** Phase 3 (end auction → `War`), Phase 4 (host swap screen),
  Phase 5 (tests). Still outstanding on the user's side: rotate the Neon
  `neondb_owner` password and update it in Render + local
  `dotnet user-secrets`.

## 3. Finalized Requirements

### Auction recording (manual entry by host)
- Results shown to all participants **immediately**.
- Host can **undo the last recorded result** (1 step back).
- **Everyone always sees**: every participant's remaining budget + squad built so far.
- Validation: a participant cannot be assigned a player they can't afford.

### Teams & scoring
- Participants do **not** pick a playing XI.
- Each participant buys a fixed squad (e.g. 15).
- Standings score = **sum of the best 11 point-scorers** in their squad,
  auto-computed. Recalculated whenever points update.
- Whole-**tournament** cumulative, NOT per-gameweek resets.

### Points source
- Real fantasy points from the **official FPL API** (see §6).
- Whatever points a player earns in FPL is what they earn here. Scoring rules
  are fixed (we do not compute points ourselves; we ingest FPL totals).
- Updated once daily (~22:00) for completed matches. Not live per-match.

### Standings view
- One shared, always-updated table.
- Shows each participant's total + **each player's individual contribution**
  (drill-down into a participant's squad and per-player points).

### Mid-tournament Transactions (Swaps)

All post-auction ownership changes are **1-for-1 swaps** — squad size is always
kept at 15. Nobody can just drop a player or just add a player. Every
transaction involves releasing one owned player AND acquiring another.

**Two transaction types:**

| Type | Source | Target | Budget effect |
|---|---|---|---|
| P2P swap | Participant A → B | Participant B → A | Cash goes either way (host enters) |
| Unsold swap | Participant → unsold pool | Unsold pool → participant | Refund release + pay for acquire |

**Money rules:**
- Dropping a player **refunds their original purchase price** to budget.
- Acquiring a player costs the **host-entered price** (highest external bid).
- Net budget change = refund − cost. Can go positive (budget increases) or
  negative (budget decreases, must stay ≥ 0).
- Host records both credit deltas for P2P swaps (who pays whom how much).

**Rules:**
- Only the **host** enters transactions.
- Allowed **only on weekends** (IST / Asia/Kolkata, Sat–Sun).
- **Per-participant limit: max 1 transaction per calendar month** (any type).
- **Per-player cooldown: 1 month** from the swap date before re-swap.
- Both the per-participant cap and the per-player cooldown are enforced
  server-side.
- Negotiation/external bidding happens offline with a 3-day advance notice
  (unwritten rule among the group — not app-enforced).

**Edge cases:**
- Two participants claim same unsold player → second recording fails
  (player already owned).
- Budget would go below 0 → rejected.
- Participant already did a swap this month → rejected.
- Either player within 1-month cooldown → rejected.
- Participant ↔ participant swap still counts as 1 for each participant
  toward their monthly cap.

### Rooms
- Single room, up to 10 participants, for now.
- Eventually two room **types**: football and cricket (separate rooms).

### Auth & deployment
- Auth: **just a name** to join (optional simple username/password later).
- Deployment target: **Vercel**.

## 4. Feature Breakdown

### MVP (must have)
- Create room with a unique code; sport = football.
- Join room with a team name (no password required).
- Room config: budget, squad size (e.g. 15), best-N-for-scoring (e.g. 11),
  optional team constraints (max from same real club, position min/max).
- Select/import a player pool (FPL players — see §6).
- **Auction shortlist**: host optionally curates which slice of the shared pool
  this room auctions (filters by club / position / min points). Auction-only —
  it does not restrict swaps. Empty = the whole pool is auctionable.
- **Auction recording flow** (one-time event): next player → pick winner →
  enter price → confirm → budgets update → visible to all immediately.
- **Undo last recorded result** (1 step).
- Live view for all: every participant's remaining budget + squad so far.
- Daily points sync from FPL API.
- **Standings page (the daily-use page)**: login → rank, team name, total
  points (best-11 sum). Must load simply and fast.
- Drill-down: a participant's full squad with each player's contribution.
- **Host swaps** with weekend-only + 1-month-per-player cooldown rules.

### Nice to have
- Edit/fix an arbitrary past auction result (not just last).
- Per-gameweek breakdown per player.
- Export standings/squads (CSV).
- Simple username/password auth.

### Future
- IPL cricket as a separate room type (different pool + points source).
- Multiple seasons.
- Multiple concurrent rooms per user.

## 5. Tech Stack (LOCKED constraints + open hosting)

Hard constraints from user:
- **Backend: C#** (ASP.NET Core Web API).
- **Frontend: HTML + CSS + JS + React only.** No extra libraries unless
  absolutely necessary (so: **plain CSS, no Tailwind; no shadcn; no React
  Query / Redux** unless a real need appears — use `fetch` + React state).
- **Everything completely free.**
- Daily use = the standings page (login → see standing). Must be fast/simple.
- Auction is a **one-time** event, not daily.

### Backend
- **ASP.NET Core Web API** (.NET 10 LTS — installed SDK 10.0.302), C#.
- **ORM:** Entity Framework Core.
- **DB:** PostgreSQL (free tier) OR SQLite (file-based, zero-cost, simplest for
  a ≤10-user single-tournament app). Leaning **SQLite** for MVP — trivially
  free, no external DB service, easy backup. Revisit if a hosted free Postgres
  is preferred (see §11).
- **Scheduled FPL sync:** a hosted-service background timer inside the API
  (`IHostedService` / `PeriodicTimer`) running daily ~22:00. Only works if the
  host is always-on; on a sleeping free host, expose a protected sync endpoint
  triggered by a free external cron (e.g. cron-job.org) instead.

### Frontend
- **React** (plain, via Vite for the build), **plain CSS**, `fetch` for API.
- Deployed as a **static site** — this part CAN go on Vercel/Netlify/GitHub
  Pages for free.

### Hosting reality (IMPORTANT — Vercel can't host C#)
- **Vercel does not run .NET.** So the C# API must live elsewhere.
- **Split deployment:** React static site on Vercel/Netlify (free) + C# API on
  a free .NET host.
- Free .NET API host candidates (all with caveats):
  - **Render free web service** — sleeps after ~15 min idle; cold start ~30–60s.
    Fine for once-daily standings checks. No credit card.
  - **Azure App Service F1 free** — always some quota; limited CPU minutes.
  - **Google Cloud Run** — generous free tier, scales to zero (cold start).
  - **Fly.io** — small free allowance (verify current terms).
- **Cold start tradeoff:** since usage is once-daily, a sleeping free host is
  acceptable — first load may take ~30–60s, then fast. Decision pending (§11 Q1).

### Tradeoff note
- SQLite means the DB lives on the API host's disk. On hosts with ephemeral
  disk (Render free), data can be lost on redeploy/restart → then a free hosted
  Postgres (Neon/Supabase) is safer. This directly ties to the host choice.

## 6. Data Source — Official FPL API

Base: `https://fantasy.premierleague.com/api/` — free, **no API key**.

- `bootstrap-static/` — all players, teams, gameweeks; `total_points`,
  `event_points`, position (`element_type`), team, photo codes.
- `element-summary/{player_id}/` — per-gameweek history for a player.
- `fixtures/` — fixtures + results.

Position map: `1=GK, 2=DEF, 3=MID, 4=FWD`.
We **ingest FPL point totals**; we do not recompute scoring.
Sync job runs daily ~22:00, upserts player points, then recomputes standings
(best-11 sum per participant).

## 7. Database Schema (draft)

```
users            id, name, (email?, password_hash? later), created_at
rooms            id, code(unique), name, host_id, sport('football'),
                 season, status('setup'|'auction'|'active'|'completed'),
                 config JSONB, created_at
participants     id, room_id, user_id, team_name, budget_remaining,
                 total_points, best_xi_points, rank, last_swap_month (YYYY-MM),
                 joined_at
                 UNIQUE(room_id, user_id)
player_pools     id, name, sport, season, is_public, created_at
players          id, pool_id, external_id(FPL id), name, team, position,
                 photo_url, total_points, event_points, metadata JSONB
auction_results  id, room_id, player_id, participant_id, purchase_price,
                 sequence_number, created_at   UNIQUE(room_id, player_id)
shortlist_entries id, room_id, player_id, added_at   UNIQUE(room_id, player_id)
                 (empty for a room = whole pool auctionable)
swaps            id, room_id, type('ParticipantToParticipant'|'UnsoldPool'),
                 participant_id, counterparty_participant_id(nullable),
                 player_out_id, player_in_id,
                 refund_amount, acquire_price, net_budget_change,
                 swapped_at, cooldown_until, created_at
audit_events     id, room_id, event_type, user_id, data JSONB, created_at
```

Notes:
- `config` JSONB holds budget, squadSize, bestN (11), constraints, swap rules.
- Standings are **derived**: participant total = sum of top-N players'
  `total_points` among the players they currently own. Store the computed
  `best_xi_points` + `rank` on `participants` for fast reads; recompute on sync.
- Ownership changes only via `auction_results` (initial) and `swaps` (edits).
- `audit_events` records undo, swaps, and result edits for traceability.

## 8. API Structure (REST, Next.js Route Handlers)

```
POST   /api/rooms                         create room
GET    /api/rooms/:code                   room details + participants + config
POST   /api/rooms/:code/join              join (team name)
PATCH  /api/rooms/:code/status            host: setup->auction->active->completed

GET    /api/rooms/:code/auction           current player + remaining pool + results
POST   /api/rooms/:code/auction/record    host: {playerId, participantId, price}
POST   /api/rooms/:code/auction/undo      host: undo last result
GET    /api/rooms/:code/shortlist         shortlisted player ids + count + curated
GET    /api/rooms/:code/shortlist/players the room's auctionable slice (filterable)
POST   /api/rooms/:code/shortlist         host: {add[], remove[]} batch edit
POST   /api/rooms/:code/swaps             host: record a swap (weekend + cooldown checked)

GET    /api/rooms/:code/standings         leaderboard (derived best-N)
GET    /api/rooms/:code/participants/:id  squad + per-player contributions

GET    /api/player-pools                  list pools
GET    /api/pools/:id/clubs               distinct club names (filter dropdown)
POST   /api/player-pools/:id/import       import/refresh from FPL

POST   /api/cron/sync-fpl                 protected; daily ~22:00 (in-app timer
                                          or external free cron if host sleeps)
```

Endpoints are ASP.NET Core controllers/minimal APIs. Frontend calls them with
plain `fetch`. No WebSockets. During the one-time auction, clients poll with a
short `setInterval`; the daily standings page just fetches once on load.
Swap validation is server-side: reject if not a weekend, or if either player is
within its 1-month cooldown.

## 9. Key Real-time-ish Decisions
- **No WebSockets / no Redis.** Auction is manual host entry; ≤10 viewers.
  React Query polling gives "immediate enough" updates and stays serverless.
- Server is the single source of truth; every record/undo/swap is a REST call
  validated server-side (budget checks, ownership uniqueness).

## 10. Auth & Authorization
- Join by name → creates/reuses a `user`, issues a lightweight session
  (signed cookie / token) identifying the user + room.
- **Host-only** actions (record, undo, swap, status change) enforced by
  checking `rooms.host_id` against the session on the server.
- Optional username/password can be layered on later without schema upheaval.

## 11. Resolved Decisions (design LOCKED 2026-08-06)
1. **C# API host:** Render free web service (no card; cold start acceptable).
2. **DB:** Neon free Postgres (survives redeploys; safer than SQLite).
3. **Constraints:** deferred — not enforced in MVP; add later.
4. **Player pool:** import full FPL pool (~600); host chooses who to auction.
5. **Swap weekend timezone:** IST (Asia/Kolkata).
6. **Transactions:** 1-for-1 swaps only; purchase-price refund, host-entered buy
   price; budget can go up or down; weekly caps TBD per season.
7. **Undo:** last-1 only for MVP.

Design is finalized. No open blockers.

## 12. Decision Log
- 2026-08-06: Dropped live bidding → removed WebSocket/Redis.
- 2026-08-06: Chose official **FPL API** (free, no key) as points source.
- 2026-08-06: Scoring = **sum of best 11** owned players, tournament-cumulative.
- 2026-08-06: Auth = name-only for MVP.
- 2026-08-06: **Backend = C# / ASP.NET Core + EF Core** (user-mandated).
- 2026-08-06: **Frontend = React + plain CSS + fetch**, minimal deps (no
  Tailwind / React Query / state libs unless necessary).
- 2026-08-06: **Vercel cannot host .NET** → split deploy: React static on
  Vercel/Netlify (free) + C# API on a free .NET host (TBD).
- 2026-08-06: Auction is a **one-time** event; daily use is the standings page.
- 2026-08-06: Mid-tournament transactions: **1-for-1 swaps only, purchase-price
  refund, host-entered buy price.** Weekend-only. Per-participant cap: 1/month.
  Per-player cooldown: 1 month. Host-entered.
- 2026-08-06: **Design LOCKED** — Render (API) + Vercel/Netlify (React).
  DB: Neon Postgres. Constraints deferred. Full FPL pool. Weekends = IST.
  Transactions in MVP.
- 2026-08-06: **Scoring model = frozen-at-swap.** Each squad slot's value =
  `InheritedPoints + (player.TotalPoints − AcquisitionPoints)`. On acquisition
  (auction or swap-in) we snapshot the player's FPL total; on swap-out the slot's
  accrued value transfers as `InheritedPoints` to the incoming player. Only
  points earned *while owned* count. Best-N of the 15 slots = standings score.
  Ownership tracked in a dedicated **`Holding`** table (not reconstructed from
  `AuctionResult`/`Swap`, which stay immutable ledgers).
- 2026-08-06: **FPL pre-season data caveat.** The live FPL API currently serves a
  transitional 2026-27 dataset (scrambled team assignments, e.g. Semenyo→Man City;
  last-season carryover `total_points`). Our importer mirrors FPL faithfully — no
  bug on our side; the daily sync auto-corrects once real season data + 0-point
  resets land. Auction runs pre-season, so acquisition snapshots ≈ 0 in practice.
- 2026-08-07: **P2P swap slot exchange = delete-then-reinsert.** Repointing both
  `Holding` rows in one `SaveChanges` fails: the two slots trade players, so each
  one's target is still held by the other, and the unique index on
  `(RoomId, PlayerId)` is checked per row — EF rejects the pair as a circular
  dependency. Fix: remove both holdings, flush, then insert the two new slots,
  all inside the existing transaction so the transient "neither owned" state is
  never externally visible. The unsold-pool path is unaffected (its incoming
  player is unowned) and still mutates the row in place via `MoveSlot`.
- 2026-08-07: **Test strategy = real Neon + fake clock.** `SwapService` takes a
  `TimeProvider` so the weekend / monthly-cap / cooldown gates can be exercised on
  any day. Tests run against the real Postgres (not an in-memory provider) because
  the bug above is a genuine Postgres unique-index behaviour an in-memory store
  would not reproduce. Each test builds a disposable room + private player pool and
  tears it down, so the shared DB stays clean. `FakeClock.GetUtcNow()` normalizes to
  UTC — Npgsql refuses to write a non-zero-offset `DateTimeOffset` to `timestamptz`.
- 2026-08-07: **Frontend DTO shapes must be read from the source, not recalled.**
  The standings endpoint returns `StandingRow[]` (`score`, `participantId`), and
  the drill-down returns `ParticipantSquad` with a `squad` array whose slots use
  `contribution` / `countsTowardScore` / `inheritedPoints`. Field names written
  from memory were wrong twice; verify against `StandingsService.cs` records.
- 2026-08-07: **Dev CORS allows any loopback origin** rather than a hardcoded
  `http://localhost:5173`, because Vite picks another port when 5173 is taken.
  `UseHttpsRedirection` is skipped in Development for the same class of reason:
  its 307 fails preflight, so every browser call would break before reaching a
  controller.
- 2026-08-08: **Controllers never return EF entities.** `AuctionController.UndoLast`
  returned the raw `AuctionResult`; its `Room`/`Participant` navigations cycle, so
  `System.Text.Json` threw *after* the undo had already committed — the client saw
  a 500 for an action that succeeded, and a retry then failed with "Nothing to
  undo." Now DTO-mapped. Audited every other controller for the same shape; this
  was the only one.
- 2026-08-08: **List endpoints must sort explicitly.** `MapRoomResponse` returned
  participants in whatever order Postgres produced, and the console re-reads that
  list on a 4-second poll, so teams could reshuffle between refreshes. Ordered by
  `JoinedAt`. Any collection a polling client re-renders needs a deterministic
  order, not an incidental one.
- 2026-08-08: **No router in the frontend.** `App.jsx` picks the view from the
  stored session (`session.js`, localStorage). Three screens, no shareable URLs
  yet, so a routing library would cost more than it saves. Standings stays
  reachable without a session because it is the daily-use page — checking the
  table should not require rejoining the room.
- 2026-08-08: **Host-only is enforced twice, on purpose.** The API is the gate
  (`CallerContext` → 403 on record/undo/swap); the console's `room.hostId ===
  userId` check only decides whether to render the controls. The client check is
  affordance, not security — a viewer with a spoofed `X-User-Id` still gets 403.
- 2026-08-09: **API ships as a Docker image.** Render has no native .NET runtime,
  so `Dockerfile` (SDK 10 build → aspnet 10 runtime) lives at the repo root — the
  build context must be the root because Api references Domain/Infrastructure by
  relative path. The entrypoint is shell-form so `${PORT}` expands at container
  start; Render assigns the port at runtime, not build time.
- 2026-08-09: **`UseForwardedHeaders` is required on Render.** Render terminates
  TLS at its edge and forwards plain HTTP, so `Request.IsHttps` is false and
  `UseHttpsRedirection` would 307 *every* request — including CORS preflight,
  which browsers do not follow. That is the same failure that made
  `UseHttpsRedirection` dev-exempt, but in production. `KnownIPNetworks`/
  `KnownProxies` are cleared because a PaaS proxy address is not known ahead of
  time. (`KnownNetworks` is deprecated in .NET 10 — use `KnownIPNetworks`.)
- 2026-08-09: **Config must survive env-var flattening.** Host dashboards only
  set flat string keys, and an env var cannot express a JSON array, so
  `Cors:AllowedOrigins` now also accepts a single comma-separated value in
  addition to the indexed `Cors__AllowedOrigins__0` form. Relatedly, the
  connection-string fallback tests `IsNullOrWhiteSpace` rather than `??`:
  `appsettings.json` ships an empty `"Default"` placeholder, which `??` accepts,
  handing Npgsql an empty string that fails at first query instead of at startup.
  Production now throws at startup with a message naming the variable to set.
- 2026-08-09: **`/health` deliberately does not touch the database.** It is a
  liveness probe for Render; if it queried Neon, a transient DB hiccup would make
  Render tear down an otherwise healthy container.
- 2026-08-09: **Both admin endpoints share one auth gate.** `POST /api/admin/import-fpl`
  was unauthenticated while `/sync` checked `X-Sync-Secret` — but both run the *same*
  full pool import, so the unprotected one was a way around the protected one. Rather
  than copy the check, the comparison moved into a private `CheckSyncSecret()` helper
  returning `IActionResult?` (null = authorized), and both actions call it. One
  mechanism, so a future change to the auth scheme cannot leave one endpoint behind.
  Verified: no header, wrong secret, and empty header all return 401 on both;
  correct secret returns 200 on both.
- 2026-08-09: **The daily sync is fire-and-forget; the manual import is not.**
  cron-job.org's free plan caps a request at 30s, but the import takes minutes —
  and the failure is worse than a truncated response: the request's
  `CancellationToken` is threaded all the way into `FplService.ImportPoolAsync`,
  which does one closing `SaveChangesAsync(ct)`, so a client disconnect cancels the
  save and writes *nothing*. A capped cron would therefore never sync, silently.
  `POST /api/admin/sync` now returns `202` immediately and runs the import on a
  detached `Task` with `CancellationToken.None`, resolving `FplService` from a fresh
  `IServiceScope` — the request scope, and the scoped `AuctionDbContext` inside it,
  are disposed the moment the response returns. `import-fpl` stays synchronous on
  purpose: something has to report the `playerCount`, and a manual caller can wait.
  A static `Interlocked` flag keeps the two from overlapping (409 on `import-fpl`,
  `already-running` on `sync`); one instance on the free plan makes that sufficient.
  Verified: 202 in **5ms** against a 30s cap, `lastSyncedAt` advanced and
  "573 players" logged *after* the connection closed, flag reset for the next run.
- 2026-08-09: **Cold start, not import time, is what the cron timeout must cover.**
  With the import detached, the only work inside the request is the secret check. But
  Render's free tier sleeps after ~15 min idle, so the 22:00 job wakes a cold
  container and that alone can approach 30s. `DEPLOY.md` documents an optional
  21:55 `/health` warm-up job; a timed-out warm-up still works, because Render boots
  the container whether or not the caller waits.
- 2026-08-09: **Known inefficiency — the import is N+1.** `FplService` issues one
  `FirstOrDefaultAsync` per player (`FplService.cs:70`), so ~573 sequential
  round-trips to Neon; that, not the FPL fetch, is the bulk of the runtime (~162s
  from Render, ~5s from a local machine on a warm cache — the gap is per-query
  latency). Correctness is unaffected and the background sync makes the duration
  invisible to callers, so this is deliberately left alone. If it ever needs fixing,
  load the pool's players into a dictionary keyed by `ExternalId` in one query and
  match in memory.
- 2026-08-10: **FPL's `photo` field lies about the extension.** It reads
  `"223094.jpg"`, but the CDN only serves `.png` — every `PhotoUrl` we had written
  since the importer existed returned 403. Nothing rendered a photo until the
  auction picker did, so the bug sat unnoticed for the whole build. The importer now
  swaps the extension via `Path.GetFileNameWithoutExtension`. A minority of players
  have no asset at any size and 403 regardless, so `PlayerPhoto` falls back to
  initials on `onError` rather than trusting the URL to resolve. Sampled 19 stored
  URLs after the fix: 16 × 200, 3 × 403 (all genuinely asset-less).
- 2026-08-10: **The picker requests a smaller photo than the API stores.** FPL's
  "110x140" asset is really 220×280 at ~93KB, and a search renders 25 rows, so list
  rows rewrite the URL to the 40x40 variant (~14KB, an 80×80 square crop) while the
  selected-player card keeps the full size. The rewrite is a plain string replace
  that no-ops if the URL shape ever changes, so a CDN path change degrades to
  heavier images rather than broken ones.
- 2026-08-10: **Age is derived on read, never stored.** `Player.BirthDate` holds the
  date and `PoolsController` computes whole years per request, so a stored age cannot
  silently go stale between syncs. The birthday adjustment does not translate to SQL,
  so the projection materializes first and maps in memory — the same reason the
  endpoint no longer projects straight into `PlayerResponse`.
- 2026-08-10: **The FPL feed is parsed defensively, because one bad row would cost a
  whole night's sync.** `points_per_game` arrives as a string and is parsed with
  `InvariantCulture` (a comma-decimal locale reads "4.4" as 44); `birth_date` is null
  for 17 players and an unparseable value is treated as absent rather than throwing;
  `starts` is modelled as `int?` so a missing field on a single element cannot fail
  the whole deserialize. `News` is deliberately left unbounded — a length cap would
  throw at `SaveChanges` on a wordy injury note and abort the entire import.
- 2026-08-11: **The shortlist is auction-scoped, not a room universe.** Per the
  user: *"its not my universe, its just for the auction not the room… if a player
  wants to swap with someone of a smaller team, his choice."* So
  `ShortlistEntry` gates exactly one operation — `AuctionService.RecordResultAsync`
  — and `SwapService` was left untouched: an unsold player who was never
  shortlisted is still a legal swap target. Room-scoped rather than pool-scoped
  because the FPL pool is shared and public; two rooms must be able to auction
  different slices of it.
- 2026-08-11: **An empty shortlist means unrestricted, not "nothing auctionable".**
  Rooms created before Phase 2 have no rows, and a host who never opens the screen
  should still be able to run an auction — the opposite default would brick the
  feature the day it shipped. `IsAuctionableAsync` therefore probes "is this room
  curated at all?" first and only then checks membership, which also makes
  *emptying* the list a deliberate way to re-open the whole pool. The cost is that
  "curated but every player removed" is not expressible; that state has no use.
- 2026-08-11: **One query expression, two endpoints.** The picker now reads
  `GET /api/rooms/{code}/shortlist/players` instead of the pool endpoint, but both
  must search, filter and order identically or the shortlist screen would show a
  player the picker then hides. The shared predicate lives in `PlayerQuery` and is
  composed into both; the room endpoint adds a correlated `EXISTS` against
  `ShortlistEntries` rather than materializing ids into an `IN` list, because a
  curated room can hold ~200 of them and Npgsql would send ~200 parameters per
  keystroke of a debounced search.
- 2026-08-11: **Shortlist edits persist per action, with an inverse-delta revert.**
  Curating 573 rows means a lot of clicks, so each toggle and each bulk action is
  its own request and there is no save button to forget. A refused request undoes
  itself by applying the *inverse delta*, not by restoring a snapshot of the set:
  clicking down a long list leaves several requests in flight, and a snapshot taken
  before one of them would wipe out the others when restored. The deltas are always
  disjoint from current membership, so the inverse is exact.
- 2026-08-11: **`fpl.js` exists so `player.jsx` exports only components.** Vite's
  fast refresh silently stops working for a module that mixes components with other
  exports — oxlint's `react(only-export-components)` caught it the moment the shared
  primitives were extracted. The FPL vocabulary (position names, status labels,
  price formatting) moved to `fpl.js`, which the shortlist screen wants anyway
  despite rendering no player row.
- 2026-08-11: **UI verification needs a DOM with effects running, and it must poll.**
  A static `renderToStaticMarkup` pass cannot reach the code Phase 2 added — the
  shortlist fetch, the button label it drives, the room-scoped search — because all
  of it lives in effects. The jsdom harness that does reach it initially failed on
  fixed `settle()` sleeps racing a 250 ms debounce plus a network round-trip; every
  assertion now waits on a condition instead. Two of its failures were the test's
  fault, not the product's, and both are worth remembering: an assertion that ran
  before a fetch resolved counted the empty-state `<li>` as a result row, and the
  "revert" check assumed an unticked starting row when the fixture's first three
  rows were the three already-shortlisted players (both lists order points-desc).
  Optimistic state has to be observed inside a synchronous `act()` — the default
  click helper's 400 ms settle is long enough for a localhost 403 to arrive and
  revert it first.


