import { Navigate, Route, Routes, Link, NavLink } from 'react-router-dom';
import { useAuth } from './auth/AuthContext';
import { ProtectedRoute } from './auth/ProtectedRoute';
import { LoginPage } from './features/auth/LoginPage';
import { RegisterPage } from './features/auth/RegisterPage';
import { SearchPage } from './features/destinations/SearchPage';
import { DestinationDetailsPage } from './features/destinations/DestinationDetailsPage';
import { TripsPage } from './features/trips/TripsPage';
import { TripDetailPage } from './features/trips/TripDetailPage';
import { Avatar } from './components/Avatar';
import { Button } from './components/Button';

const navLinkClass = ({ isActive }: { isActive: boolean }) =>
  isActive ? 'font-medium text-brand-600' : 'text-slate-600 hover:text-brand-600';

export default function App() {
  const { user, logout } = useAuth();

  return (
    <div className="min-h-screen bg-slate-50">
      <nav className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-200 bg-white px-6 py-4">
        <Link to="/" className="text-lg font-bold text-slate-900">
          ✈️ TripPlanner
        </Link>
        <div className="flex flex-wrap items-center gap-4 text-sm">
          <NavLink to="/" className={navLinkClass} end>
            Discover
          </NavLink>
          {user ? (
            <>
              <NavLink to="/trips" className={navLinkClass}>
                My trips
              </NavLink>
              <div className="flex items-center gap-2 rounded-full border border-slate-200 py-1 pl-1 pr-3">
                <Avatar label={user.displayName || user.email} />
                <span className="text-slate-700">{user.displayName || user.email}</span>
                <button type="button" onClick={logout} className="text-slate-500 hover:text-brand-600">
                  Log out
                </button>
              </div>
            </>
          ) : (
            <>
              <NavLink to="/login" className={navLinkClass}>
                Log in
              </NavLink>
              <Link to="/register">
                <Button type="button" size="sm">
                  Sign up
                </Button>
              </Link>
            </>
          )}
        </div>
      </nav>

      <main className="mx-auto max-w-6xl px-4 py-8 sm:px-6">
        <Routes>
          <Route path="/" element={<SearchPage />} />
          <Route path="/destinations/:providerId" element={<DestinationDetailsPage />} />
          <Route path="/login" element={<LoginPage />} />
          <Route path="/register" element={<RegisterPage />} />

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
