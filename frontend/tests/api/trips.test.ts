import { afterEach, describe, expect, it } from 'vitest';
import { recordRequests } from '../http';
import { apiClient } from '../../src/api/client';
import {
  addDestination,
  createTrip,
  getMyTrips,
  getTrip,
  removeDestination,
  updateItineraryItem,
  updateTrip,
} from '../../src/api/trips';
import type { TripDestination, TripDetail, TripSummary } from '../../src/types';

describe('trips api', () => {
  let restore = () => {};
  afterEach(() => restore());

  function stub(data: unknown = {}) {
    const recorder = recordRequests(apiClient, { data });
    restore = recorder.restore;
    return recorder.requests;
  }

  it('gets the signed-in user\'s trips from /trips', async () => {
    const trips: TripSummary[] = [
      { id: 't1', name: 'Vietnam', startDate: null, endDate: null, destinationCount: 0, coverImageUrl: null },
    ];
    const requests = stub(trips);

    const result = await getMyTrips();

    expect(requests[0].method).toBe('get');
    expect(requests[0].url).toBe('/trips');
    expect(result).toEqual(trips);
  });

  it('attaches the stored JWT to trip requests', async () => {
    // The trip endpoints are the only [Authorize] ones, and every page test mocks
    // this module — so this is where "the token actually goes out" gets checked.
    localStorage.setItem('tripplanner.token', 'jwt.token.here');
    const requests = stub([]);

    await getMyTrips();

    expect(requests[0].headers.get('Authorization')).toBe('Bearer jwt.token.here');
  });

  it('gets a single trip by id', async () => {
    const trip = { id: 't1', name: 'Vietnam', days: [], savedPlaces: [] } as unknown as TripDetail;
    const requests = stub(trip);

    const result = await getTrip('t1');

    expect(requests[0].method).toBe('get');
    expect(requests[0].url).toBe('/trips/t1');
    expect(result).toEqual(trip);
  });

  it('creates a trip with just a name', async () => {
    // Dates are set later through updateTrip — POST /trips takes the name alone.
    const requests = stub();

    await createTrip('Vietnam 2026');

    expect(requests[0].method).toBe('post');
    expect(requests[0].url).toBe('/trips');
    expect(requests[0].body).toEqual({ name: 'Vietnam 2026' });
  });

  it('puts the full trip detail on update', async () => {
    const requests = stub();

    await updateTrip('t1', 'Vietnam 2026', '2026-03-01', '2026-03-08');

    expect(requests[0].method).toBe('put');
    expect(requests[0].url).toBe('/trips/t1');
    expect(requests[0].body).toEqual({
      name: 'Vietnam 2026',
      startDate: '2026-03-01',
      endDate: '2026-03-08',
    });
  });

  it('sends cleared dates as explicit nulls, not omitted keys', async () => {
    // Omitting them would leave the existing dates in place; null is what tells
    // the backend to clear them and drop the generated itinerary days.
    const requests = stub();

    await updateTrip('t1', 'Vietnam', null, null);

    expect(requests[0].body).toEqual({ name: 'Vietnam', startDate: null, endDate: null });
  });

  describe('addDestination', () => {
    it('posts the provider id and target day', async () => {
      const added: TripDestination = {
        itemId: 'i1',
        providerId: 'p1',
        name: 'Old Quarter',
        imageUrl: null,
        sortOrder: 0,
      };
      const requests = stub(added);

      const result = await addDestination('t1', 'p1', 'day-1');

      expect(requests[0].method).toBe('post');
      expect(requests[0].url).toBe('/trips/t1/destinations');
      expect(requests[0].body).toEqual({ providerId: 'p1', itineraryDayId: 'day-1' });
      expect(result).toEqual(added);
    });

    it('defaults to Saved Places by sending a null day', async () => {
      // AddToTripButton adds without picking a day; null is what "Saved Places"
      // means on the wire, so it has to be sent rather than left out.
      const requests = stub();

      await addDestination('t1', 'p1');

      expect(requests[0].body).toEqual({ providerId: 'p1', itineraryDayId: null });
    });
  });

  describe('updateItineraryItem', () => {
    it('puts the new day and position for the item', async () => {
      const requests = stub();

      await updateItineraryItem('t1', 'i1', 'day-2', 3);

      expect(requests[0].method).toBe('put');
      expect(requests[0].url).toBe('/trips/t1/destinations/i1');
      expect(requests[0].body).toEqual({ itineraryDayId: 'day-2', sortOrder: 3 });
    });

    it('moves an item back to Saved Places with a null day', async () => {
      const requests = stub();

      await updateItineraryItem('t1', 'i1', null, 0);

      expect(requests[0].body).toEqual({ itineraryDayId: null, sortOrder: 0 });
    });
  });

  it('deletes an item by its item id, not its provider id', async () => {
    // The same destination can sit in several days of one trip, so the itinerary
    // item id is the only thing that identifies which row to remove.
    const requests = stub();

    await removeDestination('t1', 'i1');

    expect(requests[0].method).toBe('delete');
    expect(requests[0].url).toBe('/trips/t1/destinations/i1');
  });
});
