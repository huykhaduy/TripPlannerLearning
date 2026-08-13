import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import App from '../src/App';
import { AuthProvider } from '../src/auth/AuthContext';
import type { User } from '../src/types';

// The route table is what is under test, so every page is stubbed down to a
// marker — this file should fail when a path or a guard changes, not when a page's
// own rendering does. ProtectedRoute and the nav stay real for the same reason.
vi.mock('../src/features/destinations/SearchPage', () => ({
  SearchPage: () => <div>search page</div>,
}));
vi.mock('../src/features/destinations/DestinationDetailsPage', () => ({
  DestinationDetailsPage: () => <div>destination details page</div>,
}));
vi.mock('../src/features/auth/LoginPage', () => ({ LoginPage: () => <div>login page</div> }));
vi.mock('../src/features/auth/RegisterPage', () => ({ RegisterPage: () => <div>register page</div> }));
vi.mock('../src/features/auth/VerifyEmailPage', () => ({
  VerifyEmailPage: () => <div>verify email page</div>,
}));
vi.mock('../src/features/trips/TripsPage', () => ({ TripsPage: () => <div>trips page</div> }));
vi.mock('../src/features/trips/TripDetailPage', () => ({
  TripDetailPage: () => <div>trip detail page</div>,
}));

const USER: User = {
  id: 'u1',
  email: 'ada@example.com',
  displayName: 'Ada',
  isEmailVerified: true,
};

/** Seeding localStorage before render is how "already signed in" is expressed — it is what AuthProvider reads during its initial render. */
function signIn(user: User = USER) {
  localStorage.setItem('tripplanner.user', JSON.stringify(user));
  localStorage.setItem('tripplanner.token', 'jwt.token.here');
}

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <AuthProvider>
        <App />
      </AuthProvider>
    </MemoryRouter>,
  );
}

describe('App routes', () => {
  it.each([
    ['/', 'search page'],
    ['/destinations/abc123', 'destination details page'],
    ['/login', 'login page'],
    ['/register', 'register page'],
    ['/verify-email', 'verify email page'],
  ])('renders %s anonymously', (path, expected) => {
    renderAt(path);

    expect(screen.getByText(expected)).toBeInTheDocument();
  });

  it('sends an unknown path back to the explore page', () => {
    renderAt('/does-not-exist');

    expect(screen.getByText('search page')).toBeInTheDocument();
  });

  describe('the authenticated area', () => {
    it('redirects an anonymous visitor from /trips to login', () => {
      renderAt('/trips');

      expect(screen.getByText('login page')).toBeInTheDocument();
      expect(screen.queryByText('trips page')).not.toBeInTheDocument();
    });

    it('redirects an anonymous visitor from a trip detail url too', () => {
      // Both nested routes sit under the same ProtectedRoute; pinning the child
      // route as well means adding a third one outside the guard gets noticed.
      renderAt('/trips/t1');

      expect(screen.getByText('login page')).toBeInTheDocument();
      expect(screen.queryByText('trip detail page')).not.toBeInTheDocument();
    });

    it('renders /trips for a signed-in user', () => {
      signIn();

      renderAt('/trips');

      expect(screen.getByText('trips page')).toBeInTheDocument();
    });

    it('renders a single trip for a signed-in user', () => {
      signIn();

      renderAt('/trips/t1');

      expect(screen.getByText('trip detail page')).toBeInTheDocument();
    });
  });
});

describe('App navigation', () => {
  it('offers log in and sign up to an anonymous visitor', () => {
    renderAt('/');

    expect(screen.getByRole('link', { name: 'Log in' })).toHaveAttribute('href', '/login');
    expect(screen.getByRole('button', { name: 'Sign up' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'My Trips' })).not.toBeInTheDocument();
  });

  it('shows the account pill and My Trips once signed in', () => {
    signIn();

    renderAt('/');

    // Paired positive/negative: asserting only that "Log in" is gone would pass
    // just as happily if the whole nav failed to render.
    expect(screen.getByRole('link', { name: 'My Trips' })).toHaveAttribute('href', '/trips');
    expect(screen.getByText('Ada')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Log in' })).not.toBeInTheDocument();
  });

  it('falls back to the email when the account has no display name', () => {
    signIn({ ...USER, displayName: null });

    renderAt('/');

    expect(screen.getByText('ada@example.com')).toBeInTheDocument();
  });

  it('logs the user out and drops the protected nav', async () => {
    signIn();
    renderAt('/');

    await userEvent.click(screen.getByRole('button', { name: 'Log out' }));

    expect(screen.getByRole('link', { name: 'Log in' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'My Trips' })).not.toBeInTheDocument();
    expect(localStorage.getItem('tripplanner.token')).toBeNull();
  });

  it('toggles the mobile menu open and shut', async () => {
    renderAt('/');
    const toggle = screen.getByRole('button', { name: 'Toggle navigation menu' });
    expect(toggle).toHaveAttribute('aria-expanded', 'false');

    await userEvent.click(toggle);
    expect(toggle).toHaveAttribute('aria-expanded', 'true');
    // The desktop nav is always mounted, so the mobile panel adds a second copy.
    expect(screen.getAllByRole('link', { name: 'Explore' })).toHaveLength(2);

    await userEvent.click(toggle);
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    expect(screen.getAllByRole('link', { name: 'Explore' })).toHaveLength(1);
  });

  it('closes the mobile menu after following one of its links', async () => {
    signIn();
    renderAt('/');
    await userEvent.click(screen.getByRole('button', { name: 'Toggle navigation menu' }));

    const mobileTripsLink = screen.getAllByRole('link', { name: 'My Trips' })[1];
    await userEvent.click(mobileTripsLink);

    expect(screen.getByText('trips page')).toBeInTheDocument();
    expect(screen.getAllByRole('link', { name: 'My Trips' })).toHaveLength(1);
  });
});
