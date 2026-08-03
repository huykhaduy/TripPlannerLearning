import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthProvider } from '../../auth/AuthContext';
import { httpError, networkError } from '../../test/http';
import { DestinationDetailsPage } from './DestinationDetailsPage';
import type { DestinationDetails } from '../../types';

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

// The page renders AddToTripButton, which loads the user's trips.
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
import * as tripsApi from '../../api/trips';

const details: DestinationDetails = {
  providerId: 'geo-123',
  name: 'Golden Bridge',
  category: 'landmark',
  description: 'A bridge held up by giant stone hands.',
  imageUrl: null,
  latitude: 15.99,
  longitude: 107.99,
  address: 'Ba Na Hills, Da Nang',
  website: null,
  openingHours: null,
  imageUrls: [],
};

/**
 * AuthProvider is required, not incidental: the success path renders
 * AddToTripButton, which calls useAuth. Only the error paths render without it,
 * because the page returns early before reaching that button.
 */
function renderDetails(providerId = 'geo-123') {
  return render(
    <AuthProvider>
      <MemoryRouter initialEntries={[`/destinations/${providerId}`]}>
        <Routes>
          <Route path="/destinations/:providerId" element={<DestinationDetailsPage />} />
          <Route path="/" element={<h1>Search</h1>} />
        </Routes>
      </MemoryRouter>
    </AuthProvider>,
  );
}

describe('DestinationDetailsPage', () => {
  beforeEach(() => {
    vi.mocked(destinationsApi.getDestinationDetails).mockReset();
    vi.mocked(destinationsApi.getAttractions).mockResolvedValue([]);
    vi.mocked(tripsApi.getMyTrips).mockResolvedValue([]);
  });

  it('renders the destination once loaded', async () => {
    vi.mocked(destinationsApi.getDestinationDetails).mockResolvedValue(details);

    renderDetails();

    expect(await screen.findByText('Golden Bridge')).toBeInTheDocument();
    expect(screen.getByText('A bridge held up by giant stone hands.')).toBeInTheDocument();
    expect(destinationsApi.getDestinationDetails).toHaveBeenCalledWith('geo-123');
  });

  /**
   * The reason this page needs getErrorStatus: a 404 means the place genuinely does
   * not exist (the provider forgot it and it was never saved to a trip), which is a
   * different message from "the request blew up". Collapsing the two would tell users
   * to retry something that can never succeed.
   */
  it('distinguishes a missing destination from a failed request', async () => {
    vi.mocked(destinationsApi.getDestinationDetails).mockRejectedValue(httpError(404, {}));

    renderDetails('unknown-id');

    expect(await screen.findByText('Destination not found.')).toBeInTheDocument();
  });

  it('shows a retryable message for a server error', async () => {
    vi.mocked(destinationsApi.getDestinationDetails).mockRejectedValue(httpError(500, {}));

    renderDetails();

    expect(
      await screen.findByText('Could not load this destination. Please try again.'),
    ).toBeInTheDocument();
  });

  it('shows a retryable message when the request never reached the server', async () => {
    vi.mocked(destinationsApi.getDestinationDetails).mockRejectedValue(networkError());

    renderDetails();

    // getErrorStatus returns undefined here, which must NOT be read as "not found".
    expect(
      await screen.findByText('Could not load this destination. Please try again.'),
    ).toBeInTheDocument();
    expect(screen.queryByText('Destination not found.')).not.toBeInTheDocument();
  });

  it('offers a way back to search from an error state', async () => {
    vi.mocked(destinationsApi.getDestinationDetails).mockRejectedValue(httpError(404, {}));

    renderDetails('unknown-id');
    await screen.findByText('Destination not found.');

    expect(screen.getByRole('button', { name: '← Back to search' })).toBeInTheDocument();
  });

  it('shows a loading state before the request settles', () => {
    // Never resolves — the page must not render an error or an empty shell.
    vi.mocked(destinationsApi.getDestinationDetails).mockReturnValue(new Promise(() => {}));

    renderDetails();

    expect(screen.queryByText('Destination not found.')).not.toBeInTheDocument();
    expect(screen.queryByText('Golden Bridge')).not.toBeInTheDocument();
  });
});
