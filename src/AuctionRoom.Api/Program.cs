using AuctionRoom.Api.Services;
using AuctionRoom.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// --- Database ---
// appsettings.json ships an empty "Default" placeholder, so test for whitespace
// rather than null: `??` would happily accept "" and hand Npgsql an empty
// connection string, failing at first query instead of at startup.
var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString))
    connectionString = Environment.GetEnvironmentVariable("AUCTIONROOM_DB");
if (string.IsNullOrWhiteSpace(connectionString))
{
    connectionString = builder.Environment.IsDevelopment()
        ? "Host=localhost;Port=5432;Database=auctionroom;Username=postgres;Password=postgres"
        : throw new InvalidOperationException(
            "No database connection string. Set ConnectionStrings__Default (or " +
            "AUCTIONROOM_DB) in the host environment.");
}

builder.Services.AddDbContext<AuctionDbContext>(o =>
    o.UseNpgsql(connectionString));

// --- Application services ---
builder.Services.AddSingleton(TimeProvider.System);
// Injected for the same reason as TimeProvider: a weighted nomination draw has
// to be reproducible in a test, which means the RNG cannot be a `new Random()`
// buried in the service. Random.Shared rather than a fresh instance because
// Random's instance methods are not thread-safe and this is a singleton.
builder.Services.AddSingleton(Random.Shared);
builder.Services.AddHttpClient<FplService>();
builder.Services.AddScoped<RoomService>();
builder.Services.AddScoped<AuctionService>();
builder.Services.AddScoped<NominationService>();
builder.Services.AddScoped<RoomLifecycleService>();
builder.Services.AddScoped<StandingsService>();
builder.Services.AddScoped<SwapService>();
builder.Services.AddScoped<ShortlistService>();
builder.Services.AddScoped<CallerContext>();

// --- CORS ---
// The React app is a separate origin (Vite dev server locally, a static host in
// production), so browser calls need explicit allowance. Allowed origins come
// from config so the deployed frontend URL is not baked into code:
//   "Cors": { "AllowedOrigins": [ "https://auctionroom.vercel.app" ] }
const string FrontendCors = "frontend";
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? [];

// An env var cannot express a JSON array, and host dashboards only set flat
// keys. Accept either: Cors__AllowedOrigins__0 binds to string[], and
// Cors__AllowedOrigins as a single comma-separated value covers the case
// where only one frontend origin exists.
if (allowedOrigins.Length == 0)
{
    allowedOrigins = (builder.Configuration["Cors:AllowedOrigins"] ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

builder.Services.AddCors(o => o.AddPolicy(FrontendCors, policy =>
{
    if (builder.Environment.IsDevelopment())
    {
        // Any localhost port: Vite picks a different one if 5173 is taken.
        policy.SetIsOriginAllowed(origin =>
                Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.IsLoopback)
            .AllowAnyHeader()
            .AllowAnyMethod();
    }
    else
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    }
}));

// --- Controllers + OpenAPI ---
builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Render terminates TLS at its edge and forwards plain HTTP to the container.
// Without this, Request.IsHttps is false, so UseHttpsRedirection below answers
// *every* request with a 307 — including the CORS preflight, which browsers do
// not follow. That would break every call from the deployed frontend, the same
// way it breaks the Vite dev server locally. The proxy's address is not known
// ahead of time on a PaaS, so the default network allow-list is cleared.
if (!app.Environment.IsDevelopment())
{
    app.UseForwardedHeaders(new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor,
        KnownIPNetworks = { },
        KnownProxies = { },
    });

    app.UseHttpsRedirection();
}

app.UseCors(FrontendCors);

// Cheap liveness probe for the host's health check. Deliberately does not touch
// the database: a Neon hiccup should not make Render tear down a healthy app.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapControllers();
app.Run();
