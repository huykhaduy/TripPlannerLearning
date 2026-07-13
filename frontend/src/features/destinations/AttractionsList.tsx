import { useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { getAttractions } from '../../api/destinations';
import { AddToTripButton } from './AddToTripButton';
import type { AttractionSummary, LocationSuggestion } from '../../types';

function AttractionCard({ attraction }: { attraction: AttractionSummary }) {
  const [imageFailed, setImageFailed] = useState(false);
  const showImage = attraction.imageUrl && !imageFailed;

  return (
    <div className="overflow-hidden rounded-lg border border-slate-200 bg-white">
      {/* F2/US1 — open the details view. Add-to-trip stays outside this link
          so a button never ends up nested inside an anchor. */}
      <Link to={`/destinations/${encodeURIComponent(attraction.providerId)}`}>
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
        <div className="flex flex-col gap-1 p-3 pb-0 text-sm">
          <strong>{attraction.name}</strong>
          {attraction.category && <span className="text-slate-500">{attraction.category}</span>}
          {attraction.rating != null && <span>⭐ {attraction.rating.toFixed(1)}</span>}
        </div>
      </Link>
      <div className="p-3 pt-2">
        <AddToTripButton attraction={attraction} />
      </div>
    </div>
  );
}

const selectClass =
  'rounded-lg border border-slate-300 px-2 py-1.5 text-sm text-slate-800 focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-200';

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

  if (loading) return <p className="text-sm text-slate-500">Loading attractions…</p>;
  if (error) return <p className="text-sm text-red-600">{error}</p>;
  if (attractions.length === 0) {
    return <p className="text-sm text-slate-500">No attractions found near {city.name}.</p>;
  }

  return (
    <section>
      <h2 className="mb-3 text-lg font-semibold">Attractions near {city.name}</h2>

      <div className="mb-4 flex flex-wrap items-center gap-2 text-sm">
        <label className="flex items-center gap-1.5 text-slate-500">
          Category
          <select value={categoryFilter} onChange={(e) => setCategoryFilter(e.target.value)} className={selectClass}>
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
          <select value={minRating} onChange={(e) => setMinRating(e.target.value)} className={selectClass}>
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
            className={selectClass}
          >
            <option value="recommended">Recommended</option>
            <option value="rating">Highest rating</option>
          </select>
        </label>

        {hasActiveFilters && (
          <button
            type="button"
            onClick={() => {
              setCategoryFilter('');
              setMinRating('');
            }}
            className="rounded-lg border border-slate-300 px-2 py-1.5 text-slate-600 hover:bg-slate-50"
          >
            Clear filters
          </button>
        )}
      </div>

      {visible.length === 0 ? (
        <p className="text-sm text-slate-500">No attractions match your filters.</p>
      ) : (
        <div className="grid gap-4 sm:grid-cols-2 md:grid-cols-3">
          {visible.map((attraction) => (
            <AttractionCard key={attraction.providerId} attraction={attraction} />
          ))}
        </div>
      )}
    </section>
  );
}
