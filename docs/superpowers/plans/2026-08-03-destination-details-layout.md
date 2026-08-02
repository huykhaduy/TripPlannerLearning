# Destination Details Layout Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stack "Practical info" and "Nearby experiences" full-width instead of side-by-side on the destination details page, fixing the uneven-column-height problem (measured live: ~427px vs ~698px) and giving "Nearby experiences"'s attraction cards the same size as the search page's cards instead of squeezing them into a half-width grid.

**Architecture:** `DestinationDetailsPage.tsx`'s outer 2-column grid is removed — "Practical info" and "Nearby experiences" become two sequential full-width children. "Practical info" gains its own internal `lg:grid-cols-2` split (address/hours/website left, map right) to keep using the horizontal space it's given. `NearbyAttractions.tsx` drops its `Card` wrapper and half-width mini-grid in favor of a plain section matching `AttractionsList.tsx`'s own card grid exactly.

**Tech Stack:** React 19, Tailwind v4 (`@tailwindcss/vite`), TypeScript 5 (strict). No new npm dependencies. No frontend test runner exists in this project (`npm run lint` is `tsc --noEmit` + ESLint) — verification is manual via `npm run dev`.

## Global Constraints

- No new npm dependencies.
- No change to `NearbyAttractions`'s data-fetching (`getAttractions`, 5km radius, excluding the current destination) or `hasNearby`'s definition (`details.latitude != null && details.longitude != null`).
- No change to the hero/photo carousel, or to any other page.
- "Nearby experiences"'s grid must match `AttractionsList.tsx`'s exactly: `grid gap-5 sm:grid-cols-2 xl:grid-cols-3`.
- When `hasNearby` is false, "Practical info"'s `<dl>` must span its card's full internal width (no empty second column) — this replaces the old `lg:only:col-span-2` guard, which no longer applies once the outer grid is removed.

---

## File Structure

**Modify:**
- `frontend/src/features/destinations/DestinationDetailsPage.tsx` — remove the outer 2-column grid; "Practical info" gets an internal 2-column split.
- `frontend/src/features/destinations/NearbyAttractions.tsx` — drop the `Card` wrapper and half-width grid for a plain full-width section.

No files created or deleted.

---

### Task 1: Stack Practical info and Nearby experiences full-width

**Files:**
- Modify: `frontend/src/features/destinations/DestinationDetailsPage.tsx:198-255`
- Modify: `frontend/src/features/destinations/NearbyAttractions.tsx` (full file)

**Interfaces:**
- Consumes: `details: DestinationDetails`, `hasNearby: boolean`, `buildOsmEmbedUrl`, `buildOsmViewUrl` — all already defined earlier in `DestinationDetailsPage.tsx`, unchanged. `NearbyAttractions`'s own props (`latitude`, `longitude`, `excludeProviderId`) are unchanged.
- Produces: no new exports, no prop/type changes to either component — only their rendered markup changes.

- [ ] **Step 1: Restructure `NearbyAttractions.tsx`**

Find the full current file:

```tsx
import { useEffect, useState } from 'react';
import { getAttractions } from '../../api/destinations';
import { AttractionCard } from './AttractionCard';
import { Card } from '../../components/Card';
import type { AttractionSummary } from '../../types';

const NEARBY_RADIUS_KM = 5;

/**
 * F2 — a compact "nearby experiences" list beside Practical info in the
 * details page's two-column layout. Reuses the existing attractions endpoint
 * centered on this destination's own coordinates; hides itself entirely if
 * there's nothing to show.
 */
export function NearbyAttractions({
  latitude,
  longitude,
  excludeProviderId,
}: {
  latitude: number;
  longitude: number;
  excludeProviderId: string;
}) {
  const [attractions, setAttractions] = useState<AttractionSummary[] | null>(null);

  useEffect(() => {
    let ignore = false;
    setAttractions(null);
    getAttractions(latitude, longitude, NEARBY_RADIUS_KM)
      .then((results) => {
        if (!ignore) setAttractions(results.filter((a) => a.providerId !== excludeProviderId));
      })
      .catch(() => {
        if (!ignore) setAttractions([]);
      });
    return () => {
      ignore = true;
    };
  }, [latitude, longitude, excludeProviderId]);

  if (!attractions || attractions.length === 0) return null;

  return (
    <Card padding="tight" className="h-fit">
      <h2 className="font-headline text-base font-semibold text-brand-600">Nearby experiences</h2>
      <div className="mt-3 grid grid-cols-1 gap-4 sm:grid-cols-2">
        {attractions.slice(0, 4).map((attraction) => (
          <AttractionCard key={attraction.providerId} attraction={attraction} />
        ))}
      </div>
    </Card>
  );
}
```

Replace it entirely with:

```tsx
import { useEffect, useState } from 'react';
import { getAttractions } from '../../api/destinations';
import { AttractionCard } from './AttractionCard';
import type { AttractionSummary } from '../../types';

const NEARBY_RADIUS_KM = 5;

/**
 * F2 — a full-width "nearby experiences" section below Practical info on the
 * details page. Reuses the existing attractions endpoint centered on this
 * destination's own coordinates; hides itself entirely if there's nothing to
 * show.
 */
export function NearbyAttractions({
  latitude,
  longitude,
  excludeProviderId,
}: {
  latitude: number;
  longitude: number;
  excludeProviderId: string;
}) {
  const [attractions, setAttractions] = useState<AttractionSummary[] | null>(null);

  useEffect(() => {
    let ignore = false;
    setAttractions(null);
    getAttractions(latitude, longitude, NEARBY_RADIUS_KM)
      .then((results) => {
        if (!ignore) setAttractions(results.filter((a) => a.providerId !== excludeProviderId));
      })
      .catch(() => {
        if (!ignore) setAttractions([]);
      });
    return () => {
      ignore = true;
    };
  }, [latitude, longitude, excludeProviderId]);

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
}
```

(`Card` import dropped — no longer used. `slice(0, 4)` → `slice(0, 6)`. Heading `text-base font-semibold text-brand-600` → `mb-4 text-lg font-semibold text-slate-900`, matching `AttractionsList.tsx`'s "Attractions near {city}" heading exactly.)

- [ ] **Step 2: Restructure the lower section of `DestinationDetailsPage.tsx`**

Find:

```tsx
      <div className="mt-6 grid grid-cols-1 gap-6 lg:grid-cols-2">
        {/* lg:only:col-span-2 — if NearbyAttractions doesn't render (no other
            POIs nearby, or no coordinates at all), this card is the grid's
            only child and should fill the row instead of sitting alone at
            half-width with dead space beside it. */}
        <Card padding="tight" className="h-fit lg:only:col-span-2">
          <h2 className="font-headline text-base font-semibold text-brand-600">Practical info</h2>
          <dl className="mt-3 flex flex-col gap-3 text-sm">
            <div>
              <dt className="font-medium text-slate-500">Address</dt>
              <dd className="mt-0.5 text-slate-900">{details.address ?? 'Not available'}</dd>
            </div>
            <div>
              <dt className="font-medium text-slate-500">Opening hours</dt>
              <dd className="mt-0.5 text-slate-900">{details.openingHours ?? 'Opening hours not available'}</dd>
            </div>
            {details.website && (
              <div>
                <dt className="font-medium text-slate-500">Website</dt>
                <dd className="mt-0.5">
                  <a href={details.website} target="_blank" rel="noreferrer" className="text-brand-600 hover:underline">
                    {details.website}
                  </a>
                </dd>
              </div>
            )}
          </dl>

          {hasNearby && (
            <div className="mt-4">
              <div className="overflow-hidden rounded-lg border border-[#E2E8F0]">
                <iframe
                  title={`Map showing ${details.name}`}
                  src={buildOsmEmbedUrl(details.latitude!, details.longitude!)}
                  loading="lazy"
                  className="h-48 w-full border-0"
                />
              </div>
              <a
                href={buildOsmViewUrl(details.latitude!, details.longitude!)}
                target="_blank"
                rel="noreferrer"
                className="mt-1.5 inline-block text-xs text-brand-600 hover:underline"
              >
                View larger map
              </a>
            </div>
          )}
        </Card>

        {hasNearby && (
          <NearbyAttractions
            latitude={details.latitude!}
            longitude={details.longitude!}
            excludeProviderId={details.providerId}
          />
        )}
      </div>
    </div>
  );
}
```

Replace with:

```tsx
      <div className="mt-6">
        <Card padding="tight">
          <h2 className="font-headline text-base font-semibold text-brand-600">Practical info</h2>
          <div className="mt-3 grid grid-cols-1 gap-6 lg:grid-cols-2">
            <dl className={`flex flex-col gap-3 text-sm ${hasNearby ? '' : 'lg:col-span-2'}`}>
              <div>
                <dt className="font-medium text-slate-500">Address</dt>
                <dd className="mt-0.5 text-slate-900">{details.address ?? 'Not available'}</dd>
              </div>
              <div>
                <dt className="font-medium text-slate-500">Opening hours</dt>
                <dd className="mt-0.5 text-slate-900">{details.openingHours ?? 'Opening hours not available'}</dd>
              </div>
              {details.website && (
                <div>
                  <dt className="font-medium text-slate-500">Website</dt>
                  <dd className="mt-0.5">
                    <a href={details.website} target="_blank" rel="noreferrer" className="text-brand-600 hover:underline">
                      {details.website}
                    </a>
                  </dd>
                </div>
              )}
            </dl>

            {hasNearby && (
              <div>
                <div className="overflow-hidden rounded-lg border border-[#E2E8F0]">
                  <iframe
                    title={`Map showing ${details.name}`}
                    src={buildOsmEmbedUrl(details.latitude!, details.longitude!)}
                    loading="lazy"
                    className="h-48 w-full border-0"
                  />
                </div>
                <a
                  href={buildOsmViewUrl(details.latitude!, details.longitude!)}
                  target="_blank"
                  rel="noreferrer"
                  className="mt-1.5 inline-block text-xs text-brand-600 hover:underline"
                >
                  View larger map
                </a>
              </div>
            )}
          </div>
        </Card>
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
    </div>
  );
}
```

Note: the map's wrapping `<div className="mt-4">` becomes a plain `<div>` — the `mt-4` isn't needed anymore since it's now a grid sibling of the `<dl>` (grid handles the gap via `gap-6` on the parent), not something stacked below it in normal flow.

- [ ] **Step 3: Type-check**

Run: `cd frontend && npm run lint`
Expected: exits 0, no errors (2 pre-existing `react-refresh/only-export-components` warnings on unrelated files are fine; confirm no new "unused import" error for the removed `Card` import in `NearbyAttractions.tsx`).

- [ ] **Step 4: Verify the destination-details page**

Run `npm run dev` with the backend running. Open a destination that has both an address/map and at least one nearby attraction (e.g. search Paris on Explore, open any of the memorial/park results).

Expected:
- "Practical info" renders full-width, below the hero. On a desktop-width viewport (≥1024px, Tailwind's `lg`): address/hours/website are on the left, the embedded map is on the right, side by side within the card. On a narrower viewport: they stack (address/hours/website above, map below).
- "Nearby experiences" renders in its own full-width section below "Practical info" (not beside it) — its attraction cards are the same size as the ones on the Explore/search page (3 per row at wide viewports, not squeezed 2-per-row into a half-width box).
- Up to 6 nearby attractions are shown (previously capped at 4) — check by counting cards if the location has ≥5 nearby results.

- [ ] **Step 5: Verify the no-map case**

Open a destination with a `null` latitude/longitude (or temporarily fake one by editing `hasNearby`'s condition in devtools if none is naturally available — otherwise reason from the code: `hasNearby` is `details.latitude != null && details.longitude != null`).

Expected: "Practical info"'s address/hours/website block spans the full width of the card (no map, no empty second column beside it); "Nearby experiences" doesn't render at all.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/features/destinations/DestinationDetailsPage.tsx frontend/src/features/destinations/NearbyAttractions.tsx
git commit -m "feat: stack Practical info and Nearby experiences full-width on destination details"
```

---

## Self-Review Notes

- **Spec coverage:** every confirmed decision in `docs/superpowers/specs/2026-08-03-destination-details-layout-design.md` maps to this single task — full-width stacking, Practical info's internal 2-column split, the `lg:col-span-2` guard moved onto the `<dl>`, `NearbyAttractions`'s `Card`-to-plain-section change, its grid matching `AttractionsList.tsx`'s exactly, and the 4→6 count bump.
- **Placeholder scan:** no TBD/TODO; both before/after code blocks in each step are complete and copy-pasteable.
- **Type consistency:** `NearbyAttractions`'s props (`latitude: number`, `longitude: number`, `excludeProviderId: string`) are unchanged from the current file and match the call site in `DestinationDetailsPage.tsx` exactly (`details.latitude!`, `details.longitude!`, `details.providerId`). `hasNearby` (a `boolean` computed earlier in `DestinationDetailsPage.tsx`, unchanged) is referenced identically in both its new usage (the `<dl>`'s conditional class) and its existing usage (gating the map block and the `NearbyAttractions` render).
