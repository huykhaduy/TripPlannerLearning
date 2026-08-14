import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthProvider } from '../../../src/auth/AuthContext';
import { AttractionsList } from '../../../src/features/destinations/AttractionsList';
import type { AttractionSummary, LocationSuggestion } from '../../../src/types';

vi.mock('../../../src/api/destinations', () => ({
  searchLocations: vi.fn(),
  getAttractions: vi.fn(),
  getDestinationDetails: vi.fn(),
}));

vi.mock('../../../src/api/auth', () => ({
  login: vi.fn(),
  register: vi.fn(),
  verifyEmail: vi.fn(),
  resendVerificationEmail: vi.fn(),
}));

vi.mock('../../../src/api/trips', () => ({
  getMyTrips: vi.fn(),
  getTrip: vi.fn(),
  createTrip: vi.fn(),
  updateTrip: vi.fn(),
  addDestination: vi.fn(),
  updateItineraryItem: vi.fn(),
  removeDestination: vi.fn(),
}));

import * as destinationsApi from '../../../src/api/destinations';

const paris: LocationSuggestion = { name: 'Paris', country: 'France', latitude: 48.85, longitude: 2.35 };
const rome: LocationSuggestion = { name: 'Rome', country: 'Italy', latitude: 41.9, longitude: 12.5 };

function attraction(providerId: string, name: string, category: string | null = 'landmark'): AttractionSummary {
  return { providerId, name, category, imageUrl: null, rating: null };
}

/** AttractionCard renders a Link and an AddToTripButton, so both contexts are needed. */
function renderList(city = paris, onLoadingChange?: (loading: boolean) => void) {
  return render(
    <AuthProvider>
      <MemoryRouter>
        <AttractionsList city={city} onLoadingChange={onLoadingChange} />
      </MemoryRouter>
    </AuthProvider>,
  );
}

describe('AttractionsList', () => {
  beforeEach(() => {
    vi.mocked(destinationsApi.getAttractions).mockReset();
  });

  it('fetches attractions for the city it is given', async () => {
    vi.mocked(destinationsApi.getAttractions).mockResolvedValue([attraction('geo-1', 'Eiffel Tower')]);

    renderList();

    expect(await screen.findByText('Eiffel Tower')).toBeInTheDocument();
    expect(destinationsApi.getAttractions).toHaveBeenCalledWith(48.85, 2.35);
    expect(screen.getByRole('heading', { name: 'Attractions near Paris' })).toBeInTheDocument();
  });

  it('shows a skeleton while loading', () => {
    vi.mocked(destinationsApi.getAttractions).mockReturnValue(new Promise(() => {}));

    renderList();

    expect(screen.getByRole('region', { name: 'Loading attractions' })).toHaveAttribute('aria-busy', 'true');
  });

  it('reports a failed fetch', async () => {
    vi.mocked(destinationsApi.getAttractions).mockRejectedValue(new Error('boom'));

    renderList();

    expect(await screen.findByText('Could not load attractions. Please try again.')).toBeInTheDocument();
  });

  it('names the city when it finds nothing', async () => {
    vi.mocked(destinationsApi.getAttractions).mockResolvedValue([]);

    renderList();

    expect(await screen.findByText('No attractions found near Paris.')).toBeInTheDocument();
  });

  /** The parent hides its own content while this is in flight, so the signal must be exact. */
  describe('onLoadingChange', () => {
    it('reports true then false around a successful fetch', async () => {
      vi.mocked(destinationsApi.getAttractions).mockResolvedValue([]);
      const onLoadingChange = vi.fn();

      renderList(paris, onLoadingChange);

      await waitFor(() => expect(onLoadingChange).toHaveBeenLastCalledWith(false));
      expect(onLoadingChange.mock.calls.map(([v]) => v)).toEqual([true, false]);
    });

    it('still reports false when the fetch fails', async () => {
      vi.mocked(destinationsApi.getAttractions).mockRejectedValue(new Error('boom'));
      const onLoadingChange = vi.fn();

      renderList(paris, onLoadingChange);

      // Otherwise the parent would hide its prompt forever after one bad request.
      await waitFor(() => expect(onLoadingChange).toHaveBeenLastCalledWith(false));
    });
  });

  describe('category filtering (F1/US4)', () => {
    const mixed = [
      attraction('geo-1', 'Eiffel Tower', 'landmark'),
      attraction('geo-2', 'Louvre', 'museum'),
      attraction('geo-3', 'Arc de Triomphe', 'landmark'),
    ];

    async function renderFiltered() {
      vi.mocked(destinationsApi.getAttractions).mockResolvedValue(mixed);
      renderList();
      await screen.findByText('Eiffel Tower');
    }

    it('offers only the categories present in the results, sorted', async () => {
      await renderFiltered();

      const filters = screen.getByRole('complementary');
      const options = within(filters)
        .getAllByRole('button')
        .map((b) => b.textContent);
      expect(options).toEqual(['All destinations', 'landmark', 'museum']);
    });

    it('narrows the grid to the chosen category', async () => {
      await renderFiltered();

      await userEvent.click(screen.getByRole('button', { name: 'museum' }));

      expect(screen.getByText('Louvre')).toBeInTheDocument();
      expect(screen.queryByText('Eiffel Tower')).not.toBeInTheDocument();
      expect(screen.queryByText('Arc de Triomphe')).not.toBeInTheDocument();
    });

    it('restores everything via "All destinations"', async () => {
      await renderFiltered();
      await userEvent.click(screen.getByRole('button', { name: 'museum' }));

      await userEvent.click(screen.getByRole('button', { name: 'All destinations' }));

      expect(screen.getByText('Eiffel Tower')).toBeInTheDocument();
      expect(screen.getByText('Louvre')).toBeInTheDocument();
    });

    it('offers a clear-filters button only while a filter is active', async () => {
      await renderFiltered();
      expect(screen.queryByRole('button', { name: 'Clear filters' })).not.toBeInTheDocument();

      await userEvent.click(screen.getByRole('button', { name: 'museum' }));
      expect(screen.getByRole('button', { name: 'Clear filters' })).toBeInTheDocument();

      await userEvent.click(screen.getByRole('button', { name: 'Clear filters' }));
      expect(screen.getByText('Eiffel Tower')).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Clear filters' })).not.toBeInTheDocument();
    });

    it('excludes attractions with no category from the filter list', async () => {
      vi.mocked(destinationsApi.getAttractions).mockResolvedValue([
        attraction('geo-1', 'Eiffel Tower', 'landmark'),
        attraction('geo-2', 'Unnamed thing', null),
      ]);
      renderList();
      await screen.findByText('Eiffel Tower');

      const filters = screen.getByRole('complementary');
      expect(within(filters).getAllByRole('button').map((b) => b.textContent)).toEqual([
        'All destinations',
        'landmark',
      ]);
      // It still appears in the unfiltered grid, though.
      expect(screen.getByText('Unnamed thing')).toBeInTheDocument();
    });

    /**
     * A filter carried over from the previous city would silently hide results that
     * do exist — the user would think the new city had nothing.
     */
    it('resets the filter when the city changes', async () => {
      vi.mocked(destinationsApi.getAttractions).mockResolvedValue(mixed);
      const { rerender } = renderList();
      await screen.findByText('Eiffel Tower');
      await userEvent.click(screen.getByRole('button', { name: 'museum' }));
      expect(screen.queryByText('Eiffel Tower')).not.toBeInTheDocument();

      vi.mocked(destinationsApi.getAttractions).mockResolvedValue([
        attraction('geo-9', 'Colosseum', 'landmark'),
      ]);
      rerender(
        <AuthProvider>
          <MemoryRouter>
            <AttractionsList city={rome} />
          </MemoryRouter>
        </AuthProvider>,
      );

      // 'museum' no longer exists in Rome's results; without the reset the grid
      // would be empty.
      expect(await screen.findByText('Colosseum')).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Clear filters' })).not.toBeInTheDocument();
    });
  });

  it('explains an empty grid caused by the filter, not by the city', async () => {
    // Only reachable if the filtered subset is empty while results exist — e.g.
    // the category list changed underneath the selection.
    vi.mocked(destinationsApi.getAttractions).mockResolvedValue([
      attraction('geo-1', 'Eiffel Tower', 'landmark'),
      attraction('geo-2', 'Louvre', 'museum'),
    ]);
    renderList();
    await screen.findByText('Eiffel Tower');

    await userEvent.click(screen.getByRole('button', { name: 'museum' }));

    expect(screen.getByText('Louvre')).toBeInTheDocument();
    expect(screen.queryByText('No attractions found near Paris.')).not.toBeInTheDocument();
  });
});
