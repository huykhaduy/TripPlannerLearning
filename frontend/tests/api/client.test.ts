import { afterEach, describe, expect, it, vi } from 'vitest';
import { httpError, networkError, recordRequests } from '../http';
import {
  AUTH_LOGOUT_EVENT,
  apiClient,
  clearToken,
  getErrorMessage,
  getErrorStatus,
  getToken,
  storeToken,
} from '../../src/api/client';

/**
 * These two helpers are the only sanctioned way for a component to inspect a failed
 * request — `src/api/` is the only place allowed to import axios — so every feature
 * page's error handling routes through them.
 */
describe('client error helpers', () => {
  describe('getErrorMessage', () => {
    it("returns the backend's ProblemDetails detail when present", () => {
      const err = httpError(409, { detail: 'This destination is already in that part of the trip.' });

      expect(getErrorMessage(err, 'fallback')).toBe(
        'This destination is already in that part of the trip.',
      );
    });

    it('falls back when the response body has no detail', () => {
      expect(getErrorMessage(httpError(500, {}), 'Something went wrong.')).toBe(
        'Something went wrong.',
      );
    });

    it('falls back when there is no response at all', () => {
      expect(getErrorMessage(networkError(), 'Could not reach the server.')).toBe(
        'Could not reach the server.',
      );
    });

    it('falls back for a non-axios throw', () => {
      // A bug in our own code must not surface as an empty message.
      expect(getErrorMessage(new TypeError('x is not a function'), 'Fallback')).toBe('Fallback');
    });
  });

  describe('getErrorStatus', () => {
    it('returns the HTTP status of a failed request', () => {
      expect(getErrorStatus(httpError(404))).toBe(404);
    });

    it('distinguishes the statuses pages actually branch on', () => {
      // 403 drives the "verify your email" prompt on login; 404 drives "not found"
      // states on the destination and trip pages.
      expect(getErrorStatus(httpError(403))).toBe(403);
      expect(getErrorStatus(httpError(401))).toBe(401);
    });

    it('returns undefined when the request never got a response', () => {
      // Must be undefined, not 0 — pages compare with === 404 and a falsy number
      // would still be a number.
      expect(getErrorStatus(networkError())).toBeUndefined();
    });

    it('returns undefined for a non-axios throw', () => {
      expect(getErrorStatus(new TypeError('boom'))).toBeUndefined();
    });
  });
});

describe('token storage', () => {
  it('round-trips a token and clears it again', () => {
    expect(getToken()).toBeNull();

    storeToken('jwt.token.here');
    expect(getToken()).toBe('jwt.token.here');

    clearToken();
    expect(getToken()).toBeNull();
  });
});

describe('apiClient interceptors', () => {
  let restore = () => {};
  afterEach(() => restore());

  function stub({ status = 200 }: { status?: number } = {}) {
    const recorder = recordRequests(apiClient, { status });
    restore = recorder.restore;
    return recorder.requests;
  }

  describe('the request interceptor', () => {
    it('attaches the stored token as a Bearer header', () => {
      storeToken('jwt.token.here');
      const requests = stub();

      return apiClient.get('/trips').then(() => {
        expect(requests[0].headers.get('Authorization')).toBe('Bearer jwt.token.here');
      });
    });

    it('sends no Authorization header when nothing is stored', async () => {
      const requests = stub();

      await apiClient.get('/destinations/locations');

      // The destination endpoints are anonymous by design; sending an empty or
      // "Bearer null" header would be worse than sending none.
      expect(requests[0].headers.get('Authorization')).toBeUndefined();
    });

    it('picks up a token stored after the instance was created', async () => {
      // The interceptor reads localStorage per request rather than closing over a
      // value at module load, which is what lets a fresh login authenticate the
      // very next call without a reload.
      const requests = stub();

      await apiClient.get('/trips');
      storeToken('later.token');
      await apiClient.get('/trips');

      expect(requests[0].headers.get('Authorization')).toBeUndefined();
      expect(requests[1].headers.get('Authorization')).toBe('Bearer later.token');
    });
  });

  describe('the response interceptor', () => {
    it('clears the session and announces a logout when a token-bearing request 401s', async () => {
      storeToken('expired.token');
      const onLogout = vi.fn();
      window.addEventListener(AUTH_LOGOUT_EVENT, onLogout);
      stub({ status: 401 });

      await expect(apiClient.get('/trips')).rejects.toThrowError();

      expect(getToken()).toBeNull();
      expect(onLogout).toHaveBeenCalledTimes(1);
      window.removeEventListener(AUTH_LOGOUT_EVENT, onLogout);
    });

    it('stays quiet on a 401 with no token — a rejected login is not an expired session', async () => {
      // Without the `&& getToken()` guard, every mistyped password would fire the
      // logout flow at a user who was never logged in.
      const onLogout = vi.fn();
      window.addEventListener(AUTH_LOGOUT_EVENT, onLogout);
      stub({ status: 401 });

      await expect(apiClient.post('/auth/login')).rejects.toThrowError();

      expect(onLogout).not.toHaveBeenCalled();
      window.removeEventListener(AUTH_LOGOUT_EVENT, onLogout);
    });

    it('leaves the session alone on other failures', async () => {
      // A 403 (unverified email) or 404 says nothing about the token's validity —
      // logging the user out over one would be a bug, not a safety measure.
      storeToken('good.token');
      const onLogout = vi.fn();
      window.addEventListener(AUTH_LOGOUT_EVENT, onLogout);
      stub({ status: 403 });

      await expect(apiClient.get('/trips')).rejects.toThrowError();

      expect(getToken()).toBe('good.token');
      expect(onLogout).not.toHaveBeenCalled();
      window.removeEventListener(AUTH_LOGOUT_EVENT, onLogout);
    });

    it('rejects with the original error so callers can still read its status', async () => {
      storeToken('expired.token');
      stub({ status: 401 });

      const err = await apiClient.get('/trips').catch((e: unknown) => e);

      // Swallowing the rejection here would leave every caller's catch block dead.
      expect(getErrorStatus(err)).toBe(401);
    });

    it('passes a successful response straight through', async () => {
      const recorder = recordRequests(apiClient, { data: { id: 't1' } });
      restore = recorder.restore;

      await expect(apiClient.get('/trips/t1')).resolves.toMatchObject({ data: { id: 't1' } });
    });
  });
});
