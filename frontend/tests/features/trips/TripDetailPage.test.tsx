import { fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { httpError, networkError } from '../../http';
import { TripDetailPage } from '../../../src/features/trips/TripDetailPage';
import type { ItineraryDay, TripDestination, TripDetail } from '../../../src/types';

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

const TRIP_ID = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';

function destination(itemId: string, name: string, sortOrder = 0): TripDestination {
  return { itemId, providerId: `geo-${itemId}`, name, imageUrl: null, sortOrder };
}

function day(id: string, dayNumber: number, date: string, destinations: TripDestination[] = []): ItineraryDay {
  return { id, date, dayNumber, destinations };
}

function trip(overrides: Partial<TripDetail> = {}): TripDetail {
  return {
    id: TRIP_ID,
    name: 'Japan 2026',
    startDate: null,
    endDate: null,
    days: [],
    savedPlaces: [],
    ...overrides,
  };
}

function renderTrip() {
  return render(
    <MemoryRouter initialEntries={[`/trips/${TRIP_ID}`]}>
      <Routes>
        <Route path="/trips/:tripId" element={<TripDetailPage />} />
        <Route path="/trips" element={<h1>My trips</h1>} />
      </Routes>
    </MemoryRouter>,
  );
}

/** The page gates destructive actions behind window.confirm; jsdom has no real dialog. */
function stubConfirm(answer: boolean) {
  return vi.spyOn(window, 'confirm').mockReturnValue(answer);
}

describe('TripDetailPage', () => {
  beforeEach(() => {
    vi.mocked(tripsApi.getTrip).mockReset();
    vi.mocked(tripsApi.updateTrip).mockReset();
    vi.mocked(tripsApi.updateItineraryItem).mockReset();
    vi.mocked(tripsApi.removeDestination).mockReset();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  // -------------------------------------------------------------------
  // Load states
  // -------------------------------------------------------------------

  it('shows a skeleton until the trip arrives', () => {
    vi.mocked(tripsApi.getTrip).mockReturnValue(new Promise(() => {}));

    renderTrip();

    expect(screen.getByRole('region', { name: 'Loading trip' })).toHaveAttribute('aria-busy', 'true');
  });

  it('leaves the way out reachable while the trip loads', async () => {
    // A slow or wedged request must not trap the user on a page of placeholders.
    vi.mocked(tripsApi.getTrip).mockReturnValue(new Promise(() => {}));

    renderTrip();
    await userEvent.click(screen.getByRole('link', { name: '← Back to my trips' }));

    expect(await screen.findByRole('heading', { name: 'My trips' })).toBeInTheDocument();
  });

  it('replaces the skeleton with the loaded trip', async () => {
    vi.mocked(tripsApi.getTrip).mockResolvedValue(trip({ name: 'Japan 2026' }));

    renderTrip();
    await screen.findByRole('heading', { name: 'Japan 2026' });

    expect(screen.queryByRole('region', { name: 'Loading trip' })).not.toBeInTheDocument();
  });

  it('distinguishes a missing trip from a failed request', async () => {
    vi.mocked(tripsApi.getTrip).mockRejectedValue(httpError(404, {}));

    renderTrip();

    // 404 here also covers "someone else's trip" — the API deliberately makes the
    // two indistinguishable, and so does this message.
    expect(await screen.findByText('Trip not found.')).toBeInTheDocument();
  });

  it('shows a retryable message when the request fails for another reason', async () => {
    vi.mocked(tripsApi.getTrip).mockRejectedValue(networkError());

    renderTrip();

    expect(await screen.findByText('Could not load the trip. Please try again.')).toBeInTheDocument();
    expect(screen.queryByText('Trip not found.')).not.toBeInTheDocument();
  });

  // -------------------------------------------------------------------
  // Rendering
  // -------------------------------------------------------------------

  it('renders the days and their scheduled destinations', async () => {
    vi.mocked(tripsApi.getTrip).mockResolvedValue(
      trip({
        startDate: '2026-03-10',
        endDate: '2026-03-11',
        days: [
          day('day-1', 1, '2026-03-10', [destination('item-1', 'Golden Bridge')]),
          day('day-2', 2, '2026-03-11'),
        ],
        savedPlaces: [destination('item-2', 'Marble Mountains')],
      }),
    );

    renderTrip();

    expect(await screen.findByRole('heading', { name: 'Japan 2026' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /Day 1/ })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /Day 2/ })).toBeInTheDocument();
    expect(screen.getByText('Golden Bridge')).toBeInTheDocument();
    expect(screen.getByText('Marble Mountains')).toBeInTheDocument();
    expect(screen.getByText('Nothing planned yet — drag a destination here.')).toBeInTheDocument();
  });

  it('prompts for dates when the trip has no itinerary yet', async () => {
    vi.mocked(tripsApi.getTrip).mockResolvedValue(trip());

    renderTrip();

    expect(
      await screen.findByText('Set the trip dates to generate a day-by-day itinerary.'),
    ).toBeInTheDocument();
    // Saved Places is still offered so destinations can be collected before dating.
    expect(screen.getByRole('heading', { name: 'Saved Places' })).toBeInTheDocument();
  });

  // -------------------------------------------------------------------
  // Edit details (F3/US2)
  // -------------------------------------------------------------------

  describe('editing details', () => {
    const dated = trip({
      startDate: '2026-03-10',
      endDate: '2026-03-12',
      days: [day('day-1', 1, '2026-03-10'), day('day-2', 2, '2026-03-11'), day('day-3', 3, '2026-03-12')],
    });

    async function openEditor() {
      vi.mocked(tripsApi.getTrip).mockResolvedValue(dated);
      renderTrip();
      await screen.findByRole('heading', { name: 'Japan 2026' });
      await userEvent.click(screen.getByRole('button', { name: 'Edit details' }));
    }

    it('opens pre-filled with the trip as it currently stands', async () => {
      await openEditor();

      expect(screen.getByLabelText('Name')).toHaveValue('Japan 2026');
      expect(screen.getByLabelText('Start date')).toHaveValue('2026-03-10');
      expect(screen.getByLabelText('End date')).toHaveValue('2026-03-12');
    });

    it('saves a trimmed name and turns blank dates into nulls', async () => {
      await openEditor();
      vi.mocked(tripsApi.updateTrip).mockResolvedValue({ ...dated, name: 'Renamed', days: [] });

      await userEvent.clear(screen.getByLabelText('Name'));
      await userEvent.type(screen.getByLabelText('Name'), '  Renamed  ');
      await userEvent.clear(screen.getByLabelText('Start date'));
      await userEvent.clear(screen.getByLabelText('End date'));
      // Clearing every dated day means the confirm gate fires; those days are empty
      // so nothing is actually lost, but the guard only inspects the range.
      stubConfirm(true);
      await userEvent.click(screen.getByRole('button', { name: 'Save changes' }));

      // '' from <input type="date"> must reach the API as null, not an empty string.
      expect(tripsApi.updateTrip).toHaveBeenCalledWith(TRIP_ID, 'Renamed', null, null);
      expect(await screen.findByRole('heading', { name: 'Renamed' })).toBeInTheDocument();
    });

    it('keeps the modal open and shows the error when saving fails', async () => {
      await openEditor();
      vi.mocked(tripsApi.updateTrip).mockRejectedValue(
        httpError(400, { detail: 'A trip cannot be longer than 365 days.' }),
      );

      await userEvent.click(screen.getByRole('button', { name: 'Save changes' }));

      expect(await screen.findByText('A trip cannot be longer than 365 days.')).toBeInTheDocument();
      expect(screen.getByLabelText('Name')).toBeInTheDocument();
    });

    it('disables saving when the name is blanked out', async () => {
      await openEditor();

      await userEvent.clear(screen.getByLabelText('Name'));

      expect(screen.getByRole('button', { name: 'Save changes' })).toBeDisabled();
    });

    it('reverts unsaved edits when the modal is cancelled', async () => {
      await openEditor();
      await userEvent.clear(screen.getByLabelText('Name'));
      await userEvent.type(screen.getByLabelText('Name'), 'Abandoned');

      await userEvent.click(screen.getByRole('button', { name: 'Cancel' }));
      await userEvent.click(screen.getByRole('button', { name: 'Edit details' }));

      expect(screen.getByLabelText('Name')).toHaveValue('Japan 2026');
      expect(tripsApi.updateTrip).not.toHaveBeenCalled();
    });

    /**
     * F3/US2 AC5 — shrinking the range deletes the days outside it and sends their
     * destinations back to Saved Places. That is destructive and irreversible from
     * the UI, so it must be confirmed first.
     */
    it('warns before a date change that would unschedule destinations', async () => {
      vi.mocked(tripsApi.getTrip).mockResolvedValue(
        trip({
          startDate: '2026-03-10',
          endDate: '2026-03-12',
          days: [
            day('day-1', 1, '2026-03-10'),
            day('day-3', 3, '2026-03-12', [destination('item-1', 'Golden Bridge')]),
          ],
        }),
      );
      renderTrip();
      await screen.findByRole('heading', { name: 'Japan 2026' });
      await userEvent.click(screen.getByRole('button', { name: 'Edit details' }));
      const confirm = stubConfirm(false);

      // Shrink the range so day 3 — which HAS a destination — falls outside it.
      await userEvent.clear(screen.getByLabelText('End date'));
      await userEvent.type(screen.getByLabelText('End date'), '2026-03-10');
      await userEvent.click(screen.getByRole('button', { name: 'Save changes' }));

      expect(confirm).toHaveBeenCalled();
      // Declined, so nothing was sent and the modal stays put.
      expect(tripsApi.updateTrip).not.toHaveBeenCalled();
      expect(screen.getByLabelText('Name')).toBeInTheDocument();
    });

    it('does not warn when the days losing their place are empty', async () => {
      await openEditor(); // all three days are empty
      vi.mocked(tripsApi.updateTrip).mockResolvedValue(dated);
      const confirm = stubConfirm(true);

      await userEvent.clear(screen.getByLabelText('End date'));
      await userEvent.type(screen.getByLabelText('End date'), '2026-03-10');
      await userEvent.click(screen.getByRole('button', { name: 'Save changes' }));

      expect(confirm).not.toHaveBeenCalled();
      expect(tripsApi.updateTrip).toHaveBeenCalled();
    });
  });

  // -------------------------------------------------------------------
  // Removing a destination (F3/US7)
  // -------------------------------------------------------------------

  describe('removing a destination', () => {
    const withSaved = trip({ savedPlaces: [destination('item-1', 'Golden Bridge')] });

    it('asks for confirmation naming the destination', async () => {
      vi.mocked(tripsApi.getTrip).mockResolvedValue(withSaved);
      renderTrip();
      await screen.findByText('Golden Bridge');
      const confirm = stubConfirm(false);

      await userEvent.click(screen.getByRole('button', { name: 'Remove Golden Bridge' }));

      expect(confirm).toHaveBeenCalledWith('Remove Golden Bridge from the trip?');
      expect(tripsApi.removeDestination).not.toHaveBeenCalled();
      expect(screen.getByText('Golden Bridge')).toBeInTheDocument();
    });

    it('drops the destination from the list once removed', async () => {
      vi.mocked(tripsApi.getTrip).mockResolvedValue(withSaved);
      vi.mocked(tripsApi.removeDestination).mockResolvedValue(undefined);
      renderTrip();
      await screen.findByText('Golden Bridge');
      stubConfirm(true);

      await userEvent.click(screen.getByRole('button', { name: 'Remove Golden Bridge' }));

      expect(tripsApi.removeDestination).toHaveBeenCalledWith(TRIP_ID, 'item-1');
      expect(await screen.findByText('No saved places — add destinations from the Explore page.')).toBeInTheDocument();
    });

    it('keeps the destination and reports the failure when removal fails', async () => {
      vi.mocked(tripsApi.getTrip).mockResolvedValue(withSaved);
      vi.mocked(tripsApi.removeDestination).mockRejectedValue(httpError(500, {}));
      renderTrip();
      await screen.findByText('Golden Bridge');
      stubConfirm(true);

      await userEvent.click(screen.getByRole('button', { name: 'Remove Golden Bridge' }));

      expect(
        await screen.findByText('Could not remove the destination. Please try again.'),
      ).toBeInTheDocument();
      expect(screen.getByText('Golden Bridge')).toBeInTheDocument();
    });
  });

  // -------------------------------------------------------------------
  // Drag and drop (F3/US4-US6, NFR4)
  // -------------------------------------------------------------------

  describe('moving a destination', () => {
    const draggable = trip({
      startDate: '2026-03-10',
      endDate: '2026-03-10',
      days: [day('day-1', 1, '2026-03-10')],
      savedPlaces: [destination('item-1', 'Golden Bridge')],
    });

    /** The day column's drop zone — dropping on the area appends to the end. */
    function dayDropZone() {
      return screen.getByText('Nothing planned yet — drag a destination here.').parentElement!;
    }

    it('moves the destination into the day and tells the API', async () => {
      vi.mocked(tripsApi.getTrip).mockResolvedValue(draggable);
      vi.mocked(tripsApi.updateItineraryItem).mockResolvedValue(destination('item-1', 'Golden Bridge'));
      renderTrip();
      await screen.findByText('Golden Bridge');

      fireEvent.dragStart(screen.getByText('Golden Bridge').closest('li')!);
      fireEvent.drop(dayDropZone());

      // Dropped on the empty day's surrounding area → appended at position 0.
      expect(tripsApi.updateItineraryItem).toHaveBeenCalledWith(TRIP_ID, 'item-1', 'day-1', 0);
      // Optimistic: Saved Places empties immediately, without waiting for the API.
      expect(
        await screen.findByText('No saved places — add destinations from the Explore page.'),
      ).toBeInTheDocument();
    });

    /**
     * NFR4 wants the move to feel instant, so the page applies it locally before the
     * API confirms. The flip side is that a failure must put the item back exactly
     * where it was, rather than leaving the UI lying about the server's state.
     */
    it('rolls the move back when the API rejects it', async () => {
      vi.mocked(tripsApi.getTrip).mockResolvedValue(draggable);
      vi.mocked(tripsApi.updateItineraryItem).mockRejectedValue(
        httpError(409, { detail: 'This destination is already in that part of the trip.' }),
      );
      renderTrip();
      await screen.findByText('Golden Bridge');

      fireEvent.dragStart(screen.getByText('Golden Bridge').closest('li')!);
      fireEvent.drop(dayDropZone());

      expect(
        await screen.findByText('This destination is already in that part of the trip.'),
      ).toBeInTheDocument();
      // Back in Saved Places, and the day is empty again.
      expect(screen.getByText('Nothing planned yet — drag a destination here.')).toBeInTheDocument();
      expect(screen.getByText('Golden Bridge')).toBeInTheDocument();
    });

    it('ignores a drop that was not preceded by a drag', async () => {
      vi.mocked(tripsApi.getTrip).mockResolvedValue(draggable);
      renderTrip();
      await screen.findByText('Golden Bridge');

      fireEvent.drop(dayDropZone());

      expect(tripsApi.updateItineraryItem).not.toHaveBeenCalled();
    });
  });

  // -------------------------------------------------------------------
  // Saved Places filter
  // -------------------------------------------------------------------

  describe('filtering saved places', () => {
    const many = trip({
      savedPlaces: [
        destination('item-1', 'Golden Bridge', 0),
        destination('item-2', 'Marble Mountains', 1),
      ],
    });

    it('reports when nothing matches the query', async () => {
      vi.mocked(tripsApi.getTrip).mockResolvedValue(many);
      renderTrip();
      await screen.findByText('Golden Bridge');

      await userEvent.type(screen.getByLabelText('Search saved places'), 'zzz');

      expect(screen.getByText('No matches.')).toBeInTheDocument();
    });

    /**
     * Non-matching rows are hidden with a CSS class instead of being removed, so the
     * index-based drop positions stay aligned with the unfiltered array. That means
     * the assertion has to be on the class: jsdom loads no Tailwind stylesheet, so
     * toBeVisible() cannot see `display: none` and would wrongly report them visible.
     */
    it('hides non-matching rows without unmounting them', async () => {
      vi.mocked(tripsApi.getTrip).mockResolvedValue(many);
      renderTrip();
      await screen.findByText('Golden Bridge');

      await userEvent.type(screen.getByLabelText('Search saved places'), 'golden');

      expect(screen.getByText('Golden Bridge').closest('li')).not.toHaveClass('hidden');
      expect(screen.getByText('Marble Mountains').closest('li')).toHaveClass('hidden');
      expect(screen.queryByText('No matches.')).not.toBeInTheDocument();
    });

    it('restores every row when the query is cleared', async () => {
      vi.mocked(tripsApi.getTrip).mockResolvedValue(many);
      renderTrip();
      await screen.findByText('Golden Bridge');
      const search = screen.getByLabelText('Search saved places');
      await userEvent.type(search, 'golden');

      await userEvent.clear(search);

      const list = screen.getByText('Marble Mountains').closest('ul')!;
      for (const row of within(list).getAllByRole('listitem')) {
        expect(row).not.toHaveClass('hidden');
      }
    });
  });
});
