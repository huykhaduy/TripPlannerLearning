import { afterEach, describe, expect, it } from 'vitest';
import { recordRequests } from '../http';
import { apiClient } from '../../src/api/client';
import { getAttractions, getDestinationDetails, searchLocations } from '../../src/api/destinations';
import type { AttractionSummary, DestinationDetails, LocationSuggestion } from '../../src/types';

describe('destinations api', () => {
  let restore = () => {};
  afterEach(() => restore());

  function stub(data: unknown = {}) {
    const recorder = recordRequests(apiClient, { data });
    restore = recorder.restore;
    return recorder.requests;
  }

  describe('searchLocations', () => {
    it('gets /destinations/locations with the query as a param', async () => {
      const suggestions: LocationSuggestion[] = [
        { name: 'Hanoi', country: 'Vietnam', latitude: 21.03, longitude: 105.85 },
      ];
      const requests = stub(suggestions);

      const result = await searchLocations('hano');

      expect(requests[0].method).toBe('get');
      expect(requests[0].url).toBe('/destinations/locations');
      expect(requests[0].params).toEqual({ query: 'hano' });
      expect(result).toEqual(suggestions);
    });

    it('passes the query through as-is rather than trimming or casing it here', async () => {
      // CitySearchInput trims before calling, and the backend lowercases for its
      // cache key. Doing either again here would mean two places to keep in step.
      const requests = stub([]);

      await searchLocations('Ha Noi');

      expect(requests[0].params).toEqual({ query: 'Ha Noi' });
    });
  });

  describe('getAttractions', () => {
    it('gets /destinations/attractions with the coordinates and radius', async () => {
      const attractions: AttractionSummary[] = [
        { providerId: 'p1', name: 'Old Quarter', category: 'tourism', imageUrl: null, rating: 4.5 },
      ];
      const requests = stub(attractions);

      const result = await getAttractions(21.03, 105.85, 5);

      expect(requests[0].method).toBe('get');
      expect(requests[0].url).toBe('/destinations/attractions');
      expect(requests[0].params).toEqual({ lat: 21.03, lng: 105.85, radiusKm: 5 });
      expect(result).toEqual(attractions);
    });

    it('defaults the radius to 20 km when the caller does not choose one', async () => {
      const requests = stub([]);

      await getAttractions(21.03, 105.85);

      expect(requests[0].params).toEqual({ lat: 21.03, lng: 105.85, radiusKm: 20 });
    });

    it('sends the coordinates as numbers, not strings', async () => {
      // The backend binds these to double query parameters; a stringified value
      // that happened to parse would hide a type slip here until it did not.
      const requests = stub([]);

      await getAttractions(0, -0.1278);

      expect(requests[0].params).toMatchObject({ lat: 0, lng: -0.1278 });
      expect(typeof requests[0].params?.lat).toBe('number');
    });
  });

  describe('getDestinationDetails', () => {
    it('gets the destination by provider id', async () => {
      const details = { providerId: 'p1', name: 'Old Quarter' } as DestinationDetails;
      const requests = stub(details);

      const result = await getDestinationDetails('p1');

      expect(requests[0].method).toBe('get');
      expect(requests[0].url).toBe('/destinations/p1');
      expect(result).toEqual(details);
    });

    it('percent-encodes the provider id into the path', async () => {
      // A Geoapify place_id is an opaque, unbounded string; leaving it raw would let
      // a '/' or '?' in one silently retarget the request at a different endpoint.
      const requests = stub();

      await getDestinationDetails('51a/b?c d');

      expect(requests[0].url).toBe('/destinations/51a%2Fb%3Fc%20d');
    });

    it('handles the long non-Latin ids the backend deliberately does not cap', async () => {
      const providerId = `51${'a1b2'.repeat(60)}_Hà Nội`;
      const requests = stub();

      await getDestinationDetails(providerId);

      expect(requests[0].url).toBe(`/destinations/${encodeURIComponent(providerId)}`);
    });
  });
});
