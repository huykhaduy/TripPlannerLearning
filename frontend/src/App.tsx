import { useState } from 'react';
import { Navigate, Route, Routes, Link, NavLink } from 'react-router-dom';
import { useAuth } from './auth/AuthContext';
import { ProtectedRoute } from './auth/ProtectedRoute';
import { LoginPage } from './features/auth/LoginPage';
import { RegisterPage } from './features/auth/RegisterPage';
import { VerifyEmailPage } from './features/auth/VerifyEmailPage';
import { SearchPage } from './features/destinations/SearchPage';
import { DestinationDetailsPage } from './features/destinations/DestinationDetailsPage';
import { TripsPage } from './features/trips/TripsPage';
import { TripDetailPage } from './features/trips/TripDetailPage';
import { Avatar } from './components/Avatar';
import { Button } from './components/Button';
import { EmailVerificationBanner } from './components/EmailVerificationBanner';

const navLinkClass = ({ isActive }: { isActive: boolean }) =>
  isActive
    ? 'border-b-2 border-brand-600 px-1 py-1 font-bold text-brand-600'
    : 'px-1 py-1 text-[#434654] hover:text-brand-600';

export default function App() {
  const { user, logout } = useAuth();
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);

  return (
    <div className="min-h-screen bg-[#f9f9ff]">
      <nav className="sticky top-0 z-50 border-b border-[#E2E8F0] bg-white/95 backdrop-blur">
        <div className="relative mx-auto flex h-16 max-w-[1280px] flex-wrap items-center justify-between gap-3 px-4 sm:px-12">
          <div className="flex items-center gap-8">
            <Link to="/" className="font-headline text-xl font-bold text-brand-600">
              ✈️ Trip Planner
            </Link>
            <div className="hidden items-center gap-6 text-sm sm:flex">
              <NavLink to="/" className={navLinkClass} end>
                Explore
              </NavLink>
              {user && (
                <NavLink to="/trips" className={navLinkClass}>
                  My Trips
                </NavLink>
              )}
            </div>
          </div>
          <div className="flex flex-wrap items-center gap-4 text-sm">
            {user ? (
              <div className="flex items-center gap-2 rounded-full border border-[#E2E8F0] py-1 pl-1 pr-3">
                <Avatar label={user.displayName || user.email} />
                <span className="text-[#434654]">{user.displayName || user.email}</span>
                <button type="button" onClick={logout} className="text-[#434654] hover:text-brand-600">
                  Log out
                </button>
              </div>
            ) : (
              <>
                <NavLink to="/login" className={navLinkClass}>
                  Log in
                </NavLink>
                <Link to="/register">
                  <Button type="button" size="sm" variant="action">
                    Sign up
                  </Button>
                </Link>
              </>
            )}
            <button
              type="button"
              aria-label="Toggle navigation menu"
              aria-expanded={mobileMenuOpen}
              onClick={() => setMobileMenuOpen((open) => !open)}
              className="text-lg text-[#434654] hover:text-brand-600 sm:hidden"
            >
              ☰
            </button>
          </div>

          {mobileMenuOpen && (
            <div className="absolute left-0 right-0 top-16 flex flex-col gap-1 border-b border-[#E2E8F0] bg-white/95 p-4 text-sm backdrop-blur sm:hidden">
              <NavLink to="/" className={navLinkClass} end onClick={() => setMobileMenuOpen(false)}>
                Explore
              </NavLink>
              {user && (
                <NavLink to="/trips" className={navLinkClass} onClick={() => setMobileMenuOpen(false)}>
                  My Trips
                </NavLink>
              )}
            </div>
          )}
        </div>
      </nav>

      <EmailVerificationBanner />

      <main className="mx-auto max-w-[1280px] px-4 py-8 sm:px-12">
        <Routes>
          <Route path="/" element={<SearchPage />} />
          <Route path="/destinations/:providerId" element={<DestinationDetailsPage />} />
          <Route path="/login" element={<LoginPage />} />
          <Route path="/register" element={<RegisterPage />} />
          <Route path="/verify-email" element={<VerifyEmailPage />} />

          {/* Authenticated area (Feature 3). */}
          <Route element={<ProtectedRoute />}>
            <Route path="/trips" element={<TripsPage />} />
            <Route path="/trips/:tripId" element={<TripDetailPage />} />
          </Route>

          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </main>
    </div>
  );
}
