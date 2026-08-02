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
