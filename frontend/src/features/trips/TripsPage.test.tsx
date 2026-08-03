import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { httpError } from '../../test/http';
import { TripsPage } from './TripsPage';
import type { TripSummary } from '../../types';

vi.mock('../../api/trips', () => ({
  getMyTrips: vi.fn(),
  getTrip: vi.fn(),
  createTrip: vi.fn(),
  updateTrip: vi.fn(),
  addDestination: vi.fn(),
  updateItineraryItem: vi.fn(),
  removeDestination: vi.fn(),
}));

import * as tripsApi from '../../api/trips';

function trip(overrides: Partial<TripSummary> = {}): TripSummary {
  return {
    id: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
    name: 'Japan 2026',
    startDate: null,
    endDate: null,
    destinationCount: 0,
    coverImageUrl: null,
    ...overrides,
  };
}

/** Local YYYY-MM-DD, matching how getTripStatusLabel builds "today". */
function isoDaysFromToday(days: number): string {
  const d = new Date();
  d.setDate(d.getDate() + days);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

function renderTripsPage() {
  return render(
    <MemoryRouter>
      <TripsPage />
    </MemoryRouter>,
  );
}

describe('TripsPage', () => {
  beforeEach(() => {
    vi.mocked(tripsApi.getMyTrips).mockReset();
    vi.mocked(tripsApi.createTrip).mockReset();
  });

  it('lists the trips returned by the API', async () => {
    vi.mocked(tripsApi.getMyTrips).mockResolvedValue([
      trip({ id: 'trip-1', name: 'Japan 2026' }),
      trip({ id: 'trip-2', name: 'Iceland' }),
    ]);
    renderTripsPage();

    expect(await screen.findByText('Japan 2026')).toBeInTheDocument();
    expect(screen.getByText('Iceland')).toBeInTheDocument();
  });

  it('shows an empty state when there are no trips', async () => {
    vi.mocked(tripsApi.getMyTrips).mockResolvedValue([]);
    renderTripsPage();

    expect(
      await screen.findByText('No trips yet — create your first one above.'),
    ).toBeInTheDocument();
  });

  it('reports a load failure instead of an endless loading state', async () => {
    vi.mocked(tripsApi.getMyTrips).mockRejectedValue(httpError(500, {}));
    renderTripsPage();

    expect(
      await screen.findByText('Could not load your trips. Please try again.'),
    ).toBeInTheDocument();
    expect(screen.queryByText('Loading your trips…')).not.toBeInTheDocument();
  });

  describe('creating a trip', () => {
    it('appends the new trip to the list and closes the modal', async () => {
      vi.mocked(tripsApi.getMyTrips).mockResolvedValue([trip({ id: 'trip-1', name: 'Japan 2026' })]);
      vi.mocked(tripsApi.createTrip).mockResolvedValue(trip({ id: 'trip-2', name: 'Iceland' }));
      renderTripsPage();
      await screen.findByText('Japan 2026');

      await userEvent.click(screen.getByRole('button', { name: '+ Plan new trip' }));
      await userEvent.type(screen.getByLabelText('Trip name'), '  Iceland  ');
      await userEvent.click(screen.getByRole('button', { name: 'Create trip' }));

      // Trimmed before it reaches the API — the backend trims too, but the list
      // renders the client's value optimistically.
      expect(tripsApi.createTrip).toHaveBeenCalledWith('Iceland');
      expect(await screen.findByText('Iceland')).toBeInTheDocument();
      expect(screen.getByText('Japan 2026')).toBeInTheDocument();
      expect(screen.queryByLabelText('Trip name')).not.toBeInTheDocument();
    });

    it('keeps the modal open and shows the error when creation fails', async () => {
      vi.mocked(tripsApi.getMyTrips).mockResolvedValue([]);
      vi.mocked(tripsApi.createTrip).mockRejectedValue(
        httpError(400, { detail: 'Trip name is required.' }),
      );
      renderTripsPage();
      await screen.findByText('No trips yet — create your first one above.');

      await userEvent.click(screen.getByRole('button', { name: '+ Plan new trip' }));
      await userEvent.type(screen.getByLabelText('Trip name'), 'x');
      await userEvent.click(screen.getByRole('button', { name: 'Create trip' }));

      expect(await screen.findByText('Trip name is required.')).toBeInTheDocument();
      // Still open, so the user can correct the name rather than retyping from scratch.
      expect(screen.getByLabelText('Trip name')).toBeInTheDocument();
    });

    it('disables submission until a name is typed', async () => {
      vi.mocked(tripsApi.getMyTrips).mockResolvedValue([]);
      renderTripsPage();
      await screen.findByText('No trips yet — create your first one above.');
      await userEvent.click(screen.getByRole('button', { name: '+ Plan new trip' }));

      expect(screen.getByRole('button', { name: 'Create trip' })).toBeDisabled();

      // Whitespace alone is still empty after trimming.
      await userEvent.type(screen.getByLabelText('Trip name'), '   ');
      expect(screen.getByRole('button', { name: 'Create trip' })).toBeDisabled();

      await userEvent.type(screen.getByLabelText('Trip name'), 'Iceland');
      expect(screen.getByRole('button', { name: 'Create trip' })).toBeEnabled();
    });

    it('discards a half-typed name when the modal is cancelled', async () => {
      vi.mocked(tripsApi.getMyTrips).mockResolvedValue([]);
      renderTripsPage();
      await screen.findByText('No trips yet — create your first one above.');
      await userEvent.click(screen.getByRole('button', { name: '+ Plan new trip' }));
      await userEvent.type(screen.getByLabelText('Trip name'), 'Abandoned');

      await userEvent.click(screen.getByRole('button', { name: 'Cancel' }));
      await userEvent.click(screen.getByRole('button', { name: '+ Plan new trip' }));

      expect(screen.getByLabelText('Trip name')).toHaveValue('');
    });
  });

  /**
   * F3/US10 — the status pill is computed on the client from the trip's own dates
   * against today; there is no backend field for it. These are relative to the
   * current date on purpose, so the boundaries can't drift out of date.
   */
  describe('status pill', () => {
    it.each([
      ['Past trip', -10, -3],
      ['In progress', -1, 3],
      ['Tomorrow', 1, 5],
    ])('shows "%s"', async (expected, startOffset, endOffset) => {
      vi.mocked(tripsApi.getMyTrips).mockResolvedValue([
        trip({ startDate: isoDaysFromToday(startOffset), endDate: isoDaysFromToday(endOffset) }),
      ]);
      renderTripsPage();

      expect(await screen.findByText(expected)).toBeInTheDocument();
    });

    it('counts down for a trip further out', async () => {
      vi.mocked(tripsApi.getMyTrips).mockResolvedValue([
        trip({ startDate: isoDaysFromToday(9), endDate: isoDaysFromToday(14) }),
      ]);
      renderTripsPage();

      expect(await screen.findByText('In 9 days')).toBeInTheDocument();
    });

    it('shows no pill for a trip with no dates yet', async () => {
      vi.mocked(tripsApi.getMyTrips).mockResolvedValue([trip({ name: 'Undated' })]);
      renderTripsPage();
      await screen.findByText('Undated');

      for (const label of ['Past trip', 'In progress', 'Tomorrow']) {
        expect(screen.queryByText(label)).not.toBeInTheDocument();
      }
    });
  });
});
