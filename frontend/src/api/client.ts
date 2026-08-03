import axios from 'axios';

const TOKEN_STORAGE_KEY = 'tripplanner.token';

/** Fired when a request 401s with a token attached — the session is stale/expired. AuthContext listens and clears itself. */
export const AUTH_LOGOUT_EVENT = 'tripplanner:auth-logout';

/**
 * A single configured axios instance used by the whole app.
 * A request interceptor attaches the JWT (when present) so authenticated
 * endpoints like /api/trips work automatically.
 */
export const apiClient = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5080/api',
  headers: { 'Content-Type': 'application/json' },
});

apiClient.interceptors.request.use((config) => {
  const token = localStorage.getItem(TOKEN_STORAGE_KEY);
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

apiClient.interceptors.response.use(
  (response) => response,
  (error) => {
    // Only a request that WAS carrying a token can mean "the session went
    // stale" — a failed login/register also 401s but has no token to expire.
    if (axios.isAxiosError(error) && error.response?.status === 401 && getToken()) {
      clearToken();
      window.dispatchEvent(new Event(AUTH_LOGOUT_EVENT));
    }
    return Promise.reject(error);
  },
);

export function storeToken(token: string): void {
  localStorage.setItem(TOKEN_STORAGE_KEY, token);
}

export function clearToken(): void {
  localStorage.removeItem(TOKEN_STORAGE_KEY);
}

export function getToken(): string | null {
  return localStorage.getItem(TOKEN_STORAGE_KEY);
}

/** The API returns a ProblemDetails body with a friendly "detail" message on failure. */
export function getErrorMessage(err: unknown, fallback: string): string {
  return axios.isAxiosError(err) ? (err.response?.data?.detail ?? fallback) : fallback;
}

/**
 * The HTTP status of a failed request, or undefined if it never got a response
 * (network error, timeout, or a non-HTTP throw). Pages use this when a specific
 * status changes the UI rather than just the message — e.g. 404 renders a
 * "not found" state instead of an error banner.
 *
 * Exists so feature components don't import axios themselves just to narrow the
 * error type; this module is the only place that should know the HTTP client.
 */
export function getErrorStatus(err: unknown): number | undefined {
  return axios.isAxiosError(err) ? err.response?.status : undefined;
}
