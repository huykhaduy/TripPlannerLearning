# Itinerary Page Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the trip itinerary page (`/trips/:id`) visible without scrolling on load, stop destination names from truncating so aggressively, and make Saved Places and the Day columns look like the same kind of container — by moving the Name/Start date/End date form into a modal and widening/restyling the day-columns row, while extracting a shared `Modal` component so this doesn't become a third copy-pasted dialog implementation.

**Architecture:** A new `frontend/src/components/Modal.tsx` provides the backdrop+panel shell (title header with a close button, padded body) already used ad-hoc by `TripsPage.tsx`'s "Plan new trip" dialog. `TripsPage.tsx` and `AddToTripButton.tsx` are refactored onto it first (proving it works with zero behavior change), then `TripDetailPage.tsx` gets a new "Edit details" button that opens the Name/date form inside the same `Modal`, and finally the day-columns/Saved Places containers are widened and restyled to match each other.

**Tech Stack:** React 19, Tailwind v4 (`@tailwindcss/vite`), TypeScript 5 (strict). No new npm dependencies. No frontend test runner exists in this project (`npm run lint` is `tsc --noEmit` + ESLint) — verification is manual via `npm run dev`.

## Global Constraints

- No new npm dependencies.
- No change to `DestinationList`, drag-and-drop handlers (`handleDrop`, `moveLocally`), `handleRemove`, `wouldRemoveScheduledItems`, or any API call (`updateTrip`, `createTrip`, `addDestination`, `getMyTrips`, `getTrip`) — this is a JSX/className/modal-wiring change only.
- Column width: `w-72` (288px) → `w-80` (320px), for both day columns and the Saved Places sidebar.
- Day columns adopt Saved Places' exact container styling: `rounded-lg border border-[#E2E8F0] bg-white p-4` (replacing `bg-[#F8FAFC] p-3`, no border).
- The shared `Modal` component's shell (header with title + "✕" close button, `border-b border-[#E2E8F0]`, `p-6` body) is lifted verbatim from `TripsPage.tsx`'s existing "Plan new trip" modal — not `AddToTripButton.tsx`'s dialog shell.
- Search page and destination details page are explicitly out of scope (separate follow-up spec).

---

## File Structure

**Create:**
- `frontend/src/components/Modal.tsx` — shared backdrop + titled panel shell used by all three modals below.

**Modify:**
- `frontend/src/features/trips/TripsPage.tsx` — "Plan new trip" modal onto `Modal`.
- `frontend/src/features/destinations/AddToTripButton.tsx` — add-to-trip dialog onto `Modal`.
- `frontend/src/features/trips/TripDetailPage.tsx` — Name/date form moves into an "Edit details" `Modal`; day columns + Saved Places widened and restyled to match.

---

### Task 1: Shared `Modal` component + `TripsPage` refactor

**Files:**
- Create: `frontend/src/components/Modal.tsx`
- Modify: `frontend/src/features/trips/TripsPage.tsx:1-10` (imports), `:88-132` (the modal block)

**Interfaces:**
- Consumes: nothing new — `Modal` is a standalone presentational component.
- Produces: `Modal({ title: string, onClose: () => void, children: ReactNode, maxWidth?: string })` — a React component. `maxWidth` defaults to `'max-w-lg'` and accepts any Tailwind max-width utility class (e.g. `'max-w-md'`). Tasks 2 and 3 import this from `'../../components/Modal'`.

- [ ] **Step 1: Create the `Modal` component**

Create `frontend/src/components/Modal.tsx`:

```tsx
import type { ReactNode } from 'react';

/** Shared modal shell: backdrop (click-outside-to-close) + centered panel with a title/close header and padded body. */
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

- [ ] **Step 2: Add the import to `TripsPage.tsx`**

In `frontend/src/features/trips/TripsPage.tsx`, find:

```tsx
import { EmptyState } from '../../components/EmptyState';
import type { TripSummary } from '../../types';
```

Replace with:

```tsx
import { EmptyState } from '../../components/EmptyState';
import { Modal } from '../../components/Modal';
import type { TripSummary } from '../../types';
```

- [ ] **Step 3: Replace the hand-rolled modal with `Modal`**

Find this block (the entire `{modalOpen && (...)}` section):

```tsx
      {modalOpen && (
        <div
          className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/40 p-4"
          onClick={closeModal}
        >
          <div
            className="w-full max-w-lg rounded-2xl bg-white shadow-2xl"
            onClick={(e) => e.stopPropagation()}
            role="dialog"
            aria-label="Plan a new trip"
          >
            <div className="flex items-center justify-between border-b border-[#E2E8F0] px-6 py-4">
              <h2 className="font-headline text-lg font-semibold text-slate-900">Plan new trip</h2>
              <button
                type="button"
                onClick={closeModal}
                className="text-slate-400 hover:text-slate-600"
                aria-label="Close"
              >
                ✕
              </button>
            </div>
            <form onSubmit={handleCreate} className="flex flex-col gap-4 p-6">
              <Field label="Trip name">
                <input
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                  placeholder="e.g. Summer in Da Nang"
                  required
                  className={fieldControlClass}
                />
              </Field>
              {createError && <p className="text-sm text-red-600">{createError}</p>}
              <div className="flex justify-end gap-2 pt-2">
                <Button type="button" variant="secondary" onClick={closeModal}>
                  Cancel
                </Button>
                <Button type="submit" variant="action" disabled={creating || name.trim() === ''}>
                  {creating ? 'Creating…' : 'Create trip'}
                </Button>
              </div>
            </form>
          </div>
        </div>
      )}
```

Replace with:

```tsx
      {modalOpen && (
        <Modal title="Plan new trip" onClose={closeModal}>
          <form onSubmit={handleCreate} className="flex flex-col gap-4">
            <Field label="Trip name">
              <input
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder="e.g. Summer in Da Nang"
                required
                className={fieldControlClass}
              />
            </Field>
            {createError && <p className="text-sm text-red-600">{createError}</p>}
            <div className="flex justify-end gap-2 pt-2">
              <Button type="button" variant="secondary" onClick={closeModal}>
                Cancel
              </Button>
              <Button type="submit" variant="action" disabled={creating || name.trim() === ''}>
                {creating ? 'Creating…' : 'Create trip'}
              </Button>
            </div>
          </form>
        </Modal>
      )}
```

Note: the form's own `p-6` is dropped since `Modal`'s body wrapper now provides it — the header (`px-6 py-4` + border) and body (`p-6`) padding both come from `Modal`, so nothing here should re-add padding.

- [ ] **Step 4: Type-check**

Run: `cd frontend && npm run lint`
Expected: exits 0, no errors (2 pre-existing `react-refresh/only-export-components` warnings on unrelated files are fine).

- [ ] **Step 5: Verify "Plan new trip" still works**

Run `npm run dev` with the backend running. Log in, go to My Trips, click "+ Plan new trip".

Expected: modal opens with the same header/close-button/border look as before, typing a name and clicking "Create trip" creates the trip and closes the modal, clicking "Cancel" or the backdrop closes it without creating anything, and the "✕" button also closes it.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/components/Modal.tsx frontend/src/features/trips/TripsPage.tsx
git commit -m "refactor: extract shared Modal component, use it for Plan new trip"
```

---

### Task 2: `AddToTripButton` dialog onto `Modal`

**Files:**
- Modify: `frontend/src/features/destinations/AddToTripButton.tsx:1-9` (imports), `:131-214` (the dialog's return statement)

**Interfaces:**
- Consumes: `Modal` from Task 1 (`import { Modal } from '../../components/Modal'`).
- Produces: no change to `AddToTripButton`'s or `AddToTripDialog`'s external behavior — same props, same `onAdded`/`onClose` callbacks.

- [ ] **Step 1: Add the import**

In `frontend/src/features/destinations/AddToTripButton.tsx`, find:

```tsx
import { Button } from '../../components/Button';
import { Field, fieldControlClass } from '../../components/Field';
```

Replace with:

```tsx
import { Button } from '../../components/Button';
import { Field, fieldControlClass } from '../../components/Field';
import { Modal } from '../../components/Modal';
```

- [ ] **Step 2: Replace the dialog's outer shell with `Modal`**

Find `AddToTripDialog`'s `return` statement:

```tsx
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/40 p-4" onClick={onClose}>
      <div
        className="w-full max-w-md rounded-2xl border border-slate-200 bg-white p-6 shadow-lg"
        onClick={(e) => e.stopPropagation()}
        role="dialog"
        aria-label={`Add ${attraction.name} to a trip`}
      >
        <h3 className="text-lg font-semibold text-slate-900">Add “{attraction.name}”</h3>

        {trips === null && !error && <p className="mt-3 text-sm text-slate-500">Loading your trips…</p>}

        {trips !== null && trips.length === 0 && (
          <p className="mt-3 text-sm text-slate-500">
            You have no trips yet — create one on the My trips page first.
          </p>
        )}

        {trips !== null && trips.length > 0 && (
          <div className="mt-4 flex flex-col gap-3">
            <div role="radiogroup" aria-label="Trip" className="flex max-h-64 flex-col gap-2 overflow-y-auto pr-1">
              {trips.map((trip) => {
                const selected = tripId === trip.id;
                return (
                  <button
                    key={trip.id}
                    type="button"
                    aria-pressed={selected}
                    onClick={() => setTripId(trip.id)}
                    className={
                      selected
                        ? 'shrink-0 overflow-hidden rounded-xl border-2 border-brand-600 text-left'
                        : 'shrink-0 overflow-hidden rounded-xl border border-slate-200 text-left hover:border-slate-300'
                    }
                  >
                    <div className="relative h-16 w-full">
                      <TripThumbnail id={trip.id} coverImageUrl={trip.coverImageUrl} />
                      {selected && (
                        <span className="absolute right-2 top-2 rounded-full bg-white/90 px-2 text-sm font-bold text-brand-600">
                          ✓
                        </span>
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

            {tripId && (
              <Field label="Day">
                <select value={dayId} onChange={(e) => setDayId(e.target.value)} className={fieldControlClass}>
                  <option value="">Saved Places (no day yet)</option>
                  {(days ?? []).map((day) => (
                    <option key={day.id} value={day.id}>
                      Day {day.dayNumber} — {day.date}
                    </option>
                  ))}
                </select>
              </Field>
            )}
          </div>
        )}

        {error && <p className="mt-3 text-sm text-red-600">{error}</p>}

        <div className="mt-5 flex justify-end gap-2">
          <Button type="button" variant="secondary" size="sm" onClick={onClose}>
            Cancel
          </Button>
          <Button type="button" size="sm" onClick={handleAdd} disabled={!tripId || adding}>
            {adding ? 'Adding…' : 'Add'}
          </Button>
        </div>
      </div>
    </div>
  );
}
```

Replace with:

```tsx
  return (
    <Modal title={`Add "${attraction.name}"`} onClose={onClose} maxWidth="max-w-md">
      {trips === null && !error && <p className="text-sm text-slate-500">Loading your trips…</p>}

      {trips !== null && trips.length === 0 && (
        <p className="text-sm text-slate-500">You have no trips yet — create one on the My trips page first.</p>
      )}

      {trips !== null && trips.length > 0 && (
        <div className="flex flex-col gap-3">
          <div role="radiogroup" aria-label="Trip" className="flex max-h-64 flex-col gap-2 overflow-y-auto pr-1">
            {trips.map((trip) => {
              const selected = tripId === trip.id;
              return (
                <button
                  key={trip.id}
                  type="button"
                  aria-pressed={selected}
                  onClick={() => setTripId(trip.id)}
                  className={
                    selected
                      ? 'shrink-0 overflow-hidden rounded-xl border-2 border-brand-600 text-left'
                      : 'shrink-0 overflow-hidden rounded-xl border border-slate-200 text-left hover:border-slate-300'
                  }
                >
                  <div className="relative h-16 w-full">
                    <TripThumbnail id={trip.id} coverImageUrl={trip.coverImageUrl} />
                    {selected && (
                      <span className="absolute right-2 top-2 rounded-full bg-white/90 px-2 text-sm font-bold text-brand-600">
                        ✓
                      </span>
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

          {tripId && (
            <Field label="Day">
              <select value={dayId} onChange={(e) => setDayId(e.target.value)} className={fieldControlClass}>
                <option value="">Saved Places (no day yet)</option>
                {(days ?? []).map((day) => (
                  <option key={day.id} value={day.id}>
                    Day {day.dayNumber} — {day.date}
                  </option>
                ))}
              </select>
            </Field>
          )}
        </div>
      )}

      {error && <p className="text-sm text-red-600">{error}</p>}

      <div className="mt-5 flex justify-end gap-2">
        <Button type="button" variant="secondary" size="sm" onClick={onClose}>
          Cancel
        </Button>
        <Button type="button" size="sm" onClick={handleAdd} disabled={!tripId || adding}>
          {adding ? 'Adding…' : 'Add'}
        </Button>
      </div>
    </Modal>
  );
}
```

Note: the `<h3>Add "{attraction.name}"</h3>` title moves into `Modal`'s `title` prop (its header now renders it), so it's dropped from the body. The `mt-3`/`mt-4` top-margin utilities on the first few children are left as-is — they now sit just under `Modal`'s `p-6` body padding, adding a small extra gap that's harmless.

- [ ] **Step 3: Type-check**

Run: `cd frontend && npm run lint`
Expected: exits 0, no errors.

- [ ] **Step 4: Verify the add-to-trip dialog still works**

Run `npm run dev` with the backend running. Log in with a user that has at least one trip with a day, go to Explore, search a city, click "Add to trip" on any attraction.

Expected: the dialog opens with the same trip-card picker and Day field as before (title now in a bordered header row with a "✕" button in addition to Cancel), picking a trip highlights it and reveals the Day field, and clicking "Add" still adds the destination and shows "Added ✓" on the button.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/features/destinations/AddToTripButton.tsx
git commit -m "refactor: use shared Modal for the add-to-trip dialog"
```

---

### Task 3: Trip details move into an "Edit details" modal

**Files:**
- Modify: `frontend/src/features/trips/TripDetailPage.tsx` — imports, new `editOpen` state, `closeEditModal`, `handleSave`, header, and the Name/date form.

**Interfaces:**
- Consumes: `Modal` from Task 1 (`import { Modal } from '../../components/Modal'`), `formatDates` from `frontend/src/features/trips/TripThumbnail.tsx` (`import { formatDates } from './TripThumbnail'` — already exported and used by `TripsPage.tsx`/`AddToTripButton.tsx`).
- Produces: no new exports — `TripDetailPage`'s signature is unchanged, only its internal state and rendered markup change.

- [ ] **Step 1: Add imports**

In `frontend/src/features/trips/TripDetailPage.tsx`, find:

```tsx
import { Card } from '../../components/Card';
import { Field, fieldControlClass } from '../../components/Field';
import { Button } from '../../components/Button';
import type { TripDestination, TripDetail } from '../../types';
```

Replace with:

```tsx
import { Card } from '../../components/Card';
import { Field, fieldControlClass } from '../../components/Field';
import { Button } from '../../components/Button';
import { Modal } from '../../components/Modal';
import type { TripDestination, TripDetail } from '../../types';
import { formatDates } from './TripThumbnail';
```

- [ ] **Step 2: Add `editOpen` state**

Find:

```tsx
  const [savedPlacesQuery, setSavedPlacesQuery] = useState('');
```

Replace with:

```tsx
  const [savedPlacesQuery, setSavedPlacesQuery] = useState('');
  const [editOpen, setEditOpen] = useState(false);
```

- [ ] **Step 3: Add `closeEditModal` and update `handleSave`**

Find:

```tsx
  async function handleSave(event: FormEvent) {
    event.preventDefault();
    if (!tripId) return;

    if (wouldRemoveScheduledItems()) {
      const confirmed = window.confirm(
        'Changing the dates removes days outside the new range and moves their destinations back to Saved Places. Continue?',
      );
      if (!confirmed) return;
    }

    setSaveError(null);
    setSaving(true);
    try {
      // <input type="date"> uses '' for empty — the API wants null.
      const updated = await updateTrip(tripId, name.trim(), startDate || null, endDate || null);
      applyTrip(updated);
    } catch (err) {
      setSaveError(getErrorMessage(err, 'Could not save the trip.'));
    } finally {
      setSaving(false);
    }
  }
```

Replace with:

```tsx
  function closeEditModal() {
    setEditOpen(false);
    if (trip) {
      setName(trip.name);
      setStartDate(trip.startDate ?? '');
      setEndDate(trip.endDate ?? '');
    }
    setSaveError(null);
  }

  async function handleSave(event: FormEvent) {
    event.preventDefault();
    if (!tripId) return;

    if (wouldRemoveScheduledItems()) {
      const confirmed = window.confirm(
        'Changing the dates removes days outside the new range and moves their destinations back to Saved Places. Continue?',
      );
      if (!confirmed) return;
    }

    setSaveError(null);
    setSaving(true);
    try {
      // <input type="date"> uses '' for empty — the API wants null.
      const updated = await updateTrip(tripId, name.trim(), startDate || null, endDate || null);
      applyTrip(updated);
      setEditOpen(false);
    } catch (err) {
      setSaveError(getErrorMessage(err, 'Could not save the trip.'));
    } finally {
      setSaving(false);
    }
  }
```

- [ ] **Step 4: Drop the 4-state save label**

Find:

```tsx
  const hasDays = trip.days.length > 0;

  const isDirty =
    name !== trip.name || startDate !== (trip.startDate ?? '') || endDate !== (trip.endDate ?? '');
  const saveStatusLabel = saving ? 'Saving…' : saveError ? 'Error saving' : isDirty ? 'Unsaved changes' : 'All changes saved';

  const savedPlacesSection = (
```

Replace with:

```tsx
  const hasDays = trip.days.length > 0;

  const savedPlacesSection = (
```

- [ ] **Step 5: Replace the header + Card form with the new header + edit modal**

Find:

```tsx
  return (
    <div className="flex flex-col gap-8">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <Link to="/trips" className="text-sm text-brand-600 hover:underline">
            ← Back to my trips
          </Link>
          <h1 className="font-headline mt-2 text-3xl font-bold tracking-tight text-slate-900">{trip.name}</h1>
        </div>
        <span
          className={`flex items-center gap-2 rounded-full px-3 py-1.5 text-xs font-semibold ${
            saveError ? 'bg-red-50 text-red-600' : 'bg-brand-50 text-brand-600'
          }`}
        >
          <span aria-hidden="true">{saving ? '⏳' : '☁️'}</span>
          {saveStatusLabel}
        </span>
      </div>

      <Card>
        <form onSubmit={handleSave} className="flex flex-col gap-4">
          <Field label="Name">
            <input value={name} onChange={(e) => setName(e.target.value)} required className={fieldControlClass} />
          </Field>
          <div className="flex gap-4">
            <div className="flex-1">
              <Field label="Start date">
                <input
                  type="date"
                  value={startDate}
                  onChange={(e) => setStartDate(e.target.value)}
                  className={fieldControlClass}
                />
              </Field>
            </div>
            <div className="flex-1">
              <Field label="End date">
                <input
                  type="date"
                  value={endDate}
                  onChange={(e) => setEndDate(e.target.value)}
                  className={fieldControlClass}
                />
              </Field>
            </div>
          </div>
          {saveError && <p className="text-sm text-red-600">{saveError}</p>}
          <Button type="submit" disabled={saving || name.trim() === ''} className="self-start">
            {saving ? 'Saving…' : 'Save changes'}
          </Button>
        </form>
      </Card>

      {removeError && <p className="text-sm text-red-600">{removeError}</p>}
```

Replace with:

```tsx
  return (
    <div className="flex flex-col gap-8">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <Link to="/trips" className="text-sm text-brand-600 hover:underline">
            ← Back to my trips
          </Link>
          <h1 className="font-headline mt-2 text-3xl font-bold tracking-tight text-slate-900">{trip.name}</h1>
          <p className="text-sm text-slate-500">{formatDates(trip.startDate, trip.endDate)}</p>
        </div>
        <div className="flex items-center gap-3">
          <span className="flex items-center gap-2 rounded-full bg-brand-50 px-3 py-1.5 text-xs font-semibold text-brand-600">
            <span aria-hidden="true">{saving ? '⏳' : '☁️'}</span>
            {saving ? 'Saving…' : 'All changes saved'}
          </span>
          <Button type="button" variant="secondary" size="sm" onClick={() => setEditOpen(true)}>
            Edit details
          </Button>
        </div>
      </div>

      {editOpen && (
        <Modal title="Edit trip details" onClose={closeEditModal}>
          <form onSubmit={handleSave} className="flex flex-col gap-4">
            <Field label="Name">
              <input value={name} onChange={(e) => setName(e.target.value)} required className={fieldControlClass} />
            </Field>
            <div className="flex gap-4">
              <div className="flex-1">
                <Field label="Start date">
                  <input
                    type="date"
                    value={startDate}
                    onChange={(e) => setStartDate(e.target.value)}
                    className={fieldControlClass}
                  />
                </Field>
              </div>
              <div className="flex-1">
                <Field label="End date">
                  <input
                    type="date"
                    value={endDate}
                    onChange={(e) => setEndDate(e.target.value)}
                    className={fieldControlClass}
                  />
                </Field>
              </div>
            </div>
            {saveError && <p className="text-sm text-red-600">{saveError}</p>}
            <div className="flex justify-end gap-2">
              <Button type="button" variant="secondary" onClick={closeEditModal}>
                Cancel
              </Button>
              <Button type="submit" disabled={saving || name.trim() === ''}>
                {saving ? 'Saving…' : 'Save changes'}
              </Button>
            </div>
          </form>
        </Modal>
      )}

      {removeError && <p className="text-sm text-red-600">{removeError}</p>}
```

- [ ] **Step 6: Type-check**

Run: `cd frontend && npm run lint`
Expected: exits 0, no errors.

- [ ] **Step 7: Verify the itinerary is visible without scrolling**

Run `npm run dev` with the backend running. Open a trip that has dates set and at least one destination in a day and one in Saved Places, at a normal viewport (e.g. 1280×800, not scrolled).

Expected: the header (trip name, date subtitle, save pill, "Edit details" button) and the day columns/Saved Places are both visible without scrolling — no more full-width form pushing them down.

- [ ] **Step 8: Verify the edit modal**

Click "Edit details".

Expected: a modal opens titled "Edit trip details" with Name/Start date/End date fields pre-filled from the trip.

- Type a different name, click "Cancel". Expected: modal closes, header still shows the *original* name (edit was discarded).
- Click "Edit details" again, change the name, click "Save changes". Expected: pill briefly shows "⏳ Saving…", modal closes, header now shows the *new* name and the pill reads "☁️ All changes saved".
- Click "Edit details", change the start/end dates to a range that excludes a day with destinations in it, click "Save changes". Expected: the existing browser `confirm()` dialog ("Changing the dates removes days outside the new range…") still appears before saving, exactly as before this change.

- [ ] **Step 9: Commit**

```bash
git add frontend/src/features/trips/TripDetailPage.tsx
git commit -m "feat: move trip name/date editing into a modal on the itinerary page"
```

---

### Task 4: Widen and unify day-column/Saved Places styling

**Files:**
- Modify: `frontend/src/features/trips/TripDetailPage.tsx` — the day-columns/Saved Places row (below the code touched in Task 3).

**Interfaces:**
- Consumes: `hasDays`, `savedPlacesSection`, `trip.days` — all already defined earlier in the file, unchanged.
- Produces: no new exports or props — visual-only change.

- [ ] **Step 1: Widen and restyle the day columns + Saved Places sidebar**

Find:

```tsx
      {hasDays ? (
        <div className="flex flex-col gap-6 lg:flex-row lg:items-start">
          <div className="lg:sticky lg:top-8 lg:w-72 lg:shrink-0">{savedPlacesSection}</div>

          <div className="flex flex-1 gap-6 overflow-x-auto pb-4">
            {trip.days.map((day) => (
              <section key={day.id} className="flex w-72 shrink-0 flex-col gap-3 rounded-lg bg-[#F8FAFC] p-3">
```

Replace with:

```tsx
      {hasDays ? (
        <div className="flex flex-col gap-6 lg:flex-row lg:items-start">
          <div className="lg:sticky lg:top-8 lg:w-80 lg:shrink-0">{savedPlacesSection}</div>

          <div className="flex flex-1 gap-6 overflow-x-auto pb-4">
            {trip.days.map((day) => (
              <section
                key={day.id}
                className="flex w-80 shrink-0 flex-col gap-3 rounded-lg border border-[#E2E8F0] bg-white p-4"
              >
```

(The rest of the `<section>` — the `<h2>` day header and `<DestinationList>` — is unchanged; only the opening tag's className and formatting changed.)

- [ ] **Step 2: Type-check**

Run: `cd frontend && npm run lint`
Expected: exits 0, no errors.

- [ ] **Step 3: Verify the visual result**

In the running app, open the same trip from Task 3's Step 7 (dates set, destinations in at least 2 days and Saved Places, ideally with one longer destination name like "Monument aux martyrs de la résistance et de la déportation" to make truncation visible).

Expected:
- Day columns and the Saved Places sidebar are visually the same style (white background, border, rounded corners) — no more flat-gray-vs-white-card mismatch.
- Columns are noticeably wider than before; shorter destination names (e.g. "Parc Bigottini") now fit on one line where they might have been borderline before, and only genuinely long names still truncate.
- Drag-and-drop between Saved Places and days, and between days, still works (only the container styling changed, not `DestinationList` or its handlers).

- [ ] **Step 4: Commit**

```bash
git add frontend/src/features/trips/TripDetailPage.tsx
git commit -m "style: widen and unify day-column/Saved Places container styling"
```

---

## Self-Review Notes

- **Spec coverage:** every confirmed decision in `docs/superpowers/specs/2026-08-02-itinerary-page-redesign-design.md` maps to a task — shared `Modal` extraction + both existing call sites refactored (Tasks 1–2), edit-details modal with discard-on-cancel/close-on-save semantics and the simplified 2-state pill (Task 3), column width + unified container styling (Task 4). The out-of-scope list (search page, destination details page, `DestinationList`/drag-and-drop, scrollbar styling) has no corresponding task, as intended.
- **Placeholder scan:** no TBD/TODO; every step shows complete before/after code, not a description of what to change.
- **Type consistency:** `Modal`'s props (`title: string`, `onClose: () => void`, `children: ReactNode`, `maxWidth?: string`) are used identically at all three call sites (Tasks 1–3) — `title` a string/template literal, `onClose` an existing handler, `maxWidth` omitted (defaults to `max-w-lg`) in Tasks 1 and 3, passed `"max-w-md"` in Task 2. `formatDates(startDate: string | null, endDate: string | null)` (from `TripThumbnail.tsx`) is called with `trip.startDate`/`trip.endDate` in Task 3, matching its existing signature used by `TripsPage.tsx` and `AddToTripButton.tsx`. `closeEditModal`/`setEditOpen`/`editOpen` names are consistent between their declaration (Task 3, Steps 2–3) and use (Task 3, Step 5).
