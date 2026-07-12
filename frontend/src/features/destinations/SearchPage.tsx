import { useState } from 'react';
import { Link } from 'react-router-dom';
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
  const [selectedCity, setSelectedCity] = useState<LocationSuggestion | null>(null);

  return (
    <div className="rounded-xl border border-slate-200 bg-white p-6 shadow-sm sm:p-8">
      <h1 className="mb-2 text-2xl font-bold">Discover destinations</h1>
      <p className="text-sm text-slate-500">Search for a city to see its recommended attractions.</p>
      <CitySearchInput onSelect={setSelectedCity} />
      {selectedCity && <AttractionsList city={selectedCity} />}
      <p className="mt-4 text-sm text-slate-500">
        {isAuthenticated ? (
          <>
            Ready to plan?{' '}
            <Link to="/trips" className="text-blue-600 hover:underline">
              Go to My trips
            </Link>
            .
          </>
        ) : (
          <>
            <Link to="/login" className="text-blue-600 hover:underline">
              Log in
            </Link>{' '}
            to start planning a trip.
          </>
        )}
      </p>
    </div>
  );
}
