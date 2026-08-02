# Itinerary Page Redesign — Design

**Date:** 2026-08-02
**Scope:** Layout/visual changes to `TripDetailPage.tsx`, plus extracting a
shared `Modal` component that it (and two existing modals) will use. No
change to drag-and-drop logic, day/destination data, or API calls.

## Context

`TripDetailPage.tsx` (the trip itinerary page, `/trips/:id`) currently
renders a full-width `Card` containing the Name/Start date/End date edit
form directly in the page flow, above the day columns and Saved Places.
Day columns (`trip.days.map(...)`) are flat `bg-[#F8FAFC]` boxes with no
border; Saved Places is a white bordered card. Both use `w-72` (288px).

Live review (screenshots taken via a connected browser against real test
data — a 3-day trip with 7 destinations) surfaced three concrete problems:

1. The edit form is large enough that, on a normal viewport, it's the only
   thing visible on page load — the itinerary itself (the actual point of
   the page) requires scrolling past it.
2. Destination names truncate aggressively ("Charles de Ga…", "Square de
   Ver…", "Monument aux martyrs…") because after the drag handle,
   thumbnail, and remove button, a 288px column leaves very little room
   for text.
3. Saved Places (white, bordered) and Day columns (flat gray, no border)
   are visually two different container styles for conceptually the same
   thing — a bucket of destinations.

## Direction (confirmed with the user)

- **Edit form → modal.** Move Name/Start date/End date into a modal opened
  by an "Edit details" button, so the itinerary is visible immediately.
- **Extract a shared `Modal` component**, used by this new modal *and* the
  two existing hand-rolled modals (`TripsPage.tsx`'s "Plan new trip",
  `AddToTripButton.tsx`'s add-to-trip dialog) — this is the third copy of
  the same backdrop+panel pattern, so it's extracted rather than
  duplicated again, and the two existing ones are refactored onto it for
  consistency.
- **Widen columns**: `w-72` → `w-80` (288px → 320px) for both Day columns
  and the Saved Places sidebar.
- **Unify container styling**: Day columns adopt Saved Places' current
  look (`rounded-lg border border-[#E2E8F0] bg-white p-4`), replacing the
  flat gray/borderless treatment.
- Out of scope: any change to `DestinationList`, drag-and-drop, the
  dashed-border empty-state box, or the horizontal scrollbar's default
  browser styling.

## Approach

### Shared `Modal` component

New `frontend/src/components/Modal.tsx`:

```tsx
export function Modal({
  title,
  onClose,
  children,
  maxWidth = 'max-w-lg',
}: {
  title: string;
  onClose: () => void;
  children: ReactNode;
  maxWidth?: string;
}) {
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/40 p-4" onClick={onClose}>
      <div
        className={`w-full ${maxWidth} rounded-2xl bg-white shadow-2xl`}
        onClick={(e) => e.stopPropagation()}
        role="dialog"
        aria-label={title}
      >
        <div className="flex items-center justify-between border-b border-[#E2E8F0] px-6 py-4">
          <h2 className="font-headline text-lg font-semibold text-slate-900">{title}</h2>
          <button type="button" onClick={onClose} className="text-slate-400 hover:text-slate-600" aria-label="Close">
            ✕
          </button>
        </div>
        <div className="p-6">{children}</div>
      </div>
    </div>
  );
}
```

This is `TripsPage.tsx`'s existing "Plan new trip" modal shell, lifted
as-is (chosen over `AddToTripButton.tsx`'s shell since it already has an
explicit close button, not just Cancel/backdrop-click).

| | Approach |
|---|---|
| **Recommended** | Extract `Modal`; refactor all three call sites (`TripsPage`, `AddToTripButton`, new `TripDetailPage` edit modal) onto it. |
| Alternative | Add a third hand-rolled backdrop+panel copy just for `TripDetailPage`, leave the other two as-is. Rejected — three near-identical implementations to keep in sync is worse than one shared component, and the refactor is small (each call site just moves its existing header/body markup inside `<Modal>`). |

Call-site changes:
- `TripsPage.tsx`: the create-trip modal's outer `<div className="fixed inset-0 ...">...</div>` (including its own header row) is replaced by `<Modal title="Plan new trip" onClose={closeModal}>{form}</Modal>`. Form logic (`handleCreate`, `name`/`creating`/`createError` state) is untouched.
- `AddToTripButton.tsx`: `AddToTripDialog`'s outer div/header (`<h3>Add "{name}"</h3>`) is replaced by `<Modal title={`Add "${attraction.name}"`} onClose={onClose} maxWidth="max-w-md">{content}</Modal>`, keeping the trip-card picker and Day field as `Modal`'s children. No change to `tripId`/`dayId`/`days`/`adding`/`error` state or the `addDestination` call.

### TripDetailPage header + edit modal

New page-level state: `editOpen` (boolean).

Header becomes:

```tsx
<div className="flex flex-wrap items-center justify-between gap-3">
  <div>
    <Link to="/trips">← Back to my trips</Link>
    <h1>{trip.name}</h1>
    <p className="text-sm text-slate-500">{formatDates(trip.startDate, trip.endDate)}</p>
  </div>
  <div className="flex items-center gap-3">
    <span>{saving ? '⏳ Saving…' : '☁️ All changes saved'}</span>
    <Button variant="secondary" size="sm" onClick={() => setEditOpen(true)}>Edit details</Button>
  </div>
</div>

{editOpen && (
  <Modal title="Edit trip details" onClose={closeEditModal}>
    <form onSubmit={handleSave} className="flex flex-col gap-4">
      {/* existing Name / Start date / End date Fields, unchanged */}
      {saveError && <p className="text-sm text-red-600">{saveError}</p>}
      <div className="flex justify-end gap-2">
        <Button type="button" variant="secondary" onClick={closeEditModal}>Cancel</Button>
        <Button type="submit" disabled={saving || name.trim() === ''}>
          {saving ? 'Saving…' : 'Save changes'}
        </Button>
      </div>
    </form>
  </Modal>
)}
```

`formatDates` is imported from `TripThumbnail.tsx` (already shared between
`TripsPage` and `AddToTripButton`) — same "No dates yet" fallback.

Behavior:
- `closeEditModal` = `setEditOpen(false)` **plus** resetting `name`/
  `startDate`/`endDate` back to `trip.name`/`trip.startDate ?? ''`/
  `trip.endDate ?? ''` — Cancel (or backdrop-click) always discards
  in-progress edits, matching the create-trip modal's convention.
- `handleSave` gains `setEditOpen(false)` after a successful `applyTrip(updated)` —
  the modal closes on success.
- On failure, the modal stays open with `saveError` shown inline (unchanged
  logic, just now rendered inside the modal instead of the page body). The
  existing `wouldRemoveScheduledItems()` confirm-dialog check before saving
  is untouched.
- The page-level pill drops from 4 states to 2 (`saving` / rest) — "Unsaved
  changes" and "Error saving" no longer apply outside the modal, since
  there's no persisted draft once it's closed, and errors are visible
  in-context while the modal is open.

### Column width + unified styling

- `TripDetailPage.tsx`'s Saved Places wrapper: `lg:w-72` → `lg:w-80`.
- Day column `<section>`: `w-72` → `w-80`, and
  `bg-[#F8FAFC]` (no border) → `border border-[#E2E8F0] bg-white`
  (padding `p-3` → `p-4` to match Saved Places exactly).
- Saved Places' own container class is unchanged (it's already the target
  style) besides the width bump.

## Out of scope

- `DestinationList`, drag-and-drop handlers, `moveLocally`, API calls.
- The dashed-border empty-state box styling (unaffected by the
  white/bordered column background — still reads fine, arguably better,
  against a white background).
- Horizontal scrollbar styling under the day-columns row.
- Any change to the search page or destination details page (tracked as a
  separate follow-up spec).

## Verification

No frontend test runner — manual verification:

1. `npm run lint` (`tsc --noEmit` + ESLint) passes.
2. My Trips page: "Plan new trip" modal still opens, creates a trip, and
   closes correctly with the shared `Modal` shell.
3. A destination card's "Add to trip" dialog still works end-to-end (trip
   card picker, Day field, add) with the shared `Modal` shell.
4. Trip detail page loads with the day columns/Saved Places visible
   without scrolling (normal viewport).
5. "Edit details" opens the modal; Cancel discards typed changes; Save
   persists, closes the modal, and updates the header's name/date
   subtitle; a date change that would remove scheduled destinations still
   triggers the existing confirm dialog.
6. Day columns and Saved Places render with matching white/bordered
   styling at the new `w-80` width; destination names truncate less often.
