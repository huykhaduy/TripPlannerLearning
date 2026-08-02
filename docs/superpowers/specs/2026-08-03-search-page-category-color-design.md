# Search Page Category Color — Design

**Date:** 2026-08-03
**Scope:** A new shared helper (`categoryColor.ts`), `AttractionCard.tsx`'s
category label, and `AttractionsList.tsx`'s Filters sidebar. No backend
changes, no change to any other page's own markup (though `AttractionCard`
is shared, so its change is also visible wherever it's already used — see
Context).

## Context

The search page (and, via the shared `AttractionCard`, the destination
details page's "Nearby experiences" section) currently uses flat
`brand-600` blue for everything — the hero, "Add to trip" buttons, the
active-filter highlight — with category shown as plain gray text on each
card. The user asked for more visual distinctiveness; specifically, more
color variety.

`Category` is not a fixed enum — it's derived from Geoapify's deepest
matching tag on each place (`GeoapifyClient.cs:163-166`, `MostSpecificCategory`),
so it can be almost any string ("castle", "museum", "church", "playground",
…), not just the "Memorial"/"Park"/"Ruines" visible for Paris. A fixed
category→color lookup table would need to cover an open-ended, unknown
vocabulary and break (or need a fallback) on anything not listed.

## Direction (confirmed with the user)

Reuse the deterministic-hash-based color pattern `TripsPage.tsx` already
uses for its trip-card gradients (`headerGradient()`, hashing a trip's id
to pick one of 4 fixed gradients) — generalized to hash a *category string*
instead, picking from a small fixed palette. This ties the new color
variety to real data (a place's category) rather than being arbitrary
decoration, gives any category — known or not — a color with zero
maintenance, and is stable across reloads/re-searches (same category always
gets the same color).

Applied to:
- `AttractionCard.tsx`'s category label (plain text → colored pill).
- `AttractionsList.tsx`'s Filters sidebar (a category button's *selected*
  state uses its own hashed color instead of generic blue).

## Approach

### New helper: `frontend/src/features/destinations/categoryColor.ts`

```ts
// Deterministic (hashed from the category string), not random — so a
// category's color is stable across reloads/re-searches, without needing
// to maintain a list of every possible Geoapify category value (there
// isn't a fixed one — see GeoapifyClient.cs's MostSpecificCategory).
const CATEGORY_COLORS = [
  { bg: 'bg-brand-50', text: 'text-brand-600' },
  { bg: 'bg-amber-50', text: 'text-amber-700' },
  { bg: 'bg-emerald-50', text: 'text-tertiary-500' },
  { bg: 'bg-slate-100', text: 'text-slate-700' },
] as const;

export function categoryColor(category: string) {
  const hash = [...category].reduce((sum, ch) => sum + ch.charCodeAt(0), 0);
  return CATEGORY_COLORS[hash % CATEGORY_COLORS.length];
}
```

The 4-color palette mirrors `TripsPage.tsx`'s existing `HEADER_GRADIENTS`
set (brand blue, an amber/warm tone, the app's `tertiary` teal, neutral
slate) — no new colors introduced, just reused in a new place.

### `AttractionCard.tsx`

The category `<span>` (currently `text-sm capitalize text-slate-500`)
becomes a small pill:

```tsx
{attraction.category && (
  <span
    className={`inline-flex w-fit rounded-full px-2 py-0.5 text-xs font-semibold capitalize ${categoryColor(attraction.category).bg} ${categoryColor(attraction.category).text}`}
  >
    {attraction.category}
  </span>
)}
```

Since `AttractionCard` is a shared component, this change is also visible
in `NearbyAttractions.tsx` (the details page's "Nearby experiences" section,
restructured in the prior spec) — the same category always renders the same
color in both places, which is the intended, consistent behavior, not an
unplanned side effect.

### `AttractionsList.tsx`

Each category button's selected-state className switches from the generic
`bg-brand-50 font-semibold text-brand-600` to that category's own hashed
colors:

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

"All destinations" (the non-category "clear" option) keeps its current
generic `bg-brand-50 font-semibold text-brand-600` styling unchanged — there's
no single category to hash for it.

## Out of scope

- Any backend change — `Category`'s derivation is untouched.
- The destination details page's own category pill (over the hero photo) —
  stays solid `bg-brand-600`, unchanged. Only `AttractionCard`'s label
  changes, and only because it's a shared component.
- The hero/compact search bar, "Add to trip" buttons, or any other color
  usage on these pages.
- Introducing any new color not already defined in `styles.css` or
  Tailwind's default palette.

## Verification

No frontend test runner — manual:

1. `npm run lint` (`tsc --noEmit` + ESLint) passes.
2. Search a city with multiple categories present (e.g. Paris: Memorial,
   Park, Ruines): confirm each category's pill on the attraction cards has
   a distinct, consistent color, and the same category always gets the same
   color across different cards.
3. Click a category in the Filters sidebar: confirm it highlights using
   that category's own color (not generic blue), while "All destinations"
   still uses the generic blue highlight when selected.
4. Open a destination details page with nearby attractions: confirm
   "Nearby experiences" cards show the same colored pills as the search
   page for matching categories.
