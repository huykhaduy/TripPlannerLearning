# Saved Places Sidebar Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** On desktop (`lg` breakpoint, 1024px+), move the trip detail page's "Saved Places" list into a sticky right-hand sidebar next to the day-by-day itinerary, so it stays in view while dragging destinations between it and any day — with zero change to mobile/tablet layout or to any drag-and-drop logic.

**Architecture:** Wrap the existing days block and the existing Saved Places `<section>` (currently two separate top-level children of the page) in a single flex container that is a plain stacked column below `lg` and a two-column row (flexible day list + fixed 320px sticky sidebar) at `lg` and above. The two-column layout only applies once the trip has at least one day; with zero days, Saved Places stays full-width exactly as today.

**Tech Stack:** React 19, Tailwind v4 (`@tailwindcss/vite`), TypeScript 5 (strict). No new npm dependencies. No frontend test runner exists in this project (`npm run lint` is `tsc --noEmit` only) — verification is manual via `npm run dev`.

## Global Constraints

- Desktop-only. Mobile/tablet (below `lg`, 1024px) render byte-for-byte identically to today — same DOM structure and classes, just without the `lg:*` utilities applying.
- No change to `DestinationList`, `moveLocally`, `handleDrop`, `handleRemove`, `applyTrip`, or any other state/handler in `TripDetailPage.tsx` — this is a JSX-wrapper/className-only change.
- No new npm dependencies.
- Sidebar width: `lg:w-80` (320px), `lg:shrink-0`.
- Sidebar sticky offset: `lg:top-8` (the app header is not sticky/fixed, so no larger offset is needed).
- Breakpoint: Tailwind's `lg` (1024px), matching the breakpoint already used elsewhere in the app for multi-column grids.
- Sidebar layout only applies when `trip.days.length > 0`; with zero days, Saved Places stays full-width at every viewport width.

---

## File Structure

**Modify:**
- `frontend/src/features/trips/TripDetailPage.tsx` — wrap the days block + Saved Places section in the new responsive container; no other file changes.

No files are created or deleted.

---

### Task 1: Responsive two-column layout for days + Saved Places

**Files:**
- Modify: `frontend/src/features/trips/TripDetailPage.tsx:277-361` (the component's `return` statement)

**Interfaces:**
- Consumes: `trip: TripDetail`, `handleRemove`, `handleDrop`, `removingItemId`, `dragItemId`/`setDragItemId` — all already defined earlier in `TripDetailPage` (lines 130-256), unchanged.
- Produces: no new exports, no prop/type changes — `TripDetailPage()` keeps the same signature and behavior; only its rendered markup changes.

- [ ] **Step 1: Add the `hasDays` flag**

In `frontend/src/features/trips/TripDetailPage.tsx`, find the early-return guard just above the main `return`:

```tsx
  if (!trip) {
    return (
      <Card className="mx-auto max-w-2xl">
        <p className="text-sm text-slate-500">Loading trip…</p>
      </Card>
    );
  }

  return (
```

Replace it with (adding one line before `return`):

```tsx
  if (!trip) {
    return (
      <Card className="mx-auto max-w-2xl">
        <p className="text-sm text-slate-500">Loading trip…</p>
      </Card>
    );
  }

  const hasDays = trip.days.length > 0;

  return (
```

- [ ] **Step 2: Replace the days block + Saved Places section with the responsive wrapper**

Find this block (the last two top-level children of the page, right after the `moveError` line):

```tsx
      {trip.days.length === 0 ? (
        <p className="text-sm text-slate-500">Set the trip dates to generate a day-by-day itinerary.</p>
      ) : (
        <div className="flex flex-col gap-6">
          {trip.days.map((day) => (
            <section key={day.id}>
              <h2 className="text-lg font-semibold text-slate-900">
                Day {day.dayNumber} <span className="font-normal text-slate-500">{day.date}</span>
              </h2>
              <DestinationList
                dayId={day.id}
                destinations={day.destinations}
                emptyHint="Nothing planned yet — drag a destination here."
                onRemove={handleRemove}
                removingItemId={removingItemId}
                onDragStart={setDragItemId}
                onDragEnd={() => setDragItemId(null)}
                onDrop={handleDrop}
              />
            </section>
          ))}
        </div>
      )}

      <section>
        <h2 className="text-lg font-semibold text-slate-900">Saved Places</h2>
        <DestinationList
          dayId={null}
          destinations={trip.savedPlaces}
          emptyHint="No saved places — add destinations from the Discover page."
          onRemove={handleRemove}
          removingItemId={removingItemId}
          onDragStart={setDragItemId}
          onDragEnd={() => setDragItemId(null)}
          onDrop={handleDrop}
        />
      </section>
    </div>
  );
}
```

Replace it with:

```tsx
      <div className={hasDays ? 'flex flex-col gap-8 lg:flex-row lg:items-start' : 'flex flex-col gap-8'}>
        <div className={hasDays ? 'flex flex-1 flex-col gap-6' : 'flex flex-col gap-6'}>
          {hasDays ? (
            trip.days.map((day) => (
              <section key={day.id}>
                <h2 className="text-lg font-semibold text-slate-900">
                  Day {day.dayNumber} <span className="font-normal text-slate-500">{day.date}</span>
                </h2>
                <DestinationList
                  dayId={day.id}
                  destinations={day.destinations}
                  emptyHint="Nothing planned yet — drag a destination here."
                  onRemove={handleRemove}
                  removingItemId={removingItemId}
                  onDragStart={setDragItemId}
                  onDragEnd={() => setDragItemId(null)}
                  onDrop={handleDrop}
                />
              </section>
            ))
          ) : (
            <p className="text-sm text-slate-500">Set the trip dates to generate a day-by-day itinerary.</p>
          )}
        </div>

        <div className={hasDays ? 'lg:sticky lg:top-8 lg:w-80 lg:shrink-0' : ''}>
          <section>
            <h2 className="text-lg font-semibold text-slate-900">Saved Places</h2>
            <DestinationList
              dayId={null}
              destinations={trip.savedPlaces}
              emptyHint="No saved places — add destinations from the Discover page."
              onRemove={handleRemove}
              removingItemId={removingItemId}
              onDragStart={setDragItemId}
              onDragEnd={() => setDragItemId(null)}
              onDrop={handleDrop}
            />
          </section>
        </div>
      </div>
    </div>
  );
}
```

Note: `hasDays` replaces the inline `trip.days.length === 0` check used before — same condition, just named and reused for both the day-list branch and the two `className` ternaries.

- [ ] **Step 3: Type-check**

Run: `cd frontend && npm run lint`
Expected: exits 0, no TypeScript errors.

- [ ] **Step 4: Verify desktop sidebar layout**

Run: `npm run dev` with the backend running. Log in, open (or create) a trip with dates set and at least one destination in a day and one in Saved Places. View at ≥1024px width (e.g. a maximized browser window).

Expected:
- Saved Places renders as a ~320px-wide column to the right of the day list, not stacked below it.
- Scroll the page with a day list tall enough to scroll (add several days/destinations if needed, or shrink the window height) — Saved Places stays pinned in view instead of scrolling away.

- [ ] **Step 5: Verify mobile/tablet layout is unchanged**

In the same browser, resize the window below 1024px wide (e.g. 768px).

Expected: layout reverts to today's stacked column — day sections, then "Saved Places" below them, no sidebar width/sticky styling. Visually identical to the layout before this change.

- [ ] **Step 6: Verify the no-days case stays full-width**

Open a trip that has no start/end date set yet (or clear the dates on an existing trip and save).

Expected: at both ≥1024px and <1024px, "Set the trip dates to generate a day-by-day itinerary." and the full-width "Saved Places" section render as a single stacked column — no sidebar, no sticky behavior, matching today's behavior exactly.

- [ ] **Step 7: Verify drag-and-drop still works on desktop**

At ≥1024px width, with a trip that has ≥1 day and ≥1 saved place:
- Drag a Saved Places row into a day — it should move there (optimistic move confirmed by the server, per existing `handleDrop` logic).
- Drag an item from one day to another.
- Drag an item from a day back to Saved Places.

Expected: all three moves still work exactly as before this change (this step only restyles the wrapping containers — `DestinationList`'s own drag handlers are untouched).

- [ ] **Step 8: Commit**

```bash
git add frontend/src/features/trips/TripDetailPage.tsx
git commit -m "style: move Saved Places into a sticky sidebar on desktop"
```

---

## Self-Review Notes

- **Spec coverage:** every confirmed decision in `docs/superpowers/specs/2026-07-14-saved-places-sidebar-design.md` maps to this single task — desktop-only (`lg:` prefix, no unprefixed layout change), sticky sidebar (`lg:sticky lg:top-8`), 320px width (`lg:w-80 lg:shrink-0`), no-days full-width case (`hasDays` ternary), drag-and-drop untouched (only `DestinationList`'s call sites move, not its definition or props).
- **Placeholder scan:** no TBD/TODO; both before/after code blocks in Step 2 are complete and copy-pasteable.
- **Type consistency:** `hasDays` is a plain `boolean` local, not exported or passed as a prop — no signature to keep consistent elsewhere. `DestinationList`'s props (`dayId`, `destinations`, `emptyHint`, `onRemove`, `removingItemId`, `onDragStart`, `onDragEnd`, `onDrop`) are unchanged from the current file at every call site.
