# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project context

This is a .NET Full-Stack training capstone template (TripPlanner). `ASSIGNMENT.md` maps
the official feature list (Destination Suggestion, Destination Details, Trip Planner,
Auth) to user stories, priorities, and acceptance criteria per file — check it before
assuming a feature is unimplemented or out of scope. `README.md` has the getting-started
walkthrough and the "where to write your code" file map. `TECHNICAL_SPEC.md` is the
authoritative deep reference: Part A documents exactly what the current code does
(tagged `[Observed]`/`[Inferred]`), Part B is the full design spec, business rules, and
phase-by-phase implementation roadmap for the unimplemented features — consult it before
making a design decision (caching strategy, day-regeneration algorithm, sort-order
semantics, etc.) that isn't spelled out in ASSIGNMENT.md. Feature 4 (Auth) is the
complete reference slice; the rest are stubs (`NotImplementedException` / 501) the
student implements incrementally.

## Commands

### Backend (run from `backend/`)

```bash
dotnet restore
dotnet build
dotnet run --project src/TripPlanner.WebApi      # API at http://localhost:5080, Swagger at /swagger

# Run all tests
dotnet test

# Run a single test class
dotnet test --filter "FullyQualifiedName~TripServiceTests"

# EF Core migrations (run from repo root or backend/)
dotnet ef migrations add <MigrationName> \
  --project src/TripPlanner.Infrastructure \
  --startup-project src/TripPlanner.WebApi
```

### Frontend (run from `frontend/`)

```bash
npm install
npm run dev        # Dev server at http://localhost:5173
npm run build      # Type-check + Vite build
npm run lint       # tsc --noEmit + ESLint (flat config in eslint.config.js)
```

### Quick start (both servers, from repo root)

```bash
start-dev.bat      # Windows cmd
./dev.ps1          # PowerShell (two windows)
./dev.sh           # macOS/Linux
```

The scripts install frontend deps and create `frontend/.env` on first run.

## Architecture

### Clean Architecture layers (backend)

```
WebApi ──▶ Application ──▶ Domain
   │            ▲
   └──▶ Infrastructure ──┘
```

- **Domain** — entities (`Trip`, `ItineraryDay`, `ItineraryItem`, `Destination`, `User`). No framework dependencies.
- **Application** — service interfaces (`ITripService`, `IAuthService`, etc.) and implementations. Defines `IApplicationDbContext`, `IPasswordHasher`, `IJwtTokenGenerator`, `ICurrentUserService`, `IDestinationProvider`. Depends only on Domain.
- **Infrastructure** — EF Core `ApplicationDbContext`, JWT token generation, BCrypt, `GeoapifyClient`. Implements Application interfaces.
- **WebApi** — controllers, middleware (`ExceptionHandlingMiddleware`), DI composition root. Registers all layers via `AddApplication()` / `AddInfrastructure()`.

### Key patterns

- **DI registration**: each layer exposes an extension method (`AddApplication()`, `AddInfrastructure()`) — `Program.cs` stays small.
- **Current user**: inject `ICurrentUserService` and call `GetRequiredUserId()`. Never trust a resource ID alone — always filter by `UserId`.
- **Exceptions**: throw from the Application layer; `ExceptionHandlingMiddleware` maps them to HTTP status codes:
  | Exception | HTTP |
  |---|---|
  | `ValidationException` / `DomainException` | 400 |
  | `UnauthorizedException` | 401 |
  | `ForbiddenException` | 403 |
  | `NotFoundException` | 404 |
  | `ConflictException` | 409 |
  | `NotImplementedException` | 501 |
- **Reference slice**: `AuthService.cs` + `AuthController.cs` are the canonical worked example. Study them before implementing other features.
- **Destination caching**: `Destination` rows are a cache of external-provider data (Geoapify), keyed by a unique index on `ProviderId` (`DestinationConfiguration.cs`). `DestinationService` (search/details) never persists rows — only `TripService.AddDestinationAsync` upserts one, on first add to any trip. Do not reintroduce a "must already exist" lookup there; a fresh `ProviderId` is expected and should upsert, not 404.
- **Concurrency via unique index + catch/retry**: this codebase enforces invariants that matter under concurrent requests as DB-level unique indexes (`User.Email`, `Destination.ProviderId`, `(ItineraryDayId, DestinationId)`) rather than app-level locking. Read-then-write code against one of these (e.g. upserting a `Destination`) must catch `DbUpdateException` and retry/re-fetch — see `TripService.AddDestinationAsync` for the pattern. EF Core's InMemory provider (used in tests) does **not** enforce these indexes, so this path is untested by `dotnet test` and must be reasoned about directly against the SQL provider. `ItineraryItem.SortOrder` is *not* a DB-level unique index — ordering is enforced purely in-memory via dense resequencing per request (`TripService.Resequence`), so two concurrent reorders of the same day/bucket race without a DB-level guard; this is a known gap, not an oversight.

### Feature layout (backend)

```
Application/Features/
  Auth/               ← reference implementation (complete)
  Destinations/       ← DestinationService.cs (student impl)
  Trips/              ← TripService.cs (student impl)
```

Each feature folder holds: the service implementation, an interface, and a `Dtos/` subfolder.

### Frontend structure

```
src/
  api/         ← axios wrappers: client.ts (base instance), auth.ts, destinations.ts, trips.ts
  auth/        ← AuthContext (JWT storage, reactive logout on 401), ProtectedRoute
  features/    ← page components grouped by feature (auth, destinations, trips — all implemented)
  types.ts     ← shared TypeScript types for every DTO (auth, destination, trip)
  App.tsx      ← React Router route definitions
```

HTTP client (`src/api/client.ts`) is a configured Axios instance whose JWT interceptor
attaches `Authorization: Bearer <token>` when present, and exposes `getErrorMessage(err,
fallback)` — the shared helper every feature page uses to turn a caught error into the
backend's `ProblemDetails.detail` (or a fallback string). `destinations.ts` and `trips.ts`
follow `auth.ts`'s pattern: thin typed wrappers over `apiClient`, no raw `fetch`/`axios`
calls in components. `types.ts` covers `User`/`AuthResponse` plus every destination/trip
DTO consumed by the feature pages — add new shared shapes here rather than declaring
ad-hoc inline interfaces in components.

### Testing pattern (xUnit)

Tests use an **in-memory EF Core database** (new `Guid` database name per test) and **Moq** for interfaces. See `AuthServiceTests.cs` as the reference:

```csharp
private static ApplicationDbContext CreateDb() =>
    new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
        .Options);
```

Arrange with a fresh `CreateDb()`, construct the service under test with real implementations where cheap (e.g. `BCryptPasswordHasher`) and `Mock<T>` for everything else.

## Configuration

- API URL: `http://localhost:5080`; frontend `.env` → `VITE_API_BASE_URL=http://localhost:5080/api`
- JWT settings in `appsettings.json` under `"Jwt"` (Key, Issuer, Audience)
- Database defaults to SQLite (`tripplanner.db` in the WebApi folder). Switch to PostgreSQL by setting `"Database": { "Provider": "Postgres" }` in `appsettings.Development.json` and running `docker compose up -d`.
- Destination browse-path caching (`DestinationService`) defaults to an in-process `IDistributedCache`. Switch to Redis by setting `"Cache": { "Provider": "Redis" } }` in `appsettings.Development.json` and running `docker compose up -d`; the connection string comes from `.env` (`ConnectionStrings__Redis`), not `appsettings.json`.
- Migrations apply automatically on startup via `ApplyMigrationsAsync` in `Program.cs`.
- All environment-specific URLs and secrets are git-ignored, local-only values loaded from
  `backend/src/TripPlanner.WebApi/.env` via `DotNetEnv.Env.Load()` in `Program.cs` — copy
  `.env.example` to `.env` and fill in real values. This includes not just the Geoapify/Serper
  API keys and SMTP credentials, but every URL that previously lived in `appsettings.json`:
  `Cors__AllowedOrigins__0`, `App__FrontendBaseUrl`, `Geoapify__BaseUrl`, `Serper__BaseUrl`,
  and `ConnectionStrings__Postgres`. `appsettings.json` only keeps the SQLite connection
  string and non-URL structural defaults (e.g. `Smtp:Host`/`Smtp:Port`). Nested config keys
  use `__` as the separator (e.g. `Geoapify__ApiKey`), since `.env` values become process
  environment variables and ASP.NET Core's `AddEnvironmentVariables()` treats `__` as the
  section delimiter; array elements use a trailing index (`Cors__AllowedOrigins__0`).
  Every one of these has a matching `?? "localhost default"` fallback in code, so the app
  still runs with no `.env` overrides at all — see `AppUrlProvider.cs`,
  `DependencyInjection.cs` (Geoapify/Serper `HttpClient` setup), and `Program.cs`'s CORS
  policy.
