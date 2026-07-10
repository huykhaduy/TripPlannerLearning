# Destination Search Frontend (Autocomplete + Attractions) — Design

**Date:** 2026-07-07
**Scope:** Feature 1, US1–US3 frontend slice — city autocomplete and recommended
attractions on the public `SearchPage`. Filters/sorting (US4–5) and the details
page (Feature 2) are explicitly out of scope.

## Context

The backend already serves both endpoints this slice needs:

- `GET /api/destinations/locations?query=<text>` → `LocationSuggestionDto[]`
  (`name`, `country`, `latitude`, `longitude`). The backend validator rejects
  queries under **2 characters** (`SearchLocationsRequestValidator.MinQueryLength`).
- `GET /api/destinations/attractions?lat=<d>&lng=<d>&radiusKm=<d=20>` →
  `DestinationSummaryDto[]` (`providerId`, `name`, `category`, `imageUrl`, `rating`).

Both are public (no `[Authorize]`) because users browse before logging in (F3/US8).

The frontend is plain React 19 + axios + react-router; no data-fetching or
component libraries, and none are added by this design. `src/api/auth.ts` is the
pattern to follow for API wrappers; `src/features/destinations/SearchPage.tsx`
is the stub being replaced.

## User flow

1. Visitor lands on `/` (public SearchPage) and types a city name.
2. After the input settles (~300 ms debounce) and is ≥ 2 chars, a dropdown of
   suggestions appears ("Paris, France").
3. Picking a suggestion closes the dropdown and fetches attractions near that
   city's coordinates (default radius 20 km).
4. Attractions render as cards: image (with fallback), name, category, rating.
5. Loading, error, and empty states are shown at each step; API failures render
   a friendly inline message.

## Components & files

| File | Purpose |
|---|---|
| `src/types.ts` | Add `LocationSuggestion` and `AttractionSummary` interfaces mirroring the backend DTOs (camelCase). |
| `src/api/destinations.ts` (new) | `searchLocations(query)` and `getAttractions(lat, lng, radiusKm = 20)`, same shape as `auth.ts`. |
| `src/hooks/useDebounce.ts` (new) | Generic `useDebounce<T>(value, delayMs)` returning the value ~300 ms after it stops changing. |
| `src/features/destinations/CitySearchInput.tsx` (new) | Controlled input + suggestion dropdown. Fetches when the debounced query is ≥ 2 chars. Calls `onSelect(city)` prop; closes dropdown on select. |
| `src/features/destinations/AttractionsList.tsx` (new) | Given a selected city, fetches and renders attraction cards with loading / error / empty states. |
| `src/features/destinations/SearchPage.tsx` (rewrite) | Holds `selectedCity` state; composes the two components. Stays public. |

## Key behaviors

- **Debounce:** requests fire only after the user pauses typing (~300 ms), and
  only for queries ≥ 2 chars (matching the backend validator, so no avoidable
  400s).
- **Stale-response guard:** the fetch effect's cleanup sets an `ignore` flag so
  a slow older response cannot overwrite a newer one.
- **Image fallback:** attraction cards render a placeholder when `imageUrl` is
  null or fails to load.
- **Error handling:** axios failures set an error message rendered inline
  (reusing the login page's error styling); no crashes or raw error dumps.

## Out of scope

- Filters & sorting (F1/US4–5) — next slice.
- Destination details page (F2) — backend still throws `NotImplementedException`.
- Keyboard navigation / ARIA combobox semantics in the dropdown — follow-up polish.
- Any new npm dependencies.

## Verification

The frontend has no test runner, so verification is manual:

1. Run backend (`dotnet run --project src/TripPlanner.WebApi`) and frontend
   (`npm run dev`).
2. Type "par" — confirm exactly one debounced request in the Network tab and a
   visible dropdown.
3. Select "Paris" — attractions render with images, names, categories, ratings.
4. Type 1 character — no request fires.
5. Stop the backend and search — friendly error message, no crash.
6. `npm run lint` (tsc) passes.

## Working agreement

The student writes the code; Claude guides one step at a time, reviewing each
step before moving to the next (per established learning workflow).
