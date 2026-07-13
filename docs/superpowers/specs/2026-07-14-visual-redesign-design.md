# Frontend Visual Redesign — Design

**Date:** 2026-07-14
**Scope:** Visual-only redesign of the existing, already-functional frontend
(all 5 pages + nav + the add-to-trip modal). No backend changes, no new
features, no change to data flow, routing, or the drag-and-drop mechanics in
`TripDetailPage`. Purely: layout, color, type, spacing, and component styling.

## Context

The app works end-to-end (auth, destination search, trip planning) but reads
as generic: every page is the same white `rounded-xl border shadow-sm` card
squeezed into a `max-w-2xl` column, the default Tailwind `blue`/`slate`
palette is used as-is, and the same input/button/card Tailwind class strings
are hand-copied across 8+ files (`TripsPage`, `TripDetailPage`, `LoginPage`,
`RegisterPage`, `DestinationDetailsPage`, `CitySearchInput`,
`AttractionsList`, `AddToTripButton`). Destination photos — the one thing
that should make a travel app feel alive — render as 112px thumbnails.

Stack: React 19 + Tailwind v4 (via `@tailwindcss/vite`) + react-router 7 +
axios. No component library. `src/styles.css` currently only sets a body
background/text color via `@layer base`.

## Direction (confirmed with the user)

- Style: **clean & modern minimal** — generous whitespace, one confident
  accent color, subtle elevation used sparingly, not loud/playful.
- Accent color: **refined indigo/violet**, replacing default `blue-600`.
- Layout: **wider breathing layout** — single centered column, but widened
  from `max-w-2xl` to `max-w-6xl` on `<main>`, so grids can go 3–4 columns.
- Header must show **login status** — logged-in users currently only see
  "Signed in as {email}" buried at the top of `/trips`; it needs to live in
  the persistent header instead.
- Header account UI: **simple inline pill** (name/email + a "Log out"
  button, always visible) — no dropdown menu.
- Dark mode: **out of scope for this pass** (light only).
- Photos should be visually prominent (attraction cards, destination detail
  hero, and — new — thumbnails in the trip itinerary rows, which currently
  show no image at all).

## Approach

Introduce a small shared UI kit instead of re-typing new Tailwind strings
independently in every file (the current duplication is exactly how the
"inconsistent in places" complaint happens). Every page in this pass gets
touched anyway, so the shared components pay for themselves immediately and
keep future pages consistent for free.

## Design tokens (`src/styles.css`)

Extend the Tailwind v4 theme via `@theme` rather than hard-coding hex values
in components:

```css
@theme {
  --color-brand-50: #f5f3ff;
  --color-brand-100: #ede9fe;
  --color-brand-200: #ddd6fe;
  --color-brand-300: #c4b5fd;
  --color-brand-400: #a78bfa;
  --color-brand-500: #8b5cf6;
  --color-brand-600: #6d5bd0; /* primary buttons, active nav link */
  --color-brand-700: #5b48b8; /* hover state for brand-600 */
  --color-brand-800: #453679;
  --color-brand-900: #362a5e;
}
```

(An indigo/violet ramp — `brand-600`/`brand-700` are the two shades actually
used for interactive elements; the rest exist for tints/borders/hover states
as needed.)

- Replace `blue-*` utility usage with `brand-*` throughout.
- Neutrals stay Tailwind `slate`, but usage narrows to a deliberate subset:
  `slate-50` (page bg / subtle fills), `slate-200` (borders),
  `slate-500` (secondary text), `slate-900` (primary text) — not the current
  ad-hoc mix of 400/500/600/700/800.
- One card radius (`rounded-2xl`) and one shadow step (`shadow-sm` on
  photos/popovers/modals only — flat elsewhere) instead of shadow-on-everything.
- Type scale: page titles `text-3xl font-semibold tracking-tight`; section
  headings `text-lg font-semibold`; body/secondary unchanged size but fewer
  distinct grays.

## Shared components (`src/components/`, new)

| Component | Purpose | Replaces |
|---|---|---|
| `Button.tsx` | `primary` \| `secondary` \| `danger` variants, `disabled`/loading text handled by caller as today | The repeated `rounded-lg bg-blue-600 px-4 py-2...` / border-only variants in every form |
| `Field.tsx` | Label + input/select wrapper, consistent focus ring | The repeated `flex flex-col gap-1.5 text-sm text-slate-500` + input class pairs |
| `Card.tsx` | The one card shell (radius/border/padding) | The repeated `rounded-xl border border-slate-200 bg-white p-6 shadow-sm sm:p-8` wrapper on every page |
| `EmptyState.tsx` | Centered icon/text for "no trips yet", "no attractions found", etc. | Ad-hoc `<p className="text-sm text-slate-500">` empty messages |
| `Avatar.tsx` | Small circle with initial, used in the header pill | New — doesn't exist today |

These are presentational only — no new state, no new API calls, no behavior
change. Each page's existing logic (fetching, form handling, drag-and-drop)
is untouched; only the JSX/className layer changes.

## Page-by-page treatment

### App shell (`App.tsx`)
- `<main>` widens to `max-w-6xl`.
- Header: brand-colored logo/active-link, more vertical padding, subtle
  bottom border.
- Right side of nav:
  - Logged out: "Log in" link + filled "Sign up" button (currently no
    sign-up entry point exists in the nav at all).
  - Logged in: inline pill — display name (fallback to email) + "Log out" —
    replacing the line currently on `TripsPage`.
- Nav wraps at narrow widths; no hamburger (only 2–3 items).

### Discover (`SearchPage.tsx`, `CitySearchInput.tsx`)
- Page opens with a heading + subhead instead of starting cold at the input.
- Search input restyled as a prominent, larger rounded search bar.
- Autocomplete dropdown: brand-colored hover state.

### Attractions (`AttractionsList.tsx`)
- `AttractionCard` photo grows from 112px to a ~4:3 hero filling the card
  top; grid moves to 3 columns on desktop (room exists now that the shell is
  wider).
- Filters row (`category`/`rating`/`sort`) restyled with `Field`.
- 🏛️ emoji fallback for missing photos stays, restyled to match the new card.

### Destination details (`DestinationDetailsPage.tsx`)
- Larger, full-width hero photo (same `onError` fallback logic, unchanged).
- Address/hours/website block becomes a cleaner stacked list instead of the
  current cramped inline `dt`/`dd` pairs.

### Trips list (`TripsPage.tsx`)
- Trip rows become a card grid (2–3 columns): name, date range, destination
  count, using the same card language as attraction cards.
- Create-trip form becomes its own lightweight inline block above the grid,
  not sharing one big card with the list.
- "Signed in as…" line removed (now in the header).

### Trip detail (`TripDetailPage.tsx`)
- Drag-and-drop logic, handlers, and state are **unchanged** — this is a
  restyle of the existing `DestinationList`/row markup only.
- Each destination row becomes a small card with a thumbnail (new — the row
  currently shows no image even though `TripDestinationDto.imageUrl` exists).
- Clearer drag affordance (grip cue / cursor styling) and a more obvious
  dashed drop-target look on empty day buckets.
- "Day N" section headings restyled to the new heading scale.

### Add-to-trip modal (`AddToTripButton.tsx`)
- Overlay/panel restyled to match the new card language (`rounded-2xl`,
  softer overlay, brand primary button on "Add"). Same open/close/loading
  logic.

### Auth (`LoginPage.tsx`, `RegisterPage.tsx`)
- Centered `Card` built from the new `Field`/`Button` primitives; smaller
  visual change than the rest since these forms are already simple.
- Footer cross-links (`Log in` ↔ `Sign up`) stay as a fallback now that
  sign-up is also reachable from the header.

## Out of scope

- Dark mode.
- Any backend change.
- Any change to drag-and-drop behavior, API calls, validation, or routing.
- New npm dependencies (Tailwind v4 + existing stack is sufficient).
- Header dropdown/menu interaction (explicitly declined in favor of the
  inline pill).
- Mobile hamburger nav.

## Verification

No frontend test runner exists, so verification is manual per page, plus the
existing type-check gate:

1. `npm run lint` (`tsc --noEmit`) passes — the redesign must not introduce
   type errors in the new shared components or their usage.
2. `npm run dev` + backend running: visually check each page (Discover with
   a searched city, a destination's detail page, Login, Register, My trips
   with ≥1 trip, a trip's detail page with items in Saved Places and at
   least one scheduled day).
3. Confirm drag-and-drop still works end-to-end after the row restyle
   (drag a saved place into a day, drag between days, drag back to Saved
   Places).
4. Confirm the header shows "Log in"/"Sign up" when logged out and the
   name/email + "Log out" pill when logged in, and that "Log out" still
   works.
5. Resize to a narrow viewport — header wraps without overlapping content;
   card grids collapse to 1–2 columns.

## Working agreement

Student writes the code; Claude guides one step at a time, reviewing each
step before moving to the next (per established learning workflow) —
starting with the shared design tokens/components, then one page at a time
in the order listed above.
