# Set Neon Connection Strings
# Run this after you get your connection strings from Neon console
# Usage: .\setup-neon.ps1

Write-Host "=== Neon PostgreSQL Setup ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "You'll need TWO connection strings from Neon:" -ForegroundColor Yellow
Write-Host "  1. Pooled (with -pooler in hostname) - for the running app"
Write-Host "  2. Direct (no -pooler) - for migrations"
Write-Host ""
Write-Host "Get them from: https://console.neon.tech"
Write-Host "  -> Your project -> Connect -> toggle 'Connection pooling' on/off"
Write-Host ""

$pooled = Read-Host "Paste the POOLED connection string"
$direct = Read-Host "Paste the DIRECT connection string"

if ([string]::IsNullOrWhiteSpace($pooled) -or [string]::IsNullOrWhiteSpace($direct)) {
    Write-Host "Error: Both strings are required." -ForegroundColor Red
    exit 1
}

# Convert PostgreSQL URI to Npgsql key-value format
function Convert-NeonUri {
    param([string]$uri)

    if ($uri -match 'postgresql://([^:]+):([^@]+)@([^/]+)/(.+)') {
        $user = $matches[1]
        $pass = $matches[2]
        $host = $matches[3]
        $db = $matches[4]

        # Strip query params from db name if present
        if ($db -match '([^?]+)') {
            $db = $matches[1]
        }

        return "Host=$host;Database=$db;Username=$user;Password=$pass;SSL Mode=Require;Trust Server Certificate=true"
    }

    # Already in key-value format, just ensure SSL
    if ($uri -notmatch "SSL Mode") {
        return "$uri;SSL Mode=Require;Trust Server Certificate=true"
    }
    return $uri
}

$pooledConverted = Convert-NeonUri $pooled
$directConverted = Convert-NeonUri $direct

Write-Host ""
Write-Host "Setting user secrets..." -ForegroundColor Cyan

Push-Location src/AuctionRoom.Api
try {
    dotnet user-secrets set "ConnectionStrings:Default" $pooledConverted
    dotnet user-secrets set "ConnectionStrings:Direct" $directConverted

    Write-Host ""
    Write-Host "✓ Connection strings saved to user secrets!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Next steps:" -ForegroundColor Yellow
    Write-Host "  1. Run migrations: dotnet ef database update --project ../AuctionRoom.Infrastructure"
    Write-Host "  2. Start the API: dotnet run"
    Write-Host ""
}
finally {
    Pop-Location
}
