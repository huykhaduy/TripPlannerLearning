import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthProvider } from '../../../src/auth/AuthContext';
import { httpError } from '../../http';
import { LoginPage } from '../../../src/features/auth/LoginPage';
import type { AuthResponse, User } from '../../../src/types';

vi.mock('../../../src/api/auth', () => ({
  login: vi.fn(),
  register: vi.fn(),
  verifyEmail: vi.fn(),
  resendVerificationEmail: vi.fn(),
}));

import * as authApi from '../../../src/api/auth';

const user: User = {
  id: '11111111-1111-1111-1111-111111111111',
  email: 'user@example.com',
  displayName: null,
  isEmailVerified: true,
};

const authResponse: AuthResponse = {
  accessToken: 'jwt-token',
  expiresAt: '2099-01-01T00:00:00Z',
  user,
};

/** Mirrors App.tsx closely enough to observe redirects after a successful login. */
function renderLogin(initialEntry: string | { pathname: string; state: unknown } = '/login') {
  return render(
    <AuthProvider>
      <MemoryRouter initialEntries={[initialEntry]}>
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          <Route path="/trips" element={<h1>My trips</h1>} />
          <Route path="/destinations/:providerId" element={<h1>Destination page</h1>} />
        </Routes>
      </MemoryRouter>
    </AuthProvider>,
  );
}

async function submitCredentials(email = 'user@example.com', password = 'password123') {
  await userEvent.type(screen.getByLabelText('Email'), email);
  await userEvent.type(screen.getByLabelText('Password'), password);
  await userEvent.click(screen.getByRole('button', { name: 'Log in' }));
}

describe('LoginPage', () => {
  beforeEach(() => {
    vi.mocked(authApi.login).mockReset();
    vi.mocked(authApi.resendVerificationEmail).mockReset();
  });

  it('navigates to the planner after a successful login', async () => {
    vi.mocked(authApi.login).mockResolvedValue(authResponse);
    renderLogin();

    await submitCredentials();

    expect(await screen.findByRole('heading', { name: 'My trips' })).toBeInTheDocument();
  });

  /**
   * F3/US8 — "Add to trip" while logged out sends the user here with a `from` in
   * router state so the pending action can resume. Losing it would silently dump
   * people on the planner instead of the destination they were adding.
   */
  it('returns to the page that sent the user here', async () => {
    vi.mocked(authApi.login).mockResolvedValue(authResponse);
    renderLogin({ pathname: '/login', state: { from: '/destinations/geo-123' } });

    await submitCredentials();

    expect(await screen.findByRole('heading', { name: 'Destination page' })).toBeInTheDocument();
  });

  it("shows the backend's message when credentials are rejected", async () => {
    vi.mocked(authApi.login).mockRejectedValue(
      httpError(401, { detail: 'Invalid email or password.' }),
    );
    renderLogin();

    await submitCredentials();

    expect(await screen.findByText('Invalid email or password.')).toBeInTheDocument();
    // A 401 is not an unverified account, so no resend affordance.
    expect(
      screen.queryByRole('button', { name: 'Resend verification email' }),
    ).not.toBeInTheDocument();
  });

  /**
   * The reason LoginPage needs getErrorStatus at all: 403 specifically means
   * "verified email required" (F4/US2), and it is the only status that offers a
   * resend. Treating it like any other error would leave the user stuck with no
   * way forward.
   */
  it('offers to resend the verification email on a 403', async () => {
    vi.mocked(authApi.login).mockRejectedValue(
      httpError(403, { detail: 'Please verify your email before logging in.' }),
    );
    renderLogin();

    await submitCredentials();

    expect(await screen.findByText('Please verify your email before logging in.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Resend verification email' })).toBeInTheDocument();
  });

  it('confirms once the verification email has been resent', async () => {
    vi.mocked(authApi.login).mockRejectedValue(httpError(403, { detail: 'Please verify your email.' }));
    vi.mocked(authApi.resendVerificationEmail).mockResolvedValue(undefined);
    renderLogin();
    await submitCredentials('unverified@example.com');

    await userEvent.click(screen.getByRole('button', { name: 'Resend verification email' }));

    expect(
      await screen.findByText('Verification email sent — check your inbox.'),
    ).toBeInTheDocument();
    // Resent to the address that was typed, not a stale or empty value.
    expect(authApi.resendVerificationEmail).toHaveBeenCalledWith('unverified@example.com');
    // The button is replaced by the confirmation, so it cannot be clicked twice.
    expect(
      screen.queryByRole('button', { name: 'Resend verification email' }),
    ).not.toBeInTheDocument();
  });

  it('reports a failure to resend without losing the resend affordance', async () => {
    vi.mocked(authApi.login).mockRejectedValue(httpError(403, { detail: 'Please verify your email.' }));
    vi.mocked(authApi.resendVerificationEmail).mockRejectedValue(httpError(500, {}));
    renderLogin();
    await submitCredentials();

    await userEvent.click(screen.getByRole('button', { name: 'Resend verification email' }));

    expect(
      await screen.findByText('Could not resend the email. Please try again.'),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Resend verification email' })).toBeInTheDocument();
  });

  it('clears the unverified state when a later attempt fails differently', async () => {
    vi.mocked(authApi.login).mockRejectedValueOnce(httpError(403, { detail: 'Verify first.' }));
    renderLogin();
    await submitCredentials();
    expect(screen.getByRole('button', { name: 'Resend verification email' })).toBeInTheDocument();

    // Second attempt: wrong password on a now-verified account.
    vi.mocked(authApi.login).mockRejectedValueOnce(httpError(401, { detail: 'Invalid email or password.' }));
    await userEvent.click(screen.getByRole('button', { name: 'Log in' }));

    expect(await screen.findByText('Invalid email or password.')).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: 'Resend verification email' }),
    ).not.toBeInTheDocument();
  });

  it('redirects away when the visitor is already signed in', () => {
    localStorage.setItem('tripplanner.user', JSON.stringify(user));

    renderLogin();

    // The form makes no sense for a signed-in user.
    expect(screen.getByRole('heading', { name: 'My trips' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Log in' })).not.toBeInTheDocument();
  });
});
