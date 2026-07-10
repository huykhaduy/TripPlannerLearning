import { apiClient } from './client';
import type { AttractionSummary, LocationSuggestion } from '../types';

// Calls to the Destination search endpoints (Feature 1). Public — no JWT needed.

export async function searchLocations(query: string): Promise<LocationSuggestion[]> {
  const { data } = await apiClient.get<LocationSuggestion[]>('/destinations/locations', {
    params: { query },
  });
  return data;
}

export async function getAttractions(
  lat: number,
  lng: number,
  radiusKm = 20,
): Promise<AttractionSummary[]> {
  const { data } = await apiClient.get<AttractionSummary[]>('/destinations/attractions', {
    params: { lat, lng, radiusKm },
  });
  return data;
}
