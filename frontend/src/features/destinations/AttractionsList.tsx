import { useEffect, useState } from 'react';
import { getAttractions } from '../../api/destinations';
import type { AttractionSummary, LocationSuggestion } from '../../types';

function AttractionCard({ attraction }: { attraction: AttractionSummary }) {
  const [imageFailed, setImageFailed] = useState(false);
  const showImage = attraction.imageUrl && !imageFailed;

  return (
    <div className="attraction-card">
      {showImage ? (
        <img
          src={attraction.imageUrl!}
          alt={attraction.name}
          onError={() => setImageFailed(true)}
        />
      ) : (
        <div className="attraction-placeholder">🏛️</div>
      )}
      <div className="attraction-body">
        <strong>{attraction.name}</strong>
        {attraction.category && <span className="muted">{attraction.category}</span>}
        {attraction.rating != null && <span>⭐ {attraction.rating.toFixed(1)}</span>}
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

  if (loading) return <p className="muted">Loading attractions…</p>;
  if (error) return <p className="error">{error}</p>;
  if (attractions.length === 0) {
    return <p className="muted">No attractions found near {city.name}.</p>;
  }

  return (
    <section>
      <h2>Attractions near {city.name}</h2>
      <div className="attractions">
        {attractions.map((attraction) => (
          <AttractionCard key={attraction.providerId} attraction={attraction} />
        ))}
      </div>
    </section>
  );
}
