import { useEffect, useState } from 'react';
import { getAttractions } from '../../api/destinations';
import { AddToTripButton } from './AddToTripButton';
import type { AttractionSummary, LocationSuggestion } from '../../types';

function AttractionCard({ attraction }: { attraction: AttractionSummary }) {
  const [imageFailed, setImageFailed] = useState(false);
  const showImage = attraction.imageUrl && !imageFailed;

  return (
    <div className="overflow-hidden rounded-lg border border-slate-200 bg-white">
      {showImage ? (
        <img
          src={attraction.imageUrl!}
          alt={attraction.name}
          onError={() => setImageFailed(true)}
          className="h-28 w-full object-cover"
        />
      ) : (
        <div className="flex h-28 w-full items-center justify-center bg-slate-100 text-3xl">
          🏛️
        </div>
      )}
      <div className="flex flex-col gap-1 p-3 text-sm">
        <strong>{attraction.name}</strong>
        {attraction.category && <span className="text-slate-500">{attraction.category}</span>}
        {attraction.rating != null && <span>⭐ {attraction.rating.toFixed(1)}</span>}
        <AddToTripButton attraction={attraction} />
      </div>
    </div>
  );
}

/** F1/US3 — recommended attractions near the selected city. */
export function AttractionsList({ city }: { city: LocationSuggestion }) {
  const [attractions, setAttractions] = useState<AttractionSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let ignore = false;
    setLoading(true);
    setError(null);
    getAttractions(city.latitude, city.longitude)
      .then((results) => {
        if (!ignore) setAttractions(results);
      })
      .catch(() => {
        if (!ignore) setError('Could not load attractions. Please try again.');
      })
      .finally(() => {
        if (!ignore) setLoading(false);
      });

    return () => {
      ignore = true;
    };
  }, [city]);

  if (loading) return <p className="text-sm text-slate-500">Loading attractions…</p>;
  if (error) return <p className="text-sm text-red-600">{error}</p>;
  if (attractions.length === 0) {
    return <p className="text-sm text-slate-500">No attractions found near {city.name}.</p>;
  }

  return (
    <section>
      <h2 className="mb-3 text-lg font-semibold">Attractions near {city.name}</h2>
      <div className="grid gap-4 sm:grid-cols-2 md:grid-cols-3">
        {attractions.map((attraction) => (
          <AttractionCard key={attraction.providerId} attraction={attraction} />
        ))}
      </div>
    </section>
  );
}
