import { useMemo } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { useAuth } from '../../auth/AuthContext';
import { CitySearchInput } from './CitySearchInput';
import { AttractionsList } from './AttractionsList';
import type { LocationSuggestion } from '../../types';

/**
 * F1/US1-3 — public destination discovery page: city autocomplete +
 * recommended attractions. Filters/sorting (US4-5) and the details view
 * (Feature 2) come in later slices.
 */
export function SearchPage() {
  const { isAuthenticated } = useAuth();
  // The selected city lives in the URL (not useState) so it survives a page
  // refresh and the "back" navigation from the destination details page.
  const [searchParams, setSearchParams] = useSearchParams();

  const selectedCity = useMemo<LocationSuggestion | null>(() => {
    const name = searchParams.get('city');
    const rawLat = searchParams.get('lat');
    const rawLng = searchParams.get('lng');
    // Check presence before converting — Number(null) is 0, a valid finite
    // number, so a truncated URL missing lat/lng would otherwise silently
    // resolve to Null Island (0, 0) instead of "no city selected".
    if (!name || rawLat === null || rawLng === null) return null;
    const latitude = Number(rawLat);
    const longitude = Number(rawLng);
    if (!Number.isFinite(latitude) || !Number.isFinite(longitude)) return null;
    return { name, country: searchParams.get('country'), latitude, longitude };
  }, [searchParams]);

  function handleSelect(city: LocationSuggestion) {
    setSearchParams({
      city: city.name,
      ...(city.country ? { country: city.country } : {}),
      lat: String(city.latitude),
      lng: String(city.longitude),
    });
  }

  return (
    <div className="flex flex-col gap-8">
      <div className="rounded-3xl bg-brand-600 px-6 py-14 text-center sm:py-20">
        <h1 className="font-headline text-4xl font-bold tracking-tight text-white sm:text-5xl">Where to next?</h1>
        <p className="mt-3 text-brand-100">Search a city to see its recommended attractions.</p>
        <div className="mx-auto mt-6 max-w-xl">
          <CitySearchInput onSelect={handleSelect} initialCity={selectedCity} />
        </div>
      </div>

      {selectedCity && <AttractionsList city={selectedCity} />}

      <p className="text-center text-sm text-slate-500">
        {isAuthenticated ? (
          <>
            Ready to plan?{' '}
            <Link to="/trips" className="text-brand-600 hover:underline">
              Go to My trips
            </Link>
            .
          </>
        ) : (
          <>
            <Link to="/login" className="text-brand-600 hover:underline">
              Log in
            </Link>{' '}
            to start planning a trip.
          </>
        )}
      </p>
    </div>
  );
}
