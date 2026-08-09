# Render has no native .NET runtime, so the API ships as a container.
# Build context is the repo root (the Api project references Domain and
# Infrastructure by relative path, so a narrower context would not restore).

# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Copy only the project files first: this layer is cached and skips a full
# restore on every source-only change.
COPY src/AuctionRoom.Api/AuctionRoom.Api.csproj                   src/AuctionRoom.Api/
COPY src/AuctionRoom.Domain/AuctionRoom.Domain.csproj             src/AuctionRoom.Domain/
COPY src/AuctionRoom.Infrastructure/AuctionRoom.Infrastructure.csproj src/AuctionRoom.Infrastructure/
RUN dotnet restore src/AuctionRoom.Api/AuctionRoom.Api.csproj

# Now the sources. Tests are excluded by .dockerignore — they need a live Neon
# database and have no place in a runtime image.
COPY src/ src/
RUN dotnet publish src/AuctionRoom.Api/AuctionRoom.Api.csproj \
    -c Release \
    -o /app \
    --no-restore

# ---------- runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# Render injects PORT and routes to it; 8080 is the fallback for `docker run`
# locally. Shell form so ${PORT} expands at container start, not at build time.
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["sh", "-c", "dotnet AuctionRoom.Api.dll --urls http://+:${PORT:-8080}"]
