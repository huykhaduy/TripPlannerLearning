# Search Page Category Color Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give each attraction category a distinct, stable color (hashed, not a fixed lookup table) instead of flat blue everywhere, applied to `AttractionCard`'s category label and the Filters sidebar's selected-category highlight.

**Architecture:** A new tiny helper, `categoryColor(category: string)`, hashes the category string to deterministically pick one of 4 fixed `{ bg, text }` Tailwind class pairs — the same technique `TripsPage.tsx`'s `headerGradient()` already uses for trip-card gradients, generalized to a different input. `AttractionCard.tsx` and `AttractionsList.tsx` both import and use it; no other files change.

**Tech Stack:** React 19, Tailwind v4 (`@tailwindcss/vite`), TypeScript 5 (strict). No new npm dependencies. No frontend test runner exists in this project (`npm run lint` is `tsc --noEmit` + ESLint) — verification is manual via `npm run dev`.

## Global Constraints

- No new npm dependencies, no new colors beyond what's already in `styles.css`'s `@theme` block or Tailwind's default palette.
- No backend changes — `Category`'s derivation (`GeoapifyClient.cs`) is untouched.
- The destination details page's own category pill (over the hero photo, in `DestinationDetailsPage.tsx`) is untouched — stays solid `bg-brand-600`.
- "All destinations" (the sidebar's non-category "clear" option) keeps its current generic `bg-brand-50 font-semibold text-brand-600` styling — only per-category buttons get hashed colors.
- The color palette is exactly: `{ bg: 'bg-brand-50', text: 'text-brand-600' }`, `{ bg: 'bg-amber-50', text: 'text-amber-700' }`, `{ bg: 'bg-emerald-50', text: 'text-tertiary-500' }`, `{ bg: 'bg-slate-100', text: 'text-slate-700' }`, in that order.

---

## File Structure

**Create:**
- `frontend/src/features/destinations/categoryColor.ts` — the hash-to-color helper.

**Modify:**
- `frontend/src/features/destinations/AttractionCard.tsx` — category label becomes a colored pill.
- `frontend/src/features/destinations/AttractionsList.tsx` — selected-category sidebar button uses the matching color.

---

### Task 1: Category color helper + apply to card and filters

**Files:**
- Create: `frontend/src/features/destinations/categoryColor.ts`
- Modify: `frontend/src/features/destinations/AttractionCard.tsx:1-28`
- Modify: `frontend/src/features/destinations/AttractionsList.tsx:76-89`

**Interfaces:**
- Produces: `categoryColor(category: string): { bg: string; text: string }` — a pure function, no React dependency. Both modified files import it as `import { categoryColor } from './categoryColor'`.
- Consumes (in the modified files): `attraction.category: string | null` (already on `AttractionSummary`, unchanged) and `categories: string[]`/`categoryFilter: string` (already defined earlier in `AttractionsList.tsx`, unchanged).

- [ ] **Step 1: Create the `categoryColor` helper**

Create `frontend/src/features/destinations/categoryColor.ts`:

```ts
// Deterministic (hashed from the category string), not random — so a
// category's color is stable across reloads/re-searches, without needing
// to maintain a list of every possible Geoapify category value (there
// isn't a fixed one — GeoapifyClient.cs's MostSpecificCategory derives it
// from whatever tag hierarchy a place happens to have).
const CATEGORY_COLORS = [
  { bg: 'bg-brand-50', text: 'text-brand-600' },
  { bg: 'bg-amber-50', text: 'text-amber-700' },
  { bg: 'bg-emerald-50', text: 'text-tertiary-500' },
  { bg: 'bg-slate-100', text: 'text-slate-700' },
] as const;

export function categoryColor(category: string): { bg: string; text: string } {
  const hash = [...category].reduce((sum, ch) => sum + ch.charCodeAt(0), 0);
  return CATEGORY_COLORS[hash % CATEGORY_COLORS.length];
}
```

- [ ] **Step 2: Apply it to `AttractionCard.tsx`**

Find:

```tsx
import { useState } from 'react';
import { Link } from 'react-router-dom';
import { AddToTripButton } from './AddToTripButton';
import type { AttractionSummary } from '../../types';
```

Replace with:

```tsx
import { useState } from 'react';
import { Link } from 'react-router-dom';
import { AddToTripButton } from './AddToTripButton';
import { categoryColor } from './categoryColor';
import type { AttractionSummary } from '../../types';
```

Find:

```tsx
          {attraction.category && <span className="text-sm capitalize text-slate-500">{attraction.category}</span>}
```

Replace with:

```tsx
          {attraction.category && (
            <span
              className={`inline-flex w-fit rounded-full px-2 py-0.5 text-xs font-semibold capitalize ${categoryColor(attraction.category).bg} ${categoryColor(attraction.category).text}`}
            >
              {attraction.category}
            </span>
          )}
```

- [ ] **Step 3: Apply it to `AttractionsList.tsx`'s Filters sidebar**

Find:

```tsx
import { useEffect, useMemo, useState } from 'react';
import { getAttractions } from '../../api/destinations';
import { AttractionCard } from './AttractionCard';
import { EmptyState } from '../../components/EmptyState';
import { Button } from '../../components/Button';
import type { AttractionSummary, LocationSuggestion } from '../../types';
```

Replace with:

```tsx
import { useEffect, useMemo, useState } from 'react';
import { getAttractions } from '../../api/destinations';
import { AttractionCard } from './AttractionCard';
import { categoryColor } from './categoryColor';
import { EmptyState } from '../../components/EmptyState';
import { Button } from '../../components/Button';
import type { AttractionSummary, LocationSuggestion } from '../../types';
```

Find:

```tsx
            {categories.map((category) => (
              <button
                key={category}
                type="button"
                onClick={() => setCategoryFilter(category)}
                className={`rounded-md px-2 py-1.5 text-left text-sm capitalize ${
                  categoryFilter === category
                    ? 'bg-brand-50 font-semibold text-brand-600'
                    : 'text-slate-700 hover:bg-slate-50'
                }`}
              >
                {category}
              </button>
            ))}
```

Replace with:

```tsx
            {categories.map((category) => {
              const colors = categoryColor(category);
              const selected = categoryFilter === category;
              return (
                <button
                  key={category}
                  type="button"
                  onClick={() => setCategoryFilter(category)}
                  className={`rounded-md px-2 py-1.5 text-left text-sm capitalize ${
                    selected ? `${colors.bg} font-semibold ${colors.text}` : 'text-slate-700 hover:bg-slate-50'
                  }`}
                >
                  {category}
                </button>
              );
            })}
```

(The "All destinations" button just above this block, and everything below
it, is untouched — only this one `.map()` changes.)

- [ ] **Step 4: Type-check**

Run: `cd frontend && npm run lint`
Expected: exits 0, no errors (2 pre-existing `react-refresh/only-export-components` warnings on unrelated files are fine).

- [ ] **Step 5: Verify category colors on the search page**

Run `npm run dev` with the backend running. Search a city with multiple categories present (e.g. Paris — has Memorial, Park, Ruines results).

Expected:
- Each attraction card's category now renders as a small colored pill (background + text color), not plain gray text.
- Cards sharing the same category (e.g. two "Memorial" results) show the *same* pill color as each other.
- Different categories show visibly different colors, drawn from: blue (`bg-brand-50`/`text-brand-600`), amber (`bg-amber-50`/`text-amber-700`), teal (`bg-emerald-50`/`text-tertiary-500`), or slate (`bg-slate-100`/`text-slate-700`).
- Reload the page (same city still selected via the URL) — the same categories still show the same colors as before the reload (deterministic, not random).

- [ ] **Step 6: Verify the Filters sidebar**

On the same page, click a category button in the Filters sidebar (e.g. "Park").

Expected:
- The clicked button highlights using that category's own color pair (matching the pill color on that category's cards), not the generic blue.
- Click "All destinations" — it highlights with the generic `bg-brand-50`/`text-brand-600` blue, unchanged from before this task.
- Click a different category — the previously-selected button returns to its unselected gray state, and the newly-selected one highlights in its own color.

- [ ] **Step 7: Verify the details page picks up the same colors**

Open a destination that has nearby attractions (e.g. from the Paris search results, open any result and scroll to "Nearby experiences").

Expected: the same category pills (same colors, same categories) render there too, since `NearbyAttractions.tsx` uses the same shared `AttractionCard`.

- [ ] **Step 8: Commit**

```bash
git add frontend/src/features/destinations/categoryColor.ts frontend/src/features/destinations/AttractionCard.tsx frontend/src/features/destinations/AttractionsList.tsx
git commit -m "feat: color-code attraction categories on cards and filters"
```

---

## Self-Review Notes

- **Spec coverage:** every confirmed decision in `docs/superpowers/specs/2026-08-03-search-page-category-color-design.md` maps to this single task — the hash-based helper (not a fixed lookup table), the exact 4-color palette in the exact order, the card pill, the sidebar's selected-state recoloring, "All destinations" staying generic, and the cross-page consistency via the shared `AttractionCard` (verified in Step 7).
- **Placeholder scan:** no TBD/TODO; every step shows complete before/after code.
- **Type consistency:** `categoryColor`'s signature (`(category: string) => { bg: string; text: string }`) is used identically at both call sites — `AttractionCard.tsx` calls it inline twice (once for `.bg`, once for `.text`) on the same `attraction.category` (a non-null string at that point, since it's inside an `attraction.category && (...)` guard); `AttractionsList.tsx` calls it once per `category` in the `.map()` and destructures both fields from the single result, avoiding the double-call `AttractionCard.tsx` has (acceptable — `categoryColor` is a pure, allocation-free function, not worth optimizing further for this task's scope).
