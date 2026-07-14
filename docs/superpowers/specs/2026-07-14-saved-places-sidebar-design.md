# Saved Places Sidebar — Design

**Date:** 2026-07-14
**Scope:** Layout-only change to the trip detail page (`TripDetailPage.tsx`).
Desktop only. No change to drag-and-drop logic, state, handlers, API calls,
or the mobile/tablet experience.

## Context

Today, `TripDetailPage` renders as a single stacked column: the trip
name/date edit form, then each day's `DestinationList` one after another,
then the "Saved Places" `DestinationList` at the very bottom. To drag a
saved place onto a day (or vice versa), the user has to scroll down to
Saved Places, then scroll back up to the target day (or the reverse) — for
a trip with several days, both ends of the drag are rarely on screen at the
same time.

## Problem

Dragging between Saved Places and the day list requires scrolling, because
both live in the same vertical stack and only one can be in view at a time.

## Direction (confirmed with the user)

- **Desktop-only.** Mobile/tablet keep today's stacked layout exactly as-is
  — native HTML5 drag-and-drop (`draggable` + `onDragStart`/`onDragOver`/
  `onDrop`) doesn't fire on touch browsers at all, so there's no drag
  experience on mobile to improve; changing its layout would add complexity
  for no benefit. A touch-friendly reorder mechanism (e.g. a "Move to…"
  control) is explicitly out of scope for this pass.
- **Breakpoint:** `lg` (1024px), matching the breakpoint already used
  elsewhere in the redesign for multi-column grids.
- **Sticky sidebar.** On `lg+`, Saved Places stays pinned in the viewport
  while the day list scrolls beside it — this is the actual fix for the
  scrolling problem, not merely moving it to the side.
- **No-days case stays full-width.** If the trip has no dates set yet
  (`trip.days.length === 0`), Saved Places renders full-width under the
  "Set the trip dates…" message, same as today. The sidebar only appears
  once there's at least one day to drag into.

## Approach

Two ways to build the two-column layout were considered:

| | Approach |
|---|---|
| **Recommended** | Flexbox: `lg:flex lg:items-start lg:gap-8` on the wrapping container; day list is `lg:flex-1`, Saved Places is `lg:w-80 lg:shrink-0 lg:sticky lg:top-8`. Below `lg`, no flex classes apply and both sections render as a plain stacked column (today's behavior, byte-for-byte). |
| Alternative | CSS Grid: `lg:grid lg:grid-cols-[1fr_320px] lg:items-start lg:gap-8`. Functionally equivalent for two columns (one flexible, one fixed-width); flexbox is chosen since it needs fewer utility classes for this specific "flexible + one fixed-width column" shape and keeps the non-`lg` stacked case implicit (no grid template to override). |

Only the JSX structure and `className`s around the existing Days section and
existing Saved Places `<section>` change. `DestinationList`, `moveLocally`,
`handleDrop`, `handleRemove`, and all component state are untouched.

## Layout structure

```
hasDays = trip.days.length > 0

<div className={hasDays ? 'flex flex-col gap-8 lg:flex-row lg:items-start' : 'flex flex-col gap-8'}>
  <div className={hasDays ? 'flex flex-1 flex-col gap-6' : 'flex flex-col gap-6'}>
    {hasDays
      ? days.map(day => <section>...</section>)  {/* unchanged day sections */}
      : <p>Set the trip dates to generate a day-by-day itinerary.</p>}
  </div>

  <div className={hasDays ? 'lg:sticky lg:top-8 lg:w-80 lg:shrink-0' : ''}>
    <section>{/* Saved Places — unchanged markup/logic */}</section>
  </div>
</div>
```

- `lg:w-80` (320px) comfortably fits a destination row (grip glyph +
  thumbnail + name + "Remove" button) without wrapping.
- `lg:top-8` — the app header is not sticky/fixed, so no offset beyond a
  small top gap is needed.
- Below `lg`, the two `className` branches collapse to the same plain
  `flex flex-col` stack in use today, so tablet/mobile rendering is
  byte-for-byte unchanged.

## Out of scope

- Any change to mobile/tablet layout or interaction.
- Touch/pointer-based drag-and-drop support.
- Any change to `DestinationList`, drag handlers, `moveLocally`, or API calls.
- A collapsible/bottom-sheet variant of Saved Places for narrow screens.

## Verification

No frontend test runner exists, so verification is manual:

1. `npm run lint` (`tsc --noEmit`) passes.
2. `npm run dev` + backend running, viewport ≥1024px wide: with a trip that
   has dates set and items in both a day and Saved Places, confirm Saved
   Places renders as a 320px-wide right-hand column and stays in view while
   scrolling a long day list.
3. Same trip, resize below 1024px: confirm the layout reverts to today's
   stacked column (Saved Places below the days, no sidebar/sticky styling).
4. A trip with no dates set: confirm Saved Places renders full-width under
   the "Set the trip dates…" message at all viewport widths.
5. Drag-and-drop still works end-to-end on desktop (Saved Places → a day,
   between days, back to Saved Places) — same as verified in the prior
   visual redesign pass, unaffected by this layout change.
