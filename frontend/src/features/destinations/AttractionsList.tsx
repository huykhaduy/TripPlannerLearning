import { useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { getAttractions } from '../../api/destinations';
import { AddToTripButton } from './AddToTripButton';
import { EmptyState } from '../../components/EmptyState';
import { fieldControlClass } from '../../components/Field';
import { Button } from '../../components/Button';
import type { AttractionSummary, LocationSuggestion } from '../../types';

function AttractionCard({ attraction }: { attraction: AttractionSummary }) {
  const [imageFailed, setImageFailed] = useState(false);
  const showImage = attraction.imageUrl && !imageFailed;

  return (
    <div className="overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm">
      {/* F2/US1 — open the details view. Add-to-trip stays outside this link
          so a button never ends up nested inside an anchor. */}
      <Link to={`/destinations/${encodeURIComponent(attraction.providerId)}`}>
        {showImage ? (
          <img
            src={attraction.imageUrl!}
            alt={attraction.name}
            onError={() => setImageFailed(true)}
            className="aspect-[4/3] w-full object-cover"
          />
        ) : (
          <div className="flex aspect-[4/3] w-full items-center justify-center bg-slate-100 text-4xl">🏛️</div>
        )}
        <div className="flex flex-col gap-1 p-4 pb-0">
          <strong className="text-slate-900">{attraction.name}</strong>
          {attraction.category && <span className="text-sm text-slate-500">{attraction.category}</span>}
          {attraction.rating != null && (
            <span className="text-sm text-slate-700">⭐ {attraction.rating.toFixed(1)}</span>
          )}
        </div>
      </Link>
      <div className="p-4 pt-3">
        <AddToTripButton attraction={attraction} />
      </div>
    </div>
  );
}

/** F1/US3-US5 — recommended attractions near the selected city, with filters and sort. */
export function AttractionsList({ city }: { city: LocationSuggestion }) {
  const [attractions, setAttractions] = useState<AttractionSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // F1/US4-US5 — filters and sort are frontend concerns over the ≤20 loaded
  // results (spec §11.2); no API parameters involved.
  const [categoryFilter, setCategoryFilter] = useState('');
  const [minRating, setMinRating] = useState(''); // '' = any; otherwise a number as string
  const [sortBy, setSortBy] = useState<'recommended' | 'rating'>('recommended');

  useEffect(() => {
    let ignore = false;
    setLoading(true);
    setError(null);
    // A new city means new results — stale filters would silently hide them.
    setCategoryFilter('');
    setMinRating('');
    setSortBy('recommended');
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

  // Filter options come from the data itself — only categories that exist.
  const categories = useMemo(
    () => [...new Set(attractions.flatMap((a) => (a.category ? [a.category] : [])))].sort(),
    [attractions],
  );

  const visible = useMemo(() => {
    const filtered = attractions.filter(
      (a) =>
        (categoryFilter === '' || a.category === categoryFilter) &&
        (minRating === '' || (a.rating != null && a.rating >= Number(minRating))),
    );
    // "Recommended" keeps the API's order; rating sort puts unrated last (US5
    // keeps the filters because it sorts the already-filtered list).
    return sortBy === 'rating'
      ? [...filtered].sort((a, b) => (b.rating ?? -1) - (a.rating ?? -1))
      : filtered;
  }, [attractions, categoryFilter, minRating, sortBy]);

  const hasActiveFilters = categoryFilter !== '' || minRating !== '';

  if (loading) return <p className="text-center text-sm text-slate-500">Loading attractions…</p>;
  if (error) return <p className="text-center text-sm text-red-600">{error}</p>;
  if (attractions.length === 0) {
    return <EmptyState message={`No attractions found near ${city.name}.`} />;
  }

  return (
    <section>
      <h2 className="mb-4 text-lg font-semibold text-slate-900">Attractions near {city.name}</h2>

      <div className="mb-6 flex flex-wrap items-center gap-3 text-sm">
        <label className="flex items-center gap-1.5 text-slate-500">
          Category
          <select value={categoryFilter} onChange={(e) => setCategoryFilter(e.target.value)} className={fieldControlClass}>
            <option value="">All</option>
            {categories.map((category) => (
              <option key={category} value={category}>
                {category}
              </option>
            ))}
          </select>
        </label>

        <label className="flex items-center gap-1.5 text-slate-500">
          Rating
          <select value={minRating} onChange={(e) => setMinRating(e.target.value)} className={fieldControlClass}>
            <option value="">Any</option>
            <option value="3">3+ stars</option>
            <option value="4">4+ stars</option>
          </select>
        </label>

        <label className="flex items-center gap-1.5 text-slate-500">
          Sort by
          <select
            value={sortBy}
            onChange={(e) => setSortBy(e.target.value as 'recommended' | 'rating')}
            className={fieldControlClass}
          >
            <option value="recommended">Recommended</option>
            <option value="rating">Highest rating</option>
          </select>
        </label>

        {hasActiveFilters && (
          <Button
            type="button"
            variant="secondary"
            size="sm"
            onClick={() => {
              setCategoryFilter('');
              setMinRating('');
            }}
          >
            Clear filters
          </Button>
        )}
      </div>

      {visible.length === 0 ? (
        <EmptyState message="No attractions match your filters." />
      ) : (
        <div className="grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
          {visible.map((attraction) => (
            <AttractionCard key={attraction.providerId} attraction={attraction} />
          ))}
        </div>
      )}
    </section>
  );
}
