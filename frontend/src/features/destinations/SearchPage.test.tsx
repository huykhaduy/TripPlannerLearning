import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthProvider } from '../../auth/AuthContext';
import { SearchPage } from './SearchPage';
import type { LocationSuggestion, User } from '../../types';

vi.mock('../../api/destinations', () => ({
  searchLocations: vi.fn(),
  getAttractions: vi.fn(),
  getDestinationDetails: vi.fn(),
}));

vi.mock('../../api/auth', () => ({
  login: vi.fn(),
  register: vi.fn(),
  verifyEmail: vi.fn(),
  resendVerificationEmail: vi.fn(),
}));

vi.mock('../../api/trips', () => ({
  getMyTrips: vi.fn(),
  getTrip: vi.fn(),
  createTrip: vi.fn(),
  updateTrip: vi.fn(),
  addDestination: vi.fn(),
  updateItineraryItem: vi.fn(),
  removeDestination: vi.fn(),
}));

import * as destinationsApi from '../../api/destinations';

const paris: LocationSuggestion = { name: 'Paris', country: 'France', latitude: 48.85, longitude: 2.35 };

const signedIn: User = {
  id: '11111111-1111-1111-1111-111111111111',
  email: 'user@example.com',
  displayName: null,
  isEmailVerified: true,
};

function renderSearch({ search = '', authenticated = false } = {}) {
  if (authenticated) localStorage.setItem('tripplanner.user', JSON.stringify(signedIn));

  return render(
    <AuthProvider>
      <MemoryRouter initialEntries={[`/${search}`]}>
        <SearchPage />
      </MemoryRouter>
    </AuthProvider>,
  );
}

describe('SearchPage', () => {
  beforeEach(() => {
    vi.mocked(destinationsApi.searchLocations).mockReset();
    vi.mocked(destinationsApi.getAttractions).mockReset();
    vi.mocked(destinationsApi.getAttractions).mockResolvedValue([]);
  });

  it('shows the hero prompt when no city is selected', () => {
    renderSearch();

    expect(screen.getByRole('heading', { name: 'Where to next?' })).toBeInTheDocument();
    expect(destinationsApi.getAttractions).not.toHaveBeenCalled();
  });

  it('loads attractions for the city named in the URL', async () => {
    renderSearch({ search: '?city=Paris&country=France&lat=48.85&lng=2.35' });

    await waitFor(() =>
      expect(destinationsApi.getAttractions).toHaveBeenCalledWith(48.85, 2.35),
    );
    // The city survives a refresh because it lives in the URL, not component state.
    expect(screen.getByLabelText('Search for a city')).toHaveValue('Paris, France');
  });

  it('puts the picked city into the URL', async () => {
    vi.mocked(destinationsApi.searchLocations).mockResolvedValue([paris]);
    renderSearch();

    await userEvent.type(screen.getByLabelText('Search for a city'), 'paris');
    await userEvent.click(await screen.findByRole('option', { name: 'Paris, France' }));

    await waitFor(() =>
      expect(destinationsApi.getAttractions).toHaveBeenCalledWith(48.85, 2.35),
    );
    // The hero collapses into the compact bar once a city is chosen.
    expect(screen.queryByRole('heading', { name: 'Where to next?' })).not.toBeInTheDocument();
  });

  /**
   * The guard that stops a truncated URL resolving to Null Island: Number(null) is 0,
   * a perfectly finite number, so presence has to be checked before conversion.
   * Without it, ?city=Paris alone would silently load attractions at (0, 0) —
   * the Atlantic — and look like a backend bug.
   */
  describe('malformed URLs', () => {
    it.each([
      ['no coordinates at all', '?city=Paris'],
      ['latitude missing', '?city=Paris&lng=2.35'],
      ['longitude missing', '?city=Paris&lat=48.85'],
      ['coordinates not numbers', '?city=Paris&lat=abc&lng=def'],
      ['no city name', '?lat=48.85&lng=2.35'],
    ])('treats "%s" as no city selected', async (_label, search) => {
      renderSearch({ search });

      expect(screen.getByRole('heading', { name: 'Where to next?' })).toBeInTheDocument();
      expect(destinationsApi.getAttractions).not.toHaveBeenCalled();
    });
  });

  it('accepts a city with no country', async () => {
    renderSearch({ search: '?city=Singapore&lat=1.35&lng=103.82' });

    await waitFor(() =>
      expect(destinationsApi.getAttractions).toHaveBeenCalledWith(1.35, 103.82),
    );
    expect(screen.getByLabelText('Search for a city')).toHaveValue('Singapore');
  });

  describe('the planning prompt', () => {
    it('invites an anonymous visitor to log in', () => {
      renderSearch();

      expect(screen.getByRole('link', { name: 'Log in' })).toBeInTheDocument();
      expect(screen.queryByRole('link', { name: 'Go to My trips' })).not.toBeInTheDocument();
    });

    it('points a signed-in user at their trips', () => {
      renderSearch({ authenticated: true });

      expect(screen.getByRole('link', { name: 'Go to My trips' })).toBeInTheDocument();
      expect(screen.queryByRole('link', { name: 'Log in' })).not.toBeInTheDocument();
    });
  });
});
