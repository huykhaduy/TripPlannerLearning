import { apiClient } from './client';
import type { AttractionSummary, DestinationDetails, LocationSuggestion } from '../types';

// Calls to the Destination search & details endpoints (Features 1 & 2). Public — no JWT needed.

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

export async function getDestinationDetails(providerId: string): Promise<DestinationDetails> {
  const { data } = await apiClient.get<DestinationDetails>(
    `/destinations/${encodeURIComponent(providerId)}`,
  );
  return data;
}
