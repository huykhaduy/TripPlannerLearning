import { AxiosError, AxiosHeaders } from 'axios';
import { describe, expect, it } from 'vitest';
import { getErrorMessage, getErrorStatus } from './client';

/**
 * These two helpers are the only sanctioned way for a component to inspect a failed
 * request — `src/api/` is the only place allowed to import axios — so every feature
 * page's error handling routes through them.
 */
describe('client error helpers', () => {
  /** An axios error carrying a real HTTP response, as the interceptor would reject with. */
  function httpError(status: number, data?: unknown): AxiosError {
    const config = { headers: new AxiosHeaders() };
    return new AxiosError('Request failed', 'ERR_BAD_REQUEST', config, undefined, {
      status,
      statusText: '',
      data,
      headers: new AxiosHeaders(),
      config,
    });
  }

  /** No response at all — DNS failure, connection refused, timeout. */
  function networkError(): AxiosError {
    return new AxiosError('Network Error', 'ERR_NETWORK');
  }

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
