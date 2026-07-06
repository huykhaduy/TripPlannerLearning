# Capstone Assignment — TripPlanner

Build a travel trip-planning app. Users search for destinations, view details, and
organise them into a day-by-day itinerary. This document maps the official
**Feature List** to the code you'll write in this template.

**Priorities:** 🔴 High · 🟡 Medium · ⚪ Low. Start with the 🔴 items.
**Legend:** ✅ done for you (reference) · 🛠️ you implement.

---

## ✅ Reference feature (already built) — Feature 4: User Authentication

Study this slice before writing any code; it's your blueprint.

| US  | Title                              | Status | Where |
|-----|------------------------------------|--------|-------|
| US1 | Sign up with email and password    | ✅     | `AuthService.RegisterAsync`, `RegisterPage.tsx` |
| US3 | Log in with email and password     | ✅     | `AuthService.LoginAsync`, `LoginPage.tsx` |
| US4 | Log out                            | ✅     | `AuthContext.logout`, navbar |
| US2 | Verify email to activate account   | 🛠️ 🟡 | Extend `User.IsEmailVerified` + an email/token flow |

What it demonstrates: layered request flow (Controller → Service → DbContext),
password hashing (BCrypt), JWT issuance & validation, DTO mapping, exception →
HTTP translation, unit tests, and a React auth context with route protection.

---

## 🛠️ Feature 1: Destination Suggestion

Backend: `DestinationService.cs` + `GeoapifyClient.cs` · Frontend: `SearchPage.tsx`
Endpoints (already routed): `GET /api/destinations/locations`, `GET /api/destinations/attractions`

| US  | Title                              | Pri | Key acceptance criteria |
|-----|------------------------------------|-----|--------------------------|
| US1 | Autocomplete for search field      | 🟡  | ≥2 chars triggers up to 5 city/country suggestions |
| US2 | Search by city/country             | 🔴  | Case-insensitive, partial match, dedupe, max 5, "No attractions found" empty state |
| US3 | Recommended attractions list       | 🔴  | Name, category, rating, thumbnail (+ placeholders); city radius 20 km; max 20/page |
| US4 | Filter attractions                 | 🟡  | Category + rating filters, combinable, clearable |
| US5 | Sort attractions                   | ⚪  | Recommended (default) / highest rating; keep filters |

> Get coordinates from the geocoding call (US1/US2), then fetch POIs near them (US3).
> NFRs: location search ≤ 500 ms, attractions ≤ 1000 ms (p95) — consider caching.

---

## 🛠️ Feature 2: Destination Details

Backend: `DestinationService.GetDetailsAsync` + `GeoapifyClient` · Frontend: a details view
Endpoint: `GET /api/destinations/{providerId}`

| US  | Title                              | Pri | Key acceptance criteria |
|-----|------------------------------------|-----|--------------------------|
| US1 | Open destination details view      | 🟡  | Name, category, description, images; "Add to Trip" disabled when logged out |
| US2 | View photos                        | 🟡  | Carousel; placeholder when none |
| US4 | Opening hours when available       | ⚪  | Show hours or "Opening hours not available" |
| US3 | Map & location info                | ⚪  | Marker + zoom/pan (optional) |

> The detail view must still open when optional fields (photos/hours/map) are missing.

---

## 🛠️ Feature 3: Trip Planner

Backend: `TripService.cs` · Frontend: `TripsPage.tsx`
Endpoints (already routed, require auth): `GET/POST /api/trips`, `GET/PUT /api/trips/{id}`,
`POST /api/trips/{id}/destinations`, `DELETE /api/trips/{id}/destinations/{itemId}`

| US   | Title                                  | Pri | Key acceptance criteria |
|------|----------------------------------------|-----|--------------------------|
| US1  | Create a trip                          | 🔴  | Name required; appears in trip list |
| US2  | Set trip start and end dates           | 🔴  | One itinerary day per date; start ≤ end (`Trip.SetDates`) |
| US3  | Add a destination to a trip            | 🔴  | From list or details; optionally pick a day |
| US7  | Remove a destination from itinerary    | 🔴  | Remove after confirmation |
| US8  | Require login to save                  | 🟡  | Prompt login; resume the action after sign-in |
| US9  | Auto-save trips & destinations         | 🟡  | Persist across reloads; saving indicator; error retain |
| US10 | Load saved trips on return             | 🟡  | Show trips, days, destinations; empty state |
| US4  | Schedule destinations into a day (DnD) | 🟡  | Drag from Saved Places → day; no duplicates in a day |
| US5  | Reorder within a day                   | 🔴  | Drag to reorder; persists (`ItineraryItem.SortOrder`) |
| US6  | Move a destination between days        | 🔴  | Drag across days; no duplicates |

> **Authorization (NFR 6):** every `TripService` method must filter by
> `ICurrentUserService.UserId`. A user must never read or modify another user's trips.

---

## 🛠️ Non-Functional Requirements

| NFR  | Requirement                                                        |
|------|-------------------------------------------------------------------|
| NFR1 | Location search ≤ 500 ms (p95)                                     |
| NFR2 | Attractions displayed ≤ 1000 ms (p95)                             |
| NFR3 | Details popup ≤ 2 s                                                |
| NFR4 | Drag-and-drop responds ≤ 100 ms (optimistic UI)                   |
| NFR6 | Users can only view/modify their own trips and destinations       |

---

## Suggested milestones

1. **Get oriented** — run the app, register/login, read `AuthService` + `AuthController`.
2. **Feature 3 core (🔴)** — Trip CRUD (US1, US2, US3, US7) + the trips UI. Pure
   backend logic against your own DB; no external API needed. Great warm-up.
3. **Feature 1 (🔴)** — `GeoapifyClient` + search/attractions UI.
4. **Feature 2** — destination details view.
5. **Feature 3 advanced** — drag-and-drop scheduling, reordering, cross-day moves.
6. **Polish** — filters/sort, empty/error/loading states, performance & caching, more tests.

## Definition of done (per feature)

- [ ] Meets the acceptance criteria above.
- [ ] Business rules enforced in the **Application/Domain** layer (not the controller).
- [ ] Authenticated endpoints filter by the current user.
- [ ] At least one unit test for the new service logic (follow `AuthServiceTests`).
- [ ] UI handles loading, empty, and error states.
