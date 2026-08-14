import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthProvider } from '../../../src/auth/AuthContext';
import { getToken } from '../../../src/api/client';
import { httpError } from '../../http';
import { RegisterPage } from '../../../src/features/auth/RegisterPage';
import type { User } from '../../../src/types';

vi.mock('../../../src/api/auth', () => ({
  login: vi.fn(),
  register: vi.fn(),
  verifyEmail: vi.fn(),
  resendVerificationEmail: vi.fn(),
}));

import * as authApi from '../../../src/api/auth';

const created: User = {
  id: '11111111-1111-1111-1111-111111111111',
  email: 'new@example.com',
  displayName: null,
  isEmailVerified: false,
};

function renderRegister() {
  return render(
    <AuthProvider>
      <MemoryRouter initialEntries={['/register']}>
        <Routes>
          <Route path="/register" element={<RegisterPage />} />
          <Route path="/trips" element={<h1>My trips</h1>} />
          <Route path="/login" element={<h1>Sign in</h1>} />
        </Routes>
      </MemoryRouter>
    </AuthProvider>,
  );
}

async function fillForm({ displayName = '', email = 'new@example.com', password = 'password123' } = {}) {
  if (displayName) await userEvent.type(screen.getByLabelText('Display name (optional)'), displayName);
  await userEvent.type(screen.getByLabelText('Email'), email);
  await userEvent.type(screen.getByLabelText('Password (min 8 characters)'), password);
  await userEvent.click(screen.getByRole('button', { name: 'Sign up' }));
}

describe('RegisterPage', () => {
  beforeEach(() => {
    vi.mocked(authApi.register).mockReset();
  });

  /**
   * F4/US1 + US2 — registering must NOT sign the user in; the account is unusable
   * until the emailed link is opened. A session here would bypass that gate.
   */
  it('shows the check-your-email screen without starting a session', async () => {
    vi.mocked(authApi.register).mockResolvedValue(created);
    renderRegister();

    await fillForm();

    expect(await screen.findByRole('heading', { name: 'Check your email' })).toBeInTheDocument();
    expect(screen.getByText('new@example.com')).toBeInTheDocument();
    expect(getToken()).toBeNull();
    expect(localStorage.getItem('tripplanner.user')).toBeNull();
  });

  it('passes an entered display name through', async () => {
    vi.mocked(authApi.register).mockResolvedValue(created);
    renderRegister();

    await fillForm({ displayName: 'Duy' });

    expect(authApi.register).toHaveBeenCalledWith('new@example.com', 'password123', 'Duy');
  });

  it('sends undefined rather than an empty display name', async () => {
    vi.mocked(authApi.register).mockResolvedValue(created);
    renderRegister();

    await fillForm();

    // The field is optional; '' would be a value the backend then has to interpret.
    expect(authApi.register).toHaveBeenCalledWith('new@example.com', 'password123', undefined);
  });

  it("surfaces the backend's message and keeps the form", async () => {
    vi.mocked(authApi.register).mockRejectedValue(
      httpError(409, { detail: 'Unable to register with the provided details.' }),
    );
    renderRegister();

    await fillForm();

    expect(
      await screen.findByText('Unable to register with the provided details.'),
    ).toBeInTheDocument();
    // Still on the form, so the user can change the email rather than retyping everything.
    expect(screen.getByRole('button', { name: 'Sign up' })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Check your email' })).not.toBeInTheDocument();
  });

  it('redirects to the planner when already signed in', () => {
    localStorage.setItem('tripplanner.user', JSON.stringify({ ...created, isEmailVerified: true }));

    renderRegister();

    expect(screen.getByRole('heading', { name: 'My trips' })).toBeInTheDocument();
  });
});
