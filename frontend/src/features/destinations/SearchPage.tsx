import { useState } from 'react';
import { Link } from 'react-router-dom';
import { CitySearchInput } from './CitySearchInput';
import { AttractionsList } from './AttractionsList';
import type { LocationSuggestion } from '../../types';

/**
 * F1/US1-3 — public destination discovery page: city autocomplete +
 * recommended attractions. Filters/sorting (US4-5) and the details view
 * (Feature 2) come in later slices.
 */
export function SearchPage() {
  const [selectedCity, setSelectedCity] = useState<LocationSuggestion | null>(null);

  return (
    <div className="card">
      <h1>Discover destinations</h1>
      <p className="muted">Search for a city to see its recommended attractions.</p>
      <CitySearchInput onSelect={setSelectedCity} />
      {selectedCity && <AttractionsList city={selectedCity} />}
      <p>
        <Link to="/login">Log in</Link> to start planning a trip.
      </p>
    </div>
  );
}
