import { apiClient } from './client';
import type { AuthResponse } from '../types';

// Calls to the reference Auth endpoints (Feature 4).

export async function register(
  email: string,
  password: string,
  displayName?: string,
): Promise<AuthResponse> {
  const { data } = await apiClient.post<AuthResponse>('/auth/register', {
    email,
    password,
    displayName,
  });
  return data;
}

export async function login(email: string, password: string): Promise<AuthResponse> {
  const { data } = await apiClient.post<AuthResponse>('/auth/login', { email, password });
  return data;
}

/** F4/US2 — verify the email behind a registration via the emailed link's token. */
export async function verifyEmail(token: string): Promise<void> {
  await apiClient.post('/auth/verify-email', { token });
}

/** F4/US2 — re-send the verification email for the current (logged-in) user. */
export async function resendVerificationEmail(): Promise<void> {
  await apiClient.post('/auth/resend-verification');
}
