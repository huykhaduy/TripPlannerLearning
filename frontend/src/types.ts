// Shared types mirroring the backend DTOs.

export interface User {
  id: string;
  email: string;
  displayName?: string | null;
}

export interface AuthResponse {
  accessToken: string;
  expiresAt: string;
  user: User;
}

// F1/US1-2 — a city/country autocomplete suggestion (LocationSuggestionDto).
export interface LocationSuggestion {
  name: string;
  country: string | null;
  latitude: number;
  longitude: number;
}

// F1/US3 — a single attraction in the recommended list (DestinationSummaryDto).
export interface AttractionSummary {
  providerId: string;
  name: string;
  category: string | null;
  imageUrl: string | null;
  rating: number | null;
}

// F2/US1 — full details for a single destination (DestinationDetailsDto).
export interface DestinationDetails {
  providerId: string;
  name: string;
  category: string | null;
  description: string | null;
  imageUrl: string | null;
  latitude: number | null;
  longitude: number | null;
  address: string | null;
  website: string | null;
  openingHours: string | null;
}

// F3/US10 — summary row in the trip list (TripSummaryDto).
export interface TripSummary {
  id: string;
  name: string;
  startDate: string | null;
  endDate: string | null;
  destinationCount: number;
}

// F3 — a destination as it appears inside a trip (TripDestinationDto).
// itemId is the ItineraryItem's id — use it for remove/reorder calls.
export interface TripDestination {
  itemId: string;
  providerId: string;
  name: string;
  imageUrl: string | null;
  sortOrder: number;
}

// F3/US2 — one day of the itinerary with its scheduled destinations (ItineraryDayDto).
export interface ItineraryDay {
  id: string;
  date: string;
  dayNumber: number;
  destinations: TripDestination[];
}

// F3/US2 & US10 — full trip detail: days plus unscheduled Saved Places (TripDetailDto).
export interface TripDetail {
  id: string;
  name: string;
  startDate: string | null;
  endDate: string | null;
  days: ItineraryDay[];
  savedPlaces: TripDestination[];
}
