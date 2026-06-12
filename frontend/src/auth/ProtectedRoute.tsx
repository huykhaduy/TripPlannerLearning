import { Navigate, Outlet } from 'react-router-dom';
import { useAuth } from './AuthContext';

/**
 * Wraps routes that require a signed-in user. Anonymous visitors are redirected
 * to the login page (Feature 3 / US8 — require login to save trips).
 */
export function ProtectedRoute() {
  const { isAuthenticated } = useAuth();
  return isAuthenticated ? <Outlet /> : <Navigate to="/login" replace />;
}
