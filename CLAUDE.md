# AuctionRoom — Fantasy Football Auction & Standings Platform

> This file is the single source of truth for architecture and decisions.
> Keep it updated as decisions change. Last updated: 2026-08-09.

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

- **Phase:** Phase 6 (auction console frontend) complete + verified against the live
  API and Neon. Phases 4 (backend) and 5 (standings page) complete and verified before it.
  **The MVP frontend is feature-complete**; what remains is deployment (§5).
- **What exists:**
  - Solution scaffold: Api (.NET 10), Domain, Infrastructure, Tests
  - All domain entities with updated transaction model (1-for-1 swaps, refund+acquire money)
  - EF Core DbContext + 3 migrations applied to Neon: InitialCreate, AddHoldings, AddHoldingAcquisitionPrice
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
  - **AdminController**: POST import-fpl, POST sync — both gated by the same
    `X-Sync-Secret` check (`SYNC_SECRET` env var) via a shared `CheckSyncSecret` helper.
  - **PoolsController**: GET pools, GET pool players (search/position/take).
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
    routing: Home → console ⇄ standings), `Home.jsx` (create or join a room),
    `AuctionConsole.jsx` (host records/undoes; everyone sees live budgets and
    squads, 4s poll), `PlayerPicker.jsx` (debounced pool search, sold players
    filtered out), `Standings.jsx` (leaderboard, drill-down; accepts
    `initialCode`/`onBack` so a session flows into it), `SquadPanel.jsx`
    (per-slot contributions, best-N rows highlighted). Shared primitives
    (`.board`/`.slots` tables, `.error`, `.empty`, `.sr-only`, `.team-name`)
    live in `index.css`; per-screen rules in `Home.css`, `AuctionConsole.css`,
    `Standings.css`. Keyboard reachable, `.sr-only` labels, `role="alert"`
    errors. Lint (oxlint) and production build both clean.
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
- **Next:** Deploy (follow `DEPLOY.md`) — Render blueprint first, then Vercel
  with root dir `frontend`, then set `Cors__AllowedOrigins` to the Vercel origin
  and redeploy, then the cron-job.org daily sync.

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
POST   /api/rooms/:code/swaps             host: record a swap (weekend + cooldown checked)

GET    /api/rooms/:code/standings         leaderboard (derived best-N)
GET    /api/rooms/:code/participants/:id  squad + per-player contributions

GET    /api/player-pools                  list pools
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


