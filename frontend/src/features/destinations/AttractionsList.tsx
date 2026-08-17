import { useEffect, useMemo, useState } from 'react';
import { getAttractions } from '../../api/destinations';
import { AttractionCard } from './AttractionCard';
import { AttractionCardSkeleton } from './AttractionCardSkeleton';
import { categoryColor } from './categoryColor';
import { EmptyState } from '../../components/EmptyState';
import { Button } from '../../components/Button';
import type { AttractionSummary, LocationSuggestion } from '../../types';

/** F1/US3-US4 — recommended attractions near the selected city, with category filtering. */
export function AttractionsList({
  city,
  onLoadingChange,
}: {
  city: LocationSuggestion;
  /** Reports the fetch's in-flight status to the parent (e.g. to hide other page content while loading). */
  onLoadingChange?: (loading: boolean) => void;
}) {
  const [attractions, setAttractions] = useState<AttractionSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // F1/US4 — category filtering is a frontend concern over the ≤20 loaded
  // results (spec §11.2); no API parameters involved.
  const [categoryFilter, setCategoryFilter] = useState('');

  useEffect(() => {
    let ignore = false;
    setLoading(true);
    onLoadingChange?.(true);
    setError(null);
    // A new city means new results — a stale filter would silently hide them.
    setCategoryFilter('');
    getAttractions(city.latitude, city.longitude)
      .then((results) => {
        if (!ignore) setAttractions(results);
      })
      .catch(() => {
        if (!ignore) setError('Could not load attractions. Please try again.');
      })
      .finally(() => {
        if (!ignore) {
          setLoading(false);
          onLoadingChange?.(false);
        }
      });

    return () => {
      ignore = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps -- onLoadingChange is a setState passed by the parent; including it would re-run this fetch if the parent re-renders with a new inline function.
  }, [city]);

  // Filter options come from the data itself — only categories that exist.
  const categories = useMemo(
    () => [...new Set(attractions.flatMap((a) => (a.category ? [a.category] : [])))].sort(),
    [attractions],
  );

  const visible = useMemo(
    () => attractions.filter((a) => categoryFilter === '' || a.category === categoryFilter),
    [attractions, categoryFilter],
  );

  const hasActiveFilters = categoryFilter !== '';

  if (loading) {
    return (
      <section className="flex flex-col gap-6 md:flex-row md:items-start" aria-busy="true" aria-label="Loading attractions">
        <aside className="w-full flex-shrink-0 rounded-lg border border-[#E2E8F0] bg-white p-4 md:w-64">
          <h2 className="font-headline text-base font-semibold text-brand-600">Filters</h2>
          <div className="mt-4 flex flex-col gap-2">
            <div className="h-7 w-full animate-pulse rounded-md bg-slate-100" />
            <div className="h-7 w-3/4 animate-pulse rounded-md bg-slate-100" />
            <div className="h-7 w-2/3 animate-pulse rounded-md bg-slate-100" />
          </div>
        </aside>

        <div className="flex-1">
          <div className="mb-4 h-6 w-56 animate-pulse rounded bg-slate-200" />
          <div className="grid gap-5 sm:grid-cols-2 xl:grid-cols-3">
            {Array.from({ length: 6 }, (_, i) => (
              <AttractionCardSkeleton key={i} />
            ))}
          </div>
        </div>
      </section>
    );
  }
  if (error) return <p className="text-center text-sm text-red-600">{error}</p>;
  if (attractions.length === 0) {
    return <EmptyState message={`No attractions found near ${city.name}.`} />;
  }

  return (
    <section className="flex flex-col gap-6 md:flex-row md:items-start">
      <aside className="w-full flex-shrink-0 rounded-lg border border-[#E2E8F0] bg-white p-4 md:w-64">
        <h2 className="font-headline text-base font-semibold text-brand-600">Filters</h2>

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
            {categories.map((category) => {
              const colors = categoryColor(category);
              const selected = categoryFilter === category;
              return (
                <button
                  key={category}
                  type="button"
                  onClick={() => setCategoryFilter(category)}
                  className={`rounded-md px-2 py-1.5 text-left text-sm capitalize ${
                    selected ? `${colors.bg} font-semibold ${colors.text}` : 'text-slate-700 hover:bg-slate-50'
                  }`}
                >
                  {category}
                </button>
              );
            })}
          </div>
        </div>

        {hasActiveFilters && (
          <Button
            type="button"
            variant="secondary"
            size="sm"
            onClick={() => setCategoryFilter('')}
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
