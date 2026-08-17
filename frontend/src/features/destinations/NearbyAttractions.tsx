import { useEffect, useState } from 'react';
import { getAttractions } from '../../api/destinations';
import { AttractionCard } from './AttractionCard';
import { AttractionCardSkeleton } from './AttractionCardSkeleton';
import type { AttractionSummary } from '../../types';

const NEARBY_RADIUS_KM = 5;

// Matches the cap applied to the real results below, so the skeleton reserves
// the space the loaded grid is most likely to need.
const NEARBY_LIMIT = 6;

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

  const loading = attractions === null;

  // Hidden entirely once we know there is nothing to show — an empty "Nearby
  // experiences" heading would be worse than no section. While loading that
  // isn't knowable yet, so the skeleton shows and then disappears in the
  // nothing-nearby case; the alternative is no loading feedback at all.
  if (!loading && attractions.length === 0) return null;

  return (
    <section
      className="mt-6"
      aria-busy={loading || undefined}
      aria-label={loading ? 'Loading nearby experiences' : undefined}
    >
      <h2 className="font-headline mb-4 text-lg font-semibold text-slate-900">Nearby experiences</h2>
      {/* lg:grid-cols-3 (not xl:, unlike the search page's identical-looking
          grid) — this page's container tops out at max-w-5xl (1024px), so
          xl's 1280px breakpoint would leave a wide band showing only 2
          oversized columns before the 3rd kicks in. */}
      <div className="grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
        {loading
          ? Array.from({ length: NEARBY_LIMIT }, (_, i) => <AttractionCardSkeleton key={i} />)
          : attractions.slice(0, NEARBY_LIMIT).map((attraction) => (
              <AttractionCard key={attraction.providerId} attraction={attraction} />
            ))}
      </div>
    </section>
  );
}
