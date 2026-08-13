import { act, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AUTH_LOGOUT_EVENT, getToken } from '../../src/api/client';
import { AuthProvider, useAuth } from '../../src/auth/AuthContext';
import type { AuthResponse, User } from '../../src/types';

// The real module would hit the network; these tests are about what AuthProvider does
// with the response, not about the HTTP call itself.
vi.mock('../../src/api/auth', () => ({
  login: vi.fn(),
  register: vi.fn(),
  verifyEmail: vi.fn(),
  resendVerificationEmail: vi.fn(),
}));

import * as authApi from '../../src/api/auth';

const USER_STORAGE_KEY = 'tripplanner.user';

const user: User = {
  id: '11111111-1111-1111-1111-111111111111',
  email: 'user@example.com',
  displayName: 'Duy',
  isEmailVerified: true,
};

const authResponse: AuthResponse = {
  accessToken: 'jwt-token',
  expiresAt: '2099-01-01T00:00:00Z',
  user,
};

/** Surfaces the context so assertions can read it from the DOM. */
function Probe() {
  const { user: current, isAuthenticated, login, register, logout } = useAuth();

  return (
    <div>
      <span data-testid="authenticated">{String(isAuthenticated)}</span>
      <span data-testid="email">{current?.email ?? 'none'}</span>
      <button onClick={() => void login('user@example.com', 'password123')}>login</button>
      <button onClick={() => void register('new@example.com', 'password123')}>register</button>
      <button onClick={logout}>logout</button>
    </div>
  );
}

function renderWithProvider() {
  return render(
    <AuthProvider>
      <Probe />
    </AuthProvider>,
  );
}

describe('AuthProvider', () => {
  beforeEach(() => {
    vi.mocked(authApi.login).mockReset();
    vi.mocked(authApi.register).mockReset();
  });

  it('starts anonymous when nothing is stored', () => {
    renderWithProvider();

    expect(screen.getByTestId('authenticated')).toHaveTextContent('false');
    expect(screen.getByTestId('email')).toHaveTextContent('none');
  });

  it('stores the token and user on login', async () => {
    vi.mocked(authApi.login).mockResolvedValue(authResponse);
    renderWithProvider();

    await userEvent.click(screen.getByRole('button', { name: 'login' }));

    expect(screen.getByTestId('authenticated')).toHaveTextContent('true');
    expect(screen.getByTestId('email')).toHaveTextContent('user@example.com');
    expect(getToken()).toBe('jwt-token');
    expect(localStorage.getItem(USER_STORAGE_KEY)).toBe(JSON.stringify(user));
  });

  it('restores the session from localStorage on mount (F4/US3 — survive a refresh)', () => {
    // Simulates a page reload: the user was persisted by a previous visit.
    localStorage.setItem(USER_STORAGE_KEY, JSON.stringify(user));

    renderWithProvider();

    expect(screen.getByTestId('authenticated')).toHaveTextContent('true');
    expect(screen.getByTestId('email')).toHaveTextContent('user@example.com');
  });

  it('does NOT establish a session on register (F4/US2 blocks login until verified)', async () => {
    // register resolves with the created User — the point of this test is that
    // AuthProvider deliberately ignores it rather than starting a session.
    vi.mocked(authApi.register).mockResolvedValue({ ...user, isEmailVerified: false });
    renderWithProvider();

    await userEvent.click(screen.getByRole('button', { name: 'register' }));

    expect(screen.getByTestId('authenticated')).toHaveTextContent('false');
    expect(getToken()).toBeNull();
    expect(localStorage.getItem(USER_STORAGE_KEY)).toBeNull();
  });

  it('clears everything on logout', async () => {
    vi.mocked(authApi.login).mockResolvedValue(authResponse);
    renderWithProvider();
    await userEvent.click(screen.getByRole('button', { name: 'login' }));

    await userEvent.click(screen.getByRole('button', { name: 'logout' }));

    expect(screen.getByTestId('authenticated')).toHaveTextContent('false');
    expect(getToken()).toBeNull();
    expect(localStorage.getItem(USER_STORAGE_KEY)).toBeNull();
  });

  /**
   * The reactive-logout path. client.ts's response interceptor fires this event when a
   * request that WAS carrying a token comes back 401 — meaning the server no longer
   * honours the session. Without the listener, isAuthenticated stays true forever and
   * ProtectedRoute keeps the user on pages the API refuses to serve.
   */
  it('clears the session when the api layer signals a stale token', async () => {
    vi.mocked(authApi.login).mockResolvedValue(authResponse);
    renderWithProvider();
    await userEvent.click(screen.getByRole('button', { name: 'login' }));
    expect(screen.getByTestId('authenticated')).toHaveTextContent('true');

    act(() => {
      window.dispatchEvent(new Event(AUTH_LOGOUT_EVENT));
    });

    expect(screen.getByTestId('authenticated')).toHaveTextContent('false');
    expect(getToken()).toBeNull();
    expect(localStorage.getItem(USER_STORAGE_KEY)).toBeNull();
  });

  it('stops listening for the logout event once unmounted', () => {
    // Asserted against the listener registry rather than through behaviour: React 18
    // made setState-on-an-unmounted-component a silent no-op, so a leaked listener
    // throws nothing and logs nothing. Only add/remove bookkeeping can detect it.
    const added = vi.spyOn(window, 'addEventListener');
    const removed = vi.spyOn(window, 'removeEventListener');

    try {
      const { unmount } = renderWithProvider();
      const registered = added.mock.calls.find(([type]) => type === AUTH_LOGOUT_EVENT);
      expect(registered).toBeDefined();

      unmount();

      // Same event AND same handler reference — removeEventListener silently does
      // nothing if the function identity differs.
      expect(removed).toHaveBeenCalledWith(AUTH_LOGOUT_EVENT, registered![1]);
    } finally {
      added.mockRestore();
      removed.mockRestore();
    }
  });
});

describe('useAuth', () => {
  it('throws when used outside an AuthProvider', () => {
    // Guards against a component being mounted outside the provider in App.tsx —
    // otherwise it would read undefined and fail far from the cause.
    function Orphan() {
      useAuth();
      return null;
    }

    // React logs the error boundary trace; silence it so the output stays readable.
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {});
    try {
      expect(() => render(<Orphan />)).toThrow('useAuth must be used within an AuthProvider');
    } finally {
      consoleError.mockRestore();
    }
  });
});
