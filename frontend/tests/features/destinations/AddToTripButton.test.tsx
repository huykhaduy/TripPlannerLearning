import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthProvider } from '../../../src/auth/AuthContext';
import { httpError } from '../../http';
import { AddToTripButton } from '../../../src/features/destinations/AddToTripButton';
import type { AttractionSummary, TripDetail, TripSummary, User } from '../../../src/types';

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

import * as tripsApi from '../../../src/api/trips';

const attraction: AttractionSummary = {
  providerId: 'geo-123',
  name: 'Golden Bridge',
  category: 'landmark',
  imageUrl: null,
  rating: null,
};

const signedIn: User = {
  id: '11111111-1111-1111-1111-111111111111',
  email: 'user@example.com',
  displayName: null,
  isEmailVerified: true,
};

function tripSummary(id: string, name: string): TripSummary {
  return { id, name, startDate: null, endDate: null, destinationCount: 0, coverImageUrl: null };
}

function tripDetail(id: string): TripDetail {
  return {
    id,
    name: 'Japan 2026',
    startDate: '2026-03-10',
    endDate: '2026-03-11',
    days: [
      { id: 'day-1', date: '2026-03-10', dayNumber: 1, destinations: [] },
      { id: 'day-2', date: '2026-03-11', dayNumber: 2, destinations: [] },
    ],
    savedPlaces: [],
  };
}

/** Surfaces the router state so the US8 login hand-off can be asserted. */
function LoginProbe() {
  const location = useLocation();
  const state = location.state as { from?: string; resumeAddId?: string } | null;
  return (
    <div>
      <h1>Sign in</h1>
      <span data-testid="from">{state?.from ?? 'none'}</span>
      <span data-testid="resume">{state?.resumeAddId ?? 'none'}</span>
    </div>
  );
}

function renderButton({ authenticated = false, state = null as unknown } = {}) {
  if (authenticated) localStorage.setItem('tripplanner.user', JSON.stringify(signedIn));

  return render(
    <AuthProvider>
      <MemoryRouter initialEntries={[{ pathname: '/destinations/geo-123', state }]}>
        <Routes>
          <Route path="/destinations/:providerId" element={<AddToTripButton attraction={attraction} />} />
          <Route path="/login" element={<LoginProbe />} />
        </Routes>
      </MemoryRouter>
    </AuthProvider>,
  );
}

describe('AddToTripButton', () => {
  beforeEach(() => {
    vi.mocked(tripsApi.getMyTrips).mockReset();
    vi.mocked(tripsApi.getTrip).mockReset();
    vi.mocked(tripsApi.addDestination).mockReset();
  });

  /**
   * F3/US8 — a logged-out visitor is sent to sign in, carrying both where they were
   * and which attraction they clicked, so the flow can resume afterwards.
   */
  it('sends an anonymous visitor to log in, remembering the attraction', async () => {
    renderButton();

    await userEvent.click(screen.getByRole('button', { name: 'Add to trip' }));

    expect(screen.getByRole('heading', { name: 'Sign in' })).toBeInTheDocument();
    expect(screen.getByTestId('from')).toHaveTextContent('/destinations/geo-123');
    expect(screen.getByTestId('resume')).toHaveTextContent('geo-123');
    expect(tripsApi.getMyTrips).not.toHaveBeenCalled();
  });

  it('reopens the dialog automatically after signing in', async () => {
    vi.mocked(tripsApi.getMyTrips).mockResolvedValue([tripSummary('trip-1', 'Japan 2026')]);

    renderButton({ authenticated: true, state: { resumeAddId: 'geo-123' } });

    expect(await screen.findByText('Japan 2026')).toBeInTheDocument();
  });

  /**
   * The search page renders one button per attraction and they all see the same
   * router state — only the one the user actually clicked may reopen.
   */
  it('ignores a resume note meant for a different attraction', async () => {
    vi.mocked(tripsApi.getMyTrips).mockResolvedValue([tripSummary('trip-1', 'Japan 2026')]);

    renderButton({ authenticated: true, state: { resumeAddId: 'geo-999' } });

    expect(screen.queryByText('Japan 2026')).not.toBeInTheDocument();
    expect(tripsApi.getMyTrips).not.toHaveBeenCalled();
  });

  describe('the dialog', () => {
    async function openDialog(trips: TripSummary[]) {
      vi.mocked(tripsApi.getMyTrips).mockResolvedValue(trips);
      renderButton({ authenticated: true });
      await userEvent.click(screen.getByRole('button', { name: 'Add to trip' }));
    }

    it('tells the user when they have no trips to add to', async () => {
      await openDialog([]);

      expect(
        await screen.findByText('You have no trips yet — create one on the My trips page first.'),
      ).toBeInTheDocument();
    });

    it('cannot add until a trip is chosen', async () => {
      await openDialog([tripSummary('trip-1', 'Japan 2026')]);
      await screen.findByText('Japan 2026');

      expect(screen.getByRole('button', { name: 'Add' })).toBeDisabled();
    });

    it('loads the chosen trip\'s days as scheduling options', async () => {
      await openDialog([tripSummary('trip-1', 'Japan 2026')]);
      vi.mocked(tripsApi.getTrip).mockResolvedValue(tripDetail('trip-1'));

      await userEvent.click(await screen.findByText('Japan 2026'));

      expect(await screen.findByRole('option', { name: 'Day 1 — 2026-03-10' })).toBeInTheDocument();
      // Defaulting to Saved Places keeps "add now, schedule later" the easy path.
      expect(screen.getByRole('option', { name: 'Saved Places (no day yet)' })).toBeInTheDocument();
      expect(screen.getByLabelText('Day')).toHaveValue('');
    });

    it('adds to Saved Places when no day is picked', async () => {
      await openDialog([tripSummary('trip-1', 'Japan 2026')]);
      vi.mocked(tripsApi.getTrip).mockResolvedValue(tripDetail('trip-1'));
      vi.mocked(tripsApi.addDestination).mockResolvedValue({
        itemId: 'item-1',
        providerId: 'geo-123',
        name: 'Golden Bridge',
        imageUrl: null,
        sortOrder: 0,
      });
      await userEvent.click(await screen.findByText('Japan 2026'));

      await userEvent.click(screen.getByRole('button', { name: 'Add' }));

      // '' from the select must become null, not an empty string.
      expect(tripsApi.addDestination).toHaveBeenCalledWith('trip-1', 'geo-123', null);
      expect(await screen.findByRole('button', { name: 'Added ✓' })).toBeInTheDocument();
    });

    it('adds onto the chosen day', async () => {
      await openDialog([tripSummary('trip-1', 'Japan 2026')]);
      vi.mocked(tripsApi.getTrip).mockResolvedValue(tripDetail('trip-1'));
      vi.mocked(tripsApi.addDestination).mockResolvedValue({
        itemId: 'item-1',
        providerId: 'geo-123',
        name: 'Golden Bridge',
        imageUrl: null,
        sortOrder: 0,
      });
      await userEvent.click(await screen.findByText('Japan 2026'));
      await screen.findByRole('option', { name: 'Day 2 — 2026-03-11' });

      await userEvent.selectOptions(screen.getByLabelText('Day'), 'day-2');
      await userEvent.click(screen.getByRole('button', { name: 'Add' }));

      expect(tripsApi.addDestination).toHaveBeenCalledWith('trip-1', 'geo-123', 'day-2');
    });

    it('keeps the dialog open and shows why the add failed', async () => {
      await openDialog([tripSummary('trip-1', 'Japan 2026')]);
      vi.mocked(tripsApi.getTrip).mockResolvedValue(tripDetail('trip-1'));
      vi.mocked(tripsApi.addDestination).mockRejectedValue(
        httpError(409, { detail: 'This destination is already in that part of the trip.' }),
      );
      await userEvent.click(await screen.findByText('Japan 2026'));

      await userEvent.click(screen.getByRole('button', { name: 'Add' }));

      expect(
        await screen.findByText('This destination is already in that part of the trip.'),
      ).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Add' })).toBeInTheDocument();
    });

    it('reports a failure to load the trips', async () => {
      vi.mocked(tripsApi.getMyTrips).mockRejectedValue(httpError(500, {}));
      renderButton({ authenticated: true });

      await userEvent.click(screen.getByRole('button', { name: 'Add to trip' }));

      expect(
        await screen.findByText('Could not load your trips. Please try again.'),
      ).toBeInTheDocument();
    });
  });
});
