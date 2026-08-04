import { useEffect, useId, useRef, useState, type KeyboardEvent } from 'react';
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
export function CitySearchInput({
  onSelect,
  initialCity,
  size = 'lg',
}: {
  onSelect: (city: LocationSuggestion) => void;
  initialCity?: LocationSuggestion | null;
  /** 'sm' fits a compact bar (e.g. once a city is already selected); 'lg' (default) fits the full hero. */
  size?: 'lg' | 'sm';
}) {
  const initialLabel = initialCity ? formatCity(initialCity) : '';
  const [query, setQuery] = useState(initialLabel);
  const [suggestions, setSuggestions] = useState<LocationSuggestion[]>([]);
  const [open, setOpen] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // Selecting a suggestion writes its label into the input, which would
  // re-trigger the search below and reopen the dropdown — skip that query.
  // Also seeded from initialCity so a restored city (from the URL) doesn't
  // immediately re-search itself on mount.
  //
  // A ref, not state, on purpose: as a dependency of the search effect it would
  // re-run that effect the instant a suggestion is picked, while debouncedQuery
  // still held the OLD query — so the guard below wouldn't match, and the pick
  // would fire a second search that reopened the dropdown over the user's choice.
  const pickedLabelRef = useRef<string | null>(initialLabel || null);
  const [activeIndex, setActiveIndex] = useState(-1);
  const listboxId = useId();

  // The useState initializers above only seed the FIRST render. Without this,
  // browser back/forward restoring a DIFFERENT city in the URL updates the
  // results below (SearchPage reads the URL directly) but leaves this input
  // showing whatever the user last typed/picked — this resyncs it whenever
  // the actual selected-city label changes underneath the component.
  useEffect(() => {
    setQuery(initialLabel);
    pickedLabelRef.current = initialLabel || null;
  }, [initialLabel]);

  const debouncedQuery = useDebounce(query, 300);

  useEffect(() => {
    const trimmed = debouncedQuery.trim();
    if (trimmed.length < MIN_QUERY_LENGTH || trimmed === pickedLabelRef.current) {
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
  }, [debouncedQuery]);

  useEffect(() => {
    setActiveIndex(-1);
  }, [suggestions]);

  function handlePick(city: LocationSuggestion) {
    const label = formatCity(city);
    setQuery(label);
    pickedLabelRef.current = label;
    setOpen(false);
    setSuggestions([]);
    onSelect(city);
  }

  function handleKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (!open || suggestions.length === 0) return;
    if (event.key === 'ArrowDown') {
      event.preventDefault();
      setActiveIndex((i) => (i + 1) % suggestions.length);
    } else if (event.key === 'ArrowUp') {
      event.preventDefault();
      setActiveIndex((i) => (i - 1 + suggestions.length) % suggestions.length);
    } else if (event.key === 'Enter' && activeIndex >= 0) {
      event.preventDefault();
      handlePick(suggestions[activeIndex]);
    } else if (event.key === 'Escape') {
      setOpen(false);
    }
  }

  return (
    <div className="relative">
      <input
        type="search"
        placeholder="Search for a city, e.g. Paris"
        value={query}
        onChange={(e) => setQuery(e.target.value)}
        onKeyDown={handleKeyDown}
        aria-label="Search for a city"
        role="combobox"
        aria-expanded={open}
        aria-controls={listboxId}
        aria-autocomplete="list"
        aria-activedescendant={activeIndex >= 0 ? `${listboxId}-option-${activeIndex}` : undefined}
        className={`w-full rounded-full border border-slate-300 bg-white text-slate-900 shadow-sm focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-200 ${
          size === 'sm' ? 'px-4 py-2 text-sm' : 'px-5 py-3 text-base'
        }`}
      />
      {loading && <p className="mt-2 text-sm text-white/80">Searching…</p>}
      {error && <p className="mt-2 text-sm font-medium text-rose-100">{error}</p>}
      {open && !loading && suggestions.length === 0 && (
        <p className="mt-2 text-sm text-white/80">No matching cities found.</p>
      )}
      {open && suggestions.length > 0 && (
        <ul id={listboxId} role="listbox" className="absolute inset-x-0 top-full z-10 mt-2 overflow-hidden rounded-xl border border-slate-200 bg-white shadow-lg">
          {suggestions.map((city, index) => (
            <li key={`${city.latitude},${city.longitude}`} role="presentation">
              <button
                id={`${listboxId}-option-${index}`}
                role="option"
                aria-selected={index === activeIndex}
                type="button"
                onClick={() => handlePick(city)}
                onMouseEnter={() => setActiveIndex(index)}
                className={`block w-full px-4 py-2.5 text-left hover:bg-brand-50 ${index === activeIndex ? 'bg-brand-50' : ''}`}
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
