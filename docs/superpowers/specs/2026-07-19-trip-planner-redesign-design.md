# Trip Planner Redesign (Stitch mockups) — Design

**Date:** 2026-07-19
**Scope:** Visual/UX redesign of all 4 authenticated + public trip-planning
pages (Explore/Search, Destination Details, My Trips, Trip Itinerary) plus the
app shell nav, to match the color/type/layout system produced by Stitch
(`~/Downloads/stitch_website_development_implementation/`). The app's own
name/branding is unchanged — **"Trip Planner"** with a plane (✈️) logo, not
the mockups' "Voyager" wordmark; only the visual design system is adopted, not
the product name. No backend changes. No new frontend dependencies.

This **supersedes** the palette/layout decisions in
[2026-07-14-visual-redesign-design.md](2026-07-14-visual-redesign-design.md)
(indigo/violet minimal palette) — that pass's shared-component *structure*
(`Button`, `Card`, `Field`, `EmptyState`, `Avatar`) stays and gets restyled in
place rather than rebuilt.

## Source material

Four Stitch-generated mockups (HTML + screenshot each) plus a design-token
file:

- `my_trips/` → `TripsPage.tsx`
- `trip_itinerary_japan_adventure/` → `TripDetailPage.tsx`
- `explore_destinations_grid_view/` → `SearchPage.tsx`
- `destination_details_eiffel_tower/` → `DestinationDetailsPage.tsx`
- `voyage_organized/DESIGN.md` → color/type/spacing/shape tokens (below)

## Governing rule (confirmed with user)

The mockups were AI-generated against an idealized backend and include UI for
features this project's backend does not have. Rule for this pass: **only
build frontend pieces the current backend already supports.** Where a mockup
element needs a new endpoint or a new field on an existing DTO, it is either
dropped or substituted with a real equivalent — never faked with static/mock
data. Each page section below lists what's kept, what's substituted, and what's
dropped, with the reason.

## Design tokens (`src/styles.css`)

Replace the current indigo `brand-*` ramp with the Stitch color tokens. Keep
the `brand-*` naming (consumers don't need to change) but repoint the values:

```css
@theme {
  --color-brand-50: #eef2ff;   /* tinted backgrounds */
  --color-brand-100: #dbe1ff;
  --color-brand-200: #b5c4ff;
  --color-brand-300: #8fa6ff;
  --color-brand-400: #4a72e0;
  --color-brand-500: #1a56db;  /* primary-container */
  --color-brand-600: #003fb1;  /* primary — buttons, active nav, links */
  --color-brand-700: #003dab;  /* hover/pressed */
  --color-brand-800: #00174d;
  --color-brand-900: #00174d;

  --color-action-500: #fd761a; /* secondary/action — Add to Trip, primary CTAs */
  --color-action-600: #ea580c; /* action hover */

  --color-tertiary-500: #00544c; /* success / "Attraction" category tag */

  --font-headline: 'Hanken Grotesk', sans-serif; /* headings */
  --font-body: 'Inter', sans-serif;              /* body/labels (already default) */
}

@layer base {
  body {
    @apply bg-[#f9f9ff] text-[#151c27] antialiased;
    font-family: var(--font-body);
  }
  h1, h2, h3 { font-family: var(--font-headline); }
}
```

Load Hanken Grotesk + Inter via a `<link>` in `index.html` (Google Fonts),
same mechanism the mockups use — no new npm dependency.

- Radius: `rounded-lg` (buttons/inputs/cards, 8px) as the default everywhere;
  `rounded-2xl` reserved for modals/hero images (matches DESIGN.md's
  small/standard/large scale).
- Cards: white bg, 1px `#E2E8F0` border, `shadow-sm` on hover only (flat at
  rest) — slightly flatter than the current always-on shadow.
- One accent rule carried through every page: **blue for navigation/primary
  actions, orange strictly for the one main call-to-action per view**
  ("Add to trip", "Create trip") — not decorative.

## Shared components (`src/components/`) — restyle, no API change

| Component | Change |
|---|---|
| `Button.tsx` | `primary` → brand blue; add `action` variant → orange, used only for "Add to trip"/"Create trip"; `outline`/`ghost` → blue border+text; `danger` unchanged (red, for Remove). |
| `Card.tsx` | Border color → `#E2E8F0`; radius → `rounded-lg`; shadow only on hover. |
| `Field.tsx` | Focus ring → brand blue at 10% opacity, matching DESIGN.md's input spec. |
| `Avatar.tsx` | Unchanged behavior (initials), restyled colors only. |
| `EmptyState.tsx` | Restyled only. |

## App shell (`App.tsx`)

- Nav bar restyled with the existing "✈️ Trip Planner" wordmark (unchanged
  name/logo — only the mockups' *layout*, not their "Voyager" branding, is
  adopted) + underlined active tab (blue), sticky top, `max-w-[1280px]`
  centered content (replaces current `max-w-6xl` — close enough, align to the
  mockup's exact breakpoint).
- Right side keeps the existing pattern: initials `Avatar` + name/email +
  "Log out" pill when authenticated; "Log in"/"Sign up" when not. **No** header
  cloud-sync indicator — that's scoped to the trip detail page only (see
  below), since it reflects real save state, not a fake global autosave.

## Explore / Search page (`SearchPage.tsx`, `AttractionsList.tsx`)

**Kept:** hero heading + `CitySearchInput`, results grid, "No results found"
empty state, per-card "View details" → `/destinations/:id` and existing
`AddToTripButton`.

**Added (client-side only, no backend change — `DestinationSummaryDto`
already has `category` and `rating`):**
- A left filter sidebar: category checkboxes (values derived from whatever
  categories are present in the current result set, not a hardcoded list) and
  a minimum-rating filter (e.g. "4.5 & up", "4.0 & up"), applied by filtering
  the already-fetched array in React state. Filters clear when the searched
  city changes.
- Result count badge reflecting the filtered list.

**Dropped (would need new backend features):**
- "Want a custom plan? Let AI build your itinerary" promo card — no AI
  itinerary generation feature exists.
- Heart/favorite icon on cards — no favorites feature/endpoint.
- Per-category "Reserve"/"Book Now" buttons — no booking system; every card
  keeps the real "View details" + "Add to trip" actions regardless of
  category.
- Fabricated review counts next to rating filters (e.g. "124", "312") — those
  counts computed from the *current page's* result set, not shown as if
  they were global stats.

## Destination Details page (`DestinationDetailsPage.tsx`)

**Kept/restyled:** hero image (existing single `imageUrl` + broken-image
fallback, unchanged logic), name/category, description, address, single-line
opening hours, website link, "Add to trip".

**Added (real feature, reuses the existing attractions endpoint —
`GetAttractionsAsync(latitude, longitude, radiusKm)`, which
`DestinationDetailsDto` already gives us the coordinates for):**
- "Nearby experiences" section: a second `AttractionsList`-style query
  centered on this destination, excluding itself from the results.

**Dropped (would need new backend fields/endpoints):**
- Photo carousel / "Visual Highlights" gallery grid — backend has one
  `imageUrl`, not a photo array.
- Rating badge on this page — `DestinationDetailsDto` has no `Rating` field
  (only `DestinationSummaryDto` does); not shown here.
- Fabricated bento info cards (Dining Inside / Accessibility / Museum
  Boutique / Guided Tours) — static content, not backend data.
- Ticket pricing + "Buy Tickets Now" — no pricing/booking feature.
- "Planner Tip" community testimonial — fabricated user content.
- Mini map + "Getting There" transit list — explicitly deferred (see below).
- Mobile sticky price footer bar — depends on the dropped pricing feature.

**Explicitly deferred (discussed with user):** a small embedded map using the
existing `latitude`/`longitude` fields (F2/US3, already optional/low-priority
per `ASSIGNMENT.md`) was considered — buildable without backend changes, but
would add a new frontend mapping dependency (`react-leaflet` + OSM tiles).
User chose to skip it for this pass.

## My Trips page (`TripsPage.tsx`)

**Kept/restyled:** trip grid cards, create-trip form (converted to the
mockup's modal presentation), empty state, "Plan new trip" CTA (orange
`action` button).

**Adjusted:**
- No per-trip cover photo exists (`TripSummaryDto` has no image field) — cards
  get a deterministic gradient header (e.g. hashed from trip id/name) instead
  of a stock photo.
- Date-relative pill ("In 12 days" / "Past trip") computed client-side from
  `startDate`/`endDate` vs. today — pure presentation, no backend change.
- Destination count badge — already returned as `destinationCount`.
- The modal's "we'll auto-generate a day-by-day plan from your dates" copy is
  kept because it's true (`Trip.SetDates` already does this).

**Dropped (would need new backend support):**
- Per-card "•••" menu (rename/duplicate/delete) — no delete-trip endpoint
  exists today; only rename (via the detail page) and remove-destination are
  implemented.
- Sidebar "Settings"/"Help" links — no such pages/routes exist.

## Trip Itinerary page (`TripDetailPage.tsx`)

**Kept, restyled, logic unchanged:** Saved Places list, day sections,
drag-and-drop (`DestinationList`, `moveLocally`, `handleDrop`, `handleRemove`),
name/dates edit form, all existing error/loading states.

**Layout change:** single stacked list of days → horizontal-scrolling day
columns (the mockup's layout), with Saved Places as a persistent left
sidebar (already sticky on desktop per the prior redesign pass — now
restyled into the mockup's sidebar treatment, including drop-to-remove-back
still going through the same `handleDrop`/`onRemove` handlers).

**Added (pure client-side, no backend change):**
- A search box that filters the Saved Places list already loaded in state.
- A save-status label ("Saved" / "Saving…" / "Unsaved changes") reflecting the
  *real* state of the existing manual name/dates form — not a fake global
  autosave claim, since the app doesn't autosave field-by-field.

**Dropped/substituted (would need new backend support):**
- Per-item category tag / star rating on itinerary & saved-place cards
  (mockup shows "Dining • 4.8★") — `TripDestinationDto` has neither `category`
  nor `rating`; cards show thumbnail + name only.
- Per-day "•••" menu — no delete-day/regenerate-day endpoint.
- "Add Another Day" button — days are derived entirely from the trip's date
  range (`Trip.SetDates`); there's no endpoint to add a day outside it. Not
  shown.
- Per-day "Add Activity" button — substituted with a real link to the Explore
  page (rather than a fake inline add), since actual adding happens through
  `AddToTripButton`'s existing trip/day picker.
- User avatar photo in the nav — substituted with the existing initials
  `Avatar` (no user-photo field/upload feature exists).
- Mobile floating map-icon button — dropped (no map feature, see Details page
  above).

## Out of scope

- Any backend change (new endpoint, new DTO field, new migration).
- Any new npm dependency (no map library, per the deferred decision above).
- Dark mode (still out of scope, per the prior spec).
- True field-by-field autosave (still manual save-on-submit for name/dates).
- Booking/pricing/favorites/AI-itinerary/social-review features — none exist
  in the backend and none are added here.

## Verification

No frontend test runner exists; verification is manual, same gate as the
prior redesign pass:

1. `npm run lint` (`tsc --noEmit`) passes.
2. `npm run dev` + backend running: check each of the 4 pages, including the
   new filter sidebar (Search), nearby-experiences section (Details), trip
   grid + modal (My Trips), and the new day-column layout with drag-and-drop
   still working end-to-end (Trip Itinerary).
3. Confirm every dropped/substituted item above is actually absent (no dead
   buttons that call nothing) rather than present-but-non-functional.
4. Resize to a narrow viewport for each page.

## Working agreement

Per the existing collaboration pattern: the student writes the code, one step
at a time, with review after each step — starting with the design tokens and
shared component restyle, then one page at a time in the order listed above
(Search → Details → My Trips → Trip Itinerary).
