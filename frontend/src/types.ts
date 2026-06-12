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

// TODO (students): add Trip / Destination types as you build those features.
