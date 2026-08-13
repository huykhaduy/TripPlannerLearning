import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { CitySearchInput } from '../../../src/features/destinations/CitySearchInput';
import type { LocationSuggestion } from '../../../src/types';

vi.mock('../../../src/api/destinations', () => ({
  searchLocations: vi.fn(),
  getAttractions: vi.fn(),
  getDestinationDetails: vi.fn(),
}));

import * as destinationsApi from '../../../src/api/destinations';

const paris: LocationSuggestion = { name: 'Paris', country: 'France', latitude: 48.85, longitude: 2.35 };
const parisot: LocationSuggestion = { name: 'Parisot', country: 'France', latitude: 44.26, longitude: 1.86 };

/**
 * Real timers throughout: the 300 ms debounce is well inside findBy*'s 1 s default
 * timeout, so waiting for the outcome is simpler and less brittle than driving fake
 * timers through userEvent.
 */
function typeQuery(text: string) {
  return userEvent.type(screen.getByLabelText('Search for a city'), text);
}

describe('CitySearchInput', () => {
  const onSelect = vi.fn();

  beforeEach(() => {
    onSelect.mockReset();
    vi.mocked(destinationsApi.searchLocations).mockReset();
  });

  it('searches once the query is long enough and lists the matches', async () => {
    vi.mocked(destinationsApi.searchLocations).mockResolvedValue([paris, parisot]);
    render(<CitySearchInput onSelect={onSelect} />);

    await typeQuery('paris');

    expect(await screen.findByRole('option', { name: 'Paris, France' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Parisot, France' })).toBeInTheDocument();
  });

  /**
   * Mirrors SearchLocationsRequestValidator.MinQueryLength — a 1-character query
   * would only earn a 400, so it must not leave the client at all.
   */
  it('does not search a query shorter than the minimum', async () => {
    render(<CitySearchInput onSelect={onSelect} />);

    await typeQuery('p');
    // Long enough for the debounce to have fired if it were going to.
    await new Promise((resolve) => setTimeout(resolve, 400));

    expect(destinationsApi.searchLocations).not.toHaveBeenCalled();
  });

  it('debounces rapid typing into a single request', async () => {
    vi.mocked(destinationsApi.searchLocations).mockResolvedValue([paris]);
    render(<CitySearchInput onSelect={onSelect} />);

    await typeQuery('paris');
    await screen.findByRole('option', { name: 'Paris, France' });

    // Five keystrokes, one call — and it carries the final query, not a prefix.
    expect(destinationsApi.searchLocations).toHaveBeenCalledTimes(1);
    expect(destinationsApi.searchLocations).toHaveBeenCalledWith('paris');
  });

  it('trims the query before sending it', async () => {
    vi.mocked(destinationsApi.searchLocations).mockResolvedValue([]);
    render(<CitySearchInput onSelect={onSelect} />);

    await typeQuery('  paris  ');

    await waitFor(() => expect(destinationsApi.searchLocations).toHaveBeenCalledWith('paris'));
  });

  it('reports when nothing matches', async () => {
    vi.mocked(destinationsApi.searchLocations).mockResolvedValue([]);
    render(<CitySearchInput onSelect={onSelect} />);

    await typeQuery('zzzz');

    expect(await screen.findByText('No matching cities found.')).toBeInTheDocument();
  });

  it('reports a failed lookup', async () => {
    vi.mocked(destinationsApi.searchLocations).mockRejectedValue(new Error('boom'));
    render(<CitySearchInput onSelect={onSelect} />);

    await typeQuery('paris');

    expect(await screen.findByText('Could not load suggestions. Please try again.')).toBeInTheDocument();
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
  });

  describe('picking a suggestion', () => {
    it('reports the choice and closes the list', async () => {
      vi.mocked(destinationsApi.searchLocations).mockResolvedValue([paris]);
      render(<CitySearchInput onSelect={onSelect} />);
      await typeQuery('paris');

      await userEvent.click(await screen.findByRole('option', { name: 'Paris, France' }));

      expect(onSelect).toHaveBeenCalledWith(paris);
      expect(screen.getByLabelText('Search for a city')).toHaveValue('Paris, France');
      expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
    });

    /**
     * Picking writes the label into the input, which would otherwise look like a new
     * query and immediately reopen the dropdown over the results the user just chose.
     */
    it('does not re-search the label it just wrote into the input', async () => {
      vi.mocked(destinationsApi.searchLocations).mockResolvedValue([paris]);
      render(<CitySearchInput onSelect={onSelect} />);
      await typeQuery('paris');
      await userEvent.click(await screen.findByRole('option', { name: 'Paris, France' }));

      await new Promise((resolve) => setTimeout(resolve, 400));

      expect(destinationsApi.searchLocations).toHaveBeenCalledTimes(1);
      expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
    });
  });

  describe('keyboard navigation', () => {
    async function openSuggestions() {
      vi.mocked(destinationsApi.searchLocations).mockResolvedValue([paris, parisot]);
      render(<CitySearchInput onSelect={onSelect} />);
      await typeQuery('paris');
      await screen.findByRole('option', { name: 'Paris, France' });
    }

    it('moves the highlight down and selects with Enter', async () => {
      await openSuggestions();

      await userEvent.keyboard('{ArrowDown}{Enter}');

      expect(onSelect).toHaveBeenCalledWith(paris);
    });

    it('wraps around when arrowing past the end', async () => {
      await openSuggestions();

      // Two suggestions: down, down, down → back to the first.
      await userEvent.keyboard('{ArrowDown}{ArrowDown}{ArrowDown}{Enter}');

      expect(onSelect).toHaveBeenCalledWith(paris);
    });

    it('wraps around when arrowing before the start', async () => {
      await openSuggestions();

      // Down twice reaches the last option; up once returns to the first.
      await userEvent.keyboard('{ArrowDown}{ArrowDown}{ArrowUp}{Enter}');

      expect(onSelect).toHaveBeenCalledWith(paris);
    });

    it('arrows up from nothing highlighted onto the first option', async () => {
      await openSuggestions();

      // Documents actual behaviour: from activeIndex -1 the modular arithmetic
      // lands on 0, whereas many comboboxes jump to the LAST option here. Harmless,
      // but worth pinning so a future change to the wrap-around is deliberate.
      await userEvent.keyboard('{ArrowUp}{Enter}');

      expect(onSelect).toHaveBeenCalledWith(paris);
    });

    it('does nothing on Enter before anything is highlighted', async () => {
      await openSuggestions();

      await userEvent.keyboard('{Enter}');

      expect(onSelect).not.toHaveBeenCalled();
    });

    it('closes the list on Escape', async () => {
      await openSuggestions();

      await userEvent.keyboard('{Escape}');

      expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
    });
  });

  describe('initialCity', () => {
    it('starts showing the already-selected city without re-searching it', async () => {
      render(<CitySearchInput onSelect={onSelect} initialCity={paris} />);

      expect(screen.getByLabelText('Search for a city')).toHaveValue('Paris, France');
      await new Promise((resolve) => setTimeout(resolve, 400));
      expect(destinationsApi.searchLocations).not.toHaveBeenCalled();
    });

    /**
     * Browser back/forward changes the city in the URL; SearchPage re-reads it, so
     * this input has to follow. The useState initializers only seed the first render.
     */
    it('resyncs when the selected city changes underneath it', async () => {
      const { rerender } = render(<CitySearchInput onSelect={onSelect} initialCity={paris} />);

      rerender(<CitySearchInput onSelect={onSelect} initialCity={parisot} />);

      expect(screen.getByLabelText('Search for a city')).toHaveValue('Parisot, France');
      await new Promise((resolve) => setTimeout(resolve, 400));
      expect(destinationsApi.searchLocations).not.toHaveBeenCalled();
    });
  });
});
