import { apiClient } from './client';
import type { AuthResponse, User } from '../types';

// Calls to the reference Auth endpoints (Feature 4).

/** F4/US1 — creates the account and sends a verification email. Does NOT log the user in (F4/US2). */
export async function register(
  email: string,
  password: string,
  displayName?: string,
): Promise<User> {
  const { data } = await apiClient.post<User>('/auth/register', {
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

/** F4/US2 — re-send the verification email for an unverified account. Anonymous — no session exists to resend from, since login is blocked until verified. */
export async function resendVerificationEmail(email: string): Promise<void> {
  await apiClient.post('/auth/resend-verification', { email });
}
