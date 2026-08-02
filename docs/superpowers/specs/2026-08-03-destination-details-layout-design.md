# Destination Details Layout — Design

**Date:** 2026-08-03
**Scope:** The lower half of `DestinationDetailsPage.tsx` (below the hero
carousel) and `NearbyAttractions.tsx`. No change to the hero/carousel, the
data-fetching in either component, or `hasNearby`'s gating logic.

## Context

`DestinationDetailsPage.tsx` currently puts "Practical info" (address,
opening hours, website, embedded map) and "Nearby experiences" (up to 4
attraction cards) side by side in a `grid grid-cols-1 gap-6 lg:grid-cols-2`.
Measured live (computed bounding boxes): the two columns' left/right edges
line up exactly, but "Practical info" is only ~427px tall while "Nearby
experiences" is ~698px tall — same grid, very different content height,
leaving a large empty gap under the shorter left column while the right
column keeps going. `NearbyAttractions`'s own `AttractionCard`s are also
squeezed into a `grid-cols-1 sm:grid-cols-2` layout *within* that half-width
column, so they render noticeably narrower than the same cards on the
search page's full-width `sm:grid-cols-2 xl:grid-cols-3` grid.

## Direction (confirmed with the user)

Stack both sections full-width instead of side-by-side:
- **Practical info**: one full-width card, with an internal `lg:grid-cols-2`
  split (address/hours/website on the left, map on the right) instead of a
  half-width card with everything stacked vertically inside it.
- **Nearby experiences**: its own full-width section below, dropping the
  bordered `Card` wrapper for a plain heading + grid matching the search
  page's card grid exactly (`grid gap-5 sm:grid-cols-2 xl:grid-cols-3`).
  Shown count increases from 4 to 6 (two full rows at 3 columns) since
  there's now room.

This removes the uneven-column problem structurally (nothing sits
side-by-side with mismatched height anymore) rather than patching heights,
and gives `NearbyAttractions`'s cards the same visual weight as the search
page's cards.

## Approach

### Practical info

```tsx
<Card padding="tight">
  <h2 className="font-headline text-base font-semibold text-brand-600">Practical info</h2>
  <div className="mt-3 grid grid-cols-1 gap-6 lg:grid-cols-2">
    <dl className={`flex flex-col gap-3 text-sm ${hasNearby ? '' : 'lg:col-span-2'}`}>
      {/* Address / Opening hours / Website — same content and markup as today */}
    </dl>
    {hasNearby && (
      <div>
        {/* embedded map iframe + "View larger map" link — same as today */}
      </div>
    )}
  </div>
</Card>
```

The old `lg:only:col-span-2` guard (span the full row when there's no
`NearbyAttractions` sibling to share it with) moves *inside* this card, onto
the `<dl>`: when `hasNearby` is false, there's no map to put beside it, so
the `<dl>` spans both of the card's internal columns instead of leaving an
empty second column.

### Nearby experiences

`NearbyAttractions.tsx` changes from a `Card`-wrapped mini-grid to a plain
section matching `AttractionsList.tsx`'s own grid:

```tsx
if (!attractions || attractions.length === 0) return null;

return (
  <section>
    <h2 className="font-headline mb-4 text-lg font-semibold text-slate-900">Nearby experiences</h2>
    <div className="grid gap-5 sm:grid-cols-2 xl:grid-cols-3">
      {attractions.slice(0, 6).map((attraction) => (
        <AttractionCard key={attraction.providerId} attraction={attraction} />
      ))}
    </div>
  </section>
);
```

`Card` becomes an unused import in this file once this lands and should be
removed. Heading size bumps from `text-base` (matching the smaller
"Practical info" card heading) to `text-lg` (matching "Attractions near
{city}" on the search page), since this is now a full-width page section,
not a card title.

### DestinationDetailsPage.tsx

The outer `grid grid-cols-1 gap-6 lg:grid-cols-2` wrapping both components is
removed — they become two sequential full-width children instead, separated
by the page's existing `mt-6` spacing rhythm:

```tsx
<div className="mt-6">
  <Card padding="tight">{/* Practical info, as above */}</Card>
</div>

{hasNearby && (
  <div className="mt-6">
    <NearbyAttractions
      latitude={details.latitude!}
      longitude={details.longitude!}
      excludeProviderId={details.providerId}
    />
  </div>
)}
```

## Out of scope

- The hero/photo carousel above this section.
- `NearbyAttractions`'s data-fetching (`getAttractions`, the 5km radius,
  excluding the current destination) — unchanged.
- `hasNearby`'s definition (`details.latitude != null && details.longitude != null`) — unchanged.
- The search page, `Modal`, or any other page.

## Verification

No frontend test runner — manual:

1. `npm run lint` (`tsc --noEmit` + ESLint) passes, with no unused `Card` import left in `NearbyAttractions.tsx`.
2. A destination with both a map and ≥1 nearby attraction: "Practical info" renders full-width with address/hours/website on the left and the map on the right (on `lg+`; stacked on smaller screens); "Nearby experiences" renders below it, full-width, at the search page's card size.
3. A destination with no coordinates (`hasNearby` false): "Practical info"'s `<dl>` spans the full card width (no empty second column); "Nearby experiences" doesn't render at all.
4. A destination with coordinates but zero nearby attractions found: "Practical info" renders as in (2); "Nearby experiences" doesn't render (unchanged early-return behavior).
