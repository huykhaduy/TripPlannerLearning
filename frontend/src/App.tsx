import { Navigate, Route, Routes, Link, NavLink } from 'react-router-dom';
import { useAuth } from './auth/AuthContext';
import { ProtectedRoute } from './auth/ProtectedRoute';
import { LoginPage } from './features/auth/LoginPage';
import { RegisterPage } from './features/auth/RegisterPage';
import { SearchPage } from './features/destinations/SearchPage';
import { TripsPage } from './features/trips/TripsPage';
import { TripDetailPage } from './features/trips/TripDetailPage';

const navLinkClass = ({ isActive }: { isActive: boolean }) =>
  isActive ? 'font-medium text-blue-600' : 'text-slate-600 hover:text-blue-600';

export default function App() {
  const { isAuthenticated } = useAuth();

  return (
    <div>
      <nav className="flex items-center justify-between border-b border-slate-200 bg-white px-6 py-3">
        <Link to="/" className="text-lg font-bold">
          ✈️ TripPlanner
        </Link>
        <div className="flex items-center gap-4 text-sm">
          <NavLink to="/" className={navLinkClass} end>
            Discover
          </NavLink>
          {isAuthenticated ? (
            <NavLink to="/trips" className={navLinkClass}>
              My trips
            </NavLink>
          ) : (
            <NavLink to="/login" className={navLinkClass}>
              Log in
            </NavLink>
          )}
        </div>
      </nav>

      <main className="mx-auto max-w-2xl px-4 py-8">
        <Routes>
          <Route path="/" element={<SearchPage />} />
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
