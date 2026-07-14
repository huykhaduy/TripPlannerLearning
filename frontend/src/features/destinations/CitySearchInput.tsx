import { useEffect, useState } from 'react';
import { searchLocations } from '../../api/destinations';
import { useDebounce } from '../../hooks/useDebounce';
import type { LocationSuggestion } from '../../types';

// Matches SearchLocationsRequestValidator.MinQueryLength on the backend —
// shorter queries would just get a 400 back.
const MIN_QUERY_LENGTH = 2;

function formatCity(city: LocationSuggestion): string {
  return city.country ? `${city.name}, ${city.country}` : city.name;
}

/** F1/US1-2 — debounced city autocomplete. Calls onSelect when a suggestion is picked. */
export function CitySearchInput({ onSelect }: { onSelect: (city: LocationSuggestion) => void }) {
  const [query, setQuery] = useState('');
  const [suggestions, setSuggestions] = useState<LocationSuggestion[]>([]);
  const [open, setOpen] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // Selecting a suggestion writes its label into the input, which would
  // re-trigger the search below and reopen the dropdown — skip that query.
  const [pickedLabel, setPickedLabel] = useState<string | null>(null);

  const debouncedQuery = useDebounce(query, 300);

  useEffect(() => {
    const trimmed = debouncedQuery.trim();
    if (trimmed.length < MIN_QUERY_LENGTH || trimmed === pickedLabel) {
      setSuggestions([]);
      setOpen(false);
      setLoading(false);
      setError(null);
      return;
    }

    let ignore = false;
    setLoading(true);
    setError(null);
    searchLocations(trimmed)
      .then((results) => {
        if (ignore) return;
        setSuggestions(results);
        setOpen(true);
      })
      .catch(() => {
        if (ignore) return;
        setError('Could not load suggestions. Please try again.');
        setSuggestions([]);
        setOpen(false);
      })
      .finally(() => {
        if (!ignore) setLoading(false);
      });

    return () => {
      ignore = true;
    };
  }, [debouncedQuery, pickedLabel]);

  function handlePick(city: LocationSuggestion) {
    const label = formatCity(city);
    setQuery(label);
    setPickedLabel(label);
    setOpen(false);
    setSuggestions([]);
    onSelect(city);
  }

  return (
    <div className="relative">
      <input
        type="search"
        placeholder="Search for a city, e.g. Paris"
        value={query}
        onChange={(e) => setQuery(e.target.value)}
        aria-label="Search for a city"
        className="w-full rounded-full border border-slate-300 bg-white px-5 py-3 text-base text-slate-900 shadow-sm focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-200"
      />
      {loading && <p className="mt-2 text-sm text-white/80">Searching…</p>}
      {error && <p className="mt-2 text-sm font-medium text-rose-100">{error}</p>}
      {open && !loading && suggestions.length === 0 && (
        <p className="mt-2 text-sm text-white/80">No matching cities found.</p>
      )}
      {open && suggestions.length > 0 && (
        <ul className="absolute inset-x-0 top-full z-10 mt-2 overflow-hidden rounded-xl border border-slate-200 bg-white shadow-lg">
          {suggestions.map((city) => (
            <li key={`${city.latitude},${city.longitude}`}>
              <button
                type="button"
                onClick={() => handlePick(city)}
                className="block w-full px-4 py-2.5 text-left hover:bg-brand-50"
              >
                {formatCity(city)}
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
