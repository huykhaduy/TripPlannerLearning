import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { httpError, networkError } from '../../test/http';
import { VerifyEmailPage } from './VerifyEmailPage';

vi.mock('../../api/auth', () => ({
  login: vi.fn(),
  register: vi.fn(),
  verifyEmail: vi.fn(),
  resendVerificationEmail: vi.fn(),
}));

import * as authApi from '../../api/auth';

/** The page reads its token from the query string, exactly as the emailed link supplies it. */
function renderVerify(search: string) {
  return render(
    <MemoryRouter initialEntries={[`/verify-email${search}`]}>
      <Routes>
        <Route path="/verify-email" element={<VerifyEmailPage />} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('VerifyEmailPage', () => {
  beforeEach(() => {
    vi.mocked(authApi.verifyEmail).mockReset();
  });

  it('verifies the token from the URL and confirms success', async () => {
    vi.mocked(authApi.verifyEmail).mockResolvedValue(undefined);

    renderVerify('?token=abc123');

    expect(await screen.findByRole('heading', { name: 'Email verified' })).toBeInTheDocument();
    expect(authApi.verifyEmail).toHaveBeenCalledWith('abc123');
    expect(screen.getByRole('link', { name: 'You can now log in' })).toBeInTheDocument();
  });

  it('shows a verifying state until the request settles', () => {
    vi.mocked(authApi.verifyEmail).mockReturnValue(new Promise(() => {}));

    renderVerify('?token=abc123');

    expect(screen.getByText('Verifying your email…')).toBeInTheDocument();
  });

  it("reports the backend's reason when the token is rejected", async () => {
    vi.mocked(authApi.verifyEmail).mockRejectedValue(
      httpError(400, { detail: 'This verification link is invalid or has expired.' }),
    );

    renderVerify('?token=expired');

    expect(await screen.findByRole('heading', { name: 'Verification failed' })).toBeInTheDocument();
    expect(
      screen.getByText('This verification link is invalid or has expired.'),
    ).toBeInTheDocument();
  });

  it('falls back to a generic reason when the request never reached the server', async () => {
    vi.mocked(authApi.verifyEmail).mockRejectedValue(networkError());

    renderVerify('?token=abc123');

    expect(
      await screen.findByText('This verification link is invalid or has expired.'),
    ).toBeInTheDocument();
  });

  /**
   * A truncated link (mail clients do wrap and cut URLs) must fail locally rather
   * than sending an empty token to the API.
   */
  it('fails without calling the API when the link has no token', async () => {
    renderVerify('');

    expect(await screen.findByRole('heading', { name: 'Verification failed' })).toBeInTheDocument();
    expect(screen.getByText('This verification link is missing its token.')).toBeInTheDocument();
    expect(authApi.verifyEmail).not.toHaveBeenCalled();
  });
});
