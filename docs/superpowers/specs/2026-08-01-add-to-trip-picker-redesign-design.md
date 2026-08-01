# Add-to-Trip Picker Redesign — Design

**Date:** 2026-08-01
**Scope:** Visual-only change to the "Add to trip" dialog
(`AddToTripButton.tsx`'s `AddToTripDialog`). No change to data flow, API
calls, day-selection logic, or any other page.

## Context

`AddToTripButton.tsx` renders a button on destination cards
(`AttractionCard.tsx`) and the destination details page
(`DestinationDetailsPage.tsx`). Clicking it opens `AddToTripDialog`, which
lets the user pick one of their trips (plain `<select>`) and, once a trip is
chosen, optionally a day within it (also a plain `<select>`), then calls
`addDestination`.

`TripsPage.tsx` (My Trips) already renders each trip as a card: a
`TripCardHeader` showing the trip's `coverImageUrl` (or a deterministic
gradient fallback keyed by trip id, via `headerGradient()`) plus a status
pill, followed by name, formatted date range (`formatDates()`), and
destination count. `TripSummary` (the type returned by `getMyTrips()`)
already carries all of these fields (`coverImageUrl`, `startDate`,
`endDate`, `destinationCount`).

## Problem

The "Trip" field in `AddToTripDialog` is a bare `<select>` — just trip
names in a native dropdown. It doesn't surface the cover photo, dates, or
destination count that `TripsPage` already displays for the same data,
making the dialog feel visually disconnected from the rest of the app and
harder to scan when the user has several trips.

## Direction (confirmed with the user)

- Redesign only the **Trip** picker. The **Day** field stays a plain
  `<select>` — it's a secondary choice once a trip is picked, and a
  dropdown is adequate there.
- Each trip renders as a **mini card**: a full-width thumbnail banner on
  top (same cover-image/gradient-fallback treatment as `TripCardHeader`,
  just shorter), with name, date range, and destination count stacked in a
  text block below it — mirroring the vertical (image-on-top,
  text-below) arrangement already used on the My Trips page, not a
  horizontal icon+text row.
- Cards render in a **single-column, vertically scrollable list** (no
  grid) inside the dialog.
- No change to the no-trips / loading / error messaging, or the Cancel/Add
  buttons.

## Approach

Extract the gradient/image-fallback rendering and date formatting out of
`TripsPage.tsx` into a shared module so both the My Trips page and the new
picker stay visually in sync without duplicated logic:

| | Approach |
|---|---|
| **Recommended** | New `frontend/src/features/trips/TripThumbnail.tsx` exporting `TripThumbnail({ id, coverImageUrl })` (the image-with-`onError`-fallback-to-gradient rendering, self-contained `failed` state) and `formatDates(startDate, endDate)`. `TripsPage.tsx`'s local `TripCardHeader` becomes a thin wrapper: it renders `TripThumbnail` plus its own status pill (status stays local to `TripsPage`, since the picker has no use for it). `AddToTripButton.tsx` imports `TripThumbnail`/`formatDates` directly. |
| Alternative | Duplicate the gradient/fallback logic directly inside `AddToTripButton.tsx`. Rejected — two independent copies of `headerGradient()` would drift if the visual treatment ever changes. |

Only `TripsPage.tsx` and `AddToTripButton.tsx` change. `DestinationList`,
`addDestination`, `getMyTrips`, `getTrip`, and all dialog state
(`tripId`, `dayId`, `days`, `adding`, `error`) are untouched — the picker
change is `onChange` (select) → `onClick` (card button) setting the same
`tripId` state, nothing downstream of that changes.

## Layout structure

`AddToTripDialog`, replacing the current `<Field label="Trip"><select>`:

```
<div role="radiogroup" aria-label="Trip" className="mt-4 flex max-h-64 flex-col gap-2 overflow-y-auto pr-1">
  {trips.map((trip) => {
    const selected = tripId === trip.id;
    return (
      <button
        key={trip.id}
        type="button"
        aria-pressed={selected}
        onClick={() => setTripId(trip.id)}
        className={selected
          ? 'overflow-hidden rounded-xl border-2 border-brand-600 text-left'
          : 'overflow-hidden rounded-xl border border-slate-200 text-left hover:border-slate-300'}
      >
        <div className="relative h-16 w-full">
          <TripThumbnail id={trip.id} coverImageUrl={trip.coverImageUrl} />
          {selected && (
            <span className="absolute right-2 top-2 rounded-full bg-white/90 px-2 text-brand-600">✓</span>
          )}
        </div>
        <div className="flex flex-col gap-0.5 p-3">
          <strong className="text-sm text-slate-900">{trip.name}</strong>
          <span className="text-xs text-slate-500">{formatDates(trip.startDate, trip.endDate)}</span>
          <span className="text-xs text-slate-500">
            {trip.destinationCount} destination{trip.destinationCount === 1 ? '' : 's'}
          </span>
        </div>
      </button>
    );
  })}
</div>
```

- Dialog width: `max-w-sm` → `max-w-md`, so a ~64px-tall thumbnail plus text
  block doesn't feel cramped.
- `max-h-64 overflow-y-auto` on the list — scrolls once there are enough
  trips to exceed that height; unaffected by trip count otherwise.
- The Day field (unchanged `<select>`) continues to render below this list
  once `tripId` is set, exactly as today.

## Out of scope

- Any change to the Day field, day-fetch effect, or `addDestination` call.
- Any change to `TripsPage`'s own card layout beyond the internal
  `TripCardHeader` → `TripThumbnail` refactor (visually identical output).
- A grid/multi-column arrangement of trip cards.
- Touch/drag interactions, keyboard shortcuts beyond native button
  focus/Enter/Space (which `<button>` gives for free).

## Verification

Visual-only change with no data-flow impact — no automated tests added.
Manual verification:

1. `npm run lint` (`tsc --noEmit` + ESLint) passes.
2. `npm run dev` + backend running: open a destination card or the
   destination details page, click "Add to trip" — confirm the dialog
   shows one mini card per trip with cover photo (or gradient fallback for
   trips with no destinations/photos yet), name, date range, and
   destination count.
3. Click a card — confirm it highlights (border + checkmark) and the Day
   field appears below with that trip's days.
4. With 4+ trips, confirm the list scrolls internally without growing the
   dialog past a reasonable height.
5. Confirm `TripsPage.tsx` (My Trips) still renders identically to before
   the `TripThumbnail` extraction — same cover photos, gradients, and
   status pills.
6. Complete an add (pick trip + day, click Add) — confirm it still
   succeeds and the button flashes "Added ✓", same as before.
