# Search Results & Modal Polish — Design

**Date:** 2026-08-02
**Scope:** `SearchPage.tsx` (hero), `AttractionsList.tsx` (filters/sort),
`components/Modal.tsx` (accessibility). No backend changes, no change to
`AttractionCard.tsx`, `DestinationDetailsPage.tsx`, or any other page.

## Context

This is a follow-up to the itinerary-page redesign (`2026-08-02-itinerary-page-redesign-design.md`), covering issues found in that same live-review pass plus one carried over from the itinerary work's final code review:

1. **Search hero never shrinks.** `SearchPage.tsx`'s hero (`"Where to next?"` title, subtitle, search box) stays full-size (~400px tall) even after a city is selected and results are showing, pushing the actual attraction grid below the fold — the same "buried content" pattern the itinerary page had with its edit form.
2. **The "Minimum rating" filter is dead.** Investigating a suspected "details page missing rating" gap led to a bigger finding: `GeoapifyClient.cs:27,115` explicitly documents "Geoapify has no ratings" and hardcodes `Rating: null` for every attraction. `AttractionsList.tsx`'s ⭐ 3+/4+ star filter buttons filter on `a.rating != null && a.rating >= N`, which can never be true — those two options always return zero results. The `Sort by → Highest rating` option has the same root cause: sorting by an always-null field is a silent no-op.
3. **`Modal` (from the itinerary redesign) lacks accessibility basics.** No `aria-modal`, no Escape-to-close, no focus management — flagged as a Minor finding in that work's final review, carried forward here since it's now a single shared component instead of three separate ones.

There is no real "destination details page missing a rating" bug to fix — the rating badge never renders anywhere (search cards included) with the real data provider, so there's nothing inconsistent between the card and the details page.

## Direction (confirmed with the user)

- **Hero**: collapses to a compact bar (search input only, no title/subtitle) once a city is selected, and **stays** compact across subsequent city changes — not a scroll-triggered collapse.
- **Rating filter/sort**: remove the "Minimum rating" section (all three options — "Any rating" included, since it's meaningless alone once 3+/4+ are gone) and the "Sort by" dropdown (both options — "Recommended" alone is meaningless as a dropdown) entirely, along with their backing state/logic. `AttractionSummary.rating` and `AttractionCard`'s ⭐ badge are left as-is — harmless, and would "just work" if a future provider supplied ratings.
- **Modal accessibility**: `aria-modal="true"`, Escape-to-close, and focus management (move focus into the panel on open, restore to the trigger on close) — added once to `Modal.tsx`, benefiting all 3 existing dialogs (`TripsPage`'s "Plan new trip", `AddToTripButton`'s add-to-trip dialog, `TripDetailPage`'s "Edit trip details"). A full manual Tab-cycle focus trap is explicitly **out of scope** — Tab can still move focus to page content behind the backdrop while a modal is open.

## Approach

### 1. Search hero

`SearchPage.tsx`'s hero `<div>` becomes a ternary on `selectedCity`:

```tsx
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
```

No new state — `selectedCity` already exists (derived from URL search params). Picking a *different* city while already in the compact view keeps it compact (the ternary only reverts to the full hero when `selectedCity` is `null`, which the current UI has no path back to — but the ternary handles it correctly regardless, e.g. if a future "clear city" action is added).

### 2. Remove the dead rating filter/sort

In `AttractionsList.tsx`, remove:
- State: `const [minRating, setMinRating] = useState('');` and `const [sortBy, setSortBy] = useState<'recommended' | 'rating'>('recommended');`
- The city-change effect's `setMinRating('')` and `setSortBy('recommended')` reset calls (keep `setCategoryFilter('')`).
- The `visible` computation's rating-based filter clause and the `sortBy === 'rating'` sort branch — becomes a plain category-only filter with no re-sort:
  ```tsx
  const visible = useMemo(
    () => attractions.filter((a) => categoryFilter === '' || a.category === categoryFilter),
    [attractions, categoryFilter],
  );
  ```
- `hasActiveFilters` becomes `categoryFilter !== ''`.
- The entire "Minimum rating" `<div>` (heading + `['', '3', '4']` button map) and the "Sort by" `<label>`+`<select>` block.

`AttractionSummary.rating` (the type), `DestinationSummaryDto.Rating` (the backend DTO), and `AttractionCard.tsx`'s `{attraction.rating != null && <span>⭐ ...</span>}` are all untouched — no backend changes in this spec.

### 3. Modal accessibility

`Modal.tsx` gains two refs and one effect:

```tsx
import { useEffect, useRef, type ReactNode } from 'react';

export function Modal({ title, onClose, children, maxWidth = 'max-w-lg' }: { /* ...unchanged... */ }) {
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
        {/* ...unchanged header + body... */}
      </div>
    </div>
  );
}
```

`tabIndex={-1}` makes the panel programmatically focusable (via `.focus()`) without adding it to the natural Tab order. The effect's cleanup runs on every unmount (i.e. every close, since `Modal` is only rendered while `open`/`editOpen` is true at each call site) — so focus reliably returns to the triggering button each time.

## Out of scope

- Any backend change (no `Rating` field added anywhere).
- `AttractionCard.tsx`'s rating badge, `DestinationDetailsPage.tsx`, or any other page.
- A full manual Tab-cycle focus trap in `Modal`.
- Any change to `DestinationList`, drag-and-drop, or trip data logic.

## Verification

No frontend test runner — manual:

1. `npm run lint` (`tsc --noEmit` + ESLint) passes.
2. Search a city: hero collapses to the compact bar; search a *different* city from the compact bar — hero stays compact (doesn't revert to full-size).
3. Filters sidebar shows only Category options + "Clear filters" (when a category is active) — no "Minimum rating" section, no "Sort by" dropdown. The attraction grid still filters correctly by category.
4. Open each of the 3 modals (Plan new trip, Add to trip, Edit trip details): confirm focus lands on the panel on open, pressing Escape closes it, and focus returns to the button that opened it afterward.
