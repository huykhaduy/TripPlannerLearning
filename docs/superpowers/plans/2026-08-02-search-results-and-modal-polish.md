# Search Results & Modal Polish Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Collapse the search page's hero once a city is selected, remove the search page's rating filter/sort (which is permanently dead — the real data provider never supplies ratings), and add basic accessibility (Escape-to-close, focus management, `aria-modal`) to the shared `Modal` component used by all 3 existing dialogs.

**Architecture:** Three independent, single-file changes with no shared state or ordering dependency between them — `SearchPage.tsx`'s hero becomes a ternary on its existing `selectedCity` value, `AttractionsList.tsx` drops the rating-related state/UI/logic it already isolates behind `minRating`/`sortBy`, and `Modal.tsx` gains a focus-management `useEffect`.

**Tech Stack:** React 19, Tailwind v4 (`@tailwindcss/vite`), TypeScript 5 (strict). No new npm dependencies. No frontend test runner exists in this project (`npm run lint` is `tsc --noEmit` + ESLint) — verification is manual via `npm run dev`.

## Global Constraints

- No new npm dependencies.
- No backend changes — `AttractionSummary.rating` (frontend type) and `DestinationSummaryDto.Rating` (backend DTO) are untouched; only the *filter/sort UI* that reads `rating` is removed.
- `AttractionCard.tsx`'s `{attraction.rating != null && <span>⭐ ...</span>}` badge is untouched.
- `DestinationDetailsPage.tsx` is untouched (no rating added there — see spec's Context section for why).
- No manual Tab-cycle focus trap in `Modal` — explicitly out of scope.
- Hero collapse is permanent once a city is selected (not scroll-triggered), and stays collapsed across subsequent city changes.

---

## File Structure

**Modify:**
- `frontend/src/features/destinations/SearchPage.tsx` — hero becomes conditional on `selectedCity`.
- `frontend/src/features/destinations/AttractionsList.tsx` — remove `minRating`/`sortBy` state, their UI, and their filter/sort logic.
- `frontend/src/components/Modal.tsx` — add `aria-modal`, Escape-to-close, and focus management.

No files created or deleted.

---

### Task 1: Search hero collapses once a city is selected

**Files:**
- Modify: `frontend/src/features/destinations/SearchPage.tsx:42-50`

**Interfaces:**
- Consumes: `selectedCity: LocationSuggestion | null`, `handleSelect`, `CitySearchInput` — all already defined earlier in the file, unchanged.
- Produces: no new exports, no prop/type changes — `SearchPage()`'s signature and behavior (URL-driven city selection) are unchanged; only the hero's rendered markup changes.

- [ ] **Step 1: Make the hero conditional on `selectedCity`**

Find:

```tsx
  return (
    <div className="flex flex-col gap-8">
      <div className="rounded-3xl bg-brand-600 px-6 py-14 text-center sm:py-20">
        <h1 className="font-headline text-4xl font-bold tracking-tight text-white sm:text-5xl">Where to next?</h1>
        <p className="mt-3 text-brand-100">Search a city to see its recommended attractions.</p>
        <div className="mx-auto mt-6 max-w-xl">
          <CitySearchInput onSelect={handleSelect} initialCity={selectedCity} />
        </div>
      </div>

      {selectedCity && <AttractionsList city={selectedCity} />}
```

Replace with:

```tsx
  return (
    <div className="flex flex-col gap-8">
      {selectedCity ? (
        <div className="rounded-2xl bg-brand-600 px-4 py-3">
          <div className="mx-auto max-w-xl">
            <CitySearchInput onSelect={handleSelect} initialCity={selectedCity} />
          </div>
        </div>
      ) : (
        <div className="rounded-3xl bg-brand-600 px-6 py-14 text-center sm:py-20">
          <h1 className="font-headline text-4xl font-bold tracking-tight text-white sm:text-5xl">Where to next?</h1>
          <p className="mt-3 text-brand-100">Search a city to see its recommended attractions.</p>
          <div className="mx-auto mt-6 max-w-xl">
            <CitySearchInput onSelect={handleSelect} initialCity={selectedCity} />
          </div>
        </div>
      )}

      {selectedCity && <AttractionsList city={selectedCity} />}
```

- [ ] **Step 2: Type-check**

Run: `cd frontend && npm run lint`
Expected: exits 0, no errors (2 pre-existing `react-refresh/only-export-components` warnings on unrelated files are fine).

- [ ] **Step 3: Verify the collapse behavior**

Run `npm run dev` with the backend running. Go to Explore (the search page) with no city selected.

Expected:
- No city selected: full-size hero ("Where to next?" title, subtitle, search box) — same as before this change.
- Search and select a city: hero collapses to a slim bar with just the search input (no title/subtitle), and the attraction grid is visible right below it.
- With a city already selected, search and select a *different* city: hero stays collapsed (does not revert to the full-size version).

- [ ] **Step 4: Commit**

```bash
git add frontend/src/features/destinations/SearchPage.tsx
git commit -m "feat: collapse search hero to a compact bar once a city is selected"
```

---

### Task 2: Remove the dead rating filter/sort

**Files:**
- Modify: `frontend/src/features/destinations/AttractionsList.tsx`

**Interfaces:**
- Consumes: `AttractionSummary` (unchanged type, `rating` field left in place), `getAttractions`, `AttractionCard`, `EmptyState`, `Button` — all already defined/imported, unchanged.
- Produces: no new exports — `AttractionsList({ city })`'s signature and behavior (category filtering, attraction grid) are unchanged; `minRating`/`sortBy` state and the UI/logic that used them are removed entirely.

- [ ] **Step 1: Drop the `fieldControlClass` import**

Find:

```tsx
import { getAttractions } from '../../api/destinations';
import { AttractionCard } from './AttractionCard';
import { EmptyState } from '../../components/EmptyState';
import { fieldControlClass } from '../../components/Field';
import { Button } from '../../components/Button';
```

Replace with:

```tsx
import { getAttractions } from '../../api/destinations';
import { AttractionCard } from './AttractionCard';
import { EmptyState } from '../../components/EmptyState';
import { Button } from '../../components/Button';
```

(`fieldControlClass` was only used by the "Sort by" `<select>` removed in Step 4 below — dropped here so it isn't left as an unused import.)

- [ ] **Step 2: Remove the `minRating`/`sortBy` state**

Find:

```tsx
  // F1/US4-US5 — filters and sort are frontend concerns over the ≤20 loaded
  // results (spec §11.2); no API parameters involved.
  const [categoryFilter, setCategoryFilter] = useState('');
  const [minRating, setMinRating] = useState(''); // '' = any; otherwise a number as string
  const [sortBy, setSortBy] = useState<'recommended' | 'rating'>('recommended');
```

Replace with:

```tsx
  // F1/US4 — category filtering is a frontend concern over the ≤20 loaded
  // results (spec §11.2); no API parameters involved.
  const [categoryFilter, setCategoryFilter] = useState('');
```

- [ ] **Step 3: Drop the reset calls for the removed state**

Find:

```tsx
    setLoading(true);
    setError(null);
    // A new city means new results — stale filters would silently hide them.
    setCategoryFilter('');
    setMinRating('');
    setSortBy('recommended');
    getAttractions(city.latitude, city.longitude)
```

Replace with:

```tsx
    setLoading(true);
    setError(null);
    // A new city means new results — a stale filter would silently hide them.
    setCategoryFilter('');
    getAttractions(city.latitude, city.longitude)
```

- [ ] **Step 4: Simplify `visible` and `hasActiveFilters`**

Find:

```tsx
  const visible = useMemo(() => {
    const filtered = attractions.filter(
      (a) =>
        (categoryFilter === '' || a.category === categoryFilter) &&
        (minRating === '' || (a.rating != null && a.rating >= Number(minRating))),
    );
    // "Recommended" keeps the API's order; rating sort puts unrated last (US5
    // keeps the filters because it sorts the already-filtered list).
    return sortBy === 'rating'
      ? [...filtered].sort((a, b) => (b.rating ?? -1) - (a.rating ?? -1))
      : filtered;
  }, [attractions, categoryFilter, minRating, sortBy]);

  const hasActiveFilters = categoryFilter !== '' || minRating !== '';
```

Replace with:

```tsx
  const visible = useMemo(
    () => attractions.filter((a) => categoryFilter === '' || a.category === categoryFilter),
    [attractions, categoryFilter],
  );

  const hasActiveFilters = categoryFilter !== '';
```

- [ ] **Step 5: Remove the "Minimum rating" section and the "Sort by" dropdown**

Find:

```tsx
        <div className="mt-6">
          <p className="mb-2 text-xs font-semibold uppercase tracking-wide text-[#434654]">Minimum rating</p>
          <div className="flex flex-col gap-1">
            {(['', '3', '4'] as const).map((value) => (
              <button
                key={value || 'any'}
                type="button"
                onClick={() => setMinRating(value)}
                className={`rounded-md px-2 py-1.5 text-left text-sm ${
                  minRating === value ? 'bg-brand-50 font-semibold text-brand-600' : 'text-slate-700 hover:bg-slate-50'
                }`}
              >
                {value === '' ? 'Any rating' : `⭐ ${value}+ stars`}
              </button>
            ))}
          </div>
        </div>

        <label className="mt-6 flex flex-col gap-1.5 text-sm text-slate-500">
          Sort by
          <select
            value={sortBy}
            onChange={(e) => setSortBy(e.target.value as 'recommended' | 'rating')}
            className={fieldControlClass}
          >
            <option value="recommended">Recommended</option>
            <option value="rating">Highest rating</option>
          </select>
        </label>

        {hasActiveFilters && (
          <Button
            type="button"
            variant="secondary"
            size="sm"
            onClick={() => {
              setCategoryFilter('');
              setMinRating('');
            }}
            className="mt-4 w-full"
          >
            Clear filters
          </Button>
        )}
```

Replace with:

```tsx
        {hasActiveFilters && (
          <Button
            type="button"
            variant="secondary"
            size="sm"
            onClick={() => setCategoryFilter('')}
            className="mt-4 w-full"
          >
            Clear filters
          </Button>
        )}
```

- [ ] **Step 6: Type-check**

Run: `cd frontend && npm run lint`
Expected: exits 0, no errors.

- [ ] **Step 7: Verify the filters sidebar**

Run `npm run dev` with the backend running. Search a city on the Explore page.

Expected:
- The Filters sidebar shows "Category" (All destinations + each category) only — no "Minimum rating" section, no "Sort by" dropdown.
- Clicking a category still filters the attraction grid correctly.
- "Clear filters" still appears when a category is selected, and clicking it resets to "All destinations".

- [ ] **Step 8: Commit**

```bash
git add frontend/src/features/destinations/AttractionsList.tsx
git commit -m "fix: remove dead rating filter and sort options from search results"
```

---

### Task 3: Modal accessibility — Escape, focus management, `aria-modal`

**Files:**
- Modify: `frontend/src/components/Modal.tsx`

**Interfaces:**
- Consumes: nothing new.
- Produces: `Modal`'s props (`title`, `onClose`, `children`, `maxWidth?`) are unchanged — this task only changes `Modal`'s internal behavior, so `TripsPage.tsx`, `AddToTripButton.tsx`, and `TripDetailPage.tsx` (its 3 existing call sites) require no changes.

- [ ] **Step 1: Add refs and a focus-management effect**

Find the full current file:

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
        className={`max-h-[90vh] w-full overflow-y-auto ${maxWidth} rounded-2xl bg-white shadow-2xl`}
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

Replace it entirely with:

```tsx
import { useEffect, useRef, type ReactNode } from 'react';

/**
 * Shared modal shell: backdrop (click-outside-to-close) + centered panel
 * with a title/close header and padded body. On mount: moves focus into the
 * panel and starts listening for Escape; on unmount: restores focus to
 * whatever was focused before the modal opened. No Tab-cycle focus trap —
 * Tab can still move focus to page content behind the backdrop.
 */
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
  const panelRef = useRef<HTMLDivElement>(null);
  const previouslyFocused = useRef<HTMLElement | null>(null);

  useEffect(() => {
    previouslyFocused.current = document.activeElement as HTMLElement | null;
    panelRef.current?.focus();

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') onClose();
    }
    document.addEventListener('keydown', handleKeyDown);

    return () => {
      document.removeEventListener('keydown', handleKeyDown);
      previouslyFocused.current?.focus();
    };
  }, [onClose]);

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/40 p-4" onClick={onClose}>
      <div
        ref={panelRef}
        tabIndex={-1}
        className={`max-h-[90vh] w-full overflow-y-auto ${maxWidth} rounded-2xl bg-white shadow-2xl`}
        onClick={(e) => e.stopPropagation()}
        role="dialog"
        aria-modal="true"
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

- [ ] **Step 2: Type-check**

Run: `cd frontend && npm run lint`
Expected: exits 0, no errors.

- [ ] **Step 3: Verify each of the 3 modals**

Run `npm run dev` with the backend running, logged in with at least one trip.

For each of: My Trips → "+ Plan new trip"; a destination card's "Add to trip"; a trip detail page's "Edit details" —

Expected:
- Opening the modal moves focus into it (e.g. pressing Tab immediately after opening moves focus to the first focusable element inside the modal, not somewhere on the page behind it).
- Pressing `Escape` closes the modal (same as clicking "✕" or the backdrop).
- After closing (via Escape, "✕", Cancel, or a successful submit), focus returns to the button that opened the modal (visible focus ring on that button, or confirmable via keyboard: pressing Tab/Shift+Tab next moves relative to that button, not from the top of the page).

- [ ] **Step 4: Commit**

```bash
git add frontend/src/components/Modal.tsx
git commit -m "feat: add aria-modal, Escape-to-close, and focus management to Modal"
```

---

## Self-Review Notes

- **Spec coverage:** every confirmed decision in `docs/superpowers/specs/2026-08-02-search-results-and-modal-polish-design.md` maps to a task — hero collapse (Task 1), rating filter/sort removal including the "remove both sections entirely, not just the broken options" decision (Task 2), and `aria-modal`/Escape/focus management with no Tab-cycle trap (Task 3). The out-of-scope list (backend changes, `AttractionCard`'s badge, `DestinationDetailsPage.tsx`, a focus trap) has no corresponding task, as intended.
- **Placeholder scan:** no TBD/TODO; every step shows complete before/after code.
- **Type consistency:** `Modal`'s exported signature (`title: string`, `onClose: () => void`, `children: ReactNode`, `maxWidth?: string`) is unchanged by Task 3, so the three existing call sites (`TripsPage.tsx`, `AddToTripButton.tsx`, `TripDetailPage.tsx`) need no updates — confirmed by tracing that `panelRef`/`previouslyFocused` are purely internal and not part of the props contract. `AttractionsList`'s `visible`/`hasActiveFilters` in Task 2 keep the same names and are consumed identically by the unchanged JSX below them (`visible.map(...)`, `visible.length === 0`).
