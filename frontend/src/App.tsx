import { Navigate, Route, Routes, Link } from 'react-router-dom';
import { useAuth } from './auth/AuthContext';
import { ProtectedRoute } from './auth/ProtectedRoute';
import { LoginPage } from './features/auth/LoginPage';
import { RegisterPage } from './features/auth/RegisterPage';
import { SearchPage } from './features/destinations/SearchPage';
import { TripsPage } from './features/trips/TripsPage';

export default function App() {
  const { isAuthenticated } = useAuth();

  return (
    <div className="app">
      <nav className="navbar">
        <Link to="/" className="brand">
          ✈️ TripPlanner
        </Link>
        <div className="nav-links">
          <Link to="/">Discover</Link>
          {isAuthenticated ? <Link to="/trips">My trips</Link> : <Link to="/login">Log in</Link>}
        </div>
      </nav>

      <main className="container">
        <Routes>
          <Route path="/" element={<SearchPage />} />
          <Route path="/login" element={<LoginPage />} />
          <Route path="/register" element={<RegisterPage />} />

          {/* Authenticated area (Feature 3). */}
          <Route element={<ProtectedRoute />}>
            <Route path="/trips" element={<TripsPage />} />
          </Route>

          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </main>
    </div>
  );
}
