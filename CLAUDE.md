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
complete reference slice; Destination Suggestion/Details and Trip Planner are now also
fully implemented (no remaining `NotImplementedException` stubs in `Application`) —
`ExceptionHandlingMiddleware`'s 501 mapping stays in place for any new feature work that
introduces one, but nothing currently throws it.

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
npm test           # Vitest (single run); npm run test:watch for watch mode

# Run a single test file
npx vitest run tests/auth/AuthContext.test.tsx
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
- **Application** — service interfaces (`ITripService`, `IAuthService`, etc.) and implementations. Defines one repository interface per aggregate (`IUserRepository`, `ITripRepository`, `IDestinationRepository`) whose write methods save their own changes — there is no generic `IRepository<T>`, no unit of work, and no `IApplicationDbContext`. Also `IPasswordHasher`, `IJwtTokenGenerator`, `ICurrentUserService`, `IDestinationProvider`. Depends only on Domain.
- **Infrastructure** — EF Core `ApplicationDbContext`, repositories, JWT token generation, BCrypt, `GeoapifyClient`. Implements Application interfaces that are technology-specific but not tied to the HTTP pipeline.
- **WebApi** — controllers, middleware (`ExceptionHandlingMiddleware`), DI composition root. Registers all layers via `AddApplication()` / `AddInfrastructure()` / `AddWebApi()`. `AddWebApi()` (`WebApi/DependencyInjection.cs`) is the one exception to "Infrastructure implements Application interfaces": `ICurrentUserService` needs `IHttpContextAccessor`, an ASP.NET Core hosting concern, so its implementation (`WebApi/Services/CurrentUserService.cs`) and registration live in WebApi, not Infrastructure.

### Key patterns

- **DI registration**: each layer exposes an extension method (`AddApplication()`, `AddInfrastructure()`, `AddWebApi()`) — `Program.cs` stays small and just calls all three plus framework setup (auth, CORS, Swagger).
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
- **Validation**: every feature method starts by running its FluentValidation validator via
  `ValidateAndThrowAppExceptionAsync` (`Common/Validation/ValidationExtensions.cs`), which turns
  failures into our `ValidationException` → HTTP 400 with field-level `errors`. Validators are
  **not registered in DI and must not be** — `AddApplication()` deliberately has no
  `AddValidatorsFromAssembly` call, and the package is core `FluentValidation`, not
  `FluentValidation.DependencyInjectionExtensions`. Each service holds them as
  `private static readonly` instances (see `AuthService`), because they are stateless,
  dependency-free rule declarations: injecting `IValidator<T>` would be ten interfaces with one
  implementation each that nothing ever substitutes. The one thing that reverses this: a validator
  needing a dependency (e.g. an async rule hitting the database) can no longer be a static
  instance — convert *that* validator to a constructor parameter and leave the rest alone.
- **Logging**: adapters log at the source, then **rethrow**; callers keep their own fallback
  policy. `GeoapifyClient`, `SerperImageClient`, `SmtpEmailSender` and `ResilientDistributedCache`
  each catch their transient failures, log a Warning, and let the exception continue (the cache
  decorator is the exception — degrading to a miss *is* its contract, so it swallows after
  logging). Do not make an adapter swallow to "simplify" a caller: `DestinationService` decides
  whether to cache a result based on whether the search *failed* versus *found nothing*, and
  `AuthService`'s "registration survives a mail outage" guarantee (F4/US1) is asserted by a test
  at the Application level. `DestinationService` holds the only `ILogger` in Application, for the
  one failure that originates in this layer rather than an adapter: a cached entry that no longer
  matches its DTO shape.
- **Adapters translate their failures into `ExternalServiceUnavailableException`**: the three
  outbound adapters do not rethrow the *raw* failure — they log it, then throw the Application-owned
  `ExternalServiceUnavailableException` (`Common/Exceptions/`) with the original as `InnerException`.
  The Application layer catches only that type, so no service names `SmtpException`,
  `HttpRequestException`, `TaskCanceledException` or `JsonException`. This is the point: catching
  `SmtpException` in `AuthService` made the F4/US1 "registration survives a mail outage" guarantee
  hold only while `IEmailSender` happened to speak SMTP, and it was the reason the same
  three-type predicate was copy-pasted into `AuthService`, `DestinationService` and `TripService`.
  Adapters translate **only when their own `CancellationToken` was not the cause**, so a
  caller-driven cancellation still propagates as `TaskCanceledException` and is never mistaken for
  an outage — that split is what the "cancelled request is not a provider failure" adapter tests
  pin. Like `ConcurrencyException`, this type is deliberately **not** mapped by
  `ExceptionHandlingMiddleware`: a service with a fallback (stale cache entry; save the destination
  without a photo) catches it, and anywhere else it is a genuine 500. Two knock-on rules: JSON
  handling in `DestinationService` is now *only* for its own cache envelope (a provider's
  unparseable body is the adapter's problem), and a serialize failure in `SetCachedEnvelopeAsync`
  now surfaces as the bug it is instead of being mistaken for a provider outage and answered with
  stale data. When you add an outbound adapter, translate in it; do not teach Application a new
  technology's exception type.
- **Reference slice**: `AuthService.cs` + `AuthController.cs` are the canonical worked example. Study them before implementing other features.
- **Destination caching**: `Destination` rows are a cache of external-provider data (Geoapify), keyed by a unique index on `ProviderId` (`DestinationConfiguration.cs`). `DestinationService` (search/details) never persists rows — only `TripService.AddDestinationAsync` upserts one, on first add to any trip. Do not reintroduce a "must already exist" lookup there; a fresh `ProviderId` is expected and should upsert, not 404. `ProviderId` is unbounded `text` **by design**: a Geoapify `place_id` is a ~68-char prefix plus the hex-encoded UTF-8 place name (2 id chars per name byte), so real ids run 62–328 chars and the original `varchar(128)` made add-to-trip 500 (Postgres `22001`) for any long or non-Latin name. Never restore a `HasMaxLength` here, and never cap it in the validators (that would reject legitimate places). EF InMemory ignores `HasMaxLength`, so no behavioural test can catch a regression — `DestinationConfigurationTests` pins the absence via model metadata instead.
- **Entities supply their own keys**: `BaseEntity` self-assigns `Id = Guid.NewGuid()`, so all five
  entity configurations declare `builder.Property(x => x.Id).ValueGeneratedNever()`. Without it, EF's
  convention treats a Guid key as store-generated and classifies a *new* child arriving with a key
  already set as an EXISTING row — issuing an UPDATE instead of an INSERT, which surfaces as
  `DbUpdateConcurrencyException: Attempted to update or delete an entity that does not exist`. This
  is what lets `TripService` just do `trip.Days.Add(...)` / `trip.Items.Add(...)` and rely on one
  `UpdateAsync`. The `SetIdValueGeneratedNever` migration is intentionally **empty** and must not be
  deleted: it carries the metadata change into `ApplicationDbContextModelSnapshot`, which later
  migrations diff against.
- **Concurrency via unique index + catch/retry**: this codebase enforces invariants that matter under concurrent requests as DB-level unique indexes (`User.Email`, `Destination.ProviderId`, `(ItineraryDayId, DestinationId)`) rather than app-level locking. `ApplicationDbContext.SaveChangesAsync` translates a Postgres unique violation into `ConcurrencyException`, so read-then-write code catches that (never EF's `DbUpdateException`) and re-fetches or re-reports — all three indexes have a handler: `AuthService.RegisterAsync` (→ the same generic 409 as the in-memory duplicate check, so the two can't be told apart), `TripService.GetOrCreateDestinationAsync`, and `TripService.SaveWithDuplicateGuardAsync`. `DestinationRepository.AddAsync` detaches its losing entity before rethrowing so the caller can re-fetch on a clean change tracker. EF Core's InMemory provider (used in tests) does **not** enforce these indexes, so the translation itself is untested by `dotnet test` and must be reasoned about directly against the SQL provider; the *callers* are tested by simulating `ConcurrencyException` at the repository boundary with a mock (see `AuthServiceTests`).
- **Two accepted concurrency gaps**, both decisions rather than oversights. Postgres treats every
  NULL as distinct, so `(ItineraryDayId, DestinationId)` does **not** cover Saved Places rows
  (`ItineraryDayId IS NULL`) — two concurrent adds of the same place to one trip's Saved Places can
  both succeed. A filtered unique index for it was written and then deliberately reverted as
  over-engineering for this project; `TripService`'s in-memory check is the only guard, and the
  exposure is one duplicate row in a user's own trip. Separately, `ItineraryItem.SortOrder` has no
  unique index — ordering is enforced purely in-memory via dense resequencing per request
  (`TripService.Resequence`), so two concurrent reorders of the same bucket race.

### Feature layout (backend)

```
Application/Features/
  Auth/               ← reference implementation
  Destinations/       ← DestinationService.cs
  Trips/              ← TripService.cs
```

All three are fully implemented (Auth is the canonical worked example to study first, not the only complete one).

Each feature folder holds: the service implementation, an interface, and a `Dtos/` subfolder.

**Where an interface goes** — there are two locations and the rule is the *consumer*, not
the kind of type:

- `Features/<Feature>/I<Feature>Service.cs` — **inbound** use-case interfaces, beside their
  one implementation. The controller is the only caller and the pair is read together.
- `Common/Interfaces/` — **outbound ports** the Application layer defines for someone else to
  implement (`IUserRepository`, `ITripRepository`, `IDestinationRepository`,
  `IDestinationProvider`, `IImageSearchProvider`, `IEmailSender`, `IPasswordHasher`,
  `IJwtTokenGenerator`, `ICurrentUserService`, `IAppUrlProvider`). These are the dependency
  inversion boundary, so they sit together where that boundary is easy to see and audit.

Corollaries worth stating, because both were violated once: `Common/Interfaces/` holds
**only** interfaces — a helper class goes in `Common/Extensions/` (that is why
`CurrentUserServiceExtensions` lives there, not next to `ICurrentUserService`). And these
interfaces carry **no default implementations**: `ICurrentUserService` had an unused
`IsAuthenticated => UserId is not null` body, which put logic in a port and was dead besides.
Behaviour over a port belongs in an extension method, where `GetRequiredUserId` already is.

**A file's name must predict the types in it.** Two allowed shapes, nothing else:

- one top-level type, in a file named after it — the default for anything with behaviour;
- several small, closely-related types under a **plural name describing the group** —
  `AuthDtos.cs` / `TripDtos.cs` / `DestinationDtos.cs` (a feature's request/response records;
  twenty single-record files would be harder to scan, not easier) and
  `FakeExternalProviders.cs` in the WebApi tests.

What this rules out is a file named after *one* of the types it contains, which is how
`ItineraryDayConfiguration` and `ItineraryItemConfiguration` came to be hidden inside
`TripConfiguration.cs` — unfindable by filename — and how `TripSummaryRow` came to live in
`TripMappings.cs`. All four now have their own files.

### Frontend structure

```
src/
  api/         ← axios wrappers: client.ts (base instance), auth.ts, destinations.ts, trips.ts
  auth/        ← AuthContext (JWT storage, reactive logout on 401), ProtectedRoute
  components/  ← shared UI shell: Avatar, Button, Card, EmptyState, Field, Modal
  features/    ← page components grouped by feature (auth, destinations, trips — all implemented)
  hooks/       ← useDebounce
  types.ts     ← shared TypeScript types for every DTO (auth, destination, trip)
  App.tsx      ← React Router route definitions
tests/         ← mirrors src/ one-for-one, plus setup.ts and http.ts
```

Tests live in `tests/`, **not** beside the code — the same `src/`-vs-`tests/` split the
backend uses, so `src/` holds only shipped code. (The frontend mirror is one-for-one; the
backend's is *not* — see the xUnit section below, where one project covers three layers.) A test for
`src/features/trips/TripsPage.tsx` belongs at `tests/features/trips/TripsPage.test.tsx`;
keep the mirror exact, because `vite.config.ts` pins `include: ['tests/**/*.test.{ts,tsx}']`
and a test left under `src/` will simply never run.

HTTP client (`src/api/client.ts`) is a configured Axios instance whose JWT interceptor
attaches `Authorization: Bearer <token>` when present, and exposes two shared error helpers:
`getErrorMessage(err, fallback)` for the backend's `ProblemDetails.detail`, and
`getErrorStatus(err)` for the HTTP status when a *specific* code changes the UI rather than just
the message (e.g. 404 → render a "not found" state). **`src/api/` is the only place allowed to
import `axios`** — components call these helpers instead of narrowing the error type themselves,
so the HTTP library stays swappable and out of the presentation layer.
`destinations.ts` and `trips.ts`
follow `auth.ts`'s pattern: thin typed wrappers over `apiClient`, no raw `fetch`/`axios`
calls in components.

**Frontend tests** run on Vitest + React Testing Library in a `jsdom` environment,
configured in the `test` block of `vite.config.ts` with `tests/setup.ts` as the setup
file. `globals` is deliberately **false**: test files import `describe`/`it`/`expect` from
`vitest` explicitly, which means `tsconfig.json` (whose `include` covers `src` **and**
`tests`) type-checks them with no `types` entry — so `npm run lint` checks the tests
too, and a wrong mock signature fails the build rather than passing silently. Keep `tests`
in that `include` list: drop it and the suite still runs, but it stops being type-checked
and every mock signature silently rots. Because
globals are off, RTL cannot register its own `afterEach`, so setup.ts calls `cleanup()`
**and** `localStorage.clear()` by hand — `AuthProvider` reads localStorage during its
initial render, so a leftover session would leak into the next test.

Coverage is now whole-tree — every module under `src/` has a matching file under `tests/`.
Worth knowing where the non-obvious value sits: `client.ts`'s error helpers **and** its two
interceptors (including the `&& getToken()` guard that stops a mistyped password triggering
the logout flow); the three `src/api/` wrappers, which matter because every page test mocks
those modules wholesale — without wrapper tests a wrong URL or renamed body field would be
invisible to the entire suite; `AuthProvider` (login/logout persistence, refresh restore,
the reactive-logout listener, and that register does *not* start a session);
`ProtectedRoute`'s redirect; `App.tsx`'s route table and auth guard; every feature page; and
the shared `components/` shell.

Conventions these follow:

- API modules are mocked wholesale with `vi.mock('<…>/src/api/<module>', ...)` — the path is
  relative to the *test* file, so it climbs out of `tests/` and back into `src/`
  (`'../../../src/api/trips'` from `tests/features/trips/`). List every export, since a
  partial factory makes the missing ones `undefined` at call time.
- "Already signed in" is expressed by seeding `localStorage['tripplanner.user']` **before**
  render, because that is what `AuthProvider` reads during its initial render.
- Build axios rejections with `httpError(status, body)` / `networkError()` from
  `tests/http.ts` rather than hand-rolled objects, so `getErrorMessage`/`getErrorStatus`
  take their real branches. The same file's `recordRequests(instance)` swaps in a recording
  adapter — the seam *below* the interceptors — so the `src/api/` wrapper tests assert on
  the URL, params and headers that would really go on the wire.
- Date-dependent assertions (the status pill) are written **relative to today** via an
  offset helper, so they cannot rot into failures on a future date.
- `DestinationDetailsPage` needs an `AuthProvider` wrapper for its success path only —
  `AddToTripButton` calls `useAuth`, and the error paths return before rendering it.
- `window.confirm` gates the destructive actions in `TripDetailPage`; stub it with
  `vi.spyOn(window, 'confirm')` and assert **both** answers — declining must not call the
  API.
- Drag-and-drop is driven with `fireEvent.dragStart` / `fireEvent.drop` on the row and the
  drop zone. jsdom has no real DnD, but the handlers are ordinary React props, so this
  exercises the actual move logic including the optimistic-update rollback.
- The Saved Places filter hides non-matching rows with a CSS class rather than unmounting
  them, so index-based drop positions stay aligned with the unfiltered array. Assert on
  `toHaveClass('hidden')`: no Tailwind stylesheet is loaded under jsdom, so `toBeVisible()`
  cannot see `display: none` and would report hidden rows as visible.

Two lessons already paid for, worth keeping: a test whose only assertion was
`expect(...).not.toThrow()` could never fail, because React 18 made setState-on-unmounted
a silent no-op — the listener-cleanup test now asserts against the `addEventListener` /
`removeEventListener` pair instead. And a test asserting only that things are *absent*
passes just as happily when the page crashes; pair every absence check with a positive one.

Every module under `src/` has a test file; when you add one, add its mirror under `tests/`.

`CitySearchInput.test.tsx` uses **real timers**: the 300 ms debounce fits inside
`findBy*`'s 1 s default, which is simpler and less brittle than driving fake timers
through `userEvent`. Its "does not re-search the label it just wrote" and "resyncs when
the selected city changes underneath it" cases exist because putting `pickedLabel` in the
search effect's dependency array made picking a suggestion fire a **second** request that
reopened the dropdown over the user's choice — the guard couldn't match while
`debouncedQuery` still held the old query. It is a ref for that reason; do not turn it
back into state. `types.ts` covers `User`/`AuthResponse` plus every destination/trip
DTO consumed by the feature pages — add new shared shapes here rather than declaring
ad-hoc inline interfaces in components.

### Testing pattern (xUnit)

There are **two** test projects, split by what they need to run rather than by which layer
they cover:

- `tests/TripPlanner.UnitTests` — everything that runs in-process with no host. Despite
  living next to `src/`, it does **not** mirror it one-for-one: it covers Domain
  (`DomainRules/`), Application (`Auth/`, `Common/`, `Destinations/`, `Trips/`) **and**
  Infrastructure (`Identity/`, `Infrastructure/`, `Persistence/`) in one project, because
  they share the same fixtures and need no web host. It was named
  `TripPlanner.Application.Tests`, which claimed one layer while covering three.
- `tests/TripPlanner.WebApi.Tests` — `WebApplicationFactory` integration tests (see below).

`TripPlanner.Infrastructure.csproj` grants `InternalsVisibleTo` to `TripPlanner.UnitTests`
(for `ResilientDistributedCache`), so renaming that project again means updating the csproj
and `TripPlanner.sln` alongside the namespaces.

Tests use an **in-memory EF Core database** (new `Guid` database name per test) and **Moq** for interfaces. See `AuthServiceTests.cs` as the reference:

```csharp
private static ApplicationDbContext CreateDb() =>
    new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
        .Options);
```

Arrange with a fresh `CreateDb()`, construct the service under test with real implementations where cheap (e.g. `BCryptPasswordHasher`) and `Mock<T>` for everything else. Services take no validator
arguments (they hold their own), and anything needing an `ILogger` gets
`NullLogger<T>.Instance`.

`*ValidatorTests` (one per feature) test the validators directly with FluentValidation's
`TestValidate` helper. They exist for what the service tests don't check: which **property**
carries the error, the exact **message** (several are deliberately identical so they can't be used
to probe whether an account exists), and the **boundary** either side of each limit. Three of them
pin deliberate *absences* — `Login_WithAPasswordShorterThanRegisterAllows_IsStillValid` (adding a
length rule to login would lock out accounts whose passwords predate a policy change),
`UpdateTrip_WithEndBeforeStart_IsNotTheValidatorsJob` (that's `Trip.SetDates`, a domain rule), and
`UpdateItem_WithAPositionPastTheEndOfTheBucket_IsValid` (`TripService` clamps, so "99" means
"last"). Watch the trip-length rule: it compares a *difference* of day numbers with a strict `<`,
so the largest accepted range is `MaxTripLengthDays - 1` days apart, i.e. 365 days counted
inclusively.

`ApplicationDbContextTests` covers the `SaveChangesAsync` override's audit stamping — the
unique-violation translation in the same method is not reachable under InMemory.

`TripPlanner.WebApi.Tests` covers what the above can't: real HTTP routing, `[Authorize]`
enforcement, query-string binding, and `ExceptionHandlingMiddleware`'s exception-to-status-code
mapping. Uses
`WebApplicationFactory<Program>` against the real `Program` (see `CustomWebApplicationFactory.cs`),
with `ApplicationDbContext` swapped from Npgsql to a fresh EF Core InMemory database per
factory instance. External providers are replaced by `FakeExternalProviders.cs`, whose
`FakeDestinationProvider` answers **every** id with a hit *except* ids prefixed
`FakeDestinationProvider.UnknownIdPrefix` (`"unknown-"`) — that prefix is the only way to reach the
"unknown to the provider AND absent from the database" 404 branch. `DestinationsEndpointsTests`
also pins that the destinations endpoints stay **public** (no `[Authorize]`, F3/US8): a stray
attribute there would break anonymous browsing and nothing else in the suite would notice.

Two things to know before touching the factory:

- `AddDbContext`'s `optionsLifetime` defaults to **Scoped**, not Singleton — the InMemory
  database name must be generated *once* and captured (a field), never inlined as
  `Guid.NewGuid()` directly in the options lambda, or every DI scope (every request, and
  every `factory.Services.CreateScope()` a test opens) gets its own empty database.
- Removing `DbContextOptions<ApplicationDbContext>` alone isn't enough to swap providers —
  `AddDbContext` also registers the configuring action itself as a composable
  `IDbContextOptionsConfiguration<T>` (so multiple `AddDbContext` calls layer instead of
  replacing each other); leaving that behind means Program's `UseNpgsql(...)` and the
  test's `UseInMemoryDatabase(...)` both apply to the same options, and EF Core throws.
  Remove both.

`Program.cs`'s automatic migration-on-startup is skipped when `app.Environment.IsEnvironment("Testing")`
(set via `builder.UseEnvironment("Testing")` in the factory) — the InMemory provider doesn't
support migrations at all. `Jwt:Key` is supplied via `builder.UseSetting(...)`, which wins
over the process environment variables `DotNetEnv.Env.Load()` creates from a developer's
local `.env` — `TestHostConfigurationTests` pins that precedence, so the suite can never
silently start signing tokens with someone's real key. No secret needs to be an environment
variable, because nothing is read eagerly during startup any more: `Jwt` is bound and
validated as options in `AddInfrastructure` (`ValidateOnStart`), and the Postgres connection
string is only read inside `AddPersistence`, whose registration the factory replaces
outright. The email-verification
flow is driven by minting a token directly via `IJwtTokenGenerator` (resolved from
`factory.Services`) rather than intercepting a real email — see `AuthTestHelper.cs`.

## Configuration

- **There is no `appsettings.json`/`appsettings.Development.json`** — all configuration is either a
  C# default baked into a settings class (`JwtSettings`, `SmtpSettings`, `GeoapifySettings`,
  `SerperSettings`, all in `TripPlanner.Infrastructure`) or an override in the git-ignored
  `backend/src/TripPlanner.WebApi/.env`, loaded via `DotNetEnv.Env.Load()` in `Program.cs` before
  `WebApplication.CreateBuilder` runs. Copy `.env.example` to `.env` and fill in real values.
  Nested config keys use `__` as the separator (e.g. `Geoapify__ApiKey` → `Geoapify:ApiKey`),
  since `.env` values become process environment variables and ASP.NET Core's
  `AddEnvironmentVariables()` treats `__` as the section delimiter; array elements use a
  trailing index (`Cors__AllowedOrigins__0`).
- API URL: `http://localhost:5080`; frontend `.env` → `VITE_API_BASE_URL=http://localhost:5080/api`
- JWT: `Jwt__Issuer`/`Jwt__Audience`/`Jwt__ExpiryMinutes` are optional overrides (defaults live in
  `JwtSettings.cs`: `TripPlanner`/`TripPlannerClient`/`60`). `Jwt__Key` is the one required,
  no-default value — a defaulted signing key would defeat JWT security, so `AddInfrastructure`
  registers it with `.Validate(...).ValidateOnStart()` (blank, and shorter than
  `JwtSettings.MinKeyBytes`) and the host refuses to start rather than falling back, unlike the
  URL fallbacks below. Validation lives in the options registration, not in `Program.cs`: an
  eager `builder.Configuration[...]` read runs before any test host can layer in its own value.
- Database is PostgreSQL only (no SQLite/InMemory fallback outside tests) — required, no default:
  run `docker compose up -d` and set `ConnectionStrings__Postgres` in `.env` (see `.env.example`).
- Destination browse-path caching (`DestinationService`) defaults to an in-process `IDistributedCache`.
  Switch to Redis by setting `Cache__Provider=Redis` in `.env` and running `docker compose up -d`;
  the connection string comes from `ConnectionStrings__Redis` in the same file.
- Migrations apply automatically on startup via `ApplyMigrationsAsync` in `Program.cs`.
- Geoapify/Serper API keys and SMTP credentials are required secrets with no default (empty string
  in the settings classes) — must come from `.env`. `Cors__AllowedOrigins__0`, `App__FrontendBaseUrl`,
  `Geoapify__BaseUrl`, `Serper__BaseUrl`, `Smtp__Host`/`Smtp__Port` all have a matching
  `?? "localhost default"` (or class-level default) in code, so the app still runs with an empty
  `.env` for those — see `AppUrlProvider.cs`, `DependencyInjection.cs` (Geoapify/Serper `HttpClient`
  setup), `SmtpSettings.cs`, and `Program.cs`'s CORS policy.
- `docker-compose.yml` (repo root) is local dev only; `docker-compose.deploy.yml` is the
  separate Coolify deployment topology — see `docs/deployment-coolify.md`.
