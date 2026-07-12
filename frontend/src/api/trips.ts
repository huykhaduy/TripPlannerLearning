import { apiClient } from './client';
import type { TripDestination, TripDetail, TripSummary } from '../types';

// Calls to the Trip endpoints (Feature 3). All require the JWT — apiClient attaches it.

export async function getMyTrips(): Promise<TripSummary[]> {
  const { data } = await apiClient.get<TripSummary[]>('/trips');
  return data;
}

export async function getTrip(tripId: string): Promise<TripDetail> {
  const { data } = await apiClient.get<TripDetail>(`/trips/${tripId}`);
  return data;
}

export async function createTrip(name: string): Promise<TripSummary> {
  const { data } = await apiClient.post<TripSummary>('/trips', { name });
  return data;
}

export async function updateTrip(
  tripId: string,
  name: string,
  startDate: string | null,
  endDate: string | null,
): Promise<TripDetail> {
  const { data } = await apiClient.put<TripDetail>(`/trips/${tripId}`, {
    name,
    startDate,
    endDate,
  });
  return data;
}

export async function addDestination(
  tripId: string,
  providerId: string,
  itineraryDayId: string | null = null,
): Promise<TripDestination> {
  const { data } = await apiClient.post<TripDestination>(`/trips/${tripId}/destinations`, {
    providerId,
    itineraryDayId,
  });
  return data;
}

export async function removeDestination(tripId: string, itemId: string): Promise<void> {
  await apiClient.delete(`/trips/${tripId}/destinations/${itemId}`);
}
