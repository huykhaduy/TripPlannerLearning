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
