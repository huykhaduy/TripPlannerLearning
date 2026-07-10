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

// TODO (students): add Trip types as you build Feature 3.
