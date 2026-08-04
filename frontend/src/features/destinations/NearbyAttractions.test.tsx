import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthProvider } from '../../auth/AuthContext';
import { NearbyAttractions } from './NearbyAttractions';
import type { AttractionSummary } from '../../types';

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

function attraction(providerId: string, name: string): AttractionSummary {
  return { providerId, name, category: 'landmark', imageUrl: null, rating: null };
}

/** AttractionCard renders a Link and an AddToTripButton, so both contexts are required. */
function renderNearby(excludeProviderId = 'geo-self') {
  return render(
    <AuthProvider>
      <MemoryRouter>
        <NearbyAttractions latitude={15.99} longitude={107.99} excludeProviderId={excludeProviderId} />
      </MemoryRouter>
    </AuthProvider>,
  );
}

describe('NearbyAttractions', () => {
  beforeEach(() => {
    vi.mocked(destinationsApi.getAttractions).mockReset();
  });

  it('lists nearby attractions around the given point', async () => {
    vi.mocked(destinationsApi.getAttractions).mockResolvedValue([
      attraction('geo-1', 'Marble Mountains'),
      attraction('geo-2', 'Dragon Bridge'),
    ]);

    renderNearby();

    expect(await screen.findByRole('heading', { name: 'Nearby experiences' })).toBeInTheDocument();
    expect(screen.getByText('Marble Mountains')).toBeInTheDocument();
    // A tighter radius than the search page's — this is "what else is right here".
    expect(destinationsApi.getAttractions).toHaveBeenCalledWith(15.99, 107.99, 5);
  });

  /** The destination you are already looking at is not a "nearby experience". */
  it('excludes the destination being viewed', async () => {
    vi.mocked(destinationsApi.getAttractions).mockResolvedValue([
      attraction('geo-self', 'Golden Bridge'),
      attraction('geo-1', 'Marble Mountains'),
    ]);

    renderNearby('geo-self');

    expect(await screen.findByText('Marble Mountains')).toBeInTheDocument();
    expect(screen.queryByText('Golden Bridge')).not.toBeInTheDocument();
  });

  it('renders nothing at all when only the current destination comes back', async () => {
    vi.mocked(destinationsApi.getAttractions).mockResolvedValue([attraction('geo-self', 'Golden Bridge')]);

    renderNearby('geo-self');

    // An empty "Nearby experiences" heading would be worse than no section.
    await waitFor(() => expect(destinationsApi.getAttractions).toHaveBeenCalled());
    expect(screen.queryByRole('heading', { name: 'Nearby experiences' })).not.toBeInTheDocument();
  });

  it('stays hidden when the lookup fails', async () => {
    vi.mocked(destinationsApi.getAttractions).mockRejectedValue(new Error('boom'));

    renderNearby();

    // This is a secondary section on someone else's page — a failure here must not
    // put an error in front of the destination they actually asked for.
    await waitFor(() => expect(destinationsApi.getAttractions).toHaveBeenCalled());
    expect(screen.queryByRole('heading', { name: 'Nearby experiences' })).not.toBeInTheDocument();
  });

  it('caps the grid at six cards', async () => {
    vi.mocked(destinationsApi.getAttractions).mockResolvedValue(
      Array.from({ length: 10 }, (_, i) => attraction(`geo-${i}`, `Place ${i}`)),
    );

    renderNearby();

    expect(await screen.findByText('Place 0')).toBeInTheDocument();
    expect(screen.getByText('Place 5')).toBeInTheDocument();
    expect(screen.queryByText('Place 6')).not.toBeInTheDocument();
  });
});
