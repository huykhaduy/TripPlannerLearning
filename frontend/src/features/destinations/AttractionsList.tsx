import { useEffect, useMemo, useState } from 'react';
import { getAttractions } from '../../api/destinations';
import { AttractionCard } from './AttractionCard';
import { EmptyState } from '../../components/EmptyState';
import { fieldControlClass } from '../../components/Field';
import { Button } from '../../components/Button';
import type { AttractionSummary, LocationSuggestion } from '../../types';

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
    <section className="flex flex-col gap-6 md:flex-row md:items-start">
      <aside className="w-full flex-shrink-0 rounded-lg border border-[#E2E8F0] bg-white p-4 md:w-64">
        <h3 className="font-headline text-base font-semibold text-brand-600">Filters</h3>

        <div className="mt-4">
          <p className="mb-2 text-xs font-semibold uppercase tracking-wide text-[#434654]">Category</p>
          <div className="flex flex-col gap-1">
            <button
              type="button"
              onClick={() => setCategoryFilter('')}
              className={`rounded-md px-2 py-1.5 text-left text-sm ${
                categoryFilter === '' ? 'bg-brand-50 font-semibold text-brand-600' : 'text-slate-700 hover:bg-slate-50'
              }`}
            >
              All destinations
            </button>
            {categories.map((category) => (
              <button
                key={category}
                type="button"
                onClick={() => setCategoryFilter(category)}
                className={`rounded-md px-2 py-1.5 text-left text-sm capitalize ${
                  categoryFilter === category
                    ? 'bg-brand-50 font-semibold text-brand-600'
                    : 'text-slate-700 hover:bg-slate-50'
                }`}
              >
                {category}
              </button>
            ))}
          </div>
        </div>

        <div className="mt-6">
          <p className="mb-2 text-xs font-semibold uppercase tracking-wide text-[#434654]">Minimum rating</p>
          <div className="flex flex-col gap-1">
            {(['', '3', '4'] as const).map((value) => (
              <button
                key={value || 'any'}
                type="button"
                onClick={() => setMinRating(value)}
                className={`rounded-md px-2 py-1.5 text-left text-sm ${
                  minRating === value ? 'bg-brand-50 font-semibold text-brand-600' : 'text-slate-700 hover:bg-slate-50'
                }`}
              >
                {value === '' ? 'Any rating' : `⭐ ${value}+ stars`}
              </button>
            ))}
          </div>
        </div>

        <label className="mt-6 flex flex-col gap-1.5 text-sm text-slate-500">
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
            className="mt-4 w-full"
          >
            Clear filters
          </Button>
        )}
      </aside>

      <div className="flex-1">
        <h2 className="font-headline mb-4 text-lg font-semibold text-slate-900">Attractions near {city.name}</h2>
        {visible.length === 0 ? (
          <EmptyState message="No attractions match your filters." />
        ) : (
          <div className="grid gap-5 sm:grid-cols-2 xl:grid-cols-3">
            {visible.map((attraction) => (
              <AttractionCard key={attraction.providerId} attraction={attraction} />
            ))}
          </div>
        )}
      </div>
    </section>
  );
}
