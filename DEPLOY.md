# Deploying AuctionRoom

Split deployment, both on free tiers: the React app is a static site, the C# API
is a container, and Neon holds the data.

    Vercel (static React)  ──HTTPS──▶  Render (Docker, ASP.NET Core)  ──▶  Neon Postgres
                                            ▲
                                   cron-job.org (daily FPL sync)

**Order matters.** Each side needs the other's URL, so deploy the API first,
then the frontend, then come back and set CORS.

---

## 0. Prerequisites

- The repo pushed to GitHub (Render and Vercel both deploy from a git remote).
- Your Neon **pooled** connection string, in Npgsql form:

      Host=<host>;Database=<db>;Username=<user>;Password=<pw>;SSL Mode=Require;Trust Server Certificate=true

  Neon shows a `postgresql://` URL — `setup-neon.ps1` has the conversion logic
  if you need a reminder of the shape.

Never commit this string. `.gitignore` covers `.env*` and `appsettings.*.Local.json`,
and `appsettings.json` ships empty placeholders on purpose.

---

## 1. API on Render

1. Render → **New → Blueprint**, select the repo. It reads `render.yaml` and
   proposes a Docker web service named `auctionroom-api`.
2. Fill in the two `sync: false` env vars when prompted:
   - `ConnectionStrings__Default` → the Neon pooled string from step 0.
   - `Cors__AllowedOrigins` → leave blank for now; you get the Vercel URL in step 2.
   - `SYNC_SECRET` is generated for you. Copy it — step 3 needs it.
3. Deploy. First build takes a few minutes (it restores and publishes in the image).
4. Check it: `https://<your-service>.onrender.com/health` → `{"status":"ok"}`.

Note the free plan sleeps after ~15 minutes idle, so the first request after a
quiet spell takes ~30–60s. That was an accepted tradeoff (CLAUDE.md §5).

### Database schema

The image does **not** run EF migrations at startup. The Neon database already
has all three migrations applied, so nothing is needed for this deployment. If
you add a migration later, apply it from your machine against the Neon
connection before deploying the code that expects it:

    dotnet ef database update --project src/AuctionRoom.Infrastructure --startup-project src/AuctionRoom.Api

---

## 2. Frontend on Vercel

1. Vercel → **Add New → Project**, select the repo.
2. Set **Root Directory** to `frontend`. This is the one setting that is not in
   `vercel.json`, and the build fails without it.
3. Add an environment variable:
   - `VITE_API_BASE` = `https://<your-service>.onrender.com` (no trailing slash)
4. Deploy, and note the resulting URL.

Vite inlines `VITE_*` at build time. If you change `VITE_API_BASE` later you
must **redeploy** — editing it in the dashboard alone changes nothing.

---

## 3. Close the loop

1. Back on Render, set `Cors__AllowedOrigins` to the exact Vercel origin, e.g.
   `https://auctionroom.vercel.app` — no trailing slash, exact match. Redeploy.
2. Open the Vercel URL and load a room. If the browser console shows a CORS
   error, the origin string does not match character-for-character.

Vercel gives every deployment its own preview URL. Those origins are *not* in
the allow-list, so preview builds cannot call the API unless you add them.

---

## 4. Daily FPL sync

The API has no always-on scheduler (a sleeping free host cannot run one), so an
external cron drives it.

At [cron-job.org](https://cron-job.org) (free), create a job:

- **URL:** `https://<your-service>.onrender.com/api/admin/sync`
- **Method:** `POST`
- **Header:** `X-Sync-Secret: <the SYNC_SECRET value from step 1>`
- **Schedule:** daily, ~22:00 IST
- **Timeout:** whatever the plan allows — the free plan's 30s cap is fine.

`/api/admin/sync` answers `202 Accepted` in milliseconds and runs the import in
the background, so the cron's timeout no longer has to cover the import. It
does still have to cover Render waking up: the service is asleep at 22:00, and
a cold start can approach 30s. If a night's job reports a timeout, the wake-up
was slow — the retry a minute later lands on a warm service. Set the job to
retry on failure if the plan offers it.

**A `202` means accepted, not finished.** Confirm the import actually landed by
checking `lastSyncedAt` on `GET /api/pools`:

    curl -s https://<your-service>.onrender.com/api/pools

To watch a sync end to end by hand, use `POST /api/admin/import-fpl` instead —
same secret, but it blocks until done and returns the `playerCount`. Give curl
a long `-m` (the import runs for minutes against Neon).

### Optional: warm-up job

To take the cold start out of the equation, add a second free cron job hitting
`GET /health` at ~21:55 IST, five minutes before the sync. By 22:00 the service
is awake (Render only sleeps after ~15 min idle) and `/sync` answers instantly.

The warm-up may itself report a timeout, which is harmless — Render starts the
container as soon as the request arrives and finishes booting whether or not the
caller is still waiting. Waking the service is the whole job.

---

## Environment variable reference

| Variable | Where | Purpose |
|---|---|---|
| `ConnectionStrings__Default` | Render | Neon pooled connection string |
| `Cors__AllowedOrigins` | Render | Exact frontend origin(s). Indexed form `Cors__AllowedOrigins__0`, `__1` for several |
| `SYNC_SECRET` | Render | Shared secret for `X-Sync-Secret` on both `/api/admin/sync` and `/api/admin/import-fpl` |
| `PORT` | Render | Injected automatically; the container binds to it |
| `VITE_API_BASE` | Vercel | API base URL, inlined at build time |

---

## Known gaps

- `SYNC_SECRET` falls back to the literal `"change-me-in-production"` when unset.
  `render.yaml` generates a real value, so this only bites a deploy that skips
  the blueprint.
