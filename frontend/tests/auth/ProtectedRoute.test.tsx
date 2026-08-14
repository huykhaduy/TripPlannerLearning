import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { AuthProvider } from '../../src/auth/AuthContext';
import { ProtectedRoute } from '../../src/auth/ProtectedRoute';
import type { User } from '../../src/types';

vi.mock('../../src/api/auth', () => ({
  login: vi.fn(),
  register: vi.fn(),
  verifyEmail: vi.fn(),
  resendVerificationEmail: vi.fn(),
}));

const USER_STORAGE_KEY = 'tripplanner.user';

const user: User = {
  id: '11111111-1111-1111-1111-111111111111',
  email: 'user@example.com',
  displayName: null,
  isEmailVerified: true,
};

/** Mirrors App.tsx's shape: a guarded branch plus the public login page. */
function renderAt(path: string) {
  return render(
    <AuthProvider>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route element={<ProtectedRoute />}>
            <Route path="/trips" element={<h1>My trips</h1>} />
          </Route>
          <Route path="/login" element={<h1>Sign in</h1>} />
        </Routes>
      </MemoryRouter>
    </AuthProvider>,
  );
}

describe('ProtectedRoute', () => {
  it('redirects an anonymous visitor to the login page (F3/US8)', () => {
    renderAt('/trips');

    expect(screen.getByRole('heading', { name: 'Sign in' })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'My trips' })).not.toBeInTheDocument();
  });

  it('renders the guarded route for a signed-in user', () => {
    // AuthProvider reads the stored user during its initial render, so seeding
    // localStorage first is what "already signed in" looks like.
    localStorage.setItem(USER_STORAGE_KEY, JSON.stringify(user));

    renderAt('/trips');

    expect(screen.getByRole('heading', { name: 'My trips' })).toBeInTheDocument();
  });

  it('leaves public routes reachable while anonymous', () => {
    renderAt('/login');

    expect(screen.getByRole('heading', { name: 'Sign in' })).toBeInTheDocument();
  });
});
