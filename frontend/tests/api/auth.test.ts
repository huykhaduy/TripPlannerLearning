import { afterEach, describe, expect, it } from 'vitest';
import { recordRequests } from '../http';
import { login, register, resendVerificationEmail, verifyEmail } from '../../src/api/auth';
import { apiClient } from '../../src/api/client';
import type { AuthResponse, User } from '../../src/types';

/**
 * Every feature-page test mocks `../../api/auth` wholesale, so nothing else in the
 * suite would notice a wrong path or a renamed body field — these wrappers are the
 * only place that contract is actually pinned.
 */
describe('auth api', () => {
  let restore = () => {};
  afterEach(() => restore());

  function stub(data: unknown = {}) {
    const recorder = recordRequests(apiClient, { data });
    restore = recorder.restore;
    return recorder.requests;
  }

  describe('register', () => {
    it('posts the credentials to /auth/register and returns the created user', async () => {
      const created: User = {
        id: 'u1',
        email: 'ada@example.com',
        displayName: 'Ada',
        isEmailVerified: false,
      };
      const requests = stub(created);

      const user = await register('ada@example.com', 'correct horse', 'Ada');

      expect(requests).toHaveLength(1);
      expect(requests[0].method).toBe('post');
      expect(requests[0].url).toBe('/auth/register');
      expect(requests[0].body).toEqual({
        email: 'ada@example.com',
        password: 'correct horse',
        displayName: 'Ada',
      });
      expect(user).toEqual(created);
    });

    it('leaves displayName out of the body entirely when it is not given', async () => {
      // RegisterPage sends undefined rather than '' for a blank name; serializing
      // it as null instead would hand the backend a value it did not ask for.
      const requests = stub();

      await register('ada@example.com', 'correct horse');

      expect(requests[0].body).toEqual({ email: 'ada@example.com', password: 'correct horse' });
      expect(requests[0].body).not.toHaveProperty('displayName');
    });
  });

  describe('login', () => {
    it('posts to /auth/login and returns the token with its user', async () => {
      const response: AuthResponse = {
        accessToken: 'jwt.token.here',
        expiresAt: '2026-01-01T00:00:00Z',
        user: { id: 'u1', email: 'ada@example.com', displayName: 'Ada', isEmailVerified: true },
      };
      const requests = stub(response);

      const result = await login('ada@example.com', 'correct horse');

      expect(requests[0].method).toBe('post');
      expect(requests[0].url).toBe('/auth/login');
      expect(requests[0].body).toEqual({ email: 'ada@example.com', password: 'correct horse' });
      expect(result).toEqual(response);
    });
  });

  describe('verifyEmail', () => {
    it('posts the emailed token to /auth/verify-email', async () => {
      const requests = stub();

      await verifyEmail('verification-token');

      expect(requests[0].method).toBe('post');
      expect(requests[0].url).toBe('/auth/verify-email');
      expect(requests[0].body).toEqual({ token: 'verification-token' });
    });
  });

  describe('resendVerificationEmail', () => {
    it('posts the address to /auth/resend-verification', async () => {
      const requests = stub();

      await resendVerificationEmail('ada@example.com');

      expect(requests[0].method).toBe('post');
      expect(requests[0].url).toBe('/auth/resend-verification');
      expect(requests[0].body).toEqual({ email: 'ada@example.com' });
    });

    it('sends no Authorization header — the caller is not logged in yet', async () => {
      // Login is blocked until the address is verified, so this endpoint has to be
      // reachable anonymously; it is the one resend path LoginPage's 403 branch uses.
      const requests = stub();

      await resendVerificationEmail('ada@example.com');

      expect(requests[0].headers.get('Authorization')).toBeUndefined();
    });
  });
});
