# Coolify Deployment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deploy `TripPlanner.WebApi` to Coolify as a single Docker Compose resource containing the API, Postgres and Redis.

**Architecture:** A new `docker-compose.deploy.yml` at the repo root defines three services on Coolify's auto-created network — `api` built from the existing `backend/Dockerfile`, plus `postgres:17` and `redis:7` with no published ports. Coolify generates the Postgres password and the public domain via magic variables; six secrets are set in its UI. The API gains an anonymous `/health` endpoint so Coolify can distinguish "container started" from "migrations applied and serving".

**Tech Stack:** .NET 10 (ASP.NET Core), EF Core + Npgsql, Redis via StackExchange.Redis, Docker Compose, Coolify (Traefik proxy), xUnit + `WebApplicationFactory`.

**Spec:** `docs/superpowers/specs/2026-08-14-coolify-deployment-design.md`

## Global Constraints

- **Do not commit without asking.** The user controls their own git history. Each task ends with a commit step; present the command and wait for approval rather than running it unprompted.
- **No new NuGet packages.** `AddHealthChecks()` and `MapHealthChecks()` are both in the ASP.NET Core shared framework. If a task seems to need a package, stop — the design was chosen specifically to avoid one.
- **Do not touch these files:** `docker-compose.yml`, `dev.ps1`, `dev-docker.ps1`, `start-dev.bat`, `dev.sh`, `backend/src/TripPlanner.WebApi/.env.example`, anything under `frontend/`, and anything under `backend/src/TripPlanner.{Application,Domain,Infrastructure}`. The local-dev flow must keep working unchanged.
- **Backend commands run from `backend/`.** `docker` commands run from the repo root.
- **Docker on this machine may live only inside WSL.** If `docker` is not on the Windows PATH, prefix commands with `wsl bash -lc "cd /mnt/c/Users/duykhuynh/Learning/dotnet-full-stack && <command>"` — see `dev-docker.ps1`, which does exactly this translation.
- **The API listens on `http://+:8080` inside the container** (`ASPNETCORE_URLS` in `backend/Dockerfile`). Every healthcheck and proxy target uses 8080.
- **Coolify magic variable names are exact:** `SERVICE_PASSWORD_POSTGRES` and `SERVICE_FQDN_API_8080`. The `API` segment must match the compose service name `api`, and `8080` must match the container port.
- **Never write real secrets into a repo file.** Task 3 uses a throwaway env file under the scratchpad directory for validation and deletes it afterwards.

---

## Deviation from the spec — read before Task 1

The spec's §2 says the `/health` endpoint is **untested**: *"The endpoint has no logic to test beyond the framework's own."*

**This plan adds a test anyway,** and Task 1 is written accordingly. The reasoning that changed:

The endpoint's value is not its logic, it's two properties that nothing else in the suite protects — that it lives at `/health`, and that it is reachable **without a token**. If someone later adds a blanket `[Authorize]` fallback policy, moves the route, or gates it behind `IsDevelopment()` alongside Swagger, every existing test still passes and the only symptom is that Coolify starts reporting the deployment unhealthy — a failure that appears at deploy time, in a different tool, far from the change that caused it.

That is the same argument this codebase already accepted for `DestinationsEndpointsTests`, which pins the destination endpoints as public because *"a stray [Authorize] would break the landing experience and nothing else would catch it"* (F3/US8). One `[Fact]` against the existing `CustomWebApplicationFactory` is a small price for the same protection.

If the reviewer disagrees, drop the test file from Task 1 and keep steps 3–6; nothing later depends on it.

---

## File Structure

| File | Status | Responsibility |
|---|---|---|
| `backend/src/TripPlanner.WebApi/Program.cs` | modify (2 lines) | Register and map the liveness endpoint |
| `backend/tests/TripPlanner.WebApi.Tests/HealthEndpointTests.cs` | create | Pin `/health` as anonymous and at that exact path |
| `backend/Dockerfile` | modify (1 line + 1 comment) | Provide `curl` for the compose healthcheck |
| `docker-compose.deploy.yml` | create | The whole deployment topology — the only file Coolify reads |
| `docs/deployment-coolify.md` | create | Human runbook: click-path, env vars, verification, rollback |

Task order is a dependency chain: the compose healthcheck (Task 3) needs `curl` (Task 2), which exists only to call `/health` (Task 1).

---

### Task 1: Liveness endpoint

**Files:**
- Modify: `backend/src/TripPlanner.WebApi/Program.cs:31` and `:128`
- Test: `backend/tests/TripPlanner.WebApi.Tests/HealthEndpointTests.cs` (create)

**Interfaces:**
- Consumes: `CustomWebApplicationFactory` (existing, `backend/tests/TripPlanner.WebApi.Tests/CustomWebApplicationFactory.cs`) — an `IClassFixture` that boots the real `Program` with EF Core InMemory swapped in.
- Produces: `GET /health` → `200 OK`, body exactly `Healthy`, `Content-Type: text/plain`. Task 2 and Task 3 both depend on this exact path and port.

- [ ] **Step 1: Write the failing test**

Create `backend/tests/TripPlanner.WebApi.Tests/HealthEndpointTests.cs`:

```csharp
using System.Net;
using Xunit;

namespace TripPlanner.WebApi.Tests;

/// <summary>
/// Pins the two properties of /health that the Coolify deployment depends on and
/// that nothing else in the suite would notice breaking: the exact path, and
/// anonymous access.
///
/// docker-compose.deploy.yml's healthcheck for the `api` service runs
/// `curl -f http://localhost:8080/health` from inside the container, where no JWT is
/// available. A blanket [Authorize] fallback policy, a moved route, or gating the
/// endpoint behind IsDevelopment() (as Swagger is) would each leave every other test
/// green while making every deployment report unhealthy.
///
/// The body is asserted because Coolify surfaces it in the UI: MapHealthChecks's
/// default response writer emits the status name, and with no registered checks that
/// is "Healthy".
/// </summary>
public class HealthEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public HealthEndpointTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_WithoutAToken_ReturnsHealthy()
    {
        var client = _factory.CreateClient(); // no Authorization header

        var response = await client.GetAsync("/health");

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run from `backend/`:

```bash
dotnet test --filter "FullyQualifiedName~HealthEndpointTests"
```

Expected: **FAIL**, one test, with `Assert.Equal() Failure` comparing `OK` to `NotFound` — the route does not exist yet. If it fails with a *build* error instead, the test file is malformed; fix that before continuing.

- [ ] **Step 3: Register the health check service**

In `backend/src/TripPlanner.WebApi/Program.cs`, find line 31:

```csharp
builder.Services.AddControllers();
```

Replace it with:

```csharp
builder.Services.AddControllers();

// Liveness for the container orchestrator. No database probe on purpose: migrations
// are applied before app.Run() below, so the app answers no HTTP at all until they
// have finished — a plain 200 here already means "migrated and serving", which is the
// only question Coolify asks. A DbContextCheck would add a NuGet package to report
// something the next real request reports anyway.
builder.Services.AddHealthChecks();
```

- [ ] **Step 4: Map the endpoint**

In the same file, find line 128:

```csharp
app.MapControllers();
```

Replace it with:

```csharp
app.MapControllers();

// Anonymous by design — the healthcheck in docker-compose.deploy.yml runs curl from
// inside the container and has no token. The body is the literal string "Healthy" and
// discloses nothing else. Outside /api, so it cannot collide with a controller route.
app.MapHealthChecks("/health");
```

- [ ] **Step 5: Run the test to verify it passes**

Run from `backend/`:

```bash
dotnet test --filter "FullyQualifiedName~HealthEndpointTests"
```

Expected: **PASS**, `Passed! - Failed: 0, Passed: 1`.

- [ ] **Step 6: Run the whole suite to confirm nothing regressed**

Run from `backend/`:

```bash
dotnet test
```

Expected: **PASS**, zero failures across `TripPlanner.Application.Tests` and `TripPlanner.WebApi.Tests`. A new route is additive, so any failure here is a real signal — investigate rather than proceeding.

- [ ] **Step 7: Commit** *(ask the user before running — see Global Constraints)*

```bash
git add backend/src/TripPlanner.WebApi/Program.cs backend/tests/TripPlanner.WebApi.Tests/HealthEndpointTests.cs
```

```bash
git commit -m "feat: add anonymous /health liveness endpoint for container orchestration"
```

---

### Task 2: `curl` in the runtime image

**Files:**
- Modify: `backend/Dockerfile:1` (comment) and the final stage

**Interfaces:**
- Consumes: `/health` from Task 1.
- Produces: an image whose `final` stage has `curl` on `PATH`. Task 3's healthcheck depends on it.

- [ ] **Step 1: Fix the stale header comment**

`mcr.microsoft.com/dotnet/aspnet:10.0` ships neither `curl` nor `wget`, so the healthcheck added in Task 3 would have nothing to run.

First, the comment this change makes wrong. Find line 1 of `backend/Dockerfile`:

```dockerfile
# Build context: backend/ (Coolify "Base Directory" = backend)
```

Replace with:

```dockerfile
# Build context: backend/ — supplied by docker-compose.deploy.yml, which sets
# `context: ./backend` from the repo root. Coolify's "Base Directory" is therefore "/",
# not "backend"; see docs/deployment-coolify.md.
```

- [ ] **Step 2: Install curl in the final stage**

Find these lines near the end of `backend/Dockerfile`:

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
```

Replace with:

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Solely so docker-compose.deploy.yml's healthcheck has something to run: the aspnet
# runtime image ships neither curl nor wget. ~5MB; the list cleanup keeps it there.
# If the /health healthcheck is ever dropped, drop this too.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .
```

Note the `RUN` sits **before** `COPY --from=build`, so editing application source does not invalidate the apt layer on rebuild.

- [ ] **Step 3: Build the image**

Run from the repo root (prefix with `wsl bash -lc "..."` if `docker` is not on the Windows PATH — see Global Constraints):

```bash
docker build -t tripplanner-api:healthcheck-test ./backend
```

Expected: the build completes with `naming to docker.io/library/tripplanner-api:healthcheck-test`. The first run pulls the .NET 10 SDK image and takes several minutes.

- [ ] **Step 4: Verify curl is present**

```bash
docker run --rm --entrypoint curl tripplanner-api:healthcheck-test --version
```

Expected: a version banner beginning `curl 8.` — **not** `executable file not found`.

- [ ] **Step 5: Clean up the test image**

```bash
docker image rm tripplanner-api:healthcheck-test
```

- [ ] **Step 6: Commit** *(ask the user first)*

```bash
git add backend/Dockerfile
```

```bash
git commit -m "build: install curl in the runtime image for the compose healthcheck"
```

---

### Task 3: The deployment compose file

**Files:**
- Create: `docker-compose.deploy.yml` (repo root)

**Interfaces:**
- Consumes: `backend/Dockerfile` (Task 2) via `build.context: ./backend`; `/health` (Task 1) via the `api` healthcheck.
- Produces: service names `api`, `postgres`, `redis` — `api` reaches the others at hostnames `postgres:5432` and `redis:6379`. Named volume `tripplanner-pgdata`. Six required UI variables: `JWT_KEY`, `GEOAPIFY_API_KEY`, `SERPER_API_KEY`, `SMTP_USER`, `SMTP_APP_PASSWORD`, `FRONTEND_URL`. Task 4 documents all of these.

- [ ] **Step 1: Create the file**

Create `docker-compose.deploy.yml` at the repo root with exactly this content:

```yaml
# Coolify deployment ONLY. Local dev uses docker-compose.yml (Postgres + Redis with
# published ports and default creds); this file is never auto-loaded by
# `docker compose up`, which reads only docker-compose.yml/.override.yml.
#
# Two values are generated by Coolify rather than set by hand:
#   SERVICE_PASSWORD_POSTGRES  — a strong password, created once and remembered. Used by
#                                both POSTGRES_PASSWORD and the API connection string, so
#                                the two cannot drift. Deliberately the symbol-free
#                                variant, not SERVICE_PASSWORDWITHSYMBOLS_*: a `;` or `=`
#                                in the password would break Npgsql's connection string
#                                parsing.
#   SERVICE_FQDN_API_8080      — assigns the public domain and points the proxy at 8080,
#                                the port backend/Dockerfile listens on. Append a path
#                                (=/api) only if the API is not to sit at the domain root.
#
# No `networks:` block — Coolify creates an isolated network and warns that custom ones
# break Traefik routing. No `container_name:` — Coolify names containers per deployment.
# No `restart:` — Coolify manages restart policy. No `ports:` on postgres/redis: they are
# reachable from `api` by service name and must not be exposed to the internet.
services:
  api:
    build:
      context: ./backend
      dockerfile: Dockerfile
    environment:
      - SERVICE_FQDN_API_8080
      - ConnectionStrings__Postgres=Host=postgres;Port=5432;Database=tripplanner;Username=tripplanner;Password=${SERVICE_PASSWORD_POSTGRES}
      # Redis is switched on here (the code default is the in-process cache). Timeouts
      # match .env.example: they bound each cache call to ~1s so an unreachable Redis
      # degrades in 1-2s rather than StackExchange.Redis's ~5s+ backlog default.
      - Cache__Provider=Redis
      - ConnectionStrings__Redis=redis:6379,connectTimeout=1000,syncTimeout=1000,connectRetry=1
      # `:?` marks these required: Coolify refuses to deploy while any is blank, rather
      # than starting a container that crash-loops (Jwt__Key, validated by
      # AddInfrastructure's ValidateOnStart) or serves broken features (the rest).
      - Jwt__Key=${JWT_KEY:?}
      - Geoapify__ApiKey=${GEOAPIFY_API_KEY:?}
      - Serper__ApiKey=${SERPER_API_KEY:?}
      - Smtp__User=${SMTP_USER:?}
      - Smtp__AppPassword=${SMTP_APP_PASSWORD:?}
      # Where the browser app lives: CORS allow-list, and the base of the link in
      # verification emails.
      - Cors__AllowedOrigins__0=${FRONTEND_URL:?}
      - App__FrontendBaseUrl=${FRONTEND_URL:?}
    depends_on:
      postgres:
        condition: service_healthy
      redis:
        condition: service_healthy
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health"]
      interval: 10s
      timeout: 5s
      retries: 5
      # Covers EF Core migrations, which run before the app serves any HTTP.
      start_period: 30s

  postgres:
    image: postgres:17
    environment:
      POSTGRES_USER: tripplanner
      POSTGRES_PASSWORD: ${SERVICE_PASSWORD_POSTGRES}
      POSTGRES_DB: tripplanner
    volumes:
      - tripplanner-pgdata:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U tripplanner"]
      interval: 10s
      timeout: 5s
      retries: 5

  # No volume: this is a regenerable cache of Geoapify results. A restart costs a cold
  # cache, which is cheaper than persisting data we can always re-fetch.
  redis:
    image: redis:7
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 10s
      timeout: 5s
      retries: 5

volumes:
  tripplanner-pgdata:
```

- [ ] **Step 2: Verify the required-variable guard actually fires**

This is the test for the `:?` markers. Run from the repo root:

```bash
docker compose -f docker-compose.deploy.yml config
```

Expected: **FAIL**, with `required variable JWT_KEY is missing a value`. That is the whole point — a blank secret stops the deploy instead of producing a crash-looping container. If this *succeeds*, the `:?` suffixes are missing or malformed; fix them before continuing.

- [ ] **Step 3: Verify the file is valid once the variables are supplied**

Write a throwaway env file **outside the repo** so no secret can ever be committed:

```bash
printf 'JWT_KEY=x\nGEOAPIFY_API_KEY=x\nSERPER_API_KEY=x\nSMTP_USER=x\nSMTP_APP_PASSWORD=x\nFRONTEND_URL=http://example.test\nSERVICE_PASSWORD_POSTGRES=pw\n' > /tmp/deploy-validate.env
```

`/tmp` here means the Git Bash / WSL temp directory, which is outside the repo either way —
that is the only property that matters. Then:

```bash
docker compose -f docker-compose.deploy.yml --env-file /tmp/deploy-validate.env config
```

Expected: **PASS** — the fully resolved compose YAML is printed. Check three things in that output:

1. `api`'s `ConnectionStrings__Postgres` contains `Password=pw`, and `postgres`'s `POSTGRES_PASSWORD` is also `pw` — the single generated password reaches both.
2. Neither `postgres` nor `redis` has a `ports:` key. If either does, they would be exposed to the internet.
3. `api.depends_on` lists both services with `condition: service_healthy`.

- [ ] **Step 4: Delete the throwaway env file**

```bash
rm /tmp/deploy-validate.env
```

- [ ] **Step 5: Confirm local dev is untouched**

The point of a separate file. Run from the repo root:

```bash
docker compose config --services
```

Expected: exactly `postgres` and `redis` — no `api`. This proves the default `docker compose` invocation used by `dev-docker.ps1` still reads only `docker-compose.yml`.

- [ ] **Step 6: Commit** *(ask the user first)*

```bash
git add docker-compose.deploy.yml
```

```bash
git commit -m "feat: add Coolify deployment compose file for api, postgres and redis"
```

---

### Task 4: Deployment runbook

**Files:**
- Create: `docs/deployment-coolify.md`

**Interfaces:**
- Consumes: the service names, variable names and volume name produced by Task 3.
- Produces: nothing code depends on.

- [ ] **Step 1: Write the runbook**

Create `docs/deployment-coolify.md`:

````markdown
# Deploying the backend to Coolify

The API, Postgres and Redis deploy as **one** Coolify resource, defined by
[`docker-compose.deploy.yml`](../docker-compose.deploy.yml) at the repo root.

The local-dev `docker-compose.yml` is a different file for a different job — it
publishes 5432/6379 to your laptop and uses `tripplanner`/`tripplanner` as the
credentials. It is not used here, and `docker compose up` never loads the deploy file.

Design rationale lives in
[`docs/superpowers/specs/2026-08-14-coolify-deployment-design.md`](superpowers/specs/2026-08-14-coolify-deployment-design.md).

## 1. Create the resource

New resource → **Docker Compose** build pack → this git repository, and whichever branch
you are deploying (Coolify redeploys on push to it).

## 2. Point it at the compose file

| Setting | Value |
|---|---|
| Base Directory | `/` |
| Docker Compose Location | `docker-compose.deploy.yml` |

Base Directory is `/`, **not** `backend`, because the compose file lives at the repo root
and sets `context: ./backend` itself.

## 3. Set the environment variables

Six values, in the resource's **Environment Variables** tab. All six are declared
`${...:?}`, so Coolify refuses to deploy until each has a value — you cannot accidentally
ship a half-configured stack.

| Variable | Maps to | Where to get it |
|---|---|---|
| `JWT_KEY` | `Jwt__Key` | Random string, ≥ 32 bytes. `openssl rand -hex 48` |
| `GEOAPIFY_API_KEY` | `Geoapify__ApiKey` | myprojects.geoapify.com |
| `SERPER_API_KEY` | `Serper__ApiKey` | serper.dev |
| `SMTP_USER` | `Smtp__User` | Your Gmail address |
| `SMTP_APP_PASSWORD` | `Smtp__AppPassword` | myaccount.google.com/apppasswords — an **App Password**, not your account password; needs 2-Step Verification enabled first |
| `FRONTEND_URL` | `Cors__AllowedOrigins__0` and `App__FrontendBaseUrl` | The deployed frontend's origin. Until it exists, use the API's own domain as a placeholder |

For every one of them:

- **Switch "Build Variable" off.** Both Build and Runtime are on by default; the API
  reads all six at runtime, so there is no reason to expose secrets to the build phase.
- **Tick "Literal"** if the value contains a `$`, or it will be interpolated away.

Changing a value later takes effect **on the next redeploy**, not immediately —
substitution happens when the stack comes up.

Adding a *new* setting is not a UI operation: the compose file is Coolify's single source
of truth for a compose resource, so edit `docker-compose.deploy.yml` and push. Coolify
detects the new `${...}` placeholder and adds the field on the next deployment.

### The two variables you do not set

`SERVICE_PASSWORD_POSTGRES` and `SERVICE_FQDN_API_8080` appear in the same panel but are
generated by Coolify on first deploy.

> **Do not overwrite `SERVICE_PASSWORD_POSTGRES` after the first deploy.** Postgres bakes
> the password into the `tripplanner-pgdata` volume when it initialises. Changing the
> variable afterwards does not change the database's password — it only breaks the API's
> connection string. Read the generated value from this panel; it is the only copy.

## 4. Deploy

Coolify builds `api` from `backend/Dockerfile`, starts `postgres` and `redis`, and waits
for both to report healthy before starting `api`. EF Core migrations apply on startup via
`ApplyMigrationsAsync`; the `api` healthcheck has a 30s `start_period` to cover them.

The public domain is on the resource page once the deploy finishes.

## 5. Verify

```bash
curl -f https://<domain>/health
```

→ `Healthy`. Then:

```bash
curl "https://<domain>/api/destinations/locations?query=paris"
```

→ a JSON array, proving the Geoapify key works. Run it twice; the second call should be
faster, proving the Redis cache path.

```bash
curl -X POST https://<domain>/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{"email":"you@example.com","password":"Passw0rd!","displayName":"Test User"}'
```

→ success, proving Postgres is writable and migrations ran. The verification email
arriving proves the SMTP credentials.

Finally, in Coolify's UI, confirm `postgres` and `redis` have **no** public URL.

## 6. Redeploy and rollback

A redeploy rebuilds `api` and recreates the containers. The `tripplanner-pgdata` volume
**survives** — your data is not lost. Redis has no volume by design, so a redeploy costs
a cold cache and nothing else.

To roll back, use Coolify's deployment history and redeploy an earlier commit. Note that
this does **not** roll back database migrations: EF Core applies them forward-only, so a
rollback across a schema change needs a migration written for it.

## Known limitations

- **No automated database backups.** Coolify's backup feature attaches to its *managed*
  Postgres resource, not to a Postgres service inside a compose resource. Backing up
  `tripplanner-pgdata` is separate work.
- **No Swagger.** `Program.cs` gates it behind `IsDevelopment()`. Setting
  `ASPNETCORE_ENVIRONMENT=Development` would bring it back but also enable developer
  exception pages on a public host — use `curl` instead.
- **Single instance.** Migrations-on-startup assumes one `api` container; two replicas
  booting together would race `MigrateAsync`.
````

- [ ] **Step 2: Verify every relative link resolves**

From the repo root, confirm each target exists:

```bash
ls docker-compose.deploy.yml docs/superpowers/specs/2026-08-14-coolify-deployment-design.md
```

Expected: both paths listed, no `No such file or directory`. The runbook links to them as `../docker-compose.deploy.yml` and `superpowers/specs/...` relative to `docs/`.

- [ ] **Step 3: Cross-check the variable table against the compose file**

```bash
grep -o '\${[A-Z_]*' docker-compose.deploy.yml | sort -u
```

Expected: exactly `${FRONTEND_URL`, `${GEOAPIFY_API_KEY`, `${JWT_KEY`, `${SERPER_API_KEY`, `${SERVICE_PASSWORD_POSTGRES`, `${SMTP_APP_PASSWORD`, `${SMTP_USER`. Every one except `SERVICE_PASSWORD_POSTGRES` must appear as a row in the runbook's §3 table; `SERVICE_PASSWORD_POSTGRES` belongs in the "variables you do not set" subsection instead. Fix any mismatch.

- [ ] **Step 4: Commit** *(ask the user first)*

```bash
git add docs/deployment-coolify.md
```

```bash
git commit -m "docs: add Coolify deployment runbook"
```

---

### Task 5: First deploy (manual, in Coolify's UI)

**Files:** none — this task is executed by the user against a running Coolify instance.

**Interfaces:**
- Consumes: everything from Tasks 1–4. The branch must be pushed before starting.

An agent cannot do this task: it needs the user's Coolify login and their real API keys. Present it as a checklist and hand it over.

- [ ] **Step 1: Push the branch**

```bash
git push -u origin duykhuynh
```

- [ ] **Step 2: Walk `docs/deployment-coolify.md` §§1–4**

Create the resource, set Base Directory `/` and compose location `docker-compose.deploy.yml`, fill the six variables with Build Variable off, deploy.

- [ ] **Step 3: Record the generated Postgres password**

From the Environment Variables panel, copy `SERVICE_PASSWORD_POSTGRES` into a password manager. It is the only copy, and it cannot be changed after the volume initialises.

- [ ] **Step 4: Run the §5 verification commands**

All five checks, including confirming `postgres` and `redis` have no public URL.

- [ ] **Step 5: Correct `FRONTEND_URL` once the frontend is deployed**

Update the variable and redeploy. Accounts registered before this point received verification links pointing at the placeholder and need re-verifying.

---

## Post-implementation

Consider a short note in `CLAUDE.md` under **Configuration** recording that
`docker-compose.deploy.yml` is deployment-only and that `docker-compose.yml` must stay
dev-only — the two files look interchangeable and are not. That is a judgement call for
the reviewer, not a required step.
