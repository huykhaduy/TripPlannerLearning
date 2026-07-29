# TripPlanner — Full-Stack Capstone Template

A starter template for the **.NET Full-Stack training** capstone. You will build a
travel **TripPlanner**: search destinations, view details, and plan day-by-day
itineraries, backed by an ASP.NET Core API and a React + TypeScript frontend.

The project is structured with **Clean Architecture** and ships with **one fully
implemented vertical slice — User Authentication** — as a worked example. Your job
is to implement the remaining features (search, destination details, trip planner)
by following the same patterns.

> 📋 The features you must build are in [`ASSIGNMENT.md`](./ASSIGNMENT.md), mapped to
> the course's official feature list.

---

## Tech stack

| Layer        | Technology                                                |
|--------------|-----------------------------------------------------------|
| Backend      | ASP.NET Core Web API (.NET 10), EF Core, JWT, xUnit       |
| Frontend     | React 19 + TypeScript, Vite, React Router, axios          |
| Database     | **PostgreSQL** — required, run via Docker or your own instance |
| External API | Geoapify for destination data  |

---

## Solution structure (Clean Architecture)

```
backend/                         ← ASP.NET Core solution (TripPlanner.sln)
  src/
    TripPlanner.Domain          ← Entities & business rules. No dependencies.
    TripPlanner.Application      ← Use-cases + interfaces. Depends on Domain only.
    TripPlanner.Infrastructure   ← EF Core, JWT, BCrypt, external APIs. Implements Application interfaces.
    TripPlanner.WebApi           ← Controllers, DI, middleware. The composition root.
  tests/
    TripPlanner.Application.Tests ← xUnit tests (Auth slice covered as an example).
frontend/                        ← React + TypeScript app (Vite).
```

The golden rule: **dependencies point inward.** Domain knows nothing about EF or
ASP.NET; the API knows everything and wires it together. The Application layer
defines interfaces (e.g. `IPasswordHasher`, `IApplicationDbContext`) that
Infrastructure implements — this is the Dependency Inversion Principle.

```
WebApi ──▶ Application ──▶ Domain
   │            ▲
   └──▶ Infrastructure ──┘   (implements Application's interfaces)
```

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js 20+](https://nodejs.org/) and npm
- [Docker](https://www.docker.com/) — required to run PostgreSQL locally (or point at your own Postgres instance)
- A [Geoapify API key](https://myprojects.geoapify.com/) — free, needed for Features 1 & 2

---

## Getting started

### Quick start (run both servers at once)

After the **one-time** setup below (copying `.env` and starting Postgres), you
can launch the API and frontend together from the repo root:

```bash
start-dev.bat        # Windows
./dev.sh        # macOS / Linux  (Ctrl-C stops both)
./dev.ps1       # Windows / PowerShell  (opens two windows)
```

The script installs frontend dependencies and creates `frontend/.env` on first run.
Prefer to run them separately (or it's your first time)? Use the manual steps below.

### 1. Backend API

```bash
cd backend
dotnet restore
dotnet build

# API keys (Geoapify, Serper) and the Postgres connection string — copy the
# template and fill in your own (the Postgres default already matches
# docker-compose.yml's credentials, so it works as-is):
cp src/TripPlanner.WebApi/.env.example src/TripPlanner.WebApi/.env

# Start Postgres — the app has no other database:
docker compose up -d

# Install the EF Core CLI once, if you don't have it (needed later, for any
# new migrations you add while implementing the Trips/Destinations features):
dotnet tool install --global dotnet-ef

# Run the API (applies the existing migrations automatically on startup):
dotnet run --project src/TripPlanner.WebApi
```

The API starts at **http://localhost:5080** with Swagger UI at
**http://localhost:5080/swagger**.

### 2. Frontend

```bash
cd frontend
cp .env.example .env      # points at http://localhost:5080/api
npm install
npm run dev
```

Open **http://localhost:5173**. You can register, log in, and reach the protected
"My trips" page out of the box — that's the reference Auth slice working end to end.

### 3. Run the tests

```bash
cd backend
dotnet test
```

---

## Try the reference feature (Authentication)

1. Start the API and open Swagger.
2. `POST /api/auth/register` with `{ "email": "me@example.com", "password": "password123" }`.
3. Copy the returned `accessToken`, click **Authorize** in Swagger, and paste it.
4. Call `POST /api/trips` with `{ "name": "My trip" }`, then `GET /api/trips` — both
   work end to end in this repo (Trips, like Auth, is already implemented). If
   you're picking up a fresh copy of the template where `TripService` is still a
   stub, this same call returns **501 Not Implemented** instead — that's the signal
   to start implementing it, following `AuthService.cs`/`AuthController.cs` as the
   reference pattern.

---

## Switching to Redis (optional)

1. `docker compose up -d` (starts Redis on port 6379 — safe to run alongside Postgres).
2. In `backend/src/TripPlanner.WebApi/.env`, uncomment (or add):
   ```
   Cache__Provider=Redis
   ```
3. The `ConnectionStrings__Redis` value lives in the same `.env` file (see
   `.env.example`) — its default already matches `docker-compose.yml`'s port,
   so no change is needed unless you edit the compose file.
4. No migration step needed — caching has no schema. Just restart the API
   (`dotnet run`); destination search/attractions/details now cache through
   Redis instead of the in-process default.

---

## Where to write your code

| You implement…            | File(s)                                                              |
|---------------------------|---------------------------------------------------------------------|
| Destination search/details| `backend/src/TripPlanner.Application/Features/Destinations/DestinationService.cs` |
| External API calls        | `backend/src/TripPlanner.Infrastructure/ExternalApis/GeoapifyClient.cs`        |
| Trip planner logic        | `backend/src/TripPlanner.Application/Features/Trips/TripService.cs`               |
| Search & details UI       | `frontend/src/features/destinations/SearchPage.tsx`                              |
| Trip planner UI           | `frontend/src/features/trips/TripsPage.tsx`                                      |

Every stub throws `NotImplementedException` (backend) or shows a `TODO` (frontend),
so the project compiles and runs from day one — you fill in the blanks feature by
feature. **Study `AuthService.cs` and `AuthController.cs` first**: they are the
blueprint for everything else.

See [`ASSIGNMENT.md`](./ASSIGNMENT.md) for the full feature list and acceptance criteria.
