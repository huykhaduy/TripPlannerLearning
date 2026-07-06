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
| Database     | **SQLite** by default (no setup) — PostgreSQL optional     |
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
- (Optional) [Docker](https://www.docker.com/) — only if you switch to PostgreSQL
- A [Geoapify API key](https://myprojects.geoapify.com/) — free, needed for Features 1 & 2

---

## Getting started

### Quick start (run both servers at once)

After the **one-time** database setup below (creating the initial EF migration), you
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

# Install the EF Core CLI once, if you don't have it:
dotnet tool install --global dotnet-ef

# Create the initial database schema (run once):
dotnet ef migrations add InitialCreate \
  --project src/TripPlanner.Infrastructure \
  --startup-project src/TripPlanner.WebApi

# Run the API (auto-applies migrations on startup):
dotnet run --project src/TripPlanner.WebApi
```

The API starts at **http://localhost:5080** with Swagger UI at
**http://localhost:5080/swagger**. A `tripplanner.db` SQLite file is created in the
WebApi folder.

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
4. Call `GET /api/trips` — it returns **501 Not Implemented** because `TripService`
   is a stub. That's your first task. 🙂

---

## Switching to PostgreSQL (optional)

1. `docker compose up -d` (starts Postgres on port 5432).
2. In `backend/src/TripPlanner.WebApi/appsettings.Development.json`, add:
   ```json
   { "Database": { "Provider": "Postgres" } }
   ```
3. Delete the SQLite `Migrations` folder, re-run `dotnet ef migrations add InitialCreate`
   (providers generate different SQL), then `dotnet run`.

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
